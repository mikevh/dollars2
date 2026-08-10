using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public sealed record LineItemNetAmount(int LineItemId, decimal Amount);

public class TransactionAssignmentRepository
{
    private readonly DbSession _db;

    public TransactionAssignmentRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<IEnumerable<TransactionAssignment>> GetByTransactionIdAsync(int transactionId)
    {
        return await _db.Connection.QueryAsync<TransactionAssignment>(
            "SELECT Id, TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt FROM TransactionAssignments WHERE TransactionId = @transactionId",
            new { transactionId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<TransactionAssignment>> GetByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QueryAsync<TransactionAssignment>(
            "SELECT Id, TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt FROM TransactionAssignments WHERE LineItemId = @lineItemId",
            new { lineItemId },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int transactionId, int lineItemId, decimal amount)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO TransactionAssignments (TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt)
              VALUES (@transactionId, @lineItemId, @amount, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { transactionId, lineItemId, amount },
            _db.CurrentTransaction);
    }

    public async Task DeleteByLineItemIdAsync(int lineItemId)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM TransactionAssignments WHERE LineItemId = @lineItemId",
            new { lineItemId },
            _db.CurrentTransaction);
    }

    public async Task DeleteByTransactionIdAsync(int transactionId)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM TransactionAssignments WHERE TransactionId = @transactionId",
            new { transactionId },
            _db.CurrentTransaction);
    }

    // Net sum of every assignment for the line item, all signs included. Debits are negative and
    // credits positive, so a spend-heavy item nets negative and a credit-heavy one nets positive.
    // No sign filter: a positive assignment on an expense item (a refund, or money someone sent you
    // earmarked for that item) is real spend activity and must count.
    public async Task<decimal> GetNetAssignedByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QuerySingleAsync<decimal>(
            @"SELECT COALESCE(SUM(ta.Amount), 0) FROM TransactionAssignments ta
              INNER JOIN Transactions t ON t.Id = ta.TransactionId
              WHERE ta.LineItemId = @lineItemId AND t.IsDeleted = 0",
            new { lineItemId },
            _db.CurrentTransaction);
    }

    /// <summary>Net assigned total for every id in <paramref name="lineItemIds"/> in one round trip.
    /// An id with no assignments has no row in the result — callers should treat a missing id as 0,
    /// same as <see cref="GetNetAssignedByLineItemIdAsync"/>'s COALESCE. Callers must not pass an
    /// empty collection (produces an invalid "IN ()").</summary>
    public async Task<IEnumerable<LineItemNetAmount>> GetNetAssignedByLineItemIdsAsync(IEnumerable<int> lineItemIds)
    {
        return await _db.Connection.QueryAsync<LineItemNetAmount>(
            @"SELECT ta.LineItemId AS LineItemId, SUM(ta.Amount) AS Amount FROM TransactionAssignments ta
              INNER JOIN Transactions t ON t.Id = ta.TransactionId
              WHERE ta.LineItemId IN @lineItemIds AND t.IsDeleted = 0
              GROUP BY ta.LineItemId",
            new { lineItemIds },
            _db.CurrentTransaction);
    }
}
