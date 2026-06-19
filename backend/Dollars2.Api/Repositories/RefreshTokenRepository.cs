using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class RefreshTokenRepository
{
    private readonly DbSession _db;

    public RefreshTokenRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<RefreshToken?> GetValidTokenAsync(string token)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT Id, UserId, Token, ExpiresAt, CreatedAt, UpdatedAt FROM RefreshTokens WHERE Token = @Token AND ExpiresAt > GETUTCDATE()",
            new { Token = token },
            _db.CurrentTransaction);
    }

    public async Task CreateAsync(int userId, string token, DateTime expiresAt)
    {
        await _db.Connection.ExecuteAsync(
            "INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt) VALUES (@UserId, @Token, @ExpiresAt, GETUTCDATE(), GETUTCDATE())",
            new { UserId = userId, Token = token, ExpiresAt = expiresAt },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM RefreshTokens WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }
}
