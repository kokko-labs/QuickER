using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogOp;
using static QuickER.Tests.Db.UI.ConnectionDialog.ConnectionDialogState;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

internal static partial class ConnectionDialogRows
{
    /// <summary>OK の検証（タイムアウト・ポート・接続先）・確定・前回接続の記録</summary>
    private static IEnumerable<ScenarioRow<ConnectionDialogCase>> OkRows() =>
        [
            Row(
                "ok-sets-result-and-provider",
                "OK の確定で選択中の方言が ResultProvider に入る",
                [(Fresh, Ok)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = "db";

                    h.Ok(vm);

                    vm.Result.Should().NotBeNull();
                    vm.ResultProvider!.Name.Should().Be("sqlserver");
                }
            ),
            Row(
                "ok-saves-last-connection-restored-on-next-open",
                "OK の後に新しいダイアログを開くと、前回接続のデータベース名・認証・パスワードまで復元される",
                [(Fresh, Ok), (Profiles, Open)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "restored-server";
                    vm.Database = "restored-db";
                    vm.AuthMode = DbAuthMode.UsernamePassword;
                    vm.UserId = "sa";
                    vm.Password = "secret";
                    vm.SavePassword = true;

                    h.Ok(vm);

                    var reopened = h.Create();

                    reopened.Host.Should().Be("restored-server");
                    reopened.Database.Should().Be("restored-db");
                    reopened.UserId.Should().Be("sa");
                    reopened.Password.Should().Be("secret");
                    reopened.StatusMessage.Should().Be(Strings.DbConnection_Restored);
                }
            ),
            Row(
                "ok-server-dialect-requires-host-and-database",
                "サーバー系の方言は、接続先かデータベース名が空なら確定せずに入力を求める",
                [(Fresh, Ok)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "srv";
                    vm.Database = string.Empty;

                    h.Ok(vm);

                    vm.Result.Should().BeNull();
                    h.Closes(vm).Should().BeEmpty();
                    vm.StatusMessage.Should().Be(Strings.DbConnection_HostAndDatabaseRequired);
                }
            ),
            Row(
                "ok-oracle-carries-service-name-and-port",
                "Oracle の確定はサービス名・ポートを接続設定へ載せ、方言も Oracle になる",
                [(Fresh, Ok)],
                h =>
                {
                    var vm = h.Create(ConnectionDialects.All);
                    vm.SelectedProvider = h.Oracle;
                    vm.Host = "ora01";
                    vm.Database = "SALES";
                    vm.ServiceName = "ORCLPDB1";
                    vm.Port = "1521";

                    h.Ok(vm);

                    vm.ResultProvider.Should().BeSameAs(h.Oracle);
                    vm.Result!.ServiceName.Should().Be("ORCLPDB1");
                    vm.Result.Port.Should().Be(1521);
                }
            ),
            Row(
                "ok-command-timeout-zero-accepted",
                "コマンドタイムアウト 0（無制限）は確定できる",
                [(Fresh, Ok)],
                h =>
                {
                    var vm = h.Create();
                    vm.Host = "server";
                    vm.Database = "db";
                    vm.CommandTimeout = "0";

                    h.Ok(vm);

                    vm.Result.Should().NotBeNull();
                    vm.Result!.CommandTimeoutSeconds.Should().Be(0);
                }
            ),
            InvalidTimeoutRow("ok-command-timeout-blank-rejected", string.Empty),
            InvalidTimeoutRow("ok-command-timeout-non-numeric-rejected-letters", "abc"),
            InvalidTimeoutRow("ok-command-timeout-non-numeric-rejected-spaces", "   "),
            InvalidTimeoutRow("ok-command-timeout-non-numeric-rejected-decimal", "1.5"),
            InvalidTimeoutRow("ok-command-timeout-negative-rejected", "-1"),
            InvalidPortRow("ok-port-invalid-rejected-trailing-letter", "1543z"),
            InvalidPortRow("ok-port-invalid-rejected-zero", "0"),
            InvalidPortRow("ok-port-invalid-rejected-negative", "-1"),
            InvalidPortRow("ok-port-invalid-rejected-above-range", "65536"),
            ValidPortRow("ok-port-valid-accepted-empty", "", null),
            ValidPortRow("ok-port-valid-accepted-spaces", "  ", null),
            ValidPortRow("ok-port-valid-accepted-one", "1", 1),
            ValidPortRow("ok-port-valid-accepted-max", "65535", 65535),
        ];

    /// <summary>コマンドタイムアウトの不正値で確定しない行</summary>
    private static ScenarioRow<ConnectionDialogCase> InvalidTimeoutRow(string id, string input) =>
        Row(
            id,
            $"コマンドタイムアウト「{input}」（空欄・数値でない・負）は OK を拒否する",
            [(Fresh, Ok)],
            h =>
            {
                var vm = h.Create();
                vm.Host = "server";
                vm.Database = "db";
                vm.CommandTimeout = input;

                h.Ok(vm);

                vm.Result.Should().BeNull();
                vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
            }
        );

    /// <summary>ポートの不正値で確定しない行</summary>
    private static ScenarioRow<ConnectionDialogCase> InvalidPortRow(string id, string port) =>
        Row(
            id,
            $"ポート「{port}」の不正値は確定を止める（空欄だけが方言の既定）",
            [(Fresh, Ok)],
            h =>
            {
                var vm = h.Create();
                vm.Host = "srv";
                vm.Database = "db";
                vm.Port = port;

                h.Ok(vm);

                h.Closes(vm).Should().BeEmpty();
                vm.StatusMessage.Should().Be(Strings.DbConnection_PortInvalid);
            }
        );

    /// <summary>空欄と範囲内のポートで確定する行</summary>
    private static ScenarioRow<ConnectionDialogCase> ValidPortRow(
        string id,
        string port,
        int? expected
    ) =>
        Row(
            id,
            $"ポート「{port}」は確定でき、接続設定のポートは {expected?.ToString() ?? "空（方言の既定）"}",
            [(Fresh, Ok)],
            h =>
            {
                var vm = h.Create();
                vm.Host = "srv";
                vm.Database = "db";
                vm.Port = port;

                h.Ok(vm);

                vm.Result.Should().NotBeNull();
                vm.Result!.Port.Should().Be(expected);
            }
        );
}
