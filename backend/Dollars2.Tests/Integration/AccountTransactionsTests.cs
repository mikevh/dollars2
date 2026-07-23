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

            var (rows, total) = await repository.GetByAccountIdAsync(accountA, 1, 100, "date", "desc", null);

            Assert.Equal(2, rows.Count);
            Assert.Equal(2, total);
            Assert.Contains(rows, t => t.Id == kept);
            Assert.Contains(rows, t => t.Id == deleted && t.IsDeleted);
            Assert.All(rows, t => Assert.Equal(accountA, t.AccountId));
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

            var result = await service.GetByAccountAsync(accountId, userId, 1, 100, "date", "desc", null);

            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Equal(accountId, result.Data!.AccountId);
            Assert.Equal("Checking", result.Data.AccountName);
            Assert.Single(result.Data.Transactions);
            Assert.Equal(1, result.Data.TotalCount);
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

            var result = await service.GetByAccountAsync(accountId, otherId, 1, 100, "date", "desc", null);

            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal("ACCOUNT_NOT_FOUND", result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_pages_by_date_desc_with_stable_total()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "paging@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            // Five rows on distinct descending dates so ordering is unambiguous.
            for (var day = 1; day <= 5; day++)
            {
                await SeedTransactionAsync(db, userId, accountId, $"ROW {day}", date: $"2026-07-0{day}");
            }

            var (page1, total1) = await repository.GetByAccountIdAsync(accountId, 1, 2, "date", "desc", null);
            var (page2, total2) = await repository.GetByAccountIdAsync(accountId, 2, 2, "date", "desc", null);
            var (page3, total3) = await repository.GetByAccountIdAsync(accountId, 3, 2, "date", "desc", null);

            Assert.Equal(5, total1);
            Assert.Equal(5, total2);
            Assert.Equal(5, total3);
            Assert.Equal(2, page1.Count);
            Assert.Equal(2, page2.Count);
            Assert.Single(page3);

            // Page 1 holds the two newest dates; page 3 holds the oldest; no overlaps.
            Assert.Equal(new[] { "2026-07-05", "2026-07-04" }, page1.Select(t => t.Date.ToString("yyyy-MM-dd")));
            Assert.Equal(new[] { "2026-07-03", "2026-07-02" }, page2.Select(t => t.Date.ToString("yyyy-MM-dd")));
            Assert.Equal(new[] { "2026-07-01" }, page3.Select(t => t.Date.ToString("yyyy-MM-dd")));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_sorts_by_the_requested_column_and_direction()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "sorting@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            await SeedTransactionAsync(db, userId, accountId, "Bravo", amount: -30.00m);
            await SeedTransactionAsync(db, userId, accountId, "Alpha", amount: -10.00m);
            await SeedTransactionAsync(db, userId, accountId, "Charlie", amount: -20.00m);

            var (byAmountAsc, _) = await repository.GetByAccountIdAsync(accountId, 1, 100, "amount", "asc", null);
            var (byDescription, _) = await repository.GetByAccountIdAsync(accountId, 1, 100, "description", "asc", null);

            Assert.Equal(new[] { -30.00m, -20.00m, -10.00m }, byAmountAsc.Select(t => t.Amount));
            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, byDescription.Select(t => t.Description));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_falls_back_to_default_sort_for_invalid_params()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "invalid-sort@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            await SeedTransactionAsync(db, userId, accountId, "Older", date: "2026-07-01");
            await SeedTransactionAsync(db, userId, accountId, "Newer", date: "2026-07-09");

            // Garbage sort/dir must not error or inject — default is date desc.
            var (rows, total) = await repository.GetByAccountIdAsync(accountId, 1, 100, "amount); DROP TABLE Transactions;--", "sideways", null);

            Assert.Equal(2, total);
            Assert.Equal(new[] { "Newer", "Older" }, rows.Select(t => t.Description));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_searches_payee_and_description_case_insensitively()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "textsearch@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            var byDescription = await SeedTransactionAsync(db, userId, accountId, "Morning COFFEE run");
            var byPayee = await SeedTransactionAsync(db, userId, accountId, "Cafe visit", payee: "Coffee Cart");
            await SeedTransactionAsync(db, userId, accountId, "Gas station", payee: "Shell");

            var (rows, total) = await repository.GetByAccountIdAsync(accountId, 1, 100, "date", "desc", "coffee");

            Assert.Equal(2, total);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, t => t.Id == byDescription);
            Assert.Contains(rows, t => t.Id == byPayee);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_search_matches_absolute_amount()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "amountsearch@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            var expense = await SeedTransactionAsync(db, userId, accountId, "Groceries", amount: -42.50m);
            var income = await SeedTransactionAsync(db, userId, accountId, "Refund", amount: 42.50m);
            await SeedTransactionAsync(db, userId, accountId, "Other", amount: -7.00m);

            var (rows, total) = await repository.GetByAccountIdAsync(accountId, 1, 100, "date", "desc", "42.50");

            Assert.Equal(2, total);
            Assert.Contains(rows, t => t.Id == expense);
            Assert.Contains(rows, t => t.Id == income);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetByAccountIdAsync_includes_deleted_pending_and_manual_rows()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "statuses@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new TransactionRepository(db);

            var pending = await SeedTransactionAsync(db, userId, accountId, "Pending charge", isPending: true);
            var manual = await SeedTransactionAsync(db, userId, accountId, "Manual entry", isManual: true);
            var deleted = await SeedTransactionAsync(db, userId, accountId, "Removed");
            await repository.SoftDeleteAsync(deleted);

            var (rows, total) = await repository.GetByAccountIdAsync(accountId, 1, 100, "date", "desc", null);

            Assert.Equal(3, total);
            Assert.Contains(rows, t => t.Id == pending && t.IsPending);
            Assert.Contains(rows, t => t.Id == manual && t.IsManual);
            Assert.Contains(rows, t => t.Id == deleted && t.IsDeleted);
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

    private static async Task<int> SeedTransactionAsync(
        DbSession db,
        int userId,
        int accountId,
        string description,
        string date = "2026-07-15",
        decimal amount = -10.00m,
        string payee = "",
        bool isPending = false,
        bool isManual = false)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, AccountId, Date, Description, Payee, Memo, Amount, IsPending, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, @accountId, @date, @description, @payee, '', @amount, @isPending, @isManual, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, accountId, date, description, payee, amount, isPending, isManual },
            db.CurrentTransaction);
    }
}
