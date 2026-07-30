using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the actual bug fixed by issue #163: <see cref="BudgetService.CreateBudgetAsync"/> copies
/// a line item's <c>Notes</c> forward into next month's budget, the same way it already copies
/// <c>Name</c>, <c>PlannedAmount</c>, and <c>SortOrder</c>.
///
/// Unlike the repository tests in <see cref="LineItemNotesTests"/>, this cannot run inside an outer
/// transaction — <see cref="BudgetService.CreateBudgetAsync"/> opens and commits its own — so the
/// test cleans up the rows it committed in a finally block.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LineItemNotesCopyForwardTests
{
    private readonly MsSqlContainerFixture _fixture;

    public LineItemNotesCopyForwardTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateBudgetAsync_copies_notes_forward_onto_next_months_line_item()
    {
        const string email = "notes-copy-forward@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            var sourceGroupId = await SeedGroupAsync(db, await SeedBudgetAsync(db, userId, 2026, 7), "Bills");
            var sourceLineItemId = await SeedLineItemAsync(db, sourceGroupId, "Water", "pay by the 5th");

            var service = BudgetServiceFor(db);

            var result = await service.CreateBudgetAsync(userId, 2026, 8);

            Assert.Null(result.Error);
            var copiedItem = await db.Connection.QuerySingleOrDefaultAsync<LineItemRow>(
                "SELECT Name, Notes FROM LineItems WHERE PreviousLineItemId = @sourceLineItemId",
                new { sourceLineItemId });

            Assert.NotNull(copiedItem);
            Assert.Equal("Water", copiedItem!.Name);
            Assert.Equal("pay by the 5th", copiedItem.Notes);
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    private sealed record LineItemRow(string Name, string Notes);

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
            new { email });
    }

    private static async Task<int> SeedBudgetAsync(DbSession db, int userId, int year, int month)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Budgets (UserId, [Year], [Month], CreatedAt, UpdatedAt)
              VALUES (@userId, @year, @month, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, year, month });
    }

    private static async Task<int> SeedGroupAsync(DbSession db, int budgetId, string name)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO BudgetGroups (BudgetId, Name, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@budgetId, @name, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId, name });
    }

    private static async Task<int> SeedLineItemAsync(DbSession db, int groupId, string name, string notes)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, Notes, CreatedAt, UpdatedAt)
              VALUES (@groupId, @name, 0, 0, @notes, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId, name, notes });
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
