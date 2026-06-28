using System.IO;
using System.Text.Json;

namespace QuickER.AI;

/// <summary>Codex App Server の起動設定</summary>
public class CodexAppServerSettings
{
    /// <summary>使用するモデルプロバイダー（例: ollama-launch, openai）空の場合は codex の既定を使う</summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>使用するモデル名（例: gemma4:31b-cloud）空の場合は codex の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Codex App Server の設定を JSON ファイルへ保存・読込するストア</summary>
public class CodexAppServerSettingsStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>設定ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public CodexAppServerSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuickER"
            )
        ) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public CodexAppServerSettingsStore(string folder)
    {
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パス</summary>
    public string SettingsPath => Path.Combine(_folder, "codex-app-server.json");

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    public CodexAppServerSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new CodexAppServerSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<CodexAppServerSettings>(json, JsonOptions)
                ?? new CodexAppServerSettings();
        }
        catch
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする
            return new CodexAppServerSettings();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    public void Save(CodexAppServerSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
