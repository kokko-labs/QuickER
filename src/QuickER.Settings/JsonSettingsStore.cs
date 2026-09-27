using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

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
    /// <para>
    /// <b>書き込みの直前にディスクの JSON を読み直し、<typeparamref name="TSettings"/> が知らないキーを
    /// 引き継ぐ</b>（<see cref="CarryOverUnknownProperties"/>）。<see cref="Load"/> は
    /// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/> なので型が知らないキーを
    /// 読み捨てており、引き継がずに書くと<b>新しい版の QuickER が足したキーを、古い版で 1 度保存しただけで失う</b>。
    /// 設定ファイルは図ファイル（<c>DiagramDocument</c>）と違って前方互換の版番号を持たず、
    /// 「新しすぎるから読み込まない・上書きの前に確認する」（<c>IsNewerFormat</c>）に相当する仕組みが無いため、
    /// 黙って捨てると利用者の設定がただ消える。ここで引き継ぐのがその代わりになる。
    /// なお保存のたびにファイルを 1 回余計に読む（設定ファイルは数 KB なので通常は無視できるが、
    /// ロックされているときは <see cref="AtomicFile.TryReadAllText"/> の再試行ぶん呼び出し元を待たせる）。
    /// </para>
    /// <para>
    /// <b>読み直しが <see cref="FileReadStatus.Unavailable"/> のときは何も書かずに戻る。</b>
    /// 「あるのに今は読めない」ファイルを読めないまま上書きすると、そこにあった未知のキーを
    /// 黙って捨てることになり、この引き継ぎが塞ごうとしている穴そのものを開ける。
    /// </para>
    /// </remarks>
    public void Save(TSettings settings)
    {
        if (LastLoadUnavailable)
        {
            return;
        }

        var status = AtomicFile.TryReadAllText(SettingsPath, out var existingJson);

        // 読めないまま上書きすると、ディスクにある未知のキーを黙って捨てることになる
        if (status == FileReadStatus.Unavailable)
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        BackUpCorruptFileOnce();
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // ファイルが無い（Missing）ときは引き継ぐ相手がいないので、そのまま書く
        if (status == FileReadStatus.Loaded)
        {
            json = MergeUnknownProperties(json, existingJson!);
        }

        AtomicFile.WriteAllText(SettingsPath, json);
    }

    /// <summary>
    /// 書き出す JSON へ、ディスク上の JSON にしか無いキー（＝<typeparamref name="TSettings"/> が知らないキー）を
    /// 再帰的に引き継ぐ
    /// </summary>
    /// <remarks>
    /// <para>
    /// ディスクの JSON が壊れている・オブジェクトでない（配列や JSON リテラルの <c>null</c>）ときは、
    /// 引き継ぐ相手を決められないので<b>引き継がずにそのまま書く</b>（壊れたファイルの退避は
    /// <see cref="BackUpCorruptFileOnce"/> が別途行う）。引き継ぎの途中で失敗した場合も、
    /// 書き出すのは引き継ぎ前の文字列なので中途半端な内容にはならない。
    /// </para>
    /// <para>
    /// <b>既知のキー</b>は必ず<b>型のメタデータ</b>（<see cref="JsonSerializerOptions.GetTypeInfo(Type)"/> が返す
    /// <see cref="JsonTypeInfo.Properties"/>）から求める。<b>シリアライズ結果に現れたキーから求めてはいけない</b>。
    /// 現在の設定型では表に出ないが、次の 2 つが警告なく静かに壊れる。
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>辞書型のプロパティ:</b> JSON ではオブジェクトとして出るため、出力ベースだと中へ再帰してしまい、
    /// 利用者が消したエントリが「未知のキー」として復活する。型のメタデータなら
    /// <see cref="JsonTypeInfoKind.Dictionary"/> と分かるので、値ごと置き換わる。
    /// </description></item>
    /// <item><description>
    /// <b><see cref="JsonSerializerOptions.DefaultIgnoreCondition"/>（null を書かない設定）が将来入った場合:</b>
    /// 利用者が null にした既知のプロパティが出力に現れないため、出力ベースだとディスクの旧値が
    /// 「未知のキー」として復活する。
    /// </description></item>
    /// </list>
    /// <para>
    /// 再帰するのは <see cref="JsonTypeInfoKind.Object"/> のプロパティだけ。辞書・配列・値は
    /// 「型が知っているプロパティ」として<b>値ごと置き換える</b>（中へは入らない）。
    /// このため<b>配列の要素の中にある未知のキーは運べない</b>——要素の増減・並べ替えを考えると
    /// どの要素とどの要素を突き合わせるべきかが決まらず、引き継ぎの意味が定まらないため。
    /// これは既知の割り切りで、設定型に「要素ごとに版が増えていく配列」を足すときは注意すること。
    /// </para>
    /// <para>
    /// キー名の比較は<b>大文字小文字を区別する</b>（<see cref="StringComparer.Ordinal"/>）。
    /// 読み込み側（<see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> の既定は <c>false</c>）と
    /// 揃えないと、読み込みでは別のキーとして無視された綴りを、ここだけ既知と見なして取りこぼす。
    /// </para>
    /// </remarks>
    /// <param name="serialized">書き出そうとしている JSON（<paramref name="existingJson"/> より優先する）</param>
    /// <param name="existingJson">ディスクにある JSON</param>
    /// <returns>未知のキーを引き継いだ JSON。引き継げなかったときは <paramref name="serialized"/> のまま</returns>
    private static string MergeUnknownProperties(string serialized, string existingJson)
    {
        try
        {
            if (JsonNode.Parse(existingJson) is not JsonObject existing)
            {
                return serialized;
            }

            if (JsonNode.Parse(serialized) is not JsonObject target)
            {
                return serialized;
            }

            CarryOverUnknownProperties(
                target,
                existing,
                JsonOptions.GetTypeInfo(typeof(TSettings))
            );

            return target.ToJsonString(JsonOptions);
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or ArgumentException
                        or NotSupportedException
                        or InvalidOperationException
            )
        {
            // 壊れた JSON・キーの重複（JsonObject は列挙時に ArgumentException を投げる）など。
            // 引き継げないだけで保存そのものは成立するので、書き出す内容は変えずに続ける
            return serialized;
        }
    }

    /// <summary>
    /// <paramref name="existing"/> にしか無いキーを <paramref name="target"/> へ写し、
    /// 既知のオブジェクト型プロパティだけ 1 段深く同じ処理を行う
    /// </summary>
    /// <param name="target">書き出す JSON オブジェクト（この場で書き換える）</param>
    /// <param name="existing">ディスク上の JSON オブジェクト</param>
    /// <param name="typeInfo">両者に対応する設定型のメタデータ</param>
    private static void CarryOverUnknownProperties(
        JsonObject target,
        JsonObject existing,
        JsonTypeInfo typeInfo
    )
    {
        var known = BuildKnownProperties(typeInfo);

        foreach (var entry in existing)
        {
            if (!known.TryGetValue(entry.Key, out var nestedTypeInfo))
            {
                // 型が知らないキー＝読み込みで捨てられた値。書き出す側へそのまま持ち越す
                target[entry.Key] = entry.Value?.DeepClone();

                continue;
            }

            // 型が知っているキーは値ごと置き換わる（＝何もしない）。
            // 中へ再帰するのはオブジェクト型のときだけで、辞書・配列・値は置き換えたままにする
            if (
                nestedTypeInfo is not null
                && entry.Value is JsonObject existingChild
                && target[entry.Key] is JsonObject targetChild
            )
            {
                CarryOverUnknownProperties(targetChild, existingChild, nestedTypeInfo);
            }
        }
    }

    /// <summary>設定型が知っているキーの一覧を作る（値は「中へ再帰できるならその型のメタデータ」）</summary>
    /// <remarks>
    /// 値が <c>null</c> なのは辞書・配列・値など<b>再帰しない</b>プロパティ。
    /// 「キーを知っているか」と「中へ入ってよいか」は別の問いなので、1 つの辞書で両方を答える。
    /// </remarks>
    private static Dictionary<string, JsonTypeInfo?> BuildKnownProperties(JsonTypeInfo typeInfo)
    {
        var known = new Dictionary<string, JsonTypeInfo?>(StringComparer.Ordinal);

        foreach (var property in typeInfo.Properties)
        {
            var propertyTypeInfo = JsonOptions.GetTypeInfo(property.PropertyType);

            known[property.Name] =
                propertyTypeInfo.Kind == JsonTypeInfoKind.Object ? propertyTypeInfo : null;
        }

        return known;
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
    /// <remarks>
    /// 書き込みが原子的である理由は <see cref="Save"/> と同じ。ただし<b>未知のキーの引き継ぎは行わない</b>——
    /// エクスポートは「いまの設定を書き出す」操作で、書き出し先にたまたまあった別物の内容を
    /// 混ぜる意味が無いため（引き継ぎが要るのは、同じファイルを読んで書き戻す <see cref="Save"/> だけ）。
    /// </remarks>
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
