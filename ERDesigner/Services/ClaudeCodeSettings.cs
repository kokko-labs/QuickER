using System.IO;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>Claude Code 接続の設定</summary>
public class ClaudeCodeSettings
{
    /// <summary>使用するモデルエイリアス（例: sonnet, opus）。空なら Claude Code の既定を使う</summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>Claude Code 接続の設定を JSON ファイルへ保存・読込するストア</summary>
public class ClaudeCodeSettingsStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>設定ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>既定の保存先（%APPDATA%\ERDesigner）で設定ストアを生成する</summary>
    public ClaudeCodeSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ERDesigner"
            )
        ) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public ClaudeCodeSettingsStore(string folder)
    {
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パス</summary>
    public string SettingsPath => Path.Combine(_folder, "claude-code.json");

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    public ClaudeCodeSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ClaudeCodeSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ClaudeCodeSettings>(json, JsonOptions)
                ?? new ClaudeCodeSettings();
        }
        catch
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする
            return new ClaudeCodeSettings();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    public void Save(ClaudeCodeSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
