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
            "SELECT * FROM Accounts WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }
}
