using System.Collections.Generic;

namespace QuickER.PostgreSql;

/// <summary>
/// PostgreSQL の代表的なデータ型一覧を提供する静的クラス
/// プロパティパネルのデータ型 ComboBox の選択肢として使用する
/// </summary>
public static class PostgreSqlDataTypes
{
    /// <summary>PostgreSQL の代表的なデータ型一覧（よく使うパラメーター付きの定型表記を含む）</summary>
    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            // 数値
            "boolean",
            "smallint",
            "integer",
            "bigint",
            "numeric(10,2)",
            "numeric(18,0)",
            "real",
            "double precision",
            "money",
            // 文字列
            "varchar(50)",
            "varchar(100)",
            "varchar(255)",
            "text",
            "char(10)",
            // バイナリ
            "bytea",
            // 日付/時刻
            "date",
            "time",
            "timestamp",
            "timestamptz",
            // その他
            "uuid",
            "xml",
            "json",
            "jsonb",
        };
}
