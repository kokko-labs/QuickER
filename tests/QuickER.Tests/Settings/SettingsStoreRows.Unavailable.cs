using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Settings.SettingsOpKind;

namespace QuickER.Tests.Settings;

internal static partial class SettingsStoreRows
{
    /// <summary>ファイルが無い・読めないときの読みと保存の抑止</summary>
    private static IEnumerable<ScenarioRow<SettingsScenario>> UnavailableRows() =>
        [
            Row(
                "load-missing-file-defaults-and-allows-save",
                "Load: ファイルが無ければ既定値で読み、保存も抑止しない",
                Step<ProbeSettings>(Load) with
                {
                    Expect = o =>
                    {
                        o.Loaded!.Language.Should().BeEmpty();
                        o.Store.LastLoadUnavailable.Should().BeFalse();
                        o.Store.LastLoadCorrupt.Should().BeFalse();
                    },
                },
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Language.Should().Be("ja"),
                }
            ),
            Row(
                "save-after-unavailable-load-does-not-overwrite",
                "Load/Save: 読み取れなかったら既定値で起動し保存を抑止する",
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o => o.Memo["original"] = o.FileText,
                },
                Step<ProbeSettings>(LockFile),
                Step<ProbeSettings>(Load) with
                {
                    Expect = o =>
                    {
                        o.Loaded!.Language.Should().BeEmpty("読めなかったので既定値で起動する");
                        o.Store.LastLoadUnavailable.Should().BeTrue();
                    },
                },
                Step<ProbeSettings>(Save) with
                {
                    Expect = o => o.Saved.Should().BeFalse("書かずに戻ったことを呼び出し側へ返す"),
                },
                Step<ProbeSettings>(UnlockFile) with
                {
                    Expect = o =>
                        o.FileText.Should()
                            .Be(
                                (string?)o.Memo["original"],
                                "読めなかった内容を既定値で塗り潰さない"
                            ),
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Language.Should().Be("ja"),
                }
            ),
            // 移す前からの穴（Save の冒頭の LastLoadUnavailable を守る）。ほかの行はロックしたまま保存するので、
            // 保存の直前の読み直し（Unavailable）が二重に守り、冒頭の判定を外しても赤にならなかった
            Row(
                "save-after-unavailable-load-stays-suppressed-after-unlock",
                "Save: 読めなかった Load の後は、ファイルが読めるようになっても Load し直すまで保存しない",
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o => o.Memo["original"] = o.FileText,
                },
                Step<ProbeSettings>(LockFile),
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Store.LastLoadUnavailable.Should().BeTrue(),
                },
                Step<ProbeSettings>(UnlockFile),
                Step<ProbeSettings>(Save) with
                {
                    Expect = o =>
                    {
                        o.Saved.Should().BeFalse("読めなかった Load の既定値で塗り潰さない");
                        o.FileText.Should().Be((string?)o.Memo["original"]);
                    },
                }
            ),
        ];
}
