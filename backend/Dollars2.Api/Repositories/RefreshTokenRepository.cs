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
            "SELECT Id, UserId, Token, ExpiresAt, CreatedAt, UpdatedAt FROM RefreshTokens WHERE Token = @token AND ExpiresAt > SYSUTCDATETIME()",
            new { token },
            _db.CurrentTransaction);
    }

    public async Task CreateAsync(int userId, string token, DateTime expiresAt)
    {
        await _db.Connection.ExecuteAsync(
            "INSERT INTO RefreshTokens (UserId, Token, ExpiresAt, CreatedAt, UpdatedAt) VALUES (@userId, @token, @expiresAt, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { userId, token, expiresAt },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM RefreshTokens WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }
}
