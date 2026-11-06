using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// 接続設定の表の 1 ケースの組（一時フォルダの設定ストア・API キーの読み書きの差し込み・作った VM）と、
/// どの手の後にも掛ける不変条件の見張り
/// </summary>
/// <remarks>
/// <para>不変条件（モデルとは独立に判定し、行の本体の後に <see cref="AssertInvariants"/> がまとめて表明する）：</para>
/// <list type="number">
/// <item>API キーが読めない間（<see cref="ChatConnectionSettingsViewModel.IsSavedApiKeyUnreadable"/>）は、新しいキーが入力されるまで保存の関数を呼ばない（上書きも削除もしない）。</item>
/// <item>API キー（保存の関数へ渡した値と VM の入力欄の値）は設定 JSON に入らない。</item>
/// <item>エンドポイントの上書きの実効値は LocalLlm のときだけ持つ（VM のどのプロパティの変化の後も）。</item>
/// <item>設定の保存（<see cref="SaveSettings"/> を通したもの）は、戻ったら JSON の該当セクションが VM と一致し、投げたら JSON を変えない。</item>
/// </list>
/// </remarks>
internal sealed class ConnectionSettingsHarness : IDisposable
{
    private readonly TestTempFolder _folder = new();
    private readonly List<string> _violations = [];
    private readonly List<(ChatConnectionSettingsViewModel Vm, AiDialogKind Kind)> _vms = [];

    /// <summary>設定ストアのフォルダ（一時フォルダの下の settings。作るのは保存の時）</summary>
    public string Folder => Path.Combine(_folder.Path, "settings");

    /// <summary>設定ファイルのパス</summary>
    public string SettingsPath => Store().SettingsPath;

    /// <summary>同じフォルダの設定ストア（直接読み書きする行が使う）</summary>
    public AiSettingsStore Store() => new(Folder);

    /// <summary>API キーの読み込み（既定は保存なし＝空文字）</summary>
    public Func<string, SavedApiKey?> ApiKeyLoader { get; set; } = _ => string.Empty;

    /// <summary>API キーの保存の前に呼ぶ処理（失敗を起こす行が投げる。投げたら保存の記録に積まない）</summary>
    public Action<string, string>? ApiKeySaverHook { get; set; }

    /// <summary>保存の関数へ渡され、投げずに戻った (スロット, 値)</summary>
    public List<(string Slot, string Value)> Saves { get; } = [];

    /// <summary>Codex の config.toml の読み込み（既定は空）</summary>
    public Func<CodexConfigToml> CodexConfig { get; set; } = () => new CodexConfigToml();

    /// <summary>VM を作る（不変条件の見張りを付ける）</summary>
    /// <param name="kind">ダイアログの種類</param>
    public ChatConnectionSettingsViewModel Create(AiDialogKind kind = AiDialogKind.AiChat)
    {
        ChatConnectionSettingsViewModel? created = null;
        var vm = new ChatConnectionSettingsViewModel(
            kind,
            settingsStore: Store(),
            codexConfigReader: () => CodexConfig(),
            apiKeyLoader: slot => ApiKeyLoader(slot),
            apiKeySaver: (slot, value) =>
            {
                // 読めないキーを、新しいキーの入力が無いまま上書き・削除しない（新しいキーを入れた上書きは正しい）
                if (
                    created?.IsSavedApiKeyUnreadable == true
                    && string.IsNullOrEmpty(created.ApiKey)
                )
                {
                    _violations.Add(
                        $"不変条件 1：読めない API キーを、新しいキーの入力が無いまま保存の関数へ渡した（{slot}）"
                    );
                }

                ApiKeySaverHook?.Invoke(slot, value);
                Saves.Add((slot, value));
            }
        );
        created = vm;
        vm.PropertyChanged += (_, _) => CheckEndpoint(vm);
        _vms.Add((vm, kind));
        return vm;
    }

    /// <summary>VM の PropertyChanged の名前を記録する</summary>
    public static List<string> RecordChanges(ChatConnectionSettingsViewModel vm)
    {
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return changed;
    }

    /// <summary>設定を保存する（不変条件 4 を確かめる。投げた例外はそのまま投げ直す）</summary>
    /// <param name="vm">保存する VM（<see cref="Create"/> で作ったもの）</param>
    public void SaveSettings(ChatConnectionSettingsViewModel vm)
    {
        var kind = _vms.Single(entry => ReferenceEquals(entry.Vm, vm)).Kind;
        var (beforeReadable, before) = ReadSettingsBytes();

        try
        {
            vm.SaveSettings();
        }
        catch
        {
            var (afterReadable, after) = ReadSettingsBytes();

            // 行が設定ファイルを排他で開いている間は読めないので、比べない（その行が自分で中身を表明する）
            if (
                beforeReadable
                && afterReadable
                && !(
                    before is null
                        ? after is null
                        : after is not null && before.SequenceEqual(after)
                )
            )
            {
                _violations.Add("不変条件 4：投げた設定の保存が設定 JSON を変えた");
            }

            throw;
        }

        var ui = Store().Load().UiFor(kind);

        if (
            ui.LastBackend != vm.SelectedBackend.ToString()
            || ui.ApiProvider != vm.ApiProvider.ToString()
        )
        {
            _violations.Add(
                $"不変条件 4：戻った設定の保存の後の JSON（{ui.LastBackend}・{ui.ApiProvider}）が VM（{vm.SelectedBackend}・{vm.ApiProvider}）と違う"
            );
        }
    }

    /// <summary>設定ファイルのバイト列を読む（無ければ null・排他で開かれていて読めなければ読めない印）</summary>
    private (bool Readable, byte[]? Bytes) ReadSettingsBytes()
    {
        try
        {
            return (true, File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null);
        }
        catch (IOException)
        {
            return (false, null);
        }
    }

    /// <summary>行の本体の後に、見張った不変条件と設定 JSON の中身の不変条件をまとめて表明する</summary>
    public void AssertInvariants()
    {
        if (File.Exists(SettingsPath))
        {
            var json = File.ReadAllText(SettingsPath);
            var keys = Saves
                .Select(save => save.Value)
                .Concat(_vms.Select(entry => entry.Vm.ApiKey))
                .Where(key => !string.IsNullOrEmpty(key))
                .Distinct();

            foreach (var key in keys)
            {
                if (json.Contains(key, StringComparison.Ordinal))
                {
                    _violations.Add($"不変条件 2：API キー {key} が設定 JSON に入った");
                }
            }
        }

        _violations.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (File.Exists(SettingsPath))
        {
            File.SetAttributes(SettingsPath, FileAttributes.Normal);
        }

        _folder.Dispose();
    }

    private void CheckEndpoint(ChatConnectionSettingsViewModel vm)
    {
        if (vm.ApiProvider != AiProvider.LocalLlm && vm.EffectiveEndpointOverride is not null)
        {
            _violations.Add(
                $"不変条件 3：{vm.ApiProvider} でエンドポイントの上書きの実効値 {vm.EffectiveEndpointOverride} を持つ"
            );
        }
    }
}
