using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ERDesigner.Services.Chat;

/// <summary>Claude Code を 1 ターン起動するための設定</summary>
/// <param name="Model">モデルエイリアス（空なら Claude Code 既定）</param>
/// <param name="SystemPrompt">追加 system プロンプト（ER 設計ルール）</param>
/// <param name="McpConfigPath">ER ツールを公開する MCP 設定ファイルのパス</param>
/// <param name="AllowedTool">許可するツール指定（例: <c>mcp__erdesigner</c>）</param>
/// <param name="WorkingDirectory">作業ディレクトリ（一時フォルダ）</param>
public sealed record ClaudeCodeLaunchOptions(
    string Model,
    string SystemPrompt,
    string McpConfigPath,
    string AllowedTool,
    string WorkingDirectory
);

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
            return new ClaudeCodeTurnOutcome(
                false,
                "claude CLI が見つかりません。Claude Code をインストールしてください。",
                null,
                false
            );
        }

        // stream-json は UTF-8。コンソールを持たない WPF では既定が OS のコードページ（日本語環境では
        // CP932）になり日本語が文字化けするため、入出力を明示的に BOM なし UTF-8 に固定する。
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8NoBom,
            StandardOutputEncoding = utf8NoBom,
            StandardErrorEncoding = utf8NoBom,
        };

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
            startInfo.ArgumentList.Add("--allowed-tools");
            startInfo.ArgumentList.Add(options.AllowedTool);
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

        // ユーザーの claude 設定/認証をそのまま使わせるため、注入された Anthropic 系 env を除去する
        foreach (var name in StrippedEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            return new ClaudeCodeTurnOutcome(false, "claude の起動に失敗しました。", null, false);
        }

        _currentProcess = process;

        try
        {
            await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
            process.StandardInput.Close();

            var outcome = await ReadStreamAsync(process, onAssistantText, cancellationToken)
                .ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
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

    /// <summary>stdout の stream-json 行を解析し、テキストを逐次通知しつつ最終結果を組み立てる</summary>
    private static async Task<ClaudeCodeTurnOutcome> ReadStreamAsync(
        Process process,
        Action<string> onAssistantText,
        CancellationToken cancellationToken
    )
    {
        string? sessionId = null;
        bool success = true;
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
                    (success, error, notLoggedIn) = ParseResult(root);
                    break;
            }
        }

        return new ClaudeCodeTurnOutcome(success, error, sessionId, notLoggedIn);
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

        return (false, message ?? "Claude Code でエラーが発生しました。", notLoggedIn);
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
