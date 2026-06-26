using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class BudgetGroupRepository
{
    private readonly DbSession _db;

    public BudgetGroupRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<IEnumerable<BudgetGroup>> GetByBudgetIdAsync(int budgetId)
    {
        return await _db.Connection.QueryAsync<BudgetGroup>(
            "SELECT Id, BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt FROM BudgetGroups WHERE BudgetId = @budgetId ORDER BY SortOrder",
            new { budgetId },
            _db.CurrentTransaction);
    }

    public async Task<BudgetGroup?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<BudgetGroup>(
            "SELECT Id, BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt FROM BudgetGroups WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int budgetId, string name, bool isIncome, int sortOrder)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO BudgetGroups (BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt) VALUES (@budgetId, @name, @isIncome, @sortOrder, SYSUTCDATETIME(), SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { budgetId, name, isIncome, sortOrder },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, string name)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE BudgetGroups SET Name = @name, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, name },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM BudgetGroups WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<bool> HasLineItemsAsync(int id)
    {
        return await _db.Connection.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM LineItems WHERE GroupId = @id) THEN 1 ELSE 0 END",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task UpdateSortOrderAsync(int id, int sortOrder)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE BudgetGroups SET SortOrder = @sortOrder, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, sortOrder },
            _db.CurrentTransaction);
    }

    public async Task<int> GetMaxSortOrderAsync(int budgetId)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            "SELECT ISNULL(MAX(SortOrder), -1) FROM BudgetGroups WHERE BudgetId = @budgetId",
            new { budgetId },
            _db.CurrentTransaction);
    }
}
