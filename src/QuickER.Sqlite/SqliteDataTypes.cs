using System.Collections.Generic;

namespace QuickER.Sqlite;

/// <summary>
/// SQLite の代表的なデータ型一覧を提供する静的クラス
/// プロパティパネルのデータ型 ComboBox の選択肢として使用する
/// </summary>
/// <remarks>
/// SQLite は宣言型（declared type）文字列を verbatim に保存し読み戻せるため、本プロバイダは
/// SQL Server 風のリッチな宣言型（<c>NVARCHAR(50)</c> / <c>DECIMAL(18,2)</c> / <c>DATETIME2</c> 等）を
/// そのまま採用する。これにより SQL Server ⇄ SQLite のスキーマ往復をほぼ無損失にできる
/// （型親和性ルールは <see cref="SqliteTypeCatalog"/> のパース側で吸収する）。
/// </remarks>
public static class SqliteDataTypes
{
    /// <summary>SQLite の代表的なデータ型一覧（SQL Server 風のリッチな宣言型を含む）</summary>
    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            // 数値
            "BIT",
            "TINYINT",
            "SMALLINT",
            "INT",
            "BIGINT",
            "DECIMAL(10,2)",
            "DECIMAL(18,0)",
            "MONEY",
            "FLOAT",
            "REAL",
            // 文字列
            "CHAR(10)",
            "VARCHAR(50)",
            "VARCHAR(100)",
            "VARCHAR(255)",
            "VARCHAR(MAX)",
            "NCHAR(10)",
            "NVARCHAR(50)",
            "NVARCHAR(100)",
            "NVARCHAR(255)",
            "NVARCHAR(MAX)",
            "TEXT",
            // 日付/時刻
            "DATE",
            "TIME",
            "DATETIME",
            "DATETIME2",
            "DATETIMEOFFSET",
            // バイナリ
            "BINARY(50)",
            "VARBINARY(50)",
            "VARBINARY(MAX)",
            "BLOB",
            // その他
            "UNIQUEIDENTIFIER",
            "XML",
            "JSON",
        };
}
