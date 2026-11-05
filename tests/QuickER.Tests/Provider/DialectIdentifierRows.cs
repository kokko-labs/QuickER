using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using F = QuickER.Tests.Provider.DialectIdentifierFunctions;
using S = QuickER.Tests.Provider.DialectIdentifierShapes;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の識別子の表（5 方言を行列の軸に、識別子のクォート・分割・エスケープ・動的 SQL 用のクォート・生成ランタイムの引用と、
/// <see cref="SchemaQualifiedName"/> の分け方を、入力と期待の行で固定する）
/// </summary>
/// <remarks>
/// <para>
/// 5 方言の識別子の境界（空白・記号・日本語・危険な文字）と、スキーマ修飾名の分け方・生成ランタイムの引用。方言ごとに持つ入力の期待だけを書く。欠けは網羅の計数の一覧に出る
/// </para>
/// <para>
/// 生成ランタイムの引用は、同じ入力の絶対値の期待で書く
/// （同じ名前の <c>quote-*</c> の行と合わせて、方言の関数との一致を含む）。
/// </para>
/// </remarks>
internal static class DialectIdentifierRows
{
    private const string Ss = DialectNames.SqlServer;
    private const string Pg = DialectNames.PostgreSql;
    private const string My = DialectNames.MySql;
    private const string Or = DialectNames.Oracle;
    private const string Sl = DialectNames.Sqlite;

    /// <summary>空白・記号・日本語を含む名前</summary>
    private const string NonAsciiName = "顧客 名前#1";

    /// <summary>表</summary>
    public static ScenarioTable<DialectIdentifierCase> Table { get; } =
        new(EngineIds.Dialect, AllRows().Select(row => row.ToScenario()));

    /// <summary>(行の Id, 方言) の組（Theory のデータ）</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var row in Table)
        {
            foreach (var dialect in row.Input.Expected.Keys.Order(StringComparer.Ordinal))
            {
                data.Add(row.Id, dialect);
            }
        }

        return data;
    }

    private static IEnumerable<DialectIdentifierRow> AllRows() =>
        [.. BoundaryRows(), .. SymmetryRows(), .. RuntimeRows(), .. SplitRows()];

    /// <summary>5 方言の <c>*IdentifierTests</c>（方言ごとの識別子の関数の境界）</summary>
    private static IEnumerable<DialectIdentifierRow> BoundaryRows() =>
        [
            DialectIdentifierRow
                .Of(
                    "quote-plain-wraps",
                    "Quote は単純な識別子を方言のクォートで囲む",
                    F.Quote,
                    S.Plain
                )
                .Expect(Ss, "Users", "[Users]")
                .Expect(Pg, "users", "\"users\"")
                .Expect(My, "users", "`users`")
                .Expect(Or, "users", "\"users\"")
                .Expect(Sl, "users", "\"users\""),
            DialectIdentifierRow
                .Of(
                    "quote-dot-splits-into-two-parts",
                    "Quote はスキーマ修飾名を 2 つに分けてそれぞれクォートする",
                    F.Quote,
                    S.Dot
                )
                .Expect(Ss, "dbo.Users", "[dbo].[Users]")
                .Expect(Pg, "public.users", "\"public\".\"users\"")
                .Expect(My, "mydb.users", "`mydb`.`users`")
                .Expect(Or, "myschema.users", "\"myschema\".\"users\"")
                .Expect(Sl, "dbo.users", "\"dbo\".\"users\""),
            DialectIdentifierRow
                .Of(
                    "quote-multi-dot-splits-on-first-dot",
                    "Quote は最初のドットのみで分割し、2 個目以降のドットはそのまま第 2 部に残る",
                    F.Quote,
                    S.MultiDot
                )
                .Expect(Ss, "a.b.c", "[a].[b.c]")
                .Expect(Pg, "a.b.c", "\"a\".\"b.c\"")
                .Expect(My, "a.b.c", "`a`.`b.c`")
                .Expect(Or, "a.b.c", "\"a\".\"b.c\"")
                .Expect(Sl, "a.b.c", "\"a\".\"b.c\""),
            DialectIdentifierRow
                .Of(
                    "quote-simple-plain-wraps",
                    "QuoteSimple は単一識別子をクォートする",
                    F.QuoteSimple,
                    S.Plain
                )
                .Expect(Ss, "Users", "[Users]")
                .Expect(Pg, "users", "\"users\"")
                .Expect(My, "users", "`users`")
                .Expect(Or, "users", "\"users\"")
                .Expect(Sl, "users", "\"users\""),
            DialectIdentifierRow
                .Of(
                    "quote-simple-dot-does-not-split",
                    "QuoteSimple はドットを含む名前も分割せず単一識別子としてクォートする",
                    F.QuoteSimple,
                    S.Dot
                )
                .Expect(Ss, "dbo.Users", "[dbo.Users]")
                .Expect(Pg, "public.users", "\"public.users\"")
                .Expect(My, "mydb.users", "`mydb.users`")
                .Expect(Or, "myschema.users", "\"myschema.users\"")
                .Expect(Sl, "dbo.users", "\"dbo.users\""),
            DialectIdentifierRow
                .Of(
                    "escape-closing-quote-is-doubled",
                    "Escape は識別子内の終端クォート文字を二重化する",
                    F.Escape,
                    S.ClosingQuote
                )
                .Expect(Ss, "User]Name", "User]]Name")
                .Expect(Pg, "User\"Name", "User\"\"Name")
                .Expect(My, "User`Name", "User``Name")
                .Expect(Or, "User\"Name", "User\"\"Name")
                .Expect(Sl, "User\"Name", "User\"\"Name"),
            DialectIdentifierRow
                .Of(
                    "escape-multiple-closing-quotes-are-each-doubled",
                    "Escape は連続する複数の終端クォート文字をすべて二重化する",
                    F.Escape,
                    S.ClosingQuote
                )
                .Expect(Ss, "]]a]", "]]]]a]]")
                .Expect(Pg, "\"\"a\"", "\"\"\"\"a\"\"")
                .Expect(My, "``a`", "````a``")
                .Expect(Or, "\"\"a\"", "\"\"\"\"a\"\"")
                .Expect(Sl, "\"\"a\"", "\"\"\"\"a\"\""),
            DialectIdentifierRow
                .Of(
                    "escape-opening-quote-is-not-escaped",
                    "Escape は [ をエスケープしない（] のみが閉じ括弧のため）",
                    F.Escape,
                    S.OpeningQuote
                )
                .Expect(Ss, "[Name", "[Name"),
            DialectIdentifierRow
                .Of(
                    "quote-closing-quote-escapes-before-wrapping",
                    "Quote は終端クォート文字を含む識別子をエスケープしてからクォートする",
                    F.Quote,
                    S.ClosingQuote
                )
                .Expect(Ss, "User]Name", "[User]]Name]")
                .Expect(Pg, "User\"Name", "\"User\"\"Name\"")
                .Expect(My, "User`Name", "`User``Name`")
                .Expect(Or, "User\"Name", "\"User\"\"Name\"")
                .Expect(Sl, "User\"Name", "\"User\"\"Name\""),
            DialectIdentifierRow
                .Of(
                    "quote-dot-closing-quote-escapes-each-part",
                    "Quote はスキーマ修飾名の各部を個別にエスケープする",
                    F.Quote,
                    S.DotClosingQuote
                )
                .Expect(Ss, "db]o.Us]ers", "[db]]o].[Us]]ers]")
                .Expect(Pg, "pub\"lic.us\"ers", "\"pub\"\"lic\".\"us\"\"ers\"")
                .Expect(My, "my`db.us`ers", "`my``db`.`us``ers`")
                .Expect(Or, "my\"schema.us\"ers", "\"my\"\"schema\".\"us\"\"ers\"")
                .Expect(Sl, "db\"o.us\"ers", "\"db\"\"o\".\"us\"\"ers\""),
            NonAsciiRow(
                "quote-non-ascii-passes-through",
                "Quote は空白・記号・日本語を含む識別子をそのまま通す",
                F.Quote,
                quoted: true
            ),
            NonAsciiRow(
                "quote-simple-non-ascii-passes-through",
                "QuoteSimple は空白・記号・日本語を含む識別子をそのまま通す",
                F.QuoteSimple,
                quoted: true
            ),
            NonAsciiRow(
                "escape-non-ascii-passes-through",
                "Escape は空白・記号・日本語を含む識別子をそのまま通す",
                F.Escape,
                quoted: false
            ),
            EmptyRow(
                "quote-empty-is-empty-quotes",
                "Quote は空文字に対して空のクォートを返す（例外にならない）",
                F.Quote,
                S.Empty,
                ""
            ),
            EmptyRow(
                "quote-null-is-empty-quotes",
                "Quote は null に対して空のクォートを返す（例外にならない）",
                F.Quote,
                S.Null,
                null
            ),
            EmptyRow(
                "quote-simple-empty-is-empty-quotes",
                "QuoteSimple は空文字に対して空のクォートを返す（例外にならない）",
                F.QuoteSimple,
                S.Empty,
                ""
            ),
            EmptyRow(
                "quote-simple-null-is-empty-quotes",
                "QuoteSimple は null に対して空のクォートを返す（例外にならない）",
                F.QuoteSimple,
                S.Null,
                null
            ),
            EmptyRow(
                "escape-empty-is-empty",
                "Escape は空文字に対して空文字を返す（例外にならない）",
                F.Escape,
                S.Empty,
                ""
            ),
            EmptyRow(
                "escape-null-is-empty",
                "Escape は null に対して空文字を返す（例外にならない）",
                F.Escape,
                S.Null,
                null
            ),
        ];

    /// <summary><c>IdentifierSymmetryTests</c>（5 方言へ同じ危険な文字を当てる）</summary>
    private static IEnumerable<DialectIdentifierRow> SymmetryRows() =>
        [
            DialectIdentifierRow
                .Of(
                    "quote-simple-closing-quote-is-doubled",
                    "5 方言とも識別子クォートは終端クォート文字を二重化する",
                    F.QuoteSimple,
                    S.ClosingQuote
                )
                .Expect(Ss, "a]b", "[a]]b]")
                .Expect(Pg, "a\"b", "\"a\"\"b\"")
                .Expect(My, "a`b", "`a``b`")
                .Expect(Or, "a\"b", "\"a\"\"b\"")
                .Expect(Sl, "a\"b", "\"a\"\"b\""),
            DialectIdentifierRow
                .Of(
                    "quote-simple-single-quote-is-kept",
                    "5 方言とも識別子クォートは ' を特別扱いしない（素の文字として残す）",
                    F.QuoteSimple,
                    S.SingleQuote
                )
                .Expect(Ss, "o'brien", "[o'brien]")
                .Expect(Pg, "o'brien", "\"o'brien\"")
                .Expect(My, "o'brien", "`o'brien`")
                .Expect(Or, "o'brien", "\"o'brien\"")
                .Expect(Sl, "o'brien", "\"o'brien\""),
            DialectIdentifierRow
                .Of(
                    "quote-simple-newline-is-kept",
                    "5 方言とも識別子クォートは改行をそのまま残す（畳むのはコメント側の責務）",
                    F.QuoteSimple,
                    S.NewLine
                )
                .Expect(Ss, "a\nb", "[a\nb]")
                .Expect(Pg, "a\nb", "\"a\nb\"")
                .Expect(My, "a\nb", "`a\nb`")
                .Expect(Or, "a\nb", "\"a\nb\"")
                .Expect(Sl, "a\nb", "\"a\nb\""),
            DialectIdentifierRow
                .Of(
                    "quote-dot-is-split-quoted",
                    "5 方言ともテーブル名は最初のドットで 2 分割してそれぞれクォートする",
                    F.Quote,
                    S.Dot
                )
                .Expect(Ss, "sales.orders", "[sales].[orders]")
                .Expect(Pg, "sales.orders", "\"sales\".\"orders\"")
                .Expect(My, "sales.orders", "`sales`.`orders`")
                .Expect(Or, "sales.orders", "\"sales\".\"orders\"")
                .Expect(Sl, "sales.orders", "\"sales\".\"orders\""),
            DialectIdentifierRow
                .Of(
                    "dynamic-single-quote-is-doubled",
                    "動的 SQL 用クォートは ' を二重化する（4 方言・SQLite は対象外）",
                    F.Dynamic,
                    S.SingleQuote
                )
                .Expect(Ss, "o'brien", "[o''brien]")
                .Expect(Pg, "o'brien", "\"o''brien\"")
                .Expect(My, "o'brien", "`o''brien`")
                .Expect(Or, "o'brien", "\"o''brien\""),
            DialectIdentifierRow
                .Of(
                    "dynamic-closing-quote-keeps-identifier-escaping",
                    "動的 SQL 用クォートは終端クォート文字の二重化も維持する（4 方言）",
                    F.Dynamic,
                    S.ClosingQuote
                )
                .Expect(Ss, "a]b", "[a]]b]")
                .Expect(Pg, "a\"b", "\"a\"\"b\"")
                .Expect(My, "a`b", "`a``b`")
                .Expect(Or, "a\"b", "\"a\"\"b\""),
            DialectIdentifierRow
                .Of(
                    "dynamic-backslash-is-doubled",
                    "MySQL の動的 SQL 用クォートはバックスラッシュも二重化する",
                    F.Dynamic,
                    S.Backslash
                )
                .Expect(My, "a\\b", "`a\\\\b`"),
        ];

    /// <summary>
    /// <c>SchemaQualifiedNameParityTests</c> の生成ランタイムの引用（分け方が食い違いやすい名前。期待は方言の識別子の関数と同じ値）
    /// </summary>
    private static IEnumerable<DialectIdentifierRow> RuntimeRows() =>
        [
            Runtime("plain", S.Plain, "orders", "[orders]", "\"orders\""),
            Runtime("dot", S.Dot, "sales.orders", "[sales].[orders]", "\"sales\".\"orders\""),
            Runtime("multi-dot", S.MultiDot, "a.b.c", "[a].[b.c]", "\"a\".\"b.c\""),
            Runtime("leading-dot", S.EmptyPart, ".a", "[].[a]", "\"\".\"a\""),
            Runtime("trailing-dot", S.EmptyPart, "a.", "[a].[]", "\"a\".\"\""),
            Runtime("two-dots", S.EmptyPart, "..", "[].[.]", "\"\".\".\""),
            Runtime("three-dots", S.EmptyPart, "...", "[].[..]", "\"\".\"..\""),
            Runtime("empty", S.Empty, "", "[]", "\"\""),
            Runtime(
                "brackets-in-parts",
                S.QuoteCharsInParts,
                "x]y.z]w",
                "[x]]y].[z]]w]",
                "\"x]y\".\"z]w\""
            ),
            Runtime(
                "double-quotes-in-parts",
                S.QuoteCharsInParts,
                "x\"y.z\"w",
                "[x\"y].[z\"w]",
                "\"x\"\"y\".\"z\"\"w\""
            ),
        ];

    /// <summary><c>SchemaQualifiedNameParityTests.Split_UsesTheFirstDot</c>（方言なし）</summary>
    private static IEnumerable<DialectIdentifierRow> SplitRows() =>
        [
            Split("plain", S.Plain, "orders", null, "orders"),
            Split("dot", S.Dot, "sales.orders", "sales", "orders"),
            Split("multi-dot", S.MultiDot, "a.b.c", "a", "b.c"),
            Split("leading-dot", S.EmptyPart, ".a", "", "a"),
            Split("trailing-dot", S.EmptyPart, "a.", "a", ""),
            Split("two-dots", S.EmptyPart, "..", "", "."),
            Split("empty", S.Empty, "", null, ""),
        ];

    private static DialectIdentifierRow NonAsciiRow(
        string id,
        string description,
        string function,
        bool quoted
    ) =>
        DialectIdentifierRow
            .Of(id, description, function, S.NonAscii)
            .Expect(Ss, NonAsciiName, quoted ? $"[{NonAsciiName}]" : NonAsciiName)
            .Expect(Pg, NonAsciiName, quoted ? $"\"{NonAsciiName}\"" : NonAsciiName)
            .Expect(My, NonAsciiName, quoted ? $"`{NonAsciiName}`" : NonAsciiName)
            .Expect(Or, NonAsciiName, quoted ? $"\"{NonAsciiName}\"" : NonAsciiName)
            .Expect(Sl, NonAsciiName, quoted ? $"\"{NonAsciiName}\"" : NonAsciiName);

    private static DialectIdentifierRow EmptyRow(
        string id,
        string description,
        string function,
        string shape,
        string? input
    )
    {
        var quoted = function != F.Escape;

        return DialectIdentifierRow
            .Of(id, description, function, shape)
            .Expect(Ss, input, quoted ? "[]" : "")
            .Expect(Pg, input, quoted ? "\"\"" : "")
            .Expect(My, input, quoted ? "``" : "")
            .Expect(Or, input, quoted ? "\"\"" : "")
            .Expect(Sl, input, quoted ? "\"\"" : "");
    }

    private static DialectIdentifierRow Runtime(
        string name,
        string shape,
        string input,
        string sqlServer,
        string sqlite
    ) =>
        DialectIdentifierRow
            .Of(
                $"runtime-quote-{name}",
                "生成ランタイムは DDL と同じ分け方でテーブル名を引用する",
                F.RuntimeQuote,
                shape
            )
            .Expect(Ss, input, sqlServer)
            .Expect(Sl, input, sqlite);

    private static DialectIdentifierRow Split(
        string name,
        string shape,
        string input,
        string? schema,
        string table
    ) =>
        DialectIdentifierRow
            .Of(
                $"split-{name}",
                "Split は最初の . で分け、2 つ目以降の . と空の部分はそのまま残す",
                F.Split,
                shape
            )
            .Expect(DialectDdlAxis.None, input, DialectIdentifierRow.SplitResult(schema, table));
}
