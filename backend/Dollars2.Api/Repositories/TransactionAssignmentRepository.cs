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
            "SELECT * FROM TransactionAssignments WHERE TransactionId = @TransactionId",
            new { TransactionId = transactionId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<TransactionAssignment>> GetByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QueryAsync<TransactionAssignment>(
            "SELECT * FROM TransactionAssignments WHERE LineItemId = @LineItemId",
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
}
