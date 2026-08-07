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
/// Exercises <see cref="TransactionRawHistoryService"/> across both of its stores at once — real MSSQL
/// resolving and authorizing the transaction, real <c>amazon/dynamodb-local</c> holding its archived
/// sightings. Testing them separately would not catch the thing most likely to break: the DynamoDB key
/// the reader builds from a SQL row has to match the one the sync writer built from a provider payload.
/// Requires a running Docker daemon on the dev machine.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransactionRawHistoryTests : IAsyncLifetime
{
    private const ushort DynamoDbPort = 8000;

    /// <summary>
    /// Port 1 is privileged and nothing is listening, so the connection is refused outright — the
    /// "dynamodb container isn't running" case.
    /// </summary>
    private const string DeadServiceUrl = "http://localhost:1";

    private const string TableName = "raw-history";

    private static readonly DateOnly TransactionDate = new(2026, 8, 1);

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

    public TransactionRawHistoryTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var serviceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(DynamoDbPort)}";
        _dynamoDb = CreateClient(serviceUrl);
        _options = new DynamoDbOptions { TableName = TableName, ServiceUrl = serviceUrl };

        // Provisioned through the initializer so these tests read against the real schema.
        var initializer = new SyncArchiveTableInitializer(_dynamoDb, _options, NullLogger<SyncArchiveTableInitializer>.Instance);
        await initializer.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _dynamoDb?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task GetRawHistoryAsync_returns_every_archived_sighting_newest_first()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "rawhistory-owner@example.com");
            var accountId = await SeedAccountAsync(db, userId);
            var transactionId = await SeedTransactionAsync(db, userId, accountId, providerTransactionId: "abc123");

            var instants = new[]
            {
                new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 2, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 3, 6, 0, 0, DateTimeKind.Utc),
            };
            foreach (var (instant, index) in instants.Select((i, n) => (i, n)))
            {
                await ArchiveAsync(userId, accountId, "abc123", pending: index < 2, instant);
            }

            // A second transaction on the same account, so the query has something to exclude.
            await ArchiveAsync(userId, accountId, "def456", pending: false, instants[0]);

            var result = await BuildService(db).GetRawHistoryAsync(transactionId, userId, TestContext.Current.CancellationToken);

            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Equal(3, result.Data!.Count);
            Assert.Equal(instants.Reverse().ToArray(), result.Data.Select(e => e.SyncedAt).ToArray());
            Assert.All(result.Data, e => Assert.Equal("SimpleFIN", e.SourceType));
            Assert.All(result.Data, e => Assert.NotEqual("", e.SyncRunId));

            // Newest first, and the payload comes back byte-for-byte as the provider sent it.
            Assert.Equal(RawJsonFor("abc123", pending: false), result.Data[0].RawJson);
            Assert.Equal(RawJsonFor("abc123", pending: true), result.Data[1].RawJson);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetRawHistoryAsync_returns_an_empty_list_for_a_manual_transaction()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "rawhistory-manual@example.com");
            var transactionId = await SeedTransactionAsync(db, userId, accountId: null, providerTransactionId: null);

            var result = await BuildService(db).GetRawHistoryAsync(transactionId, userId, TestContext.Current.CancellationToken);

            // Empty, not an error: a hand-entered transaction having no provider payload is normal, and
            // the dialog tab should say so rather than raise a failure toast.
            Assert.Null(result.Error);
            Assert.NotNull(result.Data);
            Assert.Empty(result.Data!);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetRawHistoryAsync_rejects_a_transaction_owned_by_another_user()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var ownerId = await SeedUserAsync(db, "rawhistory-owner2@example.com");
            var otherId = await SeedUserAsync(db, "rawhistory-intruder@example.com");
            var accountId = await SeedAccountAsync(db, ownerId);
            var transactionId = await SeedTransactionAsync(db, ownerId, accountId, providerTransactionId: "abc123");
            await ArchiveAsync(ownerId, accountId, "abc123", pending: false, new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc));

            var result = await BuildService(db).GetRawHistoryAsync(transactionId, otherId, TestContext.Current.CancellationToken);

            // The archive is keyed by user id, so a cross-user read would come back empty anyway — but
            // "empty" and "not yours" must not look the same to the caller.
            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal("TRANSACTION_NOT_FOUND", result.Error!.Code);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task GetRawHistoryAsync_reports_an_unreachable_archive_instead_of_throwing()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "rawhistory-outage@example.com");
            var accountId = await SeedAccountAsync(db, userId);
            var transactionId = await SeedTransactionAsync(db, userId, accountId, providerTransactionId: "abc123");

            using var unreachable = CreateClient(DeadServiceUrl);
            var service = new TransactionRawHistoryService(
                new TransactionRepository(db),
                new SyncArchiveRepository(unreachable, new DynamoDbOptions { TableName = "unreachable", ServiceUrl = DeadServiceUrl })
                {
                    // Short bound so the test doesn't sit through the SDK's retry budget.
                    Timeout = TimeSpan.FromSeconds(2),
                },
                NullLogger<TransactionRawHistoryService>.Instance);

            var result = await service.GetRawHistoryAsync(transactionId, userId, TestContext.Current.CancellationToken);

            // The read failed, and the caller is told so — unlike the sync's write path, there is no
            // useful answer to give without the archive.
            Assert.Null(result.Data);
            Assert.NotNull(result.Error);
            Assert.Equal(TransactionRawHistoryService.ArchiveUnavailableCode, result.Error!.Code);
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

    private TransactionRawHistoryService BuildService(DbSession db)
    {
        return new TransactionRawHistoryService(
            new TransactionRepository(db),
            new SyncArchiveRepository(_dynamoDb, _options),
            NullLogger<TransactionRawHistoryService>.Instance);
    }

    /// <summary>
    /// Seeds the archive through the real writer rather than by hand, so the keys under test are the
    /// ones a sync would actually have produced.
    /// </summary>
    private async Task ArchiveAsync(int userId, int accountId, string providerTransactionId, bool pending, DateTime syncedAt)
    {
        var account = new Account
        {
            Id = accountId,
            UserId = userId,
            Name = "Checking",
            SourceType = SyncConstants.SourceTypeSimpleFin,
        };

        var synced = new SyncedTransaction(
            providerTransactionId,
            TransactionDate,
            "Coffee",
            "Beans",
            "",
            -4.25m,
            pending,
            RawJsonFor(providerTransactionId, pending));

        await new SyncArchiveRepository(_dynamoDb, _options).ArchiveAsync(
            account,
            new ProviderSyncResult([synced], [], null),
            Guid.NewGuid(),
            syncedAt,
            TestContext.Current.CancellationToken);
    }

    private static string RawJsonFor(string providerTransactionId, bool pending)
    {
        return $$"""{"id":"{{providerTransactionId}}","pending":{{(pending ? "true" : "false")}}}""";
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

    private static async Task<int> SeedTransactionAsync(DbSession db, int userId, int? accountId, string? providerTransactionId)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Transactions (UserId, AccountId, ProviderTransactionId, Date, Description, Payee, Memo, Amount, IsPending, IsManual, CreatedAt, UpdatedAt)
              VALUES (@userId, @accountId, @providerTransactionId, @date, 'Coffee', 'Beans', '', -4.25, 0, @isManual, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new
            {
                userId,
                accountId,
                providerTransactionId,
                date = TransactionDate.ToString("yyyy-MM-dd"),
                isManual = accountId is null,
            },
            db.CurrentTransaction);
    }
}
