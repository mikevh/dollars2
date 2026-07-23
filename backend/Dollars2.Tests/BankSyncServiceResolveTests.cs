using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Services;

namespace Dollars2.Tests;

public class BankSyncServiceResolveTests
{
    // Groups accounts by the raw ConnectionDetailsJson, standing in for a real provider whose connection
    // key is derived from an access token / URL shared across a set of accounts.
    private sealed class FakeProvider : IBankSyncProvider
    {
        public FakeProvider(string sourceType) => SourceType = sourceType;

        public string SourceType { get; }
        public bool Enabled => true;
        public TimeSpan MinSyncInterval => TimeSpan.FromHours(6);
        public string GetConnectionKey(Account account) => account.ConnectionDetailsJson ?? "";

        public Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(
            IReadOnlyList<Account> accounts, DateTime? since, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly IReadOnlyDictionary<string, IBankSyncProvider> Providers =
        new Dictionary<string, IBankSyncProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["SimpleFIN"] = new FakeProvider("SimpleFIN"),
        };

    private static Account Account(int id, string sourceType, string? connection, string name = "acct") => new()
    {
        Id = id,
        UserId = 1,
        Name = $"{name}{id}",
        SourceType = sourceType,
        ConnectionDetailsJson = connection,
    };

    [Fact]
    public void Resolves_the_connectionId_emitted_by_BuildGroups_back_to_the_same_accounts()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank", "checking"),
            Account(2, "SimpleFIN", "keybank", "savings"),
            Account(3, "SimpleFIN", "chase", "credit"),
        };

        // The opaque ids the client receives.
        var groups = AccountService.BuildGroups(accounts, Array.Empty<SyncLog>(), Array.Empty<AccountBalance>(), Providers);

        foreach (var group in groups)
        {
            var resolved = BankSyncService.ResolveConnectionAccounts(accounts, group.ConnectionId, Providers);
            Assert.Equal(group.Accounts.Select(a => a.Id), resolved.Select(a => a.Id));
        }
    }

    [Fact]
    public void Resolves_only_the_targeted_group_when_multiple_connections_exist()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank", "checking"),
            Account(2, "SimpleFIN", "keybank", "savings"),
            Account(3, "SimpleFIN", "chase", "credit"),
        };
        var chaseId = ConnectionKeyHasher.Hash("SimpleFIN", "chase");

        var resolved = BankSyncService.ResolveConnectionAccounts(accounts, chaseId, Providers);

        Assert.Equal(new[] { 3 }, resolved.Select(a => a.Id));
    }

    [Fact]
    public void Unknown_connectionId_resolves_to_no_accounts()
    {
        var accounts = new[] { Account(1, "SimpleFIN", "keybank") };

        var resolved = BankSyncService.ResolveConnectionAccounts(accounts, "deadbeefdeadbeef", Providers);

        Assert.Empty(resolved);
    }

    [Fact]
    public void Manual_connectionId_resolves_to_no_accounts()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank"),
            Account(2, "Manual", null, "cash"),
        };

        // "manual" is the id BuildGroups gives the Manual group; it must not be syncable.
        var resolved = BankSyncService.ResolveConnectionAccounts(accounts, "manual", Providers);

        Assert.Empty(resolved);
    }

    [Fact]
    public void Unknown_provider_resolves_per_account_via_its_own_connectionId()
    {
        // "Plaid" has no registered provider here, so each account stands alone under a per-account key,
        // mirroring how AccountService groups such accounts.
        var accounts = new[]
        {
            Account(1, "Plaid", "item-a"),
            Account(2, "Plaid", "item-a"),
        };
        var account1Id = ConnectionKeyHasher.Hash("Plaid", "account:1");

        var resolved = BankSyncService.ResolveConnectionAccounts(accounts, account1Id, Providers);

        Assert.Equal(new[] { 1 }, resolved.Select(a => a.Id));
    }
}
