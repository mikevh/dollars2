using System.Text.Json;
using Dollars2.Api.Models;
using Dollars2.Api.Services;
using Going.Plaid;
using Going.Plaid.Transactions;
using PlaidTransaction = Going.Plaid.Entity.Transaction;
using PlaidAccount = Going.Plaid.Entity.Account;
using RemovedTransaction = Going.Plaid.Entity.RemovedTransaction;

namespace Dollars2.Api.Providers;

public class PlaidProvider : IBankSyncProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PlaidProvider> _logger;

    private readonly string _clientId;
    private readonly string _secret;
    private readonly Going.Plaid.Environment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PlaidProvider(IConfiguration config, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PlaidProvider>();

        Enabled = config.GetValue<bool>("Plaid:Enabled");
        _clientId = config["Plaid:ClientId"] ?? "";
        _secret = config["Plaid:Secret"] ?? "";
        _environment = Enum.TryParse<Going.Plaid.Environment>(config["Plaid:Environment"], ignoreCase: true, out var env)
            ? env
            : Going.Plaid.Environment.Production;

        var hours = config.GetValue<double?>("Plaid:MinSyncIntervalHours") ?? 6;
        MinSyncInterval = TimeSpan.FromHours(hours);
    }

    public string SourceType => SyncConstants.SourceTypePlaid;

    public bool Enabled { get; }

    public TimeSpan MinSyncInterval { get; }

    public string GetConnectionKey(Account account)
    {
        var details = JsonSerializer.Deserialize<PlaidConnectionDetails>(
            account.ConnectionDetailsJson ?? "{}",
            JsonOptions);

        // The Plaid Item access token backs one /transactions/sync stream shared by every account
        // in the Item. Fall back to a per-account key when the token is missing so a broken account
        // is synced (and fails) on its own rather than derailing a healthy group.
        return string.IsNullOrEmpty(details?.AccessToken)
            ? $"account:{account.Id}"
            : details.AccessToken;
    }

    public async Task<ProviderFetchResult> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts,
        DateTime? since,
        bool fullResync = false,
        CancellationToken cancellationToken = default)
    {
        // Plaid API credentials come from configuration (the .env file). Without them no upstream call
        // can succeed, so fail the whole connection group up front with a single clear error rather than
        // doing work and throwing deep in the sync (which logs a stack trace per account).
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_secret))
        {
            _logger.LogError(
                "Plaid sync skipped for accounts {AccountIds}: Plaid:ClientId / Plaid:Secret are not configured.",
                string.Join(", ", accounts.Select(a => a.Id)));
            IReadOnlyDictionary<int, ProviderSyncResult> unconfigured = accounts.ToDictionary(
                a => a.Id,
                a => new ProviderSyncResult(
                    Array.Empty<SyncedTransaction>(),
                    Array.Empty<string>(),
                    null,
                    "Plaid:ClientId / Plaid:Secret are not configured."));
            return new ProviderFetchResult(unconfigured, Array.Empty<string>());
        }

        // All accounts share one access token (that's the connection key), but each carries its own
        // Plaid account_id filter and its own copy of the cursor.
        var parsed = accounts
            .Select(a => (Account: a, Details: JsonSerializer.Deserialize<PlaidConnectionDetails>(
                a.ConnectionDetailsJson ?? "{}", JsonOptions)))
            .ToList();

        var accessToken = parsed
            .Select(p => p.Details?.AccessToken)
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));

        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Plaid connection for accounts {AccountIds} has no access token.",
                string.Join(", ", accounts.Select(a => a.Id)));
            throw new InvalidOperationException("Plaid connection is missing an access token.");
        }

        var client = new PlaidClient(
            _environment,
            _clientId,
            _secret,
            accessToken,
            _httpClientFactory,
            _loggerFactory.CreateLogger<PlaidClient>(),
            ApiVersion.v20200914);

        // The cursor belongs to the Item, not the account. Only reuse a stored cursor when every
        // account in the group already agrees on the same non-empty value; otherwise start from
        // scratch so no account's history is missed (ProviderTransactionId dedup absorbs the
        // re-fetch). After a successful run all synced accounts are written the same advanced cursor
        // and converge.
        //
        // A user-initiated full resync (`fullResync`) forces the cursor to null so /transactions/sync
        // re-streams the whole Item from scratch. Plaid has no date-window parameter, so `since` cannot
        // narrow it — resetting the cursor is the only way to honor the resync request; dedup absorbs
        // the re-fetch and the advanced cursor is persisted as usual on success.
        var cursor = SelectGroupCursor(
            fullResync, parsed.Select(p => (p.Details?.AccountId, p.Details?.Cursor)).ToList());
        if (fullResync)
        {
            _logger.LogInformation(
                "Full resync requested for Plaid accounts {AccountIds}; ignoring stored cursor and re-streaming the Item.",
                string.Join(", ", accounts.Select(a => a.Id)));
        }
        else if (cursor is null && accounts.Count > 1)
        {
            _logger.LogInformation(
                "Plaid cursors for accounts {AccountIds} are not converged; performing a full resync to reconcile.",
                string.Join(", ", accounts.Select(a => a.Id)));
        }

        var added = new List<PlaidTransaction>();
        var modified = new List<PlaidTransaction>();
        var removed = new List<RemovedTransaction>();
        // Every /transactions/sync page carries the Item's current account snapshot (including balances);
        // keep the latest so we can record each account's balance after the stream is drained.
        IReadOnlyList<PlaidAccount> accountSnapshot = Array.Empty<PlaidAccount>();
        var rawResponseBodies = new List<string>();
        bool hasMore;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Going.Plaid's generated client exposes no CancellationToken overload on TransactionsSyncAsync
            // (or anywhere else in its surface), so an in-flight page can't be aborted mid-call — only
            // between pages, via the check above.
            var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest
            {
                Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                Count = 500,
                // Makes Going.Plaid hand back the response body verbatim on ResponseBase.RawJson, which
                // is the only way to archive what Plaid actually sent.
                ShowRawJson = true,
            });

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Error;
                throw new InvalidOperationException(
                    $"Plaid /transactions/sync failed: {error?.ErrorCode} - {error?.ErrorMessage}");
            }
            _logger.LogTrace("Plaid raw response {response}", response.RawJson);

            if (!string.IsNullOrEmpty(response.RawJson))
            {
                rawResponseBodies.Add(response.RawJson);
            }
            else
            {
                _logger.LogWarning("Plaid /transactions/sync returned no raw JSON despite ShowRawJson; archiving nothing for this page.");
            }

            added.AddRange(response.Added);
            modified.AddRange(response.Modified);
            removed.AddRange(response.Removed);
            if (response.Accounts.Count > 0)
            {
                accountSnapshot = response.Accounts;
            }

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }
        while (hasMore);

        // Removed items from /transactions/sync do not carry an account_id at the pinned API version
        // (ApiVersion.v20200914), so they cannot be attributed to a specific account here. Applying the
        // full removed set to every account in the Item is safe — and correct regardless of whether a
        // future API version starts populating account_id — because SoftDeleteByProviderTransactionIdAsync
        // is scoped by account.Id + provider transaction id and Plaid transaction ids are globally unique,
        // so each account only soft-deletes rows that are actually its own.
        var removedIds = CollectRemovedTransactionIds(removed);

        var results = new Dictionary<int, ProviderSyncResult>();
        foreach (var (account, details) in parsed)
        {
            // When several stored accounts share one access token, each must carry its own account_id
            // to attribute transactions. A blank account_id matches every transaction in the Item and
            // would pull siblings' activity into this account, so fail it rather than corrupt data. A
            // lone account on a token is unambiguous, so an empty account_id is still allowed there.
            var sharedTokenError = SharedTokenMissingAccountIdError(accounts.Count, details?.AccountId);
            if (sharedTokenError is not null)
            {
                _logger.LogWarning(
                    "Plaid account {AccountId} shares an access token with other accounts but has no account_id; skipping to avoid importing siblings' transactions.",
                    account.Id);
                results[account.Id] = new ProviderSyncResult(
                    Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null, sharedTokenError);
                continue;
            }

            bool MatchesAccount(string? plaidAccountId) =>
                string.IsNullOrEmpty(details?.AccountId) || plaidAccountId == details.AccountId;

            var upserts = new List<SyncedTransaction>();
            foreach (var t in added.Concat(modified).Where(t => MatchesAccount(t.AccountId)))
            {
                if ((t.TransactionId?.Length ?? 0) > TransactionText.MaxLength)
                {
                    // ProviderTransactionId is the dedup key (UX_Transactions_Provider); truncating could
                    // collide two distinct transactions, so skip rather than clamp.
                    _logger.LogWarning("Skipping transaction {TransactionId} for account {AccountId}: id exceeds {MaxLength} characters.", t.TransactionId, account.Id, TransactionText.MaxLength);
                    continue;
                }

                upserts.Add(MapTransaction(t));
            }

            var updatedJson = JsonSerializer.Serialize(new PlaidConnectionDetails
            {
                AccessToken = accessToken,
                AccountId = details?.AccountId,
                Cursor = cursor,
            });

            var balance = ExtractCurrentBalance(accountSnapshot, details?.AccountId);

            results[account.Id] = new ProviderSyncResult(
                upserts,
                removedIds,
                updatedJson,
                Balance: balance);
        }

        return new ProviderFetchResult(results, rawResponseBodies);
    }

    /// <summary>
    /// Returns a failure message when an account cannot be safely attributed within its connection
    /// group, or null when it is fine to sync. A blank account_id is only a problem when more than one
    /// stored account shares the access token, because then it would match (and import) every
    /// sibling's transactions; a lone account on a token is unambiguous.
    /// </summary>
    internal static string? SharedTokenMissingAccountIdError(int accountCount, string? accountId) =>
        accountCount > 1 && string.IsNullOrEmpty(accountId)
            ? "Plaid connection details are missing an account_id, which is required when multiple accounts share an access token."
            : null;

    /// <summary>
    /// Chooses the cursor to start /transactions/sync from. A user-initiated full resync forces a null
    /// cursor so the whole Item re-streams from scratch (Plaid has no date-window parameter); otherwise
    /// the stored cursor is reused only when the group has converged (<see cref="ResolveGroupCursor"/>).
    /// </summary>
    internal static string? SelectGroupCursor(bool fullResync, IReadOnlyList<(string? AccountId, string? Cursor)> group) =>
        fullResync ? null : ResolveGroupCursor(group);

    /// <summary>
    /// Chooses the cursor to sync the Plaid Item from, or null to force a full resync. The cursor is
    /// per-Item, mirrored onto each account, so a run reuses it only when every account it can actually
    /// sync already agrees on the same non-empty value. Accounts that will be skipped this run (a blank
    /// account_id on a shared token) can never advance their cursor, so they are excluded from the
    /// decision — otherwise a single persistently misconfigured account would force a full resync of its
    /// healthy siblings on every sync. A syncable account with an empty or divergent cursor (a new
    /// account, or one recovering from a failed persist) still forces the full resync it needs.
    /// </summary>
    internal static string? ResolveGroupCursor(IReadOnlyList<(string? AccountId, string? Cursor)> group)
    {
        var syncableCursors = group
            .Where(a => SharedTokenMissingAccountIdError(group.Count, a.AccountId) is null)
            .Select(a => a.Cursor)
            .ToList();

        var converged = syncableCursors.Count > 0
            && syncableCursors.All(c => !string.IsNullOrEmpty(c))
            && syncableCursors.Distinct().Count() == 1;

        return converged ? syncableCursors[0] : null;
    }

    /// <summary>
    /// Collects the provider transaction ids of every removed item in the Item's sync response,
    /// dropping any without an id. Removed items are deliberately not filtered by account_id: at the
    /// pinned API version they carry none, and the downstream soft-delete is already scoped by
    /// account.Id + provider transaction id, so an id belonging to a sibling account is a no-op there.
    /// </summary>
    internal static List<string> CollectRemovedTransactionIds(IEnumerable<RemovedTransaction> removed) =>
        removed
            .Select(r => r.TransactionId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();

    /// <summary>
    /// Finds a stored account's entry in the Item's account snapshot. When the stored account carries a
    /// Plaid account_id, the matching snapshot entry is used; a lone account with a blank account_id
    /// (unambiguous on its token) falls back to the single snapshot entry. Returns null when there is no
    /// unambiguous match.
    /// </summary>
    private static T? MatchSnapshotEntry<T>(IReadOnlyList<T> snapshot, Func<T, string?> accountIdOf, string? accountId)
        where T : class
    {
        if (!string.IsNullOrEmpty(accountId))
        {
            return snapshot.FirstOrDefault(a => accountIdOf(a) == accountId);
        }

        return snapshot.Count == 1 ? snapshot[0] : null;
    }

    /// <summary>
    /// Picks the current balance for a stored account from the Item's account snapshot, or null when
    /// <see cref="MatchSnapshotEntry"/> finds no unambiguous match or the provider reported no current
    /// balance.
    /// </summary>
    internal static decimal? ExtractCurrentBalance(IReadOnlyList<PlaidAccount> accounts, string? accountId) =>
        MatchSnapshotEntry(accounts, a => a.AccountId, accountId)?.Balances?.Current;

    internal static SyncedTransaction MapTransaction(PlaidTransaction t)
    {
        // Plaid amounts are positive for outflow (money leaving the account); our
        // convention is negative for expenses, positive for income — so negate.
        var amount = -(t.Amount ?? 0);
        var date = t.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
#pragma warning disable CS0612 // Transaction.Name is obsolete but remains the best fallback label
        var payee = t.MerchantName ?? t.Name ?? "";
        var description = t.OriginalDescription ?? t.Name ?? "";
#pragma warning restore CS0612

        return new SyncedTransaction(
            t.TransactionId ?? "",
            date,
            description,
            payee,
            "",
            amount,
            t.Pending ?? false);
    }
}
