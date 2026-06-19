using System.Data;

namespace Dollars2.Api.Data;

public class DbSession : IDisposable
{
    public IDbConnection Connection { get; }
    public IDbTransaction? CurrentTransaction { get; private set; }

    public DbSession(IDbConnection connection)
    {
        Connection = connection;
    }

    public IDbTransaction BeginTransaction()
    {
        if (Connection.State != ConnectionState.Open)
        {
            Connection.Open();
        }
        CurrentTransaction = Connection.BeginTransaction();
        return CurrentTransaction;
    }

    public void Commit()
    {
        CurrentTransaction?.Commit();
        CurrentTransaction?.Dispose();
        CurrentTransaction = null;
    }

    public void Rollback()
    {
        CurrentTransaction?.Rollback();
        CurrentTransaction?.Dispose();
        CurrentTransaction = null;
    }

    public void Dispose()
    {
        CurrentTransaction?.Dispose();
        Connection.Dispose();
    }
}
