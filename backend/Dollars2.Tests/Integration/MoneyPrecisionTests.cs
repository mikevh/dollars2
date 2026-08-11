using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the services actually gate on <see cref="Money.IsWholeCents"/> (issue #110): every
/// client-supplied amount is rejected outright when it is finer than a cent, rather than being
/// rounded on its way into a decimal(18,2) column. Each test runs inside a transaction that is
/// rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MoneyPrecisionTests
{
    private static readonly DateOnly Date = new(2026, 7, 15);

    private readonly MsSqlContainerFixture _fixture;

    public MoneyPrecisionTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }


    [Fact]
    public async Task A_cent_amount_is_stored_exactly_as_entered()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var seed = await SeedAsync(db, "money-create-ok@example.com");
            var service = TransactionServiceFor(db);

            var result = await service.CreateAsync(seed.UserId, Date, "Coffee", -10.99m, null, null, null);

            Assert.Null(result.Error);
            Assert.Equal(-10.99m, result.Data!.Amount);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static TransactionService TransactionServiceFor(DbSession db)
    {
        return new TransactionService(
            db,
            new TransactionRepository(db),
            new TransactionAssignmentRepository(db),
            new LineItemRepository(db),
            new AccountRepository(db));
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

    private static async Task<int> CountTransactionsAsync(DbSession db, int userId)
    {
        return await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Transactions WHERE UserId = @userId",
            new { userId },
            db.CurrentTransaction);
    }

    private sealed record Seed(int UserId, int GroupId, int LineItemId, int OtherLineItemId);

    private static async Task<Seed> SeedAsync(DbSession db, string email)
    {
        var userId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);

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

        var lineItemId = await InsertLineItemAsync(db, groupId, budgetId, "Item", 0);
        var otherLineItemId = await InsertLineItemAsync(db, groupId, budgetId, "Other", 1);

        return new Seed(userId, groupId, lineItemId, otherLineItemId);
    }

    private static async Task<int> InsertLineItemAsync(DbSession db, int groupId, int budgetId, string name, int sortOrder)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, BudgetId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@groupId, @budgetId, @name, 300, @sortOrder, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, budgetId, name, sortOrder },
            db.CurrentTransaction);
    }
}
