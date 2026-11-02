using System.Data;
using Dapper;

namespace MyGarage.Database.Common;

internal static class DapperSetup
{
    static DapperSetup()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    // Touching this method runs the static constructor once.
    public static void Ensure()
    {
    }

    // Npgsql returns timestamptz as a UTC DateTime through the untyped reader Dapper uses.
    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime dateTime => new DateTimeOffset(
                dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime()),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateTimeOffset."),
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
            parameter.Value = value.ToUniversalTime();
    }

    // PostgreSQL date columns come back as DateTime through the untyped reader.
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly."),
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value;
        }
    }
}
