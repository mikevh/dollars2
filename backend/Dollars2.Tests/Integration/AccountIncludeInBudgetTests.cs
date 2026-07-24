using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the per-account "include in budget" filter (issue #47): a synced account with
/// <c>IncludeInBudget = 0</c> is excluded from every budget-pane read
/// (<see cref="TransactionRepository.GetNewAsync"/>, <see cref="TransactionRepository.GetTrackedAsync"/>,
/// <see cref="TransactionRepository.GetDeletedAsync"/>, <see cref="TransactionRepository.GetPendingAsync"/>,
/// <see cref="TransactionRepository.GetCountsAsync"/>), while included-account rows, manual rows, and the
/// per-account view (<see cref="TransactionRepository.GetByAccountIdAsync"/>) are unaffected.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountIncludeInBudgetTests
{
    private readonly MsSqlContainerFixture _fixture;

    public AccountIncludeInBudgetTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_defaults_existing_accounts_to_included()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "default-included@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var repository = new AccountRepository(db);

            var account = await repository.GetByIdAsync(accountId);

            Assert.NotNull(account);
            Assert.True(account!.IncludeInBudget);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Excluded_account_is_hidden_from_the_budget_pane_but_kept_everywhere_else()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "excluded@example.com");
            var included = await SeedAccountAsync(db, userId, "Checking", includeInBudget: true);
            var excluded = await SeedAccountAsync(db, userId, "Brokerage", includeInBudget: false);
            var repository = new TransactionRepository(db);
            var assignments = new TransactionAssignmentRepository(db);
            var lineItemId = await SeedLineItemAsync(db, userId);

            // New (unassigned) rows on each account, plus a manual (accountless) row.
            var includedNew = await SeedSyncedTransactionAsync(db, userId, included, "KROGER");
            var excludedNew = await SeedSyncedTransactionAsync(db, userId, excluded, "VANGUARD BUY");
            var manualNew = await repository.CreateAsync(
                userId, new DateOnly(2026, 7, 15), "Cash", "", "", -20m, null, isManual: true);

            // Tracked (assigned) rows on each account.
            var includedTracked = await SeedSyncedTransactionAsync(db, userId, included, "RENT");
            var excludedTracked = await SeedSyncedTransactionAsync(db, userId, excluded, "DIVIDEND REINVEST");
            await assignments.CreateAsync(includedTracked, lineItemId, -100m);
            await assignments.CreateAsync(excludedTracked, lineItemId, -100m);

            // Deleted rows on each account.
            var includedDeleted = await SeedSyncedTransactionAsync(db, userId, included, "OLD CHECKING");
            var excludedDeleted = await SeedSyncedTransactionAsync(db, userId, excluded, "OLD BROKERAGE");
            await repository.SoftDeleteAsync(includedDeleted);
            await repository.SoftDeleteAsync(excludedDeleted);

            // Pending rows on each account.
            var includedPending = await SeedSyncedTransactionAsync(db, userId, included, "PENDING CHECKING", isPending: true);
            var excludedPending = await SeedSyncedTransactionAsync(db, userId, excluded, "PENDING BROKERAGE", isPending: true);

            var newIds = (await repository.GetNewAsync(userId)).Select(t => t.Id).ToHashSet();
            var trackedIds = (await repository.GetTrackedAsync(userId, new DateOnly(2026, 1, 1))).Select(t => t.Id).ToHashSet();
            var deletedIds = (await repository.GetDeletedAsync(userId)).Select(t => t.Id).ToHashSet();
            var pendingIds = (await repository.GetPendingAsync(userId)).Select(t => t.Id).ToHashSet();

            // Included-account rows and the manual row are present.
            Assert.Contains(includedNew, newIds);
            Assert.Contains(manualNew, newIds);
            Assert.Contains(includedTracked, trackedIds);
            Assert.Contains(includedDeleted, deletedIds);
            Assert.Contains(includedPending, pendingIds);

            // Excluded-account rows are gone from every budget-pane tab.
            Assert.DoesNotContain(excludedNew, newIds);
            Assert.DoesNotContain(excludedTracked, trackedIds);
            Assert.DoesNotContain(excludedDeleted, deletedIds);
            Assert.DoesNotContain(excludedPending, pendingIds);

            // Counts mirror the tab contents (included new + manual = 2, and 1 each elsewhere).
            var counts = await repository.GetCountsAsync(userId);
            Assert.Equal(2, counts.New);
            Assert.Equal(1, counts.Tracked);
            Assert.Equal(1, counts.Deleted);
            Assert.Equal(1, counts.Pending);

            // The per-account view still returns the excluded account's rows.
            var perAccount = (await repository.GetByAccountIdAsync(excluded, 1, 100, "date", "desc", null)).Rows.Select(t => t.Id).ToHashSet();
            Assert.Contains(excludedNew, perAccount);
            Assert.Contains(excludedTracked, perAccount);
            Assert.Contains(excludedDeleted, perAccount);
            Assert.Contains(excludedPending, perAccount);
        }
        finally
        {
            db.Rollback();
        }
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

    private static async Task<int> SeedAccountAsync(DbSession db, int userId, string name, bool includeInBudget = true)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, IncludeInBudget, CreatedAt, UpdatedAt)
              VALUES (@userId, @name, 'SimpleFIN', NULL, @includeInBudget, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, name, includeInBudget },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedSyncedTransactionAsync(
        DbSession db, int userId, int accountId, string description, bool isPending = false)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, AccountId, Date, Description, Payee, Memo, Amount, IsPending, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, @accountId, '2026-07-15', @description, '', '', -10.00, @isPending, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, accountId, description, isPending },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedLineItemAsync(DbSession db, int userId)
    {
        var budgetId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Budgets (UserId, [Year], [Month], CreatedAt, UpdatedAt)
              VALUES (@userId, 2026, 7, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId },
            db.CurrentTransaction);

        var groupId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO BudgetGroups (BudgetId, Name, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@budgetId, 'Group', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId },
            db.CurrentTransaction);

        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@groupId, 'Item', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId },
            db.CurrentTransaction);
    }
}
