using QuickER.Tests.Provider;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 方言の型の表の実 DB の段（<c>Dialect/TypesRealDb</c>）が作る入力の値の観測：書き込みの範囲の手順の値（範囲の端と外）と、EF Core の手順で書く値
/// </summary>
/// <remarks>
/// 列の相は手順が DDL に書く型の表記（手順の <c>Spelling</c>、無ければ表の行の出力＝<see cref="DialectTypeRealDbTestsBase.RowSpelling"/>）から読む。
/// ドライバの手順の値は SQL のリテラルの文字列で、型の値でないので観測しない。
/// </remarks>
internal static class DialectTypeRealDbValueObservations
{
    /// <summary>全部の手順の書く値</summary>
    internal static IReadOnlyList<ValueObservation> Observe()
    {
        var observations = new List<ValueObservation>();

        foreach (var probe in DialectTypeRealDbRows.All)
        {
            if (probe.Writes is null && probe.Write is null)
            {
                continue;
            }

            var row = DialectTypeRows.Table[probe.RowId].Input;
            var column = ValueColumn.FromSqlType(
                probe.Spelling
                    ?? DialectTypeRealDbTestsBase.RowSpelling(
                        row,
                        DialectTypeAxis.Get(probe.Dialect)
                    )
            );
            column = WithDialectDefaultScale(column, probe.Dialect);
            var where = probe.Describe();

            if (probe.Write is not null)
            {
                observations.Add(new ValueObservation(probe.Write, column, where));
            }

            foreach (var write in probe.Writes ?? [])
            {
                observations.Add(
                    new ValueObservation(
                        write.Value,
                        column,
                        $"{where}（{(write.Fits ? "範囲の中" : "範囲の外")}）"
                    )
                );
            }
        }

        return observations;
    }

    /// <summary>
    /// 桁を書かない時刻の型の小数秒の既定の桁を方言の既定へ付け直す（宣言の型の名前から読む相の既定は SQL Server の 7。PostgreSQL の time・timestamp は 6、
    /// MySQL の time・datetime・timestamp は 0）
    /// </summary>
    private static ValueColumn WithDialectDefaultScale(ValueColumn column, string dialect)
    {
        var type = column.SqlType?.ToLowerInvariant() ?? "";

        if (type.Contains('(', StringComparison.Ordinal))
        {
            return column;
        }

        return dialect switch
        {
            QuickER.Dialects.DialectNames.PostgreSql
                when type.StartsWith("time", StringComparison.Ordinal) => column with
            {
                Scale = 6,
            },
            QuickER.Dialects.DialectNames.MySql
                when type.StartsWith("time", StringComparison.Ordinal) || type == "datetime" =>
                column with
                {
                    Scale = 0,
                },
            _ => column,
        };
    }
}
