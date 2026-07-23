using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the line-item notes write path (issue #64): <see cref="LineItemRepository.UpdateAsync"/>
/// persists the <c>Notes</c> column verbatim (no trimming, no empty→NULL coercion) while still
/// updating <c>Name</c>/<c>PlannedAmount</c>, and a subsequent read returns the stored value.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LineItemNotesTests
{
    private readonly MsSqlContainerFixture _fixture;

    public LineItemNotesTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpdateAsync_persists_notes_and_still_updates_name_and_planned_amount()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var lineItemId = await SeedLineItemAsync(db, "notes-persist@example.com");
            var repository = new LineItemRepository(db);

            await repository.UpdateAsync(lineItemId, "Renamed", 42.50m, "pay by the 5th");

            var item = await repository.GetByIdAsync(lineItemId);
            Assert.NotNull(item);
            Assert.Equal("Renamed", item!.Name);
            Assert.Equal(42.50m, item.PlannedAmount);
            Assert.Equal("pay by the 5th", item.Notes);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task UpdateAsync_stores_notes_verbatim_without_trimming_or_null_coercion()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var lineItemId = await SeedLineItemAsync(db, "notes-verbatim@example.com");
            var repository = new LineItemRepository(db);

            // Whitespace is preserved as-is; a whitespace-only value is NOT coerced to NULL.
            await repository.UpdateAsync(lineItemId, "Item", 0m, "  spaced  ");

            var item = await repository.GetByIdAsync(lineItemId);
            Assert.Equal("  spaced  ", item!.Notes);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task UpdateAsync_can_clear_notes_back_to_null()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var lineItemId = await SeedLineItemAsync(db, "notes-clear@example.com");
            var repository = new LineItemRepository(db);

            await repository.UpdateAsync(lineItemId, "Item", 0m, "temporary");
            await repository.UpdateAsync(lineItemId, "Item", 0m, null);

            var item = await repository.GetByIdAsync(lineItemId);
            Assert.Null(item!.Notes);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static async Task<int> SeedLineItemAsync(DbSession db, string email)
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

        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt)
              VALUES (@groupId, 'Item', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { groupId },
            db.CurrentTransaction);
    }
}
