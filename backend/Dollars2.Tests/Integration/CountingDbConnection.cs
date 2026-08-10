using System.Data;

namespace Dollars2.Tests.Integration;

/// <summary>
/// Wraps a real <see cref="IDbConnection"/> and counts <see cref="CreateCommand"/> calls — Dapper
/// creates exactly one command per query/execute, so this is a direct proxy for SQL round trips.
/// Used to assert N+1 fixes stay flat as row counts grow (issue #15).
/// </summary>
public sealed class CountingDbConnection : IDbConnection
{
    private readonly IDbConnection _inner;

    public CountingDbConnection(IDbConnection inner)
    {
        _inner = inner;
    }

    public int CommandCount { get; private set; }

    public string? ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value!;
    }

    public int ConnectionTimeout => _inner.ConnectionTimeout;
    public string Database => _inner.Database;
    public ConnectionState State => _inner.State;

    public IDbTransaction BeginTransaction() => _inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => _inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public void Close() => _inner.Close();

    public IDbCommand CreateCommand()
    {
        CommandCount++;
        return _inner.CreateCommand();
    }

    public void Open() => _inner.Open();
    public void Dispose() => _inner.Dispose();
}
