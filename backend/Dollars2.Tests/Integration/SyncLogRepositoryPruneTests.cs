using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises <see cref="SyncLogRepository.PruneAsync"/> against a migrated MSSQL container.
/// The interesting property is not that old rows go away, but that the two rows the sync engine
/// reads back — the newest row per account and the newest *successful* row per account — survive
/// the prune no matter how old they are. Each test rolls back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SyncLogRepositoryPruneTests
{
    private const int RetentionDays = 90;

    private readonly MsSqlContainerFixture _fixture;

    public SyncLogRepositoryPruneTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PruneAsync_deletes_rows_past_the_cutoff_and_leaves_recent_rows_alone()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var accountId = await SeedAccountAsync(db, "synclog-prune-cutoff@example.com");
            var repository = new SyncLogRepository(db);

            var ancient = await InsertSyncLogAsync(db, accountId, daysAgo: 200, status: "Success");
            var old = await InsertSyncLogAsync(db, accountId, daysAgo: 100, status: "Failure");
            var recentSuccess = await InsertSyncLogAsync(db, accountId, daysAgo: 10, status: "Success");
            var newest = await InsertSyncLogAsync(db, accountId, daysAgo: 1, status: "Failure");

            var deleted = await repository.PruneAsync(RetentionDays);

            Assert.Equal(2, deleted);
            Assert.False(await RowExistsAsync(db, ancient));
            Assert.False(await RowExistsAsync(db, old));
            Assert.True(await RowExistsAsync(db, recentSuccess));
            Assert.True(await RowExistsAsync(db, newest));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task PruneAsync_keeps_the_newest_row_and_newest_successful_row_when_all_are_past_the_cutoff()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var accountId = await SeedAccountAsync(db, "synclog-prune-keepers@example.com");
            var repository = new SyncLogRepository(db);

            var oldestSuccess = await InsertSyncLogAsync(db, accountId, daysAgo: 300, status: "Success");
            var staleFailure = await InsertSyncLogAsync(db, accountId, daysAgo: 250, status: "Failure");
            var lastSuccess = await InsertSyncLogAsync(db, accountId, daysAgo: 200, status: "Success");
            var newestRow = await InsertSyncLogAsync(db, accountId, daysAgo: 150, status: "Failure");

            // What the sync engine reads today, before any pruning.
            var latestBefore = (await repository.GetLatestPerAccountAsync(new[] { accountId })).Single();
            var lastSuccessBefore = await repository.GetLastSuccessfulAsync(accountId);

            var deleted = await repository.PruneAsync(RetentionDays);

            Assert.Equal(2, deleted);
            Assert.False(await RowExistsAsync(db, oldestSuccess));
            Assert.False(await RowExistsAsync(db, staleFailure));
            Assert.True(await RowExistsAsync(db, lastSuccess));
            Assert.True(await RowExistsAsync(db, newestRow));

            // The keeper guarantee: both reads return exactly what they returned before the prune.
            // If lastSuccess were pruned, this account would silently revert to a 180-day refetch.
            var latestAfter = (await repository.GetLatestPerAccountAsync(new[] { accountId })).Single();
            var lastSuccessAfter = await repository.GetLastSuccessfulAsync(accountId);

            Assert.Equal(latestBefore.Id, latestAfter.Id);
            Assert.Equal(newestRow, latestAfter.Id);
            Assert.NotNull(lastSuccessAfter);
            Assert.Equal(lastSuccessBefore!.Id, lastSuccessAfter!.Id);
            Assert.Equal(lastSuccess, lastSuccessAfter.Id);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task PruneAsync_keeps_each_accounts_own_keepers_independently()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var quietAccountId = await SeedAccountAsync(db, "synclog-prune-quiet@example.com");
            var busyAccountId = await SeedAccountAsync(db, "synclog-prune-busy@example.com");
            var repository = new SyncLogRepository(db);

            // A long-dormant account: every row is ancient, and its single Success row is both keepers.
            var quietOnlyRow = await InsertSyncLogAsync(db, quietAccountId, daysAgo: 400, status: "Success");

            // An active account whose recent history is entirely failures.
            var busyOldSuccess = await InsertSyncLogAsync(db, busyAccountId, daysAgo: 120, status: "Success");
            var busyStale = await InsertSyncLogAsync(db, busyAccountId, daysAgo: 110, status: "Failure");
            var busyRecent = await InsertSyncLogAsync(db, busyAccountId, daysAgo: 2, status: "Failure");

            var deleted = await repository.PruneAsync(RetentionDays);

            Assert.Equal(1, deleted);
            Assert.True(await RowExistsAsync(db, quietOnlyRow));
            // Older than the cutoff, but it is the account's watermark, so it stays.
            Assert.True(await RowExistsAsync(db, busyOldSuccess));
            Assert.False(await RowExistsAsync(db, busyStale));
            Assert.True(await RowExistsAsync(db, busyRecent));

            var quietLastSuccess = await repository.GetLastSuccessfulAsync(quietAccountId);
            Assert.Equal(quietOnlyRow, quietLastSuccess!.Id);

            var busyLastSuccess = await repository.GetLastSuccessfulAsync(busyAccountId);
            Assert.Equal(busyOldSuccess, busyLastSuccess!.Id);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task PruneAsync_is_a_no_op_when_every_row_is_inside_the_retention_window()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var accountId = await SeedAccountAsync(db, "synclog-prune-noop@example.com");
            var repository = new SyncLogRepository(db);

            await InsertSyncLogAsync(db, accountId, daysAgo: 5, status: "Success");
            await InsertSyncLogAsync(db, accountId, daysAgo: 1, status: "Success");

            var deleted = await repository.PruneAsync(RetentionDays);

            Assert.Equal(0, deleted);

            var remaining = await db.Connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM SyncLog WHERE AccountId = @accountId",
                new { accountId },
                db.CurrentTransaction);
            Assert.Equal(2, remaining);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static async Task<int> SeedAccountAsync(DbSession db, string email)
    {
        var userId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);

        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, 'Checking', 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId },
            db.CurrentTransaction);
    }

    private static async Task<int> InsertSyncLogAsync(DbSession db, int accountId, int daysAgo, string status)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO SyncLog (AccountId, SyncedAt, Status, TransactionCount, ErrorMessage)
              VALUES (@accountId, DATEADD(day, -@daysAgo, SYSUTCDATETIME()), @status, 0, NULL);
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { accountId, daysAgo, status },
            db.CurrentTransaction);
    }

    private static async Task<bool> RowExistsAsync(DbSession db, int id)
    {
        var count = await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM SyncLog WHERE Id = @id",
            new { id },
            db.CurrentTransaction);

        return count > 0;
    }
}
