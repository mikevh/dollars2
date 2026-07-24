using System.Globalization;
using System.Text.Json;
using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Json;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;
using Dollars2.Api.Services;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Issue #68, end to end over a real MSSQL container: the step that actually broke was the round trip
/// through a DATETIME2 column, where Dapper drops <see cref="DateTimeKind"/> and the serializer then
/// omitted the UTC marker. Unit tests that construct a <c>DateTimeKind.Utc</c> value by hand skip
/// exactly that step, so this exercises the real path — insert, read via the repository, project into
/// the response DTO, serialize with the API's JSON options.
/// Each test runs inside a transaction that is rolled back, so nothing persists.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SyncTimeSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = Dollars2JsonOptions.CreateWebOptions();

    private readonly MsSqlContainerFixture _fixture;

    public SyncTimeSerializationTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Last_synced_at_serializes_with_a_utc_marker_and_the_stored_instant()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "sync-serialization@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");
            var syncedAt = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);
            await SeedSyncLogAsync(db, accountId, syncedAt);

            var lastSyncedAt = await SerializeLastSyncedAtAsync(db, userId);

            Assert.EndsWith("Z", lastSyncedAt);
            Assert.Equal(
                syncedAt,
                DateTime.Parse(lastSyncedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task A_sync_recorded_now_serializes_as_the_current_instant_not_a_local_shifted_one()
    {
        // The reported symptom: a sync hours old rendered as "just now" because the unmarked string was
        // read as local time and landed in the future. Asserting the round trip against DateTime.UtcNow
        // catches any offset the storage/serialization path reintroduces, in any server time zone.
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var userId = await SeedUserAsync(db, "sync-now@example.com");
            var accountId = await SeedAccountAsync(db, userId, "Checking");

            var before = DateTime.UtcNow;
            await new SyncLogRepository(db).CreateAsync(accountId, "Success", 3, null);
            var after = DateTime.UtcNow;

            var lastSyncedAt = await SerializeLastSyncedAtAsync(db, userId);
            var instant = DateTime.Parse(lastSyncedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            Assert.EndsWith("Z", lastSyncedAt);
            Assert.InRange(instant, before.AddSeconds(-1), after.AddSeconds(1));
        }
        finally
        {
            db.Rollback();
        }
    }

    /// <summary>
    /// Runs the production read path — repositories, <see cref="AccountService"/>, the API's JSON
    /// options — and returns the raw <c>lastSyncedAt</c> string the browser would receive.
    /// </summary>
    private static async Task<string> SerializeLastSyncedAtAsync(DbSession db, int userId)
    {
        var service = new AccountService(
            new AccountRepository(db),
            new SyncLogRepository(db),
            new AccountBalanceRepository(db),
            Array.Empty<IBankSyncProvider>());

        var groups = await service.GetAccountGroupsAsync(userId);

        var json = JsonSerializer.Serialize(groups, JsonOptions);
        using var document = JsonDocument.Parse(json);
        return document.RootElement[0]
            .GetProperty("accounts")[0]
            .GetProperty("lastSyncedAt")
            .GetString()!;
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

    private static async Task<int> SeedAccountAsync(DbSession db, int userId, string name)
    {
        return await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, @name, 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId, name },
            db.CurrentTransaction);
    }

    private static async Task SeedSyncLogAsync(DbSession db, int accountId, DateTime syncedAt)
    {
        await db.Connection.ExecuteAsync(
            @"INSERT INTO SyncLog (AccountId, SyncedAt, Status, TransactionCount, ErrorMessage)
              VALUES (@accountId, @syncedAt, 'Success', 3, NULL)",
            new { accountId, syncedAt },
            db.CurrentTransaction);
    }
}
