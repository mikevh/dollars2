using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises the per-account transactions feature end-to-end against a migrated MSSQL container:
/// the repository query (<see cref="TransactionRepository.GetByAccountIdAsync"/>) and the service
/// ownership guard (<see cref="TransactionService.GetByAccountAsync"/>). Each test runs inside a
/// transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountTransactionsTests
{
    private readonly MsSqlContainerFixture _fixture;

    public AccountTransactionsTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetByAccountIdAsync_returns_only_that_account_including_deleted()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "byaccount@example.com");
            var accountA = await SeedAccountAsync(db, userId, "Checking");
            var accountB = await SeedAccountAsync(db, userId, "Savings");
            var repository = new TransactionRepository(db);

            var kept = await SeedTransactionAsync(db, userId, accountA, "KROGER");
            var deleted = await SeedTransactionAsync(db, userId, accountA, "OLD CHARGE");
            await repository.SoftDeleteAsync(deleted);
            await SeedTransactionAsync(db, userId, accountB, "OTHER ACCOUNT");

            var result = (await repository.GetByAccountIdAsync(accountA)).ToList();

            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.Id == kept);
            Assert.Contains(result, t => t.Id == deleted && t.IsDeleted);
            Assert.All(result, t => Assert.Equal(accountA, t.AccountId));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountAsync_returns_the_account_name_and_its_transactions()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "svc-owner@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            await SeedTransactionAsync(db, userId, accountId, "KROGER");
            var service = BuildService(db);

            var result = await service.GetByAccountAsync(accountId, userId);

            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Equal(accountId, result.Data!.AccountId);
            Assert.Equal("Checking", result.Data.AccountName);
            Assert.Single(result.Data.Transactions);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountAsync_rejects_an_account_owned_by_another_user()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var ownerId = await SeedUserAsync(db, "svc-owner2@example.com");
            var otherId = await SeedUserAsync(db, "svc-intruder@example.com");
            var accountId = await SeedAccountAsync(db, ownerId, "Checking");
            var service = BuildService(db);

            var result = await service.GetByAccountAsync(accountId, otherId);

            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal("ACCOUNT_NOT_FOUND", result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static TransactionService BuildService(DbSession db)
    {
        return new TransactionService(
            db,
            new TransactionRepository(db),
            new TransactionAssignmentRepository(db),
            new LineItemRepository(db),
            new AccountRepository(db));
    }

    private static async Task<int> SeedUserAsync(DbSession db, string email)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedAccountAsync(DbSession db, int userId, string name)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, @name, 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, name },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedTransactionAsync(DbSession db, int userId, int accountId, string description)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, AccountId, Date, Description, Payee, Memo, Amount, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, @accountId, '2026-07-15', @description, '', '', -10.00, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, accountId, description },
            db.CurrentTransaction);
    }
}
