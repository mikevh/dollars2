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
            "SELECT Id, BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt FROM BudgetGroups WHERE BudgetId = @BudgetId ORDER BY SortOrder",
            new { BudgetId = budgetId },
            _db.CurrentTransaction);
    }

    public async Task<BudgetGroup?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<BudgetGroup>(
            "SELECT Id, BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt FROM BudgetGroups WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int budgetId, string name, bool isIncome, int sortOrder)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO BudgetGroups (BudgetId, Name, IsIncome, SortOrder, CreatedAt, UpdatedAt) VALUES (@BudgetId, @Name, @IsIncome, @SortOrder, GETUTCDATE(), GETUTCDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { BudgetId = budgetId, Name = name, IsIncome = isIncome, SortOrder = sortOrder },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, string name)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE BudgetGroups SET Name = @Name, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = id, Name = name },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM BudgetGroups WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task<bool> HasLineItemsAsync(int id)
    {
        return await _db.Connection.ExecuteScalarAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM LineItems WHERE GroupId = @Id) THEN 1 ELSE 0 END",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task UpdateSortOrderAsync(int id, int sortOrder)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE BudgetGroups SET SortOrder = @SortOrder, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = id, SortOrder = sortOrder },
            _db.CurrentTransaction);
    }

    public async Task<int> GetMaxSortOrderAsync(int budgetId)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            "SELECT ISNULL(MAX(SortOrder), -1) FROM BudgetGroups WHERE BudgetId = @BudgetId",
            new { BudgetId = budgetId },
            _db.CurrentTransaction);
    }
}
