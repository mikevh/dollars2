using Dapper;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Reproduces and guards against issue #76: sqlcmd defaults QUOTED_IDENTIFIER to OFF, which broke
/// 007_create_transactions.sql's filtered index (CREATE UNIQUE INDEX ... WHERE ... requires it ON).
/// The shared <see cref="MsSqlContainerFixture"/> can't exercise this — Microsoft.Data.SqlClient
/// opens sessions with QUOTED_IDENTIFIER ON by default, masking the bug — so this spins up its own
/// throwaway database and applies migrations over a session with QUOTED_IDENTIFIER explicitly OFF,
/// mirroring sqlcmd.
/// </summary>
public sealed class QuotedIdentifierOffMigrationFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "Dollars2TestQuotedIdentifierOff";

    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    private string _connectionString = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = await MsSqlContainerFixture.ProvisionDatabaseAsync(_container, TestDatabaseName);
    }

    public string ConnectionString => _connectionString;

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class QuotedIdentifierOffCollection : ICollectionFixture<QuotedIdentifierOffMigrationFixture>
{
    public const string Name = "MSSQL integration (QUOTED_IDENTIFIER OFF)";
}

[Collection(QuotedIdentifierOffCollection.Name)]
public sealed class QuotedIdentifierOffMigrationTests
{
    private readonly QuotedIdentifierOffMigrationFixture _fixture;

    public QuotedIdentifierOffMigrationTests(QuotedIdentifierOffMigrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_apply_cleanly_with_quoted_identifier_off()
    {
        await MigrationRunner.ApplyAsync(_fixture.ConnectionString, quotedIdentifierOff: true);

        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var recordedNames = (await connection.QueryAsync<string>(
            "SELECT ScriptName FROM Migrations")).ToHashSet(StringComparer.Ordinal);
        foreach (var name in MigrationRunner.ScriptNames())
        {
            Assert.True(recordedNames.Contains(name), $"Migrations is missing a row for '{name}'.");
        }

        var indexExists = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_Transactions_Provider' " +
            "AND object_id = OBJECT_ID('Transactions')");
        Assert.Equal(1, indexExists);
    }
}
