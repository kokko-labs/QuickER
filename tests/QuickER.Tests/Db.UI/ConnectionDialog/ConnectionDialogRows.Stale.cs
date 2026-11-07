using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>
    /// 開いた後にほかのプロセスが connections.json を書き換えた（一覧が古い）ときの保存・名前の変更：
    /// ストアが同じ（DBMS, 名前）を断り、VM は一覧を読み直して警告つきの上書きの確認を出す（複製を作らない）
    /// </summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> StaleRows() =>
        [
            Row(
                "save-name-added-by-other-process-after-open-asks-and-declined-keeps",
                "開いた後にほかのプロセスが同じ名前で保存したプロファイルへ保存すると、一覧を読み直して警告つきで確認し、断ると何も書かない",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    var otherId = SaveFromOtherProcess(h, "本番DB", "prod.example.com");
                    var before = h.Snapshot();
                    h.Dialogs.ConfirmResult = false;
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));
                    h.Snapshot().Should().Equal(before);
                    vm.Profiles.Should().ContainSingle().Which.Profile.Id.Should().Be(otherId);
                }
            ),
            Row(
                "save-name-added-by-other-process-after-open-confirmed-replaces-it",
                "開いた後にほかのプロセスが同じ名前で保存したプロファイルへ保存し、確認で続行すると、それを置き換えて 1 つにする",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    var otherId = SaveFromOtherProcess(h, "本番DB", "prod.example.com");
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Server.Should().Be("test01");
                    File.Exists(h.SecretFile(otherId)).Should().BeFalse();
                    vm.SelectedProfile!.Id.Should().Be(stored.Id);
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "本番DB"));
                }
            ),
            Row(
                "save-selected-profile-whose-name-was-taken-by-other-process-asks-overwrite",
                "選んだプロファイルの名前をほかのプロセスが変えて同じ名前を足した後に保存すると、警告つきで確認し、続行で 1 つにする",
                [(Selected, Save)],
                h =>
                {
                    var selectedId = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    var other = h.OtherProcessStore;
                    other.Rename(selectedId, "旧本番DB");
                    var otherId = SaveFromOtherProcess(h, "本番DB", "new.example.com");
                    vm.Host = "prod2.example.com";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(selectedId);
                    stored.Name.Should().Be("本番DB");
                    stored.Server.Should().Be("prod2.example.com");
                    File.Exists(h.SecretFile(otherId)).Should().BeFalse();
                }
            ),
            Row(
                "rename-to-name-added-by-other-process-asks-overwrite",
                "開いた後にほかのプロセスが足した名前へ名前を変えると、警告つきで確認し、続行でそのプロファイルを置き換える",
                [(Selected, Rename)],
                h =>
                {
                    var selectedId = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, vm.Profiles.Single());
                    var otherId = SaveFromOtherProcess(h, "検証DB", "staging.example.com");
                    vm.ProfileName = "検証DB";

                    h.Rename(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .ContainSingle()
                        .Which.Should()
                        .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "検証DB"));
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(selectedId);
                    stored.Name.Should().Be("検証DB");
                    stored.Server.Should().Be("prod.example.com");
                    File.Exists(h.SecretFile(otherId)).Should().BeFalse();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileRenamed, "本番DB", "検証DB"));
                }
            ),
            Row(
                "save-overwrite-confirm-while-other-process-replaces-target-asks-again",
                "一覧にある同名への上書きの確認の間に、ほかのプロセスがそれを消して同じ名前で保存すると、もう一度確認し、続行で 1 つにする",
                [(Profiles, Save)],
                h =>
                {
                    var listedId = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    Guid? otherId = null;
                    h.Dialogs.OnConfirmWarning = () =>
                    {
                        if (otherId is not null)
                        {
                            return;
                        }

                        h.OtherProcessStore.Delete(listedId);
                        otherId = SaveFromOtherProcess(h, "本番DB", "other.example.com");
                    };
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .Equal(
                            Enumerable.Repeat(
                                string.Format(
                                    Strings.DbConnection_OverwriteProfileConfirm,
                                    "本番DB"
                                ),
                                2
                            )
                        );
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Server.Should().Be("test01");
                    File.Exists(h.SecretFile(otherId!.Value)).Should().BeFalse();
                }
            ),
            Row(
                "save-conflict-again-after-confirm-reports-and-writes-nothing",
                "衝突の後の確認の間に、ほかのプロセスがさらに別の同名へ差し替えると、確認を繰り返さずに何も書かずに止めて知らせる",
                [(Fresh, Save)],
                h =>
                {
                    var vm = h.Create();
                    var firstId = SaveFromOtherProcess(h, "本番DB", "prod.example.com");
                    Guid? secondId = null;
                    h.Dialogs.OnConfirmWarning = () =>
                    {
                        if (secondId is not null)
                        {
                            return;
                        }

                        h.OtherProcessStore.Delete(firstId);
                        secondId = SaveFromOtherProcess(h, "本番DB", "other.example.com");
                    };
                    vm.Host = "test01";
                    vm.ProfileName = "本番DB";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(secondId!.Value);
                    stored.Server.Should().Be("other.example.com");
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileNameConflict, "本番DB"));
                }
            ),
        ];

    /// <summary>ほかのプロセスが SQL Server のプロファイルを 1 件保存する（パスワードを保存する）</summary>
    private static Guid SaveFromOtherProcess(ConnectionDialogHarness h, string name, string server)
    {
        var profile = new SqlConnectionProfile
        {
            Name = name,
            Dbms = SqlServerProvider.ProviderName,
            Server = server,
            Database = "SalesDb",
            SavePassword = true,
        };
        h.OtherProcessStore.Upsert(profile, "other-secret");
        return profile.Id;
    }
}
