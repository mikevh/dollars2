using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public sealed record LineItemRolloverAmount(int LineItemId, decimal Amount);

public class LineItemRepository
{
    private readonly DbSession _db;

    public LineItemRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<IEnumerable<LineItem>> GetByGroupIdAsync(int groupId)
    {
        return await _db.Connection.QueryAsync<LineItem>(
            "SELECT Id, GroupId, Name, PlannedAmount, IsIncome, SortOrder, Notes, PreviousLineItemId, CreatedAt, UpdatedAt FROM LineItems WHERE GroupId = @groupId ORDER BY SortOrder",
            new { groupId },
            _db.CurrentTransaction);
    }

    /// <summary>All line items across every group in a budget, in one query. Ordered by group then
    /// sort order so callers can bucket by GroupId and keep each group's items in display order.</summary>
    public async Task<IEnumerable<LineItem>> GetByBudgetIdAsync(int budgetId)
    {
        return await _db.Connection.QueryAsync<LineItem>(
            @"SELECT li.Id, li.GroupId, li.Name, li.PlannedAmount, li.IsIncome, li.SortOrder, li.Notes, li.PreviousLineItemId, li.CreatedAt, li.UpdatedAt
              FROM LineItems li
              INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId
              WHERE bg.BudgetId = @budgetId
              ORDER BY li.GroupId, li.SortOrder",
            new { budgetId },
            _db.CurrentTransaction);
    }

    public async Task<LineItem?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<LineItem>(
            "SELECT Id, GroupId, Name, PlannedAmount, IsIncome, SortOrder, Notes, PreviousLineItemId, CreatedAt, UpdatedAt FROM LineItems WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int groupId, string name, decimal plannedAmount, int sortOrder, bool isIncome = false, int? previousLineItemId = null, string notes = "")
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO LineItems (GroupId, Name, PlannedAmount, IsIncome, SortOrder, Notes, PreviousLineItemId, CreatedAt, UpdatedAt) VALUES (@groupId, @name, @plannedAmount, @isIncome, @sortOrder, @notes, @previousLineItemId, SYSUTCDATETIME(), SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, name, plannedAmount, isIncome, sortOrder, notes, previousLineItemId },
            _db.CurrentTransaction);
    }

    // UPDLOCK+HOLDLOCK, held until the caller's transaction commits or rolls back, so a second
    // concurrent call for the same budget blocks here rather than reading a stale pre-delete count
    // — the caller (BudgetService.DeleteLineItemAsync) relies on that to keep the last-income-item
    // check atomic with the delete.
    public async Task<int> CountIncomeInBudgetAsync(int budgetId)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM LineItems li WITH (UPDLOCK, HOLDLOCK)
              INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId
              WHERE bg.BudgetId = @budgetId AND li.IsIncome = 1",
            new { budgetId },
            _db.CurrentTransaction);
    }

    /// <summary>Single-item rollover, used by the per-item call sites (CreateLineItemAsync,
    /// UpdateLineItemAsync, UpdateGroupAsync). <see cref="GetRolloverBatchAsync"/> is the same chain
    /// walk restated to seed from many ids at once — keep the two in sync if this query's semantics
    /// change (chain-termination condition, which assignments count, etc).</summary>
    public async Task<decimal> GetRolloverAsync(int lineItemId)
    {
        return await _db.Connection.QuerySingleAsync<decimal>(
            @"WITH Chain AS (
                SELECT PreviousLineItemId AS Id FROM LineItems WHERE Id = @lineItemId
                UNION ALL
                SELECT li.PreviousLineItemId FROM LineItems li
                INNER JOIN Chain c ON li.Id = c.Id WHERE c.Id IS NOT NULL
            )
            SELECT COALESCE(SUM(li.PlannedAmount + COALESCE(asn.Total, 0)), 0)
            FROM Chain c
            INNER JOIN LineItems li ON li.Id = c.Id
            LEFT JOIN (
                SELECT ta.LineItemId, SUM(ta.Amount) AS Total
                FROM TransactionAssignments ta
                INNER JOIN Transactions t ON t.Id = ta.TransactionId
                WHERE t.IsDeleted = 0 GROUP BY ta.LineItemId
            ) asn ON asn.LineItemId = li.Id",
            new { lineItemId },
            _db.CurrentTransaction);
    }

    /// <summary>Rollover for every id in <paramref name="lineItemIds"/> in one round trip: each
    /// item's PreviousLineItemId chain is walked and summed independently, grouped back by the
    /// requesting id. An id whose chain is empty (no PreviousLineItemId) has no row in the result
    /// — callers should treat a missing id as 0, same as <see cref="GetRolloverAsync"/>'s COALESCE.
    /// Callers must not pass an empty collection (produces an invalid "IN ()"). Same chain-walk
    /// semantics as <see cref="GetRolloverAsync"/> restated to seed from many ids — keep both in
    /// sync if the rollover rule changes.</summary>
    public async Task<IEnumerable<LineItemRolloverAmount>> GetRolloverBatchAsync(IEnumerable<int> lineItemIds)
    {
        return await _db.Connection.QueryAsync<LineItemRolloverAmount>(
            @"WITH Chain AS (
                SELECT li.Id AS RootId, li.PreviousLineItemId AS AncestorId FROM LineItems li WHERE li.Id IN @lineItemIds
                UNION ALL
                SELECT c.RootId, li.PreviousLineItemId FROM LineItems li
                INNER JOIN Chain c ON li.Id = c.AncestorId WHERE c.AncestorId IS NOT NULL
            )
            SELECT c.RootId AS LineItemId, COALESCE(SUM(li.PlannedAmount + COALESCE(asn.Total, 0)), 0) AS Amount
            FROM Chain c
            INNER JOIN LineItems li ON li.Id = c.AncestorId
            LEFT JOIN (
                SELECT ta.LineItemId, SUM(ta.Amount) AS Total
                FROM TransactionAssignments ta
                INNER JOIN Transactions t ON t.Id = ta.TransactionId
                WHERE t.IsDeleted = 0 GROUP BY ta.LineItemId
            ) asn ON asn.LineItemId = li.Id
            GROUP BY c.RootId",
            new { lineItemIds },
            _db.CurrentTransaction);
    }

    public async Task ClearPreviousLinkAsync(int previousLineItemId)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET PreviousLineItemId = NULL WHERE PreviousLineItemId = @previousLineItemId",
            new { previousLineItemId },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, string name, decimal plannedAmount, string? notes)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET Name = @name, PlannedAmount = @plannedAmount, Notes = COALESCE(@notes, ''), UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, name, plannedAmount, notes },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM LineItems WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task UpdateSortOrderAsync(int id, int sortOrder)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET SortOrder = @sortOrder, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, sortOrder },
            _db.CurrentTransaction);
    }

    public async Task<int> GetMaxSortOrderAsync(int groupId)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            "SELECT ISNULL(MAX(SortOrder), -1) FROM LineItems WHERE GroupId = @groupId",
            new { groupId },
            _db.CurrentTransaction);
    }

    public async Task<bool> IsOwnedByUserAsync(int lineItemId, int userId)
    {
        return await _db.Connection.QuerySingleAsync<bool>(
            @"SELECT CASE WHEN EXISTS (
                SELECT 1 FROM LineItems li
                INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId
                INNER JOIN Budgets b ON b.Id = bg.BudgetId
                WHERE li.Id = @lineItemId AND b.UserId = @userId
              ) THEN 1 ELSE 0 END",
            new { lineItemId, userId },
            _db.CurrentTransaction);
    }
}
