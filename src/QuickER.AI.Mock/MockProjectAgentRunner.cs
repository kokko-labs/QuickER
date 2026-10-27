using System.IO;
using System.Text;
using QuickER.AI.Mock.Resources;
using QuickER.Platform;

namespace QuickER.AI.Mock;

/// <summary>ヘッドレスで <c>dotnet build</c> を実行し、その成否と出力を返すビルド検証器の抽象</summary>
/// <remarks>
/// エージェント（Claude Code 等）の自己申告だけを信じず、最終ビルドを独立に検証するために使う。
/// 実処理は <see cref="DotnetBuildRunner"/> が担い、単体テストではフェイクへ差し替える。
/// </remarks>
public interface IBuildRunner
{
    /// <summary>指定したソリューションを <c>dotnet build</c> でビルドし、成否と結合出力（stdout+stderr）を返す</summary>
    /// <param name="solutionFilePath">
    /// ビルド対象のソリューション（<c>{出力フォルダ}/{プロジェクト名}.sln</c>）のフルパス。
    /// フォルダ指定でなくパスを明示するのは、出力フォルダに別のソリューションがあると
    /// フォルダ指定のビルドが MSB1011（対象が一意に決まらない）で落ちるため。
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<BuildRunResult> BuildAsync(
        string solutionFilePath,
        CancellationToken cancellationToken = default
    );

    /// <summary>dotnet SDK が利用可能か（<c>dotnet --version</c> の成否で判定）</summary>
    Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>ビルド実行結果</summary>
/// <param name="Success">ビルドが成功したか（終了コード 0）</param>
/// <param name="Output">結合出力（ログ保全・診断用）</param>
public readonly record struct BuildRunResult(bool Success, string Output);

/// <summary>モックプロジェクト生成（実行器の実行と最終ビルド検証）の実行結果</summary>
/// <param name="Success">全体として成功したか（エージェント成功・成果物存在・最終ビルド成功のすべてを満たす）</param>
/// <param name="ClientSucceeded">エージェント（バックエンド）の実行が成功で完了したか</param>
/// <param name="ArtifactsPresent">プロジェクトフォルダに csproj と UI 成果物（WPF は .xaml・Blazor は .razor）が存在するか（軽い成果物検証）</param>
/// <param name="BuildSucceeded">独立に実行した最終 <c>dotnet build</c> が成功したか</param>
/// <param name="TimedOut">全体タイムアウトで打ち切られたか</param>
/// <param name="Canceled">利用者が中断したか</param>
/// <param name="Message">結果メッセージ（成功理由・失敗理由）</param>
/// <param name="LogPath">実行ログを書き出したパス</param>
/// <param name="BuildDeclined">
/// 最終ビルド直前の確認を利用者が取り消したため、ビルドを実行しなかったか（＝成果物は未検証のまま完了）
/// </param>
/// <param name="ScreensExpected">設計（同梱した <c>mock.json</c>）の画面の数（読めなければ null）</param>
/// <param name="ScreenArtifactsFound">画面として数えた UI 成果物の数（数えなかったときは null）</param>
/// <param name="ScreensMissing">
/// 画面として数えた成果物が設計の画面の数に届かず、ターゲットの宣言（<see cref="MockProjectTargetProfile.ScreenShortfallFailsRun"/>）
/// により全体を失敗にしたか
/// </param>
public sealed record MockProjectAgentResult(
    bool Success,
    bool ClientSucceeded,
    bool ArtifactsPresent,
    bool BuildSucceeded,
    bool TimedOut,
    bool Canceled,
    string Message,
    string LogPath,
    bool BuildDeclined = false,
    int? ScreensExpected = null,
    int? ScreenArtifactsFound = null,
    bool ScreensMissing = false
);

/// <summary>
/// スキャフォールド済みの出力フォルダに対し、<see cref="IMockProjectAgent"/>（バックエンド）へ UI 層生成を依頼し、
/// 全体タイムアウト・成果物検証・独立ビルド・ログ保全・結果メッセージ生成をバックエンド非依存に束ねる
/// 共有オーケストレーター。
/// </summary>
/// <remarks>
/// <para>
/// データ層（Entity/EditModel/Mapper/InMemory 等）はスキャフォールドが決定的に生成済みで、エージェントには
/// 書かせない。エージェントの UI 層生成が終わったら、その自己申告を信じずに成果物（csproj と UI 成果物）の存在と
/// 最終 <c>dotnet build</c>（<see cref="IBuildRunner"/>）で独立に検証する。
/// </para>
/// <para>
/// 進捗テキストは <c>onProgress</c> で逐次転送し、全体タイムアウト（<see cref="DefaultTimeout"/>）と
/// 明示的な中断（<see cref="InterruptAsync"/>）に対応する。実行ログ全文は成功・失敗を問わず
/// <c>quickr-mock-generation.log</c> へ書き出す。
/// </para>
/// <para>
/// バックエンドの差異（Claude Code / Codex / Copilot / API キー）は <see cref="IMockProjectAgent"/> の実装が吸収する。
/// </para>
/// </remarks>
public sealed class MockProjectAgentRunner
{
    /// <summary>ログファイル名（出力フォルダ直下）</summary>
    public const string LogFileName = "quickr-mock-generation.log";

    /// <summary>エージェント実行のタイムアウトの既定（30 分）</summary>
    /// <remarks>
    /// 掛かるのは実行器（Claude Code / Codex / Copilot / API キー）が UI を書いている間だけで、
    /// そのあとの最終ビルドは <see cref="DefaultBuildTimeout"/> が別に持つ。
    /// したがってランの最長は両者の和（既定で 40 分）になる——意図的に独立させている。
    /// エージェントを打ち切った時点で成果物は中途半端なので最終ビルドは走らず、
    /// 逆に「エージェントが時間ぎりぎりまで書いた」場合でも検証ビルドには
    /// 十分な時間を与えたい（残り時間で切ると、正しく書けた生成物が検証されないまま失敗扱いになる）。
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    /// <summary>最終ビルド（<c>dotnet build</c>）専用タイムアウトの既定（10 分）</summary>
    /// <remarks>
    /// 全体タイムアウト（<see cref="DefaultTimeout"/>）はエージェントの実行までしか掛からないため、
    /// 最終ビルドにも独立した上限を置く（これが無いと、応答しない <c>dotnet</c> でランが永久に終わらない）。
    /// <para>
    /// 値の根拠（実測・SDK 10.0.401 / Windows 11 26200）: スキャフォールドと同形の Blazor Web App
    /// （<c>Microsoft.NET.Sdk.Web</c>・net10.0・<c>Microsoft.Data.Sqlite</c> 参照）のソリューションを、
    /// パッケージキャッシュ（<c>NUGET_PACKAGES</c>）と HTTP キャッシュ（<c>NUGET_HTTP_CACHE_PATH</c>）を
    /// 両方とも空フォルダへ向けた「完全コールド」状態でビルドして 6.8 秒（復元込み・本実装の引数一式つき）。
    /// 10 分はその約 88 倍で、遅い回線・遅い端末・画面数の多い生成物を見込んでも十分な余裕がある一方、
    /// 「終わらないビルド」は確実に打ち切れる。
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultBuildTimeout = TimeSpan.FromMinutes(10);

    private readonly IMockProjectAgent _agent;
    private readonly IBuildRunner _buildRunner;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _buildTimeout;
    private readonly MockProjectTargetProfile _profile;
    private readonly Func<IReadOnlyList<string>, bool>? _confirmBuild;

    /// <summary>
    /// 実行中のランの中断起点（<see cref="InterruptAsync"/> が発火する）。エージェント実行と最終ビルドの
    /// 両方のトークンへ連結する。ランの外では null。
    /// </summary>
    private CancellationTokenSource? _interruptCts;

    /// <summary>
    /// <see cref="InterruptAsync"/> が呼ばれたか（外部トークンが立っていなくても中断として分類するため）。
    /// ランの開始時に下ろす。
    /// </summary>
    private volatile bool _interruptRequested;

    /// <summary>依存を注入して生成する</summary>
    /// <param name="agent">UI 層を書くエージェント（バックエンド）</param>
    /// <param name="buildRunner">最終ビルド検証器</param>
    /// <param name="timeout">全体タイムアウト（省略時は <see cref="DefaultTimeout"/>）</param>
    /// <param name="profile">生成ターゲットのプロファイル（要求へ添える・UI 成果物の検索パターン。省略時は WPF）</param>
    /// <param name="buildTimeout">最終ビルド専用タイムアウト（省略時は <see cref="DefaultBuildTimeout"/>）</param>
    /// <param name="confirmBuild">
    /// 最終ビルド直前の確認（引数＝UI 層のソース・静的資産以外で追加・変更されたファイルの相対パス）。
    /// true を返したときだけビルドを実行する。<see langword="null"/> は「確認する手段が無い」を意味し、
    /// 対象の変更があればビルドしない（未検証として完了する）＝黙って承認へ倒さない。
    /// <see cref="IMockProjectAgent.FinalBuildEscapesSandbox"/> が false の実行器では呼ばれない。
    /// </param>
    public MockProjectAgentRunner(
        IMockProjectAgent agent,
        IBuildRunner buildRunner,
        TimeSpan? timeout = null,
        MockProjectTargetProfile? profile = null,
        TimeSpan? buildTimeout = null,
        Func<IReadOnlyList<string>, bool>? confirmBuild = null
    )
    {
        _agent = agent;
        _buildRunner = buildRunner;
        _timeout = timeout ?? DefaultTimeout;
        _buildTimeout = buildTimeout ?? DefaultBuildTimeout;
        _profile = profile ?? MockProjectTargetProfile.Wpf;
        _confirmBuild = confirmBuild;
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

        // 中断の起点はランごとに作り直す（前のランの中断要求を持ち越さない）
        using var interruptCts = new CancellationTokenSource();
        _interruptRequested = false;
        _interruptCts = interruptCts;

        try
        {
            return await RunCoreAsync(
                    outputDirectory,
                    projectName,
                    additionalInstructions,
                    model,
                    modelProvider,
                    onProgress,
                    log,
                    logPath,
                    interruptCts.Token,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 想定外の例外で抜ける経路でも、そこまでの経過と原因をログへ残す（下の finally で書き出す）
            log.Append(ex).Append('\n');
            throw;
        }
        finally
        {
            _interruptCts = null;

            // 実行ログは成功・失敗・想定外の例外を問わず書き出す
            WriteLog(logPath, log.ToString());
        }
    }

    /// <summary>
    /// <see cref="RunAsync(string, string, string?, string, string, Action{string}, CancellationToken)"/> の本体
    /// （ログの書き出しと中断起点の後始末は呼び出し側が持つ）。
    /// </summary>
    private async Task<MockProjectAgentResult> RunCoreAsync(
        string outputDirectory,
        string projectName,
        string? additionalInstructions,
        string model,
        string modelProvider,
        Action<string> onProgress,
        StringBuilder log,
        string logPath,
        CancellationToken interruptToken,
        CancellationToken cancellationToken
    )
    {
        // 進捗はログへも蓄積しつつ UI へ転送する。実行器の断片が改行で終わらなくても、行は新しい行から書く
        var progress = new MockProgressWriter(text =>
        {
            log.Append(text);
            onProgress(text);
        });

        void Emit(string text) => progress.Write(text);

        void EmitLine(string line) => progress.WriteLine(line);

        // 利用者自身の中断か（外部トークン・InterruptAsync のどちらでも）
        bool IsCanceledByUser() => cancellationToken.IsCancellationRequested || _interruptRequested;

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

        // 最終ビルドが実行器にとって境界越えになるか。境界越えなのにスナップショットが無いときは
        // （記録に失敗した・記録前に打ち切られた）ビルドしない＝検知なしのビルドへ倒さない
        var boundaryRequired = _agent.FinalBuildEscapesSandbox;
        MockProjectBuildBoundary.Snapshot? boundarySnapshot = null;

        MockProjectAgentOutcome outcome;
        var timedOut = false;
        var canceled = false;

        // 最終ビルドが専用タイムアウトで打ち切られたか（結果としてはタイムアウト扱いだが文言を分ける）
        var buildTimedOut = false;

        using (
            var runCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                interruptToken
            )
        )
        {
            runCts.CancelAfter(_timeout);
            var token = runCts.Token;

            try
            {
                // スキャフォールド直後（＝ここ）の状態を記録する。記録するのは内容のハッシュで、更新日時やサイズでは
                // 比べない（書き換えたあとで日時は戻せる）。全ファイルを読むので UI スレッドから外して走らせる。
                if (boundaryRequired)
                {
                    boundarySnapshot = await Task.Run(
                            () =>
                                MockProjectBuildBoundary.Snapshot.Capture(
                                    outputDirectory,
                                    projectName,
                                    token
                                ),
                            token
                        )
                        .ConfigureAwait(false);
                }

                outcome = await _agent.RunAsync(request, Emit, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 利用者の中断とタイムアウトを区別する（外部トークンも中断要求も立っていなければタイムアウト）
                canceled = IsCanceledByUser();
                timedOut = !canceled;
                outcome = new MockProjectAgentOutcome(false, null, false);
            }
            catch (Exception ex)
            {
                // 実行器の想定外の例外は実行器の失敗として結果へ畳む（ログと結果メッセージに原因を残す）
                log.Append(ex).Append('\n');
                outcome = new MockProjectAgentOutcome(false, ex.Message, false);
            }
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
            EmitLine(string.Format(Strings.Mock_Run_ClientCompletedFormat, _agent.BackendName));
        }
        else
        {
            EmitLine(
                string.Format(
                    Strings.Mock_Run_ClientFailedFormat,
                    _agent.BackendName,
                    outcome.Error ?? Strings.Mock_ErrorUnknown
                )
            );
        }

        // 成果物の軽い検証（csproj と UI 成果物が存在するか）。
        // 探索範囲はプロジェクトフォルダ配下に限る（出力フォルダに同居する無関係な csproj で合格させない）。
        var projectDirectory = MockProjectScaffoldService.GetProjectDirectory(
            outputDirectory,
            projectName
        );
        var artifactsPresent =
            !timedOut && !canceled && HasArtifacts(projectDirectory, _profile.UiFileSearchPattern);

        if (!timedOut && !canceled)
        {
            // UI 成果物の種類はターゲットの検索パターンの拡張子で示す（WPF は .xaml・Blazor は .razor）
            var uiExtension = _profile.UiFileSearchPattern.TrimStart('*');
            EmitLine(
                string.Format(
                    artifactsPresent
                        ? Strings.Mock_Run_ArtifactsFoundFormat
                        : Strings.Mock_Run_ArtifactsMissingFormat,
                    uiExtension
                )
            );
        }

        // 画面の数の確認（設計の画面の数と、画面として数えた成果物の数を比べる）。
        // 足りないときに失敗にするかはターゲットの宣言で決め、ターゲットの名前では分岐しない
        var screensExpected = artifactsPresent ? ReadScreenCount(projectDirectory) : null;
        int? screenArtifactsFound = null;
        var screensMissing = false;

        if (screensExpected is > 0)
        {
            screenArtifactsFound = _profile.CountScreenArtifacts(projectDirectory);

            if (screenArtifactsFound < screensExpected)
            {
                screensMissing = _profile.ScreenShortfallFailsRun;
                EmitLine(
                    string.Format(
                        screensMissing
                            ? Strings.Mock_Run_ScreensMissingFormat
                            : Strings.Mock_Run_ScreensMissingWarningFormat,
                        screensExpected,
                        screenArtifactsFound
                    )
                );
            }
        }

        // 最終ビルドを独立に実行して検証する（エージェントの自己申告を信じない）。
        // タイムアウト・中断時、または成果物が無いときはビルドを試みない。
        var buildSucceeded = false;

        // 最終ビルド直前の確認を利用者が取り消したか（成果物は残るが未検証として完了する）
        var buildDeclined = false;

        // 境界越えの実行器なのにスナップショットが無い（記録が例外で失敗した）ときはビルドしない。
        // 実行器の失敗として結果に出るので、確認の取り消し（BuildDeclined）とは扱いを分ける。
        var buildBlocked = boundaryRequired && boundarySnapshot is null;

        if (!timedOut && !canceled && artifactsPresent && !buildBlocked)
        {
            // ビルド中間物を捨ててから検証する。obj/ に残った *.targets / *.props は
            // Microsoft.Common.targets のワイルドカード import で読み込まれる（実測で確認）ため、
            // AI が書いたものがこのビルドで実行され得る。前回実行の古い状態を持ち込まない意味もある。
            // 消せずに残ったものは黙って見逃さず、一覧として受け取る（フェイルクローズ）。
            //
            // 削除したあとビルドが始まるまでに、実行器の子プロセス（Codex の app-server・Copilot）が
            // 残っていれば理屈の上では書き戻せる。閉じるには実行器を破棄してから削除する順序が要るが、
            // 破棄の責務は呼び出し側（VM）にあり、ここでは持てない。Phase 3 の変更検知と同じ種類の
            // 残余として扱う（Claude Code はターン終了時にプロセスが終わっているため該当しない）。
            var residue = MockProjectBuildBoundary.ClearBuildOutputs(outputDirectory, projectName);

            // 削除の「後」に差分を取る（obj / bin は消える側なので差分の対象に含めず、残存として別に受け取る）。
            // 実行器が書いた「ビルドが設定として読み得るファイル」があれば、実行する前に利用者へ見せる。
            buildDeclined = !ConfirmBuildBoundary(
                boundarySnapshot,
                residue,
                outputDirectory,
                projectName,
                EmitLine
            );
        }

        if (!timedOut && !canceled && artifactsPresent && !buildBlocked && !buildDeclined)
        {
            EmitLine(Strings.Mock_Run_BuildVerify);

            // 最終ビルドには専用のタイムアウトを掛ける（全体タイムアウトはエージェント実行までで解けている）。
            // 中断はエージェント実行と同じ起点で効く。
            using var buildCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                interruptToken
            );
            buildCts.CancelAfter(_buildTimeout);

            try
            {
                var buildResult = await _buildRunner
                    .BuildAsync(
                        MockProjectScaffoldService.GetSolutionFilePath(
                            outputDirectory,
                            projectName
                        ),
                        buildCts.Token
                    )
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
                // 利用者の中断でなければ、打ち切ったのは最終ビルドのタイムアウト
                if (IsCanceledByUser())
                {
                    canceled = true;
                    EmitLine(Strings.Mock_Run_BuildVerifyCanceled);
                }
                else
                {
                    buildTimedOut = true;
                    EmitLine(
                        string.Format(
                            Strings.Mock_Run_BuildTimedOutFormat,
                            _buildTimeout.TotalMinutes.ToString("0")
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                // ビルドを実行できなかった（dotnet の起動失敗など）＝検証できなかったのでビルド失敗として扱う
                log.Append(ex).Append('\n');
                EmitLine(Strings.Mock_Run_BuildFailed);
            }
        }

        var success =
            !timedOut
            && !buildTimedOut
            && !canceled
            && outcome.Success
            && artifactsPresent
            && !screensMissing
            && buildSucceeded;

        var message = BuildResultMessage(
            success,
            timedOut,
            buildTimedOut,
            canceled,
            buildDeclined,
            outcome,
            artifactsPresent,
            screensMissing
                ? string.Format(
                    Strings.Mock_Result_ScreensMissingFormat,
                    screensExpected,
                    screenArtifactsFound
                )
                : null,
            buildSucceeded
        );
        EmitLine("\n" + message);

        return new MockProjectAgentResult(
            Success: success,
            ClientSucceeded: outcome.Success,
            ArtifactsPresent: artifactsPresent,
            BuildSucceeded: buildSucceeded,
            // 最終ビルドの打ち切りもタイムアウトとして報告する（利用者から見れば時間切れは 1 種類）
            TimedOut: timedOut || buildTimedOut,
            Canceled: canceled,
            Message: message,
            LogPath: logPath,
            BuildDeclined: buildDeclined,
            ScreensExpected: screensExpected,
            ScreenArtifactsFound: screenArtifactsFound,
            ScreensMissing: screensMissing
        );
    }

    /// <summary>
    /// スキャフォールドが同梱した設計（<c>design/mock/mock.json</c>）の画面の数を読む（読めなければ null＝画面の数の確認をしない）。
    /// </summary>
    private static int? ReadScreenCount(string projectDirectory)
    {
        var designFolder = Path.Combine(
            projectDirectory,
            MockProjectScaffoldService.DesignFolderRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar
            )
        );

        try
        {
            return MockFolderStore.Open(designFolder).Manifest.Screens.Count;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>実行中のランを中断する（エージェント実行中・最終ビルド中のどちらでも効く）</summary>
    /// <remarks>
    /// ランが同時に終わって中断起点が破棄されていても例外にしない。外部トークンを立てずにこれだけを
    /// 呼んだ場合も、結果は（タイムアウトでなく）中断として分類する。
    /// </remarks>
    public async Task InterruptAsync()
    {
        _interruptRequested = true;
        var interruptCts = _interruptCts;

        try
        {
            await _agent.InterruptAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                interruptCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ランが同時に終わって破棄済みなら何もしない
            }
        }
    }

    /// <summary>プロジェクトフォルダに csproj と UI 成果物（ターゲットの検索パターン）が存在するかを軽く検証する</summary>
    /// <remarks>
    /// 走査は共有ヘルパー <see cref="MockProjectFiles"/> へ委譲する（アクセス拒否で落ちない・
    /// <c>obj</c> / <c>bin</c> を数えない・範囲はプロジェクトフォルダ配下だけ）。
    /// </remarks>
    private static bool HasArtifacts(string projectDirectory, string uiFileSearchPattern) =>
        MockProjectFiles.HasAny(projectDirectory, "*.csproj")
        && MockProjectFiles.HasAny(projectDirectory, uiFileSearchPattern);

    /// <summary>
    /// 最終ビルド直前に、実行器が書いた「ビルドが設定として読み得るファイル」を検知して利用者へ確認する。
    /// </summary>
    /// <param name="snapshot">
    /// スキャフォールド直後のスナップショット（<see langword="null"/> は
    /// <see cref="IMockProjectAgent.FinalBuildEscapesSandbox"/> が false＝検知の対象外）
    /// </param>
    /// <param name="residue">
    /// 削除できずに残ったビルド中間物（<see cref="MockProjectBuildBoundary.ClearBuildOutputs"/> の戻り値）
    /// </param>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="emitLine">ログ・進捗への 1 行出力</param>
    /// <returns>最終ビルドを実行してよいか（false なら未検証で完了する）</returns>
    /// <remarks>
    /// <para>
    /// 差分も残存も無ければ何も尋ねない。どちらかがあれば一覧をログへ残したうえで確認を出し、確認する手段が
    /// 与えられていない（<c>confirmBuild</c> が null）ときは承認しない＝配線漏れが「黙って承認」へ
    /// 倒れないようにする。差分と残存は同じ表記（出力フォルダからの相対パス）なので、1 つの一覧へまとめる。
    /// </para>
    /// <para>
    /// 境界越えを宣言しない実行器（<paramref name="snapshot"/> が null）では残存をログへ残すだけで尋ねない
    /// （Claude Code は無制限のシェルを持つため最終ビルドが境界にならない・API キー方式は obj / bin を保護
    /// フォルダとして拒否する）。
    /// </para>
    /// </remarks>
    private bool ConfirmBuildBoundary(
        MockProjectBuildBoundary.Snapshot? snapshot,
        IReadOnlyList<string> residue,
        string outputDirectory,
        string projectName,
        Action<string> emitLine
    )
    {
        if (residue.Count > 0)
        {
            emitLine(Strings.Mock_Run_BuildOutputsNotDeleted);

            foreach (var path in residue)
            {
                emitLine("  " + path);
            }
        }

        if (snapshot is null)
        {
            return true;
        }

        var changed = snapshot
            .DetectUnexpectedChanges(outputDirectory, projectName)
            .Concat(residue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (changed.Count == 0)
        {
            return true;
        }

        emitLine(Strings.Mock_Run_BuildBoundaryChanges);

        foreach (var path in changed)
        {
            emitLine("  " + path);
        }

        if (_confirmBuild?.Invoke(changed) == true)
        {
            return true;
        }

        emitLine(Strings.Mock_Run_BuildDeclined);
        return false;
    }

    /// <summary>結果メッセージを組み立てる</summary>
    private static string BuildResultMessage(
        bool success,
        bool timedOut,
        bool buildTimedOut,
        bool canceled,
        bool buildDeclined,
        MockProjectAgentOutcome outcome,
        bool artifactsPresent,
        string? screensMissingMessage,
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

        if (buildTimedOut)
        {
            return Strings.Mock_Result_BuildTimedOut;
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

        if (screensMissingMessage is not null)
        {
            return screensMissingMessage;
        }

        // 確認の取り消しはエージェント自身の失敗より後に見る（実行が失敗していればそちらが原因）
        if (buildDeclined)
        {
            return Strings.Mock_Result_BuildNotVerified;
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

            AtomicFile.WriteAllText(logPath, content, TextEncodings.Utf8NoBomLenient);
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
