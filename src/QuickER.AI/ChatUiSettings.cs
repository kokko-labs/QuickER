using System.IO;
using System.Text.Json;

namespace QuickER.AI;

/// <summary>AI チャット系ダイアログの UI 状態設定（最後に使った接続タブなど）</summary>
public class ChatUiSettings
{
    /// <summary>最後に使った接続方式（<see cref="ErChatBackendKind"/> の名前。空・不正値なら既定タブ）</summary>
    public string LastBackend { get; set; } = string.Empty;

    /// <summary><see cref="LastBackend"/> を列挙値として解釈する（空・不正値は null）</summary>
    public ErChatBackendKind? ParseLastBackend() =>
        Enum.TryParse<ErChatBackendKind>(LastBackend, ignoreCase: true, out var backend)
            ? backend
            : null;
}

/// <summary>
/// AI チャット系ダイアログの UI 状態設定を JSON ファイルへ保存・読込するストア。
/// ダイアログごとにファイル名を分けて使う（例: <c>ai-chat-ui.json</c> / <c>mock-generation-ui.json</c>）。
/// </summary>
public class ChatUiSettingsStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>設定ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>設定ファイル名（ダイアログごとに分ける）</summary>
    private readonly string _fileName;

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    /// <param name="fileName">設定ファイル名（例: <c>ai-chat-ui.json</c>）</param>
    public ChatUiSettingsStore(string fileName)
        : this(
            fileName,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuickER"
            )
        ) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    /// <param name="fileName">設定ファイル名</param>
    /// <param name="folder">保存先フォルダ</param>
    public ChatUiSettingsStore(string fileName, string folder)
    {
        _fileName = fileName;
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パス</summary>
    public string SettingsPath => Path.Combine(_folder, _fileName);

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    public ChatUiSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ChatUiSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<ChatUiSettings>(json, JsonOptions)
                ?? new ChatUiSettings();
        }
        catch
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする
            return new ChatUiSettings();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    public void Save(ChatUiSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
