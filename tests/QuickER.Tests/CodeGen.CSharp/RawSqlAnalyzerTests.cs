using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 自由 SQL の静的バリデーション（<see cref="RawSqlAnalyzer"/>・レベル 1・方言非依存）を網羅するテストクラス
/// </summary>
/// <remarks>
/// 文字列リテラル・コメント・角括弧/二重引用符識別子内の <c>@</c> 無視、<c>''</c> エスケープ、<c>@@</c> 除外、
/// 大文字小文字を区別する照合（実行時の束縛・IN 展開と同じ Ordinal）、複文検出（末尾セミコロンは複文でない）、
/// 未宣言・未使用の検出を検証する。
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

    /// <summary>
    /// 大文字小文字が違う参照は未宣言として報告する。実行時のパラメータ束縛と IN 展開の置換は
    /// Ordinal 照合のため、大小違いは「宣言はあるのに束縛・展開されない」形で必ず失敗する
    /// （リスト引数なら SQL Server の「Must declare the scalar variable」）。宣言側は未使用として併記される
    /// </summary>
    [Fact(DisplayName = "パラメータ照合は大文字小文字を区別する（実行時の束縛と同じ規則）")]
    public void Analyze_CaseSensitiveMatching()
    {
        var findings = Analyze("SELECT * FROM T WHERE X = @CustomerID", "customerId");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Should()
            .ContainSingle()
            .Which.ParameterName.Should()
            .Be("CustomerID");
        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UnusedParameter)
            .Should()
            .ContainSingle()
            .Which.ParameterName.Should()
            .Be("customerId");
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

    /// <summary>識別子の中のエスケープ（]] と ""）の後ろの @ を、識別子の外として扱う</summary>
    [Fact(DisplayName = "識別子のエスケープ（]] と \"\"）の後ろの @ はパラメータとして数える")]
    public void Analyze_EscapedIdentifierQuote_DoesNotEndIdentifierEarly()
    {
        // [a]]b] は列 a]b。] を閉じと見誤ると 'b' 以降が識別子の外に出て、@ghost が未宣言になる
        Analyze("SELECT [a]]@ghost] FROM T WHERE Y = @real", "real").Should().BeEmpty();
        Analyze("SELECT \"a\"\"@ghost\" FROM T WHERE Y = @real", "real").Should().BeEmpty();
        // 識別子の外の @ は従来どおり数える
        OfKind(
                Analyze("SELECT [a]]b] FROM T WHERE Y = @real", "real"),
                RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter
            )
            .Should()
            .BeEmpty();
        OfKind(
                Analyze("SELECT [a]]b] FROM T WHERE Y = @missing", "real"),
                RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter
            )
            .Should()
            .ContainSingle();
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

    /// <summary>先頭の ;（<c>;WITH cte AS (…) SELECT …</c>）は文の終端でなく、複文ではない</summary>
    [Theory(DisplayName = "先頭のセミコロン（;WITH）は複文でない")]
    [InlineData(";WITH c AS (SELECT 1 AS n) SELECT n FROM c")]
    [InlineData("  \n ; WITH c AS (SELECT 1 AS n) SELECT n FROM c;")]
    [InlineData("-- 先頭のコメント\n;WITH c AS (SELECT 1 AS n) SELECT n FROM c")]
    public void Analyze_LeadingSemicolon_NotMultipleStatements(string sql)
    {
        Analyze(sql).Should().BeEmpty();
    }

    /// <summary>先頭の ; の後でも、文の終端の ; の後にコードが続けば複文として検出する</summary>
    [Fact(DisplayName = "先頭のセミコロンの後でも、2 つ目の文があれば複文")]
    public void Analyze_LeadingSemicolonThenSecondStatement_Detected()
    {
        OfKind(
                Analyze(";WITH c AS (SELECT 1 AS n) SELECT n FROM c; SELECT 2"),
                RawSqlAnalyzer.RawSqlIssueKind.MultipleStatements
            )
            .Should()
            .HaveCount(1);
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

    // ---------------- DECLARE（SQL ローカル変数） ----------------

    /// <summary>DECLARE で宣言した SQL ローカル変数は未宣言パラメータとして報告しない</summary>
    /// <summary>EXEC の名前つき引数の左辺（@Name = @param）はパラメータの参照ではない</summary>
    [Fact(DisplayName = "EXEC の名前つき引数の左辺は未宣言扱いしない")]
    public void Analyze_ExecNamedArgument_LeftSideIsNotAParameter()
    {
        Analyze("EXEC dbo.GetOrders @CustomerId = @customerId, @Take = @take", "customerId", "take")
            .Should()
            .BeEmpty();
        Analyze("EXECUTE dbo.GetOrders @CustomerId=@customerId", "customerId").Should().BeEmpty();
        // 右辺が未宣言なら従来どおり報告する
        OfKind(
                Analyze("EXEC dbo.GetOrders @CustomerId = @missing", "customerId"),
                RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter
            )
            .Should()
            .ContainSingle(finding => finding.ParameterName == "missing");
        // EXEC でない文の @x = は代入でなく比較の左辺ではないが、従来どおり参照として数える
        OfKind(
                Analyze("SELECT 1 WHERE @missing = 1", "customerId"),
                RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter
            )
            .Should()
            .ContainSingle(finding => finding.ParameterName == "missing");
        // EXEC は ; で終わる。; の後の文の @x = は参照として数える
        OfKind(
                Analyze("EXEC p @a = @customerId; SELECT 1 WHERE @missing = 1", "customerId"),
                RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter
            )
            .Should()
            .ContainSingle(finding => finding.ParameterName == "missing");
    }

    [Fact(DisplayName = "DECLARE のローカル変数は未宣言扱いしない")]
    public void Analyze_DeclaredLocalVariable_NotReportedAsUndeclared()
    {
        var findings = Analyze(
            "DECLARE @total int SELECT @total + @customerId FROM [Order]",
            "customerId"
        );

        findings.Should().BeEmpty();
    }

    /// <summary>ローカル変数の名前は大文字小文字を区別しない（SQL Server の既定の照合順序と同じ）</summary>
    [Fact(DisplayName = "DECLARE のローカル変数は参照の大文字小文字が違っても未宣言扱いしない")]
    public void Analyze_DeclaredLocalVariable_IsCaseInsensitive()
    {
        var findings = Analyze(
            "DECLARE @Total int SELECT @total + @customerId FROM [Order]",
            "customerId"
        );

        findings.Should().BeEmpty();
    }

    /// <summary>複数宣言（カンマ区切り）と初期化子つきの DECLARE も認識する</summary>
    [Fact(DisplayName = "DECLARE の複数宣言・初期化子つきも認識する")]
    public void Analyze_MultiDeclareWithInitializer_RecognizesAllLocals()
    {
        var findings = Analyze(
            "DECLARE @a int = @customerId, @b int SELECT @a + @b + @ghost",
            "customerId"
        );

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Should()
            .ContainSingle()
            .Which.ParameterName.Should()
            .Be("ghost");
    }

    /// <summary>テーブル変数の列定義内のカンマは宣言リストの区切りとして数えない（括弧深さ 0 のみ）</summary>
    [Fact(DisplayName = "DECLARE ... TABLE の内側のカンマは宣言の区切りでない")]
    public void Analyze_TableVariableInnerCommas_DoNotExtendDeclareList()
    {
        var findings = Analyze("DECLARE @t TABLE (a int, b int) SELECT @missing");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Should()
            .ContainSingle()
            .Which.ParameterName.Should()
            .Be("missing");
    }

    /// <summary>DECLARE cur CURSOR のように @ 以外の語が続く形は変数宣言と見なさない</summary>
    [Fact(DisplayName = "DECLARE CURSOR は変数宣言と見なさない")]
    public void Analyze_DeclareCursor_DoesNotSwallowNextParameter()
    {
        var findings = Analyze("DECLARE cur CURSOR FOR SELECT @ghost");

        OfKind(findings, RawSqlAnalyzer.RawSqlIssueKind.UndeclaredParameter)
            .Should()
            .ContainSingle()
            .Which.ParameterName.Should()
            .Be("ghost");
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
