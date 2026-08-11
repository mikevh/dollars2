using Dollars2.Api.Data;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Spins up a throwaway MSSQL container once per test run, creates a dedicated database,
/// and applies the API's real migrations against it. Torn down after the run.
/// Requires a running Docker daemon on the dev machine.
///
/// Tests get a <see cref="DbSession"/> over a fresh connection to the migrated database and
/// are expected to wrap their work in a transaction and roll it back, so nothing persists
/// between tests within the run.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "Dollars2Test";

    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    private string _connectionString = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = await ProvisionDatabaseAsync(_container, TestDatabaseName);
        await MigrationRunner.ApplyAsync(_connectionString);
    }

    /// <summary>Connection string to the migrated throwaway test database.</summary>
    public string ConnectionString => _connectionString;

    /// <summary>Opens a fresh <see cref="DbSession"/> against the migrated test database.</summary>
    public DbSession CreateSession()
    {
        return new DbSession(new SqlConnection(_connectionString));
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates (if missing) a dedicated database on an already-started container and returns a
    /// connection string targeting it. Shared by fixtures that need their own throwaway database
    /// without duplicating the master-connection/CREATE DATABASE dance.
    /// </summary>
    internal static async Task<string> ProvisionDatabaseAsync(MsSqlContainer container, string databaseName)
    {
        var masterConnectionString = container.GetConnectionString();
        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"IF DB_ID('{databaseName}') IS NULL CREATE DATABASE [{databaseName}];";
            await create.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MSSQL integration";
}
