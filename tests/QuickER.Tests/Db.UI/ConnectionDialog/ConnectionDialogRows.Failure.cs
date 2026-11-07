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
    /// <summary>ストアを読めない・書けない・保存済みのパスワードを読めないとき</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> FailureRows() =>
        [
            Row(
                "save-store-unavailable-reports-and-keeps-list",
                "接続情報ファイルを読めないときの保存は状態の文で知らせ、一覧を空にせず、何も書かない",
                [(StoreLocked, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    vm.Host = "new-server";
                    vm.Database = "new-db";
                    vm.ProfileName = "新しい接続";

                    using (h.LockConnectionsFile())
                    {
                        h.Save(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                        vm.Profiles.Should()
                            .ContainSingle("一覧は空にしない")
                            .Which.Profile.Name.Should()
                            .Be("本番DB");
                    }

                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("本番DB");
                }
            ),
            Row(
                "delete-store-unavailable-reports-and-keeps-selection",
                "接続情報ファイルを読めないときの削除は状態の文で知らせ、選択も一覧も保つ",
                [(StoreLocked, Delete)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());

                    using (h.LockConnectionsFile())
                    {
                        h.Delete(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                        vm.SelectedProfile.Should().NotBeNull("削除していないので選択も外さない");
                        vm.Profiles.Should().ContainSingle();
                    }

                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("本番DB");
                }
            ),
            Row(
                "rename-store-unavailable-keeps-replaced-profile",
                "置き換えを伴う名前の変更が中止されても、置き換え先も対象も暗号ファイルも残る",
                [(StoreLocked, Rename)],
                h =>
                {
                    var targetId = h.SeedProfile("本番DB");
                    var conflictId = h.SeedProfile(
                        "本番DB（東京）",
                        "tokyo.example.com",
                        "tokyo-secret"
                    );
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(item => item.Profile.Id == targetId));
                    vm.ProfileName = "本番DB（東京）";

                    using (h.LockConnectionsFile())
                    {
                        h.Rename(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                    }

                    var stored = h.Store.Read().Profiles;
                    stored.Should().HaveCount(2, "読めなかったのだから、どちらも消さない");
                    stored.Single(p => p.Id == targetId).Name.Should().Be("本番DB");
                    stored.Single(p => p.Id == conflictId).Name.Should().Be("本番DB（東京）");
                    h.Store.LoadPassword(conflictId)
                        .Value.Should()
                        .Be("tokyo-secret", "暗号ファイルも消さない");
                }
            ),
            Row(
                "rename-store-unavailable-keeps-displayed-name",
                "接続情報ファイルを読めないときの名前の変更は、保存できていない名前を画面に残さない",
                [(StoreLocked, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.ProfileName = "本番DB（東京）";

                    using (h.LockConnectionsFile())
                    {
                        h.Rename(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                        vm.Profiles.Single()
                            .Profile.Name.Should()
                            .Be("本番DB", "保存できていない名前を画面に残さない");
                    }

                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("本番DB");
                }
            ),
            Row(
                "rename-store-unavailable-with-conflict-keeps-both",
                "名前の衝突を置き換える経路でも、読めないときは衝突相手も対象も消えない",
                [(StoreLocked, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    h.SeedProfile("旧本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(item => item.Profile.Name == "旧本番DB"));
                    vm.ProfileName = "本番DB";

                    using (h.LockConnectionsFile())
                    {
                        h.Rename(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                    }

                    h.Store.Read()
                        .Profiles.Select(profile => profile.Name)
                        .Should()
                        .BeEquivalentTo(new[] { "本番DB", "旧本番DB" }, "どちらも消えていない");
                }
            ),
            Row(
                "ok-store-unavailable-still-confirms",
                "前回接続を記録できなくても OK の確定は続け、登録済みの内容も変えない",
                [(StoreLocked, Ok)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";

                    using (h.LockConnectionsFile())
                    {
                        h.Ok(vm);
                    }

                    h.Closes(vm).Should().Equal([true], "記録できないだけで確定は妨げない");
                    vm.Result.Should().NotBeNull();
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("本番DB");
                }
            ),
            Row(
                "save-write-fails-reports-and-reloads",
                "暗号ファイルを書けない保存は状態の文で知らせ、一覧をディスクから読み直す（JSON は書けている）",
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

                    h.Save(vm);

                    vm.StatusMessage.Should().StartWith(ConnectionDialogHarness.WriteFailedPrefix);
                    vm.Profiles.Should()
                        .ContainSingle("JSON は書けているので、ディスクの実体どおり一覧に出す")
                        .Which.Profile.Name.Should()
                        .Be("新しい接続");
                }
            ),
            Row(
                "delete-secret-delete-fails-reports-and-reloads",
                "暗号ファイルを消せない削除は状態の文で知らせ、JSON も書かず一覧を読み直す",
                [(WriteFails, Delete)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());

                    using (h.LockSecret(id))
                    {
                        h.Delete(vm);
                    }

                    vm.StatusMessage.Should().StartWith(ConnectionDialogHarness.WriteFailedPrefix);
                    vm.Profiles.Should()
                        .ContainSingle("暗号ファイルを消せなかったので JSON も書いていない")
                        .Which.Profile.Id.Should()
                        .Be(id);
                    h.Store.LoadPassword(id).Status.Should().Be(SavedPasswordStatus.Loaded);
                }
            ),
            Row(
                "rename-replaced-secret-delete-fails-reports-and-reloads",
                "名前の変更で置き換え先の暗号ファイルを消せなくても、状態の文で知らせて選択を保つ",
                [(WriteFails, Rename)],
                h =>
                {
                    var targetId = h.SeedProfile("本番DB");
                    var conflictId = h.SeedProfile(
                        "本番DB（東京）",
                        "tokyo.example.com",
                        "tokyo-secret"
                    );
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(item => item.Profile.Id == targetId));
                    vm.ProfileName = "本番DB（東京）";

                    using (h.LockSecret(conflictId))
                    {
                        h.Rename(vm);
                    }

                    vm.StatusMessage.Should().StartWith(ConnectionDialogHarness.WriteFailedPrefix);
                    vm.Profiles.Select(item => item.Profile.Id)
                        .Should()
                        .BeEquivalentTo(
                            [targetId, conflictId],
                            "置き換え先の暗号ファイルを消せなかったので JSON も書いていない"
                        );
                    vm.SelectedProfile!.Id.Should().Be(targetId);
                    vm.SelectedProfile.Name.Should().Be("本番DB");
                }
            ),
            Row(
                "ok-last-used-secret-write-fails-confirms-and-shows-error",
                "前回接続のパスワードを書けなくても確定は続け、記録できなかったことをエラーのモーダルで知らせる",
                [(WriteFails, Ok)],
                h =>
                {
                    h.BlockSecretsFolder();
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.SavePassword = true;
                    vm.Password = "secret";

                    h.Ok(vm);

                    h.Closes(vm).Should().Equal(true);
                    vm.Result.Should().NotBeNull();
                    h.Dialogs.ErrorMessages.Should()
                        .ContainSingle("記録できなかったことを黙って捨てない")
                        .Which.Should()
                        .StartWith(Strings.DbConnection_LastUsedSaveFailed.Split("{0}")[0]);
                }
            ),
            Row(
                "select-unreadable-password-reports-status",
                "保存済みのパスワードを読めないプロファイルを選ぶと、空欄にして読めなかったことを知らせる",
                [(Unreadable, Select)],
                h =>
                {
                    h.UseDpapi();
                    var id = h.SeedProfile("本番DB");
                    h.CorruptSecret(id);
                    var vm = h.Create();

                    h.Select(vm, vm.Profiles.Single());

                    vm.Password.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(
                            string.Format(
                                Strings.DbConnection_ProfileLoadedPasswordUnreadable,
                                "本番DB"
                            )
                        );
                }
            ),
            Row(
                "save-unreadable-password-empty-refused-keeps-secret",
                "読めないパスワードを空欄のまま「パスワードを保存」付きで上書きする保存は断り、暗号ファイルも内容も変えない",
                [(Unreadable, Save)],
                h =>
                {
                    h.UseDpapi();
                    var id = h.SeedProfile("本番DB");
                    var garbage = h.CorruptSecret(id);
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Host = "changed.example.com";

                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(Strings.DbConnection_SaveRefusedPasswordUnreadable);
                    File.ReadAllBytes(h.SecretFile(id)).Should().Equal(garbage);
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Server.Should().Be("prod.example.com", "断ったので何も書いていない");
                    stored.SavePassword.Should().BeTrue();
                }
            ),
            Row(
                "save-unreadable-password-reentered-saves",
                "読めないパスワードでも、入力し直せば保存できる",
                [(Unreadable, Save)],
                h =>
                {
                    h.UseDpapi();
                    var id = h.SeedProfile("本番DB");
                    h.CorruptSecret(id);
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Password = "new-secret";

                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "本番DB"));
                    h.Store.LoadPassword(id)
                        .Should()
                        .Be(new SavedPassword(SavedPasswordStatus.Loaded, "new-secret"));
                }
            ),
            Row(
                "rename-unreadable-password-keeps-secret",
                "名前の変更は、読めないパスワードの暗号ファイルを読みも書きもせず、保存の設定も変えない",
                [(Unreadable, Rename)],
                h =>
                {
                    h.UseDpapi();
                    var id = h.SeedProfile("本番DB");
                    var garbage = h.CorruptSecret(id);
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.ProfileName = "本番DB（新）";

                    h.Rename(vm);

                    File.ReadAllBytes(h.SecretFile(id)).Should().Equal(garbage);
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Name.Should().Be("本番DB（新）");
                    stored.SavePassword.Should().BeTrue("パスワードを保存する設定も変えない");
                }
            ),
            Row(
                "open-unreadable-last-connection-password-reports-status",
                "前回接続のパスワードを読めないときは、復元したうえで読めなかったことを知らせる",
                [(Unreadable, Open)],
                h =>
                {
                    h.UseDpapi();
                    h.Store.SaveLastUsed(
                        new SqlConnectionProfile
                        {
                            Server = "srv",
                            Database = "db",
                            SavePassword = true,
                        },
                        "secret"
                    );
                    File.WriteAllBytes(
                        Path.Combine(h.Store.SecretsFolder, "last-connection.dat"),
                        new byte[] { 9, 9, 9 }
                    );

                    var vm = h.Create();

                    vm.Host.Should().Be("srv");
                    vm.Password.Should().BeEmpty();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_RestoredPasswordUnreadable);
                }
            ),
        ];
}
