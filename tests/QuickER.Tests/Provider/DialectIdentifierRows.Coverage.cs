using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using F = QuickER.Tests.Provider.DialectIdentifierFunctions;
using S = QuickER.Tests.Provider.DialectIdentifierShapes;

namespace QuickER.Tests.Provider;

/// <summary>方言の識別子の表の行のうち、関数ごとの入力の形の網羅（網羅しない組を埋める行）</summary>
internal static partial class DialectIdentifierRows
{
    /// <summary>特性の固定の説明の先頭（文書に定めが無い今の振る舞い）</summary>
    private const string Characterization = "特性の固定（文書に定めが無い今の振る舞い）：";

    private static IEnumerable<DialectIdentifierRow> CoverageRows() =>
        [
            .. QuoteCoverageRows(),
            .. QuoteSimpleCoverageRows(),
            .. EscapeCoverageRows(),
            .. DynamicCoverageRows(),
            .. RuntimeCoverageRows(),
            .. SplitCoverageRows(),
        ];

    private static IEnumerable<DialectIdentifierRow> QuoteCoverageRows() =>
        [
            Five(
                "quote-leading-dot-quotes-empty-schema",
                "Quote は先頭の . の前の空の部分も空のクォートにして、残りを表の部分としてクォートする",
                F.Quote,
                S.EmptyPart,
                ".a",
                "[].[a]",
                "\"\".\"a\"",
                "``.`a`"
            ),
            Five(
                "quote-trailing-dot-quotes-empty-table",
                "Quote は末尾の . の後の空の部分も空のクォートにする",
                F.Quote,
                S.EmptyPart,
                "a.",
                "[a].[]",
                "\"a\".\"\"",
                "`a`.``"
            ),
            DialectIdentifierRow
                .Of(
                    "quote-opening-quote-is-not-escaped",
                    "Quote は [ をエスケープしない（] のみが終端のため）",
                    F.Quote,
                    S.OpeningQuote
                )
                .Expect(Ss, "a[b", "[a[b]"),
            Five(
                "quote-single-quote-is-kept",
                "Quote は ' を特別扱いせず素の文字として残す",
                F.Quote,
                S.SingleQuote,
                "sales.o'brien",
                "[sales].[o'brien]",
                "\"sales\".\"o'brien\"",
                "`sales`.`o'brien`"
            ),
            Five(
                "quote-newline-is-kept",
                "Quote は両方の部分の改行をそのまま残す（畳むのはコメント側の責務）",
                F.Quote,
                S.NewLine,
                "s\na.t\nb",
                "[s\na].[t\nb]",
                "\"s\na\".\"t\nb\"",
                "`s\na`.`t\nb`"
            ),
            Five(
                "quote-backslash-is-kept",
                "Quote は \\ を二重化せずそのまま残す（MySQL でも識別子のクォートはバックスラッシュを扱わない）",
                F.Quote,
                S.Backslash,
                "a\\b.c\\d",
                "[a\\b].[c\\d]",
                "\"a\\b\".\"c\\d\"",
                "`a\\b`.`c\\d`"
            ),
            Five(
                "quote-quote-chars-in-parts-escape-only-own-closing-quote",
                "Quote は各部分の、その方言の終端クォート文字だけを二重化する（] と \" の両方を含む名前）",
                F.Quote,
                S.QuoteCharsInParts,
                "x]y.z\"w",
                "[x]]y].[z\"w]",
                "\"x]y\".\"z\"\"w\"",
                "`x]y`.`z\"w`"
            ),
        ];

    private static IEnumerable<DialectIdentifierRow> QuoteSimpleCoverageRows() =>
        [
            Five(
                "quote-simple-multi-dot-does-not-split",
                "QuoteSimple はドットが複数あっても分割せず 1 つの識別子としてクォートする",
                F.QuoteSimple,
                S.MultiDot,
                "a.b.c",
                "[a.b.c]",
                "\"a.b.c\"",
                "`a.b.c`"
            ),
            Five(
                "quote-simple-empty-part-does-not-split",
                "QuoteSimple は先頭の . も分割せず 1 つの識別子としてクォートする",
                F.QuoteSimple,
                S.EmptyPart,
                ".a",
                "[.a]",
                "\".a\"",
                "`.a`"
            ),
            DialectIdentifierRow
                .Of(
                    "quote-simple-dot-closing-quote-escapes-without-split",
                    "QuoteSimple は . を挟んだ複数の終端クォート文字を、分割せずすべて二重化する",
                    F.QuoteSimple,
                    S.DotClosingQuote
                )
                .Expect(Ss, "db]o.us]ers", "[db]]o.us]]ers]")
                .Expect(Pg, "pub\"lic.us\"ers", "\"pub\"\"lic.us\"\"ers\"")
                .Expect(My, "my`db.us`ers", "`my``db.us``ers`")
                .Expect(Or, "my\"schema.us\"ers", "\"my\"\"schema.us\"\"ers\"")
                .Expect(Sl, "db\"o.us\"ers", "\"db\"\"o.us\"\"ers\""),
            DialectIdentifierRow
                .Of(
                    "quote-simple-opening-quote-is-not-escaped",
                    "QuoteSimple は [ をエスケープしない（] のみが終端のため）",
                    F.QuoteSimple,
                    S.OpeningQuote
                )
                .Expect(Ss, "a[b", "[a[b]"),
            Five(
                "quote-simple-backslash-is-kept",
                "QuoteSimple は \\ を二重化せずそのまま残す（MySQL でも識別子のクォートはバックスラッシュを扱わない）",
                F.QuoteSimple,
                S.Backslash,
                "a\\b",
                "[a\\b]",
                "\"a\\b\"",
                "`a\\b`"
            ),
            Five(
                "quote-simple-quote-chars-in-parts-escape-only-own-closing-quote",
                "QuoteSimple はその方言の終端クォート文字だけを二重化する（] と \" の両方を含む名前）",
                F.QuoteSimple,
                S.QuoteCharsInParts,
                "x]y.z\"w",
                "[x]]y.z\"w]",
                "\"x]y.z\"\"w\"",
                "`x]y.z\"w`"
            ),
        ];

    private static IEnumerable<DialectIdentifierRow> EscapeCoverageRows() =>
        [
            Five(
                "escape-plain-is-unchanged",
                "Escape は特別な文字の無い名前をそのまま返す",
                F.Escape,
                S.Plain,
                "users",
                "users",
                "users",
                "users"
            ),
            Five(
                "escape-dot-is-not-split",
                "Escape はクォートも分割もしないので . を含む名前もそのまま返す",
                F.Escape,
                S.Dot,
                "a.b",
                "a.b",
                "a.b",
                "a.b"
            ),
            Five(
                "escape-multi-dot-is-unchanged",
                "Escape は . が複数あってもそのまま返す",
                F.Escape,
                S.MultiDot,
                "a.b.c",
                "a.b.c",
                "a.b.c",
                "a.b.c"
            ),
            Five(
                "escape-empty-part-is-unchanged",
                "Escape は先頭の . もそのまま返す",
                F.Escape,
                S.EmptyPart,
                ".a",
                ".a",
                ".a",
                ".a"
            ),
            DialectIdentifierRow
                .Of(
                    "escape-dot-closing-quote-doubles-each",
                    "Escape は . を挟んだ複数の終端クォート文字を、分割せずすべて二重化する",
                    F.Escape,
                    S.DotClosingQuote
                )
                .Expect(Ss, "db]o.us]ers", "db]]o.us]]ers")
                .Expect(Pg, "pub\"lic.us\"ers", "pub\"\"lic.us\"\"ers")
                .Expect(My, "my`db.us`ers", "my``db.us``ers")
                .Expect(Or, "my\"schema.us\"ers", "my\"\"schema.us\"\"ers")
                .Expect(Sl, "db\"o.us\"ers", "db\"\"o.us\"\"ers"),
            Five(
                "escape-single-quote-is-kept",
                "Escape は ' を二重化しない（文字列リテラル用のエスケープは別の関数）",
                F.Escape,
                S.SingleQuote,
                "o'brien",
                "o'brien",
                "o'brien",
                "o'brien"
            ),
            Five(
                "escape-newline-is-kept",
                "Escape は改行をそのまま残す",
                F.Escape,
                S.NewLine,
                "a\nb",
                "a\nb",
                "a\nb",
                "a\nb"
            ),
            Five(
                "escape-backslash-is-kept",
                "Escape は \\ を二重化せずそのまま残す",
                F.Escape,
                S.Backslash,
                "a\\b",
                "a\\b",
                "a\\b",
                "a\\b"
            ),
            Five(
                "escape-quote-chars-in-parts-escape-only-own-closing-quote",
                "Escape はその方言の終端クォート文字だけを二重化する（] と \" の両方を含む名前）",
                F.Escape,
                S.QuoteCharsInParts,
                "x]y.z\"w",
                "x]]y.z\"w",
                "x]y.z\"\"w",
                "x]y.z\"w"
            ),
        ];

    // 動的 SQL 用のクォートは 4 方言（SQLite は組み立てる経路が無い）。Pg と Oracle は同じ二重引用符
    private static IEnumerable<DialectIdentifierRow> DynamicCoverageRows() =>
        [
            Four(
                "dynamic-plain-wraps",
                "動的 SQL 用クォートは単純な名前を方言のクォートで囲む",
                S.Plain,
                "users",
                "[users]",
                "\"users\"",
                "`users`"
            ),
            Four(
                "dynamic-dot-splits-into-two-parts",
                "動的 SQL 用クォートはテーブル名を最初の . で分けてそれぞれクォートする",
                S.Dot,
                "sales.orders",
                "[sales].[orders]",
                "\"sales\".\"orders\"",
                "`sales`.`orders`"
            ),
            Four(
                "dynamic-multi-dot-splits-on-first-dot",
                "動的 SQL 用クォートは最初の . だけで分け、2 つ目以降の . は表の部分に残す",
                S.MultiDot,
                "a.b.c",
                "[a].[b.c]",
                "\"a\".\"b.c\"",
                "`a`.`b.c`"
            ),
            Four(
                "dynamic-empty-part-quotes-empty-parts",
                "動的 SQL 用クォートは先頭の . の前の空の部分も空のクォートにする",
                S.EmptyPart,
                ".a",
                "[].[a]",
                "\"\".\"a\"",
                "``.`a`"
            ),
            DialectIdentifierRow
                .Of(
                    "dynamic-dot-closing-quote-escapes-each-part",
                    "動的 SQL 用クォートは各部分の終端クォート文字を二重化する（リテラルのエスケープは影響しない）",
                    F.Dynamic,
                    S.DotClosingQuote
                )
                .Expect(Ss, "db]o.us]ers", "[db]]o].[us]]ers]")
                .Expect(Pg, "pub\"lic.us\"ers", "\"pub\"\"lic\".\"us\"\"ers\"")
                .Expect(My, "my`db.us`ers", "`my``db`.`us``ers`")
                .Expect(Or, "my\"schema.us\"ers", "\"my\"\"schema\".\"us\"\"ers\""),
            DialectIdentifierRow
                .Of(
                    "dynamic-opening-quote-is-not-escaped",
                    "動的 SQL 用クォートは [ をエスケープしない（] のみが終端のため）",
                    F.Dynamic,
                    S.OpeningQuote
                )
                .Expect(Ss, "a[b", "[a[b]"),
            Four(
                "dynamic-newline-is-kept",
                "動的 SQL 用クォートは改行をそのまま残す",
                S.NewLine,
                "a\nb",
                "[a\nb]",
                "\"a\nb\"",
                "`a\nb`"
            ),
            DialectIdentifierRow
                .Of(
                    "dynamic-non-ascii-passes-through",
                    "動的 SQL 用クォートは空白・記号・日本語を含む識別子をそのまま通す",
                    F.Dynamic,
                    S.NonAscii
                )
                .Expect(Ss, NonAsciiName, $"[{NonAsciiName}]")
                .Expect(Pg, NonAsciiName, $"\"{NonAsciiName}\"")
                .Expect(My, NonAsciiName, $"`{NonAsciiName}`")
                .Expect(Or, NonAsciiName, $"\"{NonAsciiName}\""),
            // 根拠：4 方言とも Quote が空文字・null を空のクォートで返し（SqlIdentifier.cs:16-19・PgIdentifier.cs:20-23・
            // MySqlIdentifier.cs:21-24・OracleIdentifier.cs:22-25）、リテラルのエスケープは null を空文字として扱う
            Four(
                "dynamic-empty-is-empty-quotes",
                Characterization
                    + "動的 SQL 用クォートは空文字に対して空のクォートを返す（例外にならない）",
                S.Empty,
                "",
                "[]",
                "\"\"",
                "``"
            ),
            Four(
                "dynamic-null-is-empty-quotes",
                Characterization
                    + "動的 SQL 用クォートは null に対して空のクォートを返す（例外にならない）",
                S.Null,
                null,
                "[]",
                "\"\"",
                "``"
            ),
            Four(
                "dynamic-quote-chars-in-parts-escape-only-own-closing-quote",
                "動的 SQL 用クォートはその方言の終端クォート文字だけを二重化する（] と \" の両方を含む名前）",
                S.QuoteCharsInParts,
                "x]y.z\"w",
                "[x]]y].[z\"w]",
                "\"x]y\".\"z\"\"w\"",
                "`x]y`.`z\"w`"
            ),
            DialectIdentifierRow
                .Of(
                    "dynamic-backslash-is-kept-outside-mysql",
                    "動的 SQL 用クォートは MySQL 以外では \\ を二重化しない（リテラルのエスケープは ' だけ）",
                    F.Dynamic,
                    S.Backslash
                )
                .Expect(Ss, "a\\b", "[a\\b]")
                .Expect(Pg, "a\\b", "\"a\\b\"")
                .Expect(Or, "a\\b", "\"a\\b\""),
        ];

    // 生成ランタイムの引用は SQL Server と SQLite だけ（期待は同じ入力の方言の関数の値と同じ）
    private static IEnumerable<DialectIdentifierRow> RuntimeCoverageRows() =>
        [
            DialectIdentifierRow
                .Of(
                    "runtime-quote-closing-quote-is-doubled",
                    "生成ランタイムは識別子の終端クォート文字を二重化する",
                    F.RuntimeQuote,
                    S.ClosingQuote
                )
                .Expect(Ss, "a]b", "[a]]b]")
                .Expect(Sl, "a\"b", "\"a\"\"b\""),
            DialectIdentifierRow
                .Of(
                    "runtime-quote-dot-closing-quote-escapes-each-part",
                    "生成ランタイムは分けた各部分の終端クォート文字を個別に二重化する",
                    F.RuntimeQuote,
                    S.DotClosingQuote
                )
                .Expect(Ss, "db]o.us]ers", "[db]]o].[us]]ers]")
                .Expect(Sl, "db\"o.us\"ers", "\"db\"\"o\".\"us\"\"ers\""),
            DialectIdentifierRow
                .Of(
                    "runtime-quote-opening-quote-is-not-escaped",
                    "生成ランタイムは [ をエスケープしない（] のみが終端のため）",
                    F.RuntimeQuote,
                    S.OpeningQuote
                )
                .Expect(Ss, "a[b", "[a[b]"),
            RuntimeBoth(
                "runtime-quote-single-quote-is-kept",
                "生成ランタイムは ' を特別扱いせず素の文字として残す",
                S.SingleQuote,
                "sales.o'brien",
                "[sales].[o'brien]",
                "\"sales\".\"o'brien\""
            ),
            RuntimeBoth(
                "runtime-quote-newline-is-kept",
                "生成ランタイムは両方の部分の改行をそのまま残す",
                S.NewLine,
                "s\na.t\nb",
                "[s\na].[t\nb]",
                "\"s\na\".\"t\nb\""
            ),
            RuntimeBoth(
                "runtime-quote-non-ascii-passes-through",
                "生成ランタイムは空白・記号・日本語を含む識別子をそのまま通す",
                S.NonAscii,
                NonAsciiName,
                $"[{NonAsciiName}]",
                $"\"{NonAsciiName}\""
            ),
            // runtime-quote × null は行にしない（DialectIdentifierDimensions.Gaps の受け入れ。例外の型は定めていない）
            RuntimeBoth(
                "runtime-quote-backslash-is-kept",
                "生成ランタイムは \\ を二重化せずそのまま残す",
                S.Backslash,
                "a\\b.c\\d",
                "[a\\b].[c\\d]",
                "\"a\\b\".\"c\\d\""
            ),
        ];

    // 分け方は方言に依らず、最初の . で分ける（空の部分もそのまま返す）
    private static IEnumerable<DialectIdentifierRow> SplitCoverageRows() =>
        [
            SplitCase(
                "split-single-quote-is-kept",
                "Split は ' を特別扱いせず最初の . で分ける",
                S.SingleQuote,
                "o'a.o'b",
                "o'a",
                "o'b"
            ),
            SplitCase(
                "split-newline-is-kept",
                "Split は改行を含む部分もそのまま最初の . で分ける",
                S.NewLine,
                "s\na.t\nb",
                "s\na",
                "t\nb"
            ),
            SplitCase(
                "split-non-ascii-passes-through",
                "Split は空白・記号・日本語を含む名前もそのまま最初の . で分ける",
                S.NonAscii,
                "顧客.注文 名前#1",
                "顧客",
                "注文 名前#1"
            ),
            // 根拠：Split・IsQualified・TrySplit は入口で ArgumentNullException.ThrowIfNull(tableName) を呼ぶ
            // （src/QuickER.Dialects/SchemaQualifiedName.cs:17-23）。実行部は最初の Split で落ちる
            DialectIdentifierRow
                .Of(
                    "split-null-throws",
                    Characterization + "Split は null を受けると ArgumentNullException になる",
                    F.Split,
                    S.Null
                )
                .Expect(DialectDdlAxis.None, null, "throws ArgumentNullException"),
            SplitCase(
                "split-backslash-is-kept",
                "Split は \\ を含む部分もそのまま最初の . で分ける",
                S.Backslash,
                "a\\b.c\\d",
                "a\\b",
                "c\\d"
            ),
            SplitCase(
                "split-quote-chars-in-parts-are-kept",
                "Split は ] と \" を含む部分もそのまま最初の . で分ける",
                S.QuoteCharsInParts,
                "x]y.z\"w",
                "x]y",
                "z\"w"
            ),
        ];

    /// <summary>5 方言へ同じ入力を当てる行（Pg・Oracle・SQLite は同じ二重引用符の期待）</summary>
    private static DialectIdentifierRow Five(
        string id,
        string description,
        string function,
        string shape,
        string input,
        string bracket,
        string doubleQuote,
        string backtick
    ) =>
        DialectIdentifierRow
            .Of(id, description, function, shape)
            .Expect(Ss, input, bracket)
            .Expect(Pg, input, doubleQuote)
            .Expect(My, input, backtick)
            .Expect(Or, input, doubleQuote)
            .Expect(Sl, input, doubleQuote);

    /// <summary>動的 SQL 用クォートを持つ 4 方言へ同じ入力を当てる行（SQLite は持たない）</summary>
    private static DialectIdentifierRow Four(
        string id,
        string description,
        string shape,
        string? input,
        string bracket,
        string doubleQuote,
        string backtick
    ) =>
        DialectIdentifierRow
            .Of(id, description, F.Dynamic, shape)
            .Expect(Ss, input, bracket)
            .Expect(Pg, input, doubleQuote)
            .Expect(My, input, backtick)
            .Expect(Or, input, doubleQuote);

    /// <summary>生成ランタイムを持つ 2 方言へ同じ入力を当てる行</summary>
    private static DialectIdentifierRow RuntimeBoth(
        string id,
        string description,
        string shape,
        string input,
        string sqlServer,
        string sqlite
    ) =>
        DialectIdentifierRow
            .Of(id, description, F.RuntimeQuote, shape)
            .Expect(Ss, input, sqlServer)
            .Expect(Sl, input, sqlite);

    /// <summary>分け方の行（方言なし）</summary>
    private static DialectIdentifierRow SplitCase(
        string id,
        string description,
        string shape,
        string input,
        string schema,
        string table
    ) =>
        DialectIdentifierRow
            .Of(id, description, F.Split, shape)
            .Expect(DialectDdlAxis.None, input, DialectIdentifierRow.SplitResult(schema, table));
}
