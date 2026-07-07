using System.IO;
using System.Text.Json;

namespace QuickER.Settings;

/// <summary>
/// 設定を JSON ファイルへ保存・読込する汎用ストア。
/// 各設定ストア（ChatUiSettingsStore 等）はこのクラスを継承し、ファイル名と設定型のみを指定する薄い派生クラスとなる。
/// </summary>
/// <typeparam name="TSettings">JSON へシリアライズする設定の型（既定コンストラクタを持つクラス）</typeparam>
public class JsonSettingsStore<TSettings>
    where TSettings : class, new()
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>設定ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>設定ファイル名</summary>
    private readonly string _fileName;

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    /// <param name="fileName">設定ファイル名（例: <c>ai-chat-ui.json</c>）</param>
    public JsonSettingsStore(string fileName)
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
    public JsonSettingsStore(string fileName, string folder)
    {
        _fileName = fileName;
        _folder = folder;
    }

    /// <summary>設定ファイルの絶対パス</summary>
    public string SettingsPath => Path.Combine(_folder, _fileName);

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    public TSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new TSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<TSettings>(json, JsonOptions) ?? new TSettings();
        }
        catch
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする
            return new TSettings();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    public void Save(TSettings settings)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
