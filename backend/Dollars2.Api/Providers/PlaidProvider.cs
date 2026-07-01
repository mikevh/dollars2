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

    public async Task<ProviderSyncResult> FetchTransactionsAsync(Account account, DateTime? since, CancellationToken cancellationToken = default)
    {
        var details = JsonSerializer.Deserialize<PlaidConnectionDetails>(
            account.ConnectionDetailsJson ?? "{}",
            JsonOptions);

        if (details is null || string.IsNullOrEmpty(details.AccessToken))
        {
            _logger.LogWarning("Account {AccountId} has missing or invalid Plaid connection details.", account.Id);
            throw new InvalidOperationException($"Account {account.Id} is missing a Plaid access token.");
        }

        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_secret))
        {
            throw new InvalidOperationException("Plaid:ClientId / Plaid:Secret are not configured.");
        }

        var client = new PlaidClient(
            _environment,
            _clientId,
            _secret,
            details.AccessToken,
            _httpClientFactory,
            _loggerFactory.CreateLogger<PlaidClient>(),
            ApiVersion.v20200914);

        var added = new List<PlaidTransaction>();
        var modified = new List<PlaidTransaction>();
        var removed = new List<RemovedTransaction>();
        var cursor = details.Cursor;
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
                    $"Plaid /transactions/sync failed for account {account.Id}: {error?.ErrorCode} - {error?.ErrorMessage}");
            }

            added.AddRange(response.Added); // response.Add = List<Entity.Transaction> ... has an accouint id. there's transactions from multiple accounts in this list. they need to be propoeryly assocsatied with the stored accoutnt... key checking and savings
            modified.AddRange(response.Modified);
            removed.AddRange(response.Removed);

            cursor = response.NextCursor;
            hasMore = response.HasMore;
        }
        while (hasMore);

        bool MatchesAccount(string? plaidAccountId) =>
            string.IsNullOrEmpty(details.AccountId) || plaidAccountId == details.AccountId;

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
            AccessToken = details.AccessToken,
            AccountId = details.AccountId,
            Cursor = cursor,
        });

        return new ProviderSyncResult(upserts, removedIds, updatedJson);
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
