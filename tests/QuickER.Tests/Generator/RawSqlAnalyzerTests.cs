using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.CSharp.Queries;

namespace QuickER.Tests.Generator;

/// <summary>
/// 自由 SQL の静的バリデーション（<see cref="RawSqlAnalyzer"/>・レベル 1・方言非依存）を網羅するテストクラス
/// </summary>
/// <remarks>
/// 文字列リテラル・コメント・角括弧/二重引用符識別子内の <c>@</c> 無視、<c>''</c> エスケープ、<c>@@</c> 除外、
/// 大文字小文字非依存照合、複文検出（末尾セミコロンは複文でない）、未宣言・未使用の検出を検証する。
/// </remarks>
public class RawSqlAnalyzerTests
{
    /// <summary>解析のショートカット（宣言パラメータを可変長で受ける）</summary>
    private static IReadOnlyList<RawSqlAnalyzer.RawSqlFinding> Analyze(
        string sql,
        params string[] declared
    ) => RawSqlAnalyzer.Analyze(sql, declared);

    /// <summary>指定種別の検出結果を抽出する</summary>
    private static IEnumerable<RawSqlAnalyzer.RawSqlFinding> OfKind(
        IReadOnlyList<RawSqlAnalyzer.RawSqlFinding> findings,
        RawSqlAnalyzer.RawSqlIssueKind kind
    ) => findings.Where(f => f.Kind == kind);

    // ---------------- 正常系 ----------------

    /// <summary>宣言済みパラメータをすべて使う整った SQL は検出なし</summary>
    [Fact(DisplayName = "宣言と使用が一致する SQL は検出なし")]
    public void Analyze_AllParametersDeclaredAndUsed_NoFindings()
    {
        var findings = Analyze(
            "SELECT * FROM [Order] WHERE CustomerId = @customerId AND Amount >= @minAmount",
            "customerId",
            "minAmount"
        );

        findings.Should().BeEmpty();
    }

    /// <summary>大文字小文字が違っても宣言と一致する（照合は大文字小文字非依存）</summary>
    [Fact(DisplayName = "パラメータ照合は大文字小文字非依存")]
    public void Analyze_CaseInsensitiveMatching()
    {
        var findings = Analyze("SELECT * FROM T WHERE X = @CustomerID", "customerId");

        findings.Should().BeEmpty();
    }

    /// <summary>null / 空白のみの SQL は解析対象外（未使用の誤検知を出さない）</summary>
    [Theory(DisplayName = "空・空白 SQL は検出なし")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Analyze_EmptySql_NoFindings(string sql)
    {
        Analyze(sql, "customerId").Should().BeEmpty();
    }

    // ---------------- 未宣言 / 未使用 ----------------

    /// <summary>SQL 内で使われる @xxx が宣言に無ければ未宣言として検出する</summary>
    [Fact(DisplayName = "未宣言パラメータを検出する")]
    public void Analyze_UndeclaredParameter_Detected()
    {
        var findings = Analyze("SELECT * FROM T WHERE X = @ghost", "customerId");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Select(f => f.ParameterName)
            .Should()
            .Equal("ghost");
        // 未使用（customerId）も同時に検出される
        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UnusedParameter)
            .Select(f => f.ParameterName)
            .Should()
            .Equal("customerId");
    }

    /// <summary>宣言済みだが SQL 内で使われないパラメータは未使用として検出する</summary>
    [Fact(DisplayName = "未使用パラメータを検出する")]
    public void Analyze_UnusedParameter_Detected()
    {
        var findings = Analyze("SELECT * FROM T WHERE X = @a", "a", "b", "c");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UnusedParameter)
            .Select(f => f.ParameterName)
            .Should()
            .Equal("b", "c");
        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter).Should().BeEmpty();
    }

    /// <summary>同一パラメータを複数回使っても未宣言は 1 件・未使用と混同しない</summary>
    [Fact(DisplayName = "同一パラメータの複数使用は 1 件に集約")]
    public void Analyze_RepeatedParameter_ReportedOnce()
    {
        var findings = Analyze("SELECT @x, @x, @x FROM T WHERE Y = @x");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter).Should().HaveCount(1);
    }

    // ---------------- スキップ領域（文字列・コメント・識別子） ----------------

    /// <summary>文字列リテラル内の @ はパラメータ扱いしない（'' エスケープ対応）</summary>
    [Fact(DisplayName = "文字列リテラル内の @ は無視（'' エスケープ対応）")]
    public void Analyze_StringLiteral_IgnoresAtSign()
    {
        var findings = Analyze(
            "SELECT * FROM T WHERE Email = '@notparam' OR Note = 'it''s @also not' AND X = @real",
            "real"
        );

        findings.Should().BeEmpty();
    }

    /// <summary>行コメント（--）内の @ は無視する</summary>
    [Fact(DisplayName = "行コメント内の @ は無視")]
    public void Analyze_LineComment_IgnoresAtSign()
    {
        var findings = Analyze("SELECT * FROM T -- WHERE X = @ghost\nWHERE Y = @real", "real");

        findings.Should().BeEmpty();
    }

    /// <summary>ブロックコメント（/* */）内の @ と ; は無視する</summary>
    [Fact(DisplayName = "ブロックコメント内の @ と ; は無視")]
    public void Analyze_BlockComment_IgnoresContents()
    {
        var findings = Analyze("SELECT * /* @ghost ; SELECT 2 */ FROM T WHERE Y = @real", "real");

        findings.Should().BeEmpty();
    }

    /// <summary>[...]（SQL Server 識別子）内の @ は無視する</summary>
    [Fact(DisplayName = "角括弧識別子内の @ は無視")]
    public void Analyze_BracketIdentifier_IgnoresAtSign()
    {
        var findings = Analyze("SELECT [@ghost] FROM T WHERE Y = @real", "real");

        findings.Should().BeEmpty();
    }

    /// <summary>"..."（標準識別子）内の @ は無視する</summary>
    [Fact(DisplayName = "二重引用符識別子内の @ は無視")]
    public void Analyze_QuotedIdentifier_IgnoresAtSign()
    {
        var findings = Analyze("SELECT \"@ghost\" FROM T WHERE Y = @real", "real");

        findings.Should().BeEmpty();
    }

    /// <summary>@@ で始まるシステム変数はパラメータ扱いしない</summary>
    [Fact(DisplayName = "@@システム変数はパラメータ扱いしない")]
    public void Analyze_SystemVariable_NotTreatedAsParameter()
    {
        var findings = Analyze("SELECT @@ROWCOUNT, @@IDENTITY FROM T WHERE Y = @real", "real");

        findings.Should().BeEmpty();
    }

    // ---------------- 複文 ----------------

    /// <summary>; の後にさらにコードが続けば複文として検出する</summary>
    [Fact(DisplayName = "複文（; の後にコード）を検出")]
    public void Analyze_MultipleStatements_Detected()
    {
        var findings = Analyze("SELECT 1 FROM T; SELECT 2 FROM U");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.MultipleStatements).Should().HaveCount(1);
    }

    /// <summary>末尾のセミコロンのみ（後続コードなし）は複文でない</summary>
    [Theory(DisplayName = "末尾セミコロンのみは複文でない")]
    [InlineData("SELECT 1 FROM T;")]
    [InlineData("SELECT 1 FROM T ;  \n  ")]
    [InlineData("SELECT 1 FROM T; -- trailing comment")]
    [InlineData("SELECT 1 FROM T; /* trailing */")]
    public void Analyze_TrailingSemicolon_NotMultipleStatements(string sql)
    {
        Analyze(sql).Should().BeEmpty();
    }

    /// <summary>文字列内のセミコロンは複文の起点にならない</summary>
    [Fact(DisplayName = "文字列内の ; は複文でない")]
    public void Analyze_SemicolonInString_NotMultipleStatements()
    {
        Analyze("SELECT ';not a separator; still one' FROM T").Should().BeEmpty();
    }

    /// <summary>複文（; の後に @param）でも未宣言・未使用と複文を同時に拾う</summary>
    [Fact(DisplayName = "複文と未宣言/未使用を同時に検出")]
    public void Analyze_MultipleStatementsWithUndeclared_ReportsBoth()
    {
        var findings = Analyze("SELECT @a FROM T; DELETE FROM U WHERE X = @b", "a");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Select(f => f.ParameterName)
            .Should()
            .Equal("b");
        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.MultipleStatements).Should().HaveCount(1);
    }

    /// <summary>Describe はローカライズ済みの単文（パラメータ名を含む）を返す</summary>
    [Fact(DisplayName = "Describe はパラメータ名入りのメッセージを返す")]
    public void Describe_ContainsParameterName()
    {
        var finding = new RawSqlAnalyzer.RawSqlFinding(
            RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter,
            "ghost"
        );

        RawSqlAnalyzer.Describe(finding).Should().Contain("ghost");
    }
}
