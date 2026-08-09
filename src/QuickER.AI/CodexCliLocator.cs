using System.IO;

namespace QuickER.AI;

/// <summary>
/// codex CLI を PATH から検出する共有ロケーター。
/// AI チャット（<see cref="CodexChatEngine"/> 経由の <see cref="CodexAppServerClient"/>）と
/// AI モック生成（CodexMockProjectAgent）が同じ判定を使う。
/// </summary>
/// <remarks>
/// <see cref="ClaudeCodeProcessClient"/> の claude 検出・<see cref="CopilotCliLocator"/> の copilot 検出と
/// 同型の PATH 走査で、走査本体は <see cref="PathExecutableResolver"/> と共有する。
/// </remarks>
public static class CodexCliLocator
{
    /// <summary>検出対象のコマンド名（拡張子なし）</summary>
    private const string CommandName = "codex";

    /// <summary>codex 実行ファイルが PATH で解決できるか</summary>
    public static bool IsAvailable() => ResolveExecutablePath() is not null;

    /// <summary>PATH から codex 実行ファイルを解決する（見つからなければ null）</summary>
    public static string? ResolveExecutablePath() => PathExecutableResolver.Resolve(CommandName);

    /// <summary>走査ロジック（テストから PATH 値・存在判定を差し替えて検証する）</summary>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    internal static string? ResolveExecutablePath(
        string? pathValue,
        Func<string, bool> fileExists
    ) => PathExecutableResolver.Resolve(CommandName, pathValue, fileExists);
}
