using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the issue #75 move of IsIncome from BudgetGroups to LineItems: the repository round-trips
/// the flag, rollover suppression is keyed off the line item rather than its group, the two removed
/// group-level guards (CANNOT_MODIFY_INCOME / CANNOT_DELETE_INCOME) are genuinely gone, the new
/// CANNOT_DELETE_LAST_INCOME invariant replaces them, and a fresh user's first budget seeds an income
/// line item so that invariant holds from birth.
///
/// Tests that only call service methods with no internal transaction (UpdateLineItemAsync,
/// CreateLineItemAsync, UpdateGroupAsync, DeleteGroupAsync) run inside a transaction that is rolled
/// back, matching <see cref="LineItemNotesTests"/>. Tests that call DeleteLineItemAsync or
/// CreateBudgetAsync — both of which open and commit their own transaction — cannot be nested inside
/// an outer one, so they commit and clean up the rows they created in a finally block, matching
/// <see cref="LineItemNotesCopyForwardTests"/>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LineItemIsIncomeTests
{
    private readonly MsSqlContainerFixture _fixture;

    public LineItemIsIncomeTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAsync_persists_and_returns_IsIncome_for_both_flags()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "isincome-roundtrip@example.com");
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Group");
            var repo = new LineItemRepository(db);

            var incomeId = await repo.CreateAsync(groupId, "Paycheck", 0, 0, isIncome: true);
            var expenseId = await repo.CreateAsync(groupId, "Rent", 0, 1, isIncome: false);

            var income = await repo.GetByIdAsync(incomeId);
            var expense = await repo.GetByIdAsync(expenseId);

            Assert.True(income!.IsIncome);
            Assert.False(expense!.IsIncome);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Rollover_is_suppressed_for_income_items_but_computed_for_expense_items_with_the_same_chain_shape()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "rollover-isincome@example.com");
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Group");

            var prevExpenseId = await SeedLineItemAsync(db, groupId, "Rent (prior)", 100, isIncome: false);
            var curExpenseId = await SeedLineItemAsync(db, groupId, "Rent", 50, isIncome: false, previousLineItemId: prevExpenseId);

            var prevIncomeId = await SeedLineItemAsync(db, groupId, "Paycheck (prior)", 200, isIncome: true);
            var curIncomeId = await SeedLineItemAsync(db, groupId, "Paycheck", 300, isIncome: true, previousLineItemId: prevIncomeId);

            var service = BudgetServiceFor(db);

            var expenseResult = await service.UpdateLineItemAsync(curExpenseId, "Rent", 50, null, userId);
            var incomeResult = await service.UpdateLineItemAsync(curIncomeId, "Paycheck", 300, null, userId);

            Assert.Null(expenseResult.Error);
            Assert.Equal(100, expenseResult.Data!.RolloverAmount);

            Assert.Null(incomeResult.Error);
            Assert.True(incomeResult.Data!.IsIncome);
            Assert.Equal(0, incomeResult.Data!.RolloverAmount);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task CreateLineItemAsync_persists_the_requested_IsIncome_flag()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "create-isincome@example.com");
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Group");
            var service = BudgetServiceFor(db);

            var result = await service.CreateLineItemAsync(groupId, "Bonus", 500, isIncome: true, userId);

            Assert.Null(result.Error);
            Assert.True(result.Data!.IsIncome);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Renaming_and_deleting_an_Income_named_group_succeeds_now_that_income_is_not_a_group_property()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "income-group-guards@example.com");
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Income");
            var service = BudgetServiceFor(db);

            var renameResult = await service.UpdateGroupAsync(groupId, "Renamed Income", userId);
            Assert.Null(renameResult.Error);

            var deleteResult = await service.DeleteGroupAsync(groupId, userId);
            Assert.Null(deleteResult.Error);
            Assert.True(deleteResult.Data);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task DeleteLineItemAsync_blocks_deleting_the_last_income_line_item()
    {
        const string email = "delete-last-income@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Income");
            var onlyIncomeId = await SeedLineItemAsync(db, groupId, "Paycheck", 0, isIncome: true);

            var service = BudgetServiceFor(db);
            var result = await service.DeleteLineItemAsync(onlyIncomeId, userId);

            Assert.NotNull(result.Error);
            Assert.Equal("CANNOT_DELETE_LAST_INCOME", result.Error!.Code);
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task DeleteLineItemAsync_allows_deleting_one_of_two_income_line_items()
    {
        const string email = "delete-one-of-two-income@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Income");
            var firstIncomeId = await SeedLineItemAsync(db, groupId, "Paycheck 1", 0, isIncome: true);
            await SeedLineItemAsync(db, groupId, "Paycheck 2", 0, isIncome: true);

            var service = BudgetServiceFor(db);
            var result = await service.DeleteLineItemAsync(firstIncomeId, userId);

            Assert.Null(result.Error);
            Assert.True(result.Data);
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    /// <summary>
    /// Regression: the last-income-item count originally ran before BeginTransaction, so two
    /// concurrent deletes of two different income items could both read "2 remain" before either
    /// delete committed and both proceed, leaving zero. CountIncomeInBudgetAsync now takes an
    /// UPDLOCK+HOLDLOCK held for the transaction's lifetime, so the second delete blocks until the
    /// first commits and then correctly sees the post-delete count. Regardless of which of the two
    /// wins the race, exactly one must succeed and the other must be blocked — never both.
    /// </summary>
    [Fact]
    public async Task DeleteLineItemAsync_serializes_concurrent_deletes_of_the_last_two_income_line_items_so_only_one_succeeds()
    {
        const string email = "delete-concurrent-income@example.com";
        using var seedDb = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(seedDb, email);
            var budgetId = await SeedBudgetAsync(seedDb, userId, 2026, 7);
            var groupId = await SeedGroupAsync(seedDb, budgetId, "Income");
            var firstIncomeId = await SeedLineItemAsync(seedDb, groupId, "Paycheck 1", 0, isIncome: true);
            var secondIncomeId = await SeedLineItemAsync(seedDb, groupId, "Paycheck 2", 0, isIncome: true);

            using var dbA = _fixture.CreateSession();
            using var dbB = _fixture.CreateSession();
            var serviceA = BudgetServiceFor(dbA);
            var serviceB = BudgetServiceFor(dbB);

            var taskA = serviceA.DeleteLineItemAsync(firstIncomeId, userId);
            var taskB = serviceB.DeleteLineItemAsync(secondIncomeId, userId);
            var resultA = await taskA;
            var resultB = await taskB;

            var results = new[] { resultA, resultB };
            Assert.Single(results, r => r.Error is null);
            Assert.Single(results, r => r.Error?.Code == "CANNOT_DELETE_LAST_INCOME");
        }
        finally
        {
            await CleanupUserAsync(seedDb, userId);
        }
    }

    [Fact]
    public async Task DeleteLineItemAsync_does_not_block_deleting_expense_items_regardless_of_income_count()
    {
        const string email = "delete-expense-unaffected@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Group");
            await SeedLineItemAsync(db, groupId, "Paycheck", 0, isIncome: true); // the only income item
            var expenseId = await SeedLineItemAsync(db, groupId, "Rent", 0, isIncome: false);

            var service = BudgetServiceFor(db);
            var result = await service.DeleteLineItemAsync(expenseId, userId);

            Assert.Null(result.Error);
            Assert.True(result.Data);
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task CreateBudgetAsync_seeds_an_Income_group_with_one_income_line_item_when_the_user_has_no_previous_budget()
    {
        const string email = "create-budget-seeds-income@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            var now = DateTime.UtcNow;
            var service = BudgetServiceFor(db);

            var result = await service.CreateBudgetAsync(userId, now.Year, now.Month);

            Assert.Null(result.Error);
            var budget = result.Data!;
            var group = Assert.Single(budget.Groups);
            Assert.Equal("Income", group.Name);
            var item = Assert.Single(group.LineItems);
            Assert.Equal("Paycheck", item.Name);
            Assert.True(item.IsIncome);
            Assert.Equal(0, item.PlannedAmount);
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    private static BudgetService BudgetServiceFor(DbSession db)
    {
        return new BudgetService(
            db,
            new BudgetRepository(db),
            new BudgetGroupRepository(db),
            new LineItemRepository(db),
            new TransactionAssignmentRepository(db),
            new AccountRepository(db),
            new AccountBalanceRepository(db));
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

    private static async Task<int> SeedBudgetAsync(DbSession db, int userId, int year, int month)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Budgets (UserId, [Year], [Month], CreatedAt, UpdatedAt)
              VALUES (@userId, @year, @month, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, year, month },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedGroupAsync(DbSession db, int budgetId, string name)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO BudgetGroups (BudgetId, Name, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@budgetId, @name, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId, name },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedLineItemAsync(DbSession db, int groupId, string name, decimal plannedAmount, bool isIncome, int? previousLineItemId = null)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, IsIncome, SortOrder, PreviousLineItemId, CreatedAt, UpdatedAt)
              VALUES (@groupId, @name, @plannedAmount, @isIncome, 0, @previousLineItemId, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, name, plannedAmount, isIncome, previousLineItemId },
            db.CurrentTransaction);
    }

    private static async Task CleanupUserAsync(DbSession db, int userId)
    {
        if (userId == 0)
        {
            return;
        }

        await db.Connection.ExecuteAsync(
            @"DELETE li FROM LineItems li
              INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId
              INNER JOIN Budgets b ON b.Id = bg.BudgetId
              WHERE b.UserId = @userId;
              DELETE bg FROM BudgetGroups bg
              INNER JOIN Budgets b ON b.Id = bg.BudgetId
              WHERE b.UserId = @userId;
              DELETE FROM Budgets WHERE UserId = @userId;
              DELETE FROM Users WHERE Id = @userId;",
            new { userId });
    }
}
