using AwesomeAssertions;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary><see cref="OracleSchemaSyncExecutor.SplitStatements"/> の SQL*Plus と同じ分割（行末の ; と「/」行）を検証するテストクラス</summary>
public class OracleSchemaSyncExecutorTests
{
    /// <summary>「/」のみの行で文が分割されることを検証する</summary>
    [Fact(DisplayName = "「/」のみの行で文が分割される")]
    public void Split_BasicSlash()
    {
        var sql = "ALTER TABLE \"a\" ADD (x NUMBER);\n/\nALTER TABLE \"b\" ADD (y NUMBER);\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("ALTER TABLE \"a\"");
        statements[1].Should().Contain("ALTER TABLE \"b\"");
    }

    /// <summary>通常文は末尾の ; が除去され、PL/SQL ブロックは保持されることを検証する</summary>
    [Fact(DisplayName = "通常文は末尾 ; を除去し PL/SQL ブロックは保持する")]
    public void Split_TrimsTrailingSemicolonExceptPlSqlBlock()
    {
        OracleSchemaSyncExecutor
            .SplitStatements("SELECT 1 FROM dual;\n/\n")[0]
            .Should()
            .NotEndWith(";");
        OracleSchemaSyncExecutor
            .SplitStatements("BEGIN\n  NULL;\nEND;\n/\n")[0]
            .Should()
            .EndWith("END;");
    }

    /// <summary>コメントのみ・空の塊が実行対象から除外されることを検証する</summary>
    [Fact(DisplayName = "コメントのみ・空の塊は除外される")]
    public void Split_CommentOnlyIgnored()
    {
        OracleSchemaSyncExecutor.SplitStatements("-- note\n/\n\n/\n").Should().BeEmpty();
    }

    /// <summary>文字列リテラル内の「/」単独行では分割されないことを検証する</summary>
    /// <remarks>説明文は改行を含められるため、追跡しないと COMMENT ON の値で文が割れて部分適用になる</remarks>
    [Fact(DisplayName = "文字列リテラル内の「/」行では分割されない")]
    public void Split_SlashInsideStringLiteral_NotSplit()
    {
        var sql = "COMMENT ON TABLE \"t\" IS 'first\n/\nsecond';\n/\nSELECT 1 FROM dual;\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("first").And.Contain("second");
        statements[1].Should().Contain("SELECT 1 FROM dual");
    }

    /// <summary>リテラル内の二重化した引用符で閉じたと誤認しないことを検証する</summary>
    [Fact(DisplayName = "二重化した引用符ではリテラルが閉じない")]
    public void Split_EscapedQuoteKeepsLiteralOpen()
    {
        var sql = "COMMENT ON TABLE \"t\" IS 'it''s\n/\nfine';\n/\nSELECT 2 FROM dual;\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("it''s").And.Contain("fine");
    }

    /// <summary>引用識別子の内側にある「/」単独行では分割されないことを検証する</summary>
    [Fact(DisplayName = "引用識別子内の「/」行では分割されない")]
    public void Split_SlashInsideQuotedIdentifier_NotSplit()
    {
        var sql = "ALTER TABLE \"a\n/\nb\" ADD (x NUMBER);\n/\nSELECT 3 FROM dual;\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("ALTER TABLE");
        statements[1].Should().Contain("SELECT 3 FROM dual");
    }

    /// <summary>ブロックコメントの内側にある「/」単独行では分割されないことを検証する</summary>
    [Fact(DisplayName = "ブロックコメント内の「/」行では分割されない")]
    public void Split_SlashInsideBlockComment_NotSplit()
    {
        var sql = "SELECT 1 FROM dual\n/* note\n/\nnote */\n;\n/\nSELECT 2 FROM dual;\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("SELECT 1 FROM dual");
        statements[1].Should().Contain("SELECT 2 FROM dual");
    }

    /// <summary>行コメント内の引用符が後続行のリテラル判定へ持ち越されないことを検証する</summary>
    [Fact(DisplayName = "行コメント内の引用符はリテラルを開かない")]
    public void Split_QuoteInsideLineComment_DoesNotOpenLiteral()
    {
        var sql = "-- it's a note\nSELECT 1 FROM dual;\n/\nSELECT 2 FROM dual;\n/\n";
        OracleSchemaSyncExecutor.SplitStatements(sql).Should().HaveCount(2);
    }

    /// <summary>ブロックコメントは入れ子にならない（最初の <c>*/</c> で閉じる）ことを検証する</summary>
    /// <remarks>
    /// Oracle のブロックコメントは入れ子にできない＝T-SQL 側（段数で数える）との意図的な差。
    /// 内側の <c>*/</c> でコメントが閉じるため、その後の「/」行は通常どおり区切りとして効く
    /// </remarks>
    [Fact(DisplayName = "ブロックコメントは入れ子にならず最初の */ で閉じる")]
    public void Split_BlockCommentDoesNotNest()
    {
        var sql = "SELECT 1 FROM dual\n/* outer\n/* inner */\n/\nSELECT 2 FROM dual;\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("SELECT 1 FROM dual");
        statements[1].Should().Contain("SELECT 2 FROM dual");
    }

    /// <summary>閉じられないまま行をまたぐリテラルの内側では、以降の「/」行が区切りにならないことを検証する</summary>
    /// <remarks>
    /// 終端のない引用符は入力自体が壊れているが、そこで分割を再開すると壊れた SQL が複数文へ散らばる。
    /// Oracle の DDL は暗黙コミットのため、部分適用を増やさないぶん「全体を 1 文のまま失敗させる」方が安全
    /// </remarks>
    [Fact(DisplayName = "行をまたぐ未終端リテラル内の「/」行では分割されない")]
    public void Split_UnterminatedLiteral_KeepsWholeScriptInOneStatement()
    {
        var sql =
            "COMMENT ON TABLE \"t\" IS 'unterminated\n/\nSELECT 1 FROM dual;\n/\nSELECT 2 FROM dual;\n/\n";
        OracleSchemaSyncExecutor.SplitStatements(sql).Should().HaveCount(1);
    }

    /// <summary>通常の文は行末の ; で区切られ、「/」行が無くても分割されることを検証する</summary>
    [Fact(DisplayName = "通常の文は行末の ; で区切られる（「/」行は要らない）")]
    public void Split_SemicolonTerminatesPlainStatement()
    {
        var sql = "CREATE TABLE \"a\" (\n  x NUMBER\n);\nALTER TABLE \"a\" ADD (y NUMBER);\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().StartWith("CREATE TABLE").And.NotEndWith(";");
        statements[1].Should().StartWith("ALTER TABLE");
    }

    /// <summary>通常の文の後ろに「/」行があっても、その文は 1 回だけ実行対象になることを検証する</summary>
    /// <remarks>SQL*Plus はこの「/」で直前の文を再実行するが、実行器は空のバッファの「/」を何もしない</remarks>
    [Fact(DisplayName = "通常の文の後ろの「/」行で同じ文が 2 回実行対象にならない")]
    public void Split_SlashAfterTerminatedStatement_RunsOnce()
    {
        var sql =
            "CREATE TABLE \"a\" (x NUMBER);\n/\nBEGIN\n  NULL;\nEND;\n/\nALTER TABLE \"a\" ADD (y NUMBER);\n/\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements
            .Should()
            .Equal(
                "CREATE TABLE \"a\" (x NUMBER)",
                "BEGIN\n  NULL;\nEND;",
                "ALTER TABLE \"a\" ADD (y NUMBER)"
            );
    }

    /// <summary>PL/SQL ブロックの中の行末の ; では区切らず、「/」行で区切ることを検証する</summary>
    [Fact(DisplayName = "ブロックの中の行末の ; では区切らず「/」行で区切る")]
    public void Split_BlockEndsOnlyAtSlash()
    {
        var sql = "-- note\nDECLARE\n  n NUMBER;\nBEGIN\n  n := 1;\nEND;\n/\nSELECT 1 FROM dual;\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().StartWith("-- note\nDECLARE").And.EndWith("END;");
        statements[1].Should().Be("SELECT 1 FROM dual");
    }

    /// <summary>行コメントやリテラルの中の行末の ; では区切らないことを検証する</summary>
    [Fact(DisplayName = "行コメント・リテラルの中の行末の ; では区切らない")]
    public void Split_SemicolonInsideCommentOrLiteral_DoesNotTerminate()
    {
        var sql = "SELECT 1 -- a;\nFROM dual;\nCOMMENT ON TABLE \"t\" IS 'x;\ny';\n";
        var statements = OracleSchemaSyncExecutor.SplitStatements(sql);

        statements.Should().HaveCount(2);
        statements[0].Should().Contain("FROM dual");
        statements[1].Should().Contain("x;\ny'");
    }

    /// <summary>ブロックの判定は語の単位で行い、BEGIN で始まる別の語を見誤らないことを検証する</summary>
    [Fact(DisplayName = "BEGINNING のような語はブロックの始まりとみなさない")]
    public void Split_IsPlSqlBlock_RequiresWordBoundary()
    {
        OracleSchemaSyncExecutor.IsPlSqlBlock("BEGINNING_TABLE x;").Should().BeFalse();
        OracleSchemaSyncExecutor.IsPlSqlBlock("-- note\n  begin\nNULL;").Should().BeTrue();
    }
}
