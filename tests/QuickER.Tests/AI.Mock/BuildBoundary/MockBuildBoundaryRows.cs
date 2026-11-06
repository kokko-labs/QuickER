using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>
/// 最終ビルドの境界の表：入口 A＝変更検知・B＝ビルド出力の削除・C＝成果物の探索・D＝上書きの走査・E＝最終ビルドの引数（このファイルは表と入口 A）
/// </summary>
/// <remarks>
/// <para>
/// 最終ビルドはエージェントのサンドボックスの外・ユーザー権限で走る唯一の地点なので、検知・削除・探索の範囲が崩れても
/// 「生成は成功する」＝ビルド・型検査のどれでも気づけない（src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」「成果物の探索」「最終ビルド前の変更検知」）。
/// </para>
/// <para>
/// 確認なしで通す拡張子と確認に出す拡張子の期待は、実装の <c>MockProjectBuildBoundary.SafeExtensions</c> を読まず、
/// src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」の列挙（UI 層のソース・静的資産・画像／決して通さない入口）から手で書く。
/// </para>
/// </remarks>
internal static partial class MockBuildBoundaryRows
{
    private const string ProjectName = MockBuildBoundaryContext.ProjectName;

    private const string ObjTargetsRelative = MockBuildBoundaryContext.ObjTargetsRelative;

    /// <summary>確認なしで通す UI 層のソース・静的資産（行の名前・プロジェクトフォルダからの相対パス・元のテストにあったか）</summary>
    private static readonly (string Name, string Path, bool InOriginal)[] UiSources =
    [
        ("razor", "Pages/Orders.razor", true),
        ("xaml", "Views/OrderView.xaml", true),
        ("cs", "ViewModels/OrderViewModel.cs", true),
        ("css", "wwwroot/app.css", true),
        ("js", "wwwroot/site.js", true),
        ("png", "wwwroot/logo.png", true),
        ("html", "wwwroot/index.html", false),
        ("htm", "wwwroot/legacy.htm", false),
        ("svg", "wwwroot/icon.svg", false),
        ("jpg", "wwwroot/photo.jpg", false),
        ("jpeg", "wwwroot/banner.jpeg", false),
        ("gif", "wwwroot/spinner.gif", false),
        ("webp", "wwwroot/hero.webp", false),
        ("ico", "wwwroot/favicon.ico", false),
        // 照合は大文字小文字を無視する
        ("css-upper-case", "wwwroot/APP.CSS", false),
    ];

    /// <summary>MSBuild／dotnet が自動で読む入口の拡張子（決して確認なしで通さない）</summary>
    private static readonly string[] BuildInputExtensions =
    [
        ".props",
        ".targets",
        ".user",
        ".pubxml",
        ".json",
        ".config",
        ".rsp",
        ".csproj",
        ".sln",
        ".slnx",
    ];

    /// <summary>行の表</summary>
    public static ScenarioTable<MockBuildBoundaryScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Change(),
                .. ChangeExtensions(),
                .. ChangeAcl(),
                .. Clear(),
                .. Enumerate(),
                .. Overwrite(),
                .. Arguments(),
            ]
        );

    /// <summary>入口 A：ランナー経由の変更検知と確認</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> Change() =>
        [
            Row(
                "change-csproj-user-added-is-confirmed",
                "{プロジェクト}.csproj.user は Microsoft.Common.CurrentVersion.targets が import する（検証ビルドが中身を実行する）ので、書かれていたら最終ビルドの前に確認する",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                        File.WriteAllText(
                            Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                            "<Project/>"
                        )
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().ContainSingle().Which.Should().EndWith(".csproj.user");
                    run.BuildCalls.Should().Be(1);
                    run.Result.Success.Should().BeTrue();
                }
            ),
            Row(
                "change-global-json-at-output-root-is-confirmed",
                "global.json は MSBuild より前に dotnet が読む（SDK の解決先が変わる）ので、出力フォルダ直下に書かれていたら確認する",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                        File.WriteAllText(Path.Combine(dir, "global.json"), "{}")
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().ContainSingle().Which.Should().Be("global.json");
                }
            ),
            Row(
                "change-rewritten-csproj-with-restored-timestamp-is-confirmed",
                "内容を書き換えたあと更新日時を元へ戻した csproj も検知する（更新日時・サイズでなく内容のハッシュで比べる）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var csproj = Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj");
                    var originalWriteTime = File.GetLastWriteTimeUtc(csproj);
                    var originalLength = new FileInfo(csproj).Length;

                    var run = await c.RunAsync(_ =>
                    {
                        // 長さを変えずに内容だけ差し替える（"<Project/>" と同じ 10 文字）
                        File.WriteAllText(csproj, "<Evil___/>");
                        File.SetLastWriteTimeUtc(csproj, originalWriteTime);
                    });

                    // 更新日時もサイズも元のまま＝メタデータの比較では差分にならないことを、行自身が確かめる
                    File.GetLastWriteTimeUtc(csproj).Should().Be(originalWriteTime);
                    new FileInfo(csproj).Length.Should().Be(originalLength);

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().ContainSingle().Which.Should().EndWith(".csproj");
                }
            ),
            Row(
                "change-non-escaping-agent-is-not-confirmed",
                "境界越えを宣言しない実行器（Claude Code＝無制限 Bash／API キー＝提出ホワイトリスト）では、危険な入口が書かれても確認しない",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(
                        dir =>
                        {
                            File.WriteAllText(Path.Combine(dir, "global.json"), "{}");
                            File.WriteAllText(
                                Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                                "<Project/>"
                            );
                        },
                        escapes: false
                    );

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    run.Result.Success.Should().BeTrue();
                }
            ),
            Row(
                "change-declined-confirmation-skips-build-as-not-verified",
                "確認を取り消したら最終ビルドを実行せず、未検証として完了し、何を書かれたかと取り消しをログへ残す",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(
                        dir => File.WriteAllText(Path.Combine(dir, "global.json"), "{}"),
                        confirm: BoundaryConfirm.Decline
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.BuildCalls.Should().Be(0);
                    run.Result.BuildDeclined.Should().BeTrue();
                    run.Result.BuildSucceeded.Should().BeFalse();
                    run.Result.Success.Should().BeFalse();
                    run.Result.TimedOut.Should().BeFalse();
                    run.Result.Canceled.Should().BeFalse();
                    run.Result.Message.Should().Be(MockStrings.Mock_Result_BuildNotVerified);
                    run.Log.Should()
                        .Contain("global.json")
                        .And.Contain(MockStrings.Mock_Run_BuildDeclined);
                }
            ),
            Row(
                "change-unwired-confirmation-skips-build",
                "確認の手段が配線されていないときは、承認へ倒さず最終ビルドを見送る",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(
                        dir => File.WriteAllText(Path.Combine(dir, "global.json"), "{}"),
                        confirm: BoundaryConfirm.Unwired
                    );

                    run.BuildCalls.Should().Be(0);
                    run.Result.BuildDeclined.Should().BeTrue();
                }
            ),
            Row(
                "change-hidden-csproj-user-is-confirmed",
                "隠し属性を付けたファイルも検知する（既定の列挙は Hidden / System を飛ばす＝検知の回避路になる）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Hidden,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                    {
                        var path = Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user");
                        File.WriteAllText(path, "<Project/>");
                        File.SetAttributes(path, FileAttributes.Hidden);
                    });

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().ContainSingle().Which.Should().EndWith(".csproj.user");
                }
            ),
            Row(
                "change-junction-is-listed-without-traversal",
                "外を指すジャンクションは中へ辿らず、リンクそのものを 1 件として確認に出す（名前が確認の要らない拡張子 styles.css でも出る）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Junction,
                async c =>
                {
                    c.Scaffold();
                    var outside = c.NewFolder();
                    Directory.CreateDirectory(Path.Combine(outside, "nested"));
                    File.WriteAllText(Path.Combine(outside, "Evil.csproj"), "<Project/>");
                    File.WriteAllText(Path.Combine(outside, "nested", "global.json"), "{}");
                    var link = Path.Combine(c.ProjectDirectory, "styles.css");

                    // 辿ると C:\ を指す 1 つでディスク全体をハッシュし続け、外のファイルが出力フォルダ内の相対パスで一覧に紛れ込む
                    var run = await c.RunAsync(_ =>
                        ReparsePointTestHelper.CreateJunction(link, outside)
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/styles.css");
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-file-symlink-csproj-user-is-confirmed",
                "外を指すファイルのシンボリックリンク（.csproj.user）も検知する（reparse point を列挙の属性で飛ばすと回避路になる。作成には特権が要る）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.FileSymlink,
                async c =>
                {
                    c.Scaffold();
                    var outside = c.NewFolder();
                    var target = Path.Combine(outside, "evil.targets");
                    File.WriteAllText(target, "<Project/>");

                    var run = await c.RunAsync(dir =>
                        File.CreateSymbolicLink(
                            Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                            target
                        )
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/{ProjectName}.csproj.user");
                }
            ),
            Row(
                "change-under-project-obj-is-not-confirmed",
                "最終ビルドの直前に消す {プロジェクト}/obj へ書かれたものは確認の対象にしない（削除する範囲と検知しない範囲を揃える）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(dir =>
                        MockBuildBoundaryContext.Write(
                            Path.Combine(
                                dir,
                                ProjectName,
                                "obj",
                                $"{ProjectName}.csproj.evil.targets"
                            ),
                            "<Project/>"
                        )
                    );

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-nested-obj-below-project-root-is-confirmed",
                "検知から外すのは {プロジェクト}/obj と bin だけで、深い階層の同名フォルダ（Components/obj）に書かれた入口は確認に出す",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var relative =
                        $"{ProjectName}/Components/obj/{ProjectName}.csproj.evil.targets";
                    var run = await c.RunAsync(_ =>
                        MockBuildBoundaryContext.Write(c.FullPath(relative), "<Project/>")
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal(relative);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-deleted-file-is-not-confirmed",
                "記録の時点にあって実行器が消したファイルは確認に出さない（検知は追加と変更だけを見る）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    // 意図：削除は確認に出さない。src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」は「追加・変更」を確認すると書き、
                    // MockProjectBuildBoundary.Snapshot.DetectUnexpectedChanges の XmlDoc は「削除は対象外＝ビルドが失敗するだけで、
                    // ビルドに何かを実行させはしない」と書く（記録にあって今は無い項目を走査しない）
                    c.Scaffold();
                    var props = Path.Combine(c.Folder, "Directory.Build.props");
                    File.WriteAllText(props, "<Project/>");

                    var run = await c.RunAsync(_ => File.Delete(props));

                    File.Exists(props).Should().BeFalse();
                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    run.Result.Success.Should().BeTrue();
                }
            ),
            Row(
                "change-non-link-reparse-folder-is-traversed",
                "リンクでない reparse フォルダ（OneDrive の Files On-Demand と同じ形）は中へ入って比較し、中へ書かれた入口を確認に出す（判定は LinkTarget で、属性ビットで決めない）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.NonLinkReparse,
                async c =>
                {
                    c.Scaffold();
                    var wwwroot = Path.Combine(c.ProjectDirectory, "wwwroot");
                    Directory.CreateDirectory(wwwroot);
                    File.WriteAllText(Path.Combine(wwwroot, "site.css"), "body {}");
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(wwwroot);

                    var run = await c.RunAsync(_ =>
                        File.WriteAllText(Path.Combine(wwwroot, "evil.targets"), "<Project/>")
                    );

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal($"{ProjectName}/wwwroot/evil.targets");
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-non-link-reparse-folder-unchanged-is-not-confirmed",
                "リンクでない reparse フォルダは、中身が変わっていなければ確認に出さない（誤検知しない）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.NonLinkReparse,
                async c =>
                {
                    c.Scaffold();
                    var wwwroot = Path.Combine(c.ProjectDirectory, "wwwroot");
                    Directory.CreateDirectory(wwwroot);
                    File.WriteAllText(Path.Combine(wwwroot, "appsettings.json"), "{}");
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(wwwroot);

                    var run = await c.RunAsync(_ => { });

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                }
            ),
            Row(
                "change-unlistable-reparse-folder-is-always-listed",
                "中を列挙できない reparse フォルダは中へ入らず（ランは落ちない）、変化が無くても毎回確認に出す（中身を確かめられない以上、変わっていないとは言えない）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.UnlistableReparse,
                async c =>
                {
                    c.Scaffold();

                    // 置き場所は出力フォルダ直下（プロジェクトフォルダの外）。成果物の検証はプロジェクトフォルダ配下だけを見るので、
                    // 変更検知の挙動だけを切り出して確かめられる
                    var cache = Path.Combine(c.Folder, "cache");
                    Directory.CreateDirectory(cache);
                    ReparsePointTestHelper.MakeUnlistableReparseDirectory(cache);

                    // 実行器は何も書かない（スキャフォールドの時点から在るフォルダが変わらないまま）
                    var run = await c.RunAsync(_ => { });

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().Equal("cache");
                    run.BuildCalls.Should().Be(1);
                }
            ),
        ];

    /// <summary>入口 A：拡張子で決まる確認の要否（確認なしで通す UI 層のソース・静的資産と、自動で読まれる入口）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> ChangeExtensions() =>
        UiSources
            .Select(source =>
                Row(
                    $"change-ui-source-only-is-not-confirmed-{source.Name}",
                    $"UI 層のソース・静的資産（{source.Path}）だけが増えたランでは何も尋ねない（確認の形骸化を防ぐ）",
                    BoundaryEntrance.ChangeDetection,
                    BoundaryEntryKind.Plain,
                    async c =>
                    {
                        c.Scaffold();
                        var run = await c.RunAsync(dir =>
                            MockBuildBoundaryContext.Write(
                                Path.Combine(
                                    dir,
                                    ProjectName,
                                    source.Path.Replace('/', Path.DirectorySeparatorChar)
                                ),
                                "content"
                            )
                        );

                        run.ConfirmCalls.Should().Be(0);
                        run.BuildCalls.Should().Be(1);
                        run.Result.Success.Should().BeTrue();
                    }
                )
            )
            .Concat(
                BuildInputExtensions.Select(extension =>
                    Row(
                        $"change-build-input-extension-is-confirmed-{extension.TrimStart('.')}",
                        $"自動で読まれる設定ファイルの拡張子 {extension} は確認なしで通さない（通す拡張子の集合に入らず、書かれたら確認に出る）",
                        BoundaryEntrance.ChangeDetection,
                        BoundaryEntryKind.Plain,
                        c => BuildInputIsConfirmedAsync(c, extension, inSafeSetCheck: true)
                    )
                )
            )
            .Append(
                Row(
                    "change-build-input-extension-is-confirmed-props-upper-case",
                    "拡張子の照合は大文字小文字を無視するので、.PROPS も確認に出る",
                    BoundaryEntrance.ChangeDetection,
                    BoundaryEntryKind.Plain,
                    c => BuildInputIsConfirmedAsync(c, ".PROPS", inSafeSetCheck: false)
                )
            );

    /// <summary>自動で読まれる入口の拡張子のファイルを書き、確認に出ることを確かめる</summary>
    /// <param name="c">文脈</param>
    /// <param name="extension">拡張子</param>
    /// <param name="inSafeSetCheck">通す拡張子の集合に入っていないことも見るか（元のテストの表明）</param>
    private static async Task BuildInputIsConfirmedAsync(
        MockBuildBoundaryContext c,
        string extension,
        bool inSafeSetCheck
    )
    {
        if (inSafeSetCheck)
        {
            MockProjectBuildBoundary.SafeExtensions.Should().NotContain(extension);
        }

        c.Scaffold();
        var relative = $"{ProjectName}/Config/input{extension}";
        var run = await c.RunAsync(_ =>
            MockBuildBoundaryContext.Write(c.FullPath(relative), "<Project/>")
        );

        run.ConfirmCalls.Should().Be(1);
        run.Paths.Should().Equal(relative);
        run.BuildCalls.Should().Be(1);
    }

    /// <summary>入口 A：一覧を読めないフォルダ（拒否の ACE）は黙って飛ばさず、毎回「変更」として出す</summary>
    /// <remarks>
    /// エージェントは出力フォルダの ACL を変えて、自分に「一覧の読み取り」だけを拒否できる。ファイルは名前で開けるので MSBuild は
    /// <c>{プロジェクト}.csproj.user</c> を読むが、アクセス拒否を無視する走査では差分が空になり、確認なしでビルドへ進む。
    /// </remarks>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> ChangeAcl() =>
        [
            Row(
                "change-acl-denied-output-root-during-run-asks-before-build",
                "実行中に出力フォルダの一覧を拒否されても、確認なしにビルドしない（出力フォルダ自身を「.」として確認に出す）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(
                        dir =>
                        {
                            File.WriteAllText(
                                Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                                "<Project/>"
                            );
                            c.Deny(dir);
                        },
                        confirm: BoundaryConfirm.Decline
                    );

                    (run.ConfirmCalls, run.BuildCalls).Should().Be((1, 0));
                    run.Paths.Should().Contain(".");
                }
            ),
            Row(
                "change-acl-denied-subfolder-during-run-asks-before-build",
                "実行中にサブフォルダの一覧を拒否されても、確認なしにビルドしない（そのフォルダを確認に出す）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                async c =>
                {
                    c.Scaffold();
                    var run = await c.RunAsync(
                        dir =>
                        {
                            var target = Path.Combine(dir, ProjectName, "Build");
                            Directory.CreateDirectory(target);
                            File.WriteAllText(
                                Path.Combine(target, "Directory.Build.props"),
                                "<Project/>"
                            );
                            c.Deny(target);
                        },
                        confirm: BoundaryConfirm.Decline
                    );

                    (run.ConfirmCalls, run.BuildCalls).Should().Be((1, 0));
                    run.Paths.Should().Contain($"{ProjectName}/Build");
                }
            ),
            SyncRow(
                "change-snapshot-acl-denied-output-root-is-listed-as-dot",
                "記録の後に出力フォルダの一覧を拒否されたら、出力フォルダ自身を「.」として出す（ファイルは名前で読める＝拒否は回避路として成り立つ）",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    c.Scaffold();
                    var csprojUser = Path.Combine(c.ProjectDirectory, $"{ProjectName}.csproj.user");
                    var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                        c.Folder,
                        ProjectName,
                        c.CancellationToken
                    );
                    File.WriteAllText(csprojUser, "<Project/>");
                    c.Deny(c.Folder);

                    File.Exists(csprojUser).Should().BeTrue();
                    snapshot.DetectUnexpectedChanges(c.Folder, ProjectName).Should().Equal(".");
                }
            ),
            SyncRow(
                "change-snapshot-acl-denied-subfolder-is-listed",
                "記録の後にサブフォルダの一覧を拒否されたら、そのフォルダを出す",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    c.Scaffold();
                    var hidden = Path.Combine(c.ProjectDirectory, "Build");
                    Directory.CreateDirectory(hidden);
                    var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                        c.Folder,
                        ProjectName,
                        c.CancellationToken
                    );
                    File.WriteAllText(Path.Combine(hidden, "Directory.Build.props"), "<Project/>");
                    c.Deny(hidden);

                    snapshot
                        .DetectUnexpectedChanges(c.Folder, ProjectName)
                        .Should()
                        .Equal($"{ProjectName}/Build");
                }
            ),
            SyncRow(
                "change-snapshot-acl-denied-from-start-is-listed-every-time",
                "記録の時点で読めなかったフォルダも、変化が無いとは言えないので毎回出す",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    c.Scaffold();
                    var hidden = Path.Combine(c.ProjectDirectory, "Build");
                    Directory.CreateDirectory(hidden);
                    c.Deny(hidden);
                    var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                        c.Folder,
                        ProjectName,
                        c.CancellationToken
                    );

                    snapshot
                        .DetectUnexpectedChanges(c.Folder, ProjectName)
                        .Should()
                        .Equal($"{ProjectName}/Build");
                }
            ),
            SyncRow(
                "change-snapshot-acl-denied-folder-named-like-safe-asset-is-listed",
                "一覧を読めないフォルダは、名前が確認の要らない拡張子（assets.css）でも出す",
                BoundaryEntrance.ChangeDetection,
                BoundaryEntryKind.AclDenied,
                c =>
                {
                    c.Scaffold();
                    var hidden = Path.Combine(c.ProjectDirectory, "assets.css");
                    Directory.CreateDirectory(hidden);
                    c.Deny(hidden);
                    var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                        c.Folder,
                        ProjectName,
                        c.CancellationToken
                    );

                    snapshot
                        .DetectUnexpectedChanges(c.Folder, ProjectName)
                        .Should()
                        .Equal($"{ProjectName}/assets.css");
                }
            ),
        ];

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{入口}-{守っていること}）</param>
    /// <param name="description">説明</param>
    /// <param name="entrance">入口</param>
    /// <param name="kind">種別</param>
    /// <param name="body">流して表明する本体</param>
    private static ScenarioRow<MockBuildBoundaryScenario> Row(
        string id,
        string description,
        BoundaryEntrance entrance,
        BoundaryEntryKind kind,
        Func<MockBuildBoundaryContext, Task> body
    ) => new(id, null, description, new MockBuildBoundaryScenario(entrance, kind, body));

    /// <summary>同期の本体で行を作る</summary>
    /// <param name="id">行の Id（{入口}-{守っていること}）</param>
    /// <param name="description">説明</param>
    /// <param name="entrance">入口</param>
    /// <param name="kind">種別</param>
    /// <param name="body">流して表明する本体</param>
    private static ScenarioRow<MockBuildBoundaryScenario> SyncRow(
        string id,
        string description,
        BoundaryEntrance entrance,
        BoundaryEntryKind kind,
        Action<MockBuildBoundaryContext> body
    ) =>
        Row(
            id,
            description,
            entrance,
            kind,
            c =>
            {
                body(c);
                return Task.CompletedTask;
            }
        );
}
