using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Services;

namespace Dollars2.Tests;

public class AccountServiceTests
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

    private static SyncLog Log(int accountId, DateTime syncedAt, string status) => new()
    {
        Id = accountId,
        AccountId = accountId,
        SyncedAt = syncedAt,
        Status = status,
    };

    [Fact]
    public void Accounts_sharing_a_connection_are_grouped_together()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank", "checking"),
            Account(2, "SimpleFIN", "keybank", "savings"),
            Account(3, "SimpleFIN", "chase", "credit"),
        };

        var groups = AccountService.BuildGroups(accounts, Array.Empty<SyncLog>(), Providers);

        Assert.Equal(2, groups.Count);
        var keybank = Assert.Single(groups, g => g.Accounts.Count == 2);
        Assert.Equal(new[] { 1, 2 }, keybank.Accounts.Select(a => a.Id));
        var chase = Assert.Single(groups, g => g.Accounts.Count == 1);
        Assert.Equal(3, chase.Accounts[0].Id);
        // Distinct connections get distinct opaque ids.
        Assert.NotEqual(keybank.ConnectionId, chase.ConnectionId);
    }

    [Fact]
    public void ConnectionId_does_not_leak_the_raw_connection_key()
    {
        var groups = AccountService.BuildGroups(
            new[] { Account(1, "SimpleFIN", "super-secret-access-token") },
            Array.Empty<SyncLog>(),
            Providers);

        var group = Assert.Single(groups);
        Assert.DoesNotContain("super-secret-access-token", group.ConnectionId);
    }

    [Fact]
    public void Manual_accounts_form_their_own_group()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank"),
            Account(2, "Manual", null, "cash"),
            Account(3, "Manual", null, "envelope"),
        };

        var groups = AccountService.BuildGroups(accounts, Array.Empty<SyncLog>(), Providers);

        var manual = Assert.Single(groups, g => g.SourceType == "Manual");
        Assert.Equal("manual", manual.ConnectionId);
        Assert.Equal(new[] { 2, 3 }, manual.Accounts.Select(a => a.Id));
    }

    [Fact]
    public void Latest_sync_log_is_attached_per_account()
    {
        var accounts = new[]
        {
            Account(1, "SimpleFIN", "keybank"),
            Account(2, "SimpleFIN", "keybank"),
        };
        var syncedAt = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);
        var logs = new[] { Log(1, syncedAt, "Success") };

        var groups = AccountService.BuildGroups(accounts, logs, Providers);

        var group = Assert.Single(groups);
        var acct1 = group.Accounts.Single(a => a.Id == 1);
        Assert.Equal(syncedAt, acct1.LastSyncedAt);
        Assert.Equal("Success", acct1.LastStatus);
        // Never-synced account has no sync info.
        var acct2 = group.Accounts.Single(a => a.Id == 2);
        Assert.Null(acct2.LastSyncedAt);
        Assert.Null(acct2.LastStatus);
    }

    [Fact]
    public void Unknown_provider_falls_back_to_per_account_groups()
    {
        // "Plaid" has no registered provider here, so accounts can't be grouped by a shared connection
        // and must each stand alone rather than collapsing into one bogus group.
        var accounts = new[]
        {
            Account(1, "Plaid", "item-a"),
            Account(2, "Plaid", "item-a"),
        };

        var groups = AccountService.BuildGroups(accounts, Array.Empty<SyncLog>(), Providers);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Accounts));
    }

    [Fact]
    public void Empty_accounts_returns_no_groups()
    {
        var groups = AccountService.BuildGroups(Array.Empty<Account>(), Array.Empty<SyncLog>(), Providers);
        Assert.Empty(groups);
    }
}
