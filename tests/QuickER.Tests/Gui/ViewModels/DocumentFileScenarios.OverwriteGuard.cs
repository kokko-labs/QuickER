using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.ViewModels.DocFile;

namespace QuickER.Tests.Gui.ViewModels;

internal static partial class DocumentFileScenarios
{
    /// <summary>
    /// 図を開いて編集し、外部版を「続行」で無視したうえで上書き保存するまでの手（元のテストの IgnoreExternalVersionThenSave）
    /// </summary>
    private static DocStep[] IgnoreExternalVersionThenSave() =>
        [
            Doc.Write(A, "Original"),
            OpenClean(),
            Doc.Add(),
            Doc.Write(A, "External"),
            Doc.Modified()
                .No()
                .Expect(o => o.WarningConfirms.Should().ContainSingle("ここで続行を選ぶ")),
            Doc.Save().No().Expect(o => o.IsDirty.Should().BeFalse()),
        ];

    /// <summary>上書き保存の前のディスクの照合（外部変更・別のファイル・大小違いの同じパス・無視した版・書き出し）</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> OverwriteGuardRows() =>
        [
            Row(
                "guard-external-cancel-keeps-disk",
                "上書き保存: 外部変更を確認し、キャンセルすると書き込まない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Save()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteExternalChange);
                        o.DiskTables(A)
                            .Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("External", "キャンセルでは書き込まない");
                        o.IsDirty.Should().BeTrue("保存していないためダーティのまま");
                        o.Status.Should().NotBe(Strings.Status_Saved);
                    })
            ),
            Row(
                "guard-external-confirm-overwrites",
                "上書き保存: 外部変更の確認に続行すると上書きする",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().Contain(Strings.Confirm_OverwriteExternalChange);
                        o.DiskTables(A).Should().HaveCount(2, "現在の図で上書きする");
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "guard-unchanged-no-confirm",
                "上書き保存: 外部変更が無ければ確認しない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().BeEmpty();
                        o.Status.Should().Be(Strings.Status_Saved);
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "guard-other-file-not-compared",
                "上書き保存: 現在の文書でないファイルへの保存は照合しない",
                Doc.Write(B, "Unrelated"),
                Doc.Add(),
                Doc.Save(B)
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().BeEmpty("現在の文書を上書きするわけではない");
                        o.Path.Should().Be(o.PathOf(B));
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "guard-saveas-same-path-other-case",
                "名前を付けて保存: 大小違いの同一パスでも外部変更を照合する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.SaveAs(A, upperCase: true)
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteExternalChange);
                        o.DiskTables(A)
                            .Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be("External", "同じファイルなので書き込まない");
                    })
            ),
            Row(
                "guard-ignored-version-no-reconfirm",
                "上書き保存: 続行を選んだ外部バージョンでは再確認しない",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Modified().No().Expect(o => o.WarningConfirms.Should().ContainSingle()),
                Doc.Save()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .NotContain(
                                Strings.Confirm_OverwriteExternalChange,
                                "同じ内容について続行を選んだ後は再確認しない"
                            );
                        o.Status.Should().Be(Strings.Status_Saved);
                        o.DiskTables(A).Should().HaveCount(2);
                    })
            ),
            Row(
                "guard-ignored-forgotten-after-save-external",
                "外部変更: 続行で無視した版も、保存した後に書き戻されたら改めて確認する",
                [
                    .. IgnoreExternalVersionThenSave(),
                    Doc.Restore(A, "External"),
                    Doc.Add(),
                    Doc.Modified()
                        .No()
                        .Expect(o =>
                            o.WarningConfirms.Should()
                                .ContainSingle()
                                .Which.Should()
                                .Be(Strings.Confirm_ExternalChangeReload)
                        ),
                ]
            ),
            Row(
                "guard-ignored-forgotten-after-save-overwrite",
                "上書き保存: 続行で無視した版も、保存した後に書き戻されたら上書き前に確認する",
                [
                    .. IgnoreExternalVersionThenSave(),
                    Doc.Restore(A, "External"),
                    Doc.Add(),
                    Doc.Save()
                        .No()
                        .Expect(o =>
                        {
                            o.WarningConfirms.Should()
                                .ContainSingle()
                                .Which.Should()
                                .Be(Strings.Confirm_OverwriteExternalChange);
                            o.DiskBytes(A)
                                .Should()
                                .Equal(o.BytesOf("External"), "キャンセルでは書き込まない");
                        }),
                ]
            ),
            Row(
                "guard-export-to-current-rejected",
                "エクスポート: 出力先に開いている図のファイルを選ぶと何も書かずに断る",
                Doc.Write(A, "Original"),
                Doc.Open(A),
                Doc.ExportToCurrent()
                    .Expect(o =>
                    {
                        o.Errors.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Export_TargetIsCurrentDocument);
                        o.DiskBytes(A).Should().Equal(o.BytesOf("Original"));
                    })
            ),
        ];

    /// <summary>将来版の書き戻しの確認（保存先がどのファイルでも・置換や新規の後も・確認は 1 回）と、将来版を開く前の確認</summary>
    private static IEnumerable<ScenarioRow<DocScenario>> NewerFormatRows() =>
        [
            Row(
                "newer-save-cancel-keeps-file",
                "上書き保存: 将来版の文書を確認し、キャンセルすると書き戻さない",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A)
                    .Expect(o => o.Tables.Should().ContainSingle("将来版でも続行すれば開ける")),
                Doc.Add(),
                Doc.Save()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().Contain(Strings.Confirm_OverwriteNewerFormat);
                        o.DiskVersion(A)
                            .Should()
                            .Be(DiagramDocument.CurrentVersion + 1, "キャンセルでは書き戻さない");
                        o.IsDirty.Should().BeTrue();
                    })
            ),
            Row(
                "newer-save-confirm-overwrites",
                "上書き保存: 将来版の確認に続行すると現行フォーマットで上書きする",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A),
                Doc.Add(),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().Contain(Strings.Confirm_OverwriteNewerFormat);
                        o.DiskVersion(A).Should().Be(DiagramDocument.CurrentVersion);
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "newer-current-format-no-confirm",
                "上書き保存: 現行フォーマットの文書では確認しない",
                Doc.Write(A, "Original"),
                Doc.Open(A),
                Doc.Add(),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().BeEmpty();
                        o.Status.Should().Be(Strings.Status_Saved);
                    })
            ),
            Row(
                "newer-after-replacement-still-confirms",
                "上書き保存: 図を置換しても保存先が将来版なら確認する",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A),
                Doc.ReplaceFromModule("Imported"),
                Doc.Save()
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteNewerFormat);
                        o.DiskVersion(A)
                            .Should()
                            .Be(DiagramDocument.CurrentVersion + 1, "キャンセルでは書き戻さない");
                    })
            ),
            Row(
                "newer-after-new-diagram-confirms",
                "上書き保存: 新規作成の後でも保存先が将来版なら確認する",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A),
                Doc.New().Expect(o => o.Path.Should().BeNull("新規作成は現在パスを落とす")),
                Doc.Save(A)
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteNewerFormat);
                        o.DiskVersion(A).Should().Be(DiagramDocument.CurrentVersion + 1);
                    })
            ),
            Row(
                "newer-unreadable-version-confirms",
                "上書き保存: 版を解釈できないファイルも確認する",
                Doc.WriteRaw(
                    A,
                    DocContentKind.UnreadableVersion,
                    "Odd",
                    "{\"Version\":\"2\",\"Schema\":{\"Entities\":[]}}"
                ),
                Doc.Add(),
                Doc.Save(A)
                    .No()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteNewerFormat);
                        o.DiskText(A)
                            .Should()
                            .Contain("\"Version\":\"2\"", "キャンセルでは書き戻さない");
                    })
            ),
            Row(
                "newer-after-overwriting-once-no-confirm",
                "上書き保存: 一度上書きした後の再保存では確認しない",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A),
                Doc.Add(),
                Doc.Save()
                    .Expect(o => o.DiskVersion(A).Should().Be(DiagramDocument.CurrentVersion)),
                Doc.Add(),
                Doc.Save()
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should().BeEmpty();
                        o.Status.Should().Be(Strings.Status_Saved);
                    })
            ),
            Row(
                "newer-and-external-change-confirms-once",
                "上書き保存: 将来版と外部変更が重なっても確認は 1 回だけ",
                Doc.Write(A, "Original", content: DocContentKind.Newer),
                Doc.Open(A),
                Doc.Add(),
                Doc.Write(A, "External", content: DocContentKind.Newer),
                Doc.Save()
                    .No()
                    .Expect(o =>
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(
                                Strings.Confirm_OverwriteNewerFormat,
                                "失うものが大きい方の文言を 1 回だけ出す"
                            )
                    )
            ),
            Row(
                "newer-open-cancel-keeps-current",
                "Open: 新しいフォーマットの警告をキャンセルすると読み込まない",
                Doc.Write(A, "T1", content: DocContentKind.Newer),
                Doc.Open(A)
                    .No()
                    .Expect(o =>
                    {
                        o.Tables.Should().BeEmpty();
                        o.WarningConfirms.Should().ContainSingle();
                    })
            ),
            Row(
                "newer-open-confirm-loads",
                "Open: 新しいフォーマットの警告に続行すると読み込む",
                Doc.Write(A, "T1", content: DocContentKind.Newer),
                Doc.Open(A)
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle();
                        o.WarningConfirms.Should().ContainSingle();
                    })
            ),
        ];

    /// <summary>保存に失敗して、ダーティ・パス・ディスクが変わらないことの表明</summary>
    private static Action<DocObservation> SaveFailed(string onDisk) =>
        o =>
        {
            o.Errors.Should().ContainSingle().Which.Should().StartWith(Strings.Save_Failed);
            o.IsDirty.Should().BeTrue("保存できていないのでダーティのまま");
            o.PathFile.Should().Be(A);
            o.DiskTables(A).Should().ContainSingle().Which.Should().Be(onDisk);
        };

    /// <summary>
    /// 読めない・書けないファイル（手の間だけ FileShare.None / FileShare.Read で開いたまま・読み取り専用の属性）での保存・開く・
    /// 外部変更・起動時の照合
    /// </summary>
    private static IEnumerable<ScenarioRow<DocScenario>> AccessRows() =>
        [
            Row(
                "access-save-share-none-skips-divergence-confirm-and-fails",
                "上書き保存: 読めない現在のファイルはハッシュを算出できず、外部変更の確認を出さない。書き込みに失敗してダーティのまま",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Save()
                    .WithAccess(A, DocAccessKind.ShareNone)
                    .Expect(o => o.WarningConfirms.Should().BeEmpty())
                    .Expect(SaveFailed("External"))
            ),
            Row(
                "access-save-share-read-confirms-then-fails",
                "上書き保存: 読めるが書けない現在のファイルは外部変更を確認し、続行しても書き込みに失敗する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Save()
                    .WithAccess(A, DocAccessKind.ShareRead)
                    .Expect(o =>
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteExternalChange)
                    )
                    .Expect(SaveFailed("External"))
            ),
            Row(
                "access-save-read-only-newer-confirms-then-fails",
                "上書き保存: 読み取り専用の将来版は書き戻しを確認し、続行しても書き込みに失敗する",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "Future", content: DocContentKind.Newer),
                Doc.Save()
                    .WithAccess(A, DocAccessKind.ReadOnly)
                    .Expect(o =>
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_OverwriteNewerFormat)
                    )
                    .Expect(SaveFailed("Future"))
            ),
            // 根拠の実装：src/QuickER.Document/JsonStorageService.cs:802（TryReadFormatVersion の catch＝版が読めないことは保存を妨げない）
            Row(
                "access-save-share-none-newer-skips-confirm-and-fails",
                "特性の固定（文書に定めが無い今の振る舞い）：上書き保存: 読めない将来版は版を読めず、書き戻しの確認を出さない。書き込みに失敗してダーティのまま",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "Future", content: DocContentKind.Newer),
                Doc.Save()
                    .WithAccess(A, DocAccessKind.ShareNone)
                    .Expect(o => o.WarningConfirms.Should().BeEmpty())
                    .Expect(SaveFailed("Future"))
            ),
            Row(
                "access-save-ignored-read-only-fails",
                "上書き保存: 続行で無視した版の読み取り専用のファイルは確認せず、書き込みに失敗してダーティのまま",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Modified().No(),
                Doc.Save()
                    .WithAccess(A, DocAccessKind.ReadOnly)
                    .Expect(o => o.WarningConfirms.Should().BeEmpty())
                    .Expect(SaveFailed("External"))
            ),
            Row(
                "access-save-as-share-read-other-file-fails",
                "名前を付けて保存: 書けない別のファイルへの保存は失敗し、無題・ダーティのまま",
                Doc.Write(B, "Other"),
                Doc.Add(),
                Doc.SaveAs(B)
                    .WithAccess(B, DocAccessKind.ShareRead)
                    .Expect(o =>
                    {
                        o.Errors.Should()
                            .ContainSingle()
                            .Which.Should()
                            .StartWith(Strings.Save_Failed);
                        o.PathFile.Should().BeNull();
                        o.IsDirty.Should().BeTrue();
                        o.DiskTables(B).Should().ContainSingle().Which.Should().Be("Other");
                    })
            ),
            Row(
                "access-open-share-none-fails-when-current-file-missing",
                "Open: 読めないファイルは開けず、現在の図・パス・未保存を変えない（現在のファイルが消えている間）",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Write(B, "Other"),
                Doc.Delete(A),
                Doc.Open(B)
                    .WithAccess(B, DocAccessKind.ShareNone)
                    .Expect(o =>
                    {
                        o.Errors.Should()
                            .ContainSingle()
                            .Which.Should()
                            .StartWith(string.Format(Strings.Open_Failed, o.PathOf(B)));
                        o.PathFile.Should().Be(A);
                        o.Tables.Should().ContainSingle().Which.Should().Be("Original");
                    })
            ),
            Row(
                "access-open-share-read-opens",
                "Open: 読めるが書けないファイルは開ける",
                Doc.Write(A, "Original"),
                Doc.Open(A)
                    .WithAccess(A, DocAccessKind.ShareRead)
                    .Expect(o =>
                    {
                        o.Errors.Should().BeEmpty();
                        o.Tables.Should().ContainSingle().Which.Should().Be("Original");
                        o.Status.Should().Be(Strings.Status_Opened);
                    })
            ),
            Row(
                "access-deliver-share-none-clean-reload-fails",
                "クリーン: 読めない現在のファイルの外部変更（ハッシュ null）は確認せず再読込を試み、失敗を通知して現状維持",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .WithAccess(A, DocAccessKind.ShareNone)
                    .Expect(o =>
                    {
                        o.Confirms.Should().BeEmpty();
                        o.Status.Should().Be(Strings.Status_ExternalReloadFailed);
                        o.Tables.Should().ContainSingle().Which.Should().Be("Original");
                        o.IsDirty.Should().BeFalse();
                    })
            ),
            Row(
                "access-deliver-share-none-dirty-confirms-reload-fails",
                "ダーティ: 読めない現在のファイルの外部変更（ハッシュ null）は確認し、再読込を選んでも失敗を通知してダーティのまま",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.Add(),
                Doc.Write(A, "External"),
                Doc.Modified()
                    .WithAccess(A, DocAccessKind.ShareNone)
                    .Expect(o =>
                    {
                        o.WarningConfirms.Should()
                            .ContainSingle()
                            .Which.Should()
                            .Be(Strings.Confirm_ExternalChangeReload);
                        o.Status.Should().Be(Strings.Status_ExternalReloadFailed);
                        o.Tables.Should().HaveCount(2);
                        o.IsDirty.Should().BeTrue();
                    })
            ),
            Row(
                "access-restart-share-none-skips-startup-check",
                "起動時の照合: 読めない現在のファイルはハッシュを算出できず何もしない（読めるようになった次の起動で再読込する）",
                Doc.Write(A, "Original"),
                OpenClean(),
                Doc.AutoSave(),
                Doc.Write(A, "External"),
                Doc.Restart()
                    .WithAccess(A, DocAccessKind.ShareNone)
                    .Expect(o =>
                    {
                        o.PathFile.Should().Be(A);
                        o.Tables.Should().ContainSingle().Which.Should().Be("Original");
                        o.Notified.Should().BeFalse();
                    }),
                Doc.Restart()
                    .Expect(o =>
                    {
                        o.Tables.Should().ContainSingle().Which.Should().Be("External");
                        o.Status.Should().Be(Strings.Status_ExternalReloaded);
                    })
            ),
        ];
}
