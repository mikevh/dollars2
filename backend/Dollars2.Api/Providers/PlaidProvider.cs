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

        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_secret))
        {
            throw new InvalidOperationException("Plaid:ClientId / Plaid:Secret are not configured.");
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
        // account in the group already agrees on the same non-empty value. Otherwise (a newly added
        // account with no cursor, or cursors that diverged under the old per-account scheme) start
        // from scratch so no account's history is missed; ProviderTransactionId dedup absorbs the
        // re-fetch. After this run all accounts are written the same advanced cursor and converge.
        var cursors = parsed.Select(p => p.Details?.Cursor).ToList();
        var converged = cursors.All(c => !string.IsNullOrEmpty(c)) && cursors.Distinct().Count() == 1;
        var cursor = converged ? cursors[0] : null;
        if (!converged && accounts.Count > 1)
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

        var results = new Dictionary<int, ProviderSyncResult>();
        foreach (var (account, details) in parsed)
        {
            // When several stored accounts share one access token, each must carry its own account_id
            // to attribute transactions. A blank account_id matches every transaction in the Item and
            // would pull siblings' activity into this account, so fail it rather than corrupt data. A
            // lone account on a token is unambiguous, so an empty account_id is still allowed there.
            if (accounts.Count > 1 && string.IsNullOrEmpty(details?.AccountId))
            {
                _logger.LogWarning(
                    "Plaid account {AccountId} shares an access token with other accounts but has no account_id; skipping to avoid importing siblings' transactions.",
                    account.Id);
                results[account.Id] = new ProviderSyncResult(
                    Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null,
                    "Plaid connection details are missing an account_id, which is required when multiple accounts share an access token.");
                continue;
            }

            bool MatchesAccount(string? plaidAccountId) =>
                string.IsNullOrEmpty(details?.AccountId) || plaidAccountId == details.AccountId;

            var upserts = added.Concat(modified)
                .Where(t => MatchesAccount(t.AccountId))
                .Select(MapTransaction)
                .ToList();

            var removedIds = removed
                .Where(r => MatchesAccount(r.AccountId))
                .Select(r => r.TransactionId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
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
