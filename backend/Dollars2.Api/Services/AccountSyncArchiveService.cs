using System.Globalization;
using Dollars2.Api.Models;
using Dollars2.Api.Repositories;

namespace Dollars2.Api.Services;

/// <summary>
/// Browses one account's sync archive, newest run first: MSSQL resolves and authorizes the account,
/// then DynamoDB supplies a page of runs with every payload they archived.
/// </summary>
/// <remarks>
/// Separate from <see cref="AccountService"/> for the same reason
/// <see cref="TransactionRawHistoryService"/> is separate from <see cref="TransactionService"/> —
/// nothing else either service does has any reason to know DynamoDB exists, and an archive outage should
/// not look like it could take the account list down with it.
/// </remarks>
public class AccountSyncArchiveService
{
    /// <summary>Signals a failed archive read, which the controller answers with a 503.</summary>
    /// <remarks>
    /// The same wire code <see cref="TransactionRawHistoryService.ArchiveUnavailableCode"/> uses — a dead
    /// archive looks identical from either read path. Declared here rather than shared so an accounts
    /// endpoint does not take a dependency on a transactions service to name an error.
    /// </remarks>
    public const string ArchiveUnavailableCode = "ARCHIVE_UNAVAILABLE";

    /// <summary>Signals a <c>before</c> value that is not an instant, which the controller answers with a 400.</summary>
    public const string InvalidCursorCode = "INVALID_CURSOR";

    /// <summary>Sync runs per page when the caller does not ask for a size.</summary>
    public const int DefaultRunLimit = 50;

    /// <summary>
    /// Ceiling on runs per page. Every run comes back with its payloads inline, so this is the only thing
    /// standing between one request and the account's entire archive.
    /// </summary>
    public const int MaxRunLimit = 100;

    private readonly AccountRepository _accountRepo;
    private readonly SyncArchiveRepository _archiveRepo;
    private readonly ILogger<AccountSyncArchiveService> _logger;

    public AccountSyncArchiveService(
        AccountRepository accountRepo,
        SyncArchiveRepository archiveRepo,
        ILogger<AccountSyncArchiveService> logger)
    {
        _accountRepo = accountRepo;
        _archiveRepo = archiveRepo;
        _logger = logger;
    }

    /// <param name="before">
    /// The cursor from the previous page's <c>nextBefore</c>, as the API serialized it. Null or empty
    /// starts from the newest run.
    /// </param>
    /// <param name="limit">
    /// Requested runs per page. Clamped rather than rejected: a caller asking for more than the archive
    /// will hand out in one go is asking for the maximum, not making a mistake.
    /// </param>
    public async Task<DollarsApiResponse<AccountSyncArchiveResponse>> GetArchivePageAsync(
        int accountId,
        int userId,
        string? before,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepo.GetByIdAsync(accountId);

        // GetByIdAsync is not user-scoped in SQL, so the ownership check happens here — same shape as
        // every guarded method on TransactionService.
        if (account is null || account.UserId != userId)
        {
            return DollarsApiResponse<AccountSyncArchiveResponse>.Fail("Account not found.", "ACCOUNT_NOT_FOUND");
        }

        if (!TryParseCursor(before, out var cursor))
        {
            return DollarsApiResponse<AccountSyncArchiveResponse>.Fail(
                "The 'before' cursor is not a valid instant.",
                InvalidCursorCode);
        }

        var runLimit = Math.Clamp(limit ?? DefaultRunLimit, 1, MaxRunLimit);

        try
        {
            var page = await _archiveRepo.GetAccountArchivePageAsync(
                userId,
                accountId,
                cursor,
                runLimit,
                cancellationToken);

            return DollarsApiResponse<AccountSyncArchiveResponse>.Success(page);
        }
        // Unlike the sync's best-effort write, a failure here is the whole answer the caller asked for,
        // so it is reported rather than swallowed. The guard keeps a client that hung up from being
        // logged as an archive outage — that cancellation is not DynamoDB's fault.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the sync archive for account {AccountId}.", accountId);

            return DollarsApiResponse<AccountSyncArchiveResponse>.Fail(
                "The sync archive is unavailable.",
                ArchiveUnavailableCode);
        }
    }

    /// <summary>
    /// Parses the cursor a previous page handed out. Taken as a string rather than bound as a
    /// <see cref="DateTime"/> so a malformed value comes back as this endpoint's own error instead of
    /// MVC's model-binding response, and so the kind is settled here rather than by the binder.
    /// </summary>
    private static bool TryParseCursor(string? before, out DateTime? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(before))
        {
            return true;
        }

        if (!DateTime.TryParse(
            before,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed))
        {
            return false;
        }

        cursor = parsed;
        return true;
    }
}
