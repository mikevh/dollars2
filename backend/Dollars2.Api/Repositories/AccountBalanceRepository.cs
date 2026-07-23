using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class AccountBalanceRepository
{
    private readonly DbSession _db;

    public AccountBalanceRepository(DbSession db)
    {
        _db = db;
    }

    public async Task CreateAsync(int accountId, decimal balance)
    {
        await _db.Connection.ExecuteAsync(
            @"INSERT INTO AccountBalances (AccountId, Balance, CreatedOn, UpdatedOn)
              VALUES (@accountId, @balance, SYSUTCDATETIME(), SYSUTCDATETIME())",
            new { accountId, balance },
            _db.CurrentTransaction);
    }

    public async Task<AccountBalance?> GetLatestForAccountAsync(int accountId)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<AccountBalance>(
            @"SELECT TOP 1 Id, AccountId, Balance, CreatedOn, UpdatedOn
              FROM AccountBalances
              WHERE AccountId = @accountId
              ORDER BY CreatedOn DESC, Id DESC",
            new { accountId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<AccountBalance>> GetLatestPerAccountAsync(IEnumerable<int> accountIds)
    {
        return await _db.Connection.QueryAsync<AccountBalance>(
            @"WITH Ranked AS (
                SELECT Id, AccountId, Balance, CreatedOn, UpdatedOn,
                       ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY CreatedOn DESC, Id DESC) AS rn
                FROM AccountBalances
                WHERE AccountId IN @accountIds
              )
              SELECT Id, AccountId, Balance, CreatedOn, UpdatedOn
              FROM Ranked
              WHERE rn = 1",
            new { accountIds },
            _db.CurrentTransaction);
    }
}
