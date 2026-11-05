using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.ViewModels.DocFile;

namespace QuickER.Tests.Gui.ViewModels;

internal static partial class DocumentFileScenarios
{
    /// <summary>開いてクリーンになったことの表明（元のテストの OpenClean）</summary>
    private static DocStep OpenClean() => Doc.Open(A).Expect(o => o.IsDirty.Should().BeFalse());

    /// <summary>外部変更の検知（クリーンは自動再読込・ダーティは確認・再入・起動時・監視の通知）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> ExternalChangeRows() =>
        [
            Row(
                "ext-clean-auto-reload",
                "クリーン: 外部変更を無確認で自動再読込する",
                Doc.Write(A, "Original"),
                OpenClean()
                    .Expect(o => o.Tables.Should().ContainSingle().Which.Should().Be("Original")),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .Expect(o =>
                    {
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("External", "外部内容を反映する");
                        o.IsDirty.Should().BeFalse("再読込後はクリーン");
                        o.CanUndo.Should().BeFalse("再読込は履歴をクリアする");
                        o.Status.Should().Be(Strings.Status_ExternalReloaded);
                        o.FitRequested.Should()
                            .BeFalse("再読込ではビューポートを維持し fit を発火しない");
                    })
            ),
            Row(
                "ext-dirty-confirm-reload",
                "ダーティ: 確認で再読込を選ぶと外部内容を反映する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add().Expect(o => o.IsDirty.Should().BeTrue()),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().ContainSingle();
                        o.Tables.Should().ContainSingle().Which.Should().Be("External");
                        o.IsDirty.Should().BeFalse("再読込後はクリーン");
                    })
            ),
            Row(
                "ext-queries-only-confirm",
                "ダーティ: クエリ差し替えだけでも外部変更を確認する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.ReplaceQueries(1).Expect(o => o.IsDirty.Should().BeTrue()),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().ContainSingle("無確認の自動再読込へ流さない");
                        o.Queries.Should().Be(1, "続行を選んだのでクエリは保持される");
                    })
            ),
            Row(
                "ext-dirty-continue-suppress",
                "ダーティ: 続行を選ぶと変更を保持し同一内容は再確認しない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().ContainSingle();
                        o.Tables.Should().Contain("Original");
                        o.IsDirty.Should().BeTrue();
                    }),
                Doc.Modified()
                    .No()
                    .Expect(o => o.WarningConfirms.Should().BeEmpty("同一内容では再確認しない")),
                Doc.Write(A, "External2"),
                Doc.Modified()
                    .No()
                    .Expect(o => o.WarningConfirms.Should().ContainSingle("別内容では再確認する"))
            ),
            Row(
                "ext-corrupt-keeps-current",
                "破損: 非文書への外部変更は現状維持する",
                Doc.Write(A, "Keep"),
                OpenClean(),
                Doc.WriteRaw(A, DocContentKind.NotDiagram, "garbage", "{\"unrelated\":true}"),
                Doc.Modified()
                    .Expect(o =>
                    {
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("Keep", "破損時は現状維持");
                        o.Status.Should().Be(Strings.Status_ExternalReloadFailed);
                    })
            ),
            Row(
                "ext-deleted-notifies-only",
                "削除: 通知のみで現状維持する",
                Doc.Write(A, "Alive"),
                OpenClean(),
                Doc.Event(DocumentFileChangeKind.Deleted)
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A), "削除でもパスは維持する");
                        o.Tables.Should().ContainSingle().Which.Should().Be("Alive");
                        o.Status.Should().Be(Strings.Status_ExternalFileDeleted);
                    })
            ),
            Row(
                "ext-untitled-ignores-events",
                "無題: パス不一致の外部変更は無視する",
                Doc.Add().Expect(o => o.Path.Should().BeNull()),
                new DocStep(
                    new DeliverOp(
                        DocumentFileChangeKind.Modified,
                        DocEventHash.Unknown,
                        OtherPath: true
                    )
                ).Expect(o =>
                    o.Status.Should().Be(Strings.Status_Ready, "無題・パス不一致では何もしない")
                )
            ),
            Row(
                "ext-startup-clean-auto-reload",
                "起動時: クリーン復元で外部変更があれば自動再読込する",
                Doc.Add(),
                Doc.Save(A),
                Doc.AutoSave(),
                Doc.Write(A, "ExternalAtStartup"),
                Doc.Restart()
                    .Expect(o =>
                    {
                        o.Path.Should().Be(o.PathOf(A));
                        o.Tables.Should().ContainSingle().Which.Should().Be("ExternalAtStartup");
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "ext-startup-dirty-confirms",
                "起動時: ダーティ復元で外部変更があれば確認する",
                Doc.Add(),
                Doc.Save(A),
                Doc.Add(),
                Doc.AutoSave(),
                Doc.Write(A, "ExternalAtStartup"),
                Doc.Restart()
                    .No()
                    .Expect(o =>
                    {
                        o.IsDirty.Should().BeTrue("復元ダーティを引き継ぐ");
                        o.WarningConfirms.Should()
                            .ContainSingle("起動時のダーティ外部変更は確認する");
                    })
            ),
            Row(
                "ext-reentry-deferred-after-continue",
                "再入: 確認中に届いた外部変更は重ねず、続行の後にディスクで再評価する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "First"),
                Doc.Modified()
                    .No()
                    .DuringFirstConfirm(Doc.WriteOp(A, "Second"), Doc.ModifiedOp())
                    .Expect(o =>
                    {
                        o.MaxConfirmDepth.Should().Be(1, "確認ダイアログが入れ子で 2 枚重ならない");
                        o.WarningConfirms.Should()
                            .HaveCount(
                                2,
                                "閉じた後に最新のディスク（Second）で 1 回だけ再評価して確認する"
                            );
                        o.IsDirty.Should().BeTrue("どちらも続行を選んだので未保存変更は残る");
                    })
            ),
            Row(
                "ext-reentry-absorbed-by-reload",
                "再入: 確認中に届いた外部変更は、再読込を選べば再評価で確認を出さない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "First"),
                Doc.Modified()
                    .DuringFirstConfirm(Doc.WriteOp(A, "Second"), Doc.ModifiedOp())
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle("再読込が最新の内容を読み込んだので再評価は一致する");
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("Second", "再読込はディスクの最新を読む");
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "ext-already-reflected-ignored",
                "反映済み: 最終既知ハッシュと同じ内容の変更は確認も再読込もしない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Modified()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().BeEmpty("ディスクは最後に読み込んだ内容のまま");
                        o.Tables.Should().HaveCount(2, "未保存の編集は捨てられない");
                        o.IsDirty.Should().BeTrue();
                    })
            ),
            Row(
                "ext-watch-interrupted-status",
                "監視の中断・再開はステータスバーへ通知する（中断）",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Event(DocumentFileChangeKind.WatchInterrupted)
                    .Expect(o =>
                    {
                        o.Status.Should().Be(Strings.Status_ExternalWatchInterrupted);
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("Original", "図には触れない");
                    })
            ),
            Row(
                "ext-watch-resumed-status",
                "監視の中断・再開はステータスバーへ通知する（再開）",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Event(DocumentFileChangeKind.WatchResumed)
                    .Expect(o =>
                    {
                        o.Status.Should().Be(Strings.Status_ExternalWatchResumed);
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("Original", "図には触れない");
                    })
            ),
        ];
}
