using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class SyncLogRepository
{
    private readonly DbSession _db;

    public SyncLogRepository(DbSession db)
    {
        _db = db;
    }

    public async Task CreateAsync(int accountId, string status, int transactionCount, string? errorMessage)
    {
        await _db.Connection.ExecuteAsync(
            @"INSERT INTO SyncLog (AccountId, SyncedAt, Status, TransactionCount, ErrorMessage)
              VALUES (@accountId, SYSUTCDATETIME(), @status, @transactionCount, @errorMessage)",
            new { accountId, status, transactionCount, errorMessage },
            _db.CurrentTransaction);
    }

    public async Task<SyncLog?> GetLastSuccessfulAsync(int accountId)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<SyncLog>(
            @"SELECT TOP 1 Id, AccountId, SyncedAt, Status, TransactionCount, ErrorMessage
              FROM SyncLog
              WHERE AccountId = @accountId AND Status = 'Success'
              ORDER BY SyncedAt DESC",
            new { accountId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<SyncLog>> GetLatestPerAccountAsync(IEnumerable<int> accountIds)
    {
        return await _db.Connection.QueryAsync<SyncLog>(
            @"WITH Ranked AS (
                SELECT Id, AccountId, SyncedAt, Status, TransactionCount, ErrorMessage,
                       ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY SyncedAt DESC, Id DESC) AS rn
                FROM SyncLog
                WHERE AccountId IN @accountIds
              )
              SELECT Id, AccountId, SyncedAt, Status, TransactionCount, ErrorMessage
              FROM Ranked
              WHERE rn = 1",
            new { accountIds },
            _db.CurrentTransaction);
    }
}
