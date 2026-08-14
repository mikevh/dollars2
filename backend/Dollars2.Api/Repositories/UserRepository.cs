using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class UserRepository
{
    private readonly DbSession _db;

    public UserRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<User>(
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Email = @email",
            new { email },
            _db.CurrentTransaction);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<User>(
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<int>> GetAllIdsAsync()
    {
        return await _db.Connection.QueryAsync<int>(
            "SELECT Id FROM Users",
            transaction: _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(string email)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            "INSERT INTO Users (Email, CreatedAt, UpdatedAt) OUTPUT INSERTED.Id VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { email },
            _db.CurrentTransaction);
    }

    public async Task UpdateEmailAsync(int id, string email)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Users SET Email = @email, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, email },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM Users WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }
}
