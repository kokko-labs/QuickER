using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>前回接続を SQL Server の「last.example.com」として記録する（パスワードは保存しない）</summary>
    private static void SaveLastUsedSqlServer(ConnectionDialogHarness h) =>
        h.Store.SaveLastUsed(
            new SqlConnectionProfile { Server = "last.example.com", Database = "LastDb" },
            string.Empty
        );

    /// <summary>保存済みのプロファイル 2 件と前回接続がある状態の VM を作る（未選択）</summary>
    private static DbConnectionDialogViewModel CreateWithProfiles(
        ConnectionDialogHarness h,
        out Guid idA,
        out Guid idB
    )
    {
        idA = h.SeedProfile("本番DB");
        idB = h.SeedProfile("検証DB", "staging.example.com", "staging-secret");
        SaveLastUsedSqlServer(h);
        return h.Create();
    }

    /// <summary>一覧から Id で項目を引く</summary>
    private static DbConnectionDialogViewModel.ProfileListItem ItemOf(
        DbConnectionDialogViewModel vm,
        Guid id
    ) => vm.Profiles.Single(item => item.Profile.Id == id);

    /// <summary>空のストア・保存済みのプロファイル・選択・同期・SQLite を選んだ状態での、状態に依らない手と状態ごとに違う手</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> SweepRows() =>
        [
            Row(
                "delete-fresh-store-asks-to-select-profile",
                "空のストアで削除を打つと、確認を出さずに削除するプロファイルを選ぶよう求め、何も書かず閉じない",
                [(Fresh, Delete)],
                h =>
                {
                    var vm = h.Create();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:829-833（未選択の削除は状態の文で選択を求める）
                    h.Delete(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_SelectProfileToDelete);
                    h.Store.LoadAll().Should().BeEmpty();
                    h.Closes(vm).Should().BeEmpty();
                }
            ),
            Row(
                "browse-fresh-sql-server-has-no-file-path-and-cannot-create-new-file",
                "SQL Server のままでは、新規作成を許可していても参照の欄を出さず新規作成を実行できない",
                [(Fresh, Browse)],
                h =>
                {
                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:248-255・:885-886（欄の表示と新規作成の実行可否は SQLite のときだけ）
                    var vm = h.Create(ConnectionDialects.WithSqlite, allowSqliteFileCreation: true);

                    vm.ShowFilePath.Should().BeFalse();
                    vm.ShowCreateNewFile.Should().BeFalse();
                    vm.BrowseNewFileCommand.CanExecute(null).Should().BeFalse();
                }
            ),
            Row(
                "edit-profiles-unselected-keeps-selection-null-and-store",
                "保存済みのプロファイルがあって未選択のとき、入力を変えても選択は null のままで、名前の変更は実行できず、ストアも変わらない",
                [(Profiles, Edit)],
                h =>
                {
                    var vm = CreateWithProfiles(h, out _, out _);
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:744-747（名前の変更は選択が要る）。入力が選択を触らない点は特性
                    h.Edit(
                        vm,
                        v =>
                        {
                            v.Host = "edited.example.com";
                            v.Database = "EditedDb";
                            v.Port = "1444";
                            v.ProfileName = "別名";
                        }
                    );

                    vm.SelectedProfile.Should().BeNull();
                    vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();
                    vm.Profiles.Should().HaveCount(2);
                    h.Snapshot().Should().Equal(before);
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "test-profiles-unselected-succeeds-without-recording-last-used",
                "保存済みのプロファイルがあって未選択のまま接続テストをすると成功を知らせ、ストアと前回接続は変わらない",
                [(Profiles, Test)],
                async h =>
                {
                    var vm = CreateWithProfiles(h, out _, out _);
                    h.SqlServer.Importer.Succeed(3);
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637（接続テストはストアへ書かない）。前回接続を記録しない点は特性
                    await h.TestAsync(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 3));
                    h.SqlServer.Importer.Calls.Should().Be(1);
                    vm.IsBusy.Should().BeFalse();
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "browse-profiles-unselected-sets-file-path-and-keeps-selection-null",
                "保存済みのプロファイルがあって未選択のまま参照で選ぶとパスが入り、キャンセルでは変わらず、選択もストアも変わらない",
                [(Profiles, Browse)],
                h =>
                {
                    h.SeedProfile("本番DB");
                    var vm = h.CreateSqlite();
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:871-883（選んだパスを入れ、キャンセルは何もしない）
                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked);
                    vm.SelectedProfile.Should().BeNull();

                    h.Files.OpenResult = null;
                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked, "キャンセルは欄を変えない");
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "ok-profiles-restored-last-connection-confirms-and-updates-last-used",
                "前回接続を復元した状態で入力を直して OK すると確定し、前回接続が更新され、保存済みのプロファイルは変わらない",
                [(Profiles, Ok)],
                h =>
                {
                    var idA = h.SeedProfile("本番DB");
                    SaveLastUsedSqlServer(h);
                    var vm = h.Create();
                    vm.Host.Should().Be("last.example.com");
                    vm.Host = "edited.example.com";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:975-998（OK は前回接続を記録して確定する）
                    h.Ok(vm);

                    h.Closes(vm).Should().Equal(true);
                    vm.Result!.Host.Should().Be("edited.example.com");
                    vm.ResultProvider!.Name.Should().Be(SqlServerProvider.ProviderName);
                    h.Store.LoadLastUsed()!.Value.Profile.Server.Should().Be("edited.example.com");
                    var stored = h.Store.LoadAll().Should().ContainSingle().Subject;
                    stored.Id.Should().Be(idA);
                    stored.Server.Should().Be("prod.example.com");
                    h.Dialogs.ErrorMessages.Should().BeEmpty();
                }
            ),
            Row(
                "cancel-profiles-unselected-after-edit-closes-without-touching-store",
                "保存済みのプロファイルがあって未選択のまま入力を変えて取り消すと、結果を残さず閉じ、ストアのバイト列は変わらない",
                [(Profiles, Cancel)],
                h =>
                {
                    var vm = CreateWithProfiles(h, out _, out _);
                    vm.Host = "edited.example.com";
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008（取り消しは結果を空にして閉じるだけ）
                    h.Cancel(vm);

                    h.Closes(vm).Should().Equal(false);
                    vm.Result.Should().BeNull();
                    vm.ResultProvider.Should().BeNull();
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "select-selected-reselect-applies-each-profile-and-null-keeps-fields",
                "選んだ後に別のプロファイルへ選び直すと欄と状態の文がそちらへ変わり、選択を外しても入力欄と保存名は残る",
                [(Selected, Select)],
                h =>
                {
                    var vm = CreateWithProfiles(h, out var idA, out var idB);

                    h.Select(vm, ItemOf(vm, idA));
                    vm.Host.Should().Be("prod.example.com");
                    vm.ProfileName.Should().Be("本番DB");

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:526-577（選択で欄へ当てる）
                    h.Select(vm, ItemOf(vm, idB));

                    vm.Host.Should().Be("staging.example.com");
                    vm.Password.Should().Be("staging-secret");
                    vm.ProfileName.Should().Be("検証DB");
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileLoaded, "検証DB"));

                    // 特性の固定（文書に定めが無い今の振る舞い）：選択を外しても入力欄と保存名は残る（根拠 src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:543-546）
                    h.Select(vm, null);

                    vm.SelectedProfile.Should().BeNull();
                    vm.Host.Should().Be("staging.example.com");
                    vm.ProfileName.Should().Be("検証DB");
                }
            ),
            Row(
                "edit-selected-keeps-selection-and-profile-name",
                "特性の固定（文書に定めが無い今の振る舞い）：選んだ後に入力欄を変えても選択と保存名は保たれ、ストアは変わらない",
                [(Selected, Edit)],
                h =>
                {
                    var vm = CreateWithProfiles(h, out var idA, out _);
                    h.Select(vm, ItemOf(vm, idA));
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:309-324（入力の変更通知は選択に触れない）
                    h.Edit(
                        vm,
                        v =>
                        {
                            v.Host = "edited.example.com";
                            v.Password = "edited-secret";
                            v.AuthMode = DbAuthMode.UsernamePassword;
                        }
                    );

                    vm.SelectedProfile!.Id.Should().Be(idA);
                    vm.ProfileName.Should().Be("本番DB");
                    vm.Profiles.Should().HaveCount(2);
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "test-selected-uses-current-inputs-and-keeps-selection",
                "選んだプロファイルの接続テストは現在の入力で流れ、成功を知らせ、選択を保つ",
                [(Selected, Test)],
                async h =>
                {
                    var vm = CreateWithProfiles(h, out var idA, out _);
                    h.Select(vm, ItemOf(vm, idA));
                    h.SqlServer.Importer.Succeed(2);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:601-602（現在の入力で接続テスト）・:620
                    await h.TestAsync(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 2));
                    vm.SelectedProfile!.Id.Should().Be(idA);
                    h.SqlServer.LastSettings!.Host.Should().Be("prod.example.com");
                }
            ),
            Row(
                "browse-selected-sqlite-profile-sets-file-path-and-keeps-saved-profile",
                "SQLite のプロファイルを選んだ後に参照で選ぶとパスが入り、選択を保ち、保存済みのパスは変わらない",
                [(Selected, Browse)],
                h =>
                {
                    var saved = h.PathOf("saved.db");
                    var id = h.SeedSqliteProfile("ローカル", saved);
                    var vm = h.Create(ConnectionDialects.WithSqlite);
                    h.Select(vm, ItemOf(vm, id));
                    vm.SelectedProvider.Name.Should().Be(SqliteProvider.ProviderName);
                    var picked = h.PathOf("picked.sqlite");
                    h.Files.OpenResult = new FileDialogResult(picked, 1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:380-386（取込は方言が移る）・:882（参照は欄だけ変える）
                    h.BrowseFile(vm);

                    vm.FilePath.Should().Be(picked);
                    vm.SelectedProfile!.Id.Should().Be(id);
                    h.Store.LoadAll().Should().ContainSingle().Which.FilePath.Should().Be(saved);
                }
            ),
            Row(
                "cancel-selected-closes-and-keeps-profile-and-last-used",
                "プロファイルを選んだ状態で取り消すと結果を残さず閉じ、プロファイルも前回接続も変わらない",
                [(Selected, Cancel)],
                h =>
                {
                    var vm = CreateWithProfiles(h, out var idA, out _);
                    h.Select(vm, ItemOf(vm, idA));
                    var before = h.Snapshot();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008
                    h.Cancel(vm);

                    h.Closes(vm).Should().Equal(false);
                    vm.Result.Should().BeNull();
                    h.Store.LoadAll().Should().HaveCount(2);
                    h.Snapshot().Should().Equal(before);
                }
            ),
            Row(
                "save-sync-same-name-other-dialect-is-untouched",
                "同期モード（SQL Server 固定）で SQLite の同名プロファイルがあっても確認なしに SQL Server の分を足し、SQLite の分は変えない",
                [(Sync, Save)],
                h =>
                {
                    var sqlitePath = h.PathOf("shared.db");
                    h.SeedSqliteProfile("共通", sqlitePath);
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );
                    vm.Host = "srv";
                    vm.Database = "db";
                    vm.ProfileName = "共通";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:664-674（上書き先は同名かつ同じ方言だけ）
                    h.Save(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    var stored = h.Store.LoadAll();
                    stored.Should().HaveCount(2);
                    stored
                        .Single(p => p.Dbms == SqliteProvider.ProviderName)
                        .FilePath.Should()
                        .Be(sqlitePath);
                    stored
                        .Single(p => p.Dbms == SqlServerProvider.ProviderName)
                        .Server.Should()
                        .Be("srv");
                    vm.SelectedProvider.Should().BeSameAs(h.SqlServer);
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileSaved, "共通"));
                }
            ),
            Row(
                "rename-sync-same-dialect-profile-keeps-fixed-dialect-and-fields",
                "同期モードで同じ方言のプロファイルを選んで名前を変えると、方言は固定のまま、入力欄は塗り直さない",
                [(Sync, Rename)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );
                    h.Select(vm, ItemOf(vm, id));
                    vm.Host = "edited.example.com";
                    vm.ProfileName = "新名";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:749-761（名前だけを変え、入力欄は塗り直さない）
                    h.Rename(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileRenamed, "本番DB", "新名"));
                    vm.SelectedProvider.Should().BeSameAs(h.SqlServer);
                    vm.Host.Should().Be("edited.example.com");
                    h.Store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("新名");
                }
            ),
            Row(
                "delete-sync-same-dialect-profile-keeps-other-dialect",
                "同期モードで同じ方言のプロファイルを確認のうえ削除すると消え、別の方言のプロファイルは残り、方言は固定のまま",
                [(Sync, Delete)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");
                    h.SeedSqliteProfile("ローカル", h.PathOf("local.db"));
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );
                    h.Select(vm, ItemOf(vm, id));

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:826-869
                    h.Delete(vm);

                    h.Dialogs.WarningConfirmMessages.Should().ContainSingle();
                    h.Store.LoadAll()
                        .Should()
                        .ContainSingle()
                        .Which.Dbms.Should()
                        .Be(SqliteProvider.ProviderName);
                    vm.SelectedProvider.Should().BeSameAs(h.SqlServer);
                }
            ),
            Row(
                "test-sync-calls-only-the-fixed-dialect-importer",
                "同期モードの接続テストは固定した方言の取込だけを呼ぶ",
                [(Sync, Test)],
                async h =>
                {
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );
                    h.SqlServer.Importer.Succeed(1);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637・組の不変条件 3（方言は固定）
                    await h.TestAsync(vm);

                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 1));
                    h.SqlServer.Importer.Calls.Should().Be(1);
                    h.Sqlite.Importer.Calls.Should().Be(0);
                    vm.SelectedProvider.Should().BeSameAs(h.SqlServer);
                }
            ),
            Row(
                "cancel-sync-closes-without-result",
                "同期モードで取り消すと結果も方言も残さず閉じる",
                [(Sync, Cancel)],
                h =>
                {
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:1001-1008
                    h.Cancel(vm);

                    h.Closes(vm).Should().Equal(false);
                    vm.Result.Should().BeNull();
                    vm.ResultProvider.Should().BeNull();
                }
            ),
            Row(
                "open-sqlite-last-connection-restores-dialect-and-file-path-in-both-modes",
                "前回接続が SQLite なら、取込モードでも SQLite 固定の同期モードでも、方言とファイルパスを復元する",
                [(Sqlite, Open)],
                h =>
                {
                    var path = h.PathOf("last.db");
                    h.Store.SaveLastUsed(
                        new SqlConnectionProfile
                        {
                            Dbms = SqliteProvider.ProviderName,
                            FilePath = path,
                        },
                        string.Empty
                    );

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:340-374（復元は方言ごと・同期は固定した方言と同じときだけ）
                    var importVm = h.Create(ConnectionDialects.WithSqlite);
                    var syncVm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.Sqlite
                    );

                    foreach (var vm in new[] { importVm, syncVm })
                    {
                        vm.SelectedProvider.Name.Should().Be(SqliteProvider.ProviderName);
                        vm.ShowFilePath.Should().BeTrue();
                        vm.FilePath.Should().Be(path);
                        vm.StatusMessage.Should().Be(Strings.DbConnection_Restored);
                    }
                }
            ),
            Row(
                "select-sqlite-switches-dialect-in-import-and-refuses-in-sync",
                "SQLite を選んだ状態で SQL Server のプロファイルを選ぶと、取込は方言が SQL Server に移り、同期（SQLite 固定）は当てずに方言の不一致を知らせる",
                [(Sqlite, Select)],
                h =>
                {
                    var id = h.SeedProfile("本番DB");

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:380-386（取込は方言を移す）・:551-558（同期は当てない）・:249
                    var importVm = h.CreateSqlite();
                    h.Select(importVm, ItemOf(importVm, id));

                    importVm.SelectedProvider.Name.Should().Be(SqlServerProvider.ProviderName);
                    importVm.ShowFilePath.Should().BeFalse();

                    var syncVm = h.CreateSqlite(mode: DbConnectionDialogMode.Sync);
                    h.Select(syncVm, ItemOf(syncVm, id));

                    syncVm
                        .StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileDialectMismatch, "本番DB"));
                    syncVm.SelectedProvider.Should().BeSameAs(h.Sqlite);
                    syncVm.Host.Should().Be("localhost", "別の方言のプロファイルは欄へ当てない");
                }
            ),
            Row(
                "rename-sqlite-profile-to-name-of-other-dialect-does-not-ask",
                "SQLite のプロファイルを SQL Server の同名へ変えても、方言が違えば衝突せず確認なしに変わり、両方残り、ファイルパスは塗り直さない",
                [(Sqlite, Rename)],
                h =>
                {
                    h.SeedProfile("X");
                    var path = h.PathOf("y.db");
                    var sqliteId = h.SeedSqliteProfile("Y", path);
                    var vm = h.Create(ConnectionDialects.WithSqlite);
                    h.Select(vm, ItemOf(vm, sqliteId));
                    vm.ProfileName = "X";

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:771-777（衝突は同名かつ同じ方言だけ）
                    h.Rename(vm);

                    h.Dialogs.WarningConfirmMessages.Should().BeEmpty();
                    var stored = h.Store.LoadAll();
                    stored.Should().HaveCount(2);
                    stored.Select(p => p.Name).Should().Equal("X", "X");
                    vm.FilePath.Should().Be(path);
                }
            ),
            Row(
                "delete-sqlite-profile-removes-it-and-keeps-fields",
                "SQLite のプロファイルを確認のうえ削除すると消え、入力欄と保存名は残る",
                [(Sqlite, Delete)],
                h =>
                {
                    var path = h.PathOf("local.db");
                    var id = h.SeedSqliteProfile("ローカル", path);
                    var vm = h.Create(ConnectionDialects.WithSqlite);
                    h.Select(vm, ItemOf(vm, id));

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:845-869
                    h.Delete(vm);

                    vm.Profiles.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileDeleted, "ローカル"));

                    // 特性の固定（文書に定めが無い今の振る舞い）：削除しても入力欄と保存名は消さない（根拠 src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:866-868）
                    vm.FilePath.Should().Be(path);
                    vm.ProfileName.Should().Be("ローカル");
                }
            ),
            Row(
                "test-sqlite-empty-file-path-still-calls-importer",
                "特性の固定（文書に定めが無い今の振る舞い）：SQLite のファイルパスが空でも接続テストは取込を呼び、ファイルは作らない（OK がパスを検証するのと非対称）",
                [(Sqlite, Test)],
                async h =>
                {
                    var vm = h.CreateSqlite();

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:604-637（接続テストはファイルのパスを検証しない）。OK は :931-950 で検証する
                    await h.TestAsync(vm);

                    h.Sqlite.Importer.Calls.Should().Be(1);
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 0));
                    Directory.GetFiles(h.PathOf(), "*.db").Should().BeEmpty();
                }
            ),
            Row(
                "cancel-sqlite-new-file-chosen-creates-nothing",
                "SQLite の新規作成でパスを選んだ後に取り消すと、ファイルは作られず、取り消しとして閉じる",
                [(Sqlite, Cancel)],
                h =>
                {
                    var newPath = h.PathOf("never-created.db");
                    h.Files.SaveResult = new FileDialogResult(newPath, 1);
                    var vm = h.CreateSqlite(
                        allowFileCreation: true,
                        mode: DbConnectionDialogMode.Sync
                    );
                    h.BrowseNewFile(vm);

                    // 根拠：src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:892-894（実ファイルの作成は確定時）・組の不変条件 10
                    h.Cancel(vm);

                    File.Exists(newPath).Should().BeFalse();
                    h.Closes(vm).Should().Equal(false);
                }
            ),
        ];
}
