using Dapper;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises the real <see cref="AccountBalanceRepository"/> raw SQL and the
/// AccountBalances migration against a migrated MSSQL container. Each test runs
/// inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountBalanceRepositoryTests
{
    private readonly MsSqlContainerFixture _fixture;

    public AccountBalanceRepositoryTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAsync_then_GetLatestForAccountAsync_round_trips_a_balance()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var accountId = await SeedAccountAsync(db, "balance-roundtrip@example.com");
            var repository = new AccountBalanceRepository(db);

            await repository.CreateAsync(accountId, 1234.56m);

            var latest = await repository.GetLatestForAccountAsync(accountId);

            Assert.NotNull(latest);
            Assert.Equal(accountId, latest!.AccountId);
            Assert.Equal(1234.56m, latest.Balance);
            Assert.Equal(latest.CreatedOn, latest.UpdatedOn);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task CreateAsync_appends_a_new_row_each_time_and_returns_the_newest()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var accountId = await SeedAccountAsync(db, "balance-append@example.com");
            var repository = new AccountBalanceRepository(db);

            await repository.CreateAsync(accountId, 1000m);
            await repository.CreateAsync(accountId, 1200m);

            var count = await db.Connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM AccountBalances WHERE AccountId = @accountId",
                new { accountId },
                db.CurrentTransaction);
            Assert.Equal(2, count);

            var latest = await repository.GetLatestForAccountAsync(accountId);
            Assert.Equal(1200m, latest!.Balance);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static async Task<int> SeedAccountAsync(Dollars2.Api.Data.DbSession db, string email)
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
}
