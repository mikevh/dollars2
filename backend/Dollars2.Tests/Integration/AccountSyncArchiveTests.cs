using System.Net;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Dapper;
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
/// Exercises <see cref="AccountSyncArchiveService"/> across both of its stores at once — real MSSQL
/// resolving and authorizing the account, real <c>amazon/dynamodb-local</c> holding its archive. The
/// archive is always seeded through the real <see cref="SyncArchiveRepository.ArchiveAsync"/> writer, so
/// the keys and the LSI ordering under test are the ones a sync would actually have produced. Requires a
/// running Docker daemon on the dev machine.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountSyncArchiveTests : IAsyncLifetime
{
    private const ushort DynamoDbPort = 8000;

    /// <summary>
    /// Port 1 is privileged and nothing is listening, so the connection is refused outright — the
    /// "dynamodb container isn't running" case.
    /// </summary>
    private const string DeadServiceUrl = "http://localhost:1";

    private const string TableName = "account-sync-archive";

    private static readonly DateOnly TransactionDate = new(2026, 8, 1);

    private static readonly DateTime FirstSync = new(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

    private readonly MsSqlContainerFixture _fixture;

    private readonly IContainer _container = new ContainerBuilder("amazon/dynamodb-local")
        .WithEntrypoint("java")
        .WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory")
        .WithPortBinding(DynamoDbPort, true)
        // A bare GET against DynamoDB answers 400, so 400 — not 2xx — is what "ready" looks like.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(DynamoDbPort)
            .ForStatusCode(HttpStatusCode.BadRequest)))
        .Build();

    private IAmazonDynamoDB _dynamoDb = null!;
    private DynamoDbOptions _options = null!;

    public AccountSyncArchiveTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var serviceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(DynamoDbPort)}";
        _dynamoDb = CreateClient(serviceUrl);
        _options = new DynamoDbOptions { TableName = TableName, ServiceUrl = serviceUrl };

        // Provisioned through the initializer so these tests read against the real schema — in particular
        // the LSI this endpoint queries, which cannot be added to a table after the fact.
        var initializer = new SyncArchiveTableInitializer(_dynamoDb, _options, NullLogger<SyncArchiveTableInitializer>.Instance);
        await initializer.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _dynamoDb?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetArchivePageAsync_returns_runs_newest_first_with_every_item_type_inline()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-owner@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            var older = await ArchiveRunAsync(userId, accountId, FirstSync, transactionIds: ["abc123"]);
            var newer = await ArchiveRunAsync(
                userId,
                accountId,
                FirstSync.AddDays(1),
                transactionIds: ["abc123", "def456"],
                removedIds: ["gone789"],
                accountMetadataJson: """{"balance":"12.34"}""",
                errorsJson: ["""{"error":"rate limited"}"""],
                skippedJson: ["""{"id":"bad1","amount":"not-a-number"}"""]);

            var result = await BuildService(db).GetArchivePageAsync(accountId, userId, before: null, limit: null, TestContext.Current.CancellationToken);

            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Null(result.Data!.NextBefore);
            Assert.Equal(2, result.Data.Runs.Count);

            // Newest run first.
            var run = result.Data.Runs[0];
            Assert.Equal(newer.ToString(), run.SyncRunId);
            Assert.Equal(FirstSync.AddDays(1), run.SyncedAt);
            Assert.Equal(DateTimeKind.Utc, run.SyncedAt.Kind);
            Assert.Equal(SyncConstants.SourceTypeSimpleFin, run.SourceType);

            Assert.Equal(2, run.TransactionCount);
            Assert.Equal(1, run.RemovedCount);
            Assert.Equal(1, run.ErrorCount);
            Assert.Equal(1, run.SkippedCount);

            // The metadata payload is hoisted for the client, and the item it came from is still in Items —
            // Items is the complete list of what the run archived.
            Assert.Equal("""{"balance":"12.34"}""", run.AccountMetadataJson);
            Assert.Equal(6, run.Items.Count);

            // Ordered by sort key, which is what makes a redraw show the same order twice.
            Assert.Equal(
                new[]
                {
                    SyncArchiveRepository.ItemTypeAccountMetadata,
                    SyncArchiveRepository.ItemTypeProviderError,
                    SyncArchiveRepository.ItemTypeRemoved,
                    SyncArchiveRepository.ItemTypeSkippedTransaction,
                    SyncArchiveRepository.ItemTypeTransaction,
                    SyncArchiveRepository.ItemTypeTransaction,
                },
                run.Items.Select(i => i.ItemType).ToArray());

            // Payloads come back byte-for-byte as the provider sent them.
            var transactions = run.Items.Where(i => i.ItemType == SyncArchiveRepository.ItemTypeTransaction).ToList();
            Assert.Equal(new[] { "abc123", "def456" }, transactions.Select(i => i.ProviderTransactionId!).ToArray());
            Assert.Equal(RawJsonFor("abc123"), transactions[0].RawJson);

            // A removal's whole payload is its id, so there is no verbatim JSON to hand back and none is
            // invented.
            var removed = run.Items.Single(i => i.ItemType == SyncArchiveRepository.ItemTypeRemoved);
            Assert.Equal("gone789", removed.ProviderTransactionId);
            Assert.Null(removed.RawJson);

            var error = run.Items.Single(i => i.ItemType == SyncArchiveRepository.ItemTypeProviderError);
            Assert.Equal("""{"error":"rate limited"}""", error.RawJson);
            Assert.Null(error.ProviderTransactionId);

            var skipped = run.Items.Single(i => i.ItemType == SyncArchiveRepository.ItemTypeSkippedTransaction);
            Assert.Equal("""{"id":"bad1","amount":"not-a-number"}""", skipped.RawJson);

            var second = result.Data.Runs[1];
            Assert.Equal(older.ToString(), second.SyncRunId);
            Assert.Equal(FirstSync, second.SyncedAt);
            Assert.Equal(1, second.TransactionCount);
            Assert.Equal(0, second.RemovedCount);
            Assert.Null(second.AccountMetadataJson);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_pages_backwards_without_gaps_repeats_or_split_runs()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-paging@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            // Five runs, each with several items, so a page boundary landing mid-run would show up.
            for (var i = 0; i < 5; i++)
            {
                await ArchiveRunAsync(
                    userId,
                    accountId,
                    FirstSync.AddDays(i),
                    transactionIds: [$"txn{i}a", $"txn{i}b", $"txn{i}c"],
                    removedIds: [$"gone{i}"]);
            }

            var service = BuildService(db);
            var unpaged = await service.GetArchivePageAsync(accountId, userId, before: null, limit: null, TestContext.Current.CancellationToken);
            Assert.Null(unpaged.Error);
            Assert.Equal(5, unpaged.Data!.Runs.Count);

            var walked = new List<SyncArchiveRunResponse>();
            string? before = null;
            var pages = 0;

            do
            {
                var page = await service.GetArchivePageAsync(accountId, userId, before, limit: 2, TestContext.Current.CancellationToken);
                Assert.Null(page.Error);

                walked.AddRange(page.Data!.Runs);
                before = page.Data.NextBefore?.ToString("O");
                pages++;

                // Guards against a cursor that fails to advance turning this into an infinite loop.
                Assert.True(pages <= 5, "Paging did not terminate.");
            }
            while (before is not null);

            // Two runs, two runs, then the last one — and nothing after it.
            Assert.Equal(3, pages);

            // No gaps and no repeats: walking the pages reproduces the unpaged read exactly.
            Assert.Equal(
                unpaged.Data.Runs.Select(r => r.SyncRunId).ToArray(),
                walked.Select(r => r.SyncRunId).ToArray());
            Assert.Equal(
                unpaged.Data.Runs.Select(r => r.SyncedAt).ToArray(),
                walked.Select(r => r.SyncedAt).ToArray());

            // No run was split across a page boundary: every one still carries all four of its items.
            Assert.All(walked, run =>
            {
                Assert.Equal(4, run.Items.Count);
                Assert.Equal(3, run.TransactionCount);
                Assert.Equal(1, run.RemovedCount);
            });
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_keeps_a_run_whole_across_dynamodb_response_pages()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-bigrun@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            // DynamoDB caps a Query response at 1MB and hands back a LastEvaluatedKey for the rest, so
            // these payloads are sized to push one run past that. Without the walk over LastEvaluatedKey
            // the run would come back with only the items that fit in the first response — the exact
            // failure "a run is never split" is about, and the one no other test here can produce.
            const int itemCount = 40;
            var padding = new string('x', 40_000);
            var bigRunIds = Enumerable.Range(0, itemCount).Select(i => $"big{i:D3}").ToList();

            var older = await ArchiveRunAsync(userId, accountId, FirstSync, transactionIds: ["small"]);
            var big = await ArchiveRunAsync(
                userId,
                accountId,
                FirstSync.AddDays(1),
                transactionIds: bigRunIds,
                rawJsonPadding: padding);

            var service = BuildService(db);
            var result = await service.GetArchivePageAsync(accountId, userId, before: null, limit: null, TestContext.Current.CancellationToken);

            Assert.Null(result.Error);
            Assert.Equal(2, result.Data!.Runs.Count);

            // Every item of the oversized run made it back, in one run, on one page.
            var run = result.Data.Runs[0];
            Assert.Equal(big.ToString(), run.SyncRunId);
            Assert.Equal(itemCount, run.TransactionCount);
            Assert.Equal(itemCount, run.Items.Count);
            Assert.Equal(bigRunIds, run.Items.Select(i => i.ProviderTransactionId!).ToList());
            Assert.All(run.Items, item => Assert.Contains(padding, item.RawJson));

            // And the run behind it is still there — the multi-page walk did not stop the read short.
            Assert.Equal(older.ToString(), result.Data.Runs[1].SyncRunId);
            Assert.Null(result.Data.NextBefore);

            // The same holds when the run is the last one that fits: the page ends on its boundary with
            // every one of its items, not on the DynamoDB response boundary in the middle of it.
            var firstPage = await service.GetArchivePageAsync(accountId, userId, before: null, limit: 1, TestContext.Current.CancellationToken);

            Assert.Null(firstPage.Error);
            Assert.Single(firstPage.Data!.Runs);
            Assert.Equal(itemCount, firstPage.Data.Runs[0].Items.Count);
            Assert.Equal(FirstSync.AddDays(1), firstPage.Data.NextBefore);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_clamps_a_limit_above_the_maximum()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-clamp@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            // One more run than a page can hold, so an unclamped limit would return them all.
            var runCount = AccountSyncArchiveService.MaxRunLimit + 1;
            for (var i = 0; i < runCount; i++)
            {
                await ArchiveRunAsync(userId, accountId, FirstSync.AddMinutes(i), transactionIds: [$"txn{i}"]);
            }

            var result = await BuildService(db).GetArchivePageAsync(
                accountId,
                userId,
                before: null,
                limit: 10_000,
                TestContext.Current.CancellationToken);

            // Clamped, not rejected: asking for more than the archive hands out in one go is a request for
            // the maximum, not a mistake.
            Assert.Null(result.Error);
            Assert.Equal(AccountSyncArchiveService.MaxRunLimit, result.Data!.Runs.Count);
            Assert.NotNull(result.Data.NextBefore);

            // The one run that did not fit is exactly what the cursor picks up.
            var next = await BuildService(db).GetArchivePageAsync(
                accountId,
                userId,
                result.Data.NextBefore!.Value.ToString("O"),
                limit: null,
                TestContext.Current.CancellationToken);

            Assert.Null(next.Error);
            Assert.Single(next.Data!.Runs);
            Assert.Equal(FirstSync, next.Data.Runs[0].SyncedAt);
            Assert.Null(next.Data.NextBefore);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_returns_an_empty_page_for_an_account_that_has_never_synced()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-nosync@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            var result = await BuildService(db).GetArchivePageAsync(accountId, userId, before: null, limit: null, TestContext.Current.CancellationToken);

            // Empty, not an error: an account that has not synced yet is behaving perfectly normally.
            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data!.Runs);
            Assert.Null(result.Data.NextBefore);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_rejects_an_account_owned_by_another_user()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var ownerId = await SeedUserAsync(db, "archive-owner2@example.com");
            var otherId = await SeedUserAsync(db, "archive-intruder@example.com");
            var accountId = await SeedAccountAsync(db, ownerId);
            await ArchiveRunAsync(ownerId, accountId, FirstSync, transactionIds: ["abc123"]);

            var result = await BuildService(db).GetArchivePageAsync(accountId, otherId, before: null, limit: null, TestContext.Current.CancellationToken);

            // The archive is partitioned by user id, so a cross-user read would come back empty anyway —
            // but "empty" and "not yours" must not look the same to the caller.
            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal("ACCOUNT_NOT_FOUND", result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_rejects_an_unparseable_cursor()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-cursor@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            var result = await BuildService(db).GetArchivePageAsync(accountId, userId, "not-an-instant", limit: null, TestContext.Current.CancellationToken);

            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal(AccountSyncArchiveService.InvalidCursorCode, result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetArchivePageAsync_reports_an_unreachable_archive_instead_of_throwing()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "archive-outage@example.com");
            var accountId = await SeedAccountAsync(db, userId);

            using var unreachable = CreateClient(DeadServiceUrl);
            var service = new AccountSyncArchiveService(
                new AccountRepository(db),
                new SyncArchiveRepository(unreachable, new DynamoDbOptions { TableName = "unreachable", ServiceUrl = DeadServiceUrl })
                {
                    // Short bound so the test doesn't sit through the SDK's retry budget.
                    Timeout = TimeSpan.FromSeconds(2),
                },
                NullLogger<AccountSyncArchiveService>.Instance);

            var result = await service.GetArchivePageAsync(accountId, userId, before: null, limit: null, TestContext.Current.CancellationToken);

            // The read failed, and the caller is told so — unlike the sync's write path, there is no
            // useful answer to give without the archive.
            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal(AccountSyncArchiveService.ArchiveUnavailableCode, result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    /// <summary>
    /// Mirrors the client Program.cs registers — same credentials, same region, same SDK defaults.
    /// </summary>
    private static AmazonDynamoDBClient CreateClient(string serviceUrl)
    {
        return new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = "us-east-1",
            });
    }

    private AccountSyncArchiveService BuildService(DbSession db)
    {
        return new AccountSyncArchiveService(
            new AccountRepository(db),
            new SyncArchiveRepository(_dynamoDb, _options),
            NullLogger<AccountSyncArchiveService>.Instance);
    }

    /// <summary>
    /// Seeds one sync run through the real writer, so the keys under test are the ones a sync would
    /// actually have produced. Returns the run id it was written under.
    /// </summary>
    private async Task<Guid> ArchiveRunAsync(
        int userId,
        int accountId,
        DateTime syncedAt,
        IReadOnlyList<string>? transactionIds = null,
        IReadOnlyList<string>? removedIds = null,
        string? accountMetadataJson = null,
        IReadOnlyList<string>? errorsJson = null,
        IReadOnlyList<string>? skippedJson = null,
        string? rawJsonPadding = null)
    {
        var account = new Account
        {
            Id = accountId,
            UserId = userId,
            Name = "Checking",
            SourceType = SyncConstants.SourceTypeSimpleFin,
        };

        var upserts = (transactionIds ?? Array.Empty<string>())
            .Select(id => new SyncedTransaction(id, TransactionDate, "Coffee", "Beans", "", -4.25m, false, RawJsonFor(id, rawJsonPadding)))
            .ToList();

        var syncRunId = Guid.NewGuid();

        await new SyncArchiveRepository(_dynamoDb, _options).ArchiveAsync(
            account,
            new ProviderSyncResult(
                upserts,
                removedIds ?? Array.Empty<string>(),
                UpdatedConnectionDetailsJson: null,
                AccountMetadataJson: accountMetadataJson,
                ErrorsJson: errorsJson,
                SkippedTransactionsJson: skippedJson),
            syncRunId,
            syncedAt,
            TestContext.Current.CancellationToken);

        return syncRunId;
    }

    /// <summary>
    /// A stand-in provider payload. <paramref name="padding"/> inflates it so a run can be pushed past
    /// DynamoDB's 1MB response limit without needing a thousand transactions to do it.
    /// </summary>
    private static string RawJsonFor(string providerTransactionId, string? padding = null)
    {
        return padding is null
            ? $$"""{"id":"{{providerTransactionId}}","pending":false}"""
            : $$"""{"id":"{{providerTransactionId}}","pending":false,"padding":"{{padding}}"}""";
    }

    private static async Task<int> SeedUserAsync(DbSession db, string email)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);
    }

    private static async Task<int> SeedAccountAsync(DbSession db, int userId)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, 'Checking', 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId },
            db.CurrentTransaction);
    }
}
