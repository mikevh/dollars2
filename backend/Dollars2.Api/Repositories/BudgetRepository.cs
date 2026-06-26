using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class BudgetRepository
{
    private readonly DbSession _db;

    public BudgetRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<Budget?> GetByMonthAsync(int userId, int year, int month)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<Budget>(
            "SELECT Id, UserId, [Year], [Month], CreatedAt, UpdatedAt FROM Budgets WHERE UserId = @userId AND [Year] = @year AND [Month] = @month",
            new { userId, year, month },
            _db.CurrentTransaction);
    }

    public async Task<Budget?> GetPreviousAsync(int userId, int year, int month)
    {
        int prevYear = month == 1 ? year - 1 : year;
        int prevMonth = month == 1 ? 12 : month - 1;
        return await GetByMonthAsync(userId, prevYear, prevMonth);
    }

    public async Task<Budget?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<Budget>(
            "SELECT Id, UserId, [Year], [Month], CreatedAt, UpdatedAt FROM Budgets WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int userId, int year, int month)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO Budgets (UserId, [Year], [Month], CreatedAt, UpdatedAt) VALUES (@userId, @year, @month, SYSUTCDATETIME(), SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, year, month },
            _db.CurrentTransaction);
    }
}
