using Microsoft.Data.SqlClient;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Applies the API's numbered raw-SQL migrations, in order, against a target database.
/// The migration files are copied next to the test assembly at build time (see the csproj).
/// Each file is self-guarded (IF NOT EXISTS ... Migrations) and contains no GO batches,
/// so each is executed as a single batch.
/// </summary>
public static class MigrationRunner
{
    /// <summary>
    /// Every migration script copied next to the test assembly, in the order they are applied.
    /// The single source of truth for "which migrations exist" — tests derive their expectations
    /// from this rather than restating the list, so adding a migration needs no test edit.
    /// </summary>
    public static IReadOnlyList<string> ScriptPaths()
    {
        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
        if (!Directory.Exists(migrationsDir))
        {
            throw new DirectoryNotFoundException(
                $"Migrations directory not found at '{migrationsDir}'. " +
                "Ensure the migration .sql files are linked as copy-to-output content in the test project.");
        }

        var scripts = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        if (scripts.Length == 0)
        {
            throw new InvalidOperationException($"No migration scripts found in '{migrationsDir}'.");
        }

        return scripts;
    }

    /// <summary>Each migration's ScriptName, i.e. its basename without the .sql extension.</summary>
    public static IReadOnlyList<string> ScriptNames()
    {
        return ScriptPaths().Select(Path.GetFileNameWithoutExtension).ToArray()!;
    }

    /// <param name="connectionString">Target database connection string.</param>
    /// <param name="quotedIdentifierOff">
    /// When true, issues <c>SET QUOTED_IDENTIFIER OFF</c> on the session before applying any
    /// script, mirroring sqlcmd's default (ADO.NET's <see cref="SqlConnection"/> otherwise opens
    /// sessions with it ON). Used to reproduce and guard against issue #76.
    /// </param>
    public static async Task ApplyAsync(string connectionString, bool quotedIdentifierOff = false)
    {
        GuardAgainstNonLocalTarget(connectionString);

        var scripts = ScriptPaths();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        if (quotedIdentifierOff)
        {
            await using var setCommand = connection.CreateCommand();
            setCommand.CommandText = "SET QUOTED_IDENTIFIER OFF;";
            await setCommand.ExecuteNonQueryAsync();
        }

        foreach (var scriptPath in scripts)
        {
            var sql = await File.ReadAllTextAsync(scriptPath);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Refuses to run against anything that does not look like a throwaway local/container
    /// database, so an accidental real connection string can never be migrated by the tests.
    /// </summary>
    private static void GuardAgainstNonLocalTarget(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var host = builder.DataSource ?? "";

        // Strip protocol prefix ("tcp:") and any instance/port suffix to get the bare host.
        var afterProtocol = host.Contains(':') ? host[(host.IndexOf(':') + 1)..] : host;
        var bareHost = afterProtocol.Split(',', '\\')[0].Trim();

        var allowed = bareHost is "localhost" or "127.0.0.1" or "::1" or "";
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Refusing to run migrations against non-local host '{bareHost}'. " +
                "Integration tests may only target a throwaway local/container database.");
        }
    }
}
