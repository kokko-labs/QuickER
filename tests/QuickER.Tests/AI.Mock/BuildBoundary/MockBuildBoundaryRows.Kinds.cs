using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>
/// 最終ビルドの境界の表：入口 A〜D に、ほかの行が流していない種別（読み取り専用・ロック・無い・隠し・リンク・reparse・拒否）を置く行
/// </summary>
/// <remarks>
/// <para>
/// 期待は src/QuickER.AI.Mock/CLAUDE.md（「最終ビルド」「成果物の探索」「最終ビルド前の変更検知」）と製品の XmlDoc から導く。
/// 文書に定めが無く今の振る舞いを写すだけの行は、説明の先頭に「特性の固定（文書に定めが無い今の振る舞い）：」と書き、根拠のファイル:行を添える。
/// </para>
/// <para>
/// 属性・ACL・ハンドル・リンクは文脈（<see cref="MockBuildBoundaryContext"/>）の部品で作り、行の本体が失敗して抜けても文脈の破棄
/// （ハンドルを閉じる → 拒否の ACE を外す → reparse point・リンク・読み取り専用を外しながら消す）が元へ戻す。
/// </para>
/// </remarks>
internal static partial class MockBuildBoundaryRows
{
    /// <summary>入口 A：変更検知（読み取り専用・ロック・出力フォルダが無い）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> ChangeKinds() =>
        [
            Row(
                "change-read-only-csproj-user-is-confirmed",
                "読み取り専用の属性を付けた .csproj.user も検知して確認する（比較は内容の指紋で、属性で検知を避けられない）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.ReadOnly,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」（UI 層のソース・静的資産以外の追加・変更を確認する・比較は内容の SHA-256）
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                        MockBuildBoundaryContext.WriteReadOnly(
                            Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user")
                        )
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/{ProjectName}.csproj.user");
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-locked-csproj-user-added-is-confirmed",
                "削除の共有を許さないハンドルで掴まれたまま足された .csproj.user も検知して確認する（読めるので内容の指紋を取る）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」（追加を確認する）
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                        c.WriteAndLock(Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"))
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/{ProjectName}.csproj.user");
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-unreadable-existing-file-is-always-listed",
                "記録の時点から在って変わっていないファイルでも、ビルドの前に読めなくなっていれば（別のプロセスが共有なしで掴む）変更として確認に出す（読めない＝変わっていないとは言えない）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:271-279 の XmlDoc（指紋を取れないファイルは変更の側へ倒す）
                    c.Scaffold();
                    var user = Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj.user");
                    MockBuildBoundaryContext.Write(user, "<Project/>");

                    var run = await c.RunAsync(_ => c.LockExclusive(user));

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/{ProjectName}.csproj.user");
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-shared-locked-existing-file-unchanged-is-not-confirmed",
                "特性の固定（文書に定めが無い今の振る舞い）：削除の共有だけを許さないハンドルで掴まれた既存のファイルは読めるので、変わっていなければ確認に出さない",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:497-503（指紋は File.OpenRead で取る＝読み取りの共有だけを要る）
                    c.Scaffold();
                    var user = Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj.user");
                    MockBuildBoundaryContext.Write(user, "<Project/>");
                    c.Lock(user);

                    var run = await c.RunAsync(_ => { });

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            SyncRow(
                "change-missing-output-folder-records-nothing-then-lists-additions",
                "特性の固定（文書に定めが無い今の振る舞い）：出力フォルダが無い（存在しない・空文字・空白）ときの記録と差分は空。記録の後に作られた出力フォルダへ書かれた入口は、追加として確認に出る",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Absent,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:362-365（出力フォルダが無ければ走査しない）。
                    // 後から現れた入口が出るのは文書（CLAUDE.md「最終ビルド前の変更検知」の追加を確認する）
                    var token = c.CancellationToken;

                    foreach (var blank in new[] { string.Empty, "   " })
                    {
                        MockProjectBuildBoundary
                            .Snapshot.Capture(blank, ProjectName, token)
                            .DetectUnexpectedChanges(blank, ProjectName)
                            .Should()
                            .BeEmpty();
                    }

                    var output = Path.Combine(c.NewFolder(), "out");
                    var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                        output,
                        ProjectName,
                        token
                    );
                    snapshot.DetectUnexpectedChanges(output, ProjectName).Should().BeEmpty();

                    MockBuildBoundaryContext.Write(
                        Path.Combine(output, ProjectName, $"{ProjectName}.csproj.user"),
                        "<Project/>"
                    );

                    snapshot
                        .DetectUnexpectedChanges(output, ProjectName)
                        .Should()
                        .Equal($"{ProjectName}/{ProjectName}.csproj.user");
                }
            ),
        ];

    /// <summary>入口 B：最終ビルドの直前のビルド出力の削除（隠し・ファイルのリンク・列挙できない reparse・拒否・無い）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> ClearKinds() =>
        [
            Row(
                "clear-hidden-obj-entries-are-deleted-before-build",
                "隠し属性の obj（フォルダ自身・隠し＋読み取り専用の .targets・隠しのサブフォルダ）も最終ビルドの直前に消し、確認に出さない",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Hidden,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」（直前に obj / bin を削除する・読み取り専用を外してから消す）と、
                    // src/QuickER.AI.Mock/MockProjectFiles.cs:51-65 の XmlDoc（削除の列挙は隠し属性を飛ばさない）
                    c.Scaffold();
                    var obj = Path.Combine(c.ProjectDirectory, "obj");
                    bool? objExistsAtBuild = null;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            MockBuildBoundaryContext.WriteHidden(
                                c.FullPath(ObjTargetsRelative),
                                readOnly: true
                            );
                            MockBuildBoundaryContext.WriteHidden(
                                Path.Combine(obj, "cache", $"{ProjectName}.csproj.nested.props")
                            );
                            new DirectoryInfo(Path.Combine(obj, "cache")).Attributes |=
                                FileAttributes.Hidden;
                            new DirectoryInfo(obj).Attributes |= FileAttributes.Hidden;
                        },
                        onBuild: () => objExistsAtBuild = Directory.Exists(obj)
                    );

                    objExistsAtBuild.Should().BeFalse();
                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "clear-obj-file-symlink-removes-link-and-keeps-target",
                "obj の中のファイルのシンボリックリンク（外の .targets を指す）は、リンクだけを外してリンク先は残す（作成には特権が要る）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.FileSymlink,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」（リンクならリンクだけを外す）
                    c.Scaffold();
                    var outside = c.NewFolder();
                    var target = Path.Combine(outside, "evil.targets");
                    MockBuildBoundaryContext.WriteReadOnly(target);
                    var link = c.FullPath(ObjTargetsRelative);
                    bool? linkExistsAtBuild = null;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
                            File.CreateSymbolicLink(link, target);
                        },
                        onBuild: () => linkExistsAtBuild = File.Exists(link)
                    );

                    linkExistsAtBuild.Should().BeFalse();
                    File.ReadAllText(target).Should().Be("<Project/>");
                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "clear-unlistable-reparse-obj-is-deleted-before-build",
                "obj 自身が中を列挙できない reparse フォルダでも、フォルダごと消して確認に出さない",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.UnlistableReparse,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」（直前に obj を削除する）と
                    // src/QuickER.AI.Mock/MockProjectFiles.cs:311-316（列挙できないフォルダは中へ入らずフォルダだけを消す）
                    c.Scaffold();
                    var obj = Path.Combine(c.ProjectDirectory, "obj");
                    bool? objExistsAtBuild = null;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            Directory.CreateDirectory(obj);
                            ReparsePointTestHelper.MakeUnlistableReparseDirectory(obj);
                        },
                        onBuild: () => objExistsAtBuild = Directory.Exists(obj)
                    );

                    objExistsAtBuild.Should().BeFalse();
                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "clear-unlistable-reparse-folder-in-obj-is-listed-once",
                "obj の中の列挙できない reparse フォルダは消せずに残り、中へ入らずそのフォルダを 1 件として確認に出す（残ったまま確認なしにビルドしない）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.UnlistableReparse,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」（消せずに残ったものは確認一覧へ回す＝フェイルクローズ）と
                    // src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:162-167 の XmlDoc（中を読み切れないときはフォルダそのものを 1 件）
                    c.Scaffold();
                    var targets = c.FullPath(ObjTargetsRelative);
                    var cache = Path.Combine(c.ProjectDirectory, "obj", "cache");
                    bool? targetsExistAtBuild = null;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            MockBuildBoundaryContext.Write(targets, "<Project/>");
                            Directory.CreateDirectory(cache);
                            ReparsePointTestHelper.MakeUnlistableReparseDirectory(cache);
                        },
                        onBuild: () => targetsExistAtBuild = File.Exists(targets)
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Contain($"{ProjectName}/obj/cache");
                    run.BuildCalls.Should().Be(1);
                    run.Log.Should().Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted);

                    // obj のほかのファイルが残ったままビルドしたのなら、それも一覧に載っていなければならない
                    if (targetsExistAtBuild == true)
                    {
                        run.Paths.Should().Contain(ObjTargetsRelative);
                    }
                }
            ),
            Row(
                "clear-acl-denied-obj-is-listed-as-folder",
                "一覧の読み取りを拒否された obj は消せずに残り、中を読まずに obj を 1 件として確認に出す（答えのとおりにビルドする）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.AclDenied,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:162-167 の XmlDoc（中を読み切れないときはフォルダそのものを 1 件）と
                    // src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」（消せずに残ったものは確認一覧へ回す）
                    c.Scaffold();
                    var obj = Path.Combine(c.ProjectDirectory, "obj");

                    var run = await c.RunAsync(_ =>
                    {
                        MockBuildBoundaryContext.Write(
                            c.FullPath(ObjTargetsRelative),
                            "<Project/>"
                        );
                        c.Deny(obj);
                    });

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/obj");
                    run.BuildCalls.Should().Be(1);
                    run.Log.Should().Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted);
                }
            ),
            Row(
                "clear-missing-obj-and-bin-asks-nothing",
                "obj も bin も無いときは消すものも残りも無く、確認に出さずにビルドする（出力フォルダが無いときの削除も残りを返さない）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Absent,
                async c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectBuildBoundary.cs:162-167 の XmlDoc（フォルダが無ければ空）
                    c.Scaffold();

                    var run = await c.RunAsync(_ => { });

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    run.Log.Should().NotContain(MockStrings.Mock_Run_BuildOutputsNotDeleted);
                    MockProjectBuildBoundary
                        .ClearBuildOutputs(Path.Combine(c.NewFolder(), "missing"), ProjectName)
                        .Should()
                        .BeEmpty();
                }
            ),
        ];

    /// <summary>入口 C：成果物の探索（隠し・読み取り専用・ロック）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> EnumerateKinds() =>
        [
            SyncRow(
                "enumerate-skips-hidden-files-and-folders",
                "特性の固定（文書に定めが無い今の振る舞い）：隠し属性のファイルと隠しのフォルダの中は成果物として数えない（隠しだけなら成果物は無い。数えない向きは成果物の検証を甘くしない安全側）",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Hidden,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectFiles.cs:44-48（探索の列挙は既定の AttributesToSkip＝Hidden / System を飛ばす）
                    var root = c.Folder;
                    MockBuildBoundaryContext.Write(
                        Path.Combine(root, "Components", "Visible.razor")
                    );
                    MockBuildBoundaryContext.WriteHidden(
                        Path.Combine(root, "Components", "Hidden.razor")
                    );
                    var secret = Path.Combine(root, "Secret");
                    MockBuildBoundaryContext.Write(Path.Combine(secret, "Page.razor"));
                    new DirectoryInfo(secret).Attributes |= FileAttributes.Hidden;

                    MockProjectFiles
                        .Enumerate(root, "*.razor")
                        .Select(Path.GetFileName)
                        .Should()
                        .Equal("Visible.razor");

                    var hiddenOnly = c.NewFolder();
                    MockBuildBoundaryContext.WriteHidden(Path.Combine(hiddenOnly, "Home.razor"));
                    MockProjectFiles.HasAny(hiddenOnly, "*.razor").Should().BeFalse();
                }
            ),
            SyncRow(
                "enumerate-counts-read-only-files",
                "特性の固定（文書に定めが無い今の振る舞い）：読み取り専用の属性のファイルも成果物として数える",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.ReadOnly,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectFiles.cs:44-48（読み取り専用は AttributesToSkip に入らない）
                    var root = c.Folder;
                    MockBuildBoundaryContext.WriteReadOnly(
                        Path.Combine(root, "Components", "Home.razor")
                    );

                    MockProjectFiles
                        .Enumerate(root, "*.razor")
                        .Select(Path.GetFileName)
                        .Should()
                        .Equal("Home.razor");
                    MockProjectFiles.HasAny(root, "*.razor").Should().BeTrue();
                }
            ),
            SyncRow(
                "enumerate-counts-locked-files-without-opening",
                "特性の固定（文書に定めが無い今の振る舞い）：探索はファイルを開かないので、別のプロセスが共有なしで掴んだファイルも、掴まれたフォルダの中のファイルも数える",
                BoundaryEntrance.Enumerate,
                BoundaryEntryKind.Locked,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectFiles.cs:73-95（名前を列挙するだけで中身を読まない）
                    var root = c.Folder;
                    var home = Path.Combine(root, "Components", "Home.razor");
                    MockBuildBoundaryContext.Write(home);
                    c.LockExclusive(home);
                    var pages = Path.Combine(root, "Pages");
                    MockBuildBoundaryContext.Write(Path.Combine(pages, "Orders.razor"));
                    c.Lock(pages);

                    MockProjectFiles
                        .Enumerate(root, "*.razor")
                        .Select(Path.GetFileName)
                        .Order(StringComparer.Ordinal)
                        .Should()
                        .Equal("Home.razor", "Orders.razor");
                }
            ),
        ];

    /// <summary>入口 D：スキャフォールドの上書きの走査（読み取り専用・ロック・リンク・reparse）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> OverwriteKinds() =>
        [
            SyncRow(
                "overwrite-read-only-entries-are-listed",
                "読み取り専用の自分のもの（sln・csproj・README）と直下の別ソリューションも、上書きの確認に出す",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.ReadOnly,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs の XmlDoc（スキャフォールドが無条件に上書きするものと直下の別ソリューションを挙げる）
                    var folder = c.Folder;
                    MockBuildBoundaryContext.WriteReadOnly(
                        Path.Combine(folder, $"{ProjectName}.sln")
                    );
                    MockBuildBoundaryContext.WriteReadOnly(Path.Combine(folder, "Other.sln"));
                    MockBuildBoundaryContext.WriteReadOnly(
                        Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj")
                    );
                    MockBuildBoundaryContext.WriteReadOnly(
                        Path.Combine(c.ProjectDirectory, MockProjectScaffoldService.ReadmeFileName)
                    );

                    MockProjectOverwriteScanner
                        .Scan(folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            $"{ProjectName}.sln",
                            "Other.sln",
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj"),
                            Path.Combine(ProjectName, MockProjectScaffoldService.ReadmeFileName)
                        );
                }
            ),
            SyncRow(
                "overwrite-locked-entries-are-listed",
                "特性の固定（文書に定めが無い今の振る舞い）：走査は名前と有無だけを見るので、別のプロセスが共有なしで掴んだ自分の csproj・別プロジェクトと、掴まれた Generated/ も上書きの確認に出す",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Locked,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:69-159（File.Exists・Directory.Exists・直下の名前の列挙だけ）
                    var folder = c.Folder;
                    var csproj = Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj");
                    var library = Path.Combine(folder, "Library.csproj");
                    var generated = Path.Combine(c.ProjectDirectory, "Generated");
                    MockBuildBoundaryContext.Write(csproj);
                    MockBuildBoundaryContext.Write(library);
                    MockBuildBoundaryContext.Write(Path.Combine(generated, "Model.g.cs"));
                    c.LockExclusive(csproj);
                    c.LockExclusive(library);
                    c.Lock(generated);

                    MockProjectOverwriteScanner
                        .Scan(folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            "Library.csproj",
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj"),
                            Path.Combine(ProjectName, "Generated") + Path.DirectorySeparatorChar
                        );
                }
            ),
            SyncRow(
                "overwrite-file-symlinks-are-listed-even-when-dangling",
                "特性の固定（文書に定めが無い今の振る舞い）：ファイルのシンボリックリンクの自分の csproj・別プロジェクトは、リンク先が無くても上書きの確認に出す（作成には特権が要る）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.FileSymlink,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:75-91・:134-145（直下の列挙と File.Exists はリンク自体を見る）
                    var folder = c.Folder;
                    var outside = c.NewFolder();
                    var existingTarget = Path.Combine(outside, "Real.csproj");
                    MockBuildBoundaryContext.Write(existingTarget);
                    Directory.CreateDirectory(c.ProjectDirectory);
                    File.CreateSymbolicLink(
                        Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj"),
                        Path.Combine(outside, "Missing.csproj")
                    );
                    File.CreateSymbolicLink(Path.Combine(folder, "Other.csproj"), existingTarget);
                    File.CreateSymbolicLink(
                        Path.Combine(folder, "Dangling.csproj"),
                        Path.Combine(outside, "Gone.csproj")
                    );

                    MockProjectOverwriteScanner
                        .Scan(folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            "Dangling.csproj",
                            "Other.csproj",
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj")
                        );
                    File.Exists(existingTarget).Should().BeTrue();
                }
            ),
            SyncRow(
                "overwrite-junctions-are-listed-like-folders-without-touching-target",
                "特性の固定（文書に定めが無い今の振る舞い）：ジャンクションのプロジェクトフォルダ・Generated/ は普通のフォルダと同じ表記で上書きの確認に出し、走査はリンク先の中身に触れない",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Junction,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:98・:148-159（Directory.Exists だけでリンクかを見ない）
                    var generatedTarget = c.NewFolder();
                    var precious = Path.Combine(generatedTarget, "precious.txt");
                    File.WriteAllText(precious, "keep me");
                    Directory.CreateDirectory(c.ProjectDirectory);
                    ReparsePointTestHelper.CreateJunction(
                        Path.Combine(c.ProjectDirectory, "Generated"),
                        generatedTarget
                    );

                    MockProjectOverwriteScanner
                        .Scan(c.Folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, "Generated") + Path.DirectorySeparatorChar
                        );
                    File.ReadAllText(precious).Should().Be("keep me");

                    // プロジェクトフォルダ自身がジャンクションでも、中の上書きの対象まで普通に出す
                    var projectTarget = c.NewFolder();
                    MockBuildBoundaryContext.Write(
                        Path.Combine(projectTarget, $"{ProjectName}.csproj")
                    );
                    var output = Path.Combine(c.NewFolder(), "out");
                    Directory.CreateDirectory(output);
                    ReparsePointTestHelper.CreateJunction(
                        Path.Combine(output, ProjectName),
                        projectTarget
                    );

                    MockProjectOverwriteScanner
                        .Scan(output, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj")
                        );
                }
            ),
            SyncRow(
                "overwrite-non-link-reparse-folders-are-listed",
                "特性の固定（文書に定めが無い今の振る舞い）：リンクでない reparse の Generated/ も、リンクでない reparse の出力フォルダの直下の別プロジェクトも、上書きの確認に出す（OneDrive の Files On-Demand と同じ形）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.NonLinkReparse,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs:75-91・:148-159（中を読めるフォルダは普通のフォルダと同じに扱う）
                    var generated = Path.Combine(c.ProjectDirectory, "Generated");
                    MockBuildBoundaryContext.Write(Path.Combine(generated, "Model.g.cs"));
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(generated);

                    MockProjectOverwriteScanner
                        .Scan(c.Folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, "Generated") + Path.DirectorySeparatorChar
                        );

                    var output = Path.Combine(c.NewFolder(), "out");
                    MockBuildBoundaryContext.Write(Path.Combine(output, "Library.csproj"));
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(output);

                    MockProjectOverwriteScanner
                        .Scan(output, ProjectName)
                        .ExistingPaths.Should()
                        .Equal("Library.csproj");
                }
            ),
            SyncRow(
                "overwrite-hidden-foreign-solutions-are-listed",
                "隠し属性（読み取り専用を併せたものも）・システム属性の直下の別ソリューション・別プロジェクトも、隠しの自分の sln・csproj と同じく上書きの確認に出す",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.Hidden,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs の XmlDoc（直下の別ソリューション・別プロジェクトを属性に依らず確認の対象にする）
                    var folder = c.Folder;
                    MockBuildBoundaryContext.WriteHidden(
                        Path.Combine(folder, $"{ProjectName}.sln")
                    );
                    MockBuildBoundaryContext.WriteHidden(Path.Combine(folder, "Other.sln"));
                    MockBuildBoundaryContext.WriteHidden(
                        Path.Combine(folder, "X.slnx"),
                        readOnly: true
                    );
                    MockBuildBoundaryContext.WriteHidden(Path.Combine(folder, "Lib.csproj"));
                    var system = Path.Combine(folder, "Sys.csproj");
                    MockBuildBoundaryContext.Write(system);
                    File.SetAttributes(system, FileAttributes.System);
                    MockBuildBoundaryContext.WriteHidden(
                        Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj")
                    );

                    MockProjectOverwriteScanner
                        .Scan(folder, ProjectName)
                        .ExistingPaths.Should()
                        .Equal(
                            $"{ProjectName}.sln",
                            "Lib.csproj",
                            "Other.sln",
                            "Sys.csproj",
                            "X.slnx",
                            ProjectName + Path.DirectorySeparatorChar,
                            Path.Combine(ProjectName, $"{ProjectName}.csproj")
                        );
                }
            ),
            SyncRow(
                "overwrite-unlistable-reparse-output-folder-is-listed-as-itself",
                "出力フォルダ自身が中を列挙できない reparse フォルダでも、走査は例外にならず、出力フォルダ自身を「.\\」の 1 件として確認に出す"
                    + "（走査は生成コマンドの try の外から呼ばれる。素の列挙が実際に投げることも確かめる）",
                BoundaryEntrance.OverwriteScan,
                BoundaryEntryKind.UnlistableReparse,
                c =>
                {
                    // 根拠：src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs の XmlDoc（中を列挙できない出力フォルダは自身を 1 件として出す）・
                    // src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」（中を確かめられないフォルダは毎回出す＝フェイルクローズ）
                    var output = Path.Combine(c.NewFolder(), "out");
                    Directory.CreateDirectory(output);
                    ReparsePointTestHelper.MakeUnlistableReparseDirectory(output);

                    // 素の列挙は落ちる（＝走査が受け止める問題が実在することの確認）
                    var raw = () => Directory.EnumerateFiles(output, "*.csproj").ToList();
                    raw.Should().Throw<IOException>();

                    var scan = MockProjectOverwriteScanner.Scan(output, ProjectName);

                    scan.RequiresConfirmation.Should().BeTrue();
                    scan.ExistingPaths.Should().Equal("." + Path.DirectorySeparatorChar);
                }
            ),
        ];
}
