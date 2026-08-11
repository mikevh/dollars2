using Dapper;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the migrations are idempotent (issue #65): every script guards on its own
/// <c>Migrations</c> row, so applying the whole set a second time against an already-migrated
/// database runs clean, records exactly one row per script (with a row for every file on disk),
/// and leaves the schema intact. The shared fixture has already applied the migrations once, so
/// re-applying here is the second pass.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationIdempotencyTests
{
    private readonly MsSqlContainerFixture _fixture;

    private static readonly string[] ExpectedTables =
    {
        "Migrations", "Users", "RefreshTokens", "Budgets", "BudgetGroups", "LineItems",
        "Accounts", "Transactions", "TransactionAssignments", "SyncLog", "AccountBalances",
    };

    public MigrationIdempotencyTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Applying_migrations_again_is_a_clean_no_op()
    {
        // Second pass over the already-migrated database — must not throw.
        await MigrationRunner.ApplyAsync(_fixture.ConnectionString);

        using var db = _fixture.CreateSession();

        // Exactly one Migrations row per script, and a row for every file on disk. The expected
        // set is read from the migration directory, so a new script is covered the moment it is
        // added — and a script that fails to self-record still fails here.
        var expectedScriptNames = MigrationRunner.ScriptNames();
        var rows = (await db.Connection.QueryAsync<MigrationCount>(
            "SELECT ScriptName, COUNT(*) AS Count FROM Migrations GROUP BY ScriptName"))
            .ToDictionary(r => r.ScriptName, r => r.Count);

        foreach (var name in expectedScriptNames)
        {
            Assert.True(rows.ContainsKey(name), $"Migrations is missing a row for '{name}'.");
            Assert.Equal(1, rows[name]);
        }

        // No stray rows for scripts that no longer exist.
        Assert.Equal(
            expectedScriptNames.Order(StringComparer.Ordinal),
            rows.Keys.Order(StringComparer.Ordinal));

        // Every schema object still exists after the repeat apply.
        var tables = (await db.Connection.QueryAsync<string>(
            "SELECT name FROM sys.tables")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var table in ExpectedTables)
        {
            Assert.Contains(table, tables);
        }

        // Spot-check ALTER-added columns from the normalized scripts survive.
        var accountColumns = (await db.Connection.QueryAsync<string>(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('Accounts')"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IncludeInBudget", accountColumns);

        var transactionColumns = (await db.Connection.QueryAsync<string>(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('Transactions')"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Payee", transactionColumns);
        Assert.Contains("Memo", transactionColumns);

        // Issue #86: LineItems.BudgetId survives the repeat apply, stays NOT NULL, and keeps its FK
        // to Budgets — the backfill migration only runs once, guarded by its own Migrations row.
        var lineItemColumns = (await db.Connection.QueryAsync<LineItemColumnInfo>(
            "SELECT name AS Name, is_nullable AS IsNullable FROM sys.columns WHERE object_id = OBJECT_ID('LineItems')"))
            .ToDictionary(c => c.Name, c => c.IsNullable, StringComparer.OrdinalIgnoreCase);
        Assert.True(lineItemColumns.ContainsKey("BudgetId"));
        Assert.False(lineItemColumns["BudgetId"]);

        var foreignKeys = (await db.Connection.QueryAsync<string>(
            "SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('LineItems')"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("FK_LineItems_Budgets", foreignKeys);

        // No pre-existing LineItems row was left with a NULL or mismatched BudgetId by the backfill.
        var badBackfillCount = await db.Connection.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM LineItems li
              INNER JOIN BudgetGroups bg ON bg.Id = li.GroupId
              WHERE li.BudgetId IS NULL OR li.BudgetId <> bg.BudgetId");
        Assert.Equal(0, badBackfillCount);
    }

    private sealed record MigrationCount(string ScriptName, int Count);

    private sealed record LineItemColumnInfo(string Name, bool IsNullable);
}
