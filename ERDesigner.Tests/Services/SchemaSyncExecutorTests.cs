using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SchemaSyncExecutor.SplitBatches"/> の GO 区切り分割テスト。
/// </summary>
public class SchemaSyncExecutorTests
{
    [Fact(DisplayName = "GO で区切られたバッチが分割される")]
    public void Split_BasicGo()
    {
        var sql = "CREATE TABLE A (Id int);\nGO\nINSERT INTO A VALUES (1);\nGO\n";
        var batches = SchemaSyncExecutor.SplitBatches(sql);
        batches.Should().HaveCount(2);
        batches[0].Should().Contain("CREATE TABLE A");
        batches[1].Should().Contain("INSERT INTO A");
    }

    [Fact(DisplayName = "末尾に GO がなくてもバッチに含まれる")]
    public void Split_NoTrailingGo()
    {
        var sql = "SELECT 1;";
        SchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(1);
    }

    [Fact(DisplayName = "GO は大文字小文字無視")]
    public void Split_CaseInsensitive()
    {
        var sql = "SELECT 1;\ngo\nSELECT 2;";
        SchemaSyncExecutor.SplitBatches(sql).Should().HaveCount(2);
    }

    [Fact(DisplayName = "空文字や空行のみは無視")]
    public void Split_EmptyIgnored()
    {
        SchemaSyncExecutor.SplitBatches("").Should().BeEmpty();
        SchemaSyncExecutor.SplitBatches("GO\nGO\n").Should().BeEmpty();
    }
}
