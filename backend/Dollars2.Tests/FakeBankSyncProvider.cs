using Dollars2.Api.Models;
using Dollars2.Api.Providers;

namespace Dollars2.Tests;

/// <summary>
/// Groups accounts by the raw ConnectionDetailsJson, standing in for a real provider whose connection
/// key is derived from an access token / URL shared across a set of accounts.
/// </summary>
/// <remarks>
/// Shared by <see cref="AccountServiceTests"/> and <see cref="BankSyncServiceResolveTests"/>, which
/// test the two halves of one round trip — the connection ids AccountService emits are the ones
/// BankSyncService has to resolve back. They only prove that if both run against the same provider.
/// </remarks>
public sealed class FakeBankSyncProvider : IBankSyncProvider
{
    public FakeBankSyncProvider(string sourceType) => SourceType = sourceType;

    public string SourceType { get; }
    public bool Enabled => true;
    public TimeSpan MinSyncInterval => TimeSpan.FromHours(6);
    public string GetConnectionKey(Account account) => account.ConnectionDetailsJson ?? "";

    public Task<ProviderFetchResult> FetchTransactionsForConnectionAsync(
        IReadOnlyList<Account> accounts, DateTime? since, bool fullResync = false, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>The registered providers both suites resolve against: SimpleFIN only, so "Plaid" is unknown.</summary>
    public static readonly IReadOnlyDictionary<string, IBankSyncProvider> Registered =
        new Dictionary<string, IBankSyncProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["SimpleFIN"] = new FakeBankSyncProvider("SimpleFIN"),
        };

    public static Account Account(int id, string sourceType, string? connection, string name = "acct") => new()
    {
        Id = id,
        UserId = 1,
        Name = $"{name}{id}",
        SourceType = sourceType,
        ConnectionDetailsJson = connection,
    };
}
