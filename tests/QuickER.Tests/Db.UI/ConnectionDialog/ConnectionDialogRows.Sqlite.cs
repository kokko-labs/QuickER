using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>SQLite（ファイル型）の表示・参照・新規作成・確定・保存</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> SqliteRows() =>
        [
            Row(
                "edit-sqlite-shows-file-path-hides-server-fields",
                "SQLite を選ぶとファイルパスの欄だけを出し、サーバー系の欄を隠す",
                [(Sqlite, Edit)],
                h =>
                {
                    var vm = h.CreateSqlite();

                    vm.ShowFilePath.Should().BeTrue();
                    vm.ShowServerFields.Should().BeFalse();
                    vm.ShowUserId.Should().BeFalse();
                    vm.ShowPassword.Should().BeFalse();
                    vm.ShowAuthMode.Should().BeFalse();
                    vm.ShowTrustServerCertificate.Should().BeFalse();
                }
            ),
            Row(
                "ok-sqlite-empty-file-path-rejected",
                "SQLite でファイルパスが空なら OK を拒否する",
                [(Sqlite, Ok)],
                h =>
                {
                    var vm = h.CreateSqlite();
                    vm.FilePath = string.Empty;

                    h.Ok(vm);

                    vm.Result.Should().BeNull();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_FilePathRequired);
                }
            ),
            Row(
                "ok-sqlite-missing-file-rejected",
                "SQLite で存在しないファイルパスなら OK を拒否する（取込・新規作成なし）",
                [(Sqlite, Ok)],
                h =>
                {
                    var vm = h.CreateSqlite();
                    vm.FilePath = h.PathOf("does-not-exist.db");

                    h.Ok(vm);

                    vm.Result.Should().BeNull();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
                }
            ),
            Row(
                "ok-sqlite-existing-file-confirms-and-keeps-path",
                "SQLite で実在するファイルパスなら確定し、結果にファイルパスと方言を持つ",
                [(Sqlite, Ok)],
                h =>
                {
                    var dbPath = h.PathOf("sample.db");
                    File.WriteAllText(dbPath, string.Empty);
                    var vm = h.CreateSqlite();
                    vm.FilePath = dbPath;

                    h.Ok(vm);

                    vm.Result.Should().NotBeNull();
                    vm.Result!.FilePath.Should().Be(dbPath);
                    vm.ResultProvider!.Name.Should().Be(SqliteProvider.ProviderName);
                }
            ),
            Row(
                "browse-sqlite-file-sets-file-path",
                "参照で選んだパスがファイルパスの欄に入る",
                [(Sqlite, Browse)],
                h =>
                {
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);
                    var vm = h.CreateSqlite();

                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked);
                }
            ),
            Row(
                "save-sqlite-profile-round-trips-file-path",
                "SQLite のプロファイルを保存して開き直し、選ぶとファイルパスが戻る",
                [(Sqlite, Save), (Profiles, Select)],
                h =>
                {
                    var dbPath = h.PathOf("roundtrip.db");
                    var vm = h.CreateSqlite();
                    vm.FilePath = dbPath;
                    vm.ProfileName = "SQLite接続";

                    h.Save(vm);

                    var reopened = h.CreateSqlite();
                    h.Select(
                        reopened,
                        reopened.Profiles.Single(p =>
                            p.Profile.Dbms == SqliteProvider.ProviderName
                            && p.Profile.Name == "SQLite接続"
                        )
                    );

                    reopened.FilePath.Should().Be(dbPath);
                }
            ),
            Row(
                "save-same-name-different-dbms-does-not-ask",
                "同名でも DBMS が違えば別のプロファイルなので確認せずに足す",
                [(Sqlite, Save)],
                h =>
                {
                    h.SeedProfile("共通名");
                    h.Dialogs.ConfirmResult = false;
                    var vm = h.CreateSqlite();
                    vm.FilePath = h.PathOf("local.db");
                    vm.ProfileName = "共通名";

                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    h.Store.Read().Profiles.Should().HaveCount(2);
                }
            ),
            Row(
                "browse-new-file-disallowed-hidden-and-disabled",
                "新規作成を許可しない既定では、新規作成のボタンを出さず実行もできない",
                [(Sqlite, Browse)],
                h =>
                {
                    var vm = h.CreateSqlite(allowFileCreation: false);

                    vm.ShowCreateNewFile.Should().BeFalse();
                    vm.BrowseNewFileCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "browse-new-file-allowed-shown-and-enabled",
                "新規作成を許可して SQLite を選んでいれば、新規作成のボタンを出し実行できる",
                [(Sqlite, Browse), (Sync, Browse)],
                h =>
                {
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );

                    vm.ShowCreateNewFile.Should().BeTrue();
                    vm.BrowseNewFileCommand.CanExecute(null).Should().BeTrue();
                }
            ),
            Row(
                "ok-sqlite-creation-allowed-hand-typed-missing-path-rejected",
                "新規作成を許可していても、手入力の存在しないパスは拒否し、ファイルも作らない",
                [(Sqlite, Ok), (Sync, Ok)],
                h =>
                {
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );
                    vm.FilePath = h.PathOf("typo-does-not-exist.db");

                    h.Ok(vm);

                    vm.Result.Should().BeNull();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
                    File.Exists(vm.FilePath).Should().BeFalse();
                }
            ),
            Row(
                "ok-sqlite-new-file-created-on-ok",
                "新規作成で選んだパスは存在の検査を免れ、OK の確定で空の DB が作られる（その前は無い）",
                [(Sqlite, Browse), (Sqlite, Ok), (Sync, Browse), (Sync, Ok)],
                h =>
                {
                    var newPath = h.PathOf("brand-new.db");
                    h.Files.SaveResult = new FileDialogResult(newPath, 1);
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );

                    h.BrowseNewFile(vm);
                    vm.FilePath.Should().Be(newPath);
                    File.Exists(newPath).Should().BeFalse("OK 確定前はまだ作成されない");

                    h.Ok(vm);

                    vm.Result.Should().NotBeNull();
                    vm.Result!.FilePath.Should().Be(newPath);
                    File.Exists(newPath).Should().BeTrue("OK 確定時に空 DB が作成される");
                }
            ),
            Row(
                "ok-sqlite-new-file-then-edited-path-rejected",
                "新規作成で選んだ後に別のパスへ手で直すと、存在の検査が戻って拒否する",
                [(Sqlite, Browse), (Sqlite, Edit), (Sqlite, Ok), (Sync, Edit)],
                h =>
                {
                    h.Files.SaveResult = new FileDialogResult(h.PathOf("chosen.db"), 1);
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );

                    h.BrowseNewFile(vm);
                    vm.FilePath = h.PathOf("edited-elsewhere.db");

                    h.Ok(vm);

                    vm.Result.Should().BeNull();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
                }
            ),
            Row(
                "ok-sqlite-new-file-creation-fails-stays-open",
                "新規作成のファイルを作れないとき（親フォルダが無い）は、状態の文に出してダイアログを閉じない",
                [(Sqlite, Browse), (Sqlite, Ok), (Sync, Ok)],
                h =>
                {
                    h.Files.SaveResult = new FileDialogResult(h.PathOf("no-such-dir", "x.db"), 1);
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );

                    h.BrowseNewFile(vm);
                    h.Ok(vm);

                    // 失敗の文言の前置き（{0} の手前）で照合し、カルチャに依らないようにする
                    var failurePrefix = Strings.DbConnection_CreateFileFailed.Split('{')[0];
                    vm.Result.Should().BeNull();
                    vm.StatusMessage.Should().StartWith(failurePrefix);
                    h.Closes(vm).Should().BeEmpty("作成失敗時はダイアログを閉じない");
                }
            ),
        ];
}
