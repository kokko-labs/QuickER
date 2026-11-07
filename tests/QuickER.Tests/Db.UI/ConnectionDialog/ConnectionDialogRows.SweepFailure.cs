using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>暗号ファイルのフォルダを塞ぐ前に、パスワードを保存しないプロファイルと前回接続を入れる</summary>
    private static void SeedWithoutSecrets(ConnectionDialogHarness h)
    {
        var profile = new SqlConnectionProfile
        {
            Name = "本番DB",
            Server = "prod.example.com",
            Database = "SalesDb",
            SavePassword = false,
        };
        h.Store.Upsert(profile, string.Empty);
        h.Store.SaveLastUsed(
            new SqlConnectionProfile { Server = "last.example.com", Database = "LastDb" },
            string.Empty
        );
    }

    /// <summary>DPAPI で保存し、復号できない暗号ファイルのプロファイルを選んだ VM を作る</summary>
    private static DbConnectionDialogViewModel CreateUnreadableSelected(
        ConnectionDialogHarness h,
        out Guid id,
        out byte[] garbage
    )
    {
        h.UseDpapi();
        id = h.SeedProfile("本番DB");
        garbage = h.CorruptSecret(id);
        var vm = h.Create(ConnectionDialects.WithSqlite, allowSqliteFileCreation: true);
        h.Select(vm, ItemOf(vm, id));
        return vm;
    }

    /// <summary>ストアの失敗・接続テストの実行中の手</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> SweepFailureRows() =>
        [
            Row(
                "select-store-locked-applies-profile",
                "connections.json を掴まれた間でも、選んだプロファイルは欄へ当たり（暗号ファイルは別のファイル）、エラーのモーダルは出ない",
                [(StoreLocked, Select)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:526-577（選択はストアの JSON を読まず暗号ファイルだけを読む）
                    using (h.LockConnectionsFile())
                    {
                        h.Select(vm, ItemOf(vm, id));

                        vm.Host.Should().Be("prod.example.com");
                        vm.Password.Should().Be("prod-secret");
                        vm.StatusMessage.Should()
                            .Be(string.Format(Strings.DbConnection_ProfileLoaded, "本番DB"));
                    }

                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "edit-store-locked-touches-nothing-and-shows-no-store-notice",
                "特性の固定（文書に定めが無い今の振る舞い）：connections.json を掴まれた間の入力は例外もモーダルも出さず、ストアの文も出さず、解放後のバイト列も変わらない",
                [(StoreLocked, Edit)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:309-324（入力の変更通知はストアに触れない）
                    using (h.LockConnectionsFile())
                    {
                        h.Edit(vm, v => v.Host = "edited.example.com");

                        vm.StatusMessage.Should()
                            .NotBe(Strings.DbConnection_ProfileStoreUnavailable);
                    }

                    h.Snapshot().Should().Equal(before);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "test-store-locked-succeeds",
                "connections.json を掴まれた間でも接続テストは成功を知らせる（接続テストはストアに触れない）",
                [(StoreLocked, Test)],
                async h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.SqlServer.Importer.Succeed(1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637
                    using (h.LockConnectionsFile())
                    {
                        await h.TestAsync(vm);
                    }

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 1));
                    h.SqlServer.Importer.Calls.Should().Be(1);
                }
            ),
            Row(
                "browse-store-locked-sets-file-path-without-store-notice",
                "connections.json を掴まれた間でも参照で選んだパスが入り、ストアの文は出ない",
                [(StoreLocked, Browse)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.CreateSqlite();
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:871-883
                    using (h.LockConnectionsFile())
                    {
                        h.BrowseFile(vm);

                        vm.FilePath.Should().Be(picked);
                        vm.StatusMessage.Should()
                            .NotBe(Strings.DbConnection_ProfileStoreUnavailable);
                    }
                }
            ),
            Row(
                "cancel-store-locked-closes-without-modal",
                "connections.json を掴まれた間でも取り消しは取り消しとして閉じ、モーダルは出ない",
                [(StoreLocked, Cancel)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.Create();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008
                    using (h.LockConnectionsFile())
                    {
                        h.Cancel(vm);
                    }

                    h.Closes(vm).Should().Equal(false);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "open-store-locked-shows-empty-list-and-reports-unavailable",
                "connections.json を掴まれたまま開くと、取込・同期のどちらも一覧を空にし、前回接続を復元せず、読めなかったことをステータス行で知らせる（モーダルなし・何も書かない）",
                [(StoreLocked, Open)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    SaveLastUsedSqlServer(h);
                    var before = h.Snapshot();
                    DbConnectionDialogViewModel importVm;
                    DbConnectionDialogViewModel syncVm;

                    // 根拠：src/QuickER.Db.UI/CLAUDE.md「ストアの失敗は…ステータス行で知らせる」・SqlConnectionProfileStore.Read（分類 Unavailable）
                    using (h.LockConnectionsFile())
                    {
                        importVm = h.Create();
                        syncVm = h.Create(
                            ConnectionDialects.WithSqlite,
                            DbConnectionDialogMode.Sync,
                            h.SqlServer
                        );
                    }

                    foreach (var vm in new[] { importVm, syncVm })
                    {
                        vm.AreProfilesUnavailable.Should().BeTrue();
                        vm.Profiles.Should().BeEmpty();
                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfilesUnavailable);
                        vm.Host.Should().Be("localhost", "読めていない前回接続は復元しない");
                        vm.Database.Should().BeEmpty();
                    }

                    h.Snapshot().Should().Equal(before);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "save-after-store-locked-open-still-locked-writes-nothing",
                "connections.json を掴まれたまま開き、掴まれたまま保存名を付けて保存すると、読み直しても読めないので中止を知らせ、確認も出さず何も書かない",
                [(StoreLocked, Save)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var before = h.Snapshot();

                    // 根拠：SqlConnectionProfileStore.Upsert（読めなければ何も書かずに中止）・保存の前の読み直し
                    using (h.LockConnectionsFile())
                    {
                        var vm = h.Create();
                        vm.Host = "new-server";
                        vm.Database = "new-db";
                        vm.ProfileName = "本番DB";

                        h.Save(vm);

                        vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
                        vm.AreProfilesUnavailable.Should().BeTrue();
                        vm.Profiles.Should().BeEmpty();
                    }

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "save-after-store-locked-open-released-rereads-and-asks-overwrite",
                "connections.json を掴まれたまま開いた後に掴みが外れてから同じ名前で保存すると、一覧を読み直して上書きの確認を出し、断れば何も書かず、承諾なら同じプロファイルを上書きする（同じ名前を 2 つ作らない）",
                [(StoreLocked, Save)],
                h =>
                {
                    var prodId = h.SeedProfile("本番DB");
                    DbConnectionDialogViewModel vm;

                    using (h.LockConnectionsFile())
                    {
                        vm = h.Create();
                    }

                    vm.Host = "other.example.com";
                    vm.Database = "OtherDb";
                    vm.ProfileName = "本番DB";
                    h.Dialogs.ConfirmResult = false;

                    // 根拠：保存の前の読み直し・:730-741（選択中でない別のプロファイルへの上書きは警告つきで確認する）
                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should()
                        .Equal(
                            string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB")
                        );
                    vm.AreProfilesUnavailable.Should().BeFalse();
                    vm.Profiles.Select(item => item.Profile.Id).Should().Equal(prodId);
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Server.Should()
                        .Be("prod.example.com", "断ったので書かない");

                    h.Dialogs.ConfirmResult = true;
                    h.Save(vm);

                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(prodId);
                    stored.Server.Should().Be("other.example.com");
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "本番DB"));
                }
            ),
            Row(
                "save-store-locked-right-after-write-reports-unavailable-and-keeps-write",
                "選んだプロファイルを上書き保存した直後の読み直しで connections.json を掴まれると、保存した文で上書きせずに読めないことを知らせ、一覧と選択を空にする（ディスクは保存のとおり）",
                [(StoreLocked, Save)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    vm.Host = "edited.example.com";
                    h.LockConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.SaveProfile（保存の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Save(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_ProfilesUnavailable);
                    vm.AreProfilesUnavailable.Should().BeTrue();
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();

                    h.ReleaseReadLock();
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(id);
                    stored.Server.Should().Be("edited.example.com");
                }
            ),
            Row(
                "rename-store-locked-right-after-write-reports-unavailable-and-keeps-write",
                "名前の変更の直後の読み直しで connections.json を掴まれると、変更した文で上書きせずに読めないことを知らせ、一覧と選択を空にする（ディスクは変更のとおり）",
                [(StoreLocked, Rename)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    vm.ProfileName = "本番DB（新）";
                    h.LockConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.RenameProfile（変更の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Rename(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_ProfilesUnavailable);
                    vm.AreProfilesUnavailable.Should().BeTrue();
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();

                    h.ReleaseReadLock();
                    var stored = h.Store.Read().Profiles.Should().ContainSingle().Subject;
                    stored.Id.Should().Be(id);
                    stored.Name.Should().Be("本番DB（新）");
                }
            ),
            Row(
                "delete-store-locked-right-after-write-reports-unavailable-and-keeps-write",
                "削除の直後の読み直しで connections.json を掴まれると、削除した文で上書きせずに読めないことを知らせ、一覧と選択を空にする（ディスクは削除のとおり）",
                [(StoreLocked, Delete)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var stagingId = h.SeedProfile(
                        "検証DB",
                        "staging.example.com",
                        "staging-secret"
                    );
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    h.LockConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.DeleteProfile（削除の後の読み直しが読めなければ、そこで戻る）・ReloadProfiles
                    h.Delete(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_ProfilesUnavailable);
                    vm.AreProfilesUnavailable.Should().BeTrue();
                    vm.Profiles.Should().BeEmpty();
                    vm.SelectedProfileItem.Should().BeNull();

                    h.ReleaseReadLock();
                    h.Store.Read().Profiles.Select(profile => profile.Id).Should().Equal(stagingId);
                }
            ),
            Row(
                "save-write-fails-then-store-locked-at-reload-reports-unavailable",
                "暗号ファイルを書けない保存の後の読み直しで connections.json も掴まれると、「一覧を読み直した」とは言わずに読めないことを知らせ、一覧を空にする（JSON は書けている）",
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
                    h.LockConnectionsFileOnNextRead();

                    // 根拠：DbConnectionDialogViewModel.ReportStoreWriteFailure（読み直しが読めなければ、そこで戻る）
                    h.Save(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_ProfilesUnavailable);
                    vm.AreProfilesUnavailable.Should().BeTrue();
                    vm.Profiles.Should().BeEmpty();

                    h.ReleaseReadLock();
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle("JSON は書けている")
                        .Which.Name.Should()
                        .Be("新しい接続");
                }
            ),
            Row(
                "open-write-fails-restores-last-connection-without-modal",
                "暗号ファイルを書けない状態でも、開くと一覧と前回接続を復元し、モーダルは出ない（開くのは書かない）",
                [(WriteFails, Open)],
                h =>
                {
                    SeedWithoutSecrets(h);
                    h.BlockSecretsFolder();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:293-295・:345-374（開くのは読むだけ。パスワードを保存しない前回接続は暗号ファイルを読まない）
                    var vm = h.Create();

                    vm.Profiles.Should().ContainSingle();
                    vm.Host.Should().Be("last.example.com");
                    vm.StatusMessage.Should().Be(Strings.DbConnection_Restored);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "select-write-fails-locked-secret-reports-password-unreadable",
                "暗号ファイルを掴まれて読めないプロファイルを選ぶと、パスワードを空にして読めなかったことを知らせ、例外は出ない",
                [(WriteFails, Select)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:560-575・LoadSecret の XmlDoc（一時的なロックも Unreadable）
                    using (h.LockSecret(id))
                    {
                        h.Select(vm, ItemOf(vm, id));
                    }

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
                "edit-write-fails-keeps-status-and-shows-no-modal",
                "特性の固定（文書に定めが無い今の振る舞い）：暗号ファイルを書けない状態での入力は、状態の文もモーダルも変えない",
                [(WriteFails, Edit)],
                h =>
                {
                    SeedWithoutSecrets(h);
                    h.BlockSecretsFolder();
                    var vm = h.Create();
                    var status = vm.StatusMessage;

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:309-324（入力の変更通知はストアに触れない）
                    h.Edit(vm, v => v.Host = "edited.example.com");

                    vm.StatusMessage.Should().Be(status);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "test-write-fails-succeeds-without-modal",
                "暗号ファイルを書けない状態でも接続テストは成功を知らせ、モーダルは出ない",
                [(WriteFails, Test)],
                async h =>
                {
                    h.BlockSecretsFolder();
                    var vm = h.Create();
                    h.SqlServer.Importer.Succeed(1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637（接続テストは暗号ファイルを書かない）
                    await h.TestAsync(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 1));
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "browse-write-fails-sets-file-path",
                "暗号ファイルを書けない状態でも参照で選んだパスが入る",
                [(WriteFails, Browse)],
                h =>
                {
                    var vm = h.CreateSqlite();
                    h.BlockSecretsFolder();
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:871-883
                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked);
                }
            ),
            Row(
                "cancel-write-fails-closes-without-modal",
                "暗号ファイルを書けない状態でも取り消しは取り消しとして閉じ、モーダルは出ない",
                [(WriteFails, Cancel)],
                h =>
                {
                    h.BlockSecretsFolder();
                    var vm = h.Create();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008・組の不変条件 11
                    h.Cancel(vm);

                    h.Closes(vm).Should().Equal(false);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "edit-unreadable-then-save-is-refused-and-keeps-secret",
                "読めないパスワードのプロファイルで入力を変えても空欄のままで、続く保存は断られ、暗号ファイルは変わらない",
                [(Unreadable, Edit)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out var id, out var garbage);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:121-129・:676-688（読めなかった印は選び直し・保存し直しでだけ消える）
                    h.Edit(vm, v => v.Host = "changed.example.com");

                    vm.Password.Should().BeEmpty();
                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(Strings.DbConnection_SaveRefusedPasswordUnreadable);
                    File.ReadAllBytes(h.SecretFile(id)).Should().Equal(garbage);
                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Server.Should()
                        .Be("prod.example.com");
                }
            ),
            Row(
                "delete-unreadable-confirmed-removes-profile-secret-and-mark",
                "読めないパスワードのプロファイルを確認のうえ削除すると、プロファイルと暗号ファイルが消え、読めなかった印も消えて同名の保存が断られない",
                [(Unreadable, Delete)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out var id, out _);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:121・:541・:845-869（削除で選択が外れ印も外れる）
                    h.Delete(vm);

                    vm.Profiles.Should().BeEmpty();
                    File.Exists(h.SecretFile(id)).Should().BeFalse();

                    vm.ProfileName = "本番DB";
                    vm.SavePassword = true;
                    vm.Password = string.Empty;
                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "本番DB"));
                }
            ),
            Row(
                "delete-unreadable-declined-keeps-profile-and-mark",
                "確認を断ると読めないパスワードのプロファイルも暗号ファイルも印も残り、続く空欄の保存は断られる",
                [(Unreadable, Delete)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out var id, out var garbage);
                    h.Dialogs.ConfirmResult = false;

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:835-843（断ったら何もしない）
                    h.Delete(vm);

                    vm.Profiles.Should().ContainSingle();
                    File.ReadAllBytes(h.SecretFile(id)).Should().Equal(garbage);

                    h.Save(vm);

                    vm.StatusMessage.Should()
                        .Be(Strings.DbConnection_SaveRefusedPasswordUnreadable);
                }
            ),
            Row(
                "test-unreadable-succeeds-and-keeps-mark",
                "読めないパスワードのプロファイルの接続テストは成功を知らせ、パスワードは空のまま、読めなかった印も残る",
                [(Unreadable, Test)],
                async h =>
                {
                    var vm = CreateUnreadableSelected(h, out _, out _);
                    h.SqlServer.Importer.Succeed(1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637（接続テストは印に触れない）・:129
                    await h.TestAsync(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 1));
                    h.SqlServer.Importer.Calls.Should().Be(1);
                    vm.Password.Should().BeEmpty();
                    h.Save(vm);
                    vm.StatusMessage.Should()
                        .Be(Strings.DbConnection_SaveRefusedPasswordUnreadable);
                }
            ),
            Row(
                "browse-unreadable-sql-server-has-no-file-path",
                "読めないパスワードの SQL Server のプロファイルを選んだ状態では、参照の欄を出さず新規作成も実行できない",
                [(Unreadable, Browse)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out _, out _);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:248-255・:885-886
                    vm.ShowFilePath.Should().BeFalse();
                    vm.BrowseNewFileCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "ok-unreadable-confirms-with-empty-password-and-keeps-secret",
                "読めないパスワードのプロファイルで OK すると空のパスワードで確定し、プロファイルの暗号ファイルは変わらず、モーダルは出ない",
                [(Unreadable, Ok)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out var id, out var garbage);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:912-998・組の不変条件 7
                    h.Ok(vm);

                    h.Closes(vm).Should().Equal(true);
                    vm.Result!.Password.Should().BeEmpty();
                    vm.Result.Host.Should().Be("prod.example.com");
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                    File.ReadAllBytes(h.SecretFile(id)).Should().Equal(garbage);
                }
            ),
            Row(
                "cancel-unreadable-closes-and-keeps-secret-bytes",
                "読めないパスワードのプロファイルを選んだ状態で取り消すと、暗号ファイルを含むバイト列は変わらない",
                [(Unreadable, Cancel)],
                h =>
                {
                    var vm = CreateUnreadableSelected(h, out _, out _);
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008・組の不変条件 7
                    h.Cancel(vm);

                    h.Closes(vm).Should().Equal(false);
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "select-testing-applies-profile-and-test-keeps-start-settings",
                "特性の固定（文書に定めが無い今の振る舞い）：接続テストの実行中に別のプロファイルを選ぶと欄に当たり、実行中のまま OK は押せず、終わると状態の文が成功で上書きされ、テストは始めの接続設定のまま",
                [(Testing, Select)],
                async h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    var running = await h.BeginTest(vm, h.SqlServer);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:620（接続設定は始めに組む）・:624（終わりに状態の文を上書き）
                    h.Select(vm, ItemOf(vm, id));

                    vm.Host.Should().Be("prod.example.com");
                    vm.IsBusy.Should().BeTrue();
                    vm.OkCommand.CanExecute(null).Should().BeFalse();

                    await running.FinishAsync();

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 0));
                    h.SqlServer.LastSettings!.Host.Should().Be("localhost");
                }
            ),
            Row(
                "edit-testing-keeps-running-test-and-start-settings",
                "特性の固定（文書に定めが無い今の振る舞い）：接続テストの実行中に入力を変えても取込は 1 回のまま実行中で OK は押せず、流れた設定は始めの値",
                [(Testing, Edit)],
                async h =>
                {
                    var vm = h.Create();
                    var running = await h.BeginTest(vm, h.SqlServer);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:613-622（設定は始めに確定する）
                    h.Edit(vm, v => v.Host = "edited.example.com");

                    h.SqlServer.Importer.Calls.Should().Be(1);
                    vm.IsBusy.Should().BeTrue();
                    vm.OkCommand.CanExecute(null).Should().BeFalse();

                    await running.FinishAsync();

                    h.SqlServer.LastSettings!.Host.Should().Be("localhost");
                }
            ),
            Row(
                "save-testing-saves-and-test-result-overwrites-status",
                "特性の固定（文書に定めが無い今の振る舞い）：接続テストの実行中でも保存できて保存され、実行中のまま OK は押せず、終わると状態の文は成功になる",
                [(Testing, Save)],
                async h =>
                {
                    var vm = h.Create();
                    var running = await h.BeginTest(vm, h.SqlServer);
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.ProfileName = "保存中";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:650-720（保存に IsBusy の門は無い）
                    h.Save(vm);

                    h.Store.Read()
                        .Profiles.Should()
                        .ContainSingle()
                        .Which.Name.Should()
                        .Be("保存中");
                    vm.IsBusy.Should().BeTrue();
                    vm.OkCommand.CanExecute(null).Should().BeFalse();

                    await running.FinishAsync();

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 0));
                }
            ),
            Row(
                "rename-testing-renames-while-test-runs",
                "特性の固定（文書に定めが無い今の振る舞い）：接続テストの実行中でも名前を変えられ、実行中のまま OK は押せない",
                [(Testing, Rename)],
                async h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    var running = await h.BeginTest(vm, h.SqlServer);
                    vm.ProfileName = "新名";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:744-747・:763（名前の変更に IsBusy の門は無い）
                    h.Rename(vm);

                    h.Store.Read().Profiles.Should().ContainSingle().Which.Name.Should().Be("新名");
                    vm.IsBusy.Should().BeTrue();
                    vm.OkCommand.CanExecute(null).Should().BeFalse();

                    await running.FinishAsync();
                }
            ),
            Row(
                "delete-testing-deletes-while-test-runs",
                "特性の固定（文書に定めが無い今の振る舞い）：接続テストの実行中でも確認のうえ削除でき、テストは続く",
                [(Testing, Delete)],
                async h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create();
                    h.Select(vm, ItemOf(vm, id));
                    var running = await h.BeginTest(vm, h.SqlServer);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:827-869（削除に IsBusy の門は無い）
                    h.Delete(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    h.Store.Read().Profiles.Should().BeEmpty();
                    vm.IsBusy.Should().BeTrue();

                    await running.FinishAsync();

                    vm.IsBusy.Should().BeFalse();
                }
            ),
            Row(
                "test-testing-cannot-execute-a-second-test",
                "接続テストの実行中は、接続テストのコマンドを実行できない（CommunityToolkit.Mvvm の AsyncRelayCommand は既定で並行実行を許さない）",
                [(Testing, Test)],
                async h =>
                {
                    var vm = h.Create();
                    var running = await h.BeginTest(vm, h.SqlServer);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:603-604（コマンドに CanExecute の指定は無く、ライブラリの既定に依る）
                    vm.TestConnectionCommand.CanExecute(null).Should().BeFalse();
                    h.SqlServer.Importer.Calls.Should().Be(1);

                    await running.FinishAsync();
                }
            ),
            Row(
                "browse-testing-sets-file-path-and-test-keeps-start-path",
                "特性の固定（文書に定めが無い今の振る舞い）：SQLite で接続テストの実行中に参照で選ぶとパスが入り、実行中のまま OK は押せず、テストの設定は始めのパスのまま",
                [(Testing, Browse)],
                async h =>
                {
                    var vm = h.CreateSqlite();
                    var running = await h.BeginTest(vm, h.Sqlite);
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:873-883（参照に IsBusy の門は無い）・:620
                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked);
                    vm.IsBusy.Should().BeTrue();
                    vm.OkCommand.CanExecute(null).Should().BeFalse();

                    await running.FinishAsync();

                    h.Sqlite.LastSettings!.FilePath.Should().BeEmpty();
                }
            ),
        ];
}
