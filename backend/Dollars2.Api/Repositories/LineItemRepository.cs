using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class LineItemRepository
{
    private readonly DbSession _db;

    public LineItemRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<IEnumerable<LineItem>> GetByGroupIdAsync(int groupId)
    {
        return await _db.Connection.QueryAsync<LineItem>(
            "SELECT Id, GroupId, Name, PlannedAmount, SortOrder, Notes, CreatedAt, UpdatedAt FROM LineItems WHERE GroupId = @GroupId ORDER BY SortOrder",
            new { GroupId = groupId },
            _db.CurrentTransaction);
    }

    public async Task<LineItem?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<LineItem>(
            "SELECT Id, GroupId, Name, PlannedAmount, SortOrder, Notes, CreatedAt, UpdatedAt FROM LineItems WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateAsync(int groupId, string name, decimal plannedAmount, int sortOrder)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            "INSERT INTO LineItems (GroupId, Name, PlannedAmount, SortOrder, CreatedAt, UpdatedAt) VALUES (@GroupId, @Name, @PlannedAmount, @SortOrder, GETUTCDATE(), GETUTCDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { GroupId = groupId, Name = name, PlannedAmount = plannedAmount, SortOrder = sortOrder },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, string name, decimal plannedAmount)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET Name = @Name, PlannedAmount = @PlannedAmount, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = id, Name = name, PlannedAmount = plannedAmount },
            _db.CurrentTransaction);
    }

    public async Task DeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM LineItems WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task UpdateSortOrderAsync(int id, int sortOrder)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE LineItems SET SortOrder = @SortOrder, UpdatedAt = GETUTCDATE() WHERE Id = @Id",
            new { Id = id, SortOrder = sortOrder },
            _db.CurrentTransaction);
    }

    public async Task<int> GetMaxSortOrderAsync(int groupId)
    {
        return await _db.Connection.ExecuteScalarAsync<int>(
            "SELECT ISNULL(MAX(SortOrder), -1) FROM LineItems WHERE GroupId = @GroupId",
            new { GroupId = groupId },
            _db.CurrentTransaction);
    }
}
