using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the retention sweep is actually wired into the auth flows, not just available on the
/// repository: logging in and refreshing both leave a user's expired rows gone and their usable
/// rows intact, so <c>RefreshTokens</c> stops growing with usage.
///
/// Unlike the repository tests, these cannot run inside an outer transaction — the service opens
/// and commits its own — so each test cleans up the rows it committed in a finally block.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthServiceTokenRetentionTests
{
    private readonly MsSqlContainerFixture _fixture;

    public AuthServiceTokenRetentionTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoginAsync_sweeps_the_users_expired_tokens_and_keeps_the_valid_ones()
    {
        const string email = "auth-retention-login@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            await InsertTokenAsync(db, userId, "login-expired-a", daysUntilExpiry: -5);
            await InsertTokenAsync(db, userId, "login-expired-b", daysUntilExpiry: -1);
            await InsertTokenAsync(db, userId, "login-valid", daysUntilExpiry: 30);

            var service = new AuthService(db, new UserRepository(db), new RefreshTokenRepository(db), BuildConfig());

            var response = await service.LoginAsync(email);

            Assert.Null(response.Error);
            Assert.NotNull(response.Data);
            Assert.False(await TokenExistsAsync(db, "login-expired-a"));
            Assert.False(await TokenExistsAsync(db, "login-expired-b"));
            Assert.True(await TokenExistsAsync(db, "login-valid"));

            // The valid one plus the freshly minted one — the two expired rows are gone, so
            // repeated logins do not accumulate rows.
            Assert.Equal(2, await TokenCountAsync(db, userId));
            Assert.True(await TokenExistsAsync(db, response.Data!.RefreshToken));
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    [Fact]
    public async Task RefreshAsync_sweeps_expired_tokens_alongside_the_consumed_one()
    {
        const string email = "auth-retention-refresh@example.com";
        using var db = _fixture.CreateSession();
        var userId = 0;
        try
        {
            userId = await SeedUserAsync(db, email);
            await InsertTokenAsync(db, userId, "refresh-expired", daysUntilExpiry: -5);
            await InsertTokenAsync(db, userId, "refresh-consumed", daysUntilExpiry: 30);

            var service = new AuthService(db, new UserRepository(db), new RefreshTokenRepository(db), BuildConfig());

            var response = await service.RefreshAsync("refresh-consumed");

            Assert.Null(response.Error);
            Assert.NotNull(response.Data);
            Assert.False(await TokenExistsAsync(db, "refresh-expired"));
            // Rotation still consumes the presented token.
            Assert.False(await TokenExistsAsync(db, "refresh-consumed"));

            // Only the newly issued token remains.
            Assert.Equal(1, await TokenCountAsync(db, userId));
            Assert.True(await TokenExistsAsync(db, response.Data!.RefreshToken));
        }
        finally
        {
            await CleanupUserAsync(db, userId);
        }
    }

    private static IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-signing-secret-that-is-long-enough-for-hmac-sha256",
                ["Jwt:Issuer"] = "Dollars2",
                ["Jwt:Audience"] = "Dollars2",
                ["Jwt:ExpirationDays"] = "30",
                ["Jwt:RefreshExpirationDays"] = "30",
            })
            .Build();
    }

    private static async Task<int> SeedUserAsync(DbSession db, string email)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email });
    }

    private static async Task InsertTokenAsync(DbSession db, int userId, string token, int daysUntilExpiry)
    {
        await db.Connection.ExecuteAsync(
            @"INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt)
              VALUES (@userId, @token, DATEADD(day, @daysUntilExpiry, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { userId, token, daysUntilExpiry });
    }

    private static async Task<bool> TokenExistsAsync(DbSession db, string token)
    {
        var count = await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM RefreshTokens WHERE Token = @token",
            new { token });

        return count > 0;
    }

    private static async Task<int> TokenCountAsync(DbSession db, int userId)
    {
        return await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM RefreshTokens WHERE UserId = @userId",
            new { userId });
    }

    private static async Task CleanupUserAsync(DbSession db, int userId)
    {
        if (userId == 0)
        {
            return;
        }

        await db.Connection.ExecuteAsync(
            @"DELETE FROM RefreshTokens WHERE UserId = @userId;
              DELETE FROM Users WHERE Id = @userId;",
            new { userId });
    }
}
