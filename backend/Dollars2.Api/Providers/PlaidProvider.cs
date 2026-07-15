using System.Text.Json;
using Dollars2.Api.Models;
using Going.Plaid;
using Going.Plaid.Transactions;
using PlaidTransaction = Going.Plaid.Entity.Transaction;
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

    public string SourceType => "Plaid";

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

    public async Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts,
        DateTime? since,
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
            return accounts.ToDictionary(
                a => a.Id,
                a => new ProviderSyncResult(
                    Array.Empty<SyncedTransaction>(),
                    Array.Empty<string>(),
                    null,
                    "Plaid:ClientId / Plaid:Secret are not configured."));
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
        var cursor = ResolveGroupCursor(
            parsed.Select(p => (p.Details?.AccountId, p.Details?.Cursor)).ToList());
        if (cursor is null && accounts.Count > 1)
        {
            _logger.LogInformation(
                "Plaid cursors for accounts {AccountIds} are not converged; performing a full resync to reconcile.",
                string.Join(", ", accounts.Select(a => a.Id)));
        }

        var added = new List<PlaidTransaction>();
        var modified = new List<PlaidTransaction>();
        var removed = new List<RemovedTransaction>();
        bool hasMore;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest
            {
                Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                Count = 500,
            });

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Error;
                throw new InvalidOperationException(
                    $"Plaid /transactions/sync failed: {error?.ErrorCode} - {error?.ErrorMessage}");
            }

            added.AddRange(response.Added);
            modified.AddRange(response.Modified);
            removed.AddRange(response.Removed);

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

            var upserts = added.Concat(modified)
                .Where(t => MatchesAccount(t.AccountId))
                .Select(MapTransaction)
                .ToList();

            var updatedJson = JsonSerializer.Serialize(new PlaidConnectionDetails
            {
                AccessToken = accessToken,
                AccountId = details?.AccountId,
                Cursor = cursor,
            });

            results[account.Id] = new ProviderSyncResult(upserts, removedIds, updatedJson);
        }

        return results;
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

    private static SyncedTransaction MapTransaction(PlaidTransaction t)
    {
        // Plaid amounts are positive for outflow (money leaving the account); our
        // convention is negative for expenses, positive for income — so negate.
        var amount = -(t.Amount ?? 0);
        var date = t.Date?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow.Date;
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
