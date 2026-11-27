using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 簡易 DSL の総当たりの 5 つの部分が普段の既定の種と網羅の単位で作る入力の値（行の値・リテラル・パラメータ・IN のリストの要素）の観測
/// </summary>
/// <remarks>
/// <para>
/// 単位は各テストのクラスが流す単位と同じ（<c>DslConditionFuzzTests</c>・<c>NamedQueryDslConditionFuzz{SqlServer,PostgreSql,MySql,Oracle}RuntimeTests</c>）。
/// 種は普段の既定の数：SQLite・MySQL の単体は名前の総当たりと共有の 2（<see cref="NameFuzz.DefaultSeedCount"/>）、Integration の 4 方言は 1
/// （<c>NamedQueryDslConditionFuzzSqlServerRuntimeTests.Seeds</c>・<c>DslConditionFuzzEfCore.Seeds</c>）。どれも種 1 から。単位を作る関数を
/// 同じ種で呼んで読むだけで、新しい乱数を引かない。
/// </para>
/// <para>
/// 列の相は列の宣言の型（<see cref="DslColumn.DataType"/>）から読み、整数の列の範囲は C# 側の種類で付け直す（MySQL の tinyint は sbyte）。
/// money・smallmoney の範囲と datetimeoffset の小数秒の桁は列の宣言（<see cref="DslColumn.Money"/>・<see cref="DslColumn.FractionalScale"/>）から付ける。
/// パラメータと IN のリストの相は、比べる列（原子の列）にする。
/// </para>
/// </remarks>
public static class DslFuzzValueObservations
{
    /// <summary>Integration の 4 方言の普段の既定の種の数</summary>
    public const int IntegrationRegularSeedCount = 1;

    /// <summary>部分の名前 → 観測</summary>
    public static IReadOnlyDictionary<
        string,
        Func<IReadOnlyList<ValueObservation>>
    > Parts { get; } =
        new Dictionary<string, Func<IReadOnlyList<ValueObservation>>>(StringComparer.Ordinal)
        {
            [ProtectionNames.EnginePart(EngineIds.Dsl, "SqliteMySql")] = () =>
                Observe(SqliteMySqlUnits()),
            [ProtectionNames.EnginePart(EngineIds.Dsl, "SqlServer")] = () =>
                Observe(SqlServerUnits()),
            [ProtectionNames.EnginePart(EngineIds.Dsl, "PostgreSql")] = () =>
                Observe(PostgreSqlUnits()),
            [ProtectionNames.EnginePart(EngineIds.Dsl, "MySql")] = () => Observe(MySqlUnits()),
            [ProtectionNames.EnginePart(EngineIds.Dsl, "Oracle")] = () => Observe(OracleUnits()),
        };

    /// <summary>SQLite・MySQL の単体の総当たり（<c>DslConditionFuzzTests</c>）の単位</summary>
    private static IEnumerable<DslFuzzUnit> SqliteMySqlUnits()
    {
        IEnumerable<DslFuzzUnit> coverage =
        [
            DslFuzzUnits.Coverage(DialectNames.Sqlite),
            DslFuzzUnits.PlainCoverage(DialectNames.Sqlite),
            DslFuzzUnits.Coverage(DialectNames.MySql),
            DslFuzzUnits.PlainCoverage(DialectNames.MySql),
            DslFuzzPoolEdges.Coverage(DialectNames.Sqlite),
            DslFuzzPoolEdges.Coverage(DialectNames.MySql),
            .. DslFuzzUnits.FamiliesCoverage(DialectNames.Sqlite),
            .. DslFuzzUnits.FamiliesCoverage(DialectNames.MySql),
            .. DslFuzzUnits.TimeSpanCoverage(DialectNames.Sqlite, ordered: false),
            .. DslFuzzUnits.TimeSpanCoverage(DialectNames.Sqlite),
            .. DslFuzzUnits.TimeSpanCoverage(DialectNames.MySql),
            .. DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.Sqlite, ordered: false),
            .. DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.Sqlite),
            .. DslFuzzUnits.FloatingCoverage(DialectNames.Sqlite),
            .. DslFuzzUnits.FloatingCoverage(DialectNames.MySql),
            .. DslFuzzUnits.SqliteMixedOffsetInMemoryCoverage(),
        ];

        return coverage
            .Concat(
                Seeds(NameFuzz.DefaultSeedCount)
                    .SelectMany(seed => DslFuzzUnits.Sqlite(seed).Concat(DslFuzzUnits.MySql(seed)))
            )
            .Concat(DslFuzzUnits.Collisions());
    }

    private static IEnumerable<DslFuzzUnit> SqlServerUnits() =>
        new[]
        {
            DslFuzzUnits.Coverage(DialectNames.SqlServer),
            DslFuzzUnits.PlainCoverage(DialectNames.SqlServer),
            DslFuzzUnits.SqlServerAnsiCoverage(),
            DslFuzzUnits.SqlServerMoneyCoverage(),
            DslFuzzUnits.SqlServerOutOfDayCoverage(),
            DslFuzzUnits.SqlServerLegacyDateTimeCoverage(),
            DslFuzzPoolEdges.Coverage(DialectNames.SqlServer),
        }
            .Concat(DslFuzzUnits.FamiliesCoverage(DialectNames.SqlServer))
            .Concat(DslFuzzUnits.TimeSpanCoverage(DialectNames.SqlServer))
            .Concat(DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.SqlServer))
            .Concat(DslFuzzUnits.FloatingCoverage(DialectNames.SqlServer))
            .Concat(Seeds(IntegrationRegularSeedCount).SelectMany(DslFuzzUnits.SqlServer));

    private static IEnumerable<DslFuzzUnit> PostgreSqlUnits() =>
        new[]
        {
            DslFuzzUnits.Coverage(DialectNames.PostgreSql),
            DslFuzzUnits.PlainCoverage(DialectNames.PostgreSql),
            DslFuzzPoolEdges.Coverage(DialectNames.PostgreSql),
        }
            .Concat(DslFuzzUnits.FamiliesCoverage(DialectNames.PostgreSql))
            .Concat(DslFuzzUnits.TimeSpanCoverage(DialectNames.PostgreSql))
            .Concat(DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.PostgreSql))
            .Concat(DslFuzzUnits.FloatingCoverage(DialectNames.PostgreSql))
            .Concat(Seeds(IntegrationRegularSeedCount).SelectMany(DslFuzzUnits.PostgreSql));

    private static IEnumerable<DslFuzzUnit> MySqlUnits() =>
        new[]
        {
            DslFuzzUnits.Coverage(DialectNames.MySql),
            DslFuzzUnits.PlainCoverage(DialectNames.MySql),
            DslFuzzPoolEdges.Coverage(DialectNames.MySql),
        }
            .Concat(DslFuzzUnits.FamiliesCoverage(DialectNames.MySql))
            .Concat(DslFuzzUnits.TimeSpanCoverage(DialectNames.MySql))
            .Concat(DslFuzzUnits.FloatingCoverage(DialectNames.MySql))
            .Concat(Seeds(IntegrationRegularSeedCount).SelectMany(DslFuzzUnits.MySql));

    private static IEnumerable<DslFuzzUnit> OracleUnits() =>
        new[]
        {
            DslFuzzUnits.Coverage(DialectNames.Oracle),
            DslFuzzUnits.PlainCoverage(DialectNames.Oracle),
            DslFuzzPoolEdges.Coverage(DialectNames.Oracle),
        }
            .Concat(DslFuzzUnits.FamiliesCoverage(DialectNames.Oracle))
            .Concat(DslFuzzUnits.DateTimeOffsetCoverage(DialectNames.Oracle))
            .Concat(DslFuzzUnits.FloatingCoverage(DialectNames.Oracle))
            .Concat(Seeds(IntegrationRegularSeedCount).SelectMany(DslFuzzUnits.Oracle));

    /// <summary>種 1 から <paramref name="count"/> 個</summary>
    private static IEnumerable<int> Seeds(int count) => Enumerable.Range(1, count);

    /// <summary>単位の行の値・リテラル・パラメータ・IN のリストを観測する</summary>
    private static IReadOnlyList<ValueObservation> Observe(IEnumerable<DslFuzzUnit> units)
    {
        var observations = new List<ValueObservation>();

        foreach (var unit in units)
        {
            var columns = unit.Columns.ToDictionary(
                column => column.Name,
                ColumnOf,
                StringComparer.Ordinal
            );

            for (var index = 0; index < unit.Rows.Count; index++)
            {
                foreach (var (name, value) in unit.Rows[index])
                {
                    observations.Add(
                        new ValueObservation(
                            value,
                            columns.GetValueOrDefault(name),
                            $"{unit.Name} の行 {index + 1} の {name}"
                        )
                    );
                }
            }

            foreach (var query in unit.Queries)
            {
                foreach (var atom in query.Atoms)
                {
                    var column = ColumnOf(atom.Column);
                    var where = $"{unit.Name} のクエリ {query.Name}";

                    switch (atom)
                    {
                        case DslCompare { Operand: DslLiteral literal }:
                            observations.Add(new ValueObservation(literal.Value, column, where));
                            break;

                        case DslLike { Operand: DslLiteral literal }:
                            observations.Add(new ValueObservation(literal.Value, column, where));
                            break;

                        case DslMatch { Operand: DslLiteral literal }:
                            observations.Add(new ValueObservation(literal.Value, column, where));
                            break;

                        default:
                            if (DslFuzz.ParameterOf(atom) is { } parameter)
                            {
                                observations.Add(
                                    new ValueObservation(
                                        parameter.IsList
                                            ? parameter.Values
                                            : parameter.Values.FirstOrDefault(),
                                        column,
                                        where
                                    )
                                );
                            }

                            break;
                    }
                }
            }
        }

        return observations;
    }

    /// <summary>総当たりの列の相</summary>
    private static ValueColumn ColumnOf(DslColumn column)
    {
        var facet = ValueColumn.FromSqlType(column.DataType);

        if (IntegerRange(column.Clr) is var (min, max))
        {
            facet = facet with { Min = min, Max = max, Scale = 0, Precision = null };
        }

        if (column.Money is { } money)
        {
            facet = facet with { Min = money.Min, Max = money.Max, Scale = DslMoneyType.Scale };
        }

        if (column.FractionalScale is { } scale)
        {
            facet = facet with { Scale = scale };
        }

        return facet;
    }

    private static (decimal Min, decimal Max)? IntegerRange(DslClr clr) =>
        clr switch
        {
            DslClr.SByte => (sbyte.MinValue, sbyte.MaxValue),
            DslClr.Byte => (byte.MinValue, byte.MaxValue),
            DslClr.Int16 => (short.MinValue, short.MaxValue),
            DslClr.UInt16 => (ushort.MinValue, ushort.MaxValue),
            DslClr.Int32 => (int.MinValue, int.MaxValue),
            DslClr.UInt32 => (uint.MinValue, uint.MaxValue),
            DslClr.Int64 => (long.MinValue, long.MaxValue),
            DslClr.UInt64 => (ulong.MinValue, ulong.MaxValue),
            _ => null,
        };
}
