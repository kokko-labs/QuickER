using System.Collections.Generic;

namespace QuickER.Oracle;

/// <summary>
/// Oracle の代表的なデータ型一覧を提供する静的クラス
/// プロパティパネルのデータ型 ComboBox の選択肢として使用する
/// </summary>
public static class OracleDataTypes
{
    /// <summary>Oracle の代表的なデータ型一覧（よく使うパラメーター付きの定型表記を含む）</summary>
    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            // 数値（NUMBER の精度で C# 整数型へ振り分ける）
            "NUMBER(1)",
            "NUMBER(3)",
            "NUMBER(5)",
            "NUMBER(10)",
            "NUMBER(19)",
            "NUMBER(10,2)",
            "BINARY_FLOAT",
            "BINARY_DOUBLE",
            // 文字列（N 付きは Unicode、無しは ANSI）
            "NVARCHAR2(50)",
            "VARCHAR2(50)",
            "NCHAR(10)",
            "CHAR(10)",
            "NCLOB",
            "CLOB",
            // バイナリ
            "RAW(16)",
            "BLOB",
            // 日付/時刻
            "DATE",
            "TIMESTAMP",
            "TIMESTAMP WITH TIME ZONE",
            // その他
            "XMLTYPE",
        };
}
