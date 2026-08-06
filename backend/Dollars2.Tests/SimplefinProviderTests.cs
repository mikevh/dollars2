using System.Text.Json;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dollars2.Tests;

// Regression tests for the "silent success on missing AccountId" bug: a misconfigured SimpleFIN
// account used to report StatusSuccess with 0 transactions instead of failing, so it looked healthy
// forever. It must now surface as a per-account failure while healthy siblings still sync.
public class SimplefinProviderTests
{
    private const string Url = "https://simplefin.example/access";

    private static SimplefinProvider CreateProvider(string responseJson)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SimpleFin:Enabled"] = "true" })
            .Build();
        return new SimplefinProvider(config, new StubHttpClientFactory(responseJson), NullLogger<SimplefinProvider>.Instance);
    }

    private static Account Account(int id, string? accountId) => new()
    {
        Id = id,
        UserId = 1,
        Name = $"acct{id}",
        SourceType = "SimpleFIN",
        ConnectionDetailsJson = JsonSerializer.Serialize(new SimplefinConnectionDetails
        {
            AccountId = accountId ?? "",
            Username = "user",
            Password = "pass",
            Url = Url,
        }),
    };

    [Fact]
    public async Task Account_with_missing_AccountId_is_failed_not_silently_empty()
    {
        var provider = CreateProvider("""{"accounts":[{"id":"sf-1","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: null) }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.NotNull(results[1].Error);
        Assert.Empty(results[1].Upserts);
    }

    [Fact]
    public async Task Account_whose_AccountId_is_absent_from_response_is_failed()
    {
        var provider = CreateProvider("""{"accounts":[{"id":"sf-other","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-missing") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.NotNull(results[1].Error);
    }

    [Fact]
    public async Task Misconfigured_account_fails_while_healthy_sibling_still_syncs()
    {
        const string response = """
        {"accounts":[{"id":"sf-good","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"","pending":false}
        ]}],"errlist":[]}
        """;
        var provider = CreateProvider(response);
        var good = Account(1, accountId: "sf-good");
        var broken = Account(2, accountId: null);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { good, broken }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Null(results[1].Error);
        Assert.Single(results[1].Upserts);
        Assert.NotNull(results[2].Error);
    }

    // Issue #93: SimpleFIN puts no bound on description/payee/memo, and the columns are nvarchar(500).
    // Over-length text must be clamped rather than fail the account — an unclamped value raises
    // SqlException 8152 on write, which stalls that account's imports for as long as it is in the window.
    [Fact]
    public async Task Over_length_text_is_truncated_instead_of_failing_the_account()
    {
        var longText = new string('x', 600);
        var response = $$"""
        {"accounts":[{"id":"sf-1","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"{{longText}}","payee":"{{longText}}","memo":"{{longText}}","pending":false}
        ]}],"errlist":[]}
        """;
        var provider = CreateProvider(response);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Null(results[1].Error);
        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal(TransactionText.MaxLength, mapped.Description.Length);
        Assert.Equal(TransactionText.MaxLength, mapped.Payee.Length);
        Assert.Equal(TransactionText.MaxLength, mapped.Memo.Length);
    }

    // An explicit JSON null overwrites the DTO's "" default even though the property is declared
    // non-nullable, so the clamp has to tolerate it. If it threw, the failure would surface from the
    // fetch rather than the write, and BankSyncService fails *every* account on the connection when a
    // fetch throws — a wider blast radius than the per-account failure this issue set out to remove.
    [Fact]
    public async Task Null_text_fields_map_to_empty_without_failing_the_fetch()
    {
        const string response = """
        {"accounts":[{"id":"sf-1","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":null,"payee":null,"memo":null,"pending":false}
        ]}],"errlist":[]}
        """;
        var provider = CreateProvider(response);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Null(results[1].Error);
        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal("", mapped.Description);
        Assert.Equal("", mapped.Payee);
        Assert.Equal("", mapped.Memo);
    }

    [Fact]
    public async Task Text_within_the_column_width_is_mapped_unchanged()
    {
        const string response = """
        {"accounts":[{"id":"sf-1","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"latte","pending":false}
        ]}],"errlist":[]}
        """;
        var provider = CreateProvider(response);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal("Coffee", mapped.Description);
        Assert.Equal("Cafe", mapped.Payee);
        Assert.Equal("latte", mapped.Memo);
    }

    [Fact]
    public async Task Reported_balance_is_parsed_onto_the_result()
    {
        var provider = CreateProvider(
            """{"accounts":[{"id":"sf-1","balance":"1234.56","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Equal(1234.56m, results[1].Balance);
    }

    [Fact]
    public async Task Unparseable_balance_yields_a_null_balance_without_failing_the_sync()
    {
        var provider = CreateProvider(
            """{"accounts":[{"id":"sf-1","balance":"n/a","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Null(results[1].Balance);
        Assert.Null(results[1].Error);
    }

    // Issue #167: the sync archive stores what the bank actually sent, so RawJson has to be the response
    // bytes rather than anything re-serialized from our DTO. The transaction below carries an `extra`
    // field the DTO does not model — round-tripping through SimplefinTransaction would silently drop it,
    // so its presence is what proves the raw text is genuinely raw.
    private const string TransactionWithUnmappedField =
        """{"id":"t1","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"","pending":false,"extra":{"category":["Food"]}}""";

    [Fact]
    public async Task RawJson_is_the_transaction_object_exactly_as_the_provider_sent_it()
    {
        var provider = CreateProvider($$"""
        {"accounts":[{"id":"sf-1","balance":"10.00","transactions":[{{TransactionWithUnmappedField}}]}],"errlist":[]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal(TransactionWithUnmappedField, mapped.RawJson);
    }

    [Fact]
    public async Task RawJson_keeps_the_full_text_that_Description_and_Payee_get_clamped_to()
    {
        var longText = new string('x', 600);
        var provider = CreateProvider($$"""
        {"accounts":[{"id":"sf-1","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"{{longText}}","payee":"{{longText}}","memo":"{{longText}}","pending":false}
        ]}],"errlist":[]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        var mapped = Assert.Single(results[1].Upserts);
        // The nvarchar(500) clamp still applies to what gets persisted...
        Assert.Equal(TransactionText.MaxLength, mapped.Description.Length);
        // ...and deliberately does not apply to the archived copy.
        Assert.Contains(longText, mapped.RawJson);
    }

    [Fact]
    public async Task AccountMetadataJson_keeps_the_account_fields_and_drops_the_transactions_array()
    {
        var provider = CreateProvider("""
        {"accounts":[{"id":"sf-1","name":"Checking","balance":"1234.56","available-balance":"1200.00","currency":"USD","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"","pending":false}
        ]}],"errlist":[]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        var metadata = results[1].AccountMetadataJson;
        Assert.NotNull(metadata);

        using var parsed = JsonDocument.Parse(metadata);
        Assert.Equal("Checking", parsed.RootElement.GetProperty("name").GetString());
        Assert.Equal("1234.56", parsed.RootElement.GetProperty("balance").GetString());
        // Fields the DTO does not model survive too — this is the account object, not a re-serialization.
        Assert.Equal("1200.00", parsed.RootElement.GetProperty("available-balance").GetString());
        Assert.Equal("USD", parsed.RootElement.GetProperty("currency").GetString());
        // Dropped so the metadata item does not duplicate every transaction archived on its own.
        Assert.False(parsed.RootElement.TryGetProperty("transactions", out _));
    }

    [Fact]
    public async Task ErrorsJson_carries_the_errlist_entries_verbatim()
    {
        var provider = CreateProvider("""
        {"accounts":[{"id":"sf-1","transactions":[]}],"errlist":[{"code":"AUTH","message":"Reconnect required"}]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        var error = Assert.Single(results[1].ErrorsJson);
        using var parsed = JsonDocument.Parse(error);
        Assert.Equal("AUTH", parsed.RootElement.GetProperty("code").GetString());
        Assert.Equal("Reconnect required", parsed.RootElement.GetProperty("message").GetString());
    }

    // errlist is response-level, and on a failing account it is usually the explanation — so it has to
    // reach the failure results too, not just the ones that synced.
    [Fact]
    public async Task ErrorsJson_reaches_accounts_that_failed_to_match_a_provider_account()
    {
        var provider = CreateProvider("""
        {"accounts":[],"errlist":[{"code":"AUTH","message":"Reconnect required"}]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-missing"), Account(2, accountId: null) },
            since: null,
            cancel: TestContext.Current.CancellationToken);

        Assert.NotNull(results[1].Error);
        Assert.Single(results[1].ErrorsJson);
        Assert.NotNull(results[2].Error);
        Assert.Single(results[2].ErrorsJson);
    }

    [Fact]
    public async Task ErrorsJson_is_empty_rather_than_null_when_the_response_reported_none()
    {
        var provider = CreateProvider("""{"accounts":[{"id":"sf-1","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Empty(results[1].ErrorsJson);
        Assert.Empty(results[1].SkippedTransactionsJson);
    }

    // A transaction the amount parser rejected is precisely what the archive's forensics exist for, so
    // it must survive being dropped from Upserts.
    [Fact]
    public async Task A_transaction_skipped_for_an_unparseable_amount_is_still_captured_raw()
    {
        const string broken = """{"id":"t-bad","posted":1700000000,"amount":"n/a","description":"Mystery","payee":"","memo":"","pending":false}""";
        var provider = CreateProvider($$"""
        {"accounts":[{"id":"sf-1","transactions":[
            {{broken}},
            {"id":"t-good","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"","pending":false}
        ]}],"errlist":[]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        // Unchanged behavior: the unparseable one still never reaches MSSQL.
        var mapped = Assert.Single(results[1].Upserts);
        Assert.Equal("t-good", mapped.ProviderTransactionId);
        Assert.Null(results[1].Error);

        Assert.Equal(broken, Assert.Single(results[1].SkippedTransactionsJson));
    }

    // Two accounts on one connection share a response body; each must get its own slice of it.
    [Fact]
    public async Task Raw_capture_is_correlated_per_account_across_a_shared_response()
    {
        var provider = CreateProvider("""
        {"accounts":[
            {"id":"sf-1","name":"Checking","transactions":[{"id":"t1","posted":1700000000,"amount":"-1.00","description":"One","payee":"","memo":"","pending":false}]},
            {"id":"sf-2","name":"Savings","transactions":[{"id":"t2","posted":1700000000,"amount":"-2.00","description":"Two","payee":"","memo":"","pending":false}]}
        ],"errlist":[]}
        """);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1"), Account(2, accountId: "sf-2") },
            since: null,
            cancel: TestContext.Current.CancellationToken);

        Assert.Contains("\"t1\"", Assert.Single(results[1].Upserts).RawJson);
        Assert.Contains("Checking", results[1].AccountMetadataJson!);

        Assert.Contains("\"t2\"", Assert.Single(results[2].Upserts).RawJson);
        Assert.Contains("Savings", results[2].AccountMetadataJson!);
    }
}
