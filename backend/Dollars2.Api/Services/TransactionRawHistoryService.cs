using Dollars2.Api.Models;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

/// <summary>
/// Reads one transaction's archived provider payloads: MSSQL resolves and authorizes the transaction,
/// then DynamoDB supplies every sighting of it.
/// </summary>
/// <remarks>
/// Deliberately not a method on <see cref="TransactionService"/>. Nothing else that service does has
/// any reason to know DynamoDB exists, and handing every budget operation a dependency on the archive
/// store would make an archive outage look like a much bigger blast radius than it is.
/// </remarks>
public class TransactionRawHistoryService
{
    /// <summary>Signals a failed archive read, which the controller answers with a 503.</summary>
    public const string ArchiveUnavailableCode = "ARCHIVE_UNAVAILABLE";

    private readonly TransactionRepository _transactionRepo;
    private readonly SyncArchiveRepository _archiveRepo;
    private readonly ILogger<TransactionRawHistoryService> _logger;

    public TransactionRawHistoryService(
        TransactionRepository transactionRepo,
        SyncArchiveRepository archiveRepo,
        ILogger<TransactionRawHistoryService> logger)
    {
        _transactionRepo = transactionRepo;
        _archiveRepo = archiveRepo;
        _logger = logger;
    }

    public async Task<DollarsApiResponse<List<RawHistoryEntryResponse>>> GetRawHistoryAsync(
        int transactionId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepo.GetByIdAsync(transactionId);

        // GetByIdAsync is not user-scoped in SQL, so the ownership check happens here — same shape as
        // every guarded method on TransactionService.
        if (transaction is null || transaction.UserId != userId)
        {
            return DollarsApiResponse<List<RawHistoryEntryResponse>>.Fail("Transaction not found.", "TRANSACTION_NOT_FOUND");
        }

        // A manually entered transaction never came from a provider, so there is nothing archived for
        // it. Empty rather than an error: the dialog tab should say "no provider data", not raise a
        // failure toast for a transaction that is behaving perfectly normally.
        if (transaction.AccountId is not int accountId || string.IsNullOrEmpty(transaction.ProviderTransactionId))
        {
            return DollarsApiResponse<List<RawHistoryEntryResponse>>.Success([]);
        }

        try
        {
            var entries = await _archiveRepo.GetTransactionHistoryAsync(
                userId,
                accountId,
                transaction.ProviderTransactionId,
                cancellationToken);

            return DollarsApiResponse<List<RawHistoryEntryResponse>>.Success(entries.ToList());
        }
        // Unlike the sync's best-effort write, a failure here is the whole answer the caller asked for,
        // so it is reported rather than swallowed. The guard keeps a client that hung up from being
        // logged as an archive outage — that cancellation is not DynamoDB's fault.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Could not read the sync archive for transaction {TransactionId} (account {AccountId}).",
                transactionId,
                accountId);

            return DollarsApiResponse<List<RawHistoryEntryResponse>>.Fail(
                "The sync archive is unavailable.",
                ArchiveUnavailableCode);
        }
    }
}
