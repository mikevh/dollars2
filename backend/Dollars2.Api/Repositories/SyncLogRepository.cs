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

    public async Task<SyncLog?> GetLastSuccessfulForUserProviderAsync(int userId, string sourceType)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<SyncLog>(
            @"SELECT TOP 1 sl.Id, sl.AccountId, sl.SyncedAt, sl.Status, sl.TransactionCount, sl.ErrorMessage
              FROM SyncLog sl
              INNER JOIN Accounts a ON a.Id = sl.AccountId
              WHERE a.UserId = @userId AND a.SourceType = @sourceType AND sl.Status = 'Success'
              ORDER BY sl.SyncedAt DESC",
            new { userId, sourceType },
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

    /// <summary>
    /// Deletes sync history older than the retention window, keeping two rows per account no matter
    /// how old they are: the newest row (what <see cref="GetLatestPerAccountAsync"/> reports) and the
    /// newest successful row (what <see cref="GetLastSuccessfulAsync"/> uses as the incremental sync
    /// watermark — dropping it would silently reset the account to a full 180-day refetch).
    /// The cutoff is computed by the database so it can't drift from the SYSUTCDATETIME() stamps
    /// written by <see cref="CreateAsync"/>. Returns the number of rows removed.
    /// </summary>
    public async Task<int> PruneAsync(int retentionDays)
    {
        return await _db.Connection.ExecuteAsync(
            @"WITH Keepers AS (
                SELECT Id
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY SyncedAt DESC, Id DESC) AS rn
                    FROM SyncLog
                ) latest
                WHERE rn = 1
                UNION
                SELECT Id
                FROM (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY SyncedAt DESC, Id DESC) AS rn
                    FROM SyncLog
                    WHERE Status = 'Success'
                ) latestSuccess
                WHERE rn = 1
              )
              DELETE FROM SyncLog
              WHERE SyncedAt < DATEADD(day, -@retentionDays, SYSUTCDATETIME())
                AND Id NOT IN (SELECT Id FROM Keepers)",
            new { retentionDays },
            _db.CurrentTransaction);
    }
}
