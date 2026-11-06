using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.AI.UI.Resources;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>保存の失敗（書けない・読めない設定ファイル・読めない API キー・キーの保存の失敗）</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> FailureRows() =>
        [
            Row(
                "failure-remove-history-write-fails-keeps-candidate-and-notifies",
                "設定ファイルが書けないとき、モデルの履歴の × は例外を出さず、候補を消さずに知らせる",
                [(ApiKey, Remove)],
                h =>
                {
                    h.ApiKeyLoader = _ => null;
                    var vm = CreateVmWithHistory(h);
                    var messages = new List<string>();
                    vm.ModelHistoryRemoveFailed += (_, message) => messages.Add(message);
                    File.SetAttributes(h.SettingsPath, FileAttributes.ReadOnly);

                    var act = () => vm.RemoveApiModelHistoryCommand.Execute("custom-openai");

                    act.Should().NotThrow("コマンドから漏れた例外はアプリを終了させる");
                    messages.Should().ContainSingle();
                    vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
                    File.ReadAllText(h.SettingsPath).Should().Contain("custom-openai");
                }
            ),
            Row(
                "failure-remove-history-file-unreadable-keeps-candidate-and-notifies",
                "設定ファイルが読めないとき、モデルの履歴の × は候補を消さずに、上書きしなかったことを知らせる",
                [(ApiKey, Remove)],
                h =>
                {
                    h.ApiKeyLoader = _ => null;
                    var vm = CreateVmWithHistory(h);
                    var messages = new List<string>();
                    vm.ModelHistoryRemoveFailed += (_, message) => messages.Add(message);
                    var before = File.ReadAllText(h.SettingsPath);

                    using (
                        new FileStream(
                            h.SettingsPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.None
                        )
                    )
                    {
                        vm.RemoveApiModelHistoryCommand.Execute("custom-openai");
                    }

                    messages
                        .Should()
                        .ContainSingle()
                        .Which.Should()
                        .Contain(Strings.Connection_SettingsFileUnreadable);
                    vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
                    File.ReadAllText(h.SettingsPath).Should().Be(before);
                }
            ),
            Row(
                "failure-unreadable-api-key-is-neither-overwritten-nor-deleted",
                "保存されている API キーが読めないときは注記を出し、新しいキーを入力するまで上書きも削除もしない",
                [(ApiKey, CommitKey), (ApiKey, Save)],
                h =>
                {
                    h.ApiKeyLoader = _ => SavedApiKey.Unreadable;
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.Initialize();

                    vm.IsSavedApiKeyUnreadable.Should().BeTrue();
                    vm.ApiKey.Should().BeEmpty();

                    // 保存のオフ・オン、確定、接続設定の保存のどれでも、暗号ファイルを消す（空の値を保存する）呼び出しをしない
                    vm.SaveApiKey = false;
                    vm.SaveApiKey = true;
                    vm.CommitApiKey();
                    h.SaveSettings(vm);

                    h.Saves.Should().BeEmpty("読めなかったキーを空の値で消さない");
                    vm.IsSavedApiKeyUnreadable.Should().BeTrue();

                    // 新しいキーを入力したら、それで上書きする
                    vm.ApiKey = "sk-new";
                    vm.CommitApiKey();

                    h.Saves.Should().ContainSingle().Which.Value.Should().Be("sk-new");
                    vm.IsSavedApiKeyUnreadable.Should().BeFalse();
                }
            ),
            Row(
                "failure-save-settings-file-unreadable-throws",
                "設定ファイルが読めないとき、接続設定の保存は黙って捨てずに例外で知らせる",
                [(Common, Save)],
                h =>
                {
                    h.ApiKeyLoader = _ => null;
                    var vm = h.Create();
                    vm.LoadSettings();
                    h.SaveSettings(vm);
                    var before = File.ReadAllText(h.SettingsPath);
                    vm.ClaudeCodeModel = "my-model";

                    using (
                        new FileStream(
                            h.SettingsPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.None
                        )
                    )
                    {
                        var act = () => h.SaveSettings(vm);

                        act.Should()
                            .Throw<IOException>()
                            .WithMessage(Strings.Connection_SettingsFileUnreadable);
                    }

                    File.ReadAllText(h.SettingsPath).Should().Be(before);
                }
            ),
            Row(
                "apikey-save-failure-keeps-notifying-and-retries-on-next-commit",
                "キーの保存が失敗しても通知は出て次の確定で保存し直す",
                [(ApiKey, CommitKey)],
                h =>
                {
                    var fail = true;
                    h.ApiKeySaverHook = (_, _) =>
                    {
                        if (fail)
                        {
                            throw new System.Security.Cryptography.CryptographicException("dpapi");
                        }
                    };
                    var vm = h.Create();
                    var changes = ConnectionSettingsHarness.RecordChanges(vm);

                    vm.ApiKey = "sk-1";
                    changes.Should().Contain(nameof(ChatConnectionSettingsViewModel.ApiKey));

                    var commit = () => vm.CommitApiKey();
                    commit.Should().NotThrow();
                    h.Saves.Should().BeEmpty();

                    fail = false;
                    vm.CommitApiKey();
                    h.Saves.Should().Equal(("OpenAiApiKey", "sk-1"));
                }
            ),
        ];
}
