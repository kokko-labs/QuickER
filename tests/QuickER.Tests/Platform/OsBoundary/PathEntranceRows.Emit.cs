using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Platform.OsBoundary.PathEntrance;
using static QuickER.Tests.Platform.OsBoundary.PathOutcome;
using static QuickER.Tests.Platform.OsBoundary.PathShape;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>
/// パスの入口の表：API キー方式の提出パスの検証（QuickER.AI.Mock の <c>MockProjectEmitTools.ResolveEmitPath</c> と、
/// internal の <c>MockProjectEmitTools.FindBlockedFolderInNormalizedPath</c>）
/// </summary>
internal static partial class PathEntranceRows
{
    private static readonly MockProjectTargetProfile Wpf = MockProjectTargetProfile.Wpf;
    private static readonly MockProjectTargetProfile Blazor = MockProjectTargetProfile.Blazor;

    private static MockProjectEmitTools.EmitPathResult Result(PathObservation o) =>
        (MockProjectEmitTools.EmitPathResult)o.Value!;

    /// <summary>提出パスの行を作る（<paramref name="extra"/> は元の表明のうち Ok 以外の部分）</summary>
    private static ScenarioRow<PathEntranceCase> Emit(
        PathEntrance entrance,
        string id,
        string description,
        PathShape shape,
        string path,
        bool accepted,
        Action<MockProjectEmitTools.EmitPathResult>? extra = null
    )
    {
        var profile = entrance == EmitPathBlazor ? Blazor : Wpf;

        return Row(
            id,
            description,
            entrance,
            shape,
            accepted ? Accepted : Rejected,
            _ => MockProjectEmitTools.ResolveEmitPath(Work, profile, path),
            o =>
            {
                Result(o).Ok.Should().Be(accepted);
                extra?.Invoke(Result(o));
            }
        );
    }

    /// <summary>同じ形の入力を並べた Theory のデータを 1 行ずつにする</summary>
    private static IEnumerable<ScenarioRow<PathEntranceCase>> EmitCases(
        PathEntrance entrance,
        string idPrefix,
        string description,
        PathShape shape,
        bool accepted,
        string[] paths,
        Action<MockProjectEmitTools.EmitPathResult>? extra = null
    ) =>
        paths.Select(
            (path, index) =>
                Emit(entrance, $"{idPrefix}-{index + 1}", description, shape, path, accepted, extra)
        );

    private static IEnumerable<ScenarioRow<PathEntranceCase>> EmitRows()
    {
        // WPF の UI 層ソース（.xaml / .cs）は受理され、出力フォルダ配下の絶対パスへ解決される
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-wpf-accepts-ui-source-path",
                "WPF: 許可拡張子の相対パスは受理される",
                AllowedRelative,
                true,
                ["MockApp/App.xaml", "MockApp/App.xaml.cs", "MockApp/Views/OrderListView.xaml"],
                WpfAccepted
            )
        )
        {
            yield return row;
        }

        yield return Emit(
            EmitPathWpf,
            "emit-wpf-accepts-ui-source-path-backslash",
            "WPF: 許可拡張子の相対パスは受理される",
            BackslashRelative,
            "MockApp\\ViewModels\\MainViewModel.cs",
            true,
            WpfAccepted
        );

        // Blazor の UI 層ソース（.razor / .css / .cs）は受理される
        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-accepts-ui-source-path",
                "Blazor: 許可拡張子の相対パスは受理される",
                AllowedRelative,
                true,
                [
                    "MockApp/Components/Pages/OrderList.razor",
                    "MockApp/Components/_Imports.razor",
                    "MockApp/wwwroot/style.css",
                    "MockApp/Program.cs",
                ],
                r => r.FullPath.Should().StartWith(Work)
            )
        )
        {
            yield return row;
        }

        // 絶対パス・ドライブ文字・先頭スラッシュ・空
        yield return Emit(
            EmitPathWpf,
            "emit-rejects-absolute-drive",
            "絶対・ドライブ・先頭スラッシュ・空は拒否",
            Absolute,
            @"C:\Windows\evil.cs",
            false
        );
        yield return Emit(
            EmitPathWpf,
            "emit-rejects-absolute-leading-slash",
            "絶対・ドライブ・先頭スラッシュ・空は拒否",
            Absolute,
            "/etc/passwd",
            false
        );
        yield return Emit(
            EmitPathWpf,
            "emit-rejects-drive-relative",
            "絶対・ドライブ・先頭スラッシュ・空は拒否",
            DriveRelative,
            "D:relative.cs",
            false
        );
        yield return Emit(
            EmitPathWpf,
            "emit-rejects-empty",
            "絶対・ドライブ・先頭スラッシュ・空は拒否",
            EmptyOrWhitespace,
            "",
            false
        );
        yield return Emit(
            EmitPathWpf,
            "emit-rejects-whitespace",
            "絶対・ドライブ・先頭スラッシュ・空は拒否",
            EmptyOrWhitespace,
            "   ",
            false
        );

        yield return Emit(
            EmitPathWpf,
            "emit-rejects-parent-traversal",
            "'..' トラバーサルは拒否",
            ParentTraversal,
            "MockApp/../../evil.cs",
            false
        );

        // スキャフォールド成果物
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-scaffold-owned-path",
                "スキャフォールド成果物への書き込みは拒否",
                ScaffoldFile,
                false,
                [
                    "MockApp/Generated/Entities.cs",
                    "MockApp/design/mock/OrderList.html",
                    "MockApp/README-QuickER.md",
                    "MockApp/MockApp.csproj",
                    "MockApp.sln",
                    "NuGet.Config",
                    "MockApp/nuget.config",
                ],
                r => r.Error.Should().NotBeNullOrWhiteSpace()
            )
        )
        {
            yield return row;
        }

        // MSBuild が自動 import する制御ファイル（ビルド検証がそのままユーザー権限のコード実行になるもの）
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-build-control-file",
                "MSBuild 制御ファイル・設定ファイルは拒否",
                BuildControlFile,
                false,
                [
                    "Directory.Build.props",
                    "MockApp/Directory.Build.targets",
                    "Directory.Packages.props",
                    "global.json",
                    "Directory.Build.rsp",
                    "MockApp/appsettings.json",
                    "MockApp/Setup.ps1",
                ],
                r => r.Error.Should().NotBeNullOrWhiteSpace()
            )
        )
        {
            yield return row;
        }

        // ビルドの入出力フォルダは拡張子に依らず拒否
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-build-folder",
                "obj/・bin/ 配下は拒否",
                BuildFolder,
                false,
                [
                    "MockApp/obj/project.assets.json",
                    "MockApp/obj/Debug/Evil.cs",
                    "MockApp/bin/x.cs",
                    "bin/App.xaml",
                ],
                r => r.Error.Should().Contain("folder")
            )
        )
        {
            yield return row;
        }

        // Windows がセグメント末尾の "." / 空白を落として開く・8.3 短縮名で実在フォルダを指せることを使った保護フォルダのすり抜け
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-protected-folder-alias",
                "末尾 . / 空白・8.3 短縮名による保護フォルダのすり抜けは拒否",
                ProtectedAlias,
                false,
                [
                    "Generated./x.cs",
                    "Generated /x.cs",
                    "MockApp/Generated. ./Entities.cs",
                    "MockApp/obj../Evil.cs",
                    "MockApp/bin\t/x.cs",
                    "GENERA~1/x.cs",
                    "MockApp/OBJ~1/Evil.cs",
                    "design./mock/x.cs",
                    "MockApp/Pages./Orders.xaml",
                    "MockApp/Views/Order.xaml.",
                ],
                r => r.Error.Should().NotBeNullOrWhiteSpace()
            )
        )
        {
            yield return row;
        }

        // すり抜け対策の後も、正当な UI 層のパスは通る
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-wpf-accepts-legitimate-path-after-alias-guards",
                "正当な UI 層パスは引き続き通る",
                LegitimatePath,
                true,
                [
                    "./MockApp/Views/OrderList.xaml",
                    "MockApp//Views/OrderList.xaml",
                    "MockApp/./Views/OrderList.xaml",
                    " MockApp/Views/OrderList.xaml ",
                    "MockApp/Views/Order.Detail.xaml.cs",
                    "MockApp/Generated.Views/OrderList.xaml",
                ]
            )
        )
        {
            yield return row;
        }

        // 元のテストの 2 つ目の表明（Blazor の固定のパス）は引数に依らず毎回同じなので 1 行にまとめる
        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-accepts-legitimate-path-after-alias-guards",
            "正当な UI 層パスは引き続き通る",
            LegitimatePath,
            "Components/Pages/Orders.razor",
            true
        );

        // 空の区間と "." の区間を落とした後のパスで保護フォルダを照合する
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-protected-folder-after-dropping-dot-segments",
                "'.' と空の区間を落とした後も、保護フォルダは断る",
                DroppedSegmentsProtected,
                false,
                [
                    "./Generated/x.cs",
                    "MockApp/./Generated/Entities.cs",
                    ".//obj/x.cs",
                    "MockApp//design/mock/x.cs",
                ],
                r => r.Error.Should().Contain("folder")
            )
        )
        {
            yield return row;
        }

        // "." と空の区間を落とすと何も残らないパスは、空のパスとして断る
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-only-dot-segments-as-empty",
                "'.' だけ・'./' だけのパスは空として断る",
                DotsOnly,
                false,
                [".", "./", "././", @".\"],
                r =>
                    r.Error.Should()
                        .Contain(
                            PathSegments.Describe(
                                PathSegmentViolation.Empty,
                                CultureInfo.InvariantCulture
                            )
                        )
            )
        )
        {
            yield return row;
        }

        // 正規化後の出力フォルダ相対セグメントでの保護フォルダの照合（セグメント検査の二重化）
        yield return Blocked(
            "find-blocked-folder-finds-generated-segment",
            @"C:\work\out\MockApp\Generated\Entities.cs",
            "Generated"
        );
        yield return Blocked(
            "find-blocked-folder-finds-obj-segment",
            @"C:\work\out\obj\x.cs",
            "obj"
        );
        yield return Blocked(
            "find-blocked-folder-finds-design-segment-case-insensitively",
            @"C:\work\out\MockApp\DESIGN\mock\x.cs",
            "DESIGN"
        );
        yield return Blocked(
            "find-blocked-folder-finds-nothing-in-ui-path",
            @"C:\work\out\MockApp\Views\OrderList.xaml",
            null
        );

        // 拡張子のないファイルは拒否
        foreach (
            var row in EmitCases(
                EmitPathWpf,
                "emit-rejects-extensionless-file",
                "拡張子なしのファイルは拒否",
                Extensionless,
                false,
                ["MockApp/Dockerfile", "Makefile"]
            )
        )
        {
            yield return row;
        }

        // ターゲット外の UI 拡張子
        yield return Emit(
            EmitPathWpf,
            "emit-wpf-rejects-razor-extension",
            "ターゲット外の UI 拡張子は拒否",
            OtherTargetExtension,
            "MockApp/Components/Pages/OrderList.razor",
            false
        );
        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-rejects-xaml-extension",
            "ターゲット外の UI 拡張子は拒否",
            OtherTargetExtension,
            "MockApp/App.xaml",
            false
        );

        // 拒否メッセージが許可拡張子を列挙する（AI が次の提出を正せる）
        yield return Emit(
            EmitPathWpf,
            "emit-wpf-rejection-message-lists-allowed-extensions",
            "拒否メッセージは許可拡張子を列挙する",
            BuildControlFile,
            "Directory.Build.props",
            false,
            r =>
            {
                r.Error.Should().Contain(".cs").And.Contain(".xaml");
                r.Error.Should().NotContain(".razor");
            }
        );
        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-rejection-message-lists-allowed-extensions",
            "拒否メッセージは許可拡張子を列挙する",
            BuildControlFile,
            "Directory.Build.props",
            false,
            r => r.Error.Should().Contain(".cs").And.Contain(".css").And.Contain(".razor")
        );

        // Blazor のプロファイルでも、拡張子以外の検査は WPF と同じ経路を通る。
        // 同じ形の WPF の行と同じ入力を、Blazor の入力として自然な拡張子（.razor / .css）に替えて流す（期待は WPF の行と同じ）
        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-accepts-ui-source-path-backslash",
            "Blazor: 許可拡張子の相対パスは受理される",
            BackslashRelative,
            "MockApp\\Components\\Pages\\OrderList.razor",
            true,
            r =>
            {
                r.RelativePath.Should().NotContain("\\");
                r.FullPath.Should().StartWith(Work);
            }
        );

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-absolute",
                "絶対・ドライブ・先頭スラッシュは拒否",
                Absolute,
                false,
                [@"C:\Windows\evil.cs", "/etc/passwd", "/MockApp/Pages/Evil.razor"]
            )
        )
        {
            yield return row;
        }

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-build-folder",
                "obj/・bin/ 配下は拒否",
                BuildFolder,
                false,
                [
                    "MockApp/obj/project.assets.json",
                    "MockApp/obj/Debug/Evil.cs",
                    "MockApp/bin/x.razor",
                    "bin/App.razor",
                ],
                r => r.Error.Should().Contain("folder")
            )
        )
        {
            yield return row;
        }

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-only-dot-segments-as-empty",
                "'.' だけ・'./' だけのパスは空として断る",
                DotsOnly,
                false,
                [".", "./", "././", @".\"],
                r =>
                    r.Error.Should()
                        .Contain(
                            PathSegments.Describe(
                                PathSegmentViolation.Empty,
                                CultureInfo.InvariantCulture
                            )
                        )
            )
        )
        {
            yield return row;
        }

        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-rejects-drive-relative",
            "ドライブ相対は拒否",
            DriveRelative,
            "D:relative.razor",
            false
        );

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-protected-folder-after-dropping-dot-segments",
                "'.' と空の区間を落とした後も、保護フォルダは断る",
                DroppedSegmentsProtected,
                false,
                [
                    "./Generated/x.cs",
                    "MockApp/./Generated/Entities.cs",
                    ".//obj/x.css",
                    "MockApp//design/mock/x.razor",
                ],
                r => r.Error.Should().Contain("folder")
            )
        )
        {
            yield return row;
        }

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-empty-or-whitespace",
                "空・空白だけのパスは拒否",
                EmptyOrWhitespace,
                false,
                ["", "   "]
            )
        )
        {
            yield return row;
        }

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-extensionless-file",
                "拡張子なしのファイルは拒否",
                Extensionless,
                false,
                ["MockApp/Dockerfile", "Makefile"]
            )
        )
        {
            yield return row;
        }

        yield return Emit(
            EmitPathBlazor,
            "emit-blazor-rejects-parent-traversal",
            "'..' トラバーサルは拒否",
            ParentTraversal,
            "MockApp/../../evil.razor",
            false
        );

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-protected-folder-alias",
                "末尾 . / 空白・8.3 短縮名による保護フォルダのすり抜けは拒否",
                ProtectedAlias,
                false,
                [
                    "Generated./x.cs",
                    "Generated /x.cs",
                    "MockApp/Generated. ./Entities.cs",
                    "MockApp/obj../Evil.cs",
                    "MockApp/bin\t/x.cs",
                    "GENERA~1/x.cs",
                    "MockApp/OBJ~1/Evil.cs",
                    "design./mock/x.cs",
                    "MockApp/Pages./Orders.razor",
                    "MockApp/Views/Order.razor.",
                ],
                r => r.Error.Should().NotBeNullOrWhiteSpace()
            )
        )
        {
            yield return row;
        }

        foreach (
            var row in EmitCases(
                EmitPathBlazor,
                "emit-blazor-rejects-scaffold-owned-path",
                "スキャフォールド成果物への書き込みは拒否",
                ScaffoldFile,
                false,
                [
                    "MockApp/Generated/Entities.cs",
                    "MockApp/design/mock/OrderList.html",
                    "MockApp/README-QuickER.md",
                    "MockApp/MockApp.csproj",
                    "MockApp.sln",
                    "NuGet.Config",
                    "MockApp/nuget.config",
                ],
                r => r.Error.Should().NotBeNullOrWhiteSpace()
            )
        )
        {
            yield return row;
        }
    }

    /// <summary>受理された WPF の提出パスの表明（区切りの正規化と出力フォルダ配下への解決）</summary>
    private static void WpfAccepted(MockProjectEmitTools.EmitPathResult result)
    {
        result.RelativePath.Should().NotContain("\\");
        result.FullPath.Should().StartWith(Work);
    }

    /// <summary>正規化後の保護フォルダの照合の行を作る</summary>
    private static ScenarioRow<PathEntranceCase> Blocked(
        string id,
        string full,
        string? expected
    ) =>
        Row(
            id,
            "正規化後のパスでも保護フォルダを検出する",
            FindBlockedFolder,
            expected is null ? NormalizedUnprotected : NormalizedProtectedSegment,
            expected is null ? NotFound : Found,
            _ => MockProjectEmitTools.FindBlockedFolderInNormalizedPath(Work, full),
            o => ((string?)o.Value).Should().Be(expected)
        );
}
