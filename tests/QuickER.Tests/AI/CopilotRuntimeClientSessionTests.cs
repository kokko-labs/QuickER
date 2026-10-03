using AwesomeAssertions;
using GitHub.Copilot;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="CopilotRuntimeClient"/> のセッション設定（使わせるツールの範囲と、宣言ツールの許可）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 組込みツールを許可するセッション（<see cref="CopilotSessionOptions.AllowWorkspaceTools"/>）と宣言ツールは併用できる
/// という契約を守る。今の呼び出し元（モック生成はツールを渡さず、ER 設計チャットは組込みツールを許可しない）では
/// 併用は起きないが、併用したときに組込みツールも宣言ツールも使えなくなる形で静かに壊れないようにする
/// </remarks>
public class CopilotRuntimeClientSessionTests
{
    /// <summary>組込みツールを許可しないセッションは、宣言ツールだけに絞る</summary>
    [Fact(DisplayName = "組込みツールを許可しないセッションは宣言ツールだけに絞る")]
    public void ResolveAvailableTools_WithoutWorkspaceTools_LimitsToDeclaredTools()
    {
        CopilotRuntimeClient
            .ResolveAvailableTools(allowWorkspaceTools: false, ["add_entity"])
            .Should()
            .Equal("add_entity");
    }

    /// <summary>組込みツールを許可するセッションは、宣言ツールがあっても絞らない</summary>
    [Fact(DisplayName = "組込みツールを許可するセッションは宣言ツールがあっても絞らない")]
    public void ResolveAvailableTools_WithWorkspaceTools_DoesNotLimit()
    {
        CopilotRuntimeClient
            .ResolveAvailableTools(allowWorkspaceTools: true, ["add_entity"])
            .Should()
            .BeNull();
    }

    /// <summary>宣言ツールが無いセッションは絞らない（SDK の既定のまま）</summary>
    [Fact(DisplayName = "宣言ツールが無いセッションは絞らない")]
    public void ResolveAvailableTools_WithoutDeclaredTools_DoesNotLimit()
    {
        CopilotRuntimeClient
            .ResolveAvailableTools(allowWorkspaceTools: false, [])
            .Should()
            .BeNull();
    }

    /// <summary>宣言したツールの呼び出しだけを宣言ツールとして承認対象にする</summary>
    [Fact(DisplayName = "宣言したツールの呼び出しだけを承認対象にする")]
    public void IsDeclaredTool_MatchesOnlyDeclaredCustomTools()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal) { "add_entity" };

        CopilotRuntimeClient.IsDeclaredTool(CustomTool("add_entity"), declared).Should().BeTrue();
        CopilotRuntimeClient.IsDeclaredTool(CustomTool("rm_rf"), declared).Should().BeFalse();
        CopilotRuntimeClient
            .IsDeclaredTool(
                new PermissionRequestMcp
                {
                    ToolName = "add_entity",
                    ToolTitle = "add_entity",
                    ServerName = "s",
                    ReadOnly = true,
                },
                declared
            )
            .Should()
            .BeFalse("同名でも MCP の要求は宣言ツールではない");
    }

    /// <summary>
    /// 組込みツール許可セッションでも、宣言したツールの呼び出しは承認することを検証する
    /// （作業フォルダの判定だけだとカスタムツールの要求は拒否され、宣言したツールそのものが使えない）。
    /// </summary>
    [Fact(DisplayName = "組込みツール許可セッションでも宣言したツールの呼び出しは承認する")]
    public void IsApprovedInWorkspaceSession_ApprovesDeclaredTool()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal) { "add_entity" };
        var root = CopilotWorkspacePermissionPolicy.NormalizeRoot(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickERTests", "copilot-session")
        );

        CopilotRuntimeClient
            .IsApprovedInWorkspaceSession(CustomTool("add_entity"), declared, root)
            .Should()
            .BeTrue();
        CopilotRuntimeClient
            .IsApprovedInWorkspaceSession(CustomTool("rm_rf"), declared, root)
            .Should()
            .BeFalse("宣言していないツールは作業フォルダの判定どおり拒否する");
    }

    /// <summary>カスタムツールの許可要求を組み立てる</summary>
    private static PermissionRequestCustomTool CustomTool(string toolName) =>
        new() { ToolName = toolName, ToolDescription = "test" };
}
