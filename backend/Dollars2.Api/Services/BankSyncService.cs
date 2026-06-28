using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

public class BankSyncService
{
    private readonly DbSession _dbSession;
    private readonly AccountRepository _accountRepo;
    private readonly TransactionRepository _transactionRepo;
    private readonly SyncLogRepository _syncLogRepo;
    private readonly IBankSyncProvider _bankSyncProvider;
    private readonly IConfiguration _config;
    private readonly SyncLockService _syncLock;
    private readonly ILogger<BankSyncService> _logger;

    public BankSyncService(
        DbSession dbSession,
        AccountRepository accountRepo,
        TransactionRepository transactionRepo,
        SyncLogRepository syncLogRepo,
        IBankSyncProvider bankSyncProvider,
        IConfiguration config,
        SyncLockService syncLock,
        ILogger<BankSyncService> logger)
    {
        _dbSession = dbSession;
        _accountRepo = accountRepo;
        _transactionRepo = transactionRepo;
        _syncLogRepo = syncLogRepo;
        _bankSyncProvider = bankSyncProvider;
        _config = config;
        _syncLock = syncLock;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncResult>> SyncForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountRepo.GetByUserIdAsync(userId);
        var results = new List<SyncResult>();

        // TODO: For users with multiple SimpleFIN accounts, this makes one HTTP request per
        // account even though the SimpleFIN API returns all accounts in a single response.
        // Fix when adding a second account: fetch once per provider per user and distribute.
        foreach (var account in accounts)
        {
            if (account.SourceType == SyncConstants.SourceTypeManual)
            {
                continue;
            }
            _logger.LogInformation("Syncing account {AccountId} ({AccountName}) for user {UserId}", account.Id, account.Name, userId);
            var result = await SyncAccountAsync(account, cancellationToken);
            results.Add(result);
            _logger.LogInformation("Completed sync for account {AccountId} ({AccountName}): {Status}, {TransactionCount} new transactions", account.Id, account.Name, result.Status, result.TransactionCount);
        }

        return results;
    }

    public async Task SyncAllUsersAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default)
    {
        var userIdList = userIds.ToList();
        _logger.LogInformation("Starting bank sync for {UserCount} users", userIdList.Count);
        foreach (var userId in userIdList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_syncLock.TryAcquire(userId))
            {
                _logger.LogInformation("Skipping scheduled sync for user {UserId} — sync already in progress", userId);
                continue;
            }
            try
            {
                await SyncForUserAsync(userId, cancellationToken);
            }
            finally
            {
                _syncLock.Release(userId);
            }
        }
        _logger.LogInformation("Completed bank sync for {UserCount} users", userIdList.Count);
    }

    private async Task<SyncResult> SyncAccountAsync(Account account, CancellationToken cancellationToken)
    {
        var provider = GetProvider(account.SourceType);
        if (provider is null)
        {
            _logger.LogWarning("No provider found for account {AccountId} ({AccountName}) with source type {SourceType}", account.Id, account.Name, account.SourceType);
            return new SyncResult
            {
                AccountId = account.Id,
                AccountName = account.Name,
                Status = SyncConstants.StatusSkipped,
                TransactionCount = 0,
            };
        }

        try
        {
            var lastSync = await _syncLogRepo.GetLastSuccessfulAsync(account.Id);
            _logger.LogInformation("Last successful sync for account {AccountId} ({AccountName}) was at {LastSyncTime}", account.Id, account.Name, lastSync?.SyncedAt);
            // Overlap by 12 hours to avoid missing transactions posted near the boundary.
            // Deduplication via ProviderTransactionId prevents double-imports.
            var since = (lastSync?.SyncedAt ?? DateTime.UtcNow.AddDays(-30)).AddHours(-12);

            var transactions = (await provider.FetchTransactionsAsync(account, since, cancellationToken)).ToList();
            var count = 0;

            _dbSession.BeginTransaction();
            try
            {
                foreach (var t in transactions)
                {
                    var existing = await _transactionRepo.GetByProviderTransactionIdAsync(account.Id, t.ProviderTransactionId);
                    if (existing is null)
                    {
                        await _transactionRepo.CreateFromSyncAsync(
                            account.UserId, account.Id, t.ProviderTransactionId,
                            t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.IsPending);
                        count++;
                    }
                    else if (!existing.IsDeleted)
                    {
                        var changed = (existing.IsPending && !t.IsPending)
                                   || existing.Amount != t.Amount
                                   || existing.Description != t.Description
                                   || existing.Payee != t.Payee
                                   || existing.Memo != t.Memo
                                   || existing.Date.Date != t.Date;
                        if (changed)
                        {
                            await _transactionRepo.UpdateFromSyncAsync(existing.Id, t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.IsPending);
                        }
                    }
                }

                await _syncLogRepo.CreateAsync(account.Id, SyncConstants.StatusSuccess, count, null);
                _dbSession.Commit();
            }
            catch
            {
                _dbSession.Rollback();
                throw;
            }

            return new SyncResult
            {
                AccountId = account.Id,
                AccountName = account.Name,
                Status = SyncConstants.StatusSuccess,
                TransactionCount = count,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for account {AccountId} ({AccountName})", account.Id, account.Name);
            try
            {
                await _syncLogRepo.CreateAsync(account.Id, SyncConstants.StatusFailure, 0, ex.Message);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to write failure log for account {AccountId}", account.Id);
            }

            return new SyncResult
            {
                AccountId = account.Id,
                AccountName = account.Name,
                Status = SyncConstants.StatusFailure,
                TransactionCount = 0,
                ErrorMessage = ex.Message,
            };
        }
    }

    private IBankSyncProvider? GetProvider(string sourceType)
    {
        return sourceType switch
        {
            SyncConstants.SourceTypeSimpleFin when _config.GetValue<bool>("SimpleFin:Enabled") => _bankSyncProvider,
            _ => null,
        };
    }
}
