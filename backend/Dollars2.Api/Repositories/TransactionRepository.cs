using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;

namespace Dollars2.Api.Repositories;

public class TransactionRepository
{
    private readonly DbSession _db;

    public TransactionRepository(DbSession db)
    {
        _db = db;
    }

    public async Task<Transaction?> GetByIdAsync(int id)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetNewAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.* FROM Transactions t
              LEFT JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE t.UserId = @UserId AND t.IsDeleted = 0 AND t.IsPending = 0
                AND ta.Id IS NULL
              ORDER BY t.Date DESC",
            new { UserId = userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetTrackedAsync(int userId, DateTime fromDate)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.* FROM Transactions t
              WHERE t.UserId = @UserId AND t.IsDeleted = 0 AND t.Date >= @FromDate
                AND EXISTS (SELECT 1 FROM TransactionAssignments ta WHERE ta.TransactionId = t.Id)
              ORDER BY t.Date DESC",
            new { UserId = userId, FromDate = fromDate },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetDeletedAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE UserId = @UserId AND IsDeleted = 1 ORDER BY Date DESC",
            new { UserId = userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetPendingAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE UserId = @UserId AND IsPending = 1 AND IsDeleted = 0 ORDER BY Date DESC",
            new { UserId = userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.* FROM Transactions t
              INNER JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE ta.LineItemId = @LineItemId AND t.IsDeleted = 0
              ORDER BY t.Date DESC",
            new { LineItemId = lineItemId },
            _db.CurrentTransaction);
    }

    public async Task<TransactionCountsResponse> GetCountsAsync(int userId)
    {
        var trackedFromDate = DateTime.UtcNow.AddMonths(-2);
        using var multi = await _db.Connection.QueryMultipleAsync(
            @"SELECT COUNT(*) FROM Transactions t
              LEFT JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE t.UserId = @UserId AND t.IsDeleted = 0 AND t.IsPending = 0 AND ta.Id IS NULL;

              SELECT COUNT(*) FROM Transactions t
              WHERE t.UserId = @UserId AND t.IsDeleted = 0 AND t.Date >= @TrackedFromDate
                AND EXISTS (SELECT 1 FROM TransactionAssignments ta WHERE ta.TransactionId = t.Id);

              SELECT COUNT(*) FROM Transactions
              WHERE UserId = @UserId AND IsDeleted = 1;

              SELECT COUNT(*) FROM Transactions
              WHERE UserId = @UserId AND IsPending = 1 AND IsDeleted = 0;",
            new { UserId = userId, TrackedFromDate = trackedFromDate },
            _db.CurrentTransaction);

        return new TransactionCountsResponse
        {
            New = await multi.ReadSingleAsync<int>(),
            Tracked = await multi.ReadSingleAsync<int>(),
            Deleted = await multi.ReadSingleAsync<int>(),
            Pending = await multi.ReadSingleAsync<int>(),
        };
    }

    public async Task<int> CreateAsync(int userId, DateTime date, string description, decimal amount, string? notes, bool isManual)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, Date, Description, Amount, Notes, IsManual, CreatedAt, UpdatedAt)
              VALUES (@UserId, @Date, @Description, @Amount, @Notes, @IsManual, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { UserId = userId, Date = date, Description = description, Amount = amount, Notes = notes, IsManual = isManual },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, DateTime date, string description, decimal amount, string? notes)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET Date = @Date, Description = @Description, Amount = @Amount, Notes = @Notes, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = id, Date = date, Description = description, Amount = amount, Notes = notes },
            _db.CurrentTransaction);
    }

    public async Task UpdateNotesAsync(int id, string? notes)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET Notes = @Notes, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = id, Notes = notes },
            _db.CurrentTransaction);
    }

    public async Task SoftDeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task RestoreAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsDeleted = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }

    public async Task HardDeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM Transactions WHERE Id = @Id",
            new { Id = id },
            _db.CurrentTransaction);
    }
}
