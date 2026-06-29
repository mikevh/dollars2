using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class AccountRepository
{
    private readonly DbSession _db;

    public AccountRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<Account?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<Account>(
            "SELECT Id, UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt FROM Accounts WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Account>> GetByUserIdAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Account>(
            "SELECT Id, UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt FROM Accounts WHERE UserId = @userId",
            new { userId },
            _db.CurrentTransaction);
    }

    public async Task UpdateConnectionDetailsJsonAsync(int id, string connectionDetailsJson)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Accounts SET ConnectionDetailsJson = @connectionDetailsJson, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, connectionDetailsJson },
            _db.CurrentTransaction);
    }
}
