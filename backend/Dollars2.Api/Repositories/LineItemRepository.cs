using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

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
            "SELECT Id, GroupId, Name, PlannedAmount, SortOrder, Notes, PreviousLineItemId, CreatedAt, UpdatedAt FROM LineItems WHERE GroupId = @groupId ORDER BY SortOrder",
            new { groupId },
            _db.CurrentTransaction);
    }

    public async Task<LineItem?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<LineItem>(
            "SELECT Id, GroupId, Name, PlannedAmount, SortOrder, Notes, PreviousLineItemId, CreatedAt, UpdatedAt FROM LineItems WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int groupId, string name, decimal plannedAmount, int sortOrder, int? previousLineItemId = null)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, PreviousLineItemId, CreatedAt, UpdatedAt) VALUES (@groupId, @name, @plannedAmount, @sortOrder, @previousLineItemId, SYSUTCDATETIME(), SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, name, plannedAmount, sortOrder, previousLineItemId },
            _db.CurrentTransaction);
    }

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

    public async Task ClearPreviousLinkAsync(int previousLineItemId)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET PreviousLineItemId = NULL WHERE PreviousLineItemId = @previousLineItemId",
            new { previousLineItemId },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, string name, decimal plannedAmount)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET Name = @name, PlannedAmount = @plannedAmount, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, name, plannedAmount },
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
