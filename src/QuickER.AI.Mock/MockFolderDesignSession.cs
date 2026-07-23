using System.Text;
using System.Text.Json;
using QuickER.AI;
using QuickER.AI.Mock.Resources;
using QuickER.Model;

namespace QuickER.AI.Mock;

/// <summary>画面 HTML が保存されたときの通知内容</summary>
/// <param name="File">保存された画面ファイル名</param>
/// <param name="RevisionNote">この版の変更点（省略時は空文字）</param>
/// <param name="Warnings">機械検証の警告一覧（英語・問題なしなら空）</param>
public sealed record MockScreenSavedEventArgs(
    string File,
    string RevisionNote,
    IReadOnlyList<string> Warnings
);

/// <summary>共有スタイルシートが保存されたときの通知内容</summary>
/// <param name="RevisionNote">この版の変更点（省略時は空文字）</param>
/// <param name="Warnings">機械検証の警告一覧（英語・問題なしなら空）</param>
public sealed record MockStylesheetSavedEventArgs(
    string RevisionNote,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// <see cref="IErChatEngine"/> をラップし、ER スキーマから「モックフォルダ」（画面ごとの HTML＋共有 style.css）を
/// 生成する会話セッション。画面/CSS の保存・取得・削除ツール（save_screen / save_stylesheet / get_screen /
/// remove_screen）を内部のツールホストとして処理し、<see cref="MockFolderStore"/> へ委譲する。
/// </summary>
/// <remarks>
/// エンジンには <see cref="MockDesignProfile.FolderMockDesign"/> プロファイル
/// （<see cref="MockFolderDesignPrompts"/>／<see cref="MockFolderDesignTools"/>）が注入されている前提。
/// エンジン生成はアプリ側の責務とし、本クラスは会話制御とツール実行に専念する。
/// フォルダ（<see cref="MockFolderStore"/>）は呼び出し側（VM）が CreateNew / Open 済みのものを渡す。
/// </remarks>
public sealed class MockFolderDesignSession : IErDiagramToolHost
{
    private readonly IErChatEngine _engine;

    private readonly MockFolderStore _store;

    /// <summary>UI（サイドバー・プレビュー）が参照するモックフォルダのストア</summary>
    public MockFolderStore Store => _store;

    /// <summary>画面が保存されたときに発火する（プレビュー更新用）</summary>
    public event EventHandler<MockScreenSavedEventArgs>? ScreenSaved;

    /// <summary>画面が削除されたときに発火する（引数は削除された画面ファイル名）</summary>
    public event EventHandler<string>? ScreenRemoved;

    /// <summary>共有スタイルシートが保存されたときに発火する</summary>
    public event EventHandler<MockStylesheetSavedEventArgs>? StylesheetSaved;

    /// <summary>応答テキストの逐次断片(エンジンから転送)</summary>
    public event EventHandler<string>? AssistantDeltaReceived;

    /// <summary>ターンの完了（エンジンから転送）</summary>
    public event EventHandler<ErChatTurnResult>? TurnCompleted;

    /// <summary>ステータス文言の変化（エンジンから転送）</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// エンジンファクトリとモックフォルダストアからセッションを生成する。
    /// エンジンはツールホストをコンストラクタで要求し、本セッション自身がそのツールホストであるため相互依存になる。
    /// これを解くため、ファクトリには本セッションへ遅延解決するツールホストを渡してエンジンを生成させ、
    /// 構築完了後にツールホストの解決先を自分自身へ結び付ける（エンジン⇔ツールホストの循環を断つ）。
    /// </summary>
    /// <param name="engineFactory">
    /// ツールホストを受け取り、モックフォルダ方式プロファイル注入済みのエンジンを生成するファクトリ。
    /// 引数のツールホストは本セッション自身（画面/CSS ツールを処理する）へ解決される
    /// </param>
    /// <param name="store">対象のモックフォルダストア（呼び出し側で CreateNew / Open 済み）</param>
    public MockFolderDesignSession(
        Func<IErDiagramToolHost, IErChatEngine> engineFactory,
        MockFolderStore store
    )
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

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
                ? ("Session is not initialized.", false)
                : Target.Execute(toolName, argumentsJson);
    }

    /// <summary>
    /// 新規フローで会話を開始する。ER スキーマ記述＋ユーザー補足指示を初回プロンプトとして送信し、
    /// ストアのスキーマスナップショットが空なら現在スキーマで保存する。
    /// </summary>
    /// <param name="diagram">モックの元になる ER 図</param>
    /// <param name="userInstructions">ユーザーからの補足指示（省略可）</param>
    /// <param name="attachments">同梱する添付（省略可・null なら添付なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task StartNewAsync(
        ErDiagram diagram,
        string? userInstructions,
        IReadOnlyList<ChatAttachment>? attachments,
        CancellationToken cancellationToken = default
    )
    {
        var schema = MockSchemaSerializer.Serialize(diagram);

        // スキーマスナップショットが空（新規作成直後）なら、現在スキーマを保存しておく
        if (string.IsNullOrWhiteSpace(_store.Manifest.SourceSchema))
        {
            _store.UpdateSourceSchema(schema);
        }

        var prompt = AppendUserInstructions(
            string.Format(Strings.Mock_FolderInitialPromptTemplate, schema),
            userInstructions
        );

        await SendInitialAsync(prompt, attachments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 再開フローで会話を開始する。現在スキーマとマニフェストから状態再開プロンプトを組み立てて送信し、
    /// 送信前にストアのスキーマスナップショットを現在スキーマへ更新する。
    /// </summary>
    /// <param name="diagram">モックの元になる（現在の）ER 図</param>
    /// <param name="userInstructions">ユーザーからの補足指示（省略可）</param>
    /// <param name="attachments">同梱する添付（省略可・null なら添付なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task StartResumeAsync(
        ErDiagram diagram,
        string? userInstructions,
        IReadOnlyList<ChatAttachment>? attachments,
        CancellationToken cancellationToken = default
    )
    {
        var schema = MockSchemaSerializer.Serialize(diagram);
        var manifest = _store.Manifest;
        var changed = MockResumePrompt.IsSchemaChanged(schema, manifest);

        var prompt = AppendUserInstructions(
            MockResumePrompt.Build(schema, manifest, changed),
            userInstructions
        );

        // 再開時は元スキーマを現在の内容へ更新してから送信する（次回の差異判定の基準になる）
        _store.UpdateSourceSchema(schema);

        await SendInitialAsync(prompt, attachments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>エンジンを初期化して会話を開始し、初回プロンプトを送信する（新規・再開で共通）</summary>
    private async Task SendInitialAsync(
        string prompt,
        IReadOnlyList<ChatAttachment>? attachments,
        CancellationToken cancellationToken
    )
    {
        await _engine.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _engine.StartConversationAsync(cancellationToken).ConfigureAwait(false);

        await _engine
            .SendAsync(prompt, attachments ?? Array.Empty<ChatAttachment>(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>初回プロンプト本文へユーザー補足指示（見出し付き）を連結する</summary>
    private static string AppendUserInstructions(string prompt, string? userInstructions)
    {
        if (string.IsNullOrWhiteSpace(userInstructions))
        {
            return prompt;
        }

        return prompt
            + "\n\n"
            + Strings.Mock_PromptUserInstructionsHeading
            + "\n"
            + userInstructions.Trim();
    }

    /// <summary>修正指示を 1 ターンとして送信する（添付は透過的に渡す）</summary>
    /// <param name="feedback">ユーザーの修正指示</param>
    /// <param name="attachments">同梱する添付（省略可・null なら添付なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public Task SendFeedbackAsync(
        string feedback,
        IReadOnlyList<ChatAttachment>? attachments = null,
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

    /// <summary>ツール実行。4 ツール（save_screen / remove_screen / save_stylesheet / get_screen）を振り分ける</summary>
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
            );
            var root = document.RootElement;

            return toolName switch
            {
                MockFolderDesignTools.SaveScreenToolName => ExecuteSaveScreen(root),
                MockFolderDesignTools.RemoveScreenToolName => ExecuteRemoveScreen(root),
                MockFolderDesignTools.SaveStylesheetToolName => ExecuteSaveStylesheet(root),
                MockFolderDesignTools.GetScreenToolName => ExecuteGetScreen(root),
                _ => (string.Format(Strings.Mock_UnknownToolResult, toolName), false),
            };
        }
        catch (JsonException ex)
        {
            return (string.Format(Strings.Mock_ArgumentsParseFailedResult, ex.Message), false);
        }
    }

    /// <summary>save_screen: 画面 HTML を保存し、警告を連結して受領文言を返す</summary>
    private (string Result, bool Success) ExecuteSaveScreen(JsonElement root)
    {
        var file = GetString(root, "file");
        var name = GetString(root, "name");
        var description = GetString(root, "description");
        var html = GetString(root, "html");
        var revisionNote = GetString(root, "revision_note");
        var transitions = ParseTransitions(root, file);

        IReadOnlyList<string> warnings;

        try
        {
            warnings = _store.SaveScreen(file, name, description, html, transitions, revisionNote);
        }
        catch (ArgumentException ex)
        {
            // file 不正・html 空/非 HTML などは失敗結果として返す（例外は外へ漏らさない）
            return (ex.Message, false);
        }

        ScreenSaved?.Invoke(this, new MockScreenSavedEventArgs(file, revisionNote, warnings));

        var result = AppendWarnings(string.Format(Strings.Mock_ScreenSavedResult, file), warnings);

        return (result, true);
    }

    /// <summary>remove_screen: 画面を削除する（未存在は失敗）</summary>
    private (string Result, bool Success) ExecuteRemoveScreen(JsonElement root)
    {
        var file = GetString(root, "file");

        try
        {
            _store.RemoveScreen(file);
        }
        catch (ArgumentException ex)
        {
            return (ex.Message, false);
        }
        catch (InvalidOperationException)
        {
            // 未存在: 利用可能な画面一覧を添えて失敗を返す
            return (
                string.Format(Strings.Mock_ScreenNotFoundResult, file, DescribeKnownScreens()),
                false
            );
        }

        ScreenRemoved?.Invoke(this, file);

        return (string.Format(Strings.Mock_ScreenRemovedResult, file), true);
    }

    /// <summary>save_stylesheet: 共有スタイルシートを保存し、警告を連結して受領文言を返す</summary>
    private (string Result, bool Success) ExecuteSaveStylesheet(JsonElement root)
    {
        var css = GetString(root, "css");
        var revisionNote = GetString(root, "revision_note");

        var warnings = _store.SaveStylesheet(css, revisionNote);

        StylesheetSaved?.Invoke(this, new MockStylesheetSavedEventArgs(revisionNote, warnings));

        var result = AppendWarnings(Strings.Mock_StylesheetSavedResult, warnings);

        return (result, true);
    }

    /// <summary>get_screen: 画面 HTML を返す（未存在は利用可能な画面一覧を添えて失敗）</summary>
    private (string Result, bool Success) ExecuteGetScreen(JsonElement root)
    {
        var file = GetString(root, "file");

        string? html;

        try
        {
            html = _store.GetScreenHtml(file);
        }
        catch (ArgumentException ex)
        {
            return (ex.Message, false);
        }

        if (html is null)
        {
            return (
                string.Format(Strings.Mock_ScreenNotFoundResult, file, DescribeKnownScreens()),
                false
            );
        }

        return (html, true);
    }

    /// <summary>マニフェスト宣言の画面一覧を、利用可能画面の説明文字列にまとめる</summary>
    private string DescribeKnownScreens()
    {
        var files = _store
            .Manifest.Screens.Select(s => s.File)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        return files.Count == 0 ? "(none)" : string.Join(", ", files);
    }

    /// <summary>save_screen 引数の transitions 配列を <see cref="MockTransition"/>（From＝当該画面）へ変換する</summary>
    private static IReadOnlyList<MockTransition> ParseTransitions(JsonElement root, string file)
    {
        if (
            !root.TryGetProperty("transitions", out var transitionsElement)
            || transitionsElement.ValueKind != JsonValueKind.Array
        )
        {
            return Array.Empty<MockTransition>();
        }

        var transitions = new List<MockTransition>();

        foreach (var element in transitionsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            transitions.Add(
                new MockTransition
                {
                    From = file,
                    To = GetString(element, "to"),
                    Trigger = GetString(element, "trigger"),
                }
            );
        }

        return transitions;
    }

    /// <summary>受領文言へ、警告があれば「Warnings:」見出しで英語警告を改行連結する</summary>
    private static string AppendWarnings(string baseText, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return baseText;
        }

        var builder = new StringBuilder(baseText);
        builder.AppendLine();
        builder.Append("Warnings:");

        foreach (var warning in warnings)
        {
            builder.AppendLine();
            builder.Append("- ");
            builder.Append(warning);
        }

        return builder.ToString();
    }

    /// <summary>JSON オブジェクトから文字列プロパティを取り出す（未設定・非文字列は空文字）</summary>
    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
