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

    /// <summary>既定の保存先（%LOCALAPPDATA%\QuickER）で設定ストアを生成する</summary>
    /// <remarks>
    /// ローミング（<c>%APPDATA%</c>）ではなくローカル（<c>%LOCALAPPDATA%</c>）を使う。ここに置くのは
    /// この PC に固有の作業状態（ウィンドウ状態・直近ファイル・DPAPI で保護した秘密）であり、
    /// ローミングプロファイルでドメイン内の他端末へ複製されると、復号できない秘密や別端末のパスを
    /// 運ぶことになるため（Windows のローミング指針どおり、機械固有のものはローカルへ置く）。
    /// </remarks>
    /// <param name="fileName">設定ファイル名（例: <c>ai-settings.json</c>）</param>
    public JsonSettingsStore(string fileName)
        : this(
            fileName,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

    /// <summary>壊れていた設定ファイルの退避先（<c>{設定ファイル}.corrupt</c>）</summary>
    /// <remarks>
    /// 退避するのは<b>最初の 1 回だけ</b>（既に存在すれば上書きしない）。残したいのは
    /// 「最後に正しく読めていた状態にいちばん近いもの」＝最も古い退避で、上書きし続けると
    /// 既定値で上書き保存したあとの無害なファイルへ入れ替わってしまう。
    /// </remarks>
    public string CorruptBackupPath => SettingsPath + ".corrupt";

    /// <summary>直近の <see cref="Load"/> で設定ファイルを読み取れなかったか（＝保存を抑止しているか）</summary>
    /// <remarks>
    /// <c>true</c> の間、<see cref="Save"/> は<b>何も書かずに戻る</b>。読めなかった内容を
    /// 既定値で上書きすると、利用者の設定が丸ごと消えるため。<see cref="Load"/> のたびに更新されるので、
    /// 読み取りが再び成功すれば抑止は自動的に解ける（自己修復）。
    /// </remarks>
    public bool LastLoadUnavailable { get; private set; }

    /// <summary>直近の <see cref="Load"/> で設定ファイルを JSON として解釈できなかったか</summary>
    /// <remarks>
    /// <c>true</c> のとき、次の <see cref="Save"/> は上書きの前に
    /// <see cref="CorruptBackupPath"/> へ退避する（既に退避があれば何もしない）。
    /// 読み取り自体は成功しているので保存は抑止しない（＝既定値で書き直して正常化する）。
    /// </remarks>
    public bool LastLoadCorrupt { get; private set; }

    /// <summary>設定を読み込む（ファイルが無い・解析失敗時は既定値を返す）</summary>
    /// <remarks>
    /// <para>
    /// <b>起動を妨げないため例外は投げない</b>が、失敗の種類は
    /// <see cref="LastLoadUnavailable"/> / <see cref="LastLoadCorrupt"/> として残す。
    /// とくに「ファイルはあるのに読めなかった」を既定値で黙って飲み込むと、保存が
    /// 「読み込み → 変更 → 書き込み」である以上、次の保存でその既定値が確定して
    /// 利用者の設定が全消しになる。ここで印を立て、<see cref="Save"/> が書かないようにする。
    /// </para>
    /// <para>
    /// 「無い（初回起動）」だけは本当に空なので、従来どおり既定値で何の印も立てない。
    /// </para>
    /// </remarks>
    public TSettings Load()
    {
        var status = AtomicFile.TryReadAllText(SettingsPath, out var json);

        // 読めなかった＝中身は無事。既定値で起動しつつ、その既定値で上書きしないよう保存を抑止する
        LastLoadUnavailable = status == FileReadStatus.Unavailable;

        if (status != FileReadStatus.Loaded)
        {
            LastLoadCorrupt = false;

            return new TSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<TSettings>(json!, JsonOptions);

            // JSON リテラルの null は設定として解釈できない（＝壊れている扱い）
            LastLoadCorrupt = settings is null;

            return settings ?? new TSettings();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // 破損ファイル等で起動を妨げないよう既定値へフォールバックする（次の保存で .corrupt へ退避する）
            LastLoadCorrupt = true;

            return new TSettings();
        }
    }

    /// <summary>設定を保存する（保存先フォルダが無ければ作成する）</summary>
    /// <remarks>
    /// <para>
    /// 書き込みは <see cref="AtomicFile.WriteAllText"/> による原子的書き込み。素の
    /// <c>File.WriteAllText</c> は既存ファイルを切り詰めてから書くため、途中でプロセスが落ちると
    /// 設定ファイルが破損した JSON になる。読込側（<see cref="Load"/>）は破損を握り潰して既定値へ
    /// フォールバックするため、直後の read-modify-write 保存で他のセクションが巻き添えで消失する
    /// 連鎖が起きる。これを防ぐのがここでの原子的書き込みの目的。
    /// </para>
    /// <para>
    /// <b>直近の読み取りが失敗していたときは何も書かずに戻る</b>（<see cref="LastLoadUnavailable"/>）。
    /// 呼び出し側が持っているのは読めなかったファイルの中身ではなく既定値なので、そのまま書くと
    /// 利用者の設定を既定値で塗り潰すことになる。書かずに諦めればファイルの中身は前のままで、
    /// 次に読み取りが成功した時点で抑止も解ける。
    /// </para>
    /// </remarks>
    public void Save(TSettings settings)
    {
        if (LastLoadUnavailable)
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        BackUpCorruptFileOnce();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFile.WriteAllText(SettingsPath, json);
    }

    /// <summary>壊れていた設定ファイルを、上書きしてしまう前に 1 回だけ退避する</summary>
    /// <remarks>
    /// 退避はベストエフォート（失敗しても保存は続ける）。保存そのものを止めてまで守る価値は無い一方、
    /// 手掛かりを残さず上書きすると「なぜ設定が初期化されたのか」を後から調べる手段が消える。
    /// </remarks>
    private void BackUpCorruptFileOnce()
    {
        if (!LastLoadCorrupt)
        {
            return;
        }

        try
        {
            if (File.Exists(SettingsPath) && !File.Exists(CorruptBackupPath))
            {
                File.Copy(SettingsPath, CorruptBackupPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 退避できなくても保存は続ける（退避は手掛かりを残すための付随処理）
        }
    }

    /// <summary>設定を任意のパスへ保存する（エクスポート用。親フォルダが無ければ作成する）</summary>
    /// <remarks>書き込みが原子的である理由は <see cref="Save"/> と同じ</remarks>
    public void SaveTo(string path, TSettings settings)
    {
        var folder = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicFile.WriteAllText(path, json);
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
