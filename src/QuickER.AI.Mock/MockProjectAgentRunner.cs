using System.IO;
using System.Text;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>ヘッドレスで <c>dotnet build</c> を実行し、その成否と出力を返すビルド検証器の抽象</summary>
/// <remarks>
/// エージェント（Claude Code 等）の自己申告だけを信じず、最終ビルドを独立に検証するために使う。
/// 実処理は <see cref="DotnetBuildRunner"/> が担い、単体テストではフェイクへ差し替える。
/// </remarks>
public interface IBuildRunner
{
    /// <summary>指定フォルダで <c>dotnet build</c> を実行し、成否と結合出力（stdout+stderr）を返す</summary>
    /// <param name="workingDirectory">ビルド対象フォルダ（プロジェクトを含む）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<BuildRunResult> BuildAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default
    );

    /// <summary>dotnet SDK が利用可能か（<c>dotnet --version</c> の成否で判定）</summary>
    Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>ビルド実行結果</summary>
/// <param name="Success">ビルドが成功したか（終了コード 0）</param>
/// <param name="Output">結合出力（ログ保全・診断用）</param>
public readonly record struct BuildRunResult(bool Success, string Output);

/// <summary>WPF モックプロジェクト生成の実行結果</summary>
/// <param name="Success">全体として成功したか（エージェント成功・成果物存在・最終ビルド成功のすべてを満たす）</param>
/// <param name="ClientSucceeded">エージェント（バックエンド）の実行が成功で完了したか</param>
/// <param name="ArtifactsPresent">出力フォルダに csproj と xaml が存在するか（軽い成果物検証）</param>
/// <param name="BuildSucceeded">独立に実行した最終 <c>dotnet build</c> が成功したか</param>
/// <param name="TimedOut">全体タイムアウトで打ち切られたか</param>
/// <param name="Canceled">利用者が中断したか</param>
/// <param name="Message">結果メッセージ（成功理由・失敗理由）</param>
/// <param name="LogPath">実行ログを書き出したパス</param>
public sealed record MockProjectAgentResult(
    bool Success,
    bool ClientSucceeded,
    bool ArtifactsPresent,
    bool BuildSucceeded,
    bool TimedOut,
    bool Canceled,
    string Message,
    string LogPath
);

/// <summary>
/// スキャフォールド済みの出力フォルダに対し、<see cref="IMockProjectAgent"/>（バックエンド）へ UI 層生成を依頼し、
/// 全体タイムアウト・成果物検証・独立ビルド・ログ保全・結果メッセージ生成をバックエンド非依存に束ねる
/// 共有オーケストレーター。
/// </summary>
/// <remarks>
/// <para>
/// データ層（Entity/EditModel/Mapper/InMemory 等）はスキャフォールドが決定的に生成済みで、エージェントには
/// 書かせない。エージェントの UI 層生成が終わったら、その自己申告を信じずに成果物（csproj/xaml）の存在と
/// 最終 <c>dotnet build</c>（<see cref="IBuildRunner"/>）で独立に検証する。
/// </para>
/// <para>
/// 進捗テキストは <c>onProgress</c> で逐次転送し、全体タイムアウト（<see cref="DefaultTimeout"/>）と
/// 明示的な中断（<see cref="InterruptAsync"/>）に対応する。実行ログ全文は成功・失敗を問わず
/// <c>quickr-mock-generation.log</c> へ書き出す。
/// </para>
/// <para>
/// バックエンドの差異（Claude Code / Codex / API キー等）は <see cref="IMockProjectAgent"/> の実装が吸収する。
/// </para>
/// </remarks>
public sealed class MockProjectAgentRunner
{
    /// <summary>ログファイル名（出力フォルダ直下）</summary>
    public const string LogFileName = "quickr-mock-generation.log";

    /// <summary>全体タイムアウトの既定（30 分）</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly IMockProjectAgent _agent;
    private readonly IBuildRunner _buildRunner;
    private readonly TimeSpan _timeout;
    private readonly MockProjectTargetProfile _profile;

    /// <summary>実行中ターンのキャンセル起点（中断・タイムアウトで発火）</summary>
    private CancellationTokenSource? _runCts;

    /// <summary>依存を注入して生成する</summary>
    /// <param name="agent">UI 層を書くエージェント（バックエンド）</param>
    /// <param name="buildRunner">最終ビルド検証器</param>
    /// <param name="timeout">全体タイムアウト（省略時は <see cref="DefaultTimeout"/>）</param>
    /// <param name="profile">生成ターゲットのプロファイル（要求へ添える・UI 成果物の検索パターン。省略時は WPF）</param>
    public MockProjectAgentRunner(
        IMockProjectAgent agent,
        IBuildRunner buildRunner,
        TimeSpan? timeout = null,
        MockProjectTargetProfile? profile = null
    )
    {
        _agent = agent;
        _buildRunner = buildRunner;
        _timeout = timeout ?? DefaultTimeout;
        _profile = profile ?? MockProjectTargetProfile.Wpf;
    }

    /// <summary>エージェント（バックエンド）が利用可能か（Claude Code なら claude CLI の解決可否）</summary>
    public bool IsClaudeAvailable() => _agent.IsAvailable();

    /// <summary>dotnet SDK が利用可能か</summary>
    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        _buildRunner.IsDotnetAvailableAsync(cancellationToken);

    /// <summary>
    /// 出力フォルダに対してエージェントを実行し、UI 層を生成させて最終ビルドを検証する。
    /// </summary>
    /// <param name="outputDirectory">スキャフォールド済みの出力フォルダ（cwd になる）</param>
    /// <param name="projectName">プロジェクト名（プロンプトの案内に使う）</param>
    /// <param name="additionalInstructions">実装に対する追加指示（空／null なら付与しない）</param>
    /// <param name="model">モデルエイリアス（空なら既定）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">外部キャンセルトークン</param>
    /// <remarks>モデルプロバイダー指定なし（既定＝空）で <see cref="RunAsync(string, string, string?, string, string, Action{string}, CancellationToken)"/> へ委譲する。</remarks>
    public Task<MockProjectAgentResult> RunAsync(
        string outputDirectory,
        string projectName,
        string? additionalInstructions,
        string model,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    ) =>
        RunAsync(
            outputDirectory,
            projectName,
            additionalInstructions,
            model,
            modelProvider: string.Empty,
            onProgress,
            cancellationToken
        );

    /// <summary>
    /// 出力フォルダに対してエージェントを実行し、UI 層を生成させて最終ビルドを検証する（モデルプロバイダー指定あり）。
    /// </summary>
    /// <param name="outputDirectory">スキャフォールド済みの出力フォルダ（cwd になる）</param>
    /// <param name="projectName">プロジェクト名（プロンプトの案内に使う）</param>
    /// <param name="additionalInstructions">実装に対する追加指示（空／null なら付与しない）</param>
    /// <param name="model">モデルエイリアス（空なら既定）</param>
    /// <param name="modelProvider">モデルプロバイダー（Codex 用。空なら既定。Claude Code バックエンドは無視する）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">外部キャンセルトークン</param>
    public async Task<MockProjectAgentResult> RunAsync(
        string outputDirectory,
        string projectName,
        string? additionalInstructions,
        string model,
        string modelProvider,
        Action<string> onProgress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(onProgress);

        var log = new StringBuilder();
        var logPath = Path.Combine(outputDirectory, LogFileName);

        // 進捗はログへも蓄積しつつ UI へ転送する
        void Emit(string text)
        {
            log.Append(text);
            onProgress(text);
        }

        void EmitLine(string line)
        {
            log.Append(line).Append('\n');
            onProgress(line + "\n");
        }

        EmitLine(Strings.Mock_Run_Start);
        EmitLine(string.Format(Strings.Mock_Run_OutputFolderFormat, outputDirectory));
        EmitLine(string.Format(Strings.Mock_Run_ProjectNameFormat, projectName));

        var request = new MockProjectAgentRequest(
            WorkingDirectory: outputDirectory,
            ProjectName: projectName,
            AdditionalInstructions: additionalInstructions,
            Model: model,
            Profile: _profile,
            ModelProvider: modelProvider
        );

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCts.CancelAfter(_timeout);
        var token = _runCts.Token;

        MockProjectAgentOutcome outcome;
        var timedOut = false;
        var canceled = false;

        try
        {
            outcome = await _agent.RunAsync(request, Emit, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 外部キャンセルとタイムアウトを区別する（外部トークンが立っていなければタイムアウト）
            timedOut = !cancellationToken.IsCancellationRequested;
            canceled = cancellationToken.IsCancellationRequested;
            outcome = new MockProjectAgentOutcome(false, null, false);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }

        if (timedOut)
        {
            EmitLine(
                string.Format(Strings.Mock_Run_TimedOutFormat, _timeout.TotalMinutes.ToString("0"))
            );
        }
        else if (canceled)
        {
            EmitLine(Strings.Mock_Run_Canceled);
        }
        else if (outcome.Success)
        {
            EmitLine(Strings.Mock_Run_ClientCompleted);
        }
        else
        {
            EmitLine(
                string.Format(
                    Strings.Mock_Run_ClientFailedFormat,
                    outcome.Error ?? Strings.Mock_ErrorUnknown
                )
            );
        }

        // 成果物の軽い検証（csproj と UI 成果物が存在するか）
        var artifactsPresent =
            !timedOut && !canceled && HasArtifacts(outputDirectory, _profile.UiFileSearchPattern);

        if (!timedOut && !canceled)
        {
            EmitLine(
                artifactsPresent
                    ? Strings.Mock_Run_ArtifactsFound
                    : Strings.Mock_Run_ArtifactsMissing
            );
        }

        // 最終ビルドを独立に実行して検証する（エージェントの自己申告を信じない）。
        // タイムアウト・中断時、または成果物が無いときはビルドを試みない。
        var buildSucceeded = false;

        if (!timedOut && !canceled && artifactsPresent)
        {
            EmitLine(Strings.Mock_Run_BuildVerify);

            try
            {
                var buildResult = await _buildRunner
                    .BuildAsync(outputDirectory, cancellationToken)
                    .ConfigureAwait(false);
                buildSucceeded = buildResult.Success;

                if (!string.IsNullOrEmpty(buildResult.Output))
                {
                    log.Append(buildResult.Output).Append('\n');
                }

                EmitLine(
                    buildSucceeded ? Strings.Mock_Run_BuildSucceeded : Strings.Mock_Run_BuildFailed
                );
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                EmitLine(Strings.Mock_Run_BuildVerifyCanceled);
            }
        }

        var success =
            !timedOut && !canceled && outcome.Success && artifactsPresent && buildSucceeded;

        var message = BuildResultMessage(
            success,
            timedOut,
            canceled,
            outcome,
            artifactsPresent,
            buildSucceeded
        );
        EmitLine("\n" + message);

        WriteLog(logPath, log.ToString());

        return new MockProjectAgentResult(
            Success: success,
            ClientSucceeded: outcome.Success,
            ArtifactsPresent: artifactsPresent,
            BuildSucceeded: buildSucceeded,
            TimedOut: timedOut,
            Canceled: canceled,
            Message: message,
            LogPath: logPath
        );
    }

    /// <summary>実行中のターンを中断する</summary>
    public async Task InterruptAsync()
    {
        await _agent.InterruptAsync().ConfigureAwait(false);
        _runCts?.Cancel();
    }

    /// <summary>出力フォルダに csproj と UI 成果物（ターゲットの検索パターン）が存在するかを軽く検証する</summary>
    private static bool HasArtifacts(string outputDirectory, string uiFileSearchPattern)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return false;
        }

        var hasCsproj = Directory
            .EnumerateFiles(outputDirectory, "*.csproj", SearchOption.AllDirectories)
            .Any();
        var hasUiFile = Directory
            .EnumerateFiles(outputDirectory, uiFileSearchPattern, SearchOption.AllDirectories)
            .Any();

        return hasCsproj && hasUiFile;
    }

    /// <summary>結果メッセージを組み立てる</summary>
    private static string BuildResultMessage(
        bool success,
        bool timedOut,
        bool canceled,
        MockProjectAgentOutcome outcome,
        bool artifactsPresent,
        bool buildSucceeded
    )
    {
        if (success)
        {
            return Strings.Mock_Result_Success;
        }

        if (timedOut)
        {
            return Strings.Mock_Result_TimedOut;
        }

        if (canceled)
        {
            return Strings.Mock_Result_Canceled;
        }

        if (outcome.NotLoggedIn)
        {
            return Strings.Mock_Result_NotLoggedIn;
        }

        if (!outcome.Success)
        {
            return string.Format(
                Strings.Mock_Result_ClientFailedFormat,
                outcome.Error ?? Strings.Mock_ErrorUnknown
            );
        }

        if (!artifactsPresent)
        {
            return Strings.Mock_Result_ArtifactsMissing;
        }

        if (!buildSucceeded)
        {
            return Strings.Mock_Result_BuildFailed;
        }

        return Strings.Mock_Result_Failed;
    }

    /// <summary>実行ログを出力フォルダへ書き出す（成功・失敗を問わず。ベストエフォート）</summary>
    private static void WriteLog(string logPath, string content)
    {
        try
        {
            var directory = Path.GetDirectoryName(logPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                logPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
        catch (IOException)
        {
            // ログ保全の失敗は結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
            // 権限不足も無視する
        }
    }
}
