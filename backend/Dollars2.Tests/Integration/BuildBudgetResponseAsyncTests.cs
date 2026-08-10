using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Microsoft.Data.SqlClient;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the issue #15 fix: <see cref="BudgetService"/>'s budget-response builder returns the same
/// values (including a multi-month rollover chain) after flattening, and issues a query count that
/// stays flat as the number of groups/items grows instead of fanning out per row.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BuildBudgetResponseAsyncTests
{
    private readonly MsSqlContainerFixture _fixture;

    public BuildBudgetResponseAsyncTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Rollover_chain_and_ordering_are_correct_after_flattening()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "rollover-chain@example.com");

            var mayBudgetId = await SeedBudgetAsync(db, userId, 2026, 5);
            var mayGroupId = await SeedGroupAsync(db, mayBudgetId, "Bills", sortOrder: 0);
            var mayWaterId = await SeedLineItemAsync(db, mayGroupId, "Water", plannedAmount: 40m, sortOrder: 0);
            await AssignAsync(db, userId, mayWaterId, -10m); // May leftover: 40 + (-10) = 30

            var juneBudgetId = await SeedBudgetAsync(db, userId, 2026, 6);
            var juneGroupId = await SeedGroupAsync(db, juneBudgetId, "Bills", sortOrder: 0);
            var juneWaterId = await SeedLineItemAsync(db, juneGroupId, "Water", plannedAmount: 50m, sortOrder: 0, previousLineItemId: mayWaterId);
            await AssignAsync(db, userId, juneWaterId, -20m); // June leftover: 50 + (-20) = 30

            var julyBudgetId = await SeedBudgetAsync(db, userId, 2026, 7);
            var incomeGroupId = await SeedGroupAsync(db, julyBudgetId, "Income", sortOrder: 0);
            var paycheckId = await SeedLineItemAsync(db, incomeGroupId, "Paycheck", plannedAmount: 1000m, sortOrder: 0, isIncome: true);
            await AssignAsync(db, userId, paycheckId, 900m);

            var billsGroupId = await SeedGroupAsync(db, julyBudgetId, "Bills", sortOrder: 1);
            // Seed Rent before Water but give it a higher SortOrder, so a correctness check on
            // ordering actually exercises the ORDER BY rather than matching insertion order by luck.
            var rentId = await SeedLineItemAsync(db, billsGroupId, "Rent", plannedAmount: 800m, sortOrder: 1);
            var julyWaterId = await SeedLineItemAsync(db, billsGroupId, "Water", plannedAmount: 60m, sortOrder: 0, previousLineItemId: juneWaterId);
            await AssignAsync(db, userId, julyWaterId, -15m);

            var service = BudgetServiceFor(db);
            var result = await service.GetBudgetAsync(userId, 2026, 7);

            Assert.Null(result.Error);
            var budget = result.Data!;
            Assert.Equal(2, budget.Groups.Count);

            var income = budget.Groups[0];
            Assert.Equal(incomeGroupId, income.Id);
            var paycheck = Assert.Single(income.LineItems);
            Assert.Equal(900m, paycheck.ReceivedAmount);
            Assert.Equal(-900m, paycheck.SpentAmount);
            Assert.Equal(0m, paycheck.RolloverAmount);

            var bills = budget.Groups[1];
            Assert.Equal(billsGroupId, bills.Id);
            Assert.Equal(2, bills.LineItems.Count);
            // SortOrder 0 (Water) must come before SortOrder 1 (Rent) despite Rent being inserted first.
            Assert.Equal("Water", bills.LineItems[0].Name);
            Assert.Equal("Rent", bills.LineItems[1].Name);

            var water = bills.LineItems[0];
            Assert.Equal(15m, water.SpentAmount);
            Assert.Equal(60m, water.RolloverAmount); // 30 (May) + 30 (June), July's own activity excluded

            var rent = bills.LineItems[1];
            Assert.Equal(0m, rent.RolloverAmount); // no PreviousLineItemId chain
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Query_count_for_BuildBudgetResponseAsync_is_constant_regardless_of_group_and_item_count()
    {
        var small = await QueryCountForBudgetAsync(groupCount: 2, itemsPerGroup: 2);
        var large = await QueryCountForBudgetAsync(groupCount: 8, itemsPerGroup: 5);

        Assert.Equal(small, large);
    }

    private async Task<int> QueryCountForBudgetAsync(int groupCount, int itemsPerGroup)
    {
        var counting = new CountingDbConnection(new SqlConnection(_fixture.ConnectionString));
        using var db = new DbSession(counting);
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, $"count-{groupCount}-{itemsPerGroup}@example.com");
            var budgetId = await SeedBudgetAsync(db, userId, 2026, 7);

            for (var g = 0; g < groupCount; g++)
            {
                var groupId = await SeedGroupAsync(db, budgetId, $"Group {g}", sortOrder: g);
                for (var i = 0; i < itemsPerGroup; i++)
                {
                    var itemId = await SeedLineItemAsync(db, groupId, $"Item {g}-{i}", plannedAmount: 100m, sortOrder: i);
                    await AssignAsync(db, userId, itemId, -25m);
                }
            }

            var service = BudgetServiceFor(db);
            var before = counting.CommandCount;
            var result = await service.GetBudgetAsync(userId, 2026, 7);
            Assert.Null(result.Error);

            return counting.CommandCount - before;
        }
        finally
        {
            db.Rollback();
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

    private static async Task AssignAsync(DbSession db, int userId, int lineItemId, decimal amount)
    {
        var transactionId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, Date, Description, Payee, Memo, Amount, IsDeleted, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, '2026-07-15', 'Test', '', '', @amount, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, amount },
            db.CurrentTransaction);

        await new TransactionAssignmentRepository(db).CreateAsync(transactionId, lineItemId, amount);
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

    private static async Task<int> SeedGroupAsync(DbSession db, int budgetId, string name, int sortOrder)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO BudgetGroups (BudgetId, Name, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@budgetId, @name, @sortOrder, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId, name, sortOrder },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedLineItemAsync(DbSession db, int groupId, string name, decimal plannedAmount, int sortOrder, int? previousLineItemId = null, bool isIncome = false)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, IsIncome, SortOrder, PreviousLineItemId, CreatedAt, UpdatedAt)
              VALUES (@groupId, @name, @plannedAmount, @isIncome, @sortOrder, @previousLineItemId, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, name, plannedAmount, isIncome, sortOrder, previousLineItemId },
            db.CurrentTransaction);
    }
}
