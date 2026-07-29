using Dapper;
using Dollars2.Api.Data;
using Dollars2.Api.Models;
using Dollars2.Api.Providers;
using Dollars2.Api.Repositories;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves against real MSSQL that provider text longer than the nvarchar(500) columns is stored
/// truncated instead of raising SqlException 8152 (issue #93). Before the fix that exception was caught
/// per account by BankSyncService.RecordFailureAsync, so one overlong bank memo failed the whole
/// account's sync and kept failing it on every subsequent run. The tests write through the same
/// expressions BankSyncService uses — the fields of a mapped <see cref="SyncedTransaction"/> — on both
/// the create- and update-from-sync paths. Each test runs inside a transaction that is rolled back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SyncTextTruncationTests
{
    private static readonly DateOnly Date = new(2026, 7, 15);

    private readonly MsSqlContainerFixture _fixture;

    public SyncTextTruncationTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Over_length_provider_text_persists_truncated_on_create()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var (userId, accountId) = await SeedAsync(db, "sync-truncate-create@example.com");
            var repository = new TransactionRepository(db);
            var t = SyncedWithTextOfLength(600, "t1");

            var id = await repository.CreateFromSyncAsync(
                userId, accountId, t.ProviderTransactionId,
                t.Date, t.Description, t.Payee, t.Memo, t.Amount, t.IsPending);

            var stored = await repository.GetByIdAsync(id);
            Assert.Equal(TransactionText.MaxLength, stored!.Description.Length);
            Assert.Equal(TransactionText.MaxLength, stored.Payee.Length);
            Assert.Equal(TransactionText.MaxLength, stored.Memo.Length);
        }
        finally
        {
            db.Rollback();
        }
    }

    [Fact]
    public async Task Over_length_provider_text_persists_truncated_on_update()
    {
        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var (userId, accountId) = await SeedAsync(db, "sync-truncate-update@example.com");
            var repository = new TransactionRepository(db);
            var created = SyncedWithTextOfLength(10, "t1");
            var id = await repository.CreateFromSyncAsync(
                userId, accountId, created.ProviderTransactionId,
                created.Date, created.Description, created.Payee, created.Memo, created.Amount, created.IsPending);

            // The same transaction comes back from the bank with a memo that has grown past the column.
            var updated = SyncedWithTextOfLength(600, "t1");
            await repository.UpdateFromSyncAsync(
                id, updated.Date, updated.Description, updated.Payee, updated.Memo, updated.Amount, updated.IsPending);

            var stored = await repository.GetByIdAsync(id);
            Assert.Equal(TransactionText.MaxLength, stored!.Description.Length);
            Assert.Equal(TransactionText.MaxLength, stored.Payee.Length);
            Assert.Equal(TransactionText.MaxLength, stored.Memo.Length);
        }
        finally
        {
            db.Rollback();
        }
    }

    private static SyncedTransaction SyncedWithTextOfLength(int length, string providerTransactionId)
    {
        var text = new string('x', length);
        return new SyncedTransaction(providerTransactionId, Date, text, text, text, -12.50m, IsPending: false);
    }

    private static async Task<(int UserId, int AccountId)> SeedAsync(DbSession db, string email)
    {
        var userId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Users (Email, CreatedAt, UpdatedAt)
              VALUES (@email, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { email },
            db.CurrentTransaction);

        var accountId = await db.Connection.QuerySingleAsync<int>(
            @"INSERT INTO Accounts (UserId, Name, SourceType, ConnectionDetailsJson, CreatedAt, UpdatedAt)
              VALUES (@userId, 'Checking', 'SimpleFIN', NULL, SYSUTCDATETIME(), SYSUTCDATETIME());
              SELECT CAST(SCOPE_IDENTITY() AS INT)",
            new { userId },
            db.CurrentTransaction);

        return (userId, accountId);
    }
}
