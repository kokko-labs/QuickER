using System.Diagnostics;
using System.Runtime.ExceptionServices;
using AwesomeAssertions;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>起動の入口（OS との境界のエンジンの ProcessLaunch の部分）</summary>
/// <remarks>
/// SUT は複数のプロジェクトにまたがる：
/// QuickER.Platform（<c>BatchShimProcessGuard</c>・<c>PathExecutableResolver</c>・<c>ExternalLauncher</c>）、
/// QuickER.AI（Claude Code・Codex・Copilot の CLI の起動の配線とロケータ）、
/// QuickER.AI.UI（<c>ShellChatLinkOpener</c>）。
/// </remarks>
internal enum LaunchEntrance
{
    /// <summary><c>BatchShimProcessGuard.IsBatchShim</c></summary>
    ShimIsBatchShim,

    /// <summary><c>BatchShimProcessGuard.WrapCommandLine</c>（引数が 1 本の文字列）</summary>
    ShimWrapCommandLine,

    /// <summary><c>BatchShimProcessGuard.Apply</c>（ArgumentList）</summary>
    ShimApply,

    /// <summary><c>ClaudeCodeProcessClient.ApplyBatchShimGuard</c>（Apply へ Claude の文言を渡す配線）</summary>
    ClaudeApplyBatchShimGuard,

    /// <summary><c>CodexAppServerClient.ResolveLaunch</c></summary>
    CodexResolveLaunch,

    /// <summary><c>CodexAppServerClient.ResolveStartInfo</c>（WrapCommandLine へ Codex の文言を渡す配線）</summary>
    CodexResolveStartInfo,

    /// <summary><c>CopilotCliLocator.EnsureSafeToLaunch</c></summary>
    CopilotEnsureSafeToLaunch,

    /// <summary><c>PathExecutableResolver.Resolve</c>（PATH・存在・PATHEXT を注入）</summary>
    PathExecutableResolve,

    /// <summary><c>CopilotCliLocator.ResolveExecutablePath</c>（internal・PATH と存在を注入）</summary>
    CopilotLocate,

    /// <summary><c>CodexCliLocator.ResolveExecutablePath</c>（internal・PATH と存在を注入）</summary>
    CodexLocate,

    /// <summary><c>ExternalLauncher.OpenUrl</c>（internal・起動のデリゲートを注入）</summary>
    ExternalOpenUrl,

    /// <summary><c>ExternalLauncher.OpenFolder</c>（internal・起動のデリゲートを注入）</summary>
    ExternalOpenFolder,

    /// <summary><c>ExternalLauncher.StartExecutable</c>（internal・起動のデリゲートを注入）</summary>
    ExternalStartExecutable,

    /// <summary><c>ExternalLauncher.CreateProcess</c>（Process を作るだけで Start しない）</summary>
    ExternalCreateProcess,

    /// <summary><c>ShellChatLinkOpener.Open</c>（起動のデリゲートとダイアログを注入）</summary>
    ShellChatLinkOpen,
}

/// <summary>入口が受け取る入力の種類（次元表の <c>isValid</c> が入口と形の組を絞るのに使う）</summary>
internal enum LaunchInputKind
{
    /// <summary>起動するファイルのパス</summary>
    ShimPath,

    /// <summary>起動の引数（1 本の文字列か ArgumentList）</summary>
    ShimArgument,

    /// <summary>PATH の探索（PATH の値・存在するファイル・PATHEXT・コマンド名）</summary>
    PathSearch,

    /// <summary>開く URL</summary>
    Url,

    /// <summary>開くフォルダ</summary>
    Folder,

    /// <summary>起動する実行ファイル（パスか ProcessStartInfo）</summary>
    Executable,
}

/// <summary>入力の形（次元表の因子 Shape の水準）</summary>
internal enum LaunchShape
{
    /// <summary>シムでない実行ファイル（.exe）を素の引数で</summary>
    ExeTarget,

    /// <summary>シム（.cmd / .bat）を素の引数で（空白を含むパスを含む）</summary>
    ShimTarget,

    /// <summary>シムの拡張子の綴りの揺れ（大文字・末尾の空白とドット）</summary>
    ShimSpelling,

    /// <summary>Windows でない形のパス・null</summary>
    NonWindowsOrNullPath,

    /// <summary>パスに引用符</summary>
    PathQuote,

    /// <summary>パスに <c>%</c></summary>
    PathPercent,

    /// <summary>パスに <c>!</c></summary>
    PathExclamation,

    /// <summary>パスに改行</summary>
    PathNewline,

    /// <summary>.exe のパスにシムなら断る文字（<c>%</c>・<c>&amp;</c>・<c>!</c>）</summary>
    ExePathWithShimMeta,

    /// <summary>引数に cmd のメタ文字（<c>&amp;</c> <c>|</c> <c>&lt;</c> <c>&gt;</c> <c>^</c>）</summary>
    ArgCmdMeta,

    /// <summary>引数に <c>%</c></summary>
    ArgPercent,

    /// <summary>引数に引用符</summary>
    ArgQuote,

    /// <summary>引数に改行</summary>
    ArgNewline,

    /// <summary>引数に <c>!</c></summary>
    ArgExclamation,

    /// <summary>引数にバックスラッシュ（引用符の直前・末尾）</summary>
    ArgBackslash,

    /// <summary>空の引数</summary>
    ArgEmpty,

    /// <summary>PATH の要素にある</summary>
    OnPath,

    /// <summary>Windows の .cmd の候補だけがある</summary>
    CmdCandidate,

    /// <summary>.exe と .cmd の両方がある</summary>
    ExeAndCmd,

    /// <summary>どの要素にも無い</summary>
    NotOnPath,

    /// <summary>PATH が null・空</summary>
    EmptyPath,

    /// <summary>PATH に空・空白の要素</summary>
    EmptyEntries,

    /// <summary>PATH に相対の要素</summary>
    RelativeEntries,

    /// <summary>PATH に引用符で囲んだ要素</summary>
    QuotedEntry,

    /// <summary>前の要素に拡張子の無い同名のファイル</summary>
    ExtensionlessFirst,

    /// <summary>PATHEXT の並びに従う</summary>
    PathExtOrder,

    /// <summary>PATHEXT に無い拡張子</summary>
    ExtensionOutsidePathExt,

    /// <summary>コマンド名ごとに探す</summary>
    PerCommandName,

    /// <summary>解決は検証しない（引用符を含む PATH の要素）</summary>
    UnvalidatedEntry,

    /// <summary>安全な URL（http / https）</summary>
    SafeUrl,

    /// <summary>許さないスキームの URL（javascript・file・ms-settings・mailto・ftp）</summary>
    UnsafeScheme,

    /// <summary>UNC の形の URL</summary>
    UncUrl,

    /// <summary>ユーザー情報を含む URL</summary>
    UserInfoUrl,

    /// <summary>起動のデリゲートが投げる</summary>
    LaunchFails,

    /// <summary>相対パス</summary>
    RelativePath,

    /// <summary>無いフォルダ</summary>
    MissingFolder,

    /// <summary>在るフォルダのフルパス</summary>
    ExistingFolder,

    /// <summary>名前だけ・相対の実行ファイル</summary>
    BareName,

    /// <summary>名前だけで許す実行ファイル（dotnet）</summary>
    AllowedBareName,

    /// <summary>包み直していないシム</summary>
    UnwrappedShim,

    /// <summary>Apply で包み直したシム</summary>
    WrappedShim,

    /// <summary>UseShellExecute</summary>
    ShellExecute,
}

/// <summary>起動の結果の種類（行が宣言し、流す側が観測と食い違わないかを確かめる）</summary>
internal enum LaunchOutcome
{
    /// <summary>真偽の判定を返す</summary>
    Classified,

    /// <summary>入力をそのまま返す（書き換えない）</summary>
    PassThrough,

    /// <summary>cmd.exe 経由へ包み直す</summary>
    Wrapped,

    /// <summary>例外で断る（何も起動しない）</summary>
    Rejected,

    /// <summary>パスを解決した</summary>
    Resolved,

    /// <summary>見つからない（null）</summary>
    NotFound,

    /// <summary>起動した（Process を作った）</summary>
    Launched,

    /// <summary>例外を出さずに起動しない</summary>
    NotLaunched,

    /// <summary>起動の失敗をエラーのダイアログで知らせた</summary>
    Reported,
}

/// <summary>1 行を流した観測</summary>
internal sealed class LaunchObservation
{
    /// <summary>入口の戻り値（例外なら null）</summary>
    public object? Value { get; set; }

    /// <summary>入口が投げた例外</summary>
    public Exception? Thrown { get; set; }

    /// <summary>起動のデリゲートが受け取った <see cref="ProcessStartInfo"/>（<c>ExternalLauncher</c> の入口）</summary>
    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>起動のデリゲートが受け取った URL（<c>ShellChatLinkOpener</c>）</summary>
    public List<string> Launched { get; } = [];

    /// <summary>ダイアログの記録（<c>ShellChatLinkOpener</c>）</summary>
    public StubDialogService Dialogs { get; } = new();

    /// <summary>
    /// 例外の表明を元のテストの書き方（<c>act.Should().Throw&lt;T&gt;().WithMessage(pattern)</c>）のまま行う
    /// </summary>
    /// <typeparam name="T">期待する例外の型（派生の型も通る＝元の表明と同じ）</typeparam>
    /// <param name="pattern">メッセージのワイルドカードの形（null なら型だけを見る）</param>
    public void ShouldHaveThrown<T>(string? pattern = null)
        where T : Exception
    {
        var thrown = Thrown;
        Action act = () =>
        {
            if (thrown is not null)
            {
                ExceptionDispatchInfo.Throw(thrown);
            }
        };

        var assertion = act.Should().Throw<T>();

        if (pattern is not null)
        {
            assertion.WithMessage(pattern);
        }
    }
}

/// <summary>起動の表の 1 行の入力</summary>
/// <param name="Entrance">入口</param>
/// <param name="Shape">入力の形</param>
/// <param name="Outcome">結果の種類（観測と照合する）</param>
/// <param name="Act">入口を呼ぶ（戻り値は <see cref="LaunchObservation.Value"/> に入る。起動の記録は観測へ足す）</param>
/// <param name="Expect">元のテストの表明</param>
/// <param name="WindowsOnly">元のテストが Windows のときだけ表明していた行</param>
internal sealed record LaunchCase(
    LaunchEntrance Entrance,
    LaunchShape Shape,
    LaunchOutcome Outcome,
    Func<LaunchObservation, object?> Act,
    Action<LaunchObservation> Expect,
    bool WindowsOnly = false
);

/// <summary>起動の表（入口 × 入力の形 → 結果）</summary>
/// <remarks>
/// 行は元の単発のテストと同じ入力・同じ表明を持つ（主題ごとのファイル：<c>ProcessLaunchRows.Shim.cs</c>・
/// <c>ProcessLaunchRows.Resolve.cs</c>・<c>ProcessLaunchRows.External.cs</c>）。
/// 流す側（<see cref="ProcessLaunchRunner"/>）は、行の表明の前に、宣言した結果の種類が観測と食い違わないこと
/// （断る行は例外があり何も起動しない・ほかの行は例外が無い、など）を全行に掛ける。
/// </remarks>
internal static partial class ProcessLaunchRows
{
    /// <summary>表</summary>
    public static ScenarioTable<LaunchCase> Table =>
        new(
            EngineIds.OsBoundary,
            [.. ShimRows(), .. ResolveRows(), .. ExternalRows(), .. FilledRows(), .. GapRows()]
        );

    /// <summary>行を作る</summary>
    private static ScenarioRow<LaunchCase> Row(
        string id,
        string description,
        LaunchEntrance entrance,
        LaunchShape shape,
        LaunchOutcome outcome,
        Func<LaunchObservation, object?> act,
        Action<LaunchObservation> expect,
        bool windowsOnly = false
    ) =>
        new(
            id,
            null,
            description,
            new LaunchCase(entrance, shape, outcome, act, expect, windowsOnly)
        );
}

/// <summary>起動の表の行を流す</summary>
internal static class ProcessLaunchRunner
{
    /// <summary>1 行を流す（結果の種類の照合 → 行の表明）。Windows だけの行を Windows 以外で流したら false</summary>
    public static bool Run(LaunchCase row)
    {
        if (row.WindowsOnly && !OperatingSystem.IsWindows())
        {
            return false;
        }

        var observation = new LaunchObservation();

        try
        {
            observation.Value = row.Act(observation);
        }
        catch (Exception ex)
        {
            observation.Thrown = ex;
        }

        CheckOutcome(row.Outcome, observation);
        row.Expect(observation);
        return true;
    }

    /// <summary>宣言した結果の種類が観測と食い違わないことを確かめる（全行に共通）</summary>
    private static void CheckOutcome(LaunchOutcome outcome, LaunchObservation o)
    {
        if (outcome == LaunchOutcome.Rejected)
        {
            o.Thrown.Should().NotBeNull("断る行は例外で断る");
            o.Started.Should().BeEmpty("断ったら起動しない");
            o.Launched.Should().BeEmpty("断ったら起動しない");
            return;
        }

        o.Thrown.Should().BeNull($"結果が {outcome} の行は例外を出さない");

        switch (outcome)
        {
            case LaunchOutcome.Classified:
                o.Value.Should().BeOfType<bool>();
                break;

            case LaunchOutcome.Resolved:
                o.Value.Should().BeOfType<string>();
                break;

            case LaunchOutcome.NotFound:
                o.Value.Should().BeNull();
                break;

            case LaunchOutcome.Launched:
                (o.Started.Count + o.Launched.Count)
                    .Should()
                    .BePositive("起動した行は起動を記録する");
                break;

            case LaunchOutcome.NotLaunched:
                o.Started.Should().BeEmpty();
                o.Launched.Should().BeEmpty();
                break;

            case LaunchOutcome.Reported:
                o.Dialogs.ErrorMessages.Should().NotBeEmpty("知らせた行はエラーのダイアログを出す");
                break;
        }
    }
}
