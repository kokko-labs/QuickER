using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 全 <see cref="CanonicalTypeKind"/>（23 種）× 全方言（5 実装）の総当たりで
/// <c>TryFormat</c> → <c>TryParse</c> の往復結果を表として固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 既存の <c>*TypeCatalogTests</c> は手書き <c>InlineData</c> の積み上げで、方言切替（<c>DiagramTypeConverter</c>）が
/// 実際に依存している「どの種別がどの方言へ落ちるか／落ちないか」の全体像がどこにも表明されていなかった。
/// ここは<b>仕様の可視化</b>が目的で、挙動を変えるテストではない。新しい <see cref="CanonicalTypeKind"/> を足すと
/// 全方言分のセル宣言が強制され、「気づかないうちに非可逆な変換が増えていた」を構造的に防ぐ。
/// </para>
/// <para>
/// 各セルは次の 3 値（＋境界の 1 値）に分類される:
/// </para>
/// <list type="bullet">
///   <item><b>可逆</b>: <c>TryFormat</c> が成功し、その出力を同じ方言で <c>TryParse</c> すると元の正規型へ戻る</item>
///   <item><b>非可逆（引数）</b>: 種別は保たれるが長さ・精度が変わる（例: PostgreSQL の <c>bytea</c> は長さを持たない）</item>
///   <item><b>非可逆（種別）</b>: 別の種別へ落ちる（例: PostgreSQL に <c>tinyint</c> が無く <c>smallint</c> になる）</item>
///   <item><b>変換不能</b>: <c>TryFormat</c> が失敗する（方言切替時に「変換できない列」として一覧に載る）</item>
/// </list>
/// <para>
/// 入力は種別ごとの代表値（<see cref="RepresentativeSample"/>＝<c>CanonicalTypeTokenTests</c> と同じ規約）で、
/// 長さ・精度を取る種別だけ引数を与える。表の右辺は「ネイティブ型表記」と「読み戻した正規型のトークン表記」の対で、
/// これがそのまま仕様の記述になる。
/// </para>
/// </remarks>
public class CanonicalTypeRoundTripMatrixTests
{
    /// <summary>往復の結果分類</summary>
    private enum RoundTripOutcome
    {
        /// <summary>TryFormat が失敗する（方言切替で「変換不能」として一覧に載る）</summary>
        NotConvertible,

        /// <summary>書き出せるが、その表記を同じ方言で読み戻せない</summary>
        ReparseFailed,

        /// <summary>書き出して読み戻すと元の正規型（引数まで）へ戻る</summary>
        Reversible,

        /// <summary>種別は保たれるが長さ・精度が変わる</summary>
        LossyArguments,

        /// <summary>別の種別へ落ちる</summary>
        LossyKind,
    }

    /// <summary>1 セルの実測結果</summary>
    /// <param name="NativeType">TryFormat の出力（変換不能なら <c>null</c>）</param>
    /// <param name="Reparsed">出力を TryParse で読み戻した正規型のトークン表記（読み戻せないなら <c>null</c>）</param>
    private sealed record CellResult(string? NativeType, string? Reparsed);

    /// <summary>方言名（表の列。方言プロジェクトの 5 実装と 1:1）</summary>
    private static readonly string[] DialectNames =
    [
        "sqlserver",
        "postgresql",
        "mysql",
        "oracle",
        "sqlite",
    ];

    /// <summary>方言名から型カタログを得る（テストは公開 API 越しに直接 new する既存流儀に合わせる）</summary>
    private static ITypeCatalog CatalogOf(string dialect) =>
        dialect switch
        {
            "sqlserver" => new SqlServerTypeCatalog(),
            "postgresql" => new PostgreSqlTypeCatalog(),
            "mysql" => new MySqlTypeCatalog(),
            "oracle" => new OracleTypeCatalog(),
            "sqlite" => new SqliteTypeCatalog(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "未知の方言"),
        };

    /// <summary>
    /// 期待表。キーは (方言, 種別)、値は「TryFormat の出力」と「読み戻した正規型のトークン表記」の対。
    /// </summary>
    /// <remarks>
    /// <c>null</c> のネイティブ型＝変換不能（TryFormat が false）。ネイティブ型はあるが読み戻しが <c>null</c>＝
    /// 書き出し専用（同じ方言で読み戻せない）。宣言の抜け・余りはどちらもテストが名指しで落とす。
    /// </remarks>
    private static readonly IReadOnlyDictionary<
        (string Dialect, CanonicalTypeKind Kind),
        CellResult
    > Expected = BuildExpectedTable();

    /// <summary>
    /// 全 23 種別 × 5 方言のセルが、宣言された往復結果（ネイティブ型表記・読み戻した正規型）と一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "全 CanonicalTypeKind × 5 方言の TryFormat→TryParse 往復が宣言表と一致する"
    )]
    public void RoundTripMatrix_ShouldMatchDeclaredTable()
    {
        var mismatches = new List<string>();

        foreach (var dialect in DialectNames)
        {
            var catalog = CatalogOf(dialect);

            foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
            {
                var actual = Measure(catalog, kind);

                if (!Expected.TryGetValue((dialect, kind), out var expected))
                {
                    mismatches.Add(
                        $"[{dialect} × {kind}] 期待表に未宣言。実測は {Describe(actual)} "
                            + $"（宣言行: {DeclarationLine(dialect, kind, actual)}）"
                    );

                    continue;
                }

                if (actual != expected)
                {
                    mismatches.Add(
                        $"[{dialect} × {kind}] 期待 {Describe(expected)} / 実測 {Describe(actual)}"
                    );
                }
            }
        }

        mismatches
            .Should()
            .BeEmpty(
                "型カタログの往復表が宣言と食い違っている（挙動を変えたなら期待表も更新すること）:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, mismatches)
            );
    }

    /// <summary>期待表に、実在しない (方言, 種別) の宣言が残っていないことを検証する（種別を削ったときの取り残し検知）</summary>
    [Fact(DisplayName = "型往復の期待表に実在しない方言・種別の宣言が残っていない")]
    public void ExpectedTable_ShouldNotDeclareUnknownCells()
    {
        var kinds = Enum.GetValues<CanonicalTypeKind>().ToHashSet();
        var dialects = DialectNames.ToHashSet(StringComparer.Ordinal);

        var stale = Expected
            .Keys.Where(key => !dialects.Contains(key.Dialect) || !kinds.Contains(key.Kind))
            .Select(key => $"{key.Dialect} × {key.Kind}")
            .ToList();

        stale
            .Should()
            .BeEmpty("期待表に実在しない方言・種別の宣言が残っている: " + string.Join(", ", stale));

        Expected
            .Should()
            .HaveCount(
                DialectNames.Length * kinds.Count,
                "期待表は 全方言 × 全種別 のセルをちょうど 1 つずつ持つべき"
            );
    }

    /// <summary>
    /// 「変換不能」と宣言されたセルが実際に <c>TryFormat</c> で失敗し、それ以外は成功することを分類の水準で再確認する。
    /// </summary>
    /// <remarks>
    /// 上のセル一致テストと重複するが、こちらは失敗時に<b>分類</b>（可逆／非可逆／変換不能）で報告するため、
    /// 「非可逆が増えた」ことが一目で分かる。方言切替 UI が「変換できない列」として一覧に出す集合の正本でもある。
    /// </remarks>
    [Fact(DisplayName = "方言ごとの変換不能・非可逆の集合が宣言表から導かれる分類と一致する")]
    public void RoundTripMatrix_ClassificationShouldMatch()
    {
        var mismatches = new List<string>();

        foreach (var dialect in DialectNames)
        {
            var catalog = CatalogOf(dialect);

            foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
            {
                // 未宣言セルはセル一致テストが名指しで落とすため、ここでは分類の比較対象から外す
                if (!Expected.TryGetValue((dialect, kind), out var declared))
                {
                    continue;
                }

                var expected = Classify(kind, declared);
                var actual = Classify(kind, Measure(catalog, kind));

                if (expected != actual)
                {
                    mismatches.Add($"[{dialect} × {kind}] 期待 {expected} / 実測 {actual}");
                }
            }
        }

        mismatches
            .Should()
            .BeEmpty(
                "型カタログの往復分類が宣言と食い違っている:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, mismatches)
            );
    }

    /// <summary>指定方言・種別の代表値について TryFormat → TryParse を実測する</summary>
    private static CellResult Measure(ITypeCatalog catalog, CanonicalTypeKind kind)
    {
        var canonical = RepresentativeSample(kind);

        if (!catalog.TryFormat(canonical, out var nativeType))
        {
            return new CellResult(null, null);
        }

        return catalog.TryParse(nativeType, out var parsed)
            ? new CellResult(nativeType, CanonicalTypeToken.Format(parsed))
            : new CellResult(nativeType, null);
    }

    /// <summary>セル結果を分類する（元の正規型のトークン表記と読み戻しの比較）</summary>
    private static RoundTripOutcome Classify(CanonicalTypeKind kind, CellResult cell)
    {
        if (cell.NativeType is null)
        {
            return RoundTripOutcome.NotConvertible;
        }

        if (cell.Reparsed is null)
        {
            return RoundTripOutcome.ReparseFailed;
        }

        var source = CanonicalTypeToken.Format(RepresentativeSample(kind));

        if (string.Equals(source, cell.Reparsed, StringComparison.Ordinal))
        {
            return RoundTripOutcome.Reversible;
        }

        return CanonicalTypeToken.TryParse(cell.Reparsed, out var parsed) && parsed.Kind == kind
            ? RoundTripOutcome.LossyArguments
            : RoundTripOutcome.LossyKind;
    }

    /// <summary>セル結果を人間可読の 1 行へ整形する</summary>
    private static string Describe(CellResult cell) =>
        cell switch
        {
            { NativeType: null } => "変換不能",
            { Reparsed: null } => $"'{cell.NativeType}'（読み戻し不可）",
            _ => $"'{cell.NativeType}' → {cell.Reparsed}",
        };

    /// <summary>未宣言セルの失敗メッセージへ載せる、コピーしてそのまま貼れる宣言行</summary>
    private static string DeclarationLine(
        string dialect,
        CanonicalTypeKind kind,
        CellResult cell
    ) =>
        cell switch
        {
            { NativeType: null } => $"Unsupported(\"{dialect}\", CanonicalTypeKind.{kind}),",
            { Reparsed: null } =>
                $"FormatOnly(\"{dialect}\", CanonicalTypeKind.{kind}, \"{cell.NativeType}\"),",
            _ =>
                $"Cell(\"{dialect}\", CanonicalTypeKind.{kind}, \"{cell.NativeType}\", \"{cell.Reparsed}\"),",
        };

    /// <summary>各種別の代表サンプル（<c>CanonicalTypeTokenTests.RepresentativeSample</c> と同じ規約）</summary>
    private static CanonicalType RepresentativeSample(CanonicalTypeKind kind) =>
        kind switch
        {
            CanonicalTypeKind.String
            or CanonicalTypeKind.AnsiString
            or CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString
            or CanonicalTypeKind.Binary
            or CanonicalTypeKind.FixedBinary => new CanonicalType(kind, Length: 42),
            CanonicalTypeKind.Decimal => new CanonicalType(kind, Precision: 12, Scale: 3),
            CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => new CanonicalType(kind, Precision: 6),
            _ => new CanonicalType(kind),
        };

    /// <summary>期待表を組み立てる</summary>
    private static IReadOnlyDictionary<(string, CanonicalTypeKind), CellResult> BuildExpectedTable()
    {
        var table = new Dictionary<(string, CanonicalTypeKind), CellResult>();

        foreach (var entry in Declarations())
        {
            table[entry.Key] = entry.Value;
        }

        return table;
    }

    /// <summary>往復可能なセル（ネイティブ型表記と読み戻したトークン表記）を宣言する</summary>
    private static KeyValuePair<(string, CanonicalTypeKind), CellResult> Cell(
        string dialect,
        CanonicalTypeKind kind,
        string nativeType,
        string reparsed
    ) => new((dialect, kind), new CellResult(nativeType, reparsed));

    /// <summary>書き出せるが同じ方言で読み戻せないセルを宣言する</summary>
    private static KeyValuePair<(string, CanonicalTypeKind), CellResult> FormatOnly(
        string dialect,
        CanonicalTypeKind kind,
        string nativeType
    ) => new((dialect, kind), new CellResult(nativeType, null));

    /// <summary>TryFormat が失敗する（変換不能な）セルを宣言する</summary>
    private static KeyValuePair<(string, CanonicalTypeKind), CellResult> Unsupported(
        string dialect,
        CanonicalTypeKind kind
    ) => new((dialect, kind), new CellResult(null, null));

    /// <summary>
    /// 期待表の宣言本体（方言ごと・<see cref="CanonicalTypeKind"/> の宣言順）。
    /// </summary>
    /// <remarks>
    /// 代表値は長さ 42・精度 12/位取り 3・小数秒 6。右辺のトークンが左辺と同じ種別なら可逆、
    /// 別種別なら非可逆（コメントで理由を添える）。
    /// </remarks>
    private static IEnumerable<
        KeyValuePair<(string, CanonicalTypeKind), CellResult>
    > Declarations() =>
        [
            // ---- SQL Server: json 以外は全種別が可逆（正規型の設計基準となる方言）----
            Cell("sqlserver", CanonicalTypeKind.Boolean, "bit", "boolean"),
            Cell("sqlserver", CanonicalTypeKind.TinyInt, "tinyint", "tinyint"),
            Cell("sqlserver", CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            Cell("sqlserver", CanonicalTypeKind.Int32, "int", "int32"),
            Cell("sqlserver", CanonicalTypeKind.Int64, "bigint", "int64"),
            Cell("sqlserver", CanonicalTypeKind.Decimal, "decimal(12,3)", "decimal(12,3)"),
            Cell("sqlserver", CanonicalTypeKind.Float32, "real", "float32"),
            Cell("sqlserver", CanonicalTypeKind.Float64, "float", "float64"),
            Cell("sqlserver", CanonicalTypeKind.Money, "money", "money"),
            Cell("sqlserver", CanonicalTypeKind.String, "nvarchar(42)", "string(42)"),
            Cell("sqlserver", CanonicalTypeKind.AnsiString, "varchar(42)", "ansistring(42)"),
            Cell("sqlserver", CanonicalTypeKind.FixedString, "nchar(42)", "fixedstring(42)"),
            Cell("sqlserver", CanonicalTypeKind.AnsiFixedString, "char(42)", "ansifixedstring(42)"),
            Cell("sqlserver", CanonicalTypeKind.Binary, "varbinary(42)", "binary(42)"),
            Cell("sqlserver", CanonicalTypeKind.FixedBinary, "binary(42)", "fixedbinary(42)"),
            Cell("sqlserver", CanonicalTypeKind.Date, "date", "date"),
            Cell("sqlserver", CanonicalTypeKind.Time, "time(6)", "time(6)"),
            Cell("sqlserver", CanonicalTypeKind.DateTime, "datetime2(6)", "datetime(6)"),
            Cell(
                "sqlserver",
                CanonicalTypeKind.DateTimeOffset,
                "datetimeoffset(6)",
                "datetimeoffset(6)"
            ),
            Cell("sqlserver", CanonicalTypeKind.Guid, "uniqueidentifier", "guid"),
            Cell("sqlserver", CanonicalTypeKind.Xml, "xml", "xml"),
            // 非可逆（種別）: SqlServerDataTypes に json 型が無く nvarchar(max) を代替に使うため文字列へ落ちる
            Cell("sqlserver", CanonicalTypeKind.Json, "nvarchar(max)", "string(max)"),
            Cell("sqlserver", CanonicalTypeKind.RowVersion, "rowversion", "rowversion"),
            // ---- PostgreSQL ----
            Cell("postgresql", CanonicalTypeKind.Boolean, "boolean", "boolean"),
            // 非可逆（種別）: PostgreSQL に tinyint が無く smallint で受ける
            Cell("postgresql", CanonicalTypeKind.TinyInt, "smallint", "smallint"),
            Cell("postgresql", CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            Cell("postgresql", CanonicalTypeKind.Int32, "integer", "int32"),
            Cell("postgresql", CanonicalTypeKind.Int64, "bigint", "int64"),
            Cell("postgresql", CanonicalTypeKind.Decimal, "numeric(12,3)", "decimal(12,3)"),
            Cell("postgresql", CanonicalTypeKind.Float32, "real", "float32"),
            Cell("postgresql", CanonicalTypeKind.Float64, "double precision", "float64"),
            Cell("postgresql", CanonicalTypeKind.Money, "money", "money"),
            Cell("postgresql", CanonicalTypeKind.String, "varchar(42)", "string(42)"),
            // 非可逆（種別）: PostgreSQL の varchar は Unicode 可変長のみ＝Ansi の区別が消える
            Cell("postgresql", CanonicalTypeKind.AnsiString, "varchar(42)", "string(42)"),
            Cell("postgresql", CanonicalTypeKind.FixedString, "char(42)", "fixedstring(42)"),
            // 非可逆（種別）: 同上（固定長も Ansi の区別が消える）
            Cell("postgresql", CanonicalTypeKind.AnsiFixedString, "char(42)", "fixedstring(42)"),
            // 非可逆（引数）: bytea は長さの概念を持たないため上限なしのバイナリになる
            Cell("postgresql", CanonicalTypeKind.Binary, "bytea", "binary(max)"),
            // 非可逆（種別）: 固定長バイナリも bytea 単一型へ落ちる
            Cell("postgresql", CanonicalTypeKind.FixedBinary, "bytea", "binary(max)"),
            Cell("postgresql", CanonicalTypeKind.Date, "date", "date"),
            Cell("postgresql", CanonicalTypeKind.Time, "time(6)", "time(6)"),
            Cell("postgresql", CanonicalTypeKind.DateTime, "timestamp(6)", "datetime(6)"),
            Cell(
                "postgresql",
                CanonicalTypeKind.DateTimeOffset,
                "timestamptz(6)",
                "datetimeoffset(6)"
            ),
            Cell("postgresql", CanonicalTypeKind.Guid, "uuid", "guid"),
            Cell("postgresql", CanonicalTypeKind.Xml, "xml", "xml"),
            Cell("postgresql", CanonicalTypeKind.Json, "jsonb", "json"),
            // 変換不能: 行バージョンは SQL Server 固有（方言切替で「変換できない列」に載る）
            Unsupported("postgresql", CanonicalTypeKind.RowVersion),
            // ---- MySQL ----
            Cell("mysql", CanonicalTypeKind.Boolean, "tinyint(1)", "boolean"),
            Cell("mysql", CanonicalTypeKind.TinyInt, "tinyint unsigned", "tinyint"),
            Cell("mysql", CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            Cell("mysql", CanonicalTypeKind.Int32, "int", "int32"),
            Cell("mysql", CanonicalTypeKind.Int64, "bigint", "int64"),
            Cell("mysql", CanonicalTypeKind.Decimal, "decimal(12,3)", "decimal(12,3)"),
            Cell("mysql", CanonicalTypeKind.Float32, "float", "float32"),
            Cell("mysql", CanonicalTypeKind.Float64, "double", "float64"),
            // 非可逆（種別）: MySQL に通貨型が無く decimal(19,4) で受ける（format 専用）
            Cell("mysql", CanonicalTypeKind.Money, "decimal(19,4)", "decimal(19,4)"),
            Cell("mysql", CanonicalTypeKind.String, "varchar(42)", "string(42)"),
            // 非可逆（種別）: utf8mb4 前提のため Ansi の区別が消える
            Cell("mysql", CanonicalTypeKind.AnsiString, "varchar(42)", "string(42)"),
            Cell("mysql", CanonicalTypeKind.FixedString, "char(42)", "fixedstring(42)"),
            // 非可逆（種別）: 同上
            Cell("mysql", CanonicalTypeKind.AnsiFixedString, "char(42)", "fixedstring(42)"),
            Cell("mysql", CanonicalTypeKind.Binary, "varbinary(42)", "binary(42)"),
            Cell("mysql", CanonicalTypeKind.FixedBinary, "binary(42)", "fixedbinary(42)"),
            Cell("mysql", CanonicalTypeKind.Date, "date", "date"),
            Cell("mysql", CanonicalTypeKind.Time, "time(6)", "time(6)"),
            Cell("mysql", CanonicalTypeKind.DateTime, "datetime(6)", "datetime(6)"),
            Cell("mysql", CanonicalTypeKind.DateTimeOffset, "timestamp(6)", "datetimeoffset(6)"),
            // 非可逆（種別）: MySQL に UUID 型が無く char(36) で受ける（format 専用）
            Cell("mysql", CanonicalTypeKind.Guid, "char(36)", "fixedstring(36)"),
            // 非可逆（種別）: MySQL に XML 型が無く longtext で受ける（format 専用）
            Cell("mysql", CanonicalTypeKind.Xml, "longtext", "string(max)"),
            Cell("mysql", CanonicalTypeKind.Json, "json", "json"),
            // 変換不能: 行バージョンは SQL Server 固有
            Unsupported("mysql", CanonicalTypeKind.RowVersion),
            // ---- Oracle ----
            Cell("oracle", CanonicalTypeKind.Boolean, "NUMBER(1)", "boolean"),
            Cell("oracle", CanonicalTypeKind.TinyInt, "NUMBER(3)", "tinyint"),
            Cell("oracle", CanonicalTypeKind.SmallInt, "NUMBER(5)", "smallint"),
            Cell("oracle", CanonicalTypeKind.Int32, "NUMBER(10)", "int32"),
            Cell("oracle", CanonicalTypeKind.Int64, "NUMBER(19)", "int64"),
            Cell("oracle", CanonicalTypeKind.Decimal, "NUMBER(12,3)", "decimal(12,3)"),
            Cell("oracle", CanonicalTypeKind.Float32, "BINARY_FLOAT", "float32"),
            Cell("oracle", CanonicalTypeKind.Float64, "BINARY_DOUBLE", "float64"),
            // 非可逆（種別）: Oracle に通貨型が無く NUMBER(19,4) で受ける
            Cell("oracle", CanonicalTypeKind.Money, "NUMBER(19,4)", "decimal(19,4)"),
            Cell("oracle", CanonicalTypeKind.String, "NVARCHAR2(42)", "string(42)"),
            Cell("oracle", CanonicalTypeKind.AnsiString, "VARCHAR2(42)", "ansistring(42)"),
            Cell("oracle", CanonicalTypeKind.FixedString, "NCHAR(42)", "fixedstring(42)"),
            Cell("oracle", CanonicalTypeKind.AnsiFixedString, "CHAR(42)", "ansifixedstring(42)"),
            Cell("oracle", CanonicalTypeKind.Binary, "RAW(42)", "binary(42)"),
            // 非可逆（種別）: 可変長・固定長ともに RAW(n) 単一型へ落ちる
            Cell("oracle", CanonicalTypeKind.FixedBinary, "RAW(42)", "binary(42)"),
            // 非可逆（種別）: Oracle の DATE は時刻を含むため、日付のみが日時になる（設計上の非対称）
            Cell("oracle", CanonicalTypeKind.Date, "DATE", "datetime"),
            // 変換不能: Oracle に TIME 型が無い
            Unsupported("oracle", CanonicalTypeKind.Time),
            Cell("oracle", CanonicalTypeKind.DateTime, "TIMESTAMP(6)", "datetime(6)"),
            Cell(
                "oracle",
                CanonicalTypeKind.DateTimeOffset,
                "TIMESTAMP(6) WITH TIME ZONE",
                "datetimeoffset(6)"
            ),
            // 非可逆（種別）: Oracle に UUID 型が無く RAW(16) で表現する（format 専用）
            Cell("oracle", CanonicalTypeKind.Guid, "RAW(16)", "binary(16)"),
            Cell("oracle", CanonicalTypeKind.Xml, "XMLTYPE", "xml"),
            // 非可逆（種別）: 19c に JSON 型が無く CLOB で表現する（format 専用）
            Cell("oracle", CanonicalTypeKind.Json, "CLOB", "ansistring(max)"),
            // 変換不能: 行バージョンは SQL Server 固有
            Unsupported("oracle", CanonicalTypeKind.RowVersion),
            // ---- SQLite（型親和性のため SQL Server 表記をほぼそのまま受ける）----
            Cell("sqlite", CanonicalTypeKind.Boolean, "BIT", "boolean"),
            Cell("sqlite", CanonicalTypeKind.TinyInt, "TINYINT", "tinyint"),
            Cell("sqlite", CanonicalTypeKind.SmallInt, "SMALLINT", "smallint"),
            Cell("sqlite", CanonicalTypeKind.Int32, "INT", "int32"),
            Cell("sqlite", CanonicalTypeKind.Int64, "BIGINT", "int64"),
            Cell("sqlite", CanonicalTypeKind.Decimal, "DECIMAL(12,3)", "decimal(12,3)"),
            Cell("sqlite", CanonicalTypeKind.Float32, "REAL", "float32"),
            Cell("sqlite", CanonicalTypeKind.Float64, "FLOAT", "float64"),
            Cell("sqlite", CanonicalTypeKind.Money, "MONEY", "money"),
            Cell("sqlite", CanonicalTypeKind.String, "NVARCHAR(42)", "string(42)"),
            Cell("sqlite", CanonicalTypeKind.AnsiString, "VARCHAR(42)", "ansistring(42)"),
            Cell("sqlite", CanonicalTypeKind.FixedString, "NCHAR(42)", "fixedstring(42)"),
            Cell("sqlite", CanonicalTypeKind.AnsiFixedString, "CHAR(42)", "ansifixedstring(42)"),
            Cell("sqlite", CanonicalTypeKind.Binary, "VARBINARY(42)", "binary(42)"),
            Cell("sqlite", CanonicalTypeKind.FixedBinary, "BINARY(42)", "fixedbinary(42)"),
            Cell("sqlite", CanonicalTypeKind.Date, "DATE", "date"),
            Cell("sqlite", CanonicalTypeKind.Time, "TIME(6)", "time(6)"),
            Cell("sqlite", CanonicalTypeKind.DateTime, "DATETIME2(6)", "datetime(6)"),
            Cell(
                "sqlite",
                CanonicalTypeKind.DateTimeOffset,
                "DATETIMEOFFSET(6)",
                "datetimeoffset(6)"
            ),
            Cell("sqlite", CanonicalTypeKind.Guid, "UNIQUEIDENTIFIER", "guid"),
            Cell("sqlite", CanonicalTypeKind.Xml, "XML", "xml"),
            Cell("sqlite", CanonicalTypeKind.Json, "JSON", "json"),
            // 非可逆（種別）: SQLite に行バージョンの概念が無く、値だけを写せる BLOB（ミラー列）へ落とす。
            // 読み戻すと上限なしのバイナリになるため、SQL Server へ戻しても varbinary(max) にしかならない
            Cell("sqlite", CanonicalTypeKind.RowVersion, "BLOB", "binary(max)"),
        ];
}
