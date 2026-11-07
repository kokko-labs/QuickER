using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>
    /// 壊れた connections.json（ストアの分類 Corrupt）：開くときと一覧の読み直しで、壊れていること・ファイルの場所・
    /// 最初に壊れていたファイルの写しの場所をステータス行で知らせ、空の一覧として黙って出さない
    /// </summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> CorruptRows() =>
        [
            Row(
                "open-corrupt-connections-file-reports-and-shows-empty-list",
                "壊れた connections.json（JSON として読めない・旧配列形式）で開くと、取込・同期のどちらでも一覧を空にして壊れていることを知らせ、モーダルを出さず、ファイルも変えない",
                [(Profiles, Open)],
                h =>
                {
                    foreach (var text in new[] { "{ broken", "[{\"name\":\"Legacy\"}]" })
                    {
                        foreach (var mode in Enum.GetValues<DbConnectionDialogMode>())
                        {
                            File.WriteAllText(h.Store.ConnectionsPath, text);
                            var before = h.Snapshot();

                            var vm = h.Create(
                                mode: mode,
                                fixedProvider: mode == DbConnectionDialogMode.Sync
                                    ? h.SqlServer
                                    : null
                            );

                            vm.Profiles.Should().BeEmpty();
                            vm.SelectedProfileItem.Should().BeNull();
                            vm.AreProfilesUnavailable.Should()
                                .BeFalse("読めてはいる（壊れている）");
                            vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                            h.Snapshot().Should().Equal(before);
                            File.Exists(h.Store.CorruptBackupPath)
                                .Should()
                                .BeFalse("退避は保存のときだけ");
                        }
                    }

                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                }
            ),
            Row(
                "save-after-corrupt-open-backs-up-and-lists-saved-profile",
                "壊れた connections.json で開いて保存すると、壊れたファイルを退避してから書き直し、保存したプロファイルを一覧に出す",
                [(Profiles, Save)],
                h =>
                {
                    File.WriteAllText(h.Store.ConnectionsPath, "{ broken");
                    var vm = h.Create();
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "本番DB"));
                    File.ReadAllText(h.Store.CorruptBackupPath).Should().Be("{ broken");
                    vm.Profiles.Should().ContainSingle().Which.Profile.Name.Should().Be("本番DB");
                    vm.SelectedProfile!.Name.Should().Be("本番DB");
                }
            ),
            Row(
                "save-connections-file-corrupted-right-after-write-reports-corrupt",
                "保存した直後の読み直しの前に connections.json が壊されると、保存した文で上書きせずに壊れていることを知らせ、一覧と選択を空にする",
                [(Profiles, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    vm.Host = "staging.example.com";
                    vm.ProfileName = "検証DB";
                    h.CorruptConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.SaveProfile（保存の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Save(vm);

                    vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();
                }
            ),
            Row(
                "rename-connections-file-corrupted-right-after-write-reports-corrupt",
                "名前の変更の直後の読み直しの前に connections.json が壊されると、変更した文で上書きせずに壊れていることを知らせ、一覧と選択を空にする",
                [(Selected, Rename)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    vm.ProfileName = "本番DB（新）";
                    h.CorruptConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.RenameProfile（変更の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Rename(vm);

                    vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();
                }
            ),
            Row(
                "delete-connections-file-corrupted-right-after-write-reports-corrupt",
                "削除の直後の読み直しの前に connections.json が壊されると、削除した文で上書きせずに壊れていることを知らせ、一覧と選択を空にする",
                [(Selected, Delete)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    h.SeedProfile("検証DB", "staging.example.com", "staging-secret");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    h.CorruptConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.DeleteProfile（削除の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Delete(vm);

                    vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();
                }
            ),
            Row(
                "save-write-fails-then-connections-file-corrupted-at-reload-reports-corrupt",
                "暗号ファイルを書けない保存の後の読み直しの前に connections.json が壊されると、「一覧を読み直した」とは言わずに壊れていることを知らせ、一覧を空にする",
                [(WriteFails, Save)],
                h =>
                {
                    h.BlockSecretsFolder();
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.SavePassword = true;
                    vm.Password = "secret";
                    vm.ProfileName = "新しい接続";
                    h.CorruptConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.ReportStoreWriteFailure（読み直しで一覧を出せなければ、そこで戻る）
                    h.Save(vm);

                    vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();
                }
            ),
            Row(
                "save-conflict-then-connections-file-corrupted-at-reload-stops-without-asking",
                "保存がほかのプロセスの同名に断られた後の読み直しの前に connections.json が壊されると、上書きを確かめずに壊れていることを知らせて止め、何も書かない",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    SaveFromOtherProcess(h, "本番DB", "prod.example.com");
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";
                    h.CorruptConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.ResolveNameConflict（読み直しで一覧を出せなければ、確かめずに戻る）
                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    vm.StatusMessage.Should().Be(h.ProfilesCorruptMessage);
                    vm.Profiles.Should().BeEmpty();
                    File.Exists(h.Store.CorruptBackupPath)
                        .Should()
                        .BeFalse("書き直していない（退避は書くときだけ）");
                }
            ),
        ];
}
