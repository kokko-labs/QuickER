using System.IO;
using System.Security.AccessControl;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>最終ビルドの境界の表の入口 C（成果物の探索）と D（スキャフォールドの上書きの走査）</summary>
/// <remarks>
/// 成果物の探索の 3 つの性質＝アクセス拒否を飛ばす・起点直下の obj / bin を数えない・範囲は起点フォルダ配下だけ
/// （素の <c>Directory.EnumerateFiles(..., AllDirectories)</c> は権限のないサブフォルダで例外を投げ、ビルド中間物も無関係な同居ファイルも数える）。
/// 上書きの走査は、スキャフォールドが無条件に上書きするもの（ソリューション・csproj・README-QuickER.md・Generated/・design/mock/）と
/// 直下の別ソリューションを列挙する（確認ダイアログの判断材料）。
/// </remarks>
internal static partial class MockBuildBoundaryRows
{
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> Enumerate() =>
        [
            SyncRow(
                "enumerate-excludes-obj-and-bin",
                "ビルド中間物（起点直下の obj / bin）配下のファイルを成果物として数えない",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var root = c.Folder;
                    MockBuildBoundaryContext.Write(Path.Combine(root, "Components", "Home.razor"));
                    MockBuildBoundaryContext.Write(
                        Path.Combine(root, "obj", "Debug", "Generated.razor")
                    );
                    MockBuildBoundaryContext.Write(
                        Path.Combine(root, "bin", "Debug", "Copied.razor")
                    );

                    var files = MockProjectFiles.Enumerate(root, "*.razor").ToList();

                    files.Should().ContainSingle();
                    files[0].Should().EndWith("Home.razor");
                }
            ),
            SyncRow(
                "enumerate-excludes-only-top-level-obj-and-bin",
                "除外は起点直下の obj / bin だけで、深い階層の同名フォルダ（Components/bin）は数える（最終ビルドの前に消す 2 つと揃える）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var root = c.Folder;
                    MockBuildBoundaryContext.Write(
                        Path.Combine(root, "obj", "Debug", "Generated.razor")
                    );
                    MockBuildBoundaryContext.Write(
                        Path.Combine(root, "Components", "bin", "Nested.razor")
                    );

                    var files = MockProjectFiles.Enumerate(root, "*.razor").ToList();

                    files.Should().ContainSingle("起点直下の obj だけを除外する");
                    files[0].Should().EndWith("Nested.razor");
                }
            ),
            SyncRow(
                "enumerate-has-any-does-not-look-outside-root",
                "探索の範囲は起点フォルダ配下に限られる（出力フォルダ直下＝プロジェクトフォルダの外の同名ファイルを拾わない）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Plain,
                c =>
                {
                    MockBuildBoundaryContext.Write(Path.Combine(c.Folder, "Unrelated.csproj"));
                    Directory.CreateDirectory(c.ProjectDirectory);

                    MockProjectFiles.HasAny(c.ProjectDirectory, "*.csproj").Should().BeFalse();

                    MockBuildBoundaryContext.Write(
                        Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj")
                    );

                    MockProjectFiles.HasAny(c.ProjectDirectory, "*.csproj").Should().BeTrue();
                }
            ),
            SyncRow(
                "enumerate-missing-root-returns-empty",
                "起点フォルダが無い・空文字・空白だけでも例外にならず、空／false を返す",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Absent,
                c =>
                {
                    MockProjectFiles.Enumerate(string.Empty, "*.cs").Should().BeEmpty();
                    MockProjectFiles
                        .Enumerate(Path.Combine(c.Folder, "missing"), "*.cs")
                        .Should()
                        .BeEmpty();
                    MockProjectFiles.HasAny("   ", "*.cs").Should().BeFalse();
                }
            ),
            SyncRow(
                "enumerate-acl-denied-subfolder-does-not-throw",
                "アクセス拒否のサブフォルダがあっても例外にならず、読めるファイルだけを返す（素の再帰列挙が実際に投げることも確かめる）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    var root = c.Folder;
                    var denied = Path.Combine(root, "denied");
                    MockBuildBoundaryContext.Write(Path.Combine(root, "Visible.csproj"));
                    MockBuildBoundaryContext.Write(Path.Combine(denied, "Hidden.csproj"));
                    c.Deny(
                        denied,
                        FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                    );

                    // 素の列挙は落ちる（＝共有ヘルパーが解決している問題が実在することの確認）
                    var raw = () =>
                        Directory
                            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                            .ToList();
                    raw.Should().Throw<UnauthorizedAccessException>();

                    var files = MockProjectFiles.Enumerate(root, "*.csproj").ToList();

                    files.Should().ContainSingle();
                    files[0].Should().EndWith("Visible.csproj");
                }
            ),
            SyncRow(
                "enumerate-unlistable-reparse-folder-does-not-throw",
                "中を列挙できない reparse フォルダがあっても HasAny / Enumerate は例外にならず、ほかのファイルを返す（素の再帰列挙は IOException で止まる）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.UnlistableReparse,
                c =>
                {
                    var root = c.Folder;
                    MockBuildBoundaryContext.Write(Path.Combine(root, $"{ProjectName}.csproj"));
                    MockBuildBoundaryContext.Write(Path.Combine(root, "Components", "Home.razor"));
                    var cache = Path.Combine(root, "cache");
                    Directory.CreateDirectory(cache);
                    ReparsePointTestHelper.MakeUnlistableReparseDirectory(cache);

                    // 素の列挙は落ちる（＝共有ヘルパーが解決している問題が実在することの確認）
                    var raw = () =>
                        Directory
                            .EnumerateFiles(
                                root,
                                "*.razor",
                                new EnumerationOptions
                                {
                                    RecurseSubdirectories = true,
                                    IgnoreInaccessible = true,
                                }
                            )
                            .ToList();
                    raw.Should().Throw<IOException>();

                    MockProjectFiles.HasAny(root, "*.csproj").Should().BeTrue();
                    MockProjectFiles.HasAny(root, "*.xaml").Should().BeFalse();
                    MockProjectFiles
                        .Enumerate(root, "*.razor")
                        .Should()
                        .ContainSingle()
                        .Which.Should()
                        .EndWith("Home.razor");
                }
            ),
            SyncRow(
                "enumerate-does-not-follow-junctions",
                "ジャンクションの先は数えない（辿ると、外を指す 1 つで出力フォルダの外を走査し、外のファイルで成果物の検証が通る）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Junction,
                c =>
                {
                    var outside = c.NewFolder();
                    MockBuildBoundaryContext.Write(Path.Combine(outside, "Outside.razor"));
                    ReparsePointTestHelper.CreateJunction(
                        Path.Combine(c.Folder, "linked"),
                        outside
                    );

                    MockProjectFiles.HasAny(c.Folder, "*.razor").Should().BeFalse();
                    File.Exists(Path.Combine(outside, "Outside.razor")).Should().BeTrue();
                }
            ),
            SyncRow(
                "enumerate-traverses-non-link-reparse-folder",
                "リンクでない reparse フォルダ（OneDrive の Files On-Demand と同じ形）の中は数える（属性ビットだけで止めると、そこへ書かれた成果物が見えなくなる）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.NonLinkReparse,
                c =>
                {
                    var cloud = Path.Combine(c.Folder, "Components");
                    MockBuildBoundaryContext.Write(Path.Combine(cloud, "Home.razor"));
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(cloud);

                    MockProjectFiles.HasAny(c.Folder, "*.razor").Should().BeTrue();
                }
            ),
            SyncRow(
                "enumerate-is-link-plain-folder-is-false",
                "IsLink は通常のフォルダを偽にする",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var plain = Path.Combine(c.Folder, "plain");
                    Directory.CreateDirectory(plain);

                    MockProjectFiles.IsLink(new DirectoryInfo(plain)).Should().BeFalse();
                }
            ),
            SyncRow(
                "enumerate-is-link-junction-is-true",
                "IsLink はジャンクションを真にする",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Junction,
                c =>
                {
                    var junction = Path.Combine(c.Folder, "junction");
                    ReparsePointTestHelper.CreateJunction(junction, c.NewFolder());

                    MockProjectFiles.IsLink(new DirectoryInfo(junction)).Should().BeTrue();
                }
            ),
            SyncRow(
                "enumerate-is-link-non-link-reparse-folder-is-false",
                "IsLink はリンクでない reparse フォルダを偽にする（判定は LinkTarget で、属性ビットだけで見るとリンクと区別できない）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.NonLinkReparse,
                c =>
                {
                    var nonLink = Path.Combine(c.Folder, "cloud");
                    MockBuildBoundaryContext.Write(Path.Combine(nonLink, "inside.txt"));
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(nonLink);

                    MockProjectFiles.IsLink(new DirectoryInfo(nonLink)).Should().BeFalse();
                    new DirectoryInfo(nonLink)
                        .Attributes.HasFlag(FileAttributes.ReparsePoint)
                        .Should()
                        .BeTrue();
                }
            ),
            SyncRow(
                "enumerate-is-link-file-symlink-is-true",
                "IsLink はファイルのシンボリックリンクを真にし、リンク先のファイルは偽にする（作成には特権が要る）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.FileSymlink,
                c =>
                {
                    var fileTarget = Path.Combine(c.NewFolder(), "target.txt");
                    MockBuildBoundaryContext.Write(fileTarget);
                    var fileLink = Path.Combine(c.Folder, "link.txt");
                    File.CreateSymbolicLink(fileLink, fileTarget);

                    MockProjectFiles.IsLink(new FileInfo(fileLink)).Should().BeTrue();
                    MockProjectFiles.IsLink(new FileInfo(fileTarget)).Should().BeFalse();
                }
            ),
        ];

    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> Overwrite() =>
        [
            SyncRow(
                "overwrite-empty-or-missing-requires-no-confirmation",
                "空のフォルダ・存在しないフォルダ・空文字では上書きの確認が要らない",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Absent,
                c =>
                {
                    MockProjectOverwriteScanner
                        .Scan(c.Folder, ProjectName)
                        .RequiresConfirmation.Should()
                        .BeFalse();
                    MockProjectOverwriteScanner
                        .Scan(Path.Combine(c.Folder, "missing"), ProjectName)
                        .RequiresConfirmation.Should()
                        .BeFalse();
                    MockProjectOverwriteScanner
                        .Scan(string.Empty, ProjectName)
                        .RequiresConfirmation.Should()
                        .BeFalse();
                }
            ),
            SyncRow(
                "overwrite-same-project-name-lists-overwritten-paths",
                "同じプロジェクト名での再生成では、上書きされる既存物をすべて列挙する（手書きのコードは列挙しない）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var folder = c.Folder;
                    var project = c.ProjectDirectory;
                    MockBuildBoundaryContext.Write(Path.Combine(folder, $"{ProjectName}.sln"));
                    MockBuildBoundaryContext.Write(Path.Combine(project, $"{ProjectName}.csproj"));
                    MockBuildBoundaryContext.Write(
                        Path.Combine(project, MockProjectScaffoldService.ReadmeFileName)
                    );
                    MockBuildBoundaryContext.Write(
                        Path.Combine(project, "Generated", "Entities.g.cs")
                    );
                    MockBuildBoundaryContext.Write(
                        Path.Combine(project, "design", "mock", "mock.json")
                    );
                    // 上書きの対象ではない手書きのコード（列挙しない）
                    MockBuildBoundaryContext.Write(Path.Combine(project, "Program.cs"));

                    var scan = MockProjectOverwriteScanner.Scan(folder, ProjectName);

                    scan.RequiresConfirmation.Should().BeTrue();
                    scan.ExistingPaths.Should()
                        .Equal(
                            $"{ProjectName}.sln",
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj"),
                            Path.Combine(ProjectName, MockProjectScaffoldService.ReadmeFileName),
                            Path.Combine(ProjectName, "Generated") + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, "design", "mock")
                                + Path.DirectorySeparatorChar
                        );
                }
            ),
            SyncRow(
                "overwrite-foreign-solutions-at-root-are-listed",
                "出力フォルダ直下の別ソリューション・別プロジェクトも確認の対象にする（直下でないものは同居の判断材料にならないので対象外）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var folder = c.Folder;
                    MockBuildBoundaryContext.Write(Path.Combine(folder, "Other.sln"));
                    MockBuildBoundaryContext.Write(Path.Combine(folder, "Legacy.slnx"));
                    MockBuildBoundaryContext.Write(Path.Combine(folder, "Library.csproj"));
                    MockBuildBoundaryContext.Write(Path.Combine(folder, "nested", "Deep.csproj"));

                    var scan = MockProjectOverwriteScanner.Scan(folder, ProjectName);

                    scan.RequiresConfirmation.Should().BeTrue();
                    scan.ExistingPaths.Should().Equal("Legacy.slnx", "Library.csproj", "Other.sln");
                }
            ),
            SyncRow(
                "overwrite-existing-empty-project-folder-requires-confirmation",
                "プロジェクトフォルダが既にあれば、中身が空でも「そこへ書き込む」ことを確認の対象にする",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Plain,
                c =>
                {
                    Directory.CreateDirectory(c.ProjectDirectory);

                    var scan = MockProjectOverwriteScanner.Scan(c.Folder, ProjectName);

                    scan.RequiresConfirmation.Should().BeTrue();
                    scan.ExistingPaths.Should().Equal(ProjectName + Path.DirectorySeparatorChar);
                }
            ),
            SyncRow(
                "overwrite-acl-denied-output-folder-does-not-throw",
                "出力フォルダ自体が列挙を拒否していても走査は例外にならない（走査は生成コマンドの try の外から呼ばれる。素の列挙が実際に投げることも確かめる）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    var folder = c.Folder;
                    MockBuildBoundaryContext.Write(Path.Combine(folder, "Other.csproj"));
                    c.Deny(folder);

                    // 素の列挙は落ちる（＝オプションが解決している問題が実在することの確認）
                    var raw = () => Directory.EnumerateFiles(folder, "*.csproj").ToList();
                    raw.Should().Throw<UnauthorizedAccessException>();

                    var scan = () => MockProjectOverwriteScanner.Scan(folder, ProjectName);
                    scan.Should().NotThrow();
                }
            ),
        ];
}
