using AwesomeAssertions;
using QuickER.Db.UI.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>プロファイルの保存（上書きの確認）・名前の変更・削除</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> ProfileRows() =>
        [
            Row(
                "save-overwriting-unselected-profile-asks-and-declined-keeps",
                "選択していない同名プロファイルの上書きは警告つきで確認し、断ると接続先もパスワードも変えない",
                [(Profiles, Save)],
                h =>
                {
                    var existingId = h.SeedProfile("本番DB");
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    vm.Host = "test01";
                    vm.Password = "dev-secret";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(existingId);
                    stored.Server.Should().Be("prod.example.com");
                    h.Store.LoadPassword(existingId).Value.Should().Be("prod-secret");
                }
            ),
            Row(
                "save-confirmed-overwrite-replaces-existing",
                "確認で続行すれば同名プロファイルを同じ Id のまま上書きする",
                [(Profiles, Save)],
                h =>
                {
                    var existingId = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(existingId);
                    stored.Server.Should().Be("test01");
                }
            ),
            Row(
                "save-overwriting-selected-profile-does-not-ask",
                "選択中のプロファイル自身の上書きは確認しない",
                [(Selected, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Host = "prod2.example.com";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Server.Should()
                        .Be("prod2.example.com");
                }
            ),
            Row(
                "save-new-name-creates-another-without-asking",
                "選択中のプロファイルを読み込んで別名で保存すると、確認なしで別のプロファイルになる",
                [(Selected, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Host = "staging01";
                    vm.ProfileName = "検証DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    h.Store.Read()
                        .Profiles.Select(profile => (profile.Name, profile.Server))
                        .Should()
                        .BeEquivalentTo([("本番DB", "prod.example.com"), ("検証DB", "staging01")]);
                }
            ),
            Row(
                "save-onto-another-profile-name-asks",
                "選択中と違うプロファイルの名前を打って保存すると確認し、断ると相手を変えない",
                [(Selected, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var stagingId = h.SeedProfile(
                        "検証DB",
                        "staging.example.com",
                        "staging-secret"
                    );
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(p => p.Profile.Name == "本番DB"));
                    vm.Host = "staging01";
                    vm.ProfileName = "検証DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "検証DB"));
                    h.Store.Read()
                        .Profiles.Single(profile => profile.Id == stagingId)
                        .Server.Should()
                        .Be("staging.example.com");
                    h.Store.LoadPassword(stagingId).Value.Should().Be("staging-secret");
                }
            ),
            Row(
                "save-name-with-surrounding-spaces-matches-existing",
                "前後に空白を付けた保存名も既存の同名プロファイルとして確認・上書きし、保存名の欄も整える",
                [(Profiles, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.ProfileName = "  本番DB  ";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle("選択していない同名プロファイルの上書き確認");
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Server.Should()
                        .Be("srv");
                    vm.ProfileName.Should().Be("本番DB");
                }
            ),
            Row(
                "save-keeps-typed-password-when-not-saved",
                "パスワードを保存しない設定でも、保存の直後にパスワード欄の入力は消えない",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.SavePassword = false;
                    vm.Password = "typed";
                    vm.ProfileName = "開発";

                    h.Save(vm);

                    vm.SelectedProfile!.Name.Should().Be("開発");
                    vm.Password.Should().Be("typed");
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "開発"));
                }
            ),
            Row(
                "save-command-timeout-round-trips-into-settings",
                "コマンドタイムアウトはプロファイルへ保存され、選び直して確定すると接続設定へ流れる",
                [(Fresh, Save), (Profiles, Select), (Selected, Ok)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "server";
                    vm.Database = "db";
                    vm.CommandTimeout = "180";
                    vm.ProfileName = "長時間取込";
                    h.Save(vm);

                    var reopened = h.Create();
                    h.Select(
                        reopened,
                        reopened.Profiles.Single(p => p.Profile.Name == "長時間取込")
                    );
                    h.Ok(reopened);

                    reopened.CommandTimeout.Should().Be("180");
                    reopened.Result.Should().NotBeNull();
                    reopened.Result!.CommandTimeoutSeconds.Should().Be(180);
                }
            ),
            InvalidNumericSaveRow("save-invalid-port-rejected", "1543z", "60", portIsInvalid: true),
            InvalidNumericSaveRow("save-invalid-timeout-rejected", "", "abc", portIsInvalid: false),
            Row(
                "save-profile-name-required",
                "保存名が空欄なら保存せずに保存名を求める",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.ProfileName = "   ";

                    h.Save(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileNameRequired);
                    h.Store.Read().Profiles.Should().BeEmpty();
                }
            ),
            Row(
                "rename-changes-name-in-place",
                "名前の変更は複製を作らず名前だけを変え、接続先と保存済みのパスワードを引き継ぐ",
                [(Selected, Rename)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.ProfileName = "本番DB（東京）";

                    h.Rename(vm);

                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(id);
                    stored.Name.Should().Be("本番DB（東京）");
                    stored.Server.Should().Be("prod.example.com");
                    h.Store.LoadPassword(id).Value.Should().Be("prod-secret");
                }
            ),
            Row(
                "rename-does-not-save-edited-fields",
                "名前の変更は入力欄の編集内容を保存しない",
                [(Selected, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Host = "typo-host";
                    vm.ProfileName = "本番DB2";

                    h.Rename(vm);

                    h.Store.Read().Profiles.Single().Server.Should().Be("prod.example.com");
                }
            ),
            Row(
                "rename-keeps-unsaved-edits",
                "名前の変更の直後も入力中の編集は保存値へ戻らない",
                [(Selected, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.Host = "edited.example.com";
                    vm.ProfileName = "本番DB（新）";

                    h.Rename(vm);

                    vm.Host.Should().Be("edited.example.com");
                    vm.Password.Should().Be("prod-secret");
                    vm.SelectedProfile!.Name.Should().Be("本番DB（新）");
                    h.Store.Read()
                        .Profiles.Single()
                        .Server.Should()
                        .Be("prod.example.com", "変えたのは名前だけ");
                }
            ),
            Row(
                "rename-requires-selection-and-different-name",
                "未選択・同名・空欄では名前の変更を実行できず、選んで名前を変えると実行できる",
                [(Profiles, Rename), (Selected, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();

                    vm.ProfileName = "別名";
                    vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();

                    h.Select(vm, vm.Profiles.Single());
                    vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();

                    vm.ProfileName = "本番DB（東京）";
                    vm.RenameProfileCommand.CanExecute(null).Should().BeTrue();

                    vm.ProfileName = "   ";
                    vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "rename-collision-asks-then-replaces",
                "名前の変更が別のプロファイルと衝突すると警告つきで確認し、続行するとそちらを置き換える",
                [(Selected, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var stagingId = h.SeedProfile(
                        "検証DB",
                        "staging.example.com",
                        "staging-secret"
                    );
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(p => p.Profile.Name == "検証DB"));
                    vm.ProfileName = "本番DB";

                    h.Rename(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(stagingId);
                    stored.Name.Should().Be("本番DB");
                    stored.Server.Should().Be("staging.example.com");
                }
            ),
            Row(
                "rename-collision-declined-changes-nothing",
                "名前の変更の衝突の確認を断ると何も変わらない",
                [(Selected, Rename)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    h.SeedProfile("検証DB", "staging.example.com", "staging-secret");
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single(p => p.Profile.Name == "検証DB"));
                    vm.ProfileName = "本番DB";

                    h.Rename(vm);

                    h.Store.Read()
                        .Profiles.Select(profile => profile.Name)
                        .Should()
                        .BeEquivalentTo(["本番DB", "検証DB"]);
                }
            ),
            Row(
                "rename-replacing-removes-its-secret-keeps-own",
                "置き換えを伴う名前の変更は、置き換えた側を暗号ファイルごと消し、対象のパスワードを残し、一覧を読み直す",
                [(Selected, Rename)],
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

                    h.Rename(vm);

                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(targetId);
                    stored.Name.Should().Be("本番DB（東京）");
                    h.Store.LoadPassword(targetId)
                        .Value.Should()
                        .Be("prod-secret", "改名で自分のパスワードは失わない");
                    h.Store.LoadPassword(conflictId)
                        .Value.Should()
                        .BeEmpty("置き換えた側の暗号ファイルは残さない");
                    vm.Profiles.Should().ContainSingle("一覧も再読込する");
                }
            ),
            Row(
                "rename-profile-deleted-elsewhere-reports-not-found",
                "選んだプロファイルをほかのプログラムが消していたら、名前の変更は一覧を読み直し選択を外して知らせる",
                [(Selected, Rename)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    vm.ProfileName = "本番DB（新）";
                    h.Store.Delete(id);

                    h.Rename(vm);

                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfile.Should().BeNull();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileNotFound, "本番DB"));
                }
            ),
            Row(
                "delete-confirm-declined-keeps-profile",
                "削除は警告つきで確認し、断るとプロファイルは残る",
                [(Selected, Delete)],
                h =>
                {
                    h.Store.Upsert(
                        new QuickER.Db.UI.SqlConnectionProfile
                        {
                            Name = "TestDB",
                            Server = "s",
                            Database = "d",
                        },
                        password: ""
                    );
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles[0]);

                    h.Delete(vm);

                    vm.Profiles.Should().ContainSingle();
                    h.Dialogs.ConfirmMessages.Should()
                        .BeEmpty("取り消せない削除は警告つきで確認する");
                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain("TestDB");
                }
            ),
            Row(
                "delete-confirm-accepted-deletes-profile",
                "削除の確認で続行するとプロファイルが消え、状態の文に出る",
                [(Selected, Delete)],
                h =>
                {
                    h.Store.Upsert(
                        new QuickER.Db.UI.SqlConnectionProfile
                        {
                            Name = "TestDB",
                            Server = "s",
                            Database = "d",
                        },
                        password: ""
                    );
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles[0]);

                    h.Delete(vm);

                    vm.Profiles.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileDeleted, "TestDB"));
                }
            ),
            Row(
                "delete-without-selection-asks-to-select",
                "未選択の削除は確認を出さずに、削除するプロファイルを選ぶよう求める",
                [(Profiles, Delete)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();

                    h.Delete(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_SelectProfileToDelete);
                    h.Store.Read().Profiles.Should().ContainSingle();
                }
            ),
        ];

    /// <summary>ポート・タイムアウトの不正値を保存しない行</summary>
    private static ScenarioRow<ConnectionDialogCase> InvalidNumericSaveRow(
        string id,
        string port,
        string timeout,
        bool portIsInvalid
    ) =>
        Row(
            id,
            $"保存はポート「{port}」・タイムアウト「{timeout}」の不正値で止め、ストアへ書かない",
            [(Fresh, Save)],
            h =>
            {
                var vm = h.Create();
                vm.Host = "srv";
                vm.Database = "db";
                vm.Port = port;
                vm.CommandTimeout = timeout;
                vm.ProfileName = "開発";

                h.Save(vm);

                vm.StatusMessage.Should()
                    .Be(
                        portIsInvalid
                            ? Strings.DbConnection_PortInvalid
                            : Strings.DbConnection_CommandTimeoutInvalid
                    );
                h.Store.Read().Profiles.Should().BeEmpty();
            }
        );
}
