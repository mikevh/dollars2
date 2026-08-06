using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
    private string _serviceUrl = "";

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

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("already exists"));
    }

    [Fact]
    public async Task StartAsync_logs_an_error_and_keeps_going_when_the_connection_is_refused()
    {
        // Port 1 is privileged and nothing is listening, so the connection is refused outright — the
        // "dynamodb container isn't running" case. Same client config the app uses; no retry override.
        const string deadUrl = "http://localhost:1";
        using var unreachable = CreateClient(deadUrl);
        var logger = new CapturingLogger<SyncArchiveTableInitializer>();
        // Short bound so this doesn't sit through the SDK's retry budget — that the bound is honored
        // at all is the black-hole test's job; this one is about what gets logged.
        var initializer = new SyncArchiveTableInitializer(unreachable, OptionsFor("archive-refused", deadUrl), logger)
        {
            StartupTimeout = TimeSpan.FromSeconds(2),
        };

        // Startup must survive this: sync archiving is best-effort and must never be able to stop the
        // app from serving budgets.
        await initializer.StartAsync(TestContext.Current.CancellationToken);

        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.NotNull(error.Exception);
        Assert.Contains(deadUrl, error.Message);
    }

    [Fact]
    public async Task StartAsync_gives_up_within_its_own_timeout_when_dynamodb_accepts_but_never_answers()
    {
        // The dangerous failure is not a refused connection — that fails fast on its own. It is a
        // wedged DynamoDB that completes the TCP handshake and then goes silent, because the client
        // Program.cs registers sets neither Timeout nor MaxErrorRetry: without a bound of its own,
        // StartAsync would sit through the SDK's full retry budget while the host serves nothing.
        using var blackHole = new BlackHoleListener();
        using var client = CreateClient(blackHole.ServiceUrl);
        var logger = new CapturingLogger<SyncArchiveTableInitializer>();
        var initializer = new SyncArchiveTableInitializer(client, OptionsFor("archive-blackhole", blackHole.ServiceUrl), logger)
        {
            StartupTimeout = TimeSpan.FromSeconds(2),
        };

        var elapsed = Stopwatch.StartNew();
        await initializer.StartAsync(TestContext.Current.CancellationToken);
        elapsed.Stop();

        // Generous headroom over the 2s budget — the point is that it is bounded at all, not that it
        // is punctual. Unbounded, this is minutes.
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(20),
            $"StartAsync took {elapsed.Elapsed.TotalSeconds:0.0}s against a black-holed endpoint; it is not honoring StartupTimeout.");

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
    /// Mirrors the client Program.cs registers — same credentials, same region, and deliberately the
    /// same SDK defaults for timeout and retries. The failure-path tests are only meaningful if the
    /// client they run against is the one the app actually has.
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

    /// <summary>
    /// The ServiceUrl has to be the real one: it is the field the error log reports, and a diagnostic
    /// naming an endpoint that never failed is worse than none.
    /// </summary>
    private DynamoDbOptions OptionsFor(string tableName)
    {
        return OptionsFor(tableName, _serviceUrl);
    }

    private static DynamoDbOptions OptionsFor(string tableName, string serviceUrl)
    {
        return new DynamoDbOptions { TableName = tableName, ServiceUrl = serviceUrl };
    }

    private async Task<TableDescription> DescribeAsync(string tableName)
    {
        var response = await _dynamoDb.DescribeTableAsync(tableName, TestContext.Current.CancellationToken);
        return response.Table;
    }

    /// <summary>
    /// A socket that completes the TCP handshake and then never writes a byte, holding every accepted
    /// connection open. Simulates a wedged DynamoDB, which is distinct from a refused connection.
    /// </summary>
    private sealed class BlackHoleListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _accepted = [];

        public BlackHoleListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            ServiceUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = AcceptForeverAsync();
        }

        public string ServiceUrl { get; }

        public void Dispose()
        {
            _listener.Stop();

            foreach (var client in _accepted)
            {
                client.Dispose();
            }

            _listener.Dispose();
        }

        private async Task AcceptForeverAsync()
        {
            try
            {
                while (true)
                {
                    _accepted.Add(await _listener.AcceptTcpClientAsync());
                }
            }
            catch (Exception)
            {
                // Disposed out from under us at the end of the test — that is the exit condition.
            }
        }
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
