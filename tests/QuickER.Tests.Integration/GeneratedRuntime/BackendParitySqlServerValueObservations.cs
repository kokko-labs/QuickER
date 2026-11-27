using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の値の往復（<c>GeneratedRuntime/ParityValuesSqlServer</c>）が作る入力の値の観測：決まった形の網羅と普段の既定の種のシナリオの
/// 書く値と列の相
/// </summary>
/// <remarks>
/// 列の相は <c>value_probes</c> の宣言の型（<see cref="SqlServerValueColumn"/> の XmlDoc の型）。種は普段の既定の数
/// （<see cref="BackendParitySqlServerValueRuntimeTests.Seeds"/>）で、生成器を同じ種で呼んで読むだけ（乱数を新しく引かない）。
/// </remarks>
internal static class BackendParitySqlServerValueObservations
{
    /// <summary>列の宣言の型</summary>
    internal static string SqlTypeOf(SqlServerValueColumn column) =>
        column switch
        {
            SqlServerValueColumn.At2 => "datetime2(2)",
            SqlServerValueColumn.At => "datetime2",
            SqlServerValueColumn.Clock => "time(2)",
            SqlServerValueColumn.AtOffset => "datetimeoffset",
            SqlServerValueColumn.AtOffset2 => "datetimeoffset(2)",
            SqlServerValueColumn.Day => "date",
            SqlServerValueColumn.Amount => "decimal(10,2)",
            SqlServerValueColumn.Ansi => "varchar(20)",
            SqlServerValueColumn.Code => "nchar(10)",
            SqlServerValueColumn.Label => "nvarchar(20)",
            SqlServerValueColumn.Tag => "varbinary(16)",
            SqlServerValueColumn.Payload => "varbinary(max)",
            SqlServerValueColumn.LegacyAt => "datetime",
            SqlServerValueColumn.SmallAt => "smalldatetime",
            SqlServerValueColumn.Price => "money",
            SqlServerValueColumn.Fee => "smallmoney",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };

    /// <summary>決まった形の網羅と普段の既定の種のシナリオの書く値</summary>
    internal static IReadOnlyList<ValueObservation> Observe()
    {
        var seeds = BackendParitySqlServerValueRuntimeTests.Seeds;
        var scenarios = new[] { BackendParitySqlServerValueGenerator.Fixed() }.Concat(
            Enumerable
                .Range(seeds.FirstSeed, seeds.RegularCount)
                .Select(BackendParitySqlServerValueGenerator.Create)
        );

        return
        [
            .. scenarios.SelectMany(scenario =>
                scenario.Writes.SelectMany(write =>
                    write.Rows.SelectMany(row =>
                        row.Cells.Select(cell => new ValueObservation(
                            cell.Value,
                            ValueColumn.FromSqlType(SqlTypeOf(cell.Column)),
                            $"{scenario.Name} の行 {row.Id} の {cell.Column}（{cell.Shape}・{write.Path}）"
                        ))
                    )
                )
            ),
        ];
    }
}
