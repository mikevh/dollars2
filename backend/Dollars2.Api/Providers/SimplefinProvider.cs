using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dollars2.Api.Models;

namespace Dollars2.Api.Providers;

public class SimplefinProvider : IBankSyncProvider
{
    private readonly string? _accessUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SimplefinProvider> _logger;

    public SimplefinProvider(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<SimplefinProvider> logger)
    {
        _accessUrl = config["SimpleFin:AccessUrl"];
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<SyncedTransaction>> FetchTransactionsAsync(Account account, DateTime? since, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accessUrl))
        {
            throw new InvalidOperationException("SimpleFin:AccessUrl is not configured.");
        }

        var connectionDetails = JsonSerializer.Deserialize<SimplefinConnectionDetails>(
            account.ConnectionDetailsJson ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (connectionDetails is null || string.IsNullOrEmpty(connectionDetails.AccountId))
        {
            _logger.LogWarning("Account {AccountId} has missing or invalid SimpleFIN connection details.", account.Id);
            throw new InvalidOperationException($"Account {account.Id} has missing or invalid SimpleFIN connection details.");
        }

        var uri = new Uri(_accessUrl);
        var base64Credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(uri.UserInfo));
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        var cleanUrl = $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";

        var requestUrl = cleanUrl;
        if (since.HasValue)
        {
            var startDate = ((DateTimeOffset)DateTime.SpecifyKind(since.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
            requestUrl += $"?start-date={startDate}";
        }

        _logger.LogTrace("Fetching transactions for account {AccountId} from SimpleFIN", account.Id);
        var http = _httpClientFactory.CreateClient("simplefin");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"SimpleFIN request failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var accountSet = JsonSerializer.Deserialize<SimplefinAccountSet>(json)
            ?? throw new InvalidOperationException("Failed to deserialize SimpleFIN response.");

        foreach (var error in accountSet.Errlist)
        {
            _logger.LogWarning("SimpleFIN returned error for account {AccountId}: {Error}", account.Id, error.ToString());
        }

        var simplefinAccount = accountSet.Accounts.FirstOrDefault(a => a.Id == connectionDetails.AccountId);
        if (simplefinAccount is null)
        {
            _logger.LogWarning("No matching account found in SimpleFIN response for account {AccountId} with SimpleFIN AccountId {SimplefinAccountId}.", account.Id, connectionDetails.AccountId);
            return Enumerable.Empty<SyncedTransaction>();
        }

        var result = new List<SyncedTransaction>();
        foreach (var t in simplefinAccount.Transactions)
        {
            if (!decimal.TryParse(t.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                _logger.LogWarning("Skipping transaction {TransactionId} for account {AccountId} with invalid amount: '{Amount}'", t.Id, account.Id, t.Amount);
                continue;
            }

            var date = t.Posted == 0
                ? DateTime.UtcNow.Date
                : DateTimeOffset.FromUnixTimeSeconds(t.Posted).UtcDateTime.Date;

            result.Add(new SyncedTransaction(t.Id, date, t.Description, amount, t.Pending));
        }

        return result;
    }
}
