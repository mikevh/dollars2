using Dapper;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Guards the constraint-naming convention (issue #87): a fully-migrated database must contain no
/// system-named constraints, so every constraint can be dropped or altered by name from a future
/// migration instead of being hunted down through the catalog views with dynamic SQL.
///
/// Any new migration that uses the inline <c>PRIMARY KEY</c> / <c>REFERENCES</c> / <c>UNIQUE</c> /
/// <c>DEFAULT</c> column shorthand silently gets a per-database generated name and fails this test.
/// The catalog views are the authority here, so the check is indifferent to SQL formatting.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConstraintNamingTests
{
    private const string SweepScriptName = "017_name_existing_constraints";

    private const string SystemNamedConstraintsSql = """
        SELECT name FROM sys.default_constraints WHERE is_system_named = 1
        UNION ALL SELECT name FROM sys.key_constraints   WHERE is_system_named = 1
        UNION ALL SELECT name FROM sys.foreign_keys      WHERE is_system_named = 1
        UNION ALL SELECT name FROM sys.check_constraints WHERE is_system_named = 1
        """;

    private const string AllConstraintNamesSql = """
        SELECT name FROM sys.default_constraints
        UNION ALL SELECT name FROM sys.key_constraints
        UNION ALL SELECT name FROM sys.foreign_keys
        UNION ALL SELECT name FROM sys.check_constraints
        """;

    /// <summary>One constraint of each kind the sweep renames, by its post-sweep name.</summary>
    private static readonly string[] ExpectedConstraintNames =
    {
        "DF_BudgetGroups_IsIncome",
        "PK_LineItems",
        "FK_TransactionAssignments_Transactions",
        "UQ_Migrations_ScriptName",
    };

    private readonly MsSqlContainerFixture _fixture;

    public ConstraintNamingTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrated_database_has_no_system_named_constraints()
    {
        using var db = _fixture.CreateSession();

        var systemNamed = (await db.Connection.QueryAsync<string>(SystemNamedConstraintsSql)).ToArray();

        Assert.True(
            systemNamed.Length == 0,
            "Every constraint must be named explicitly (see the naming convention in CLAUDE.md). " +
            "These were created with the inline shorthand and got a generated, per-database name: " +
            string.Join(", ", systemNamed.Order()));

        // The convention is only useful if the derived names are the ones a migration would write.
        var allNames = (await db.Connection.QueryAsync<string>(AllConstraintNamesSql))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ExpectedConstraintNames)
        {
            Assert.Contains(name, allNames);
        }
    }

    /// <summary>
    /// The sweep's <c>is_system_named = 1</c> filter, not just its <c>Migrations</c> guard, is what
    /// makes it re-runnable. Deleting the guard row forces the body to actually execute against an
    /// already-swept database; it must find nothing to rename and leave every name untouched.
    /// The whole thing runs inside a rolled-back transaction, so the fixture is unchanged.
    /// </summary>
    [Fact]
    public async Task Re_running_the_naming_sweep_is_a_clean_no_op()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Migrations", $"{SweepScriptName}.sql");
        Assert.True(File.Exists(scriptPath), $"Sweep migration not found at '{scriptPath}'.");
        var sweepSql = await File.ReadAllTextAsync(scriptPath, TestContext.Current.CancellationToken);

        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            var before = (await db.Connection.QueryAsync<string>(
                AllConstraintNamesSql, transaction: db.CurrentTransaction)).Order().ToArray();

            await db.Connection.ExecuteAsync(
                "DELETE FROM Migrations WHERE ScriptName = @ScriptName",
                new { ScriptName = SweepScriptName },
                db.CurrentTransaction);

            await db.Connection.ExecuteAsync(sweepSql, transaction: db.CurrentTransaction);

            var after = (await db.Connection.QueryAsync<string>(
                AllConstraintNamesSql, transaction: db.CurrentTransaction)).Order().ToArray();

            Assert.Equal(before, after);

            var systemNamed = (await db.Connection.QueryAsync<string>(
                SystemNamedConstraintsSql, transaction: db.CurrentTransaction)).ToArray();
            Assert.Empty(systemNamed);
        }
        finally
        {
            db.Rollback();
        }
    }

    /// <summary>
    /// Covers the sweep's multi-column <c>UQ_</c> branch, which the live schema does not exercise —
    /// the only unique constraint it renames is single-column. A throwaway table with an inline
    /// two-column <c>UNIQUE</c> is created inside a rolled-back transaction, so the fixture is
    /// unchanged. The columns are declared out of ordinal order to prove the name follows
    /// <c>key_ordinal</c> rather than <c>column_id</c>.
    /// </summary>
    [Fact]
    public async Task Sweep_names_a_multi_column_unique_constraint_in_key_order()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Migrations", $"{SweepScriptName}.sql");
        var sweepSql = await File.ReadAllTextAsync(scriptPath, TestContext.Current.CancellationToken);

        using var db = _fixture.CreateSession();
        db.BeginTransaction();
        try
        {
            await db.Connection.ExecuteAsync(
                """
                CREATE TABLE ConstraintNamingProbe (
                    Alpha INT NOT NULL,
                    Beta INT NOT NULL,
                    UNIQUE (Beta, Alpha)
                )
                """,
                transaction: db.CurrentTransaction);

            await db.Connection.ExecuteAsync(
                "DELETE FROM Migrations WHERE ScriptName = @ScriptName",
                new { ScriptName = SweepScriptName },
                db.CurrentTransaction);

            await db.Connection.ExecuteAsync(sweepSql, transaction: db.CurrentTransaction);

            var probeConstraints = (await db.Connection.QueryAsync<string>(
                "SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('ConstraintNamingProbe')",
                transaction: db.CurrentTransaction)).ToArray();

            Assert.Equal(new[] { "UQ_ConstraintNamingProbe_Beta_Alpha" }, probeConstraints);
        }
        finally
        {
            db.Rollback();
        }
    }
}
