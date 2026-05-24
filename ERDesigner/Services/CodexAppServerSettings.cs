using System.IO;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>Codex App Server の起動設定です。</summary>
public class CodexAppServerSettings
{
    /// <summary>使用するモデルプロバイダーです（例: ollama-launch, openai）。空なら codex の既定を使います。</summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>使用するモデル名です（例: gemma4:31b-cloud）。空なら codex の既定を使います。</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Codex App Server の設定を JSON ファイルに保存・読込します。</summary>
public class CodexAppServerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _folder;

    /// <summary>既定の設定ストアを生成します。</summary>
    public CodexAppServerSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner")) { }

    /// <summary>テスト用に保存先フォルダを指定して設定ストアを生成します。</summary>
    public CodexAppServerSettingsStore(string folder)
    {
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パスです。</summary>
    public string SettingsPath => Path.Combine(_folder, "codex-app-server.json");

    /// <summary>設定を読み込みます。ファイルがなければ既定値を返します。</summary>
    public CodexAppServerSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new CodexAppServerSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<CodexAppServerSettings>(json, JsonOptions) ?? new CodexAppServerSettings();
        }
        catch
        {
            return new CodexAppServerSettings();
        }
    }

    /// <summary>設定を保存します。</summary>
    public void Save(CodexAppServerSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
