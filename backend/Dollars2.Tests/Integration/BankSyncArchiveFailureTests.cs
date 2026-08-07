using System.Diagnostics;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;
using Microsoft.Extensions.Logging;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves against real MSSQL that sync archiving is genuinely best-effort: with DynamoDB unreachable,
/// a sync still imports its transactions, logs a warning, and surfaces no exception. The archive is a
/// forensic record, and losing it must never cost the user a transaction in their budget.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BankSyncArchiveFailureTests : IDisposable
{
    /// <summary>
    /// Port 1 is privileged and nothing is listening, so the connection is refused outright — the
    /// "dynamodb container isn't running" case.
    /// </summary>
    private const string DeadServiceUrl = "http://localhost:1";

    private static readonly DateOnly TransactionDate = new(2026, 8, 1);

    private readonly MsSqlContainerFixture _fixture;

    /// <summary>
    /// Mirrors the client Program.cs registers — same credentials, same region, and deliberately the
    /// same SDK defaults for timeout and retries — but pointed at nothing. The whole point is what
    /// happens when the real client cannot reach DynamoDB.
    /// </summary>
    private readonly AmazonDynamoDBClient _unreachable = new(
        new BasicAWSCredentials("local", "local"),
        new AmazonDynamoDBConfig { ServiceURL = DeadServiceUrl, AuthenticationRegion = "us-east-1" });

    public BankSyncArchiveFailureTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public void Dispose()
    {
        _unreachable.Dispose();
    }

    [Fact]
    public async Task Transactions_still_import_when_the_archive_store_is_unreachable()
    {
        using var db = _fixture.CreateSession();
        var (userId, accountId) = await SeedAsync(db, "archive-outage@example.com");

        var logger = new CapturingLogger<BankSyncService>();
        var provider = new StubProvider(new ProviderSyncResult(
            [Synced("abc123"), Synced("def456")],
            [],
            UpdatedConnectionDetailsJson: null));
        var service = BuildService(db, provider, logger);

        var elapsed = Stopwatch.StartNew();
        var results = await service.SyncForUserAsync(userId, cancellationToken: TestContext.Current.CancellationToken);
        elapsed.Stop();

        // The sync reports success — a failed archive is not a failed sync.
        var result = Assert.Single(results);
        Assert.Equal(SyncConstants.StatusSuccess, result.Status);
        Assert.Equal(2, result.TransactionCount);
        Assert.Null(result.ErrorMessage);

        // And the transactions are actually in the database.
        var imported = await db.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Transactions WHERE AccountId = @accountId",
            new { accountId });
        Assert.Equal(2, imported);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Could not archive sync payloads", warning.Message);
        Assert.Contains(accountId.ToString(), warning.Message);

        // Bounded by the repository's own Timeout rather than the SDK's retry budget. Generous headroom
        // over the 2s budget set below — the point is that it is bounded at all, not that it is punctual.
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(30),
            $"The sync took {elapsed.Elapsed.TotalSeconds:0.0}s against an unreachable archive; it is not honoring SyncArchiveRepository.Timeout.");
    }

    [Fact]
    public async Task A_failing_account_is_still_recorded_as_a_failure_when_the_archive_store_is_unreachable()
    {
        using var db = _fixture.CreateSession();
        var (userId, accountId) = await SeedAsync(db, "archive-outage-error@example.com");

        // The archive is attempted on the error path too, so this is the case where an archive failure
        // could plausibly mask the real one.
        var logger = new CapturingLogger<BankSyncService>();
        var provider = new StubProvider(new ProviderSyncResult([], [], null, Error: "connection details are misconfigured"));
        var service = BuildService(db, provider, logger);

        var results = await service.SyncForUserAsync(userId, cancellationToken: TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(SyncConstants.StatusFailure, result.Status);
        Assert.Equal("connection details are misconfigured", result.ErrorMessage);

        var logged = await db.Connection.QuerySingleAsync<string>(
            "SELECT TOP 1 ErrorMessage FROM SyncLog WHERE AccountId = @accountId ORDER BY Id DESC",
            new { accountId });
        Assert.Equal("connection details are misconfigured", logged);
    }

    private BankSyncService BuildService(DbSession db, IBankSyncProvider provider, ILogger<BankSyncService> logger)
    {
        var archiveRepo = new SyncArchiveRepository(
            _unreachable,
            new DynamoDbOptions { TableName = "unreachable", ServiceUrl = DeadServiceUrl })
        {
            // Short bound so the test doesn't sit through the SDK's retry budget. That the bound is
            // honored at all is what the elapsed-time assertion checks.
            Timeout = TimeSpan.FromSeconds(2),
        };

        return new BankSyncService(
            db,
            new AccountRepository(db),
            new TransactionRepository(db),
            new SyncLogRepository(db),
            new AccountBalanceRepository(db),
            archiveRepo,
            [provider],
            new SyncLockService(),
            logger);
    }

    private static SyncedTransaction Synced(string providerTransactionId)
    {
        return new SyncedTransaction(
            providerTransactionId,
            TransactionDate,
            "Coffee",
            "Beans",
            "",
            -4.25m,
            IsPending: false,
            RawJson: $$"""{"id":"{{providerTransactionId}}"}""");
    }

    private static async Task<(int UserId, int AccountId)> SeedAsync(DbSession db, string email)
    {
        var userId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email });

        var accountId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, 'Checking', 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId });

        return (userId, accountId);
    }

    /// <summary>Returns one canned result for every account, without touching a network.</summary>
    private sealed class StubProvider : IBankSyncProvider
    {
        private readonly ProviderSyncResult _result;

        public StubProvider(ProviderSyncResult result)
        {
            _result = result;
        }

        public string SourceType => SyncConstants.SourceTypeSimpleFin;

        public bool Enabled => true;

        public TimeSpan MinSyncInterval => TimeSpan.Zero;

        public string GetConnectionKey(Account account) => "stub-connection";

        public Task<IReadOnlyDictionary<int, ProviderSyncResult>> FetchTransactionsForConnectionAsync(
            IReadOnlyList<Account> accounts,
            DateTime? since,
            bool fullResync = false,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, ProviderSyncResult> results =
                accounts.ToDictionary(a => a.Id, _ => _result);
            return Task.FromResult(results);
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
