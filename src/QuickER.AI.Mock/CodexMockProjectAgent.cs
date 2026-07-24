using System.IO;
using QuickER.AI;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// Codex App Server をヘッドレス実行して WPF の UI 層を書かせる <see cref="IMockProjectAgent"/> の実装。
/// </summary>
/// <remarks>
/// <para>
/// Claude Code 版（<see cref="ClaudeCodeMockProjectAgent"/>）と同じデザイン仕様（design/mock/ のモックフォルダ）・
/// 同じシステムプロンプト／初回プロンプト（<see cref="MockProjectPromptBuilder"/> を共有）で UI 層を生成させる。
/// dynamicTools は登録せず、Codex ネイティブのファイル編集・コマンド実行に任せる（承認は never・サンドボックスは
/// workspace-write）。システムプロンプト相当は Codex の developer instructions として渡す。
/// </para>
/// <para>
/// 全体タイムアウト・成果物検証・独立ビルド・ログ保全は共有オーケストレーター（<see cref="MockProjectAgentRunner"/>）が
/// 担う。キャンセル（タイムアウト・中断）は実行中ターンを中断しつつ <see cref="OperationCanceledException"/> を伝播させ、
/// タイムアウトと中断の区別は呼び出し側へ委ねる。App Server クライアントは 1 実行の終わりに破棄する（使い捨て）。
/// </para>
/// </remarks>
public sealed class CodexMockProjectAgent : IMockProjectAgent
{
    private const string ClientName = "erdesigner";
    private const string ClientTitle = "QuickER";
    private const string ClientVersion = "1.0.0";

    /// <summary>承認ポリシー（ヘッドレス＝一切確認せず自動で進める）</summary>
    private const string ApprovalPolicyNever = "never";

    /// <summary>サンドボックス設定（作業フォルダ内の書き込みを許可）</summary>
    private const string SandboxWorkspaceWrite = "workspace-write";

    /// <summary>認証が要るのは openai プロバイダーのみ（<see cref="CodexChatEngine"/> と同じ規則）</summary>
    private const string OpenAiProviderName = "openai";

    private readonly ICodexAppServerClient _client;

    /// <summary>実行中スレッド・ターンの識別子（中断で <see cref="ICodexAppServerClient.InterruptTurnAsync"/> に渡す）</summary>
    private string? _currentThreadId;
    private string? _currentTurnId;

    /// <summary>Codex App Server クライアントを注入して生成する</summary>
    /// <param name="client">Codex App Server クライアント（本番は使い捨てインスタンス・テストはフェイク）</param>
    public CodexMockProjectAgent(ICodexAppServerClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    /// <remarks>codex CLI が PATH で解決できるかで判定する（App Server は起動しない）。</remarks>
    public bool IsAvailable() => ResolveCodexExecutablePath() is not null;

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

        // 進捗（アシスタント差分・コマンド実行／ファイル変更の項目情報）を onProgress へ流す
        void OnDelta(object? sender, CodexAgentMessageDeltaNotification e) => onProgress(e.Delta);
        void OnItemStarted(object? sender, CodexItemStartedNotification e) =>
            EmitItemSummary(onProgress, e.ItemType);

        // ターン完了通知を「現在のターンの完了ソース」へ写像する（完了・失敗・中断）
        void OnTurnCompleted(object? sender, CodexTurnCompletedNotification e)
        {
            var turn = e.Turn;

            if (turn.Status == "interrupted")
            {
                // 中断はキャンセルとして伝播させる（区別は呼び出し側の外部トークン判定に委ねる）
                turnCompletion?.TrySetCanceled(CancellationToken.None);
            }
            else if (turn.Status == "failed")
            {
                turnCompletion?.TrySetResult(
                    new MockProjectAgentOutcome(
                        false,
                        string.IsNullOrWhiteSpace(turn.Error)
                            ? Strings.Mock_ErrorUnknown
                            : turn.Error,
                        NotLoggedIn: false
                    )
                );
            }
            else
            {
                turnCompletion?.TrySetResult(new MockProjectAgentOutcome(true, null, false));
            }
        }

        _client.AgentMessageDeltaReceived += OnDelta;
        _client.ItemStarted += OnItemStarted;
        _client.TurnCompleted += OnTurnCompleted;

        // キャンセル（タイムアウト・中断）で実行中ターンを中断し、OperationCanceledException を伝播させる
        using var registration = cancellationToken.Register(() =>
        {
            InterruptCurrentTurn();
            turnCompletion?.TrySetCanceled(cancellationToken);
        });

        // 1 ターンを送信し、その完了通知を待って結果を返すローカル手続き（初回・ナッジで共用）
        async Task<MockProjectAgentOutcome> RunTurnAndWaitAsync(string threadId, string turnPrompt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // StartTurnAsync より前に完了ソースを差し替える（完了通知の取りこぼしを防ぐ）
            turnCompletion = new TaskCompletionSource<MockProjectAgentOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var turn = await _client
                .StartTurnAsync(threadId, turnPrompt, cancellationToken)
                .ConfigureAwait(false);
            _currentTurnId = turn.Id;

            return await turnCompletion.Task.ConfigureAwait(false);
        }

        try
        {
            // 1) App Server 起動（未起動時のみ実際に起動する）
            await _client
                .StartAsync(
                    BuildSettings(request),
                    ClientName,
                    ClientTitle,
                    ClientVersion,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // 2) 認証確認（openai プロバイダーのみ必要）。未ログインは NotLoggedIn として失敗を返す
            if (
                await IsNotLoggedInAsync(request.ModelProvider, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                return new MockProjectAgentOutcome(
                    false,
                    Strings.Mock_CodexNotLoggedIn,
                    NotLoggedIn: true
                );
            }

            // 3) スレッド開始（cwd・approval=never・sandbox=workspace-write・システムプロンプト＝developer instructions）
            var thread = await _client
                .StartThreadAsync(BuildThreadStartOptions(request), cancellationToken)
                .ConfigureAwait(false);
            _currentThreadId = thread.Id;

            // 4) 初回プロンプトを送信し、完了通知を待つ（Claude Code 版と同一本文を共有）。
            //    キャンセルは登録済みハンドラが OCE を伝播させる
            var prompt = MockProjectPromptBuilder.BuildPrompt(
                request.Profile,
                request.ProjectName,
                request.AdditionalInstructions
            );
            var outcome = await RunTurnAndWaitAsync(thread.Id, prompt).ConfigureAwait(false);

            // 5) 保険: ターンが成功で完了したのに UI 層（*.xaml 等）が 1 つも無いときは、承認待ちで止まった
            //    （計画提示だけで終わった）疑いが濃いため、同一スレッドへ 1 回だけ続行を促す。
            //    無限ループを避けるため 2 回目以降は促さない（失敗ターン・キャンセルでもここへは来ない）。
            if (
                outcome.Success
                && !HasAnyUiFile(request.WorkingDirectory, request.Profile.UiFileSearchPattern)
            )
            {
                onProgress(Strings.Mock_Codex_AutoContinueNotice);
                outcome = await RunTurnAndWaitAsync(
                        thread.Id,
                        MockProjectPromptBuilder.CodexContinuationNudge
                    )
                    .ConfigureAwait(false);
            }

            return outcome;
        }
        finally
        {
            _client.AgentMessageDeltaReceived -= OnDelta;
            _client.ItemStarted -= OnItemStarted;
            _client.TurnCompleted -= OnTurnCompleted;
            _currentThreadId = null;
            _currentTurnId = null;

            // クライアント（＝子プロセス）は 1 実行ごとに破棄する（使い捨て・リーク防止）
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task InterruptAsync()
    {
        if (_currentThreadId is not null && _currentTurnId is not null)
        {
            try
            {
                await _client
                    .InterruptTurnAsync(_currentThreadId, _currentTurnId)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 中断要求の失敗は握りつぶす（本線のキャンセルは呼び出し側のトークンで伝わる）
            }
        }
    }

    /// <summary>実行中ターンをベストエフォートで中断する（キャンセルハンドラから同期的に呼ぶ）</summary>
    private void InterruptCurrentTurn()
    {
        if (_currentThreadId is not null && _currentTurnId is not null)
        {
            _ = _client.InterruptTurnAsync(
                _currentThreadId,
                _currentTurnId,
                CancellationToken.None
            );
        }
    }

    /// <summary>スレッド開始オプションを組み立てる（dynamicTools なし・システムプロンプトは developer instructions）</summary>
    private static CodexThreadStartOptions BuildThreadStartOptions(
        MockProjectAgentRequest request
    ) =>
        new()
        {
            Cwd = request.WorkingDirectory,
            ApprovalPolicy = ApprovalPolicyNever,
            Sandbox = SandboxWorkspaceWrite,
            ModelProvider = NormalizeOptional(request.ModelProvider),
            Model = NormalizeOptional(request.Model),
            DeveloperInstructions = MockProjectPromptBuilder.BuildSystemPrompt(
                request.Profile,
                request.ProjectName
            ),
        };

    /// <summary>プロバイダー・モデルから App Server 起動設定を組み立てる</summary>
    private static CodexAppServerSettings BuildSettings(MockProjectAgentRequest request) =>
        new()
        {
            ModelProvider = request.ModelProvider?.Trim() ?? string.Empty,
            Model = request.Model?.Trim() ?? string.Empty,
        };

    /// <summary>選択プロバイダーで認証が必要かつ未ログインかを判定する（openai 以外は認証不要）</summary>
    private async Task<bool> IsNotLoggedInAsync(
        string? modelProvider,
        CancellationToken cancellationToken
    )
    {
        if (!IsOpenAiProvider(modelProvider))
        {
            return false;
        }

        var account = await _client
            .ReadAccountAsync(refreshToken: true, cancellationToken)
            .ConfigureAwait(false);
        return account.RequiresOpenAiAuth && !account.IsLoggedIn;
    }

    /// <summary>作業フォルダ配下に UI 成果物ファイルが 1 つでも存在するかを判定する（承認待ちで止まった兆候の検出用）</summary>
    /// <remarks>
    /// スキャフォールドはデータ層のみ（UI 層は生成しない）ため、成功ターン後に UI 成果物（WPF なら *.xaml）が
    /// 皆無なら「計画提示だけで終わった（実装未着手）」疑いが濃い。検索パターンはターゲットのプロファイルが与える
    /// （design/mock/ 配下は HTML なので WPF の *.xaml では誤検知しない）。
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

    /// <summary>コマンド実行・ファイル変更の項目開始を 1 行で要約して通知する</summary>
    private static void EmitItemSummary(Action<string> onProgress, string? itemType)
    {
        if (itemType is "commandExecution" or "fileChange")
        {
            onProgress($"\n· {itemType}\n");
        }
    }

    /// <summary>プロバイダーが openai か（空も openai 扱い＝認証が要る唯一のプロバイダー）</summary>
    private static bool IsOpenAiProvider(string? modelProvider) =>
        string.IsNullOrWhiteSpace(modelProvider)
        || modelProvider.Trim().Equals(OpenAiProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>空白を null へ正規化する（未指定なら codex の既定に委ねる）</summary>
    private static string? NormalizeOptional(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>PATH から codex 実行ファイルを解決する（見つからなければ null）</summary>
    private static string? ResolveCodexExecutablePath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? ["codex.exe", "codex.cmd", "codex.bat", "codex"]
            : ["codex"];

        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                string fullPath;

                try
                {
                    fullPath = Path.Combine(directory.Trim(), candidate);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
