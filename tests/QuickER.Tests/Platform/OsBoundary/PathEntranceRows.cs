using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>パスの入口の表（OS との境界のエンジンの PathEntrances の部分）</summary>
/// <remarks>
/// SUT は QuickER.AI.Mock の 3 つの入口：<c>MockPreviewPanel</c>（<c>IsNavigationAllowed</c>・<c>IsResourceAllowed</c>）・
/// <c>MockProjectEmitTools</c>（<c>ResolveEmitPath</c>・internal の <c>FindBlockedFolderInNormalizedPath</c>）・
/// <c>ApiKeyMockProjectAgent.ReadScreenHtml</c>。
/// </remarks>
internal enum PathEntrance
{
    /// <summary><c>MockPreviewPanel.IsNavigationAllowed</c>（遷移の許可）</summary>
    PreviewNavigation,

    /// <summary><c>MockPreviewPanel.IsResourceAllowed</c>（サブリソースの許可）</summary>
    PreviewResource,

    /// <summary><c>MockProjectEmitTools.ResolveEmitPath</c>（WPF のプロファイル）</summary>
    EmitPathWpf,

    /// <summary><c>MockProjectEmitTools.ResolveEmitPath</c>（Blazor のプロファイル）</summary>
    EmitPathBlazor,

    /// <summary><c>MockProjectEmitTools.FindBlockedFolderInNormalizedPath</c>（正規化後の 2 層目）</summary>
    FindBlockedFolder,

    /// <summary><c>ApiKeyMockProjectAgent.ReadScreenHtml</c>（実ファイル）</summary>
    ReadScreenHtml,
}

/// <summary>入口が受け取る入力の種類（次元表の <c>isValid</c> が入口と形の組を絞るのに使う）</summary>
internal enum PathInputKind
{
    /// <summary>プレビューへ届く URI と許可フォルダ</summary>
    UriTarget,

    /// <summary>AI が指定した相対パス（文字列）</summary>
    EmitRelative,

    /// <summary>正規化済みの出力フォルダ配下の絶対パス</summary>
    NormalizedFull,

    /// <summary>マニフェストの画面のファイル名</summary>
    ScreenFile,
}

/// <summary>入力の形（次元表の因子 Shape の水準）</summary>
internal enum PathShape
{
    /// <summary>許可フォルダ直下のファイル（html を含む）</summary>
    DirectChild,

    /// <summary>#fragment 付きの同一ファイル</summary>
    Fragment,

    /// <summary>サブフォルダ配下</summary>
    SubFolder,

    /// <summary>許可フォルダの外（親フォルダ）</summary>
    OutsideRoot,

    /// <summary><c>..</c> で外へ抜ける</summary>
    ParentTraversal,

    /// <summary>http / https のスキーム</summary>
    HttpScheme,

    /// <summary>ws / wss のスキーム</summary>
    WebSocketScheme,

    /// <summary>html でないファイル（プレビュー・画面ファイルの入力）</summary>
    NonHtml,

    /// <summary>許可フォルダが空文字</summary>
    EmptyRoot,

    /// <summary>許可フォルダが null</summary>
    NullRoot,

    /// <summary>対象が null</summary>
    NullTarget,

    /// <summary>許可拡張子の相対パス（UI 層のソース）</summary>
    AllowedRelative,

    /// <summary>区切りがバックスラッシュの相対パス</summary>
    BackslashRelative,

    /// <summary>絶対パス（ドライブつき・先頭スラッシュ）</summary>
    Absolute,

    /// <summary>ドライブ相対（<c>D:relative.cs</c>）</summary>
    DriveRelative,

    /// <summary>空・空白だけ</summary>
    EmptyOrWhitespace,

    /// <summary>足場が持つファイル・フォルダ（Generated・design・README・csproj・sln・NuGet.Config）</summary>
    ScaffoldFile,

    /// <summary>ビルド制御のファイル・設定ファイル（Directory.Build.props など）</summary>
    BuildControlFile,

    /// <summary>ビルドのフォルダ（obj・bin）</summary>
    BuildFolder,

    /// <summary>保護フォルダの別名（末尾の <c>.</c>・空白・8.3 短縮名など）</summary>
    ProtectedAlias,

    /// <summary><c>.</c> と空の区間を落としても正当なパス</summary>
    LegitimatePath,

    /// <summary><c>.</c> と空の区間を落とした後の保護フォルダ</summary>
    DroppedSegmentsProtected,

    /// <summary><c>.</c> だけ・<c>./</c> だけ</summary>
    DotsOnly,

    /// <summary>拡張子なし</summary>
    Extensionless,

    /// <summary>別ターゲットの拡張子</summary>
    OtherTargetExtension,

    /// <summary>正規化後の保護セグメント</summary>
    NormalizedProtectedSegment,

    /// <summary>正規化後に保護セグメントが無い</summary>
    NormalizedUnprotected,

    /// <summary>直下の正しい画面ファイル</summary>
    ValidScreenFile,
}

/// <summary>入口の結果の種類（行が宣言し、流す側が戻り値から導いた結果と照合する）</summary>
internal enum PathOutcome
{
    /// <summary>受け付ける（許可・Ok・中身を読んだ）</summary>
    Accepted,

    /// <summary>断る（拒否・Ok でない・空文字）</summary>
    Rejected,

    /// <summary>保護セグメントの名前を返した</summary>
    Found,

    /// <summary>見つからない（null）</summary>
    NotFound,
}

/// <summary>1 行を流した観測</summary>
internal sealed class PathObservation : IDisposable
{
    private TestTempFolder? _folder;

    /// <summary>入口の戻り値（<c>bool</c>・<c>EmitPathResult</c>・<c>string?</c>）</summary>
    public object? Value { get; set; }

    /// <summary>入口が投げた例外</summary>
    public Exception? Thrown { get; set; }

    /// <summary>実ファイルを作る行のための一時フォルダ（初めて読んだときに作る）</summary>
    public string TempFolder => (_folder ??= new TestTempFolder()).Path;

    /// <inheritdoc />
    public void Dispose() => _folder?.Dispose();
}

/// <summary>パスの入口の表の 1 行の入力</summary>
/// <param name="Entrance">入口</param>
/// <param name="Shape">入力の形</param>
/// <param name="Outcome">結果の種類（戻り値から導いて照合する）</param>
/// <param name="Act">入口を呼ぶ（戻り値は <see cref="PathObservation.Value"/> に入る）</param>
/// <param name="Expect">元のテストの表明</param>
internal sealed record PathEntranceCase(
    PathEntrance Entrance,
    PathShape Shape,
    PathOutcome Outcome,
    Func<PathObservation, object?> Act,
    Action<PathObservation> Expect
);

/// <summary>パスの入口の表（入口 × 入力の形 → 結果）</summary>
/// <remarks>
/// 行は元の単発のテストと同じ入力・同じ表明を持つ（主題ごとのファイル：<c>PathEntranceRows.Preview.cs</c>・
/// <c>PathEntranceRows.Emit.cs</c>・<c>PathEntranceRows.Screen.cs</c>）。
/// 流す側（<see cref="PathEntranceRunner"/>）は、行の表明の前に、入口の戻り値から導いた結果の種類が宣言と食い違わないことを全行に掛ける。
/// </remarks>
internal static partial class PathEntranceRows
{
    /// <summary>プレビューの許可フォルダ（元のテストと同じ）</summary>
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "QuickERMockRoot");

    /// <summary>出力フォルダ（元のテストと同じ）</summary>
    private const string Work = @"C:\work\out";

    /// <summary>表</summary>
    public static ScenarioTable<PathEntranceCase> Table =>
        new(EngineIds.OsBoundary, [.. PreviewRows(), .. EmitRows(), .. ScreenRows()]);

    /// <summary>行を作る</summary>
    private static ScenarioRow<PathEntranceCase> Row(
        string id,
        string description,
        PathEntrance entrance,
        PathShape shape,
        PathOutcome outcome,
        Func<PathObservation, object?> act,
        Action<PathObservation> expect
    ) => new(id, null, description, new PathEntranceCase(entrance, shape, outcome, act, expect));
}

/// <summary>パスの入口の表の行を流す</summary>
internal static class PathEntranceRunner
{
    /// <summary>1 行を流す（結果の種類の照合 → 行の表明）</summary>
    public static void Run(PathEntranceCase row)
    {
        using var observation = new PathObservation();

        try
        {
            observation.Value = row.Act(observation);
        }
        catch (Exception ex)
        {
            observation.Thrown = ex;
        }

        observation.Thrown.Should().BeNull("パスの入口は例外でなく結果の種類で断る");
        Derive(row.Entrance, observation.Value)
            .Should()
            .Be(row.Outcome, "宣言した結果の種類は入口の戻り値から導いた結果と一致する");
        row.Expect(observation);
    }

    /// <summary>入口の戻り値から結果の種類を導く</summary>
    private static PathOutcome Derive(PathEntrance entrance, object? value) =>
        entrance switch
        {
            PathEntrance.PreviewNavigation or PathEntrance.PreviewResource => (bool)value!
                ? PathOutcome.Accepted
                : PathOutcome.Rejected,
            PathEntrance.EmitPathWpf or PathEntrance.EmitPathBlazor => (
                (MockProjectEmitTools.EmitPathResult)value!
            ).Ok
                ? PathOutcome.Accepted
                : PathOutcome.Rejected,
            PathEntrance.FindBlockedFolder => value is null
                ? PathOutcome.NotFound
                : PathOutcome.Found,
            PathEntrance.ReadScreenHtml => string.IsNullOrEmpty((string?)value)
                ? PathOutcome.Rejected
                : PathOutcome.Accepted,
            _ => throw new ArgumentOutOfRangeException(nameof(entrance), entrance, null),
        };
}
