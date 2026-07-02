using System.Collections.Generic;

namespace QuickER.MySql;

/// <summary>
/// MySQL の代表的なデータ型一覧を提供する静的クラス
/// プロパティパネルのデータ型 ComboBox の選択肢として使用する
/// </summary>
public static class MySqlDataTypes
{
    /// <summary>MySQL の代表的なデータ型一覧（よく使うパラメーター付きの定型表記を含む）</summary>
    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            // 数値
            "tinyint(1)",
            "tinyint unsigned",
            "smallint",
            "int",
            "bigint",
            "decimal(10,2)",
            "float",
            "double",
            // 文字列
            "varchar(255)",
            "char(10)",
            "text",
            "longtext",
            // バイナリ
            "varbinary(255)",
            "binary(16)",
            "longblob",
            // 日付/時刻
            "date",
            "time",
            "datetime",
            "timestamp",
            // その他
            "json",
        };
}
