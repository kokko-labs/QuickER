using FluentAssertions;
using QuickER.Services;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

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
}
