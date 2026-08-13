using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dollars2.Api.Models;
using Dollars2.Api.Services;

namespace Dollars2.Api.Providers;

public class SimplefinProvider : IBankSyncProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SimplefinProvider> _logger;

    public SimplefinProvider(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<SimplefinProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        Enabled = config.GetValue<bool>("SimpleFin:Enabled");
        var hours = config.GetValue<double?>("SimpleFin:MinSyncIntervalHours") ?? 6;
        MinSyncInterval = TimeSpan.FromHours(hours);
    }

    public string SourceType => SyncConstants.SourceTypeSimpleFin;

    public bool Enabled { get; }

    public TimeSpan MinSyncInterval { get; }

    public string GetConnectionKey(Account account)
    {
        var details = JsonSerializer.Deserialize<SimplefinConnectionDetails>(
            account.ConnectionDetailsJson ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // A single SimpleFIN access URL returns every account it covers in one response. Group by
        // that URL + username; fall back to a per-account key when unusable so a broken account is
        // synced (and fails) on its own.
        if (details is null || string.IsNullOrEmpty(details.Url))
        {
            return $"account:{account.Id}";
        }
        return $"{details.Url}\n{details.Username}";
    }

    public async Task<ProviderFetchResult> FetchTransactionsForConnectionAsync(IReadOnlyList<Account> accounts, DateTime? since, bool fullResync = false, CancellationToken cancel = default)
    {
        // SimpleFIN fetches purely by the `since` window (?start-date), so a full resync needs no
        // special handling here — the widened `since` the caller supplies already does the work.
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsed = accounts
            .Select(a => (Account: a, Details: JsonSerializer.Deserialize<SimplefinConnectionDetails>(a.ConnectionDetailsJson ?? "{}", jsonOptions)))
            .ToList();

        // Credentials are shared across the connection group (that's the key), so any account's
        // details drive the single request.
        var connectionDetails = parsed
            .Select(p => p.Details)
            .FirstOrDefault(d => d is not null
                && !string.IsNullOrEmpty(d.Url)
                && !string.IsNullOrEmpty(d.Username)
                && !string.IsNullOrEmpty(d.Password));

        if (connectionDetails is null)
        {
            _logger.LogWarning("SimpleFIN connection for accounts {AccountIds} has missing or invalid details.", string.Join(", ", accounts.Select(a => a.Id)));
            throw new InvalidOperationException("SimpleFIN connection has missing or invalid details.");
        }

        var url = connectionDetails.Url;
        var base64Credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connectionDetails.Username}:{connectionDetails.Password}"));

        if (!fullResync && since.HasValue)
        {
            var startDate = ((DateTimeOffset)DateTime.SpecifyKind(since.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var separator = url.Contains('?') ? '&' : '?';
            url += $"{separator}start-date={startDate}";
        }
        else
        {
            _logger.LogInformation("Simplefin fullsync? {fullResync}, since {since}, url {url}", fullResync, since?.ToString("u") ?? "since was null", url);
        }

        _logger.LogTrace("Fetching transactions for accounts {AccountIds} from SimpleFIN", string.Join(", ", accounts.Select(a => a.Id)));

        var http = _httpClientFactory.CreateClient("simplefin");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);

        using var response = await http.SendAsync(request, cancel);
        var json = await response.Content.ReadAsStringAsync(cancel);

        _logger.LogTrace("SimpleFIN response body for accounts {AccountIds}: {body}", string.Join(", ", accounts.Select(a => a.Id)), json);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"SimpleFIN request failed with status {(int)response.StatusCode}: {json}");
        }

        var accountSet = JsonSerializer.Deserialize<SimplefinAccountSet>(json) ?? throw new InvalidOperationException("Failed to deserialize SimpleFIN response.");

        foreach (var error in accountSet.Errlist)
        {
            _logger.LogWarning("SimpleFIN returned error: {Error}", error.ToString());
        }

        var results = new Dictionary<int, ProviderSyncResult>();
        foreach (var (account, details) in parsed)
        {
            if (details is null || string.IsNullOrEmpty(details.AccountId))
            {
                _logger.LogWarning("SimpleFIN account {AccountId} has no configured SimpleFIN AccountId.", account.Id);
                results[account.Id] = new ProviderSyncResult(Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null, "SimpleFIN connection details are missing an AccountId.");
                continue;
            }

            var simplefinAccount = accountSet.Accounts.FirstOrDefault(a => a.Id == details.AccountId);
            if (simplefinAccount is null)
            {
                _logger.LogWarning("No matching account found in SimpleFIN response for account {AccountId} with SimpleFIN AccountId {SimplefinAccountId}.", account.Id, details.AccountId);
                results[account.Id] = new ProviderSyncResult(Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null, $"SimpleFIN returned no account matching AccountId '{details.AccountId}'.");
                continue;
            }

            var transactions = new List<SyncedTransaction>();
            foreach (var t in simplefinAccount.Transactions)
            {
                if (!decimal.TryParse(t.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                {
                    _logger.LogWarning("Skipping transaction {TransactionId} for account {AccountId} with invalid amount: '{Amount}'", t.Id, account.Id, t.Amount);
                    continue;
                }

                if (t.Id.Length > TransactionText.MaxLength)
                {
                    // ProviderTransactionId is the dedup key (UX_Transactions_Provider); truncating could
                    // collide two distinct transactions, so skip rather than clamp.
                    _logger.LogWarning("Skipping transaction {TransactionId} for account {AccountId}: id exceeds {MaxLength} characters.", t.Id, account.Id, TransactionText.MaxLength);
                    continue;
                }

                var date = t.Posted == 0
                    ? DateOnly.FromDateTime(DateTime.UtcNow)
                    : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.Posted).UtcDateTime);

                transactions.Add(new SyncedTransaction(t.Id, date, t.Description, t.Payee, t.Memo, amount, t.Pending));
            }

            decimal? balance = null;
            if (decimal.TryParse(simplefinAccount.Balance, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedBalance))
            {
                balance = parsedBalance;
            }
            else if (!string.IsNullOrEmpty(simplefinAccount.Balance))
            {
                _logger.LogWarning("Skipping unparseable balance '{Balance}' for account {AccountId}", simplefinAccount.Balance, account.Id);
            }

            results[account.Id] = new ProviderSyncResult(
                transactions,
                Array.Empty<string>(),
                null,
                Balance: balance);
        }

        return new ProviderFetchResult(results, [json]);
    }
}
