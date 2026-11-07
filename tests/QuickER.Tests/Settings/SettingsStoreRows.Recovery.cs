using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Settings.SettingsOpKind;

namespace QuickER.Tests.Settings;

internal static partial class SettingsStoreRows
{
    /// <summary>読めるようになったときの自己修復・壊れたファイルの退避・インポートとエクスポート</summary>
    private static IEnumerable<ScenarioRow<SettingsScenario>> RecoveryRows() =>
        [
            // 抑止が解けないと、一度の一時的な失敗以降その設定を二度と保存できなくなる
            Row(
                "save-after-unavailable-then-successful-load-writes-again",
                "Save: 次の Load が成功すれば抑止は解ける",
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                },
                Step<ProbeSettings>(LockFile),
                Step<ProbeSettings>(Load),
                Step<ProbeSettings>(UnlockFile) with
                {
                    Expect = o =>
                        o.Store.LastLoadUnavailable.Should().BeTrue("まだ Load し直していない"),
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o =>
                        o.Store.LastLoadUnavailable.Should().BeFalse("読めたので抑止は解ける"),
                },
                Step<ProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Language = "en",
                    Expect = o => o.Saved.Should().BeTrue(),
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Language.Should().Be("en"),
                }
            ),
            Row(
                "load-corrupt-json-uses-defaults-without-suppressing-save",
                "Load: 壊れた JSON は既定値で読み保存は抑止しない",
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "{ これは JSON ではない",
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o =>
                    {
                        o.Loaded!.Language.Should().BeEmpty();
                        o.Store.LastLoadCorrupt.Should().BeTrue();
                        o.Store.LastLoadUnavailable.Should()
                            .BeFalse("読めてはいるので保存は抑止しない");
                    },
                }
            ),
            // 退避に失敗しても保存は続ける。書けたファイルはもう壊れていないので、壊れていた印を残すと、
            // 読み直さずに続けた次の保存が正常なファイルを退避先へ写す
            Row(
                "save-after-failed-backup-does-not-back-up-healthy-file",
                "Save: 退避に失敗したあとの次の保存は、正常なファイルを .corrupt へ写さない",
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "壊れた",
                },
                Step<ProbeSettings>(Load),
                // 退避先にフォルダがあると File.Copy が失敗する（退避はベストエフォートで、保存は続く）
                Step<ProbeSettings>(BlockBackup),
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o => o.Saved.Should().BeTrue(),
                },
                Step<ProbeSettings>(UnblockBackup),
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "en" },
                    Expect = o =>
                    {
                        o.Saved.Should().BeTrue();
                        o.BackupExists.Should()
                            .BeFalse("書けたファイルはもう壊れていないので退避しない");
                    },
                }
            ),
            // 残したいのは「最後に正しく読めていた状態にいちばん近いもの」＝最も古い退避
            Row(
                "save-corrupt-json-backs-up-only-once",
                "Save: 壊れた JSON は最初の 1 回だけ .corrupt へ退避する",
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "壊れた 1 回目",
                },
                Step<ProbeSettings>(Load),
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o =>
                    {
                        o.BackupExists.Should().BeTrue();
                        o.BackupText.Should().Be("壊れた 1 回目");
                    },
                },
                Step<ProbeSettings>(Load) with
                {
                    Expect = o =>
                        o.Loaded!.Language.Should()
                            .Be("ja", "壊れたファイルは正常な内容で置き換わる"),
                },
                // 2 度目の破損（別の内容）でも、最初の退避は上書きしない
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "壊れた 2 回目",
                },
                Step<ProbeSettings>(Load),
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "en" },
                    Expect = o => o.BackupText.Should().Be("壊れた 1 回目", "最も古い退避を残す"),
                }
            ),
            Row(
                "save-healthy-file-does-not-create-backup",
                "Save: 正常に読めたファイルは .corrupt を作らない",
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "ja" },
                },
                Step<ProbeSettings>(Load),
                Step<ProbeSettings>(Save) with
                {
                    Mutate = loaded => loaded.Language = "en",
                    Expect = o => o.BackupExists.Should().BeFalse(),
                }
            ),
            // TryLoadFrom は利用者の明示操作なので、失敗を既定値で隠さず null を返す。
            // SaveTo は既定ファイルを読まないため抑止の対象外。
            // 元のテストは設定ファイルを FileMode.Create・書き込みで開いたまま（空のファイルを作って排他）にしている
            Row(
                "tryloadfrom-and-saveto-unaffected-by-suppression",
                "TryLoadFrom/SaveTo: 抑止の影響を受けず従来どおり動く",
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = string.Empty,
                },
                Step<ProbeSettings>(LockFile),
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Store.LastLoadUnavailable.Should().BeTrue(),
                },
                // 抑止中でもエクスポートは別ファイルなので通る
                Step<ProbeSettings>(SaveTo) with
                {
                    Name = "exported.json",
                    Make = () => new ProbeSettings { Language = "ja" },
                },
                Step<ProbeSettings>(UnlockFile),
                Step<ProbeSettings>(TryLoadFrom) with
                {
                    Name = "exported.json",
                    Expect = o => o.TryLoaded!.Language.Should().Be("ja"),
                },
                Step<ProbeSettings>(TryLoadFrom) with
                {
                    Name = "absent.json",
                    Expect = o => o.TryLoaded.Should().BeNull(),
                }
            ),
            // ここから 3 行は、エクスポート（SaveTo）とインポート（TryLoadFrom）が設定ファイルの状態に依らず
            // 指定のパスだけを読み書きし、設定ファイルに触れないことを、設定ファイルの状態ごとに見る
            // （SaveTo・TryLoadFrom の XmlDoc：任意のパスへ保存する・任意のパスから読み込む）
            Row(
                "export-and-import-without-settings-file-leave-it-absent",
                "SaveTo/TryLoadFrom: 設定ファイルが無くても書き出し・読み込みでき、設定ファイルを作らない",
                Step<ProbeSettings>(SaveTo) with
                {
                    Name = "exported.json",
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o => o.FileText.Should().BeNull("設定ファイルを作らない"),
                },
                Step<ProbeSettings>(TryLoadFrom) with
                {
                    Name = "exported.json",
                    Expect = o =>
                    {
                        o.TryLoaded!.Language.Should().Be("ja");
                        o.FileText.Should().BeNull("設定ファイルを作らない");
                    },
                }
            ),
            Row(
                "export-beside-healthy-or-corrupt-settings-file-leaves-it-unchanged",
                "SaveTo: 設定ファイルが読める・壊れているどちらでも書き出せ、設定ファイルを変えず、その未知のキーを書き出し先へ混ぜない",
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "{\"Language\":\"en\",\"Unknown\":1}",
                },
                Step<ProbeSettings>(SaveTo) with
                {
                    Name = "exported.json",
                    Make = () => new ProbeSettings { Language = "ja" },
                    Expect = o =>
                    {
                        o.FileText.Should().Be("{\"Language\":\"en\",\"Unknown\":1}");
                        System
                            .IO.File.ReadAllText(o.PathOf("exported.json"))
                            .Should()
                            .NotContain(
                                "Unknown",
                                "エクスポートは未知のキーを引き継がない（SaveTo の XmlDoc）"
                            );
                    },
                },
                Step<ProbeSettings>(WriteFile) with
                {
                    Text = "{ broken",
                },
                Step<ProbeSettings>(SaveTo) with
                {
                    Name = "exported-again.json",
                    Make = () => new ProbeSettings { Language = "fr" },
                    Expect = o =>
                    {
                        o.FileText.Should().Be("{ broken", "壊れた設定ファイルも書き換えない");
                        System
                            .IO.File.ReadAllText(o.PathOf("exported-again.json"))
                            .Should()
                            .Contain("fr");
                    },
                },
                Step<ProbeSettings>(TryLoadFrom) with
                {
                    Name = "exported-again.json",
                    Expect = o => o.TryLoaded!.Language.Should().Be("fr"),
                }
            ),
            Row(
                "import-while-settings-file-is-locked-reads-the-given-file",
                "TryLoadFrom: 設定ファイルが別のハンドルに排他で開かれていても、指定のファイルは読める",
                Step<ProbeSettings>(Save) with
                {
                    Make = () => new ProbeSettings { Language = "en" },
                },
                Step<ProbeSettings>(SaveTo) with
                {
                    Name = "exported.json",
                    Make = () => new ProbeSettings { Language = "ja" },
                },
                Step<ProbeSettings>(LockFile),
                Step<ProbeSettings>(TryLoadFrom) with
                {
                    Name = "exported.json",
                    Expect = o => o.TryLoaded!.Language.Should().Be("ja"),
                },
                Step<ProbeSettings>(UnlockFile),
                Step<ProbeSettings>(Load) with
                {
                    Expect = o => o.Loaded!.Language.Should().Be("en", "設定ファイルはそのまま"),
                }
            ),
        ];
}
