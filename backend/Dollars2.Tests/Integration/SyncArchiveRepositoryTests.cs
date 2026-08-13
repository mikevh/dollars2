using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dollars2.Api.Data;
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
    public async Task ArchiveAsync_writes_one_item_per_raw_response_body_sharing_one_fetch_id()
    {
        var repository = await RepositoryForAsync("archive-write");
        var syncedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

        await repository.ArchiveAsync(
            "Plaid",
            ["""{"page":1}""", """{"page":2}"""],
            syncedAt,
            TestContext.Current.CancellationToken);

        var items = await ScanAsync();
        Assert.Equal(2, items.Count);

        // Same prefix (provider + instant + fetch id) on both, distinguished only by the page suffix —
        // proves one fetch id was generated and shared across every item the call produced.
        var prefixes = items.Select(i => i["pk"].S[..i["pk"].S.LastIndexOf('#')]).Distinct().ToArray();
        var prefix = Assert.Single(prefixes);
        Assert.Matches(@"^Plaid#2026-08-01T06:00:00\.000Z#[0-9a-f]{8}$", prefix);

        var first = Single(items, $"{prefix}#0000");
        Assert.Equal("""{"page":1}""", first["rawJson"].S);

        var second = Single(items, $"{prefix}#0001");
        Assert.Equal("""{"page":2}""", second["rawJson"].S);
    }

    [Fact]
    public async Task ArchiveAsync_writes_a_single_raw_response_body_without_a_page_suffix()
    {
        var repository = await RepositoryForAsync("archive-single");
        var syncedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

        await repository.ArchiveAsync("SimpleFIN", ["""{"accounts":[]}"""], syncedAt, TestContext.Current.CancellationToken);

        var item = Assert.Single(await ScanAsync());
        Assert.Matches(@"^SimpleFIN#2026-08-01T06:00:00\.000Z#[0-9a-f]{8}$", item["pk"].S);
        Assert.Equal("""{"accounts":[]}""", item["rawJson"].S);
    }

    [Fact]
    public async Task ArchiveAsync_does_not_collide_when_two_fetches_share_a_provider_and_instant()
    {
        // The regression this guards: before the fetch id existed, two connection-level fetches of the
        // same provider landing in the same millisecond (e.g. concurrent "Sync Now" requests from
        // different users — SyncLockService only serializes syncs per user) collided on an identical
        // {sourceType}#{instant} key and one silently overwrote the other.
        var repository = await RepositoryForAsync("archive-concurrent");
        var syncedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

        await repository.ArchiveAsync("SimpleFIN", ["""{"user":1}"""], syncedAt, TestContext.Current.CancellationToken);
        await repository.ArchiveAsync("SimpleFIN", ["""{"user":2}"""], syncedAt, TestContext.Current.CancellationToken);

        var items = await ScanAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(
            ["""{"user":1}""", """{"user":2}"""],
            items.Select(i => i["rawJson"].S).Order().ToArray());
    }

    [Fact]
    public async Task ArchiveAsync_does_nothing_when_there_are_no_raw_response_bodies()
    {
        var repository = await RepositoryForAsync("archive-empty");

        await repository.ArchiveAsync("SimpleFIN", [], DateTime.UtcNow, TestContext.Current.CancellationToken);

        Assert.Empty(await ScanAsync());
    }

    [Fact]
    public void BuildItems_stamps_an_unspecified_kind_instant_as_UTC_rather_than_shifting_it()
    {
        // ToUniversalTime would read an Unspecified DateTime as local time and move the key by the
        // machine's offset, so the same run would key differently depending on where it ran.
        var fetchId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var items = SyncArchiveRepository.BuildItems(
            "SimpleFIN",
            ["""{"accounts":[]}"""],
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Unspecified),
            fetchId);

        var item = Assert.Single(items);
        Assert.Equal("SimpleFIN#2026-08-01T06:00:00.000Z#11111111", item["pk"].S);
    }

    [Fact]
    public void BuildItems_shares_one_fetch_id_across_every_item_and_still_orders_pages()
    {
        var fetchId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var items = SyncArchiveRepository.BuildItems(
            "Plaid",
            ["""{"page":1}""", """{"page":2}"""],
            new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
            fetchId);

        Assert.Equal(
            [
                "Plaid#2026-08-01T06:00:00.000Z#aaaaaaaa#0000",
                "Plaid#2026-08-01T06:00:00.000Z#aaaaaaaa#0001",
            ],
            items.Select(i => i["pk"].S).ToArray());
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
        string partitionKey)
    {
        return Assert.Single(items, item => item["pk"].S == partitionKey);
    }

    /// <summary>
    /// Creates a table of its own for the test and returns a repository pointed at it. Each test gets a
    /// distinct table so the shared container needs no cleanup between them.
    /// </summary>
    private async Task<SyncArchiveRepository> RepositoryForAsync(string tableName)
    {
        _tableName = tableName;
        var options = new DynamoDbOptions { TableName = tableName, ServiceUrl = _serviceUrl };

        // Provisioned through the initializer so these tests write against the real schema.
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
