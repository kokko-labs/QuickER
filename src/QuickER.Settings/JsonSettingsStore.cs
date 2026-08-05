using System.IO;
using System.Text.Json;

namespace QuickER.Settings;

/// <summary>
/// 設定を JSON ファイルへ保存・読込する汎用ストア。
/// 各設定ストア（AiSettingsStore 等）はこのクラスを継承し、ファイル名と設定型のみを指定する薄い派生クラスとなる。
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
    /// <param name="fileName">設定ファイル名（例: <c>ai-settings.json</c>）</param>
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
        WriteAtomic(SettingsPath, json);
    }

    /// <summary>設定を任意のパスへ保存する（エクスポート用。親フォルダが無ければ作成する）</summary>
    public void SaveTo(string path, TSettings settings)
    {
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomic(path, json);
    }

    /// <summary>JSON 文字列を原子的に（書き込み途中の中断で既存ファイルを壊さずに）書き込む</summary>
    /// <remarks>
    /// <c>QuickER.Documents.JsonStorageService.SaveAtomic</c> と同型の手当てを、依存ゼロの
    /// このプロジェクトへ自前で実装したもの。素の <c>File.WriteAllText</c> は既存ファイルを切り詰めて
    /// から書くため、途中でプロセスが落ちると設定ファイルが破損した JSON になる。読込側（<see cref="Load"/>）
    /// は破損を握り潰して既定値へフォールバックするため、直後の read-modify-write 保存で他のセクションが
    /// 巻き添えで消失する連鎖が起きる。tmp ファイルへ全量を書き切ってから本体へ置換することでこれを防ぐ。
    /// </remarks>
    /// <param name="path">書き込み先の絶対パス</param>
    /// <param name="json">書き込む JSON 文字列</param>
    private static void WriteAtomic(string path, string json)
    {
        // 同時保存・同名衝突を避けるため GUID を挟んだ一時ファイル名にする
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                {
                    // クラウド同期フォルダ等、File.Replace が使えない環境向けのフォールバック
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            // 正常終了時は置換/移動済みで存在しない。例外発生時のみ tmp の残骸を掃除する
            // （掃除自体の失敗で元の例外を握り潰さないよう黙殺する）
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // tmp の残骸は次回保存時に上書きされるため無害
            }
        }
    }

    /// <summary>任意のパスから設定を読み込む（インポート用）</summary>
    /// <returns>読み込んだ設定。ファイルが無い・解析失敗時は null（呼び出し側がエラー表示を判断する）</returns>
    public TSettings? TryLoadFrom(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);

            // ユーザーの明示操作では失敗を可視化するため、既定値フォールバックはしない（Deserialize が null でも null を返す）
            return JsonSerializer.Deserialize<TSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
