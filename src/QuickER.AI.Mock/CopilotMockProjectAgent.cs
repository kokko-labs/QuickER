using System.IO;
using QuickER.AI;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// GitHub Copilot CLI をヘッドレス実行して UI 層を書かせる <see cref="IMockProjectAgent"/> の実装。
/// </summary>
/// <remarks>
/// <para>
/// Claude Code 版（<see cref="ClaudeCodeMockProjectAgent"/>）・Codex 版（<see cref="CodexMockProjectAgent"/>）と
/// 同じデザイン仕様（design/mock/ のモックフォルダ）・同じシステムプロンプト／初回プロンプト
/// （<see cref="MockProjectPromptBuilder"/> を共有）で UI 層を生成させる自己修正ありのエージェント型。
/// ER 設計ツールは公開せず、Copilot 組込みのファイル編集・シェル実行に任せる
/// （<see cref="CopilotSessionOptions.AllowWorkspaceTools"/>＝出力フォルダ配下だけを自動承認）。
/// </para>
/// <para>
/// 全体タイムアウト・成果物検証・独立ビルド・ログ保全は共有オーケストレーター
/// （<see cref="MockProjectAgentRunner"/>）が担う。キャンセル（タイムアウト・中断）は実行中ターンを
/// 中断しつつ <see cref="OperationCanceledException"/> を伝播させ、タイムアウトと中断の区別は
/// 呼び出し側へ委ねる。ランタイムクライアント（＝copilot 子プロセス）は 1 実行の終わりに破棄する（使い捨て）。
/// </para>
/// </remarks>
public sealed class CopilotMockProjectAgent : IMockProjectAgent
{
    private readonly ICopilotRuntimeClient _client;

    /// <summary>Copilot ランタイムクライアントを注入して生成する</summary>
    /// <param name="client">Copilot ランタイムクライアント（本番は使い捨てインスタンス・テストはフェイク）</param>
    public CopilotMockProjectAgent(ICopilotRuntimeClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    /// <remarks>
    /// copilot CLI が PATH で解決できるかで判定する（ランタイムは起動しない）。走査はクライアント実装が
    /// 共有ロケーター <see cref="CopilotCliLocator"/>（チャット側の存在検出と同一）へ委譲する。
    /// </remarks>
    public bool IsAvailable() => _client.IsAvailable();

    /// <inheritdoc />
    public async Task<MockProjectAgentOutcome> RunAsync(
        MockProjectAgentRequest request,
        Action<string> onProgress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onProgress);

        // 各ターンの完了通知を待つための完了ソース（ターンごとに作り直す。保険のナッジ用の追加ターンにも使い回す）
        TaskCompletionSource<MockProjectAgentOutcome>? turnCompletion = null;

        // 進捗（アシスタント差分・組込みツールの実行開始）を onProgress へ流す
        void OnDelta(object? sender, string delta) => onProgress(delta);
        void OnToolStarted(object? sender, string toolName) => onProgress($"\n· {toolName}\n");

        // 許可範囲外の要求を拒否したことも進捗へ残す（なぜ止まったかを後から追えるようにする）
        void OnPermissionDeclined(object? sender, string description) =>
            onProgress($"\n· {description} (declined)\n");

        // アイドル復帰＝ターン完了。中断はキャンセルとして伝播させる（区別は呼び出し側の外部トークン判定に委ねる）
        void OnIdle(object? sender, bool aborted)
        {
            if (aborted)
            {
                turnCompletion?.TrySetCanceled(CancellationToken.None);
            }
            else
            {
                turnCompletion?.TrySetResult(new MockProjectAgentOutcome(true, null, false));
            }
        }

        // セッションエラーは実行中ターンの失敗として写す
        void OnError(object? sender, string message) =>
            turnCompletion?.TrySetResult(
                new MockProjectAgentOutcome(
                    false,
                    string.IsNullOrWhiteSpace(message) ? Strings.Mock_ErrorUnknown : message,
                    NotLoggedIn: false
                )
            );

        _client.AssistantDeltaReceived += OnDelta;
        _client.ToolExecutionStarted += OnToolStarted;
        _client.PermissionDeclined += OnPermissionDeclined;
        _client.SessionIdle += OnIdle;
        _client.SessionErrorReceived += OnError;

        // キャンセル（タイムアウト・中断）で実行中ターンを中断し、OperationCanceledException を伝播させる
        using var registration = cancellationToken.Register(() =>
        {
            _ = _client.AbortAsync(CancellationToken.None);
            turnCompletion?.TrySetCanceled(cancellationToken);
        });

        // 1 ターンを送信し、その完了通知を待って結果を返すローカル手続き（初回・ナッジで共用）
        async Task<MockProjectAgentOutcome> RunTurnAndWaitAsync(string turnPrompt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // SendAsync より前に完了ソースを差し替える（完了通知の取りこぼしを防ぐ）
            turnCompletion = new TaskCompletionSource<MockProjectAgentOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await _client
                .SendAsync(turnPrompt, Array.Empty<ChatAttachment>(), cancellationToken)
                .ConfigureAwait(false);

            return await turnCompletion.Task.ConfigureAwait(false);
        }

        try
        {
            // 1) ランタイム起動（copilot 子プロセス）
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);

            // 2) 認証確認。未ログインは NotLoggedIn として失敗を返す（セッションは張らない）
            var auth = await _client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);

            if (!auth.IsAuthenticated)
            {
                return new MockProjectAgentOutcome(
                    false,
                    Strings.Mock_CopilotNotLoggedIn,
                    NotLoggedIn: true
                );
            }

            // 3) セッション開始（作業フォルダ＝出力先・組込みツール許可・システムプロンプトは共有ヘルパ由来）
            await _client
                .StartSessionAsync(BuildSessionOptions(request), cancellationToken)
                .ConfigureAwait(false);

            // 4) 初回プロンプトを送信し、完了通知を待つ（他バックエンドと同一本文）。
            //    キャンセルは登録済みハンドラが OCE を伝播させる
            var prompt = MockProjectPromptBuilder.BuildPrompt(
                request.Profile,
                request.ProjectName,
                request.AdditionalInstructions
            );
            var outcome = await RunTurnAndWaitAsync(prompt).ConfigureAwait(false);

            // 5) 保険: ターンが成功で完了したのに UI 層（*.xaml / *.razor）が 1 つも無いときは、承認待ちで止まった
            //    （計画提示だけで終わった）疑いが濃いため、同一セッションへ 1 回だけ続行を促す（Codex 版と同じ）。
            //    無限ループを避けるため 2 回目以降は促さない（失敗ターン・キャンセルでもここへは来ない）。
            if (
                outcome.Success
                && !HasAnyUiFile(request.WorkingDirectory, request.Profile.UiFileSearchPattern)
            )
            {
                onProgress(Strings.Mock_AutoContinueNotice);
                outcome = await RunTurnAndWaitAsync(MockProjectPromptBuilder.ContinuationNudge)
                    .ConfigureAwait(false);
            }

            return outcome;
        }
        finally
        {
            _client.AssistantDeltaReceived -= OnDelta;
            _client.ToolExecutionStarted -= OnToolStarted;
            _client.PermissionDeclined -= OnPermissionDeclined;
            _client.SessionIdle -= OnIdle;
            _client.SessionErrorReceived -= OnError;

            // クライアント（＝子プロセス）は 1 実行ごとに破棄する（使い捨て・リーク防止）
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task InterruptAsync()
    {
        try
        {
            await _client.AbortAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 中断要求の失敗は握りつぶす（本線のキャンセルは呼び出し側のトークンで伝わる）
        }
    }

    /// <summary>
    /// セッション生成オプションを組み立てる（ER 設計ツールは公開せず、組込みのファイル編集・シェル実行を許可する）。
    /// </summary>
    /// <remarks>
    /// <see cref="CopilotSessionOptions.WorkingDirectory"/> は出力フォルダで、許可要求の自動承認範囲の基準にもなる。
    /// システムプロンプト相当は Copilot のシステムメッセージへの追記として渡す。
    /// </remarks>
    internal static CopilotSessionOptions BuildSessionOptions(MockProjectAgentRequest request) =>
        new()
        {
            Model = request.Model ?? string.Empty,
            WorkingDirectory = request.WorkingDirectory,
            AllowWorkspaceTools = true,
            Instructions = MockProjectPromptBuilder.BuildSystemPrompt(
                request.Profile,
                request.ProjectName
            ),
        };

    /// <summary>作業フォルダ配下に UI 成果物ファイルが 1 つでも存在するかを判定する（承認待ちで止まった兆候の検出用）</summary>
    /// <remarks>
    /// スキャフォールドはデータ層のみ（UI 層は生成しない）ため、成功ターン後に UI 成果物（WPF なら *.xaml）が
    /// 皆無なら「計画提示だけで終わった（実装未着手）」疑いが濃い。検索パターンはターゲットのプロファイルが与える。
    /// </remarks>
    private static bool HasAnyUiFile(string workingDirectory, string uiFileSearchPattern)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(workingDirectory, uiFileSearchPattern, SearchOption.AllDirectories)
            .Any();
    }
}
