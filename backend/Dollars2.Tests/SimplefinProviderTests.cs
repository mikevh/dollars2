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
            new[] { Account(1, accountId: null) }, since: null, TestContext.Current.CancellationToken);

        Assert.NotNull(results[1].Error);
        Assert.Empty(results[1].Upserts);
    }

    [Fact]
    public async Task Account_whose_AccountId_is_absent_from_response_is_failed()
    {
        var provider = CreateProvider("""{"accounts":[{"id":"sf-other","transactions":[]}],"errlist":[]}""");

        var results = await provider.FetchTransactionsForConnectionAsync(
            new[] { Account(1, accountId: "sf-missing") }, since: null, TestContext.Current.CancellationToken);

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
            new[] { good, broken }, since: null, TestContext.Current.CancellationToken);

        Assert.Null(results[1].Error);
        Assert.Single(results[1].Upserts);
        Assert.NotNull(results[2].Error);
    }
}
