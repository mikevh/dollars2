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
    public static async Task ApplyAsync(string connectionString)
    {
        GuardAgainstNonLocalTarget(connectionString);

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

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

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
