using System.Text.Json;
using AwesomeAssertions;
using QuickER.Mcp.Tools;

namespace QuickER.Tests.TestSupport;

/// <summary>MCP のツールホストで扱う図ファイルを作る（前提の組み立て用）</summary>
/// <remarks>
/// <c>create_diagram</c> そのものを確かめるテスト（上書きしない・方言の検証・結果の文面）はこれを使わず、ツールを直接呼ぶ。
/// </remarks>
internal static class DiagramFiles
{
    /// <summary>空の図ファイルを <c>create_diagram</c> で作り、成功したことを確かめる</summary>
    /// <param name="file">作る図ファイルのパス（まだ無いこと）</param>
    /// <param name="targetDbms">図の対象 DBMS</param>
    public static void Create(string file, string targetDbms)
    {
        var (result, success) = DocumentErDiagramToolHost.Execute(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            file,
            JsonSerializer.SerializeToElement(new { target_dbms = targetDbms })
        );

        success.Should().BeTrue($"前提の図ファイルを作れること: {result}");
    }
}
