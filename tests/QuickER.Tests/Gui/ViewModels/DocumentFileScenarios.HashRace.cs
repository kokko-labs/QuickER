using AwesomeAssertions;
using QuickER.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.ViewModels.DocFile;

namespace QuickER.Tests.Gui.ViewModels;

internal static partial class DocumentFileScenarios
{
    /// <summary>最終既知ハッシュの採取（読み書きと採取の隙間の外部書き込み・BOM）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> HashRaceRows() =>
        [
            Row(
                "race-open-external-write-detected",
                "開く: 読み取り直後に割り込んだ外部書き込みを検知して再読込する",
                Doc.Write(A, "Original"),
                Doc.Open(A)
                    .RaceAt(DocHook.AfterLoadRead, Doc.WriteOp(A, "ExternalDuringOpen"))
                    .Expect(o =>
                    {
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("ExternalDuringOpen", "外部内容へ追従する");
                        o.Status.Should().Be(Strings.Status_ExternalReloaded);
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "race-open-without-external-write",
                "開く: 割り込みが無ければ通常どおり開いて終わる",
                Doc.Write(A, "Original"),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle().Which.Should().Be("Original");
                        o.Status.Should()
                            .Be(Strings.Status_Opened, "開いた通知のまま（再読込へ流れない）");
                        o.IsDirty.Should().BeFalse();
                        o.Path.Should().Be(o.PathOf(A));
                    })
            ),
            Row(
                "race-open-byte-order-mark",
                "開く: BOM 付きのファイルでも外部変更と誤判定しない",
                Doc.Write(A, "WithBom", content: DocContentKind.DiagramWithBom),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle().Which.Should().Be("WithBom");
                        o.Status.Should().Be(Strings.Status_Opened, "BOM の有無で再読込へ流れない");
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "race-reload-external-write-not-overwritten",
                "再読込: 読み取り直後の外部書き込みを、次の上書き保存で黙って消さない",
                Doc.Write(A, "Original"),
                Doc.Open(A).No(),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .No()
                    .RaceAt(DocHook.AfterLoadRead, Doc.WriteOp(A, "ExternalDuringReload"))
                    .Expect(o => o.Tables.Should().ContainSingle().Which.Should().Be("External")),
                Doc.Save()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().Contain(Strings.Confirm_OverwriteExternalChange);
                        o.DiskTables(A)
                            .Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("ExternalDuringReload", "確認で取り消したので外部の内容が残る");
                    })
            ),
            Row(
                "race-save-external-write-detected",
                "保存直後に割り込んだ外部書き込みを検知して再読込する",
                Doc.Add(),
                Doc.Save(A)
                    .RaceAt(DocHook.AfterSaveWrite, Doc.WriteOp(A, "ExternalDuringSave"))
                    .Expect(o =>
                    {
                        o.Tables.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("ExternalDuringSave", "外部内容へ追従する");
                        o.Status.Should().Be(Strings.Status_ExternalReloaded);
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "race-save-without-external-write",
                "割り込みが無ければ保存は通常どおり完了する",
                Doc.Add(),
                Doc.Save(A)
                    .Expect(o =>
                    {
                        o.Status.Should()
                            .Be(Strings.Status_Saved, "保存通知のまま（再読込へ流れない）");
                        o.IsDirty.Should().BeFalse();
                        o.Path.Should().Be(o.PathOf(A));
                    })
            ),
        ];

    /// <summary>A を保存して自動保存し、B を開くまでの手（元のテストの SaveAThenOpenB）</summary>
    private static DocStep[] SaveAThenOpenB() =>
        [
            Doc.Write(B, "OnlyInB"),
            Doc.Add(),
            Doc.Rename(0, "OnlyInA"),
            Doc.Save(A),
            Doc.AutoSave(),
            Doc.Open(B).Expect(o => o.Path.Should().Be(o.PathOf(B))),
        ];

    /// <summary>自動保存の図とメタの照合（メタだけ書けない・両方書けた・旧版のメタ）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> AutoSaveMetaRows() =>
        [
            Row(
                "meta-lost-restores-untitled",
                "メタだけ書けなかった自動保存は、無題・未保存として復元し、上書き保存で前のファイルを上書きしない",
                [
                    .. SaveAThenOpenB(),
                    Doc.AutoSave(metaLost: true),
                    Doc.Restart()
                        .Expect(o =>
                        {
                            o.Tables.Should().Equal("OnlyInB");
                            o.Path.Should().BeNull("メタのパスは別の図のものなので信じない");
                            o.IsDirty.Should().BeTrue("ファイルへ保存されていない内容として開く");
                            o.Status.Should().Be(Strings.Status_AutoSaveRestoredUntitled);
                        }),
                    Doc.Save()
                        .Expect(o =>
                        {
                            o.SaveDialogs.Should().Be(1);
                            o.Harness.UnchangedSinceStepStart(A).Should().BeTrue();
                        }),
                ]
            ),
            Row(
                "meta-both-written-restores-path",
                "図とメタの両方を書けた自動保存は、ファイルのパスを付けて復元する",
                [
                    .. SaveAThenOpenB(),
                    Doc.AutoSave(),
                    Doc.Restart()
                        .Expect(o =>
                        {
                            o.Path.Should().Be(o.PathOf(B));
                            o.IsDirty.Should().BeFalse();
                            o.Tables.Should().Equal("OnlyInB");
                        }),
                ]
            ),
            Row(
                "meta-legacy-without-hash-trusted",
                "図のハッシュを持たない旧版のメタは、照合せずに信じる",
                [
                    .. SaveAThenOpenB(),
                    Doc.AutoSave(),
                    Doc.StripMetaHash(),
                    Doc.Restart()
                        .Expect(o =>
                        {
                            o.Path.Should().Be(o.PathOf(B));
                            o.IsDirty.Should().BeFalse();
                        }),
                ]
            ),
            // メタのハッシュを消す手は自動保存の記録だけを書き換え、図のファイルを読まない。ディスクの図が Id 重複に
            // 書き換わっていても、旧版のメタは照合せずに信じてパスを付けて復元する（読み込むのは自動保存の図）
            Row(
                "meta-legacy-without-hash-trusted-while-disk-is-duplicate-id",
                "図のハッシュを持たない旧版のメタは、図のファイルが Id 重複に書き換わっていても照合せずに信じる",
                [
                    .. SaveAThenOpenB(),
                    Doc.AutoSave(),
                    Doc.WriteKind(B, DocContentKind.DuplicateId, "duplicate-id"),
                    Doc.StripMetaHash(),
                    Doc.Restart()
                        .Expect(o =>
                        {
                            o.Path.Should().Be(o.PathOf(B));
                            o.Tables.Should()
                                .ContainSingle()
                                .Which.Should()
                                .Be("OnlyInB", "復元するのは自動保存の図");
                        }),
                ]
            ),
        ];

    /// <summary>開けない図の案内（Id 重複・必須の文字列の欠落・図でない JSON）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> OpenFailureRows() =>
        [
            Row(
                "openfail-duplicate-id-dedicated-message",
                "開く: Id が重複する図は、手で直すよう案内する専用の文言で断る",
                Doc.WriteRaw(
                    A,
                    DocContentKind.DuplicateId,
                    "duplicate-id",
                    $$"""
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          { "Id": "{{DuplicatedId}}", "TableName": "Orders", "Columns": [] },
                          { "Id": "{{DuplicatedId}}", "TableName": "OrderLines", "Columns": [] }
                        ]
                      }
                    }
                    """
                ),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        var message = o.Errors.Should().ContainSingle().Subject;
                        message
                            .Should()
                            .StartWith(string.Format(Strings.Open_DuplicateId, o.PathOf(A)));
                        message.Should().Contain(DuplicatedId.ToString(), "手で直すには場所が要る");
                    })
            ),
            Row(
                "openfail-missing-required-text-dedicated-message",
                "開く: 必須の名前が null の図は、手で直すよう案内する専用の文言で断る",
                Doc.WriteRaw(
                    A,
                    DocContentKind.MissingRequiredText,
                    "missing-table-name",
                    """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          { "TableName": null, "Columns": [] }
                        ]
                      }
                    }
                    """
                ),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        var message = o.Errors.Should().ContainSingle().Subject;
                        message
                            .Should()
                            .StartWith(
                                string.Format(Strings.Open_MissingRequiredText, o.PathOf(A))
                            );
                        message.Should().Contain("TableName", "手で直すには場所が要る");
                    })
            ),
            Row(
                "openfail-not-diagram-generic-message",
                "開く: 無関係な JSON は一般の失敗として断る",
                Doc.WriteRaw(
                    A,
                    DocContentKind.NotDiagram,
                    "hello-world",
                    """{ "hello": "world" }"""
                ),
                Doc.Open(A)
                    .Expect(o =>
                        o.Errors.Should()
                            .ContainSingle()
                            .Which.Should()
                            .StartWith(string.Format(Strings.Open_Failed, o.PathOf(A)))
                    )
            ),
        ];

    /// <summary>Id 重複のシナリオの重複した Id（元のテストは実行ごとの新しい Guid。どの値でも同じことを確かめる）</summary>
    private static Guid DuplicatedId => new("5c1f0e4e-9a3b-4d55-8f1e-3d2b7c6a9e01");
}
