using System.Text.Json;
using Dollars2.Api.Models;
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

        // Each typed transaction is carried alongside the raw JSON object it was deserialized from, so
        // the sync archive can hold the bytes Plaid sent rather than a re-serialization of Going.Plaid's
        // DTOs (which would silently drop every field the library does not model).
        var added = new List<(PlaidTransaction Transaction, string RawJson)>();
        var modified = new List<(PlaidTransaction Transaction, string RawJson)>();
        var removed = new List<RemovedTransaction>();
        // Every /transactions/sync page carries the Item's current account snapshot (including balances);
        // keep the latest so we can record each account's balance after the stream is drained. The raw
        // snapshot is replaced in lockstep with the typed one, so AccountMetadataJson and Balance always
        // describe the same page rather than being stitched together from two different ones.
        IReadOnlyList<PlaidAccount> accountSnapshot = Array.Empty<PlaidAccount>();
        IReadOnlyList<RawPlaidAccount> rawAccountSnapshot = Array.Empty<RawPlaidAccount>();
        bool hasMore;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest
            {
                Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                Count = 500,
                // Makes Going.Plaid hand back the response body verbatim on ResponseBase.RawJson, which
                // is the only way to archive what Plaid actually sent — the typed entities expose no
                // route back to their own JSON.
                ShowRawJson = true,
            });

            if (!response.IsSuccessStatusCode)
            {
                var error = response.Error;
                throw new InvalidOperationException(
                    $"Plaid /transactions/sync failed: {error?.ErrorCode} - {error?.ErrorMessage}");
            }

            var rawPage = ReadRawPage(response.RawJson, response.Added.Count, response.Modified.Count, _logger);

            // Zip rather than a lookup by transaction_id: a transaction can appear in `added` on one page
            // and `modified` on another with different content, and an id-keyed map could only hold one of
            // the two — pairing one typed entry with the other's bytes. ReadRawPage guarantees these lists
            // are the same length as the typed ones, so no transaction is ever dropped by the zip.
            added.AddRange(response.Added.Zip(rawPage.Added, (t, raw) => (Transaction: t, RawJson: raw)));
            modified.AddRange(response.Modified.Zip(rawPage.Modified, (t, raw) => (Transaction: t, RawJson: raw)));
            removed.AddRange(response.Removed);
            if (response.Accounts.Count > 0)
            {
                accountSnapshot = response.Accounts;
                rawAccountSnapshot = rawPage.Accounts;
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

            var upserts = added.Concat(modified)
                .Where(t => MatchesAccount(t.Transaction.AccountId))
                .Select(t => MapTransaction(t.Transaction, t.RawJson))
                .ToList();

            var updatedJson = JsonSerializer.Serialize(new PlaidConnectionDetails
            {
                AccessToken = accessToken,
                AccountId = details?.AccountId,
                Cursor = cursor,
            });

            var balance = ExtractCurrentBalance(accountSnapshot, details?.AccountId);

            // ErrorsJson is deliberately left empty for Plaid rather than forgotten. A successful
            // /transactions/sync response carries no errors collection to capture (there is no analogue to
            // SimpleFIN's errlist), and the one failure path throws above, before any result exists to
            // attach errors to. Filling it with our own text rewrapped as JSON is the opposite of what the
            // field documents — provider-reported errors, verbatim.
            results[account.Id] = new ProviderSyncResult(
                upserts,
                removedIds,
                updatedJson,
                Balance: balance,
                AccountMetadataJson: ExtractRawAccountMetadata(rawAccountSnapshot, details?.AccountId));
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
    /// <summary>
    /// Chooses the cursor to start /transactions/sync from. A user-initiated full resync forces a null
    /// cursor so the whole Item re-streams from scratch (Plaid has no date-window parameter); otherwise
    /// the stored cursor is reused only when the group has converged (<see cref="ResolveGroupCursor"/>).
    /// </summary>
    internal static string? SelectGroupCursor(bool fullResync, IReadOnlyList<(string? AccountId, string? Cursor)> group) =>
        fullResync ? null : ResolveGroupCursor(group);

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
    /// Picks the current balance for a stored account from the Item's account snapshot. When the stored
    /// account carries a Plaid account_id, the matching snapshot account is used; a lone account with a
    /// blank account_id (unambiguous on its token) falls back to the single snapshot account. Returns
    /// null when there is no unambiguous match or the provider reported no current balance.
    /// </summary>
    internal static decimal? ExtractCurrentBalance(IReadOnlyList<PlaidAccount> accounts, string? accountId)
    {
        PlaidAccount? match;
        if (!string.IsNullOrEmpty(accountId))
        {
            match = accounts.FirstOrDefault(a => a.AccountId == accountId);
        }
        else
        {
            match = accounts.Count == 1 ? accounts[0] : null;
        }

        return match?.Balances?.Current;
    }

    /// <summary>
    /// Picks the raw JSON of a stored account's entry in the Item's account snapshot, using the same
    /// matching rule as <see cref="ExtractCurrentBalance"/> so the two never describe different accounts:
    /// by Plaid account_id when the stored account carries one, falling back to the sole snapshot account
    /// for a lone account with a blank account_id. Returns null when there is no unambiguous match.
    /// </summary>
    internal static string? ExtractRawAccountMetadata(IReadOnlyList<RawPlaidAccount> accounts, string? accountId)
    {
        RawPlaidAccount? match;
        if (!string.IsNullOrEmpty(accountId))
        {
            match = accounts.FirstOrDefault(a => a.AccountId == accountId);
        }
        else
        {
            match = accounts.Count == 1 ? accounts[0] : null;
        }

        return match?.Json;
    }

    /// <summary>
    /// Splits one /transactions/sync page body into the raw JSON of its individual objects, so the sync
    /// archive stores the bytes Plaid sent rather than a re-serialization of Going.Plaid's entities —
    /// which would silently drop anything Plaid returns that the library does not model.
    /// </summary>
    /// <remarks>
    /// The added/modified lists are correlated positionally with the deserialized ones, which is sound
    /// because both views come from the same body and JSON preserves array order. To keep it sound the
    /// invariant is checked rather than assumed: a raw array whose length disagrees with the typed one
    /// yields empty strings for the whole page, so a skewed pairing can never file one transaction's
    /// bytes under another's id. Archiving nothing is recoverable; archiving the wrong bytes is not.
    /// Any failure here is logged and degrades to empty capture — the archive is best-effort and must
    /// never be able to fail a sync that Plaid itself answered successfully.
    /// </remarks>
    internal static RawPage ReadRawPage(string? rawJson, int addedCount, int modifiedCount, ILogger logger)
    {
        if (string.IsNullOrEmpty(rawJson))
        {
            logger.LogWarning(
                "Plaid /transactions/sync returned no raw JSON despite ShowRawJson; archiving nothing for this page.");
            return RawPage.Empty(addedCount, modifiedCount);
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("Plaid /transactions/sync raw JSON was not an object; archiving nothing for this page.");
                return RawPage.Empty(addedCount, modifiedCount);
            }

            return new RawPage(
                ReadRawTransactions(document.RootElement, "added", addedCount, logger),
                ReadRawTransactions(document.RootElement, "modified", modifiedCount, logger),
                ReadRawAccounts(document.RootElement));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Plaid /transactions/sync raw JSON could not be parsed; archiving nothing for this page.");
            return RawPage.Empty(addedCount, modifiedCount);
        }
    }

    /// <summary>
    /// Reads one of the page's transaction arrays, returning exactly <paramref name="expectedCount"/>
    /// entries so the caller's zip against the deserialized list can never drop a transaction. Entries
    /// are empty strings when the array is missing or its length disagrees with the deserialized one.
    /// </summary>
    private static IReadOnlyList<string> ReadRawTransactions(
        JsonElement root, string propertyName, int expectedCount, ILogger logger)
    {
        if (expectedCount == 0)
        {
            return Array.Empty<string>();
        }

        if (!root.TryGetProperty(propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() != expectedCount)
        {
            logger.LogWarning(
                "Plaid raw '{Property}' array does not line up with the {ExpectedCount} deserialized transactions; archiving nothing for it rather than pairing by skew.",
                propertyName,
                expectedCount);
            return Enumerable.Repeat("", expectedCount).ToList();
        }

        var raw = new List<string>(expectedCount);
        foreach (var transaction in array.EnumerateArray())
        {
            raw.Add(transaction.GetRawText());
        }

        return raw;
    }

    /// <summary>
    /// Reads the page's account snapshot, keeping each account object whole. Unlike SimpleFIN's, a Plaid
    /// account carries no nested transactions array, so nothing has to be stripped to keep the archived
    /// metadata from duplicating transactions that are already archived on their own.
    /// </summary>
    private static IReadOnlyList<RawPlaidAccount> ReadRawAccounts(JsonElement root)
    {
        if (!root.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RawPlaidAccount>();
        }

        var raw = new List<RawPlaidAccount>(accounts.GetArrayLength());
        foreach (var account in accounts.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var accountId = account.TryGetProperty("account_id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;

            raw.Add(new RawPlaidAccount(accountId, account.GetRawText()));
        }

        return raw;
    }

    /// <param name="RawJson">
    /// The transaction object exactly as it appeared in the /transactions/sync body, or an empty string
    /// when the page's raw JSON could not be correlated. Passed in rather than derived here because a
    /// <see cref="PlaidTransaction"/> offers no route back to its own JSON.
    /// </param>
    internal static SyncedTransaction MapTransaction(PlaidTransaction t, string rawJson)
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
            t.Pending ?? false,
            rawJson);
    }

    /// <summary>One account object from a /transactions/sync page, kept verbatim alongside its id.</summary>
    internal sealed record RawPlaidAccount(string? AccountId, string Json);

    internal sealed record RawPage(
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Modified,
        IReadOnlyList<RawPlaidAccount> Accounts)
    {
        public static RawPage Empty(int addedCount, int modifiedCount) => new(
            Enumerable.Repeat("", addedCount).ToList(),
            Enumerable.Repeat("", modifiedCount).ToList(),
            Array.Empty<RawPlaidAccount>());
    }
}
