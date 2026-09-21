using AwesomeAssertions;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 5 方言の識別子ユーティリティ（<c>SqlIdentifier</c> / <c>PgIdentifier</c> / <c>MySqlIdentifier</c> /
/// <c>OracleIdentifier</c> / <c>SqliteIdentifier</c>）が、<b>同一のケース集合</b>に対して同じ振る舞いを示すことを固定する。
/// </summary>
/// <remarks>
/// <para>
/// プロバイダの対称性（5 実装が同じ構造を持つ）は設計上の約束だが、方言ごとに別ファイルの別テストを書いている限り
/// 「1 方言だけケースが足りない」に気付けない。ここでは「危険な文字を含む名前」という同じ集合を 5 方言へ一度に当て、
/// 対称性そのものをテストへ翻訳する（方言別の個別境界は各 <c>*IdentifierTests</c> が担当する）。
/// </para>
/// <para>
/// 固定する規則は 3 つ。(1) 識別子クォートは終端クォート文字だけを二重化する。(2) <c>'</c> は識別子クォートでは
/// 特別扱いしない（クォートの中では素の文字）。(3) 動的 SQL の文字列リテラルへ埋めるときだけ <c>'</c> も二重化する
/// （<c>QuoteForDynamicSql</c>）。SQLite だけは動的 SQL を組み立てる経路が無いためこのヘルパーを持たない。
/// </para>
/// </remarks>
public class IdentifierSymmetryTests
{
    /// <summary>1 方言分の識別子ユーティリティを関数として束ねたもの</summary>
    /// <param name="Dialect">方言名（失敗メッセージ用）</param>
    /// <param name="Open">クォート開始文字</param>
    /// <param name="Close">クォート終了文字（＝二重化の対象）</param>
    /// <param name="Quote">テーブル名のクォート（ドット分割あり）</param>
    /// <param name="QuoteSimple">単一識別子のクォート</param>
    /// <param name="QuoteForDynamicSql">動的 SQL のリテラル内へ埋めるクォート（SQLite は null）</param>
    private sealed record DialectIdentifier(
        string Dialect,
        string Open,
        string Close,
        Func<string, string> Quote,
        Func<string, string> QuoteSimple,
        Func<string, string>? QuoteForDynamicSql
    );

    /// <summary>5 方言の識別子ユーティリティ（SQLite のみ動的 SQL ヘルパーを持たない）</summary>
    private static readonly DialectIdentifier[] Dialects =
    [
        new(
            "SqlServer",
            "[",
            "]",
            SqlIdentifier.Bracket,
            SqlIdentifier.BracketSimple,
            SqlIdentifier.QuoteForDynamicSql
        ),
        new(
            "PostgreSql",
            "\"",
            "\"",
            PgIdentifier.Quote,
            PgIdentifier.QuoteSimple,
            PgIdentifier.QuoteForDynamicSql
        ),
        new(
            "MySql",
            "`",
            "`",
            MySqlIdentifier.Quote,
            MySqlIdentifier.QuoteSimple,
            MySqlIdentifier.QuoteForDynamicSql
        ),
        new(
            "Oracle",
            "\"",
            "\"",
            OracleIdentifier.Quote,
            OracleIdentifier.QuoteSimple,
            OracleIdentifier.QuoteForDynamicSql
        ),
        new("Sqlite", "\"", "\"", SqliteIdentifier.Quote, SqliteIdentifier.QuoteSimple, null),
    ];

    [Fact(DisplayName = "5 方言とも識別子クォートは終端クォート文字を二重化する")]
    public void QuoteSimple_ClosingQuoteInName_IsDoubled()
    {
        foreach (var dialect in Dialects)
        {
            var name = "a" + dialect.Close + "b";

            dialect
                .QuoteSimple(name)
                .Should()
                .Be(
                    dialect.Open + "a" + dialect.Close + dialect.Close + "b" + dialect.Close,
                    $"{dialect.Dialect} は終端クォート文字を二重化してクォートの脱出を防ぐべき"
                );
        }
    }

    [Fact(DisplayName = "5 方言とも識別子クォートは ' を特別扱いしない（素の文字として残す）")]
    public void QuoteSimple_SingleQuoteInName_IsKeptVerbatim()
    {
        foreach (var dialect in Dialects)
        {
            dialect
                .QuoteSimple("o'brien")
                .Should()
                .Be(
                    dialect.Open + "o'brien" + dialect.Close,
                    $"{dialect.Dialect} の識別子クォートの中では ' は素の文字"
                );
        }
    }

    [Fact(DisplayName = "5 方言とも識別子クォートは改行をそのまま残す（畳むのはコメント側の責務）")]
    public void QuoteSimple_NewLineInName_IsKeptVerbatim()
    {
        foreach (var dialect in Dialects)
        {
            dialect
                .QuoteSimple("a\nb")
                .Should()
                .Be(
                    dialect.Open + "a\nb" + dialect.Close,
                    $"{dialect.Dialect} の識別子クォートは改行を畳まない（C# 生成前診断と SqlComment が担当する）"
                );
        }
    }

    [Fact(DisplayName = "5 方言ともテーブル名は最初のドットで 2 分割してそれぞれクォートする")]
    public void Quote_SchemaQualifiedName_IsSplitQuoted()
    {
        foreach (var dialect in Dialects)
        {
            dialect
                .Quote("sales.orders")
                .Should()
                .Be(
                    dialect.Open
                        + "sales"
                        + dialect.Close
                        + "."
                        + dialect.Open
                        + "orders"
                        + dialect.Close,
                    $"{dialect.Dialect} はスキーマ修飾名を分割クォートする（5 方言対称の割り切り）"
                );
        }
    }

    [Fact(DisplayName = "動的 SQL 用クォートは ' を二重化する（4 方言・SQLite は対象外）")]
    public void QuoteForDynamicSql_SingleQuoteInName_IsDoubled()
    {
        foreach (var dialect in Dialects.Where(d => d.QuoteForDynamicSql is not null))
        {
            dialect
                .QuoteForDynamicSql!("o'brien")
                .Should()
                .Be(
                    dialect.Open + "o''brien" + dialect.Close,
                    $"{dialect.Dialect} は動的 SQL のリテラル内で ' を二重化してリテラルの脱出を防ぐべき"
                );
        }
    }

    [Fact(DisplayName = "動的 SQL 用クォートは終端クォート文字の二重化も維持する（4 方言）")]
    public void QuoteForDynamicSql_KeepsIdentifierEscaping()
    {
        foreach (var dialect in Dialects.Where(d => d.QuoteForDynamicSql is not null))
        {
            var name = "a" + dialect.Close + "b";

            dialect
                .QuoteForDynamicSql!(name)
                .Should()
                .Be(
                    dialect.Open + "a" + dialect.Close + dialect.Close + "b" + dialect.Close,
                    $"{dialect.Dialect} は識別子エスケープを保ったままリテラルエスケープを重ねるべき"
                );
        }
    }

    [Fact(DisplayName = "MySQL の動的 SQL 用クォートはバックスラッシュも二重化する")]
    public void QuoteForDynamicSql_MySqlEscapesBackslash() =>
        // MySQL は既定でバックスラッシュを文字列のエスケープ文字として解釈する
        MySqlIdentifier.QuoteForDynamicSql("a\\b").Should().Be("`a\\\\b`");
}
