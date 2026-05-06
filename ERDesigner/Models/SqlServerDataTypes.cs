using System.Collections.Generic;

namespace ERDesigner.Models;

/// <summary>
/// SQL Server で利用される代表的なデータ型の一覧を提供する静的クラスです。
/// プロパティパネルの型 ComboBox の選択肢として使用します。
/// </summary>
public static class SqlServerDataTypes
{
    /// <summary>SQL Server の代表的なデータ型一覧（既定値・パラメーター付きの定型を含む）。</summary>
    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            // 数値
            "bit",
            "tinyint",
            "smallint",
            "int",
            "bigint",
            "decimal(10,2)",
            "numeric(18,0)",
            "money",
            "smallmoney",
            "float",
            "real",
            // 文字列
            "char(10)",
            "varchar(50)",
            "varchar(100)",
            "varchar(255)",
            "varchar(max)",
            "nchar(10)",
            "nvarchar(50)",
            "nvarchar(100)",
            "nvarchar(255)",
            "nvarchar(max)",
            "text",
            "ntext",
            // 日付/時刻
            "date",
            "time",
            "datetime",
            "datetime2",
            "smalldatetime",
            "datetimeoffset",
            // バイナリ
            "binary(50)",
            "varbinary(50)",
            "varbinary(max)",
            "image",
            // その他
            "uniqueidentifier",
            "xml",
            "rowversion",
            "geography",
            "geometry",
            "hierarchyid",
            "sql_variant",
        };
}
