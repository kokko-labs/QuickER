using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>最終ビルドの境界の表の入口 B：最終ビルドの直前のビルド出力（{プロジェクト}/obj・bin）の削除</summary>
/// <remarks>
/// <c>obj/{プロジェクト}.csproj.*.targets</c> は <c>Microsoft.Common.targets</c> のワイルドカード import で読み込まれるので、
/// 残すと AI のコードが最終ビルドで走る。消せずに残ったものは黙って進まず確認一覧へ回す（フェイルクローズ。
/// 変更検知は obj / bin を見ないので、拾えるのはこの残存検査だけ）。
/// </remarks>
internal static partial class MockBuildBoundaryRows
{
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> Clear() =>
        [
            Row(
                "clear-obj-and-bin-are-deleted-before-build",
                "最終ビルドの直前にビルド中間物（obj / bin）を消す（obj/*.targets はワイルドカード import で読み込まれる）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Plain,
                async c =>
                {
                    c.Scaffold();
                    var obj = Path.Combine(c.ProjectDirectory, "obj");
                    var bin = Path.Combine(c.ProjectDirectory, "bin");
                    var objExistsAtBuild = true;
                    var binExistsAtBuild = true;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            // AI が書いた（ことにする）ビルド中間物
                            MockBuildBoundaryContext.Write(
                                Path.Combine(obj, $"{ProjectName}.csproj.injected.targets"),
                                "<Project/>"
                            );
                            MockBuildBoundaryContext.Write(Path.Combine(bin, "stale.dll"));
                        },
                        escapes: false,
                        confirm: BoundaryConfirm.Unwired,
                        onBuild: () =>
                        {
                            objExistsAtBuild = Directory.Exists(obj);
                            binExistsAtBuild = Directory.Exists(bin);
                        }
                    );

                    run.Result.Success.Should().BeTrue();
                    run.BuildCalls.Should().Be(1);
                    objExistsAtBuild.Should().BeFalse();
                    binExistsAtBuild.Should().BeFalse();
                }
            ),
            Row(
                "clear-obj-junction-removes-link-and-keeps-target",
                "obj がジャンクションでも、リンクだけを外してリンク先の中身は消さない（出力フォルダの外のフォルダを巻き添えにしない）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Junction,
                async c =>
                {
                    c.Scaffold();
                    var linkTarget = c.NewFolder();
                    var keepFile = Path.Combine(linkTarget, "precious.txt");
                    File.WriteAllText(keepFile, "keep me");
                    var obj = Path.Combine(c.ProjectDirectory, "obj");

                    await c.RunAsync(
                        _ => ReparsePointTestHelper.CreateJunction(obj, linkTarget),
                        escapes: false,
                        confirm: BoundaryConfirm.Unwired
                    );

                    // リンクの実体は外れる（ビルド中間物として扱われる）が、リンク先は無傷
                    Directory.Exists(obj).Should().BeFalse();
                    File.Exists(keepFile).Should().BeTrue();
                    File.ReadAllText(keepFile).Should().Be("keep me");
                }
            ),
            Row(
                "clear-read-only-obj-targets-never-reach-build-unconfirmed-wired",
                "読み取り専用の obj/*.targets は、ビルドの時点で無いか、確認一覧に載って承認されたときだけビルドへ渡る（確認の手段あり）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.ReadOnly,
                c => ReadOnlyObjTargetsNeverReachBuildUnconfirmedAsync(c, wireConfirm: true)
            ),
            Row(
                "clear-read-only-obj-targets-never-reach-build-unconfirmed-unwired",
                "読み取り専用の obj/*.targets は、ビルドの時点で無いか、確認の手段が無ければビルドしない（確認の手段なし）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.ReadOnly,
                c => ReadOnlyObjTargetsNeverReachBuildUnconfirmedAsync(c, wireConfirm: false)
            ),
            Row(
                "clear-read-only-obj-targets-are-deleted-before-build",
                "読み取り専用の属性を外してから消すので、読み取り専用の obj/*.targets は確認なしに消えてそのままビルドする（上の性質のうち「ファイルが無い」側）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.ReadOnly,
                async c =>
                {
                    c.Scaffold();
                    var targets = c.FullPath(ObjTargetsRelative);
                    bool? existedAtBuild = null;

                    var run = await c.RunAsync(
                        _ => MockBuildBoundaryContext.WriteReadOnly(targets),
                        onBuild: () => existedAtBuild = File.Exists(targets)
                    );

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    existedAtBuild.Should().BeFalse();
                    run.Result.Success.Should().BeTrue();
                }
            ),
            Row(
                "clear-read-only-bin-file-is-deleted-before-build",
                "bin の読み取り専用のファイルも属性を外して消し、確認なしでビルドする（obj と bin は同じ削除を通る）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.ReadOnly,
                async c =>
                {
                    c.Scaffold();
                    var stale = Path.Combine(c.ProjectDirectory, "bin", "Debug", "stale.dll");
                    bool? binExistedAtBuild = null;

                    var run = await c.RunAsync(
                        _ => MockBuildBoundaryContext.WriteReadOnly(stale),
                        onBuild: () =>
                            binExistedAtBuild = Directory.Exists(
                                Path.Combine(c.ProjectDirectory, "bin")
                            )
                    );

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    binExistedAtBuild.Should().BeFalse();
                    run.Result.Success.Should().BeTrue();
                }
            ),
            Row(
                "clear-undeletable-obj-file-is-listed-approved",
                "削除できずに残った obj のファイルは確認一覧へ回り、承認したらビルドする（残存をログにも残す）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Locked,
                c => UndeletableObjFileIsListedAsync(c, BoundaryConfirm.Approve)
            ),
            Row(
                "clear-undeletable-obj-file-is-listed-declined",
                "削除できずに残った obj のファイルは確認一覧へ回り、取り消したらビルドしない（残存をログにも残す）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Locked,
                c => UndeletableObjFileIsListedAsync(c, BoundaryConfirm.Decline)
            ),
            Row(
                "clear-undeletable-obj-file-unwired-skips-build",
                "削除できずに残った obj があり、確認する手段が無ければビルドしない",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    c.Scaffold();
                    var targets = c.FullPath(ObjTargetsRelative);

                    var run = await c.RunAsync(
                        _ => c.WriteAndLock(targets),
                        confirm: BoundaryConfirm.Unwired
                    );

                    run.BuildCalls.Should().Be(0);
                    run.Result.BuildDeclined.Should().BeTrue();
                }
            ),
            Row(
                "clear-undeletable-obj-file-non-escaping-agent-is-logged-only",
                "境界越えを宣言しない実行器では、残存は確認に回さずログへ残すだけ（黙らせない）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    c.Scaffold();
                    var targets = c.FullPath(ObjTargetsRelative);

                    var run = await c.RunAsync(_ => c.WriteAndLock(targets), escapes: false);

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    run.Log.Should()
                        .Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted)
                        .And.Contain(ObjTargetsRelative);
                }
            ),
            Row(
                "clear-undeletable-obj-junction-is-listed-once-without-traversal",
                "外せなかった obj のジャンクションは、フォルダそのものを 1 件として確認に出す（中へ入らない・重複しない・リンク先の中身は消さない）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.Locked,
                async c =>
                {
                    c.Scaffold();
                    var outside = c.NewFolder();
                    var outsideTargets = Path.Combine(
                        outside,
                        $"{ProjectName}.csproj.evil.targets"
                    );
                    File.WriteAllText(outsideTargets, "<Project/>");
                    var obj = Path.Combine(c.ProjectDirectory, "obj");

                    var run = await c.RunAsync(_ =>
                    {
                        ReparsePointTestHelper.CreateJunction(obj, outside);
                        c.Lock(obj);
                    });

                    run.ConfirmCalls.Should().Be(1);
                    run.Paths.Should().OnlyHaveUniqueItems();
                    run.Paths.Should().Equal($"{ProjectName}/obj");
                    run.BuildCalls.Should().Be(1);
                    File.Exists(outsideTargets).Should().BeTrue("リンク先の中身には触れない");
                }
            ),
            Row(
                "clear-obj-non-link-reparse-folder-is-deleted-with-contents",
                "obj がリンクでない reparse フォルダでも、中身ごと消してから確認なしでビルドする（属性ビットでリンク扱いにすると非再帰の削除が失敗して obj が残る）",
                BoundaryEntrance.ClearOutputs,
                BoundaryEntryKind.NonLinkReparse,
                async c =>
                {
                    c.Scaffold();
                    var obj = Path.Combine(c.ProjectDirectory, "obj");
                    bool? objExistedAtBuild = null;

                    var run = await c.RunAsync(
                        _ =>
                        {
                            Directory.CreateDirectory(obj);
                            File.WriteAllText(c.FullPath(ObjTargetsRelative), "<Project/>");
                            ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(obj);
                        },
                        onBuild: () => objExistedAtBuild = Directory.Exists(obj)
                    );

                    run.ConfirmCalls.Should().Be(0);
                    run.BuildCalls.Should().Be(1);
                    objExistedAtBuild.Should().BeFalse();
                }
            ),
        ];

    /// <summary>
    /// 読み取り専用の obj/*.targets が確認なしに最終ビルドへ渡らないこと（安全側の性質）。結果だけを見る＝
    /// 「ビルドの時点でファイルが無い」か「確認一覧に載る（確認の手段が無ければビルドしない）」のどちらか
    /// </summary>
    /// <remarks>読み取り専用の解除を外しても、残存検査が拾うのでこの性質は保たれる（二重化の確認）。</remarks>
    private static async Task ReadOnlyObjTargetsNeverReachBuildUnconfirmedAsync(
        MockBuildBoundaryContext c,
        bool wireConfirm
    )
    {
        c.Scaffold();
        var targets = c.FullPath(ObjTargetsRelative);
        bool? existedAtBuild = null;

        var run = await c.RunAsync(
            _ => MockBuildBoundaryContext.WriteReadOnly(targets),
            confirm: wireConfirm ? BoundaryConfirm.Approve : BoundaryConfirm.Unwired,
            onBuild: () => existedAtBuild = File.Exists(targets)
        );

        if (run.BuildCalls == 0)
        {
            // ビルドしなかったのなら、確認する手段が無かった場合に限る
            run.Result.BuildDeclined.Should().BeTrue();
            wireConfirm.Should().BeFalse();
        }
        else if (existedAtBuild == true)
        {
            // 残ったままビルドしたのなら、利用者がそれを一覧で見て承認していなければならない
            wireConfirm.Should().BeTrue();
            run.Paths.Should().Contain(ObjTargetsRelative);
        }
    }

    /// <summary>削除できずに残った obj のファイルが確認一覧へ回り、答えのとおりにビルドすること</summary>
    /// <remarks>
    /// 削除の共有を許さないハンドルで開いたままにして、削除を確実に失敗させる（読み取り専用と違い、QuickER の側からは外せない）。
    /// </remarks>
    private static async Task UndeletableObjFileIsListedAsync(
        MockBuildBoundaryContext c,
        BoundaryConfirm confirm
    )
    {
        c.Scaffold();
        var targets = c.FullPath(ObjTargetsRelative);

        var run = await c.RunAsync(_ => c.WriteAndLock(targets), confirm: confirm);

        run.ConfirmCalls.Should().Be(1);
        run.Paths.Should().Equal(ObjTargetsRelative);
        run.BuildCalls.Should().Be(confirm == BoundaryConfirm.Approve ? 1 : 0);
        run.Result.BuildDeclined.Should().Be(confirm != BoundaryConfirm.Approve);
        run.Log.Should()
            .Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted)
            .And.Contain(ObjTargetsRelative);
    }
}
