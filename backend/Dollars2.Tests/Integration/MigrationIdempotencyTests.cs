using Dapper;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Proves the migrations are idempotent (issue #65): every script guards on its own
/// <c>Migrations</c> row, so applying the whole set a second time against an already-migrated
/// database runs clean, records exactly one row per script (with a row for every file 000–016),
/// and leaves the schema intact. The shared fixture has already applied the migrations once, so
/// re-applying here is the second pass.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationIdempotencyTests
{
    private readonly MsSqlContainerFixture _fixture;

    /// <summary>Every migration file's ScriptName, i.e. its basename without the .sql extension.</summary>
    private static readonly string[] ExpectedScriptNames =
    {
        "000_create_migrations_table",
        "001_create_users",
        "002_create_refresh_tokens",
        "003_create_budgets",
        "004_create_budget_groups",
        "005_create_line_items",
        "006_create_accounts",
        "007_create_transactions",
        "008_create_transaction_assignments",
        "009_add_unique_transaction_assignment",
        "010_allow_split_assignments",
        "011_add_previous_line_item_id",
        "012_add_refresh_token_index",
        "013_create_sync_log",
        "014_add_transaction_payee_memo",
        "015_create_account_balances",
        "016_add_account_include_in_budget",
    };

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

        // Exactly one Migrations row per script, and a row for every file 000–016.
        var rows = (await db.Connection.QueryAsync<MigrationCount>(
            "SELECT ScriptName, COUNT(*) AS Count FROM Migrations GROUP BY ScriptName"))
            .ToDictionary(r => r.ScriptName, r => r.Count);

        foreach (var name in ExpectedScriptNames)
        {
            Assert.True(rows.ContainsKey(name), $"Migrations is missing a row for '{name}'.");
            Assert.Equal(1, rows[name]);
        }

        Assert.Equal(ExpectedScriptNames.Length, rows.Count);

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
    }

    private sealed record MigrationCount(string ScriptName, int Count);
}
