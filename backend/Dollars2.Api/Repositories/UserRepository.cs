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
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Email = @Email",
            new { Email = email },
            _db.CurrentTransaction);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<User>(
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }
}
