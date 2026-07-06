using System.IO;
using System.Text;

namespace QuickER.AI;

/// <summary>ヘッドレスで <c>dotnet build</c> を実行し、その成否と出力を返すビルド検証器の抽象</summary>
/// <remarks>
/// クライアント（Claude Code）の自己申告だけを信じず、最終ビルドを独立に検証するために使う。
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
/// <param name="Success">全体として成功したか（クライアント成功・成果物存在・最終ビルド成功のすべてを満たす）</param>
/// <param name="ClientSucceeded">Claude Code の実行が成功で完了したか</param>
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
/// 確定 HTML・決定的スキャフォールドが用意された出力フォルダに対し、Claude Code CLI をヘッドレス実行して
/// WPF の UI 層を書かせ、<c>dotnet build</c> 成功まで自己修正させるオーケストレーター。
/// </summary>
/// <remarks>
/// <para>
/// データ層（Entity/EditModel/Mapper/InMemory 等）はスキャフォールドが決定的に生成済みで、AI には書かせない。
/// AI には design/mock.html をデザイン仕様として、README-QuickER.md の規約に従い CommunityToolkit.Mvvm の
/// MVVM で UI 層を作らせる。MCP サーバー（ER ツール）は使わせず、ファイル編集・コマンド実行のみを許可する。
/// </para>
/// <para>
/// 進捗テキストは <c>onProgress</c> で逐次転送し、全体タイムアウト（<see cref="DefaultTimeout"/>）と
/// 明示的な中断（<see cref="InterruptAsync"/>）に対応する。実行ログ全文は成功・失敗を問わず
/// <c>quickr-mock-generation.log</c> へ書き出す。最終ビルドは <see cref="IBuildRunner"/> で独立に検証する。
/// </para>
/// </remarks>
public sealed class MockProjectAgentRunner
{
    /// <summary>ログファイル名（出力フォルダ直下）</summary>
    public const string LogFileName = "quickr-mock-generation.log";

    /// <summary>デザイン仕様 HTML の相対パス（スキャフォールドが配置する）</summary>
    public const string DesignHtmlRelativePath = "design/mock.html";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    /// <summary>全体タイムアウトの既定（30 分）</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    /// <summary>ヘッドレスで許可する追加ツール（ファイル編集・コマンド実行）</summary>
    private static readonly string[] HeadlessAllowedTools = ["Edit", "Write", "MultiEdit", "Bash"];

    private readonly IClaudeCodeClient _client;
    private readonly IBuildRunner _buildRunner;
    private readonly TimeSpan _timeout;

    /// <summary>実行中ターンのキャンセル起点（中断・タイムアウトで発火）</summary>
    private CancellationTokenSource? _runCts;

    /// <summary>依存を注入して生成する</summary>
    /// <param name="client">Claude Code クライアント</param>
    /// <param name="buildRunner">最終ビルド検証器</param>
    /// <param name="timeout">全体タイムアウト（省略時は <see cref="DefaultTimeout"/>）</param>
    public MockProjectAgentRunner(
        IClaudeCodeClient client,
        IBuildRunner buildRunner,
        TimeSpan? timeout = null
    )
    {
        _client = client;
        _buildRunner = buildRunner;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>claude CLI が利用可能か</summary>
    public bool IsClaudeAvailable() => _client.IsAvailable();

    /// <summary>dotnet SDK が利用可能か</summary>
    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        _buildRunner.IsDotnetAvailableAsync(cancellationToken);

    /// <summary>
    /// 出力フォルダに対して Claude Code をヘッドレス実行し、UI 層を生成させて最終ビルドを検証する。
    /// </summary>
    /// <param name="outputDirectory">スキャフォールド済みの出力フォルダ（cwd になる）</param>
    /// <param name="projectName">プロジェクト名（プロンプトの案内に使う）</param>
    /// <param name="model">Claude Code モデルエイリアス（空なら既定）</param>
    /// <param name="onProgress">進捗テキストの逐次転送先</param>
    /// <param name="cancellationToken">外部キャンセルトークン</param>
    public async Task<MockProjectAgentResult> RunAsync(
        string outputDirectory,
        string projectName,
        string model,
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

        EmitLine("== WPF モック生成を開始します ==");
        EmitLine($"出力フォルダ: {outputDirectory}");
        EmitLine($"プロジェクト名: {projectName}");

        var options = BuildLaunchOptions(model, outputDirectory);
        var prompt = BuildPrompt(projectName);

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCts.CancelAfter(_timeout);
        var token = _runCts.Token;

        ClaudeCodeTurnOutcome outcome;
        var timedOut = false;
        var canceled = false;

        try
        {
            outcome = await _client
                .RunTurnAsync(prompt, resumeSessionId: null, options, Emit, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 外部キャンセルとタイムアウトを区別する（外部トークンが立っていなければタイムアウト）
            timedOut = !cancellationToken.IsCancellationRequested;
            canceled = cancellationToken.IsCancellationRequested;
            outcome = new ClaudeCodeTurnOutcome(false, null, null, false);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }

        if (timedOut)
        {
            EmitLine($"\n== タイムアウト（{_timeout.TotalMinutes:0} 分）により打ち切りました ==");
        }
        else if (canceled)
        {
            EmitLine("\n== 中断されました ==");
        }
        else if (outcome.Success)
        {
            EmitLine("\n== Claude Code の実行が完了しました ==");
        }
        else
        {
            EmitLine(
                "\n== Claude Code の実行が失敗しました: " + (outcome.Error ?? "詳細不明") + " =="
            );
        }

        // 成果物の軽い検証（csproj と xaml が存在するか）
        var artifactsPresent = !timedOut && !canceled && HasArtifacts(outputDirectory);

        if (!timedOut && !canceled)
        {
            EmitLine(
                artifactsPresent
                    ? "成果物チェック: csproj と xaml を確認しました。"
                    : "成果物チェック: csproj または xaml が見つかりません。"
            );
        }

        // 最終ビルドを独立に実行して検証する（クライアントの自己申告を信じない）。
        // タイムアウト・中断時、または成果物が無いときはビルドを試みない。
        var buildSucceeded = false;

        if (!timedOut && !canceled && artifactsPresent)
        {
            EmitLine("\n== 最終ビルドを検証します（dotnet build） ==");

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
                    buildSucceeded
                        ? "最終ビルド: 成功しました。"
                        : "最終ビルド: 失敗しました（ログを確認してください）。"
                );
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                EmitLine("最終ビルドの検証が中断されました。");
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
    public Task InterruptAsync()
    {
        _client.Interrupt();
        _runCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>ヘッドレス（ファイル編集・コマンド実行許可）の起動オプションを組み立てる</summary>
    /// <remarks>
    /// MCP は使わない（ER ツール不要）。<c>--permission-mode acceptEdits</c> と Edit/Write/Bash の許可で、
    /// 作業フォルダ内のファイル作成・編集と dotnet build 等のコマンド実行をヘッドレスで通す。
    /// </remarks>
    internal static ClaudeCodeLaunchOptions BuildLaunchOptions(
        string model,
        string workingDirectory
    ) =>
        new(
            Model: model ?? string.Empty,
            SystemPrompt: BuildSystemPrompt(),
            McpConfigPath: string.Empty,
            AllowedTool: string.Empty,
            WorkingDirectory: workingDirectory
        )
        {
            PermissionMode = "acceptEdits",
            AdditionalAllowedTools = HeadlessAllowedTools,
        };

    /// <summary>ヘッドレス実行のシステムプロンプト（規約・制約）を組み立てる</summary>
    internal static string BuildSystemPrompt() =>
        $@"あなたは WPF (.NET) の熟練エンジニアで、既存のプロジェクトに GUI（UI 層）を実装します。
このフォルダには QuickER が生成した WPF プロジェクトの雛形と、データ層のコードが既に用意されています。

# 守るべき規約
- 作業を始める前に、必ず {ReadmeFileName} を読み、その規約に従ってください。
- {DesignHtmlRelativePath} がデザイン仕様です。この HTML の画面構成・項目・画面遷移を WPF で忠実に再現してください（HTML をそのまま埋め込むのではなく、WPF のネイティブ UI で作り直します）。
- Generated/ 配下（データ層の自動生成コード）は読み取り専用です。絶対に編集・削除しないでください。UI からは I{{Entity}}Repository を DI 経由で使います。
- UI は CommunityToolkit.Mvvm を用いた MVVM（ObservableObject / RelayCommand / ObservableProperty）で実装してください。
- 起動時の DI 登録は AddGeneratedInMemoryRepositories()（サンプルデータ入り）を使ってください（実 DB 接続は不要）。

# 進め方
- App.xaml / App.xaml.cs で DI を構成し、MainWindow とビュー・ビューモデルを実装します。
- design/mock.html の各画面（一覧・登録／編集・遷移等）を WPF のウィンドウ／ページ／ユーザーコントロールとして再現します。
- 実装が一段落したら `dotnet build` を実行し、警告なし・エラーなしで通るまで修正を繰り返してください。
- 最後に、ビルドがエラー・警告なしで成功したことを確認した旨を報告してください。";

    /// <summary>初回プロンプト（実装の起点となる具体指示）を組み立てる</summary>
    internal static string BuildPrompt(string projectName) =>
        $@"プロジェクト『{projectName}』の WPF UI 層を実装してください。

手順:
1. まず {ReadmeFileName} を読み、プロジェクト構成と規約を把握する。
2. {DesignHtmlRelativePath} を読み、再現すべき画面構成・項目・遷移を把握する。
3. Generated/ 配下のデータ層（Entity / I{{Entity}}Repository / AddGeneratedInMemoryRepositories 等）を確認し、UI から利用する。
4. App.xaml(.cs)・MainWindow・各ビュー／ビューモデルを CommunityToolkit.Mvvm の MVVM で実装する。DI には AddGeneratedInMemoryRepositories() を使う。
5. `dotnet build` を実行し、エラー・警告なしで通るまで自己修正する。
6. ビルドが成功したことを確認して報告する。

Generated/ 配下は読み取り専用です。編集しないでください。";

    /// <summary>出力フォルダに csproj と xaml が存在するかを軽く検証する</summary>
    private static bool HasArtifacts(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return false;
        }

        var hasCsproj = Directory
            .EnumerateFiles(outputDirectory, "*.csproj", SearchOption.AllDirectories)
            .Any();
        var hasXaml = Directory
            .EnumerateFiles(outputDirectory, "*.xaml", SearchOption.AllDirectories)
            .Any();

        return hasCsproj && hasXaml;
    }

    /// <summary>結果メッセージを組み立てる</summary>
    private static string BuildResultMessage(
        bool success,
        bool timedOut,
        bool canceled,
        ClaudeCodeTurnOutcome outcome,
        bool artifactsPresent,
        bool buildSucceeded
    )
    {
        if (success)
        {
            return "WPF モックの生成が完了しました（ビルド成功を確認）。";
        }

        if (timedOut)
        {
            return "タイムアウトにより打ち切りました。生成途中のファイルとログは出力フォルダに残っています。";
        }

        if (canceled)
        {
            return "生成を中断しました。途中経過とログは出力フォルダに残っています。";
        }

        if (outcome.NotLoggedIn)
        {
            return "Claude Code が未ログインです。ターミナルで `claude` を起動し /login でログインしてください。";
        }

        if (!outcome.Success)
        {
            return "Claude Code の実行に失敗しました: "
                + (outcome.Error ?? "詳細不明")
                + "。ログを確認してください。";
        }

        if (!artifactsPresent)
        {
            return "生成物（csproj / xaml）を確認できませんでした。ログを確認してください。";
        }

        if (!buildSucceeded)
        {
            return "最終ビルドが失敗しました。生成物とログは出力フォルダに残っています。";
        }

        return "生成に失敗しました。ログを確認してください。";
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
