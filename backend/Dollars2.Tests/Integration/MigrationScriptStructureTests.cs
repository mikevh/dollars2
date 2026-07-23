using System.Text.RegularExpressions;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Guards the migration convention (issue #65): every <c>Migrations/*.sql</c> script must record
/// itself in the <c>Migrations</c> table using its own basename, so a new migration can't be added
/// without self-recording (which would break the re-runnable manual runner). Reads the migration
/// files copied next to the test assembly; needs no database.
/// </summary>
public sealed class MigrationScriptStructureTests
{
    public static IEnumerable<object[]> MigrationScripts()
    {
        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found at '{migrationsDir}'.");

        foreach (var path in Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(MigrationScripts))]
    public void Every_script_records_its_own_basename_in_Migrations(string scriptPath)
    {
        var scriptName = Path.GetFileNameWithoutExtension(scriptPath);
        var sql = File.ReadAllText(scriptPath);

        // e.g. INSERT INTO Migrations (ScriptName) VALUES ('016_add_account_include_in_budget')
        var pattern = new Regex(
            @"INSERT\s+INTO\s+Migrations\s*\(\s*ScriptName\s*\)\s*VALUES\s*\(\s*'" +
            Regex.Escape(scriptName) + @"'\s*\)",
            RegexOptions.IgnoreCase);

        Assert.True(
            pattern.IsMatch(sql),
            $"Migration '{scriptName}.sql' must contain " +
            $"INSERT INTO Migrations (ScriptName) VALUES ('{scriptName}').");
    }
}
