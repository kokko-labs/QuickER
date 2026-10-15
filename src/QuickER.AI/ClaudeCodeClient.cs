using System.Diagnostics;
using System.IO;
using System.Text.Json;
using QuickER.AI.Resources;
using QuickER.Platform;

namespace QuickER.AI;

/// <summary>Claude Code を 1 ターン起動するための設定</summary>
/// <param name="Model">モデルエイリアス（空なら Claude Code 既定）</param>
/// <param name="SystemPrompt">追加 system プロンプト（ER 設計ルール）</param>
/// <param name="McpConfigPath">ER ツールを公開する MCP 設定ファイルのパス</param>
/// <param name="AllowedTool">許可するツール指定（例: <c>mcp__erdesigner</c>）</param>
/// <param name="WorkingDirectory">作業ディレクトリ（一時フォルダ）</param>
/// <remarks>
/// 既定（<see cref="PermissionMode"/> 空・<see cref="AdditionalAllowedTools"/> 空）は従来のチャット用途で、
/// MCP ツール 1 系統だけを許可する。WPF モック生成のようにファイル編集・コマンド実行をヘッドレスで許可したい
/// 場合は <see cref="PermissionMode"/>（例 <c>acceptEdits</c>）と <see cref="AdditionalAllowedTools"/>
/// （例 <c>Edit</c> / <c>Write</c> / <c>Bash</c>）を指定する。既存チャット経路はこれらを指定しないため挙動不変。
/// </remarks>
public sealed record ClaudeCodeLaunchOptions(
    string Model,
    string SystemPrompt,
    string McpConfigPath,
    string AllowedTool,
    string WorkingDirectory
)
{
    /// <summary>
    /// <c>--permission-mode</c> に渡す値（例 <c>acceptEdits</c>）。空なら渡さない（既定＝プロンプト都度確認相当）。
    /// </summary>
    public string PermissionMode { get; init; } = string.Empty;

    /// <summary>
    /// <see cref="AllowedTool"/>（MCP 系）に追加して許可するツール名の一覧（例 <c>Edit</c> / <c>Write</c> / <c>Bash</c>）。
    /// </summary>
    /// <remarks>空（既定）ならチャット用途のまま。指定時は MCP ツールと合わせて <c>--allowed-tools</c> へ列挙する。</remarks>
    public IReadOnlyList<string> AdditionalAllowedTools { get; init; } = [];
}

/// <summary>Claude Code の 1 ターン実行結果</summary>
/// <param name="Success">成功したか</param>
/// <param name="Error">失敗時のメッセージ（成功時は null）</param>
/// <param name="SessionId">継続用セッション ID（取得できれば）</param>
/// <param name="NotLoggedIn">未ログインが原因の失敗か</param>
public sealed record ClaudeCodeTurnOutcome(
    bool Success,
    string? Error,
    string? SessionId,
    bool NotLoggedIn
);

/// <summary>stdout（stream-json）の読み取りだけで判明した中間結果</summary>
/// <param name="SessionId">継続用セッション ID（取得できれば）</param>
/// <param name="ResultReceived">
/// <c>result</c> イベントを受信したか。未受信は「CLI が結果を返さないまま終了した」＝失敗を意味する
/// （引数エラーで stderr のみ出力して終了した場合など）
/// </param>
/// <param name="Success">result イベントが成功を示したか（未受信時は false）</param>
/// <param name="Error">result イベントのエラーメッセージ</param>
/// <param name="NotLoggedIn">未ログインが原因か</param>
internal sealed record ClaudeCodeStreamResult(
    string? SessionId,
    bool ResultReceived,
    bool Success,
    string? Error,
    bool NotLoggedIn
);

/// <summary>軽量ログインプローブの結果</summary>
public enum ClaudeLoginProbeResult
{
    /// <summary>ログイン済み（送信可能）</summary>
    LoggedIn,

    /// <summary>未ログイン（/login が必要）</summary>
    NotLoggedIn,

    /// <summary>判定不能（応答解析失敗・想定外エラーなど）</summary>
    Unavailable,
}

/// <summary>Claude Code CLI をヘッドレスで駆動するクライアントの抽象（テストでフェイクへ差し替える）</summary>
public interface IClaudeCodeClient : IAsyncDisposable
{
    /// <summary>claude CLI が利用可能か（PATH 解決できるか）</summary>
    bool IsAvailable();

    /// <summary>1 ターンを実行する。アシスタントのテキストは <paramref name="onAssistantText"/> で逐次通知する</summary>
    /// <remarks>
    /// キャンセルされた場合は <see cref="OperationCanceledException"/> を投げず、
    /// <c>Success=false, Error=null</c> の失敗結果へ畳んで返す（<see cref="ClaudeCodeProcessClient"/> はプロセスを kill する）。
    /// チャットの中断を「エラー」として表示しないための規約で、3 エンジン（API キー / Codex / Claude Code）で共通。
    /// 中断とエラーを区別したい呼び出し側は、戻り値を受けたあとで自分のトークンを見ること
    /// （例: <c>ClaudeCodeMockProjectAgent.RunAsync</c>）。
    /// </remarks>
    Task<ClaudeCodeTurnOutcome> RunTurnAsync(
        string prompt,
        string? resumeSessionId,
        ClaudeCodeLaunchOptions options,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    );

    /// <summary>最小実行でログイン状態だけを軽量に確認する（明示操作時のみ呼ぶ）</summary>
    Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken);

    /// <summary>実行中のターンを中断する</summary>
    void Interrupt();
}

/// <summary>
/// 実際の <c>claude</c> プロセスをヘッドレス（<c>-p --output-format stream-json</c>）で起動し、
/// stream-json イベントを解析する本番クライアント。プロンプトは stdin（text）で渡し、
/// <c>--resume</c> で会話を継続する。継承した ANTHROPIC_*/CLAUDE_CODE_* 環境変数は除去し、
/// ユーザーの claude 設定/認証をそのまま使わせる。
/// </summary>
public sealed class ClaudeCodeProcessClient : IClaudeCodeClient
{
    private static readonly string[] StrippedEnvironmentVariables =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_MODEL",
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_ENTRYPOINT",
        "CLAUDE_CODE_SESSION_ID",
    ];

    /// <summary>失敗 result の errors 配列から取り込む最大件数（メッセージが際限なく伸びるのを防ぐ）</summary>
    private const int MaxReportedErrorEntries = 3;

    /// <summary>再認証（<c>/login</c>）が必要な失敗を示す文言マーカー（判定規則は <see cref="IndicatesLoginRequired"/>）</summary>
    private static readonly string[] LoginRequiredMarkers =
    [
        "Not logged in",
        "Failed to authenticate",
        "Invalid API key",
        "/login",
    ];

    /// <summary>ログイン確認（<see cref="ProbeLoginAsync"/>）の既定の上限時間</summary>
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>claude の実行ファイルのパスを返す処理（呼ぶたびに解決する）</summary>
    /// <remarks>
    /// 結果をキャッシュしない＝<see cref="PathExecutableResolver"/> と同じ契約。キャッシュすると、アプリを起動した後に
    /// PATH 上のフォルダへ claude を入れても「再確認」で検出できない
    /// </remarks>
    private readonly Func<string?> _resolveExecutablePath;

    /// <summary>ログイン確認の上限時間（超えたらプロセスを止めて「確認できなかった」として返す）</summary>
    private readonly TimeSpan _probeTimeout;

    private Process? _currentProcess;

    /// <summary>実行中のログイン確認のプロセス（破棄で止めるために控える）</summary>
    /// <remarks>
    /// ターンの <see cref="_currentProcess"/> とは分けて持つ。<see cref="Interrupt"/> は「実行中のターンの中断」なので
    /// 確認を止めるのは意味が違い、共有すると確認とターンが重なったときに片方の後片づけが相手を追跡から外す
    /// </remarks>
    private Process? _probeProcess;

    /// <summary>PATH 上の claude を使うクライアントを作る</summary>
    public ClaudeCodeProcessClient()
        : this(ResolveExecutablePath, DefaultProbeTimeout) { }

    /// <summary>実行ファイルとログイン確認の上限時間を差し替えたクライアントを作る（テストが偽の claude を使うため）</summary>
    /// <param name="resolveExecutablePath">claude の実行ファイルのパスを返す処理（見つからなければ null）</param>
    /// <param name="probeTimeout">ログイン確認の上限時間</param>
    internal ClaudeCodeProcessClient(Func<string?> resolveExecutablePath, TimeSpan probeTimeout)
    {
        _resolveExecutablePath = resolveExecutablePath;
        _probeTimeout = probeTimeout;
    }

    /// <inheritdoc />
    public bool IsAvailable() => _resolveExecutablePath() is not null;

    /// <inheritdoc />
    public async Task<ClaudeCodeTurnOutcome> RunTurnAsync(
        string prompt,
        string? resumeSessionId,
        ClaudeCodeLaunchOptions options,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    )
    {
        var executable = _resolveExecutablePath();

        if (executable is null)
        {
            return new ClaudeCodeTurnOutcome(false, Strings.ClaudeCode_CliNotFound, null, false);
        }

        var startInfo = CreateStartInfo(executable, options.WorkingDirectory);

        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("stream-json");
        startInfo.ArgumentList.Add("--verbose");
        // トークン単位でテキストを流すため部分メッセージ（content_block_delta）を有効化する
        startInfo.ArgumentList.Add("--include-partial-messages");

        if (!string.IsNullOrWhiteSpace(options.McpConfigPath))
        {
            startInfo.ArgumentList.Add("--mcp-config");
            startInfo.ArgumentList.Add(options.McpConfigPath);
        }

        // 許可ツールを組み立てる: MCP ツール（McpConfigPath 指定時）＋追加ツール（ファイル編集・コマンド実行等）。
        // 既存チャット用途では McpConfigPath のみ・追加ツール空のため、従来と同じ 1 系統の許可指定になる。
        var allowedTools = new List<string>();

        if (
            !string.IsNullOrWhiteSpace(options.McpConfigPath)
            && !string.IsNullOrWhiteSpace(options.AllowedTool)
        )
        {
            allowedTools.Add(options.AllowedTool);
        }

        allowedTools.AddRange(
            options.AdditionalAllowedTools.Where(tool => !string.IsNullOrWhiteSpace(tool))
        );

        if (allowedTools.Count > 0)
        {
            startInfo.ArgumentList.Add("--allowed-tools");
            startInfo.ArgumentList.Add(string.Join(",", allowedTools));
        }

        // 許可モード（acceptEdits 等）はヘッドレスでのファイル編集・コマンド実行を通すために指定する。
        // 空（既定）なら渡さず、従来のチャット挙動を保つ。
        if (!string.IsNullOrWhiteSpace(options.PermissionMode))
        {
            startInfo.ArgumentList.Add("--permission-mode");
            startInfo.ArgumentList.Add(options.PermissionMode);
        }

        // --model / --resume はシステムプロンプトより前に積む。ファイル渡し化で改行は引数から
        // 消えるが、「後ろに積んだ引数が失われる」形の事故に対する順序依存も残さない
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(options.Model);
        }

        if (!string.IsNullOrWhiteSpace(resumeSessionId))
        {
            startInfo.ArgumentList.Add("--resume");
            startInfo.ArgumentList.Add(resumeSessionId);
        }

        // システムプロンプトは引数でなく一時ファイルで渡す（理由は AppendSystemPromptArguments の XmlDoc）
        var systemPromptPath = string.IsNullOrWhiteSpace(options.SystemPrompt)
            ? null
            : AppendSystemPromptArguments(startInfo, options.SystemPrompt);

        try
        {
            // 引数を積み終えてからシムガードを通す（claude が .cmd で入っている場合に cmd.exe 経由へ包み直す）
            ApplyBatchShimGuard(startInfo);

            using var process = ExternalLauncher.CreateProcess(startInfo);

            if (!process.Start())
            {
                return new ClaudeCodeTurnOutcome(
                    false,
                    Strings.ClaudeCode_LaunchFailed,
                    null,
                    false
                );
            }

            _currentProcess = process;

            // stderr は起動直後から常時ドレインする（読まないままだと数 KB でパイプバッファが満杯になり、
            // 子プロセスが write でブロックして stdout も進まなくなる＝ReadLineAsync が返らなくなる）
            var standardError = new StandardErrorDrain(process);

            try
            {
                try
                {
                    await process
                        .StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // claude がプロンプトを読み切る前に終わった（起動直後に落ちた・引数を拒否した）。生の例外で抜けると
                    // stderr の診断が失われるので、入力を閉じて読み進め、終了コードと stderr で失敗の結果を組み立てる
                    CloseInputQuietly(process);
                }

                var stream = await ReadStreamAsync(process, onAssistantText, cancellationToken)
                    .ConfigureAwait(false);

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                // 終了コードと stderr を判定材料に含めるため、読み切りを（上限付きで）待ってから評価する
                await standardError.WaitForCompletionAsync().ConfigureAwait(false);

                return EvaluateTurnOutcome(stream, process.ExitCode, standardError.RecentLines);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return new ClaudeCodeTurnOutcome(false, null, resumeSessionId, false);
            }
            finally
            {
                _currentProcess = null;
            }
        }
        finally
        {
            // プロセス終了後に後片付けする（読み終える前に消すと claude が読めない）
            TryDeleteSystemPromptFile(systemPromptPath);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 確認のプロセスは破棄（<see cref="DisposeAsync"/>）で止まり、上限時間（既定 60 秒）を過ぎても止める。
    /// どちらも、呼び出し元の取り消しと同じく <see cref="ClaudeLoginProbeResult.Unavailable"/>（確認できなかった）を返す
    /// </remarks>
    public async Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken)
    {
        var executable = _resolveExecutablePath();

        if (executable is null)
        {
            return ClaudeLoginProbeResult.Unavailable;
        }

        // ER ツール・システムプロンプト・継続なしの最小実行でログイン状態だけを確認する
        var startInfo = CreateStartInfo(executable, Path.GetTempPath());
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("json");
        ApplyBatchShimGuard(startInfo);

        using var process = ExternalLauncher.CreateProcess(startInfo);

        if (!process.Start())
        {
            return ClaudeLoginProbeResult.Unavailable;
        }

        _probeProcess = process;

        // 実ターンと同様に stderr を常時ドレインする（読まないままだとパイプバッファ満杯で
        // 子プロセスが停止し、stdout の読み取りが返らなくなる）
        var standardError = new StandardErrorDrain(process);

        // 呼び出し元の取り消しに上限時間を重ねる（確認が返らないまま子プロセスが残り続けないように）
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_probeTimeout);

        try
        {
            try
            {
                await process.StandardInput.WriteAsync("ping").ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // claude が入力を読む前に終わった（破棄で止められた・起動直後に落ちた）。書き込みはスレッドプールで
                // 進むので、破棄が先に走ればここへ来る。確認の結果は出力で決める（出力が無ければ確認できなかった）。
                // 閉じ損ねた入力を閉じておく（StreamWriter は失敗しても下のパイプを閉じる＝プロセスの破棄で再び投げない）
                CloseInputQuietly(process);
            }

            var output = await process
                .StandardOutput.ReadToEndAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await standardError.WaitForCompletionAsync().ConfigureAwait(false);

            return InterpretProbe(output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return ClaudeLoginProbeResult.Unavailable;
        }
        finally
        {
            // 自分が載せたものだけを外す
            Interlocked.CompareExchange(ref _probeProcess, null, process);
        }
    }

    /// <summary>プローブ応答（--output-format json の単一オブジェクト）をログイン状態へ解釈する</summary>
    /// <remarks>
    /// 応答の形は result イベントと同じため、成否・再認証要否の判定は <see cref="ParseResult"/> へ委譲する
    /// （プローブと実ターンで判定規則がずれると、状態表示と送信結果が食い違う）。
    /// </remarks>
    internal static ClaudeLoginProbeResult InterpretProbe(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var (success, _, notLoggedIn) = ParseResult(document.RootElement);

            if (success)
            {
                return ClaudeLoginProbeResult.LoggedIn;
            }

            return notLoggedIn
                ? ClaudeLoginProbeResult.NotLoggedIn
                : ClaudeLoginProbeResult.Unavailable;
        }
        catch (JsonException)
        {
            return ClaudeLoginProbeResult.Unavailable;
        }
    }

    /// <summary>
    /// claude プロセス起動用の <see cref="ProcessStartInfo"/> を生成する。
    /// 入出力を BOM なし UTF-8 に固定（コンソール無し環境での文字化け対策）し、
    /// ユーザーの claude 設定/認証をそのまま使わせるため注入された Anthropic 系 env を除去する。
    /// </summary>
    private static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory)
    {
        var utf8NoBom = TextEncodings.Utf8NoBomLenient;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8NoBom,
            StandardOutputEncoding = utf8NoBom,
            StandardErrorEncoding = utf8NoBom,
        };

        foreach (var name in StrippedEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        return startInfo;
    }

    /// <summary>システムプロンプトの一時ファイルを置くフォルダ（<c>%TEMP%\QuickER\claude-code</c>）</summary>
    private static string SystemPromptDirectory =>
        Path.Combine(AppDataPaths.TempRoot, "claude-code");

    /// <summary>
    /// システムプロンプトを一時ファイルへ書き出し、<c>--append-system-prompt-file</c> の引数として積む。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本文を <c>--append-system-prompt</c> の引数で直接渡さないのは、npm 導入の claude が
    /// <c>.cmd</c> シム（<c>"%~dp0bin\claude.exe" %*</c> 形）だと、cmd が <c>%*</c> を展開する段階で
    /// **最初の改行から後ろを黙って捨てる**ため。捨てられるのはプロンプトの 2 行目以降だけでなく
    /// 後続の引数もすべてで、<c>--model</c> / <c>--resume</c> が消える＝毎ターン新規セッションになる。
    /// ER 設計ルールもモック生成プロンプトも複数行なので、この経路は常に踏む。
    /// </para>
    /// <para>
    /// 渡すのがパスなら改行も cmd のメタ文字も含まない（ファイル名は GUID）ため、シム経由でも
    /// 1 トークンのまま届く。内容は BOM なし UTF-8 で書く（BOM を本文の一部として読ませない）。
    /// </para>
    /// </remarks>
    /// <param name="startInfo">引数を積む起動情報</param>
    /// <param name="systemPrompt">渡すシステムプロンプト本文</param>
    /// <returns>書き出した一時ファイルのパス（呼び出し側がプロセス終了後に削除する）</returns>
    internal static string AppendSystemPromptArguments(
        ProcessStartInfo startInfo,
        string systemPrompt
    )
    {
        Directory.CreateDirectory(SystemPromptDirectory);

        var path = Path.Combine(SystemPromptDirectory, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, systemPrompt, TextEncodings.Utf8NoBomLenient);

        startInfo.ArgumentList.Add("--append-system-prompt-file");
        startInfo.ArgumentList.Add(path);

        return path;
    }

    /// <summary>システムプロンプトの一時ファイルをベストエフォートで削除する</summary>
    /// <remarks>
    /// 削除の失敗（ウイルス対策の掴み等）でターンの結果を差し替えないよう握り潰す。残っても
    /// <c>%TEMP%</c> 配下の小さなテキストで、OS のクリーンアップ対象。
    /// </remarks>
    /// <param name="path">削除するパス（null なら何もしない）</param>
    internal static void TryDeleteSystemPromptFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 掴まれている等で消せなくても無視する
        }
        catch (UnauthorizedAccessException)
        {
            // 権限で消せなくても無視する
        }
    }

    /// <summary>
    /// claude の実体が <c>.cmd</c> / <c>.bat</c> シム（npm 導入）のとき、起動情報を
    /// <c>cmd.exe</c> 経由へ包み直す。<c>.exe</c> なら何もしない。
    /// </summary>
    /// <remarks>
    /// 引数を積み終えた後に呼ぶこと（<see cref="ProcessStartInfo.ArgumentList"/> の全トークンを
    /// 引用し直すため）。ポリシーの正本は <see cref="BatchShimProcessGuard"/>。
    /// </remarks>
    internal static void ApplyBatchShimGuard(ProcessStartInfo startInfo) =>
        BatchShimProcessGuard.Apply(
            startInfo,
            Strings.ClaudeCode_PathHasQuote,
            Strings.ClaudeCode_PathHasEnvExpansion,
            Strings.ClaudeCode_ArgHasEnvExpansion,
            Strings.ClaudeCode_ArgHasNewline
        );

    /// <summary>stdout の stream-json 行を解析し、テキストを逐次通知しつつ中間結果を組み立てる</summary>
    private static async Task<ClaudeCodeStreamResult> ReadStreamAsync(
        Process process,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    )
    {
        string? sessionId = null;
        // result イベントを受信して初めて成否が確定する（未受信を成功と見なすと、引数エラー等で
        // stdout に何も出ないまま終了した場合に偽の成功を返してしまう）
        bool resultReceived = false;
        bool success = false;
        string? error = null;
        bool notLoggedIn = false;

        while (true)
        {
            var line = await process
                .StandardOutput.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonElement root;

            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (
                root.TryGetProperty("session_id", out var sid)
                && sid.ValueKind == JsonValueKind.String
            )
            {
                sessionId = sid.GetString();
            }

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            switch (type)
            {
                case "stream_event":
                    EmitPartialText(root, onAssistantText);
                    break;
                case "result":
                    resultReceived = true;
                    (success, error, notLoggedIn) = ParseResult(root);
                    break;
            }
        }

        return new ClaudeCodeStreamResult(sessionId, resultReceived, success, error, notLoggedIn);
    }

    /// <summary>
    /// stdout の解析結果・プロセスの終了コード・stderr の直近行から、ターンの最終結果を決定する。
    /// </summary>
    /// <remarks>
    /// result イベントを受信していればその判定を正本とし、成功かつ終了コード 0 のときだけ成功を返す。
    /// result 未受信（引数エラーで stdout に何も出ないまま終了した場合など）と、result は成功なのに
    /// 異常終了した場合は失敗として扱う。失敗はいずれの経路でも原因調査のため stderr の直近行を
    /// メッセージへ添える（CLI 自身が報告した失敗でも、stderr にしか出ない詳細があるため）。
    /// </remarks>
    internal static ClaudeCodeTurnOutcome EvaluateTurnOutcome(
        ClaudeCodeStreamResult stream,
        int exitCode,
        IReadOnlyList<string> standardErrorLines
    )
    {
        if (stream.ResultReceived)
        {
            // CLI 自身が報告したエラー（未ログイン等）はそのまま伝える＝終了コードより情報量が多い
            if (!stream.Success)
            {
                // 報告文へ stderr を添えるのは他分岐と同じ扱い（報告文も stderr も無いなら null のまま返す）
                var reported = stream.Error + BuildStandardErrorSuffix(standardErrorLines);

                return new ClaudeCodeTurnOutcome(
                    false,
                    string.IsNullOrEmpty(reported) ? null : reported,
                    stream.SessionId,
                    stream.NotLoggedIn
                );
            }

            if (exitCode == 0)
            {
                return new ClaudeCodeTurnOutcome(true, null, stream.SessionId, false);
            }

            return new ClaudeCodeTurnOutcome(
                false,
                string.Format(Strings.ClaudeCode_ExitedWithError, exitCode)
                    + BuildStandardErrorSuffix(standardErrorLines),
                stream.SessionId,
                false
            );
        }

        return new ClaudeCodeTurnOutcome(
            false,
            string.Format(Strings.ClaudeCode_NoResult, exitCode)
                + BuildStandardErrorSuffix(standardErrorLines),
            stream.SessionId,
            false
        );
    }

    /// <summary>直近の標準エラー出力を失敗メッセージへの補足文として返す</summary>
    /// <returns>stderr が空の場合は空文字列</returns>
    internal static string BuildStandardErrorSuffix(IReadOnlyList<string> standardErrorLines)
    {
        var lines = standardErrorLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();

        return lines.Length == 0 ? string.Empty : $" stderr: {string.Join(" | ", lines)}";
    }

    /// <summary>
    /// stream_event の content_block_delta（text_delta）から差分テキストを取り出して逐次通知する。
    /// 集約版の assistant イベントは無視する（二重出力になるため）。thinking_delta も対象外。
    /// </summary>
    private static void EmitPartialText(JsonElement root, Action<string> onAssistantText)
    {
        if (
            !root.TryGetProperty("event", out var streamEvent)
            || !streamEvent.TryGetProperty("type", out var eventType)
            || eventType.GetString() != "content_block_delta"
            || !streamEvent.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("type", out var deltaType)
            || deltaType.GetString() != "text_delta"
            || !delta.TryGetProperty("text", out var text)
            || text.GetString() is not { Length: > 0 } value
        )
        {
            return;
        }

        onAssistantText(value);
    }

    /// <summary>result イベントから成否・エラー・未ログインを判定する</summary>
    internal static (bool Success, string? Error, bool NotLoggedIn) ParseResult(JsonElement root)
    {
        var isError =
            root.TryGetProperty("is_error", out var isErrorEl)
            && isErrorEl.ValueKind == JsonValueKind.True;

        if (!isError)
        {
            return (true, null, false);
        }

        var message = ReadFailureMessage(root);

        return (false, message ?? Strings.ClaudeCode_GenericError, IndicatesLoginRequired(message));
    }

    /// <summary>
    /// 失敗メッセージが「ターミナルで <c>/login</c> し直す必要がある」種類の認証エラーかを判定する。
    /// </summary>
    /// <remarks>
    /// 判定材料は claude CLI が実際に出す認証エラー文言で、次の 4 系統を <see cref="LoginRequiredMarkers"/>
    /// で拾う: <c>Not logged in · Please run /login</c>（未ログイン）、
    /// <c>Failed to authenticate: OAuth session expired and could not be refreshed</c> および
    /// <c>Failed to authenticate. {詳細}</c>（保存済み資格情報の失効。ヘッドレス実行はこちらへ落ちる）、
    /// <c>Invalid API key · Fix external API key</c>（外部 API キー不正）、
    /// <c>Please run /login · {詳細}</c>（CLI 自身の再ログイン指示）。
    /// <para>
    /// <c>OAuth</c> や <c>expired</c> 単体はマーカーにしない。MCP サーバー側の OAuth 失敗まで拾ってしまい、
    /// claude 本体のログインとは無関係な失敗に「/login し直せ」と誤誘導するため。
    /// </para>
    /// <para>
    /// 拾えなかった認証エラーは <see cref="ClaudeLoginProbeResult.Unavailable"/>（案内は「しばらく待って再確認」）
    /// へ落ちる。待っても直らない失敗にその案内を出すことになるので、新しい文言を見つけたらここへ足す。
    /// </para>
    /// </remarks>
    internal static bool IndicatesLoginRequired(string? message) =>
        message is not null
        && LoginRequiredMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>失敗した result イベントの原因文言を取り出す（result 文字列 → errors 配列の順）</summary>
    /// <remarks>
    /// <c>--resume</c> に不在のセッション ID を渡した場合など、CLI は <c>result</c> を持たず
    /// <c>errors</c> 配列にだけ具体的な原因（<c>No conversation found ...</c>）を載せて返す。
    /// 文字列 <c>result</c> が無い／空のときは errors を拾わないと原因が捨てられ、汎用文言だけが残る。
    /// </remarks>
    private static string? ReadFailureMessage(JsonElement root)
    {
        if (
            root.TryGetProperty("result", out var resultEl)
            && resultEl.ValueKind == JsonValueKind.String
            && resultEl.GetString() is { } message
            && !string.IsNullOrWhiteSpace(message)
        )
        {
            return message;
        }

        if (
            !root.TryGetProperty("errors", out var errorsEl)
            || errorsEl.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        var entries = errorsEl
            .EnumerateArray()
            .Select(DescribeErrorEntry)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(MaxReportedErrorEntries)
            .ToArray();

        return entries.Length == 0 ? null : string.Join(" | ", entries);
    }

    /// <summary>errors 配列の 1 要素を文言化する（文字列はそのまま・オブジェクト等は生 JSON で欠落させない）</summary>
    private static string DescribeErrorEntry(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.String
            ? entry.GetString() ?? string.Empty
            : entry.GetRawText();

    /// <inheritdoc />
    public void Interrupt() => TryKill(_currentProcess);

    /// <summary>書き込みに失敗した標準入力を、例外を出さずに閉じる</summary>
    private static void CloseInputQuietly(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 残りを書き出せないだけで、下のパイプは閉じられている
        }
    }

    /// <summary>プロセスを安全に終了する</summary>
    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 既に終了しているなどの競合は無視する
        }
    }

    /// <summary>PATH から claude 実行ファイルを解決する（見つからなければ null）</summary>
    /// <remarks>走査規則は codex / copilot のロケーターと共通（<see cref="PathExecutableResolver"/>）。</remarks>
    private static string? ResolveExecutablePath() => PathExecutableResolver.Resolve("claude");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TryKill(_currentProcess);
        _currentProcess = null;
        // 実行中のログイン確認も止める（止めないと、確認が返らない限り子プロセスが孤児として残る）
        TryKill(_probeProcess);
        _probeProcess = null;
        return ValueTask.CompletedTask;
    }
}
