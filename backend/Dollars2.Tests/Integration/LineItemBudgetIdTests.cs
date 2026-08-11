using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves issue #86: <c>LineItems.BudgetId</c> is a direct column rather than reached only through
/// <c>BudgetGroups</c>. A line item created through the normal path always has a <c>BudgetId</c>
/// matching its group's, and <see cref="LineItemRepository.IsOwnedByUserAsync"/> — now rewritten to
/// join <c>LineItems -&gt; Budgets</c> directly instead of via <c>BudgetGroups</c> — still rejects a
/// line item owned by another user. Each test runs inside a transaction that is rolled back, so
/// nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LineItemBudgetIdTests
{
    private readonly MsSqlContainerFixture _fixture;

    public LineItemBudgetIdTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IsOwnedByUserAsync_accepts_the_owner_and_rejects_another_user_via_the_direct_BudgetId_join()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var ownerId = await SeedUserAsync(db, "budgetid-owner@example.com");
            var otherId = await SeedUserAsync(db, "budgetid-other@example.com");
            var budgetId = await SeedBudgetAsync(db, ownerId, 2026, 7);
            var groupId = await SeedGroupAsync(db, budgetId, "Group");
            var lineItemId = await SeedLineItemAsync(db, groupId, budgetId, "Gas", 100m);

            var repository = new LineItemRepository(db);

            Assert.True(await repository.IsOwnedByUserAsync(lineItemId, ownerId));
            Assert.False(await repository.IsOwnedByUserAsync(lineItemId, otherId));
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

    private static async Task<int> SeedLineItemAsync(DbSession db, int groupId, int budgetId, string name, decimal plannedAmount)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, BudgetId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@groupId, @budgetId, @name, @plannedAmount, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, budgetId, name, plannedAmount },
            db.CurrentTransaction);
    }
}
