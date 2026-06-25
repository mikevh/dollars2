using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

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
            "SELECT Id, TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt FROM TransactionAssignments WHERE TransactionId = @TransactionId",
            new { TransactionId = transactionId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<TransactionAssignment>> GetByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QueryAsync<TransactionAssignment>(
            "SELECT Id, TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt FROM TransactionAssignments WHERE LineItemId = @LineItemId",
            new { LineItemId = lineItemId },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int transactionId, int lineItemId, decimal amount)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO TransactionAssignments (TransactionId, LineItemId, Amount, CreatedAt, UpdatedAt)
              VALUES (@TransactionId, @LineItemId, @Amount, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { TransactionId = transactionId, LineItemId = lineItemId, Amount = amount },
            _db.CurrentTransaction);
    }

    public async Task DeleteByLineItemIdAsync(int lineItemId)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM TransactionAssignments WHERE LineItemId = @LineItemId",
            new { LineItemId = lineItemId },
            _db.CurrentTransaction);
    }

    public async Task DeleteByTransactionIdAsync(int transactionId)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM TransactionAssignments WHERE TransactionId = @TransactionId",
            new { TransactionId = transactionId },
            _db.CurrentTransaction);
    }

    public async Task<decimal> GetSpentByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QuerySingleAsync<decimal>(
            @"SELECT COALESCE(SUM(ta.Amount), 0) FROM TransactionAssignments ta
              INNER JOIN Transactions t ON t.Id = ta.TransactionId
              WHERE ta.LineItemId = @LineItemId AND t.IsDeleted = 0 AND ta.Amount < 0",
            new { LineItemId = lineItemId },
            _db.CurrentTransaction);
    }

    public async Task<decimal> GetReceivedByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QuerySingleAsync<decimal>(
            @"SELECT COALESCE(SUM(ta.Amount), 0) FROM TransactionAssignments ta
              INNER JOIN Transactions t ON t.Id = ta.TransactionId
              WHERE ta.LineItemId = @LineItemId AND t.IsDeleted = 0 AND ta.Amount > 0",
            new { LineItemId = lineItemId },
            _db.CurrentTransaction);
    }
}
