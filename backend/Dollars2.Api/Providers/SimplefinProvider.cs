using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dollars2.Api.Models;

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

    public string SourceType => "SimpleFIN";

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

    public async Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(IReadOnlyList<Account> accounts, DateTime? since, bool fullResync = false, CancellationToken cancel = default)
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
            url += $"?start-date={startDate}";
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
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancel);
            throw new HttpRequestException($"SimpleFIN request failed with status {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancel);
        var accountSet = JsonSerializer.Deserialize<SimplefinAccountSet>(json) ?? throw new InvalidOperationException("Failed to deserialize SimpleFIN response.");

        foreach (var error in accountSet.Errlist)
        {
            _logger.LogWarning("SimpleFIN returned error: {Error}", error.ToString());
        }

        // errlist is response-level, not per-account, so every result in this connection group carries
        // the same entries — including the failure results, where they are often the explanation.
        var errorsJson = accountSet.Errlist.Select(e => e.GetRawText()).ToList();

        // Only the SimpleFIN accounts this group actually tracks: one access URL can cover accounts the
        // user never added, and indexing those would build the largest strings in the response for nothing.
        var wantedAccountIds = parsed
            .Where(p => !string.IsNullOrEmpty(p.Details?.AccountId))
            .Select(p => p.Details!.AccountId)
            .ToHashSet();

        var rawAccounts = IndexRawAccounts(json, wantedAccountIds);

        var results = new Dictionary<int, ProviderSyncResult>();
        foreach (var (account, details) in parsed)
        {
            if (details is null || string.IsNullOrEmpty(details.AccountId))
            {
                _logger.LogWarning("SimpleFIN account {AccountId} has no configured SimpleFIN AccountId.", account.Id);
                results[account.Id] = new ProviderSyncResult(Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null, "SimpleFIN connection details are missing an AccountId.", ErrorsJson: errorsJson);
                continue;
            }

            var simplefinAccount = accountSet.Accounts.FirstOrDefault(a => a.Id == details.AccountId);
            if (simplefinAccount is null)
            {
                _logger.LogWarning("No matching account found in SimpleFIN response for account {AccountId} with SimpleFIN AccountId {SimplefinAccountId}.", account.Id, details.AccountId);
                results[account.Id] = new ProviderSyncResult(Array.Empty<SyncedTransaction>(), Array.Empty<string>(), null, $"SimpleFIN returned no account matching AccountId '{details.AccountId}'.", ErrorsJson: errorsJson);
                continue;
            }

            rawAccounts.TryGetValue(details.AccountId, out var raw);

            var transactions = new List<SyncedTransaction>();
            var skippedTransactionsJson = new List<string>();
            foreach (var t in simplefinAccount.Transactions)
            {
                // Looked up by id rather than re-serialized from the DTO, so the archive holds the bytes
                // SimpleFIN sent — including fields the DTO does not model.
                var rawTransaction = raw?.TransactionsById.GetValueOrDefault(t.Id);
                if (rawTransaction is null)
                {
                    // Both views are built from the same response body, so this should be unreachable.
                    // Logged rather than passed over: an archive that silently holds nothing for a
                    // transaction is the failure you would most want to find in the logs afterwards.
                    _logger.LogWarning(
                        "No raw JSON captured for SimpleFIN transaction {TransactionId} on account {AccountId}",
                        t.Id,
                        account.Id);
                }

                if (!decimal.TryParse(t.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                {
                    _logger.LogWarning("Skipping transaction {TransactionId} for account {AccountId} with invalid amount: '{Amount}'", t.Id, account.Id, t.Amount);

                    // An empty entry would archive nothing while looking like a captured payload.
                    if (!string.IsNullOrEmpty(rawTransaction))
                    {
                        skippedTransactionsJson.Add(rawTransaction);
                    }
                    continue;
                }

                var date = t.Posted == 0
                    ? DateOnly.FromDateTime(DateTime.UtcNow)
                    : DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.Posted).UtcDateTime);

                transactions.Add(new SyncedTransaction(t.Id, date, t.Description, t.Payee, t.Memo, amount, t.Pending, rawTransaction ?? ""));
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
                Balance: balance,
                AccountMetadataJson: raw?.MetadataJson,
                ErrorsJson: errorsJson,
                SkippedTransactionsJson: skippedTransactionsJson);
        }

        return results;
    }

    /// <summary>
    /// Indexes the response body by SimpleFIN account id, so the typed mapping loop above can attach the
    /// raw text of each account and transaction object without re-serializing its own DTOs. Restricted to
    /// <paramref name="wantedAccountIds"/> — the accounts this connection group actually tracks.
    /// </summary>
    /// <remarks>
    /// Duplicate ids resolve first-wins at both levels, via TryAdd. That is not arbitrary: the typed loop
    /// selects its account with FirstOrDefault and walks transactions in document order, so last-wins here
    /// would quietly pair one account's transactions with another's raw text — an archive holding the
    /// wrong bytes under the right id, which is worse than holding none.
    /// </remarks>
    private static Dictionary<string, RawAccount> IndexRawAccounts(string json, IReadOnlySet<string> wantedAccountIds)
    {
        var indexed = new Dictionary<string, RawAccount>();

        if (wantedAccountIds.Count == 0)
        {
            return indexed;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
        {
            return indexed;
        }

        foreach (var account in accounts.EnumerateArray())
        {
            if (account.ValueKind != JsonValueKind.Object
                || !TryGetId(account, out var accountId)
                || !wantedAccountIds.Contains(accountId)
                || indexed.ContainsKey(accountId))
            {
                continue;
            }

            var transactionsById = new Dictionary<string, string>();
            if (account.TryGetProperty("transactions", out var transactions) && transactions.ValueKind == JsonValueKind.Array)
            {
                foreach (var transaction in transactions.EnumerateArray())
                {
                    if (transaction.ValueKind == JsonValueKind.Object && TryGetId(transaction, out var transactionId))
                    {
                        // TryAdd rather than Add or the indexer: a provider repeating an id must not throw,
                        // and first-wins keeps the first occurrence paired with its own bytes.
                        transactionsById.TryAdd(transactionId, transaction.GetRawText());
                    }
                }
            }

            indexed[accountId] = new RawAccount(WriteMetadataWithoutTransactions(account), transactionsById);
        }

        return indexed;
    }

    private static bool TryGetId(JsonElement element, out string id)
    {
        id = "";

        if (element.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String)
        {
            id = value.GetString() ?? "";
        }

        return id.Length > 0;
    }

    /// <summary>
    /// Copies the account object property for property, dropping only the nested transactions array so
    /// the archived metadata does not duplicate every transaction that is already archived on its own.
    /// Everything kept is written through unchanged.
    /// </summary>
    private static string WriteMetadataWithoutTransactions(JsonElement account)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in account.EnumerateObject())
            {
                if (!property.NameEquals("transactions"))
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private sealed record RawAccount(string MetadataJson, Dictionary<string, string> TransactionsById);
}
