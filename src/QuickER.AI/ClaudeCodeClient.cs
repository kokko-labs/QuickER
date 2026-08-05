using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using QuickER.AI.Resources;

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

    private readonly Lazy<string?> _executablePath = new(ResolveExecutablePath);
    private Process? _currentProcess;

    /// <inheritdoc />
    public bool IsAvailable() => _executablePath.Value is not null;

    /// <inheritdoc />
    public async Task<ClaudeCodeTurnOutcome> RunTurnAsync(
        string prompt,
        string? resumeSessionId,
        ClaudeCodeLaunchOptions options,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    )
    {
        var executable = _executablePath.Value;

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

        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            startInfo.ArgumentList.Add("--append-system-prompt");
            startInfo.ArgumentList.Add(options.SystemPrompt);
        }

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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            return new ClaudeCodeTurnOutcome(false, Strings.ClaudeCode_LaunchFailed, null, false);
        }

        _currentProcess = process;

        // stderr は起動直後から常時ドレインする（読まないままだと数 KB でパイプバッファが満杯になり、
        // 子プロセスが write でブロックして stdout も進まなくなる＝ReadLineAsync が返らなくなる）
        var standardError = new StandardErrorDrain(process);

        try
        {
            await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
            process.StandardInput.Close();

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

    /// <inheritdoc />
    public async Task<ClaudeLoginProbeResult> ProbeLoginAsync(CancellationToken cancellationToken)
    {
        var executable = _executablePath.Value;

        if (executable is null)
        {
            return ClaudeLoginProbeResult.Unavailable;
        }

        // ER ツール・システムプロンプト・継続なしの最小実行でログイン状態だけを確認する
        var startInfo = CreateStartInfo(executable, Path.GetTempPath());
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("json");

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            return ClaudeLoginProbeResult.Unavailable;
        }

        // 実ターンと同様に stderr を常時ドレインする（読まないままだとパイプバッファ満杯で
        // 子プロセスが停止し、stdout の読み取りが返らなくなる）
        var standardError = new StandardErrorDrain(process);

        try
        {
            await process.StandardInput.WriteAsync("ping").ConfigureAwait(false);
            process.StandardInput.Close();

            var output = await process
                .StandardOutput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await standardError.WaitForCompletionAsync().ConfigureAwait(false);

            return InterpretProbe(output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return ClaudeLoginProbeResult.Unavailable;
        }
    }

    /// <summary>プローブ応答（--output-format json の単一オブジェクト）をログイン状態へ解釈する</summary>
    private static ClaudeLoginProbeResult InterpretProbe(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;

            var isError =
                root.TryGetProperty("is_error", out var isErrorEl)
                && isErrorEl.ValueKind == JsonValueKind.True;

            if (!isError)
            {
                return ClaudeLoginProbeResult.LoggedIn;
            }

            var message = root.TryGetProperty("result", out var resultEl)
                ? resultEl.GetString()
                : null;

            return
                message is not null
                && message.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)
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
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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
    /// 異常終了した場合は失敗として扱い、原因調査のため stderr の直近行をメッセージへ添える。
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
                return new ClaudeCodeTurnOutcome(
                    false,
                    stream.Error,
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
    /// 子プロセスの標準エラー出力を常時読み出し、直近行だけをリングバッファで保持するドレイナ。
    /// </summary>
    /// <remarks>
    /// stderr を読まないままにすると、子プロセスが数 KB 書いた時点でパイプバッファが満杯になって
    /// write でブロックし、stdout も進まなくなって読み取りが永久に返らない（デッドロック）。
    /// <see cref="CodexAppServerClient"/> と同じ流儀で、読み捨てつつ診断用に直近行だけを残す。
    /// </remarks>
    private sealed class StandardErrorDrain
    {
        /// <summary>診断用に保持する直近行数</summary>
        private const int MaxRetainedLines = 20;

        /// <summary>読み切りを待つ上限（子孫プロセスがパイプを握ったままでも固まらないようにする）</summary>
        private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(2);

        private readonly ConcurrentQueue<string> _lines = new();
        private readonly Task _drainTask;

        public StandardErrorDrain(Process process)
        {
            _drainTask = Task.Run(() => DrainAsync(process), CancellationToken.None);
        }

        /// <summary>保持している直近の stderr 行（古い順）</summary>
        public IReadOnlyList<string> RecentLines => _lines.ToArray();

        /// <summary>EOF までの読み切りを上限付きで待つ（超過しても保持済みの行はそのまま使える）</summary>
        public async Task WaitForCompletionAsync()
        {
            try
            {
                await _drainTask.WaitAsync(CompletionTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // 読み切れなくても保持済みの行で診断する（待ち続けない）
            }
        }

        /// <summary>EOF まで stderr を読み続け、直近 <see cref="MaxRetainedLines"/> 行だけを残す</summary>
        private async Task DrainAsync(Process process)
        {
            try
            {
                while (true)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);

                    if (line is null)
                    {
                        break;
                    }

                    _lines.Enqueue(line);

                    while (_lines.Count > MaxRetainedLines && _lines.TryDequeue(out _)) { }
                }
            }
            catch
            {
                // 中断・プロセス破棄に伴うストリーム例外は無視する（診断目的のため取り逃しは許容）
            }
        }
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
    private static (bool Success, string? Error, bool NotLoggedIn) ParseResult(JsonElement root)
    {
        var isError =
            root.TryGetProperty("is_error", out var isErrorEl)
            && isErrorEl.ValueKind == JsonValueKind.True;

        if (!isError)
        {
            return (true, null, false);
        }

        var message = root.TryGetProperty("result", out var resultEl) ? resultEl.GetString() : null;
        var notLoggedIn =
            message is not null
            && message.Contains("Not logged in", StringComparison.OrdinalIgnoreCase);

        return (false, message ?? Strings.ClaudeCode_GenericError, notLoggedIn);
    }

    /// <inheritdoc />
    public void Interrupt() => TryKill(_currentProcess);

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
    private static string? ResolveExecutablePath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? ["claude.exe", "claude.cmd", "claude.bat", "claude"]
            : ["claude"];

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

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TryKill(_currentProcess);
        _currentProcess = null;
        return ValueTask.CompletedTask;
    }
}
