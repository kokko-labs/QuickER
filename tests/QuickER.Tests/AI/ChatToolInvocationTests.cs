using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.Resources;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// チャットエンジン 4 種が共有する、ツール実行と活動通知の作法を検証するテストクラス。
/// </summary>
/// <remarks>
/// この 2 つは 4 エンジンで同じでなければならないが、4 箇所へ写していた間に実際にずれた
/// （通知を保護していないエンジン・通知の失敗を「応答送信の失敗」と表示するエンジン）。
/// 作法そのものはここで、4 エンジンがそこを通ることは末尾のソースガードで固定する。
/// </remarks>
public class ChatToolInvocationTests
{
    /// <summary>指定の結果を返すか、指定の例外を投げるツールホスト</summary>
    private sealed class StubToolHost(
        (string Result, bool Success) result,
        Exception? throws = null
    ) : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            throws is not null ? throw throws : result;
    }

    /// <summary>その場で実行する UI ディスパッチャ（マーシャリングの有無はここでは関係しない）</summary>
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    private static readonly IUiDispatcher Dispatcher = new ImmediateDispatcher();

    [Fact(DisplayName = "Execute: 成功した結果はそのまま返す")]
    public void Execute_Success_ReturnsResult()
    {
        var host = new StubToolHost(("added", true));

        ChatToolInvocation
            .Execute(host, Dispatcher, "add_entity", () => "{}", rethrowCancellation: false)
            .Should()
            .Be(("added", true));
    }

    [Fact(DisplayName = "Execute: 例外は英語の失敗結果へ畳む")]
    public void Execute_Exception_FoldsIntoFailureResult()
    {
        var host = new StubToolHost(default, new InvalidOperationException("boom"));

        var (result, success) = ChatToolInvocation.Execute(
            host,
            Dispatcher,
            "add_entity",
            () => "{}",
            rethrowCancellation: false
        );

        result.Should().Be("The tool 'add_entity' threw an exception: boom");
        success.Should().BeFalse();
    }

    /// <summary>引数の取り出しで投げても、失敗結果へ畳むことを検証する</summary>
    /// <remarks>
    /// 引数を値で受けると、呼び出し側の引数評価が try の外で走る。Codex は <c>arguments</c> を持たない
    /// 要求で <c>JsonElement.GetRawText()</c> が投げるため、そこで抜けると応答を送れず AI が待ち続ける。
    /// </remarks>
    [Fact(DisplayName = "Execute: 引数の取り出しで投げても失敗結果へ畳む")]
    public void Execute_WhenArgumentsThrow_FoldsIntoFailureResult()
    {
        var host = new StubToolHost(("added", true));

        var (result, success) = ChatToolInvocation.Execute(
            host,
            Dispatcher,
            "add_entity",
            () => throw new InvalidOperationException("引数がありません"),
            rethrowCancellation: false
        );

        result.Should().Be("The tool 'add_entity' threw an exception: 引数がありません");
        success.Should().BeFalse();
    }

    /// <summary>中断の扱いが 4 エンジンの唯一の差であることを固定する</summary>
    /// <remarks>
    /// API キー接続だけは中断を通す必要がある（未実行の呼び出しへ合成結果を補って履歴の対応を保つ経路へ渡す）。
    /// 残る 3 つは中断もツールの失敗として畳む従来の挙動。
    /// </remarks>
    [Fact(DisplayName = "Execute: 中断は rethrowCancellation の指定どおりに扱う")]
    public void Execute_Cancellation_FollowsTheFlag()
    {
        var host = new StubToolHost(default, new OperationCanceledException());

        var act = () =>
            ChatToolInvocation.Execute(
                host,
                Dispatcher,
                "add_entity",
                () => "{}",
                rethrowCancellation: true
            );
        act.Should().Throw<OperationCanceledException>();

        var (result, success) = ChatToolInvocation.Execute(
            host,
            Dispatcher,
            "add_entity",
            () => "{}",
            rethrowCancellation: false
        );
        result.Should().StartWith("The tool 'add_entity' threw an exception");
        success.Should().BeFalse();
    }

    [Fact(DisplayName = "Notify: 購読側の例外は呼び出し元へ伝えず、状態メッセージで知らせる")]
    public void Notify_SubscriberThrows_IsReportedNotPropagated()
    {
        var statuses = new List<string>();
        EventHandler<ErChatToolActivity> handler = (_, _) =>
            throw new InvalidOperationException("UI が落ちた");

        var act = () =>
            ChatToolInvocation.Notify(
                handler,
                this,
                new ErChatToolActivity("add_entity", "added", true),
                statuses.Add
            );

        act.Should().NotThrow();
        statuses
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(AiStrings.Chat_ToolActivityNotifyFailed, "UI が落ちた"));
    }

    [Fact(DisplayName = "Notify: 購読者がいなければ何もしない")]
    public void Notify_NoSubscriber_DoesNothing()
    {
        var statuses = new List<string>();

        var act = () =>
            ChatToolInvocation.Notify(
                null,
                this,
                new ErChatToolActivity("add_entity", "added", true),
                statuses.Add
            );

        act.Should().NotThrow();
        statuses.Should().BeEmpty();
    }

    /// <summary>4 エンジンが共有ヘルパーを通していることをソース上で固定する</summary>
    /// <remarks>
    /// 作法を写し取ると、また同じずれ（保護漏れ・文言違い）が起きる。エンジン側に生の文言や
    /// 裸の通知が残っていないことまで見張る。
    /// </remarks>
    [Theory(DisplayName = "4 エンジンは共有ヘルパーを通す")]
    [InlineData("ChatTurnEngine.cs")]
    [InlineData("ClaudeCodeChatEngine.cs")]
    [InlineData("CodexChatEngine.cs")]
    [InlineData("CopilotChatEngine.cs")]
    public void Engines_GoThroughTheSharedHelper(string fileName)
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.AI", fileName)
        );

        source.Should().Contain("ChatToolInvocation.Execute(");
        source.Should().Contain("ChatToolInvocation.Notify(");
        source.Should().NotContain("threw an exception:", "失敗結果の文言はヘルパー 1 箇所に置く");
        source
            .Should()
            .NotContain(
                "ToolActivityReceived?.Invoke(",
                "保護しない裸の通知を残さない（ヘルパー経由にする）"
            );
    }
}
