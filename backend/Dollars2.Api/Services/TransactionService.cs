using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class TransactionService
{
    private readonly DbSession _dbSession;
    private readonly TransactionRepository _transactionRepo;
    private readonly TransactionAssignmentRepository _assignmentRepo;
    private readonly LineItemRepository _lineItemRepo;
    private readonly AccountRepository _accountRepo;

    public TransactionService(DbSession dbSession, TransactionRepository transactionRepo, TransactionAssignmentRepository assignmentRepo, LineItemRepository lineItemRepo, AccountRepository accountRepo)
    {
        _dbSession = dbSession;
        _transactionRepo = transactionRepo;
        _assignmentRepo = assignmentRepo;
        _lineItemRepo = lineItemRepo;
        _accountRepo = accountRepo;
    }

    public async Task<DollarsApiResponse<List<TransactionResponse>>> GetNewAsync(int userId)
    {
        var transactions = await _transactionRepo.GetNewAsync(userId);
        var responses = new List<TransactionResponse>();
        foreach (var t in transactions)
        {
            responses.Add(await BuildResponseAsync(t));
        }
        return DollarsApiResponse<List<TransactionResponse>>.Success(responses);
    }

    public async Task<DollarsApiResponse<List<TransactionResponse>>> GetTrackedAsync(int userId, DateTime fromDate)
    {
        var transactions = await _transactionRepo.GetTrackedAsync(userId, fromDate);
        var responses = new List<TransactionResponse>();
        foreach (var t in transactions)
        {
            responses.Add(await BuildResponseAsync(t));
        }
        return DollarsApiResponse<List<TransactionResponse>>.Success(responses);
    }

    public async Task<DollarsApiResponse<List<TransactionResponse>>> GetDeletedAsync(int userId)
    {
        var transactions = await _transactionRepo.GetDeletedAsync(userId);
        var responses = new List<TransactionResponse>();
        foreach (var t in transactions)
        {
            responses.Add(await BuildResponseAsync(t));
        }
        return DollarsApiResponse<List<TransactionResponse>>.Success(responses);
    }

    public async Task<DollarsApiResponse<List<TransactionResponse>>> GetPendingAsync(int userId)
    {
        var transactions = await _transactionRepo.GetPendingAsync(userId);
        var responses = new List<TransactionResponse>();
        foreach (var t in transactions)
        {
            responses.Add(await BuildResponseAsync(t));
        }
        return DollarsApiResponse<List<TransactionResponse>>.Success(responses);
    }

    public async Task<DollarsApiResponse<TransactionResponse>> CreateAsync(int userId, DateTime date, string description, decimal amount, string? notes)
    {
        if (amount == 0)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Amount cannot be zero.", "INVALID_AMOUNT");
        }

        var id = await _transactionRepo.CreateAsync(userId, date, description, amount, notes, true);
        var transaction = (await _transactionRepo.GetByIdAsync(id))!;
        return DollarsApiResponse<TransactionResponse>.Success(await BuildResponseAsync(transaction));
    }

    public async Task<DollarsApiResponse<TransactionResponse>> UpdateAsync(int id, int userId, DateTime date, string description, decimal amount, string? notes)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        if (!transaction.IsManual)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Cannot edit a synced transaction.", "CANNOT_EDIT_SYNCED");
        }

        if (amount == 0)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Amount cannot be zero.", "INVALID_AMOUNT");
        }

        await _transactionRepo.UpdateAsync(id, date, description, amount, notes);
        transaction = (await _transactionRepo.GetByIdAsync(id))!;
        return DollarsApiResponse<TransactionResponse>.Success(await BuildResponseAsync(transaction));
    }

    public async Task<DollarsApiResponse<bool>> SoftDeleteAsync(int id, int userId)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<bool>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        var assignments = await _assignmentRepo.GetByTransactionIdAsync(id);
        if (assignments.Any())
        {
            return DollarsApiResponse<bool>.Fail("Transaction must be unassigned before deleting.", "TRANSACTION_ASSIGNED");
        }

        await _transactionRepo.SoftDeleteAsync(id);
        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<bool>> RestoreAsync(int id, int userId)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<bool>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        if (!transaction.IsDeleted)
        {
            return DollarsApiResponse<bool>.Fail("Transaction is not deleted.", "NOT_DELETED");
        }

        await _transactionRepo.RestoreAsync(id);
        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<bool>> HardDeleteAsync(int id, int userId)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<bool>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        if (!transaction.IsManual)
        {
            return DollarsApiResponse<bool>.Fail("Only manual transactions can be permanently deleted.", "CANNOT_HARD_DELETE_SYNCED");
        }

        if (!transaction.IsDeleted)
        {
            return DollarsApiResponse<bool>.Fail("Transaction must be deleted before it can be permanently removed.", "NOT_DELETED");
        }

        _dbSession.BeginTransaction();
        try
        {
            await _assignmentRepo.DeleteByTransactionIdAsync(id);
            await _transactionRepo.HardDeleteAsync(id);
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        return DollarsApiResponse<bool>.Success(true);
    }

    public async Task<DollarsApiResponse<TransactionResponse>> AssignAsync(int id, int lineItemId, int userId)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        if (transaction.IsDeleted)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Cannot assign a deleted transaction.", "TRANSACTION_DELETED");
        }

        var lineItem = await _lineItemRepo.GetByIdAsync(lineItemId);
        if (lineItem is null || !await _lineItemRepo.IsOwnedByUserAsync(lineItemId, userId))
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Line item not found.", "LINE_ITEM_NOT_FOUND");
        }

        _dbSession.BeginTransaction();
        try
        {
            var existing = await _assignmentRepo.GetByTransactionIdAsync(id);
            if (existing.Any())
            {
                _dbSession.Rollback();
                return DollarsApiResponse<TransactionResponse>.Fail("Transaction is already assigned. Unassign first.", "ALREADY_ASSIGNED");
            }

            await _assignmentRepo.CreateAsync(id, lineItemId, transaction.Amount);
            _dbSession.Commit();
        }
        catch
        {
            _dbSession.Rollback();
            throw;
        }

        transaction = (await _transactionRepo.GetByIdAsync(id))!;
        return DollarsApiResponse<TransactionResponse>.Success(await BuildResponseAsync(transaction));
    }

    public async Task<DollarsApiResponse<TransactionResponse>> UnassignAsync(int id, int userId)
    {
        var transaction = await _transactionRepo.GetByIdAsync(id);
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        var existing = await _assignmentRepo.GetByTransactionIdAsync(id);
        if (!existing.Any())
        {
            return DollarsApiResponse<TransactionResponse>.Fail("Transaction is not assigned.", "NOT_ASSIGNED");
        }

        await _assignmentRepo.DeleteByTransactionIdAsync(id);
        transaction = (await _transactionRepo.GetByIdAsync(id))!;
        return DollarsApiResponse<TransactionResponse>.Success(await BuildResponseAsync(transaction));
    }

    private async Task<TransactionResponse> BuildResponseAsync(Transaction t)
    {
        var assignments = await _assignmentRepo.GetByTransactionIdAsync(t.Id);
        var assignmentResponses = new List<TransactionAssignmentResponse>();

        foreach (var a in assignments)
        {
            var lineItem = await _lineItemRepo.GetByIdAsync(a.LineItemId);
            assignmentResponses.Add(new TransactionAssignmentResponse
            {
                Id = a.Id,
                LineItemId = a.LineItemId,
                LineItemName = lineItem?.Name ?? "",
                Amount = a.Amount
            });
        }

        string? accountName = null;
        if (t.AccountId.HasValue)
        {
            var account = await _accountRepo.GetByIdAsync(t.AccountId.Value);
            accountName = account?.Name;
        }

        return new TransactionResponse
        {
            Id = t.Id,
            AccountId = t.AccountId,
            AccountName = accountName,
            Date = t.Date,
            Description = t.Description,
            Amount = t.Amount,
            Notes = t.Notes,
            IsDeleted = t.IsDeleted,
            IsPending = t.IsPending,
            IsManual = t.IsManual,
            Assignments = assignmentResponses
        };
    }
}
