using FluentAssertions;
using QuickER.AI.Chat;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// <see cref="ErDiagramHostChatAdapter"/> が契約 <see cref="QuickER.Extensibility.IErDiagramHost"/> を
/// チャット固有インターフェースへ正しく委譲することを検証するテストクラス。
/// </summary>
public class ErDiagramHostChatAdapterTests
{
    /// <summary>IsEmpty がホストの値をそのまま返すことを検証する</summary>
    [Fact(DisplayName = "IsEmpty はホストへ委譲する")]
    public void IsEmpty_DelegatesToHost()
    {
        var host = new StubErDiagramHost { IsEmptyToReturn = true };
        var adapter = new ErDiagramHostChatAdapter(host);

        adapter.IsEmpty.Should().BeTrue();
    }

    /// <summary>AutoArrangeNewDiagram がホストへ委譲されることを検証する</summary>
    [Fact(DisplayName = "AutoArrangeNewDiagram はホストへ委譲する")]
    public void AutoArrange_DelegatesToHost()
    {
        var host = new StubErDiagramHost();
        var adapter = new ErDiagramHostChatAdapter(host);

        adapter.AutoArrangeNewDiagram();

        host.AutoArrangeCallCount.Should().Be(1);
    }

    /// <summary>ToolHost.Execute の引数がホストの ExecuteTool へ渡り、戻り値が伝播することを検証する</summary>
    [Fact(DisplayName = "ToolHost.Execute はホストの ExecuteTool へ委譲する")]
    public void ToolHostExecute_DelegatesToHost()
    {
        var host = new StubErDiagramHost { ToolResultToReturn = ("done", true) };
        var adapter = new ErDiagramHostChatAdapter(host);

        var (result, success) = adapter.ToolHost.Execute("add_entity", "{\"name\":\"Order\"}");

        result.Should().Be("done");
        success.Should().BeTrue();
        host.LastToolName.Should().Be("add_entity");
        host.LastArgumentsJson.Should().Be("{\"name\":\"Order\"}");
    }
}
