using Dapper;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// End-to-end proof of the Testcontainers infrastructure: exercises the real
/// <see cref="TransactionRepository"/> raw SQL against a migrated MSSQL container.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransactionRepositoryTests
{
    private readonly MsSqlContainerFixture _fixture;

    public TransactionRepositoryTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateAsync_then_GetByIdAsync_round_trips_a_transaction()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "roundtrip@example.com");
            var repository = new TransactionRepository(db);

            var date = new DateOnly(2026, 7, 15);
            var id = await repository.CreateAsync(
                userId,
                date,
                description: "Coffee",
                payee: "Blue Bottle",
                memo: "latte",
                amount: -4.50m,
                notes: "morning",
                isManual: true);

            var loaded = await repository.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal(id, loaded!.Id);
            Assert.Equal(userId, loaded.UserId);
            Assert.Equal(date, loaded.Date);
            Assert.Equal("Coffee", loaded.Description);
            Assert.Equal("Blue Bottle", loaded.Payee);
            Assert.Equal("latte", loaded.Memo);
            Assert.Equal(-4.50m, loaded.Amount);
            Assert.Equal("morning", loaded.Notes);
            Assert.True(loaded.IsManual);
            Assert.False(loaded.IsDeleted);
            Assert.False(loaded.IsPending);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task SoftDeleteAsync_moves_transaction_into_the_deleted_set()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "softdelete@example.com");
            var repository = new TransactionRepository(db);

            var id = await repository.CreateAsync(
                userId, new DateOnly(2026, 7, 15), "Groceries", "", "", -60m, null, isManual: true);

            await repository.SoftDeleteAsync(id);

            var deleted = await repository.GetDeletedAsync(userId);
            Assert.Contains(deleted, t => t.Id == id);

            var reloaded = await repository.GetByIdAsync(id);
            Assert.True(reloaded!.IsDeleted);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static async Task<int> SeedUserAsync(Dollars2.Api.Data.DbSession db, string email)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);
    }
}
