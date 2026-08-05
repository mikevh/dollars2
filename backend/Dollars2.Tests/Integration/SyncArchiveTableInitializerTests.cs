using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dollars2.Api.Data;
using Dollars2.Api.Services;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Exercises <see cref="SyncArchiveTableInitializer"/> against a throwaway <c>amazon/dynamodb-local</c>
/// container — the same image the app runs in development and on the home server. Each test uses its
/// own table name, so the shared instance needs no cleanup between them.
/// Requires a running Docker daemon on the dev machine.
/// </summary>
public sealed class SyncArchiveTableInitializerTests : IAsyncLifetime
{
    private const ushort DynamoDbPort = 8000;

    // -inMemory rather than the deploy's -dbPath: nothing here should survive the container.
    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local")
        .WithEntrypoint("java")
        .WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory")
        .WithPortBinding(DynamoDbPort, true)
        // Must be an HTTP round trip, not a TCP port check: the port accepts connections before
        // DynamoDBLocal's Jetty layer will serve them, and the first request then dies mid-response.
        // A bare GET against DynamoDB answers 400, so 400 — not 2xx — is what "ready" looks like
        // (the same trap the compose healthcheck sidesteps by not passing curl -f).
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(DynamoDbPort)
            .ForStatusCode(HttpStatusCode.BadRequest)))
        .Build();

    private IAmazonDynamoDB _dynamoDb = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _dynamoDb = CreateClient($"http://{_container.Hostname}:{_container.GetMappedPublicPort(DynamoDbPort)}");
    }

    public async ValueTask DisposeAsync()
    {
        _dynamoDb?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_creates_the_table_with_the_syncedAt_local_secondary_index()
    {
        var options = OptionsFor("archive-create");
        var initializer = new SyncArchiveTableInitializer(_dynamoDb, options, new CapturingLogger<SyncArchiveTableInitializer>());

        await initializer.StartAsync(TestContext.Current.CancellationToken);

        var table = await DescribeAsync(options.TableName);
        Assert.Equal(TableStatus.ACTIVE, table.TableStatus);

        Assert.Equal(
            new[] { ("pk", KeyType.HASH), ("sk", KeyType.RANGE) },
            table.KeySchema.Select(k => (k.AttributeName, k.KeyType)).ToArray());

        // syncedAt is only ever an index key, so it has to be declared as a table attribute too.
        var attributes = table.AttributeDefinitions.ToDictionary(a => a.AttributeName, a => a.AttributeType);
        Assert.Equal(ScalarAttributeType.S, attributes["pk"]);
        Assert.Equal(ScalarAttributeType.S, attributes["sk"]);
        Assert.Equal(ScalarAttributeType.S, attributes["syncedAt"]);

        var index = Assert.Single(table.LocalSecondaryIndexes);
        Assert.Equal("LSI_SyncedAt", index.IndexName);
        Assert.Equal(
            new[] { ("pk", KeyType.HASH), ("syncedAt", KeyType.RANGE) },
            index.KeySchema.Select(k => (k.AttributeName, k.KeyType)).ToArray());
        Assert.Equal(ProjectionType.ALL, index.Projection.ProjectionType);
    }

    [Fact]
    public async Task StartAsync_finds_the_existing_table_and_does_not_recreate_it()
    {
        var options = OptionsFor("archive-idempotent");
        var logger = new CapturingLogger<SyncArchiveTableInitializer>();
        var initializer = new SyncArchiveTableInitializer(_dynamoDb, options, logger);

        await initializer.StartAsync(TestContext.Current.CancellationToken);
        var afterFirstStart = await DescribeAsync(options.TableName);

        logger.Entries.Clear();
        await initializer.StartAsync(TestContext.Current.CancellationToken);
        var afterSecondStart = await DescribeAsync(options.TableName);

        // A recreate would reset the creation timestamp — and silently discard the archive with it.
        Assert.Equal(afterFirstStart.CreationDateTime, afterSecondStart.CreationDateTime);
        Assert.Single(await ListTableNamesAsync(options.TableName));

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("already exists"));
    }

    [Fact]
    public async Task StartAsync_logs_an_error_and_keeps_going_when_dynamodb_is_unreachable()
    {
        // Port 1 is privileged and nothing is listening, so the connection is refused outright.
        // Retries off and a short timeout only so the test doesn't sit through the SDK's backoff.
        using var unreachable = CreateClient(new AmazonDynamoDBConfig
        {
            ServiceURL = "http://localhost:1",
            AuthenticationRegion = "us-east-1",
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromSeconds(5),
        });
        var logger = new CapturingLogger<SyncArchiveTableInitializer>();
        var initializer = new SyncArchiveTableInitializer(unreachable, OptionsFor("archive-unreachable"), logger);

        // Startup must survive this: sync archiving is best-effort and must never be able to stop the
        // app from serving budgets.
        await initializer.StartAsync(TestContext.Current.CancellationToken);

        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.NotNull(error.Exception);
    }

    [Fact]
    public void BuildCreateTableRequest_asks_for_on_demand_billing_with_no_provisioned_throughput()
    {
        var request = SyncArchiveTableInitializer.BuildCreateTableRequest("Dollars2SyncArchive");

        Assert.Equal(BillingMode.PAY_PER_REQUEST, request.BillingMode);
        Assert.Null(request.ProvisionedThroughput);
        Assert.All(request.LocalSecondaryIndexes, index => Assert.Equal(ProjectionType.ALL, index.Projection.ProjectionType));
    }

    /// <summary>
    /// Mirrors the client Program.cs registers, so the tests exercise the same retry and signing
    /// behavior the app has.
    /// </summary>
    private static IAmazonDynamoDB CreateClient(string serviceUrl)
    {
        return CreateClient(new AmazonDynamoDBConfig
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = "us-east-1",
        });
    }

    private static IAmazonDynamoDB CreateClient(AmazonDynamoDBConfig config)
    {
        return new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), config);
    }

    private static DynamoDbOptions OptionsFor(string tableName)
    {
        return new DynamoDbOptions { TableName = tableName, ServiceUrl = "http://test" };
    }

    private async Task<TableDescription> DescribeAsync(string tableName)
    {
        var response = await _dynamoDb.DescribeTableAsync(tableName, TestContext.Current.CancellationToken);
        return response.Table;
    }

    private async Task<IReadOnlyList<string>> ListTableNamesAsync(string tableName)
    {
        var response = await _dynamoDb.ListTablesAsync(TestContext.Current.CancellationToken);
        return (response.TableNames ?? []).Where(n => n == tableName).ToList();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
