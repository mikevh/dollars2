using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises <see cref="SyncArchiveRepository"/> against a throwaway <c>amazon/dynamodb-local</c>
/// container — the same image the app runs in development and on the home server. Each test creates its
/// own table, so the shared instance needs no cleanup between them.
/// Requires a running Docker daemon on the dev machine.
/// </summary>
public sealed class SyncArchiveRepositoryTests : IAsyncLifetime
{
    private const ushort DynamoDbPort = 8000;

    private static readonly DateOnly TransactionDate = new(2026, 8, 1);

    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local")
        .WithEntrypoint("java")
        .WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory")
        .WithPortBinding(DynamoDbPort, true)
        // A bare GET against DynamoDB answers 400, so 400 — not 2xx — is what "ready" looks like. See
        // SyncArchiveTableInitializerTests for why a TCP port check is not enough.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(DynamoDbPort)
            .ForStatusCode(HttpStatusCode.BadRequest)))
        .Build();

    private IAmazonDynamoDB _dynamoDb = null!;
    private string _serviceUrl = "";
    private string _tableName = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _serviceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(DynamoDbPort)}";
        _dynamoDb = CreateClient(_serviceUrl);
    }

    public async ValueTask DisposeAsync()
    {
        _dynamoDb?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task ArchiveAsync_writes_one_item_per_payload_all_sharing_the_sync_run()
    {
        var repository = await RepositoryForAsync("archive-write");
        var account = AccountFor(userId: 7, accountId: 42);
        var syncRunId = Guid.NewGuid();
        var syncedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        var result = new ProviderSyncResult(
            [Synced("abc123", pending: true), Synced("def456", pending: false)],
            ["gone789"],
            UpdatedConnectionDetailsJson: null,
            AccountMetadataJson: """{"id":"acct-1","balance":"100.00"}""",
            ErrorsJson: ["""{"code":"AUTH_FAILED"}"""],
            SkippedTransactionsJson: ["""{"id":"bad1","amount":"not-a-number"}"""]);

        await repository.ArchiveAsync(account, result, syncRunId, syncedAt, TestContext.Current.CancellationToken);

        var items = await ScanAsync();

        // Every item lands in the account's partition and carries the run that produced it — that is
        // what lets the archive page group a run back together.
        Assert.All(items, item => Assert.Equal("USER#7#ACCT#42", item["pk"].S));
        Assert.All(items, item => Assert.Equal(syncRunId.ToString(), item["syncRunId"].S));
        Assert.All(items, item => Assert.Equal("2026-08-01T06:00:00.000Z", item["syncedAt"].S));
        Assert.All(items, item => Assert.Equal("SimpleFIN", item["sourceType"].S));
        Assert.All(items, item => Assert.Equal("7", item["userId"].N));
        Assert.All(items, item => Assert.Equal("42", item["accountId"].N));

        Assert.Equal(
            [
                "ACCTMETA#2026-08-01T06:00:00.000Z",
                "ERROR#2026-08-01T06:00:00.000Z#0000",
                "REMOVED#gone789#2026-08-01T06:00:00.000Z",
                "SKIPPED#2026-08-01T06:00:00.000Z#0000",
                "TXN#abc123#2026-08-01T06:00:00.000Z",
                "TXN#def456#2026-08-01T06:00:00.000Z",
            ],
            items.Select(i => i["sk"].S).Order().ToArray());

        var transaction = Single(items, "TXN#abc123#2026-08-01T06:00:00.000Z");
        Assert.Equal("Transaction", transaction["itemType"].S);
        Assert.Equal("abc123", transaction["providerTransactionId"].S);
        Assert.Equal(RawJsonFor("abc123", pending: true), transaction["rawJson"].S);

        var removed = Single(items, "REMOVED#gone789#2026-08-01T06:00:00.000Z");
        Assert.Equal("Removed", removed["itemType"].S);
        Assert.Equal("gone789", removed["providerTransactionId"].S);
        // The id is the whole payload a removal carries, and it already has its own attribute.
        Assert.False(removed.ContainsKey("rawJson"));

        var metadata = Single(items, "ACCTMETA#2026-08-01T06:00:00.000Z");
        Assert.Equal("AccountMetadata", metadata["itemType"].S);
        Assert.Equal("""{"id":"acct-1","balance":"100.00"}""", metadata["rawJson"].S);
        Assert.False(metadata.ContainsKey("providerTransactionId"));

        var error = Single(items, "ERROR#2026-08-01T06:00:00.000Z#0000");
        Assert.Equal("ProviderError", error["itemType"].S);
        Assert.Equal("""{"code":"AUTH_FAILED"}""", error["rawJson"].S);

        var skipped = Single(items, "SKIPPED#2026-08-01T06:00:00.000Z#0000");
        Assert.Equal("SkippedTransaction", skipped["itemType"].S);
        Assert.Equal("""{"id":"bad1","amount":"not-a-number"}""", skipped["rawJson"].S);
    }

    [Fact]
    public async Task Re_archiving_a_transaction_adds_a_version_instead_of_overwriting_it()
    {
        var repository = await RepositoryForAsync("archive-versions");
        var account = AccountFor(userId: 1, accountId: 1);
        var firstSeen = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);
        var thenPosted = new DateTime(2026, 8, 3, 6, 0, 0, DateTimeKind.Utc);

        await repository.ArchiveAsync(
            account,
            new ProviderSyncResult([Synced("abc123", pending: true)], [], null),
            Guid.NewGuid(),
            firstSeen,
            TestContext.Current.CancellationToken);

        await repository.ArchiveAsync(
            account,
            new ProviderSyncResult([Synced("abc123", pending: false)], [], null),
            Guid.NewGuid(),
            thenPosted,
            TestContext.Current.CancellationToken);

        // Two sightings of one transaction must be two items. Overwriting is what would make the
        // pending→posted transition — the thing this archive exists to show — invisible.
        var items = await ScanAsync();
        Assert.Equal(2, items.Count);

        var versions = items
            .OrderBy(i => i["syncedAt"].S, StringComparer.Ordinal)
            .Select(i => i["rawJson"].S)
            .ToArray();
        Assert.Equal(RawJsonFor("abc123", pending: true), versions[0]);
        Assert.Equal(RawJsonFor("abc123", pending: false), versions[1]);
    }

    [Fact]
    public async Task ArchiveAsync_writes_every_item_when_the_result_exceeds_one_batch()
    {
        var repository = await RepositoryForAsync("archive-chunking");
        // 60 transactions is three BatchWriteItem requests: DynamoDB caps one at 25 items and rejects
        // anything larger outright, so an unchunked write would land nothing at all.
        var upserts = Enumerable.Range(0, 60).Select(i => Synced($"txn-{i:D3}", pending: false)).ToList();

        await repository.ArchiveAsync(
            AccountFor(userId: 1, accountId: 1),
            new ProviderSyncResult(upserts, [], null),
            Guid.NewGuid(),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        var items = await ScanAsync();
        Assert.Equal(60, items.Count);
        Assert.Equal(
            upserts.Select(u => u.ProviderTransactionId).Order().ToArray(),
            items.Select(i => i["providerTransactionId"].S).Order().ToArray());
    }

    [Fact]
    public async Task A_transaction_reported_twice_in_one_payload_does_not_cost_the_whole_batch()
    {
        var repository = await RepositoryForAsync("archive-duplicates");
        // DynamoDB rejects a BatchWriteItem containing two items with the same key as a whole request,
        // so a provider echoing one transaction twice would otherwise lose the entire run's archive.
        var result = new ProviderSyncResult(
            [Synced("abc123", pending: true), Synced("abc123", pending: false), Synced("def456", pending: false)],
            [],
            null);

        await repository.ArchiveAsync(
            AccountFor(userId: 1, accountId: 1),
            result,
            Guid.NewGuid(),
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
            TestContext.Current.CancellationToken);

        var items = await ScanAsync();
        Assert.Equal(2, items.Count);

        // Same key, so only the payload can differ — the last sighting is the one kept.
        var deduped = Single(items, "TXN#abc123#2026-08-01T06:00:00.000Z");
        Assert.Equal(RawJsonFor("abc123", pending: false), deduped["rawJson"].S);
    }

    [Fact]
    public async Task ArchiveAsync_does_nothing_when_the_provider_reported_nothing()
    {
        var repository = await RepositoryForAsync("archive-empty");

        await repository.ArchiveAsync(
            AccountFor(userId: 1, accountId: 1),
            new ProviderSyncResult([], [], null),
            Guid.NewGuid(),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        Assert.Empty(await ScanAsync());
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_returns_every_sighting_newest_first()
    {
        var repository = await RepositoryForAsync("history-versions");
        var account = AccountFor(userId: 7, accountId: 42);
        var instants = new[]
        {
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 3, 6, 0, 0, DateTimeKind.Utc),
        };
        var runIds = new List<Guid>();

        foreach (var (instant, index) in instants.Select((i, n) => (i, n)))
        {
            var syncRunId = Guid.NewGuid();
            runIds.Add(syncRunId);
            await repository.ArchiveAsync(
                account,
                new ProviderSyncResult([Synced("abc123", pending: index < 2)], [], null),
                syncRunId,
                instant,
                TestContext.Current.CancellationToken);
        }

        var history = await repository.GetTransactionHistoryAsync(
            userId: 7,
            accountId: 42,
            "abc123",
            TestContext.Current.CancellationToken);

        Assert.Equal(3, history.Count);

        // Newest first is the whole point: the dialog leads with what the provider says now, and the
        // older sightings are the trail behind it.
        Assert.Equal(instants.Reverse().ToArray(), history.Select(e => e.SyncedAt).ToArray());
        // Kind has to survive the round trip through the sort key, or the global UtcDateTimeConverter
        // would be reinterpreting a value it was never told the zone of.
        Assert.All(history, e => Assert.Equal(DateTimeKind.Utc, e.SyncedAt.Kind));

        Assert.Equal(runIds.Select(r => r.ToString()).Reverse().ToArray(), history.Select(e => e.SyncRunId).ToArray());
        Assert.All(history, e => Assert.Equal("SimpleFIN", e.SourceType));

        // Verbatim, not re-serialized — the point of archiving the payload at all.
        Assert.Equal(RawJsonFor("abc123", pending: false), history[0].RawJson);
        Assert.Equal(RawJsonFor("abc123", pending: true), history[1].RawJson);
        Assert.Equal(RawJsonFor("abc123", pending: true), history[2].RawJson);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_returns_only_the_requested_transaction()
    {
        var repository = await RepositoryForAsync("history-isolation");
        var syncedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

        // "abc" and "abc#extra" share a sort-key prefix: the key for the latter is
        // "TXN#abc#extra#<instant>", which begins_with("TXN#abc#") matches. Only the filter on the
        // providerTransactionId attribute tells them apart.
        await repository.ArchiveAsync(
            AccountFor(userId: 7, accountId: 42),
            new ProviderSyncResult(
                [Synced("abc", pending: false), Synced("abc#extra", pending: false), Synced("zzz", pending: false)],
                ["abc"],
                null),
            Guid.NewGuid(),
            syncedAt,
            TestContext.Current.CancellationToken);

        // Another account's partition, same transaction id.
        await repository.ArchiveAsync(
            AccountFor(userId: 7, accountId: 43),
            new ProviderSyncResult([Synced("abc", pending: false)], [], null),
            Guid.NewGuid(),
            syncedAt,
            TestContext.Current.CancellationToken);

        var history = await repository.GetTransactionHistoryAsync(
            userId: 7,
            accountId: 42,
            "abc",
            TestContext.Current.CancellationToken);

        // One entry: not the prefix-sharing id, not the other account, and not the REMOVED# sighting
        // of this same id — the history is the TXN# prefix only.
        var entry = Assert.Single(history);
        Assert.Equal(RawJsonFor("abc", pending: false), entry.RawJson);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_returns_empty_when_nothing_was_ever_archived()
    {
        var repository = await RepositoryForAsync("history-empty");

        await repository.ArchiveAsync(
            AccountFor(userId: 7, accountId: 42),
            new ProviderSyncResult([Synced("abc123", pending: false)], [], null),
            Guid.NewGuid(),
            DateTime.UtcNow,
            TestContext.Current.CancellationToken);

        var history = await repository.GetTransactionHistoryAsync(
            userId: 7,
            accountId: 42,
            "never-seen",
            TestContext.Current.CancellationToken);

        Assert.Empty(history);
    }

    [Fact]
    public void BuildItems_stamps_an_unspecified_kind_instant_as_UTC_rather_than_shifting_it()
    {
        // ToUniversalTime would read an Unspecified DateTime as local time and move the sort key by the
        // machine's offset, so the same run would key differently depending on where it ran.
        var items = SyncArchiveRepository.BuildItems(
            AccountFor(userId: 1, accountId: 1),
            new ProviderSyncResult([Synced("abc123", pending: false)], [], null),
            Guid.NewGuid(),
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Unspecified));

        var item = Assert.Single(items);
        Assert.Equal("2026-08-01T06:00:00.000Z", item["syncedAt"].S);
        Assert.Equal("TXN#abc123#2026-08-01T06:00:00.000Z", item["sk"].S);
    }

    /// <summary>
    /// Mirrors the client Program.cs registers — same credentials, same region, and deliberately the
    /// same SDK defaults for timeout and retries, so what these tests write goes through the client the
    /// app actually has.
    /// </summary>
    private static IAmazonDynamoDB CreateClient(string serviceUrl)
    {
        return new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "us-east-1",
            });
    }

    private static Dictionary<string, AttributeValue> Single(
        IReadOnlyList<Dictionary<string, AttributeValue>> items,
        string sortKey)
    {
        return Assert.Single(items, item => item["sk"].S == sortKey);
    }

    private static SyncedTransaction Synced(string providerTransactionId, bool pending)
    {
        return new SyncedTransaction(
            providerTransactionId,
            TransactionDate,
            "Coffee",
            "Beans",
            "",
            -4.25m,
            pending,
            RawJsonFor(providerTransactionId, pending));
    }

    private static string RawJsonFor(string providerTransactionId, bool pending)
    {
        return $$"""{"id":"{{providerTransactionId}}","pending":{{(pending ? "true" : "false")}}}""";
    }

    private static Account AccountFor(int userId, int accountId)
    {
        return new Account
        {
            Id = accountId,
            UserId = userId,
            Name = "Checking",
            SourceType = SyncConstants.SourceTypeSimpleFin,
        };
    }

    /// <summary>
    /// Creates a table of its own for the test and returns a repository pointed at it. Each test gets a
    /// distinct table so the shared container needs no cleanup between them.
    /// </summary>
    private async Task<SyncArchiveRepository> RepositoryForAsync(string tableName)
    {
        _tableName = tableName;
        var options = new DynamoDbOptions { TableName = tableName, ServiceUrl = _serviceUrl };

        // Provisioned through the initializer so these tests write against the real schema — the sort
        // keys are only meaningful against the key schema the app actually creates.
        var initializer = new SyncArchiveTableInitializer(_dynamoDb, options, NullLogger<SyncArchiveTableInitializer>.Instance);
        await initializer.StartAsync(TestContext.Current.CancellationToken);

        return new SyncArchiveRepository(_dynamoDb, options);
    }

    /// <summary>
    /// Reads back everything in the table under test. A scan rather than a query on purpose: each test
    /// owns its table and writes a handful of items, and a scan would also surface anything written into
    /// an unexpected partition, which a query keyed by the expected pk would quietly hide.
    /// </summary>
    private async Task<IReadOnlyList<Dictionary<string, AttributeValue>>> ScanAsync()
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? startKey = null;

        do
        {
            var response = await _dynamoDb.ScanAsync(
                new ScanRequest { TableName = _tableName, ExclusiveStartKey = startKey },
                TestContext.Current.CancellationToken);

            items.AddRange(response.Items);
            startKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        }
        while (startKey is not null);

        return items;
    }
}
