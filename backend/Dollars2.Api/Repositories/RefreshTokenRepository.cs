using System.Data;
using Dapper;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class RefreshTokenRepository
{
    private readonly IDbConnection _db;

    public RefreshTokenRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<RefreshToken?> GetValidTokenAsync(string token)
    {
        return await _db.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT Id, UserId, Token, ExpiresAt, CreatedAt, UpdatedAt FROM RefreshTokens WHERE Token = @Token AND ExpiresAt > GETUTCDATE()",
            new { Token = token });
    }

    public async Task CreateAsync(int userId, string token, DateTime expiresAt)
    {
        await _db.ExecuteAsync(
            "INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt) VALUES (@UserId, @Token, @ExpiresAt, GETUTCDATE(), GETUTCDATE())",
            new { UserId = userId, Token = token, ExpiresAt = expiresAt });
    }

    public async Task DeleteAsync(int id)
    {
        await _db.ExecuteAsync("DELETE FROM RefreshTokens WHERE Id = @Id", new { Id = id });
    }
}
