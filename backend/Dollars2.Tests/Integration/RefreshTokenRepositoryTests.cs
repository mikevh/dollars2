using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises the real <see cref="RefreshTokenRepository"/> raw SQL against a migrated MSSQL
/// container, focused on the expired-token retention sweep. Each test runs inside a transaction
/// that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RefreshTokenRepositoryTests
{
    private readonly MsSqlContainerFixture _fixture;

    public RefreshTokenRepositoryTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteExpiredForUserAsync_removes_only_that_users_expired_tokens()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "token-prune-owner@example.com");
            var otherUserId = await SeedUserAsync(db, "token-prune-other@example.com");
            var repository = new RefreshTokenRepository(db);

            await InsertTokenAsync(db, userId, "expired-owner", daysUntilExpiry: -1);
            await InsertTokenAsync(db, userId, "valid-owner", daysUntilExpiry: 30);
            await InsertTokenAsync(db, otherUserId, "expired-other", daysUntilExpiry: -1);

            var deleted = await repository.DeleteExpiredForUserAsync(userId);

            Assert.Equal(1, deleted);
            Assert.False(await TokenExistsAsync(db, "expired-owner"));
            Assert.True(await TokenExistsAsync(db, "valid-owner"));
            // A different user's expired token is not this call's business.
            Assert.True(await TokenExistsAsync(db, "expired-other"));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task DeleteExpiredForUserAsync_is_a_no_op_when_nothing_has_expired()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "token-prune-none@example.com");
            var repository = new RefreshTokenRepository(db);

            await InsertTokenAsync(db, userId, "still-valid", daysUntilExpiry: 30);

            var deleted = await repository.DeleteExpiredForUserAsync(userId);

            Assert.Equal(0, deleted);
            Assert.True(await TokenExistsAsync(db, "still-valid"));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task DeleteExpiredForUserAsync_removes_a_token_that_expired_exactly_now()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "token-prune-boundary@example.com");
            var repository = new RefreshTokenRepository(db);

            // GetValidTokenAsync requires ExpiresAt > now, so a row at exactly now is already
            // unusable and must be sweepable — the two predicates have to agree on the boundary.
            await db.Connection.ExecuteAsync(
                @"INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt)
                  VALUES (@userId, 'expires-now', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME())",
                new { userId },
                db.CurrentTransaction);

            var deleted = await repository.DeleteExpiredForUserAsync(userId);

            Assert.Equal(1, deleted);
            Assert.False(await TokenExistsAsync(db, "expires-now"));
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

    private static async Task InsertTokenAsync(DbSession db, int userId, string token, int daysUntilExpiry)
    {
        await db.Connection.ExecuteAsync(
            @"INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt)
              VALUES (@userId, @token, DATEADD(day, @daysUntilExpiry, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { userId, token, daysUntilExpiry },
            db.CurrentTransaction);
    }

    private static async Task<bool> TokenExistsAsync(DbSession db, string token)
    {
        var count = await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM RefreshTokens WHERE Token = @token",
            new { token },
            db.CurrentTransaction);

        return count > 0;
    }
}
