using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using Dapper;

namespace Dollars2.Api.Data;

/// <summary>
/// Teaches Dapper to read a SQL <c>date</c> column into a <see cref="DateOnly"/>.
///
/// Writing already works natively — Microsoft.Data.SqlClient binds a DateOnly parameter as
/// <c>SqlDbType.Date</c> — but there is no conversion on the way back: the reader surfaces a
/// <c>date</c> column as a <see cref="DateTime"/> with a zero time component, and Dapper's default
/// materializer throws on the cast to DateOnly.
///
/// This is what lets calendar dates (<c>Transactions.Date</c>) be typed DateOnly end to end, which in
/// turn is what makes the global UTC <see cref="Json.UtcDateTimeConverter"/> safe: after this, every
/// remaining DateTime in the API is an instant.
/// </summary>
public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value)
    {
        return value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException(
                $"Cannot convert {value?.GetType().Name ?? "null"} to DateOnly."),
        };
    }

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }
}

/// <summary>
/// Registers the project's Dapper type handlers. A module initializer rather than a call from
/// Program.cs, because the repositories are equally reachable from tests that never build a host —
/// a repository is simply unusable without these registered, so it should not be possible to forget.
/// </summary>
internal static class DapperTypeHandlers
{
    [ModuleInitializer]
    internal static void Register()
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }
}
