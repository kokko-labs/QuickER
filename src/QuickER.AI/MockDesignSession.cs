using System.Text.Json;
using QuickER.Model;

namespace QuickER.AI;

/// <summary>モック HTML が更新されたときの通知内容</summary>
/// <param name="Html">提出された完全な HTML 全体</param>
/// <param name="RevisionNote">この版の変更点（省略時は空文字）</param>
public readonly record struct MockHtmlUpdate(string Html, string RevisionNote);

/// <summary>
/// <see cref="IErChatEngine"/> をラップし、ER スキーマから Web モック HTML を生成する会話セッション。
/// <c>save_mock_html</c> ツール呼び出しを内部のツールホストとして処理し、確定 HTML を保持・通知する。
/// </summary>
/// <remarks>
/// エンジンには <see cref="ErChatProfile"/> の「モック生成プロファイル」（<see cref="MockDesignPrompts"/>／
/// <see cref="MockDesignTools"/>）が注入されている前提。エンジン生成はアプリ側の責務とし、本クラスは会話制御に専念する。
/// </remarks>
public sealed class MockDesignSession : IErDiagramToolHost
{
    private readonly IErChatEngine _engine;

    /// <summary>最新の確定 HTML（未提出なら null）</summary>
    public string? CurrentHtml { get; private set; }

    /// <summary>モック HTML が更新されたときに発火する</summary>
    public event EventHandler<MockHtmlUpdate>? HtmlUpdated;

    /// <summary>応答テキストの逐次断片（エンジンから転送）</summary>
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <summary>ターンの完了（エンジンから転送）</summary>
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <summary>ステータス文言の変化（エンジンから転送）</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>モック生成プロファイルが注入済みのエンジンからセッションを生成する</summary>
    /// <param name="engine">会話エンジン（本セッションがツールホストとして振る舞う前提で構成されていること）</param>
    public MockDesignSession(IErChatEngine engine)
    {
        _engine = engine;
        SubscribeEngine();
    }

    /// <summary>
    /// エンジンファクトリからセッションを生成する。
    /// エンジンはツールホストをコンストラクタで要求し、本セッション自身がそのツールホストであるため相互依存になる。
    /// これを解くため、ファクトリには本セッションへ遅延解決するツールホストを渡してエンジンを生成させ、
    /// 構築完了後にツールホストの解決先を自分自身へ結び付ける（エンジン⇔ツールホストの循環を断つ）。
    /// </summary>
    /// <param name="engineFactory">
    /// ツールホストを受け取り、モック生成プロファイル注入済みのエンジンを生成するファクトリ。
    /// 引数のツールホストは本セッション自身（<c>save_mock_html</c> を処理する）へ解決される
    /// </param>
    public MockDesignSession(Func<IErDiagramToolHost, IErChatEngine> engineFactory)
    {
        var deferred = new DeferredToolHost();
        _engine = engineFactory(deferred);
        // エンジン生成が済み this が有効になったので、ツールホストの解決先を自分自身に確定する
        deferred.Target = this;
        SubscribeEngine();
    }

    /// <summary>エンジンのイベントをセッションのイベントへ転送する</summary>
    private void SubscribeEngine()
    {
        _engine.AssistantDeltaReceived += (_, delta) => AssistantDeltaReceived?.Invoke(this, delta);
        _engine.TurnCompleted += (_, result) => TurnCompleted?.Invoke(this, result);
        _engine.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
    }

    /// <summary>
    /// エンジン生成時にツールホストとして渡し、セッション構築完了後に本セッションへ解決先を確定する遅延ホスト。
    /// エンジン⇔セッションの相互依存（コンストラクタ順序の鶏卵問題）を断つための薄い転送層。
    /// </summary>
    private sealed class DeferredToolHost : IErDiagramToolHost
    {
        /// <summary>解決先のツールホスト（構築完了後に設定される）</summary>
        public IErDiagramToolHost? Target { get; set; }

        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            Target is null
                ? ("セッションが初期化されていません。", false)
                : Target.Execute(toolName, argumentsJson);
    }

    /// <summary>会話を開始する。スキーマ直列化＋ユーザー補足指示を初回プロンプトとして送信する</summary>
    /// <param name="diagram">モックの元になる ER 図</param>
    /// <param name="userInstructions">ユーザーからの補足指示（省略可）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public Task StartAsync(
        ErDiagram diagram,
        string? userInstructions,
        CancellationToken cancellationToken = default
    ) => StartAsync(diagram, userInstructions, attachments: null, cancellationToken);

    /// <summary>
    /// 会話を開始する（添付付きオーバーロード）。スキーマ直列化＋ユーザー補足指示を初回プロンプトとし、
    /// 添付（画像・PDF）をデザイン参考としてエンジンへ透過的に渡す。
    /// </summary>
    /// <param name="diagram">モックの元になる ER 図</param>
    /// <param name="userInstructions">ユーザーからの補足指示（省略可）</param>
    /// <param name="attachments">同梱する添付（省略可・null なら添付なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task StartAsync(
        ErDiagram diagram,
        string? userInstructions,
        IReadOnlyList<ChatAttachment>? attachments,
        CancellationToken cancellationToken = default
    )
    {
        CurrentHtml = null;

        await _engine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _engine.StartConversationAsync(cancellationToken).ConfigureAwait(false);

        var prompt = BuildInitialPrompt(diagram, userInstructions);
        await _engine
            .SendAsync(prompt, attachments ?? Array.Empty<ChatAttachment>(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>修正指示を 1 ターンとして送信する</summary>
    /// <param name="feedback">ユーザーの修正指示</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public Task SendFeedbackAsync(string feedback, CancellationToken cancellationToken = default) =>
        SendFeedbackAsync(feedback, attachments: null, cancellationToken);

    /// <summary>修正指示を 1 ターンとして送信する（添付付きオーバーロード。添付を透過的に渡す）</summary>
    /// <param name="feedback">ユーザーの修正指示</param>
    /// <param name="attachments">同梱する添付（省略可・null なら添付なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public Task SendFeedbackAsync(
        string feedback,
        IReadOnlyList<ChatAttachment>? attachments,
        CancellationToken cancellationToken = default
    ) =>
        _engine.SendAsync(
            feedback,
            attachments ?? Array.Empty<ChatAttachment>(),
            cancellationToken
        );

    /// <summary>実行中のターンを中断する</summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default) =>
        _engine.InterruptAsync(cancellationToken);

    /// <summary>初回プロンプト（スキーマ記述＋補足指示）を組み立てる</summary>
    internal static string BuildInitialPrompt(ErDiagram diagram, string? userInstructions)
    {
        var schema = MockSchemaSerializer.Serialize(diagram);

        var prompt =
            "次の ER スキーマをもとに、業務に即した画面構成を提案してください。\n\n" + schema;

        if (!string.IsNullOrWhiteSpace(userInstructions))
        {
            prompt += $"\n\n# 補足指示\n{userInstructions.Trim()}";
        }

        return prompt;
    }

    /// <summary>ツール実行（<c>save_mock_html</c> のみ処理）。HTML を検証し確定 HTML を更新・通知する</summary>
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        if (
            !string.Equals(toolName, MockDesignTools.SaveMockHtmlToolName, StringComparison.Ordinal)
        )
        {
            return ($"未知のツールです: {toolName}", false);
        }

        string? html;
        string revisionNote;

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
            );
            var root = document.RootElement;

            html =
                root.TryGetProperty("html", out var htmlElement)
                && htmlElement.ValueKind == JsonValueKind.String
                    ? htmlElement.GetString()
                    : null;

            revisionNote =
                root.TryGetProperty("revision_note", out var noteElement)
                && noteElement.ValueKind == JsonValueKind.String
                    ? noteElement.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (JsonException ex)
        {
            return ($"引数の JSON を解釈できませんでした: {ex.Message}", false);
        }

        // 軽微なサニティチェック: 空でなく、HTML らしさ（<html を含む）があること
        if (string.IsNullOrWhiteSpace(html))
        {
            return ("html が空です。完全な HTML 全体を提出してください。", false);
        }

        if (!html.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "HTML として不完全です。<html> を含む単一ファイルの完全な HTML を提出してください。",
                false
            );
        }

        CurrentHtml = html;
        HtmlUpdated?.Invoke(this, new MockHtmlUpdate(html, revisionNote));

        return ("モックを受領しました。プレビューへ反映済みです。", true);
    }
}
