using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>開く・前回接続の復元・選択・方言ごとの表示・接続テスト・取り消し</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> OpenRows() =>
        [
            Row(
                "open-does-not-auto-select-saved-profile",
                "保存済みの接続があっても、開いた直後は選択せず入力欄は既定のまま",
                [(Profiles, Open)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile
                        {
                            Name = "TestDB",
                            Server = "saved-server",
                            Database = "saved-db",
                        },
                        password: ""
                    );

                    var vm = h.Create();

                    vm.SelectedProfile.Should().BeNull();
                    vm.Host.Should().Be("localhost");
                    vm.Database.Should().BeEmpty();
                    vm.ProfileName.Should().BeEmpty();
                }
            ),
            Row(
                "open-profile-display-name-contains-dbms",
                "一覧の表示名に DBMS の表示名が付く",
                [(Profiles, Open)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile
                        {
                            Name = "本番DB",
                            Dbms = "sqlserver",
                            Server = "s",
                            Database = "d",
                        },
                        password: ""
                    );

                    var vm = h.Create();

                    vm.Profiles.Should().ContainSingle();
                    vm.Profiles[0].Display.Should().Be("[SQL Server] 本番DB");
                }
            ),
            Row(
                "open-import-mode-restores-dialect-of-last-connection",
                "取込モードは前回接続を方言ごと復元する",
                [(Profiles, Open)],
                h =>
                {
                    h.Store.SaveLastUsed(
                        new SqlConnectionProfile
                        {
                            Dbms = SqliteProvider.ProviderName,
                            FilePath = @"C:\data\app.db",
                        },
                        password: string.Empty
                    );

                    var vm = h.Create(ConnectionDialects.WithSqlite);

                    vm.SelectedProvider.Name.Should().Be(SqliteProvider.ProviderName);
                    vm.FilePath.Should().Be(@"C:\data\app.db");
                }
            ),
            Row(
                "open-command-timeout-defaults-to-helper-default",
                "コマンドタイムアウトの既定は DbCommands の既定値",
                [(Fresh, Open)],
                h =>
                {
                    var vm = h.Create();

                    vm.CommandTimeout.Should().Be(DbCommands.DefaultTimeoutSeconds.ToString());
                }
            ),
            Row(
                "open-ssl-mode-defaults-to-unspecified",
                "TLS 要求水準の初期値は未指定で、接続設定へも未指定で載る",
                [(Fresh, Open)],
                h =>
                {
                    var vm = h.Create(ConnectionDialects.All);

                    vm.SslMode.Should().Be(DbSslMode.Unspecified);
                    vm.ToSettings().SslMode.Should().Be(DbSslMode.Unspecified);
                }
            ),
            NullProfilesRow("open-null-profiles-list-in-file", "{\"profiles\": null}", 0),
            NullProfilesRow(
                "open-null-profile-element-in-file",
                "{\"profiles\": [null, {\"name\": \"本番DB\", \"server\": \"srv\"}]}",
                1
            ),
            Row(
                "sync-mode-disables-dbms-selection",
                "同期モードでは DBMS を選べない",
                [(Sync, Open)],
                h =>
                {
                    var vm = h.Create(
                        mode: DbConnectionDialogMode.Sync,
                        fixedProvider: h.SqlServer
                    );

                    vm.CanSelectDbms.Should().BeFalse();
                }
            ),
            Row(
                "sync-mode-other-dialect-last-connection-not-restored",
                "同期モードでは、図と違う方言の前回接続を復元しない",
                [(Sync, Open)],
                h =>
                {
                    h.Store.SaveLastUsed(
                        new SqlConnectionProfile
                        {
                            Dbms = SqlServerProvider.ProviderName,
                            Server = "prod-sqlserver",
                            Database = "Sales",
                        },
                        password: "p@ss"
                    );

                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.Sqlite
                    );

                    vm.Host.Should().Be("localhost", "別方言のサーバー名は入れず既定のまま");
                    vm.Database.Should().BeEmpty();
                    vm.Password.Should().BeEmpty();
                    vm.StatusMessage.Should().NotBe(Strings.DbConnection_Restored);
                }
            ),
            Row(
                "sync-mode-same-dialect-last-connection-restored",
                "同期モードでも、方言が同じ前回接続は復元する",
                [(Sync, Open)],
                h =>
                {
                    h.Store.SaveLastUsed(
                        new SqlConnectionProfile
                        {
                            Dbms = SqlServerProvider.ProviderName,
                            Server = "prod-sqlserver",
                            Database = "Sales",
                        },
                        password: "p@ss"
                    );

                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );

                    vm.Host.Should().Be("prod-sqlserver");
                    vm.Database.Should().Be("Sales");
                    vm.StatusMessage.Should().Be(Strings.DbConnection_Restored);
                }
            ),
            Row(
                "sync-mode-other-dialect-profile-not-applied",
                "同期モードで別方言のプロファイルを選ぶと、方言も入力欄も変えずに理由を出す",
                [(Sync, Select)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile
                        {
                            Name = "ローカル",
                            Dbms = SqliteProvider.ProviderName,
                            FilePath = @"C:\data\local.db",
                        },
                        password: ""
                    );
                    var vm = h.Create(
                        ConnectionDialects.WithSqlite,
                        DbConnectionDialogMode.Sync,
                        h.SqlServer
                    );

                    h.Select(vm, vm.Profiles.Single());

                    vm.SelectedProvider.Should().BeSameAs(h.SqlServer);
                    vm.FilePath.Should().BeEmpty();
                    vm.ProfileName.Should().BeEmpty();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ProfileDialectMismatch, "ローカル"));
                }
            ),
            Row(
                "select-profile-applies-fields-and-password",
                "保存済み接続を選んだときだけ入力欄・復号したパスワード・保存名を反映する",
                [(Profiles, Select)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile
                        {
                            Name = "TestDB",
                            Server = "saved-server",
                            Database = "saved-db",
                            AuthMode = DbAuthMode.UsernamePassword,
                            UserId = "sa",
                            TrustServerCertificate = false,
                            SavePassword = true,
                        },
                        password: "secret"
                    );
                    var vm = h.Create();
                    vm.Host = "restored-server";
                    vm.Database = "restored-db";
                    vm.ProfileName = "復元済み";

                    h.Select(vm, vm.Profiles[0]);

                    vm.Host.Should().Be("saved-server");
                    vm.Database.Should().Be("saved-db");
                    vm.UserId.Should().Be("sa");
                    vm.Password.Should().Be("secret");
                    vm.ProfileName.Should().Be("TestDB");
                    vm.StatusMessage.Should().Contain("TestDB");
                }
            ),
            Row(
                "select-legacy-profile-command-timeout-defaults",
                "コマンドタイムアウトを持たない旧プロファイルは既定値で読み込まれる",
                [(Profiles, Select)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile { Name = "旧プロファイル", Server = "legacy" },
                        password: ""
                    );
                    var vm = h.Create();

                    h.Select(vm, vm.Profiles[0]);

                    vm.CommandTimeout.Should().Be(DbCommands.DefaultTimeoutSeconds.ToString());
                }
            ),
            Row(
                "select-ssl-mode-restored-and-flows-into-result",
                "TLS 要求水準は保存済み接続から復元されて確定内容へ載り、前回接続として次に開いたときも復元される",
                [(Profiles, Select), (Selected, Ok), (Profiles, Open)],
                h =>
                {
                    h.Store.Upsert(
                        new SqlConnectionProfile
                        {
                            Name = "PG",
                            Dbms = "postgresql",
                            Server = "pg01",
                            Database = "shop",
                            SslMode = DbSslMode.VerifyFull,
                        },
                        password: ""
                    );
                    var vm = h.Create(ConnectionDialects.All);

                    h.Select(vm, vm.Profiles[0]);
                    vm.SslMode.Should().Be(DbSslMode.VerifyFull);

                    h.Ok(vm);

                    vm.Result.Should().NotBeNull();
                    vm.Result!.SslMode.Should().Be(DbSslMode.VerifyFull);
                    h.Create(ConnectionDialects.All).SslMode.Should().Be(DbSslMode.VerifyFull);
                }
            ),
            IntegerAuthModeRow("select-integer-auth-mode-windows", 0, DbAuthMode.Windows),
            IntegerAuthModeRow(
                "select-integer-auth-mode-username-password",
                1,
                DbAuthMode.UsernamePassword
            ),
            IntegerAuthModeRow("select-integer-auth-mode-entra-id", 2, DbAuthMode.EntraId),
            Row(
                "edit-sqlserver-shows-server-fields-hides-file-path",
                "SQL Server を選ぶとサーバー系の欄を出し、ファイルパスの欄を隠す",
                [(Fresh, Edit)],
                h =>
                {
                    var vm = h.Create(ConnectionDialects.WithSqlite);

                    vm.SelectedProvider = h.SqlServer;

                    vm.ShowFilePath.Should().BeFalse();
                    vm.ShowServerFields.Should().BeTrue();
                }
            ),
            DialectDisplayRow("edit-ssl-mode-shown-postgresql", "postgresql", sslMode: true),
            DialectDisplayRow("edit-ssl-mode-shown-mysql", "mysql", sslMode: true),
            DialectDisplayRow("edit-ssl-mode-hidden-sqlserver", "sqlserver", sslMode: false),
            DialectDisplayRow("edit-ssl-mode-hidden-sqlite", "sqlite", sslMode: false),
            DialectDisplayRow(
                "edit-oracle-encryption-note-shown-oracle",
                "oracle",
                oracleNote: true
            ),
            DialectDisplayRow(
                "edit-oracle-encryption-note-hidden-postgresql",
                "postgresql",
                oracleNote: false
            ),
            DialectDisplayRow(
                "edit-oracle-encryption-note-hidden-sqlserver",
                "sqlserver",
                oracleNote: false
            ),
            DialectDisplayRow(
                "edit-oracle-encryption-note-hidden-sqlite",
                "sqlite",
                oracleNote: false
            ),
            Row(
                "test-connection-disables-ok-until-finished",
                "接続テストの実行中は OK を実行できず、テストが終われば実行できる",
                [(Fresh, Test), (Testing, Ok)],
                async h =>
                {
                    var gate = h.SqlServer.Importer.Hold();
                    var vm = h.Create(fixedProvider: h.SqlServer);
                    vm.OkCommand.CanExecute(null).Should().BeTrue("テスト前は確定できる");

                    var testing = h.StartTest(vm);
                    await gate.Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.OkCommand.CanExecute(null).Should().BeFalse("接続テスト中は確定できない");

                    gate.Release.SetResult();
                    await testing.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.OkCommand.CanExecute(null).Should().BeTrue("テストが終われば確定できる");
                }
            ),
            Row(
                "test-connection-succeeded-reports-table-count",
                "接続テストが通ると、見つけたテーブル数を状態の文に出し、実行中を解く",
                [(Fresh, Test)],
                async h =>
                {
                    h.SqlServer.Importer.Succeed(3);
                    var vm = h.Create();

                    await h.TestAsync(vm);

                    h.SqlServer.Importer.Calls.Should().Be(1);
                    vm.IsBusy.Should().BeFalse();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectSucceeded, 3));
                }
            ),
            Row(
                "test-connection-failed-reports-error",
                "接続テストが失敗すると、例外の文を状態の文に出し、実行中を解く",
                [(Fresh, Test)],
                async h =>
                {
                    h.SqlServer.Importer.Fail(new InvalidOperationException("boom"));
                    var vm = h.Create();

                    await h.TestAsync(vm);

                    vm.IsBusy.Should().BeFalse();
                    vm.StatusMessage.Should()
                        .Be(string.Format(Strings.DbConnection_ConnectFailed, "boom"));
                    h.Dialogs.ErrorMessages.Should()
                        .BeEmpty("接続テストの結果はステータス行で知らせる");
                }
            ),
            Row(
                "test-command-timeout-blank-rejected",
                "空欄のコマンドタイムアウトは接続テストを断り、実行中も立たない",
                [(Fresh, Test)],
                async h =>
                {
                    var vm = h.Create();
                    vm.Host = "server";
                    vm.Database = "db";
                    vm.CommandTimeout = string.Empty;

                    await h.TestAsync(vm);

                    vm.IsBusy.Should().BeFalse();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
                    h.SqlServer.Importer.Calls.Should().Be(0);
                }
            ),
            Row(
                "test-port-invalid-rejected",
                "ポートの不正値では接続しない",
                [(Fresh, Test)],
                async h =>
                {
                    var vm = h.Create();
                    vm.Port = "1543z";

                    await h.TestAsync(vm);

                    vm.StatusMessage.Should().Be(Strings.DbConnection_PortInvalid);
                    h.SqlServer.Importer.Calls.Should().Be(0);
                }
            ),
            Row(
                "cancel-clears-result-and-closes-without-confirming",
                "取り消しは結果を空にして、確定せずに閉じる",
                [(Fresh, Cancel)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";

                    h.Cancel(vm);

                    vm.Result.Should().BeNull();
                    vm.ResultProvider.Should().BeNull();
                    h.Closes(vm).Should().Equal(false);
                }
            ),
            Row(
                "cancel-during-test-connection-closes",
                "接続テストの実行中でも取り消して閉じられる",
                [(Testing, Cancel)],
                async h =>
                {
                    var gate = h.SqlServer.Importer.Hold();
                    var vm = h.Create();
                    _ = h.StartTest(vm);
                    await gate.Started.Task.WaitAsync(
                        TimeSpan.FromSeconds(60),
                        TestContext.Current.CancellationToken
                    );

                    vm.CancelCommand.CanExecute(null).Should().BeTrue();
                    h.Cancel(vm);

                    vm.Result.Should().BeNull();
                    h.Closes(vm).Should().Equal(false);
                }
            ),
        ];

    /// <summary>一覧や要素が null の connections.json でも開ける行</summary>
    private static ScenarioRow<ConnectionDialogCase> NullProfilesRow(
        string id,
        string json,
        int expectedCount
    ) =>
        Row(
            id,
            $"一覧や要素が null の connections.json でも開け、読めるプロファイル {expectedCount} 件を並べる",
            [(Profiles, Open)],
            h =>
            {
                File.WriteAllText(h.Store.ConnectionsPath, json);

                var vm = h.Create();

                vm.Profiles.Should().HaveCount(expectedCount);
            }
        );

    /// <summary>整数で保存された認証方式を同じ選択肢で読む行</summary>
    private static ScenarioRow<ConnectionDialogCase> IntegerAuthModeRow(
        string id,
        int saved,
        DbAuthMode expected
    ) =>
        Row(
            id,
            $"整数 {saved} で保存された認証方式は {expected} を選んだ状態で読み込まれる",
            [(Profiles, Select)],
            h =>
            {
                var json =
                    "{\"profiles\":[{\"id\":\""
                    + Guid.NewGuid().ToString()
                    + "\",\"name\":\"Saved\",\"dbms\":\"sqlserver\",\"server\":\"srv\",\"database\":\"db\",\"authMode\":"
                    + saved.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"userId\":\"u\",\"trustServerCertificate\":true,\"savePassword\":false}]}";
                File.WriteAllText(h.Store.ConnectionsPath, json);
                var vm = h.Create();

                h.Select(vm, vm.Profiles.Single());

                vm.AuthMode.Should().Be(expected);
                vm.AuthModes.Should().ContainSingle(i => i.Mode == expected);
            }
        );

    /// <summary>方言を選んだときの TLS 要求水準の欄・Oracle の平文接続の注記の表示の行（宣言の表）</summary>
    private static ScenarioRow<ConnectionDialogCase> DialectDisplayRow(
        string id,
        string dbms,
        bool? sslMode = null,
        bool? oracleNote = null
    ) =>
        Row(
            id,
            sslMode is { } shown
                ? $"TLS 要求水準の欄は {dbms} で{(shown ? "表示する" : "表示しない")}（PostgreSQL / MySQL だけ）"
                : $"Oracle の平文接続の注記は {dbms} で{(oracleNote == true ? "表示する" : "表示しない")}（Oracle だけ）",
            [(Fresh, Edit)],
            h =>
            {
                var vm = h.Create(ConnectionDialects.All);

                vm.SelectedProvider = h.Dialect(dbms);

                if (sslMode is { } ssl)
                {
                    vm.ShowSslMode.Should().Be(ssl);
                }

                if (oracleNote is { } note)
                {
                    vm.ShowOracleEncryptionNote.Should().Be(note);
                }
            }
        );
}
