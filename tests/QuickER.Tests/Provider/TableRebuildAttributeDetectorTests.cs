using AwesomeAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// テーブル再構築で失われる列レベル属性の検出（<see cref="TableRebuildAttributeDetector"/>）を検証する。
/// </summary>
/// <remarks>
/// 意味モデルが持たない属性（<c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> / 生成列）は
/// 再構築で黙って消える。検出漏れは「消えたことに気づかない」に直結するため、種別ごとに固定する。
/// </remarks>
public class TableRebuildAttributeDetectorTests
{
    /// <summary>AUTOINCREMENT を検出する</summary>
    [Fact(DisplayName = "AUTOINCREMENT を検出する")]
    public void Detects_AutoIncrement()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)")
            .Should()
            .Equal("AUTOINCREMENT");
    }

    /// <summary>DEFAULT を検出する</summary>
    [Fact(DisplayName = "DEFAULT を検出する")]
    public void Detects_Default()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (id INTEGER PRIMARY KEY, qty INTEGER NOT NULL DEFAULT 0)")
            .Should()
            .Equal("DEFAULT");
    }

    /// <summary>CHECK を検出する</summary>
    [Fact(DisplayName = "CHECK を検出する")]
    public void Detects_Check()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (id INTEGER PRIMARY KEY, qty INTEGER CHECK (qty > 0))")
            .Should()
            .Equal("CHECK");
    }

    /// <summary>COLLATE を検出する</summary>
    [Fact(DisplayName = "COLLATE を検出する")]
    public void Detects_Collate()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (id INTEGER PRIMARY KEY, code TEXT COLLATE NOCASE)")
            .Should()
            .Equal("COLLATE");
    }

    /// <summary>GENERATED ALWAYS の生成列を検出する</summary>
    [Fact(DisplayName = "GENERATED ALWAYS の生成列を検出する")]
    public void Detects_GeneratedColumn()
    {
        TableRebuildAttributeDetector
            .Detect(
                "CREATE TABLE t (id INTEGER PRIMARY KEY, qty INTEGER, "
                    + "total INTEGER GENERATED ALWAYS AS (qty * 2) VIRTUAL)"
            )
            .Should()
            .Equal("GENERATED");
    }

    /// <summary>GENERATED を省いた生成列（<c>col AS (expr)</c>）も同じトークンで検出する</summary>
    [Fact(DisplayName = "省略形の生成列（AS (...)）も GENERATED として検出する")]
    public void Detects_ShorthandGeneratedColumn()
    {
        TableRebuildAttributeDetector
            .Detect(
                "CREATE TABLE t (id INTEGER PRIMARY KEY, qty INTEGER, total INTEGER AS (qty * 2))"
            )
            .Should()
            .Equal("GENERATED");
    }

    /// <summary>複数種別は出現順（キーワード定義順）で重複なく並ぶ</summary>
    [Fact(DisplayName = "複数の属性をまとめて検出する")]
    public void Detects_MultipleAttributes()
    {
        TableRebuildAttributeDetector
            .Detect(
                "CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, "
                    + "qty INTEGER NOT NULL DEFAULT 0 CHECK (qty >= 0), "
                    + "code TEXT COLLATE NOCASE)"
            )
            .Should()
            .Equal("AUTOINCREMENT", "DEFAULT", "CHECK", "COLLATE");
    }

    /// <summary>属性を持たない素のテーブルでは検出しない（＝警告も出ない）</summary>
    [Fact(DisplayName = "属性のないテーブルでは検出しない")]
    public void Detects_NothingForPlainTable()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (id INTEGER, name TEXT NOT NULL, PRIMARY KEY(id))")
            .Should()
            .BeEmpty();
    }

    /// <summary>大文字小文字は区別しない（SQLite は原文を保持するため小文字の DDL もある）</summary>
    [Fact(DisplayName = "小文字の DDL でも検出する")]
    public void Detects_CaseInsensitively()
    {
        TableRebuildAttributeDetector
            .Detect("create table t (id integer primary key autoincrement)")
            .Should()
            .Equal("AUTOINCREMENT");
    }

    /// <summary>テーブル名・列名に含まれるキーワードは誤検出しない（最外括弧内かつ引用識別子を除外して見る）</summary>
    [Fact(DisplayName = "テーブル名・引用識別子中のキーワードは誤検出しない")]
    public void DoesNotDetect_KeywordsInsideIdentifiers()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE \"default_check\" (\"collate\" TEXT, [check] INTEGER)")
            .Should()
            .BeEmpty();
    }

    /// <summary>文字列リテラル・コメント中のキーワードは誤検出しない</summary>
    [Fact(DisplayName = "文字列リテラル・コメント中のキーワードは誤検出しない")]
    public void DoesNotDetect_KeywordsInsideLiteralsOrComments()
    {
        TableRebuildAttributeDetector
            .Detect(
                "CREATE TABLE t (\n"
                    + "  id INTEGER, -- DEFAULT is not applied here\n"
                    + "  name TEXT, /* CHECK comment */\n"
                    + "  note TEXT\n"
                    + ")"
            )
            .Should()
            .BeEmpty();
    }

    /// <summary>語の一部（<c>defaults</c> 等）はキーワードとして拾わない</summary>
    [Fact(DisplayName = "語の一部は検出しない（語境界で判定する）")]
    public void DoesNotDetect_PartialWords()
    {
        TableRebuildAttributeDetector
            .Detect("CREATE TABLE t (defaults INTEGER, checked INTEGER, collated TEXT)")
            .Should()
            .BeEmpty();
    }

    /// <summary>DDL が無い（取得できなかった）場合は検出なし</summary>
    [Fact(DisplayName = "DDL が無ければ検出しない")]
    public void DoesNotDetect_WhenSqlIsMissing()
    {
        TableRebuildAttributeDetector.Detect(null).Should().BeEmpty();
        TableRebuildAttributeDetector.Detect("   ").Should().BeEmpty();
    }
}
