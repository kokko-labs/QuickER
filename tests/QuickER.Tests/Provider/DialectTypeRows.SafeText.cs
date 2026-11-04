using System.Text.RegularExpressions;
using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>型の表記の安全性（<see cref="SqlTypeText.IsSafe"/>）の行（方言なし。カタログ全表記だけ 5 方言）</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> SafeTextRows() =>
        [
            Catalog("catalog-safe-all-data-types", "5 方言の型カタログの全表記が安全パターンを通る")
                .ExpectAll("ok all-safe=1"),
            .. SafeTextRealWorldRows(),
            .. SafeTextRejectedRows(
                "safe-unsafe",
                "SQL を壊せる型表記は安全パターンを通らない",
                [
                    ("drop-table-comment", "int'; DROP TABLE users; --"),
                    ("trailing-quote", "varchar(50)'"),
                    ("crlf", "int\r\nDROP TABLE users"),
                    ("line-comment", "int -- comment"),
                    ("block-comment", "int /* comment */"),
                    ("semicolon", "int; DELETE FROM users"),
                    ("bracket-quoted", "[int]"),
                    ("double-quoted", "\"char\""),
                    ("backtick-quoted", "`int`"),
                    ("backslash", "int\\"),
                    ("hyphenated-word", "user-defined"),
                    // 値リスト型に見せかけてリテラルを閉じる形（\ による脱出は許さない）
                    ("enum-backslash-escape", @"enum('a\','b')"),
                    ("enum-close-literal", "enum('a'; DROP TABLE users; --')"),
                ]
            ),
            SafeText(
                    "safe-too-long",
                    "長さ上限を超える型表記は安全パターンを通らない",
                    new string('a', SqlTypeText.MaxLength + 1)
                )
                .Expect("unsafe"),
            .. SafeTextRejectedRows(
                "safe-hash",
                "# を含む型表記は安全パターンを通らない（MySQL の行末コメント）",
                [("trailing", "int#"), ("trailing-comment", "int# comment"), ("leading", "#int")]
            ),
            .. SafeTextRejectedRows(
                "safe-untrimmed",
                "前後に空白・改行が付いた型表記は安全パターンを通らない",
                [
                    ("newline-go", "\nGO\n"),
                    ("leading-space", " int"),
                    ("trailing-space", "int "),
                    ("both-spaces", " int "),
                    ("leading-tab", "\tint"),
                ]
            ),
            .. SafeTextRejectedRows(
                "safe-smuggled",
                "2 語目以降に列定義の句を含む型表記は安全パターンを通らない",
                [
                    ("not-null", "int NOT NULL"),
                    ("default", "int DEFAULT 0"),
                    ("primary-key", "int PRIMARY KEY"),
                    ("references", "int REFERENCES users"),
                    ("generated-always", "int GENERATED ALWAYS"),
                    ("identity", "int IDENTITY"),
                    ("collate", "varchar(50) COLLATE Latin1_General_BIN"),
                    ("unique", "int UNIQUE"),
                    ("check", "int CHECK"),
                    // 生成列の省略形・自動採番・可視性・名前付きの列制約（図に無い性質が DB 側にだけ生まれる）
                    ("as-space-paren", "int AS (price)"),
                    ("as-paren", "int AS(price)"),
                    ("auto-increment", "int AUTO_INCREMENT"),
                    ("autoincrement", "integer AUTOINCREMENT"),
                    ("invisible", "int INVISIBLE"),
                    ("visible", "int VISIBLE"),
                    ("constraint", "int CONSTRAINT foo"),
                ]
            ),
        ];

    /// <summary>安全でない型表記の行（同じ説明で、表記ごとに 1 行）</summary>
    private static IEnumerable<DialectTypeRow> SafeTextRejectedRows(
        string prefix,
        string description,
        (string Id, string DataType)[] rows
    ) =>
        rows.Select(row =>
            SafeText($"{prefix}-{row.Id}", description, row.DataType).Expect("unsafe")
        );

    /// <summary>取込が組み立てる実在の型表記（カタログ外を含む）が通る</summary>
    private static IEnumerable<DialectTypeRow> SafeTextRealWorldRows()
    {
        const string description = "取込が組み立てる実在の型表記が安全パターンを通る";
        (string Group, string[] Types)[] groups =
        [
            (
                "sqlserver",
                [
                    "nvarchar(max)",
                    "varbinary(max)",
                    "decimal(10,2)",
                    "decimal(18)",
                    "datetime2",
                    "uniqueidentifier",
                    "sql_variant",
                    "hierarchyid",
                    "rowversion",
                ]
            ),
            (
                "postgresql",
                [
                    "double precision",
                    "timestamp with time zone",
                    "timestamp without time zone",
                    "time with time zone",
                    "character varying(255)",
                    "bit varying",
                    "timestamptz(6)",
                    "numeric(10, 2)",
                    // 負のスケール（100 の倍数へ丸める）は PostgreSQL / Oracle の実在宣言で、取込がそのまま持ち帰る
                    "numeric(10,-2)",
                    "bit(8)",
                    "bit varying(16)",
                    "interval day to second(3)",
                    "time(3) with time zone",
                    "varchar(20)[]",
                    // 多次元配列（format_type は次元を出さないが、図の型は手入力・他ツール由来でもあり得る）
                    "integer[][]",
                    // PostGIS の型は括弧引数に語を取る。数値限定だと、この 1 列のせいで図全体の DDL 生成が止まる
                    "geometry",
                    "geometry(Point,4326)",
                    "geometry(MultiPolygon)",
                    "geography(Point,4326)",
                    "geometry(PointZ,4326)",
                    "_int4",
                    "jsonb",
                    "integer[]",
                ]
            ),
            (
                "mysql",
                [
                    "tinyint(1)",
                    "int unsigned",
                    "decimal(10,2) unsigned",
                    "bigint(20) unsigned zerofill",
                    "enum('a','b')",
                    "set('x', 'y')",
                    "longblob",
                ]
            ),
            (
                "oracle",
                [
                    "NUMBER(10,2)",
                    "NUMBER(10,-2)",
                    "NUMBER(*,2)",
                    "TIMESTAMP(6) WITH TIME ZONE",
                    "TIMESTAMP(6) WITH LOCAL TIME ZONE",
                    "INTERVAL DAY(2) TO SECOND(6)",
                    "INTERVAL YEAR(2) TO MONTH",
                    "LONG RAW",
                    "VARCHAR2(100 CHAR)",
                    "CHAR(10 BYTE)",
                    "SDO_GEOMETRY",
                    "XMLTYPE",
                ]
            ),
            // SQLite（外部 DB 由来の宣言型を verbatim に持ち帰る）
            (
                "sqlite",
                [
                    "NVARCHAR(MAX)",
                    "UNSIGNED BIG INT",
                    "VARYING CHARACTER(255)",
                    "NATIVE CHARACTER(70)",
                    "DOUBLE PRECISION",
                ]
            ),
        ];

        foreach (var (group, types) in groups)
        {
            foreach (var type in types)
            {
                yield return SafeText($"safe-real-{group}-{Slug(type)}", description, type)
                    .Expect("safe");
            }
        }

        // 型未設定は「注入」ではないので安全側
        yield return SafeText("safe-real-unset-empty", description, "").Expect("safe");
        yield return SafeText("safe-real-unset-blank", description, "   ").Expect("safe");
    }

    /// <summary>型の表記を Id に使える形（英小文字・数字・ハイフン）にする（記号は語に置き換えて衝突を避ける）</summary>
    private static string Slug(string text)
    {
        var replaced = text.Replace("*", "star", StringComparison.Ordinal)
            .Replace("-", "neg", StringComparison.Ordinal)
            .Replace("[", "arr", StringComparison.Ordinal)
            .Replace("'", "q", StringComparison.Ordinal);

        return Regex.Replace(replaced.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }
}
