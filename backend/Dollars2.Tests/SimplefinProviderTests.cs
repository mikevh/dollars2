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

        Assert.NotNull(results.Results[1].Error);
        Assert.Empty(results.Results[1].Upserts);
    }

    [Fact]
    public async Task Account_whose_AccountId_is_absent_from_response_is_failed()
    {
        var provider = CreateProvider("""{"accounts":[{"id":"sf-other","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-missing") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.NotNull(results.Results[1].Error);
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

        Assert.Null(results.Results[1].Error);
        Assert.Single(results.Results[1].Upserts);
        Assert.NotNull(results.Results[2].Error);
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

        Assert.Null(results.Results[1].Error);
        var mapped = Assert.Single(results.Results[1].Upserts);
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

        Assert.Null(results.Results[1].Error);
        var mapped = Assert.Single(results.Results[1].Upserts);
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

        var mapped = Assert.Single(results.Results[1].Upserts);
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

        Assert.Equal(1234.56m, results.Results[1].Balance);
    }

    [Fact]
    public async Task Unparseable_balance_yields_a_null_balance_without_failing_the_sync()
    {
        var provider = CreateProvider(
            """{"accounts":[{"id":"sf-1","balance":"n/a","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Null(results.Results[1].Balance);
        Assert.Null(results.Results[1].Error);
    }

    // Issue #259: the sync archive now stores the whole connection-level response body verbatim rather
    // than per-transaction slices, so the fetch has to hand that body back unmodified for archiving.
    [Fact]
    public async Task FetchTransactionsForConnectionAsync_captures_the_response_body_verbatim_for_the_archive()
    {
        const string response = """
        {"accounts":[{"id":"sf-1","transactions":[
            {"id":"t1","posted":1700000000,"amount":"-12.50","description":"Coffee","payee":"Cafe","memo":"","pending":false,"extra":{"category":["Food"]}}
        ]}],"errlist":[]}
        """;
        var provider = CreateProvider(response);

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-1") }, since: null, cancel: TestContext.Current.CancellationToken);

        Assert.Equal(response, Assert.Single(results.RawResponseBodies));
    }

    // A transaction the amount parser rejected must still never reach MSSQL.
    [Fact]
    public async Task A_transaction_skipped_for_an_unparseable_amount_is_dropped_without_failing_the_account()
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

        var mapped = Assert.Single(results.Results[1].Upserts);
        Assert.Equal("t-good", mapped.ProviderTransactionId);
        Assert.Null(results.Results[1].Error);
    }

    // Two accounts on one connection share a response body; each must get its own slice of upserts.
    [Fact]
    public async Task Upserts_are_attributed_per_account_across_a_shared_response()
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

        Assert.Equal("t1", Assert.Single(results.Results[1].Upserts).ProviderTransactionId);
        Assert.Equal("t2", Assert.Single(results.Results[2].Upserts).ProviderTransactionId);
    }
}
