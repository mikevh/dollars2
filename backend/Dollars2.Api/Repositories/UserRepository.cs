using System.Data;
using Dapper;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class UserRepository
{
    private readonly IDbConnection _db;

    public UserRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _db.QuerySingleOrDefaultAsync<User>(
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Email = @Email",
            new { Email = email });
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _db.QuerySingleOrDefaultAsync<User>(
            "SELECT Id, Email, CreatedAt, UpdatedAt FROM Users WHERE Id = @Id",
            new { Id = id });
    }
}
