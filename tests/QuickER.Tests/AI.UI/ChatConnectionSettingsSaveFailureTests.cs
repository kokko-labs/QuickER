using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.AI.UI.Resources;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// AI の接続設定（ai-settings.json）が保存できないときに、アプリを落とさず、表示と保存を食い違わせずに知らせることを固定する
/// </summary>
/// <remarks>
/// 保存できないのは、書き込みの失敗（読み取り専用・アクセス拒否）と、読めない既存ファイルを守るためにストアが書かずに
/// 戻ったとき（<see cref="QuickER.Settings.JsonSettingsStore{TSettings}.Save"/> が <c>false</c>）の 2 通り。
/// モデルの履歴の × はコマンドから例外を漏らすと UI の例外の受け皿がアプリを終了させ、<c>false</c> を捨てると
/// 表示だけ消えて保存されない。
/// </remarks>
public sealed class ChatConnectionSettingsSaveFailureTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "chat-settings-save-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        try
        {
            var path = new AiSettingsStore(_folder).SettingsPath;

            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗は無視する
        }
    }

    private string SettingsPath => new AiSettingsStore(_folder).SettingsPath;

    private ChatConnectionSettingsViewModel CreateVm() =>
        new(
            AiDialogKind.AiChat,
            settingsStore: new AiSettingsStore(_folder),
            codexConfigReader: () => new CodexConfigToml(),
            apiKeyLoader: _ => null,
            apiKeySaver: (_, _) => { }
        );

    /// <summary>カスタムモデルを 1 つ履歴に記録した VM（表示中のモデルは別のもの）</summary>
    private ChatConnectionSettingsViewModel CreateVmWithHistory()
    {
        var vm = CreateVm();
        vm.LoadSettings();
        vm.ApiModel = "custom-openai";
        vm.RecordSuccessfulModel();
        vm.ApiModel = "other";
        vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
        return vm;
    }

    [Fact(
        DisplayName = "設定ファイルが書けないとき、モデルの履歴の × は例外を出さず、候補を消さずに知らせる"
    )]
    public void RemoveHistory_WriteFails_KeepsCandidateAndNotifies()
    {
        var vm = CreateVmWithHistory();
        var messages = new List<string>();
        vm.ModelHistoryRemoveFailed += (_, message) => messages.Add(message);
        File.SetAttributes(SettingsPath, FileAttributes.ReadOnly);

        var act = () => vm.RemoveApiModelHistoryCommand.Execute("custom-openai");

        act.Should().NotThrow("コマンドから漏れた例外はアプリを終了させる");
        messages.Should().ContainSingle();
        vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
        File.ReadAllText(SettingsPath).Should().Contain("custom-openai");
    }

    [Fact(
        DisplayName = "設定ファイルが読めないとき、モデルの履歴の × は候補を消さずに、上書きしなかったことを知らせる"
    )]
    public void RemoveHistory_FileUnreadable_KeepsCandidateAndNotifies()
    {
        var vm = CreateVmWithHistory();
        var messages = new List<string>();
        vm.ModelHistoryRemoveFailed += (_, message) => messages.Add(message);
        var before = File.ReadAllText(SettingsPath);

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm.RemoveApiModelHistoryCommand.Execute("custom-openai");
        }

        messages
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(Strings.Connection_SettingsFileUnreadable);
        vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-openai");
        File.ReadAllText(SettingsPath).Should().Be(before);
    }

    [Fact(
        DisplayName = "保存されている API キーが読めないときは注記を出し、新しいキーを入力するまで上書きも削除もしない"
    )]
    public void UnreadableApiKey_IsNeitherOverwrittenNorDeletedUntilNewKeyIsEntered()
    {
        var saved = new List<(string Slot, string Key)>();
        var vm = new ChatConnectionSettingsViewModel(
            AiDialogKind.AiChat,
            settingsStore: new AiSettingsStore(_folder),
            codexConfigReader: () => new CodexConfigToml(),
            apiKeyLoader: _ => SavedApiKey.Unreadable,
            apiKeySaver: (slot, key) => saved.Add((slot, key))
        );
        vm.LoadSettings();
        vm.Initialize();

        vm.IsSavedApiKeyUnreadable.Should().BeTrue();
        vm.ApiKey.Should().BeEmpty();

        // 保存のオフ・オン、確定、接続設定の保存のどれでも、暗号ファイルを消す（空の値を保存する）呼び出しをしない
        vm.SaveApiKey = false;
        vm.SaveApiKey = true;
        vm.CommitApiKey();
        vm.SaveSettings();

        saved.Should().BeEmpty("読めなかったキーを空の値で消さない");
        vm.IsSavedApiKeyUnreadable.Should().BeTrue();

        // 新しいキーを入力したら、それで上書きする
        vm.ApiKey = "sk-new";
        vm.CommitApiKey();

        saved.Should().ContainSingle().Which.Key.Should().Be("sk-new");
        vm.IsSavedApiKeyUnreadable.Should().BeFalse();
    }

    [Fact(DisplayName = "設定ファイルが読めないとき、接続設定の保存は黙って捨てずに例外で知らせる")]
    public void SaveSettings_FileUnreadable_Throws()
    {
        var vm = CreateVm();
        vm.LoadSettings();
        vm.SaveSettings();
        var before = File.ReadAllText(SettingsPath);
        vm.ClaudeCodeModel = "my-model";

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => vm.SaveSettings();

            act.Should()
                .Throw<IOException>()
                .WithMessage(Strings.Connection_SettingsFileUnreadable);
        }

        File.ReadAllText(SettingsPath).Should().Be(before);
    }
}
