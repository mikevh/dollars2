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
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetNewAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.Id, t.AccountId, t.UserId, t.ProviderTransactionId, t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.Notes, t.IsDeleted, t.IsPending, t.IsManual, t.CreatedAt, t.UpdatedAt
              FROM Transactions t
              LEFT JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE t.UserId = @userId AND t.IsDeleted = 0 AND t.IsPending = 0
                AND ta.Id IS NULL
              ORDER BY t.Date DESC",
            new { userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetTrackedAsync(int userId, DateTime fromDate)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.Id, t.AccountId, t.UserId, t.ProviderTransactionId, t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.Notes, t.IsDeleted, t.IsPending, t.IsManual, t.CreatedAt, t.UpdatedAt
              FROM Transactions t
              WHERE t.UserId = @userId AND t.IsDeleted = 0 AND t.Date >= @fromDate
                AND EXISTS (SELECT 1 FROM TransactionAssignments ta WHERE ta.TransactionId = t.Id)
              ORDER BY t.Date DESC",
            new { userId, fromDate },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetDeletedAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE UserId = @userId AND IsDeleted = 1 ORDER BY Date DESC",
            new { userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetPendingAsync(int userId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE UserId = @userId AND IsPending = 1 AND IsDeleted = 0 ORDER BY Date DESC",
            new { userId },
            _db.CurrentTransaction);
    }

    public async Task<IEnumerable<Transaction>> GetByLineItemIdAsync(int lineItemId)
    {
        return await _db.Connection.QueryAsync<Transaction>(
            @"SELECT t.Id, t.AccountId, t.UserId, t.ProviderTransactionId, t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.Notes, t.IsDeleted, t.IsPending, t.IsManual, t.CreatedAt, t.UpdatedAt
              FROM Transactions t
              INNER JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE ta.LineItemId = @lineItemId AND t.IsDeleted = 0
              ORDER BY t.Date DESC",
            new { lineItemId },
            _db.CurrentTransaction);
    }

    public async Task<TransactionCountsResponse> GetCountsAsync(int userId)
    {
        var trackedFromDate = DateTime.UtcNow.AddMonths(-2);
        using var multi = await _db.Connection.QueryMultipleAsync(
            @"SELECT COUNT(*) FROM Transactions t
              LEFT JOIN TransactionAssignments ta ON ta.TransactionId = t.Id
              WHERE t.UserId = @userId AND t.IsDeleted = 0 AND t.IsPending = 0 AND ta.Id IS NULL;

              SELECT COUNT(*) FROM Transactions t
              WHERE t.UserId = @userId AND t.IsDeleted = 0 AND t.Date >= @trackedFromDate
                AND EXISTS (SELECT 1 FROM TransactionAssignments ta WHERE ta.TransactionId = t.Id);

              SELECT COUNT(*) FROM Transactions
              WHERE UserId = @userId AND IsDeleted = 1;

              SELECT COUNT(*) FROM Transactions
              WHERE UserId = @userId AND IsPending = 1 AND IsDeleted = 0;",
            new { userId, trackedFromDate },
            _db.CurrentTransaction);

        return new TransactionCountsResponse
        {
            New = await multi.ReadSingleAsync<int>(),
            Tracked = await multi.ReadSingleAsync<int>(),
            Deleted = await multi.ReadSingleAsync<int>(),
            Pending = await multi.ReadSingleAsync<int>(),
        };
    }

    public async Task<int> CreateAsync(int userId, DateTime date, string description, string payee, string memo, decimal amount, string? notes, bool isManual)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, Date, Description, Payee, Memo, Amount, Notes, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, @date, @description, @payee, @memo, @amount, @notes, @isManual, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, date, description, payee, memo, amount, notes, isManual },
            _db.CurrentTransaction);
    }

    public async Task UpdateAsync(int id, DateTime date, string description, decimal amount, string? notes)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET Date = @date, Description = @description, Amount = @amount, Notes = @notes, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, date, description, amount, notes },
            _db.CurrentTransaction);
    }

    public async Task UpdateNotesAsync(int id, string? notes)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET Notes = @notes, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, notes },
            _db.CurrentTransaction);
    }

    public async Task SoftDeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task RestoreAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsDeleted = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task HardDeleteAsync(int id)
    {
        await _db.Connection.ExecuteAsync(
            "DELETE FROM Transactions WHERE Id = @id",
            new { id },
            _db.CurrentTransaction);
    }

    public async Task<Transaction?> GetByProviderTransactionIdAsync(int accountId, string providerTransactionId)
    {
        return await _db.Connection.QuerySingleOrDefaultAsync<Transaction>(
            "SELECT Id, AccountId, UserId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, Notes, IsDeleted, IsPending, IsManual, CreatedAt, UpdatedAt FROM Transactions WHERE AccountId = @accountId AND ProviderTransactionId = @providerTransactionId",
            new { accountId, providerTransactionId },
            _db.CurrentTransaction);
    }

    public async Task<int> CreateFromSyncAsync(
        int userId, 
        int accountId, 
        string providerTransactionId, 
        DateTime date, 
        string description, 
        string payee,
        string memo,
        decimal amount, 
        bool isPending)
    {
        return await _db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions 
    (UserId, AccountId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, IsPending, IsManual, CreatedAt, UpdatedAt) VALUES 
   (@userId, @accountId, @providerTransactionId, @date, @description, @payee, @memo, @amount, @isPending, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, accountId, providerTransactionId, date, description, payee, memo, amount, isPending },
            _db.CurrentTransaction);
    }

    public async Task UpdateFromSyncAsync(int id, DateTime date, string description, string payee, string memo, decimal amount, bool isPending)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET Date = @date, Description = @description, Payee = @payee, Memo = @memo, Amount = @amount, IsPending = @isPending, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, date, description, payee, memo, amount, isPending },
            _db.CurrentTransaction);
    }

    public async Task UpdatePendingStatusAsync(int id, bool isPending)
    {
        await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsPending = @isPending, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id",
            new { id, isPending },
            _db.CurrentTransaction);
    }

    public async Task<int> SoftDeleteByProviderTransactionIdAsync(int accountId, string providerTransactionId)
    {
        return await _db.Connection.ExecuteAsync(
            "UPDATE Transactions SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE AccountId = @accountId AND ProviderTransactionId = @providerTransactionId AND IsDeleted = 0",
            new { accountId, providerTransactionId },
            _db.CurrentTransaction);
    }
}
