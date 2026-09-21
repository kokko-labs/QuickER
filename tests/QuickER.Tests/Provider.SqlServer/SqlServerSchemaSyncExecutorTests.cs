using AwesomeAssertions;
using QuickER.Provider.SqlServer;
using QuickER.Services;

namespace QuickER.Tests.Provider.SqlServer;

/// <summary><see cref="SqlServerSchemaSyncExecutor.SplitBatches"/> の GO 区切り分割を検証するテストクラス</summary>
public class SqlServerSchemaSyncExecutorTests
{
    /// <summary>GO で区切られた SQL が複数バッチへ分割されることを検証する</summary>
    [Fact(DisplayName = "GO で区切られたバッチが分割される")]
    public void Split_BasicGo()
    {
        var sql = "CREATE TABLE A (Id int);\nGO\nINSERT INTO A VALUES (1);\nGO\n";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);
        batches.Should().HaveCount(2);
        batches[0].Should().Contain("CREATE TABLE A");
        batches[1].Should().Contain("INSERT INTO A");
    }

    /// <summary>末尾に GO が無くても残りが 1 バッチとして扱われることを検証する</summary>
    [Fact(DisplayName = "末尾に GO がなくてもバッチに含まれる")]
    public void Split_NoTrailingGo()
    {
        var sql = "SELECT 1;";
        SqlServerSchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(1);
    }

    /// <summary>小文字 go も区切りとして認識されることを検証する</summary>
    [Fact(DisplayName = "GO は大文字小文字無視")]
    public void Split_CaseInsensitive()
    {
        var sql = "SELECT 1;\ngo\nSELECT 2;";
        SqlServerSchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(2);
    }

    /// <summary>空文字や GO のみの入力では空バッチ集合を返すことを検証する</summary>
    [Fact(DisplayName = "空文字や空行のみは無視")]
    public void Split_EmptyIgnored()
    {
        SqlServerSchemaSyncExecutor.SplitBatches("").Should().BeEmpty();
        SqlServerSchemaSyncExecutor.SplitBatches("GO\nGO\n").Should().BeEmpty();
    }

    /// <summary>文字列リテラル内の GO 単独行では分割されないことを検証する</summary>
    /// <remarks>説明文は改行を含められるため、追跡しないと拡張プロパティの値でバッチが割れて構文エラーになる</remarks>
    [Fact(DisplayName = "文字列リテラル内の GO 行では分割されない")]
    public void Split_GoInsideStringLiteral_NotSplit()
    {
        var sql = "EXEC sp_addextendedproperty @value = N'first\nGO\nsecond';\nGO\nSELECT 1;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("first").And.Contain("second");
        batches[1].Should().Contain("SELECT 1");
    }

    /// <summary>リテラル内の二重化した引用符で閉じたと誤認しないことを検証する</summary>
    [Fact(DisplayName = "二重化した引用符ではリテラルが閉じない")]
    public void Split_EscapedQuoteKeepsLiteralOpen()
    {
        var sql = "EXEC p N'it''s\nGO\nfine';\nGO\nSELECT 2;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("it''s").And.Contain("fine");
    }

    /// <summary>角括弧識別子の内側にある GO 単独行では分割されないことを検証する</summary>
    [Fact(DisplayName = "角括弧識別子内の GO 行では分割されない")]
    public void Split_GoInsideBracketIdentifier_NotSplit()
    {
        var sql = "CREATE TABLE [a\nGO\nb] (x int);\nGO\nSELECT 3;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("CREATE TABLE");
        batches[1].Should().Contain("SELECT 3");
    }

    /// <summary>ブロックコメントの内側にある GO 単独行では分割されないことを検証する</summary>
    [Fact(DisplayName = "ブロックコメント内の GO 行では分割されない")]
    public void Split_GoInsideBlockComment_NotSplit()
    {
        var sql = "SELECT 1;\n/* note\nGO\nnote */\nSELECT 2;\nGO\nSELECT 3;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("SELECT 1").And.Contain("SELECT 2");
        batches[1].Should().Contain("SELECT 3");
    }

    /// <summary>行コメント内の引用符が後続行のリテラル判定へ持ち越されないことを検証する</summary>
    [Fact(DisplayName = "行コメント内の引用符はリテラルを開かない")]
    public void Split_QuoteInsideLineComment_DoesNotOpenLiteral()
    {
        var sql = "-- it's a note\nSELECT 1;\nGO\nSELECT 2;";
        SqlServerSchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(2);
    }

    /// <summary>入れ子のブロックコメント内にある GO 単独行でも分割されないことを検証する</summary>
    /// <remarks>T-SQL のブロックコメントは入れ子にできるため、内側の <c>*/</c> でコメントが閉じたと誤認しない</remarks>
    [Fact(DisplayName = "入れ子ブロックコメント内の GO 行では分割されない")]
    public void Split_GoInsideNestedBlockComment_NotSplit()
    {
        var sql = "SELECT 1;\n/* outer\n/* inner */\nGO\n*/\nSELECT 2;\nGO\nSELECT 3;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("SELECT 1").And.Contain("SELECT 2");
        batches[1].Should().Contain("SELECT 3");
    }

    /// <summary>閉じられないまま行をまたぐリテラルの内側では、以降の GO 行が区切りにならないことを検証する</summary>
    /// <remarks>
    /// 終端のない引用符は入力自体が壊れているが、そこで分割を再開すると壊れた SQL が複数バッチへ散らばる。
    /// 「開いたら閉じるまで区切らない」＝スクリプト全体が 1 バッチのまま残り、失敗が 1 件に閉じる
    /// </remarks>
    [Fact(DisplayName = "行をまたぐ未終端リテラル内の GO 行では分割されない")]
    public void Split_UnterminatedLiteral_KeepsWholeScriptInOneBatch()
    {
        var sql = "EXEC p N'unterminated\nGO\nSELECT 1;\nGO\nSELECT 2;";
        SqlServerSchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(1);
    }

    /// <summary>二重引用符の識別子（QUOTED_IDENTIFIER）内にある GO 単独行でも分割されないことを検証する</summary>
    [Fact(DisplayName = "二重引用符の識別子内の GO 行では分割されない")]
    public void Split_GoInsideQuotedIdentifier_NotSplit()
    {
        var sql = "CREATE TABLE \"a\nGO\nb\" (x int);\nGO\nSELECT 3;";
        var batches = SqlServerSchemaSyncExecutor.SplitBatches(sql);

        batches.Should().HaveCount(2);
        batches[0].Should().Contain("CREATE TABLE");
        batches[1].Should().Contain("SELECT 3");
    }
}
