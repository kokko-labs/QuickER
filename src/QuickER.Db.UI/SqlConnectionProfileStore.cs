using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickER.Settings;

namespace QuickER.Db.UI;

/// <summary>connections.json のルート（登録プロファイル一覧＋前回接続）</summary>
public sealed class SqlConnectionData
{
    /// <summary>登録済みの接続プロファイル一覧</summary>
    public List<SqlConnectionProfile> Profiles { get; set; } = new();

    /// <summary>前回使用した接続情報（未使用時は null）</summary>
    public SqlConnectionProfile? LastUsed { get; set; }
}

/// <summary>接続情報ファイルを読み取れず、保存を中止したことを表す例外</summary>
/// <remarks>
/// <para>
/// 接続プロファイルの保存はどれも「読み込み → 変更 → 書き込み」で、読めなかったときに空データへ
/// フォールバックすると、その空データが保存先へ書き戻って<b>登録済みのプロファイルが全件消える</b>。
/// そのため読めなかったときは<b>何も書かずに</b>この例外を投げる——投げられた側から見た意味は
/// 「保存は行われていない・ファイルの中身は操作前のまま」で、失ったものは何も無い。
/// </para>
/// <para>
/// 原因は別プロセスの保存中・ウイルス対策ソフトの一瞬のロック・権限不足などで、多くは一過性。
/// 呼び出し側（UI）は失敗を利用者へ知らせてやり直しを促すか、<b>ついでの記録</b>であれば
/// 黙って諦めてよい（書かなかっただけで何も失われないため）。
/// </para>
/// </remarks>
public sealed class ConnectionProfileStoreUnavailableException : InvalidOperationException
{
    /// <summary>読み取れなかった接続情報ファイルのパスを指定して例外を生成する</summary>
    /// <param name="path">読み取れなかった接続情報ファイルのパス</param>
    public ConnectionProfileStoreUnavailableException(string path)
        : base(
            $"Could not read the connection profile file '{path}', so saving was aborted to avoid losing the saved profiles."
        )
    {
        ConnectionsPath = path;
    }

    /// <summary>読み取れなかった接続情報ファイルのパス</summary>
    public string ConnectionsPath { get; }
}

/// <summary>SQL 接続プロファイルを JSON ファイルへ保存・読込するストア</summary>
/// <remarks>
/// パスワードはプロファイル本体とは分離し、Windows DPAPI（CurrentUser スコープ）で
/// 暗号化した別ファイルへ保存する
/// </remarks>
public class SqlConnectionProfileStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>各ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>パスワードを DPAPI で暗号化するかどうか（テストでは平文保存のため false）</summary>
    private readonly bool _useDpapi;

    /// <summary>接続情報 JSON（プロファイル一覧＋前回接続）のファイルパス</summary>
    public string ConnectionsPath => Path.Combine(_folder, "connections.json");

    /// <summary>パスワード暗号ファイルの格納フォルダ</summary>
    public string SecretsFolder => Path.Combine(_folder, "connection-secrets");

    /// <summary>既定（<c>%LOCALAPPDATA%\QuickER</c>・DPAPI 有効）のストアを生成する</summary>
    /// <remarks>
    /// 保存先がローミング（<c>%APPDATA%</c>）ではなくローカルである理由は
    /// <see cref="JsonSettingsStore{TSettings}"/> の既定コンストラクタを参照（DPAPI で保護した
    /// パスワードは他端末では復号できないため、複製されて困る筆頭がここ）。
    /// </remarks>
    public SqlConnectionProfileStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickER"
            ),
            true
        ) { }

    /// <summary>保存先フォルダと DPAPI 利用可否を指定してストアを生成する（テスト用）</summary>
    /// <param name="folder">保存先ディレクトリ</param>
    /// <param name="useDpapi">DPAPI でパスワードを暗号化するか <c>false</c> なら平文保存（テスト用）</param>
    public SqlConnectionProfileStore(string folder, bool useDpapi)
    {
        _folder = folder;
        _useDpapi = useDpapi;
    }

    /// <summary>接続情報の読み取り結果の分類</summary>
    /// <remarks>
    /// 表示（読み取り専用の公開 API）はどの分類でも空データで続行してよいが、
    /// 保存（read-modify-write）は <see cref="Unavailable"/> のときだけは中止しなければならない
    /// （読めなかった中身を空データで上書きすると、登録済みプロファイルが全件消える）。
    /// </remarks>
    private enum ConnectionDataStatus
    {
        /// <summary>読み取りと解析に成功した</summary>
        Loaded,

        /// <summary>ファイルが無い＝保存されたものが無い（本当に空）</summary>
        Missing,

        /// <summary>読めたが JSON として解釈できない（破損・旧配列形式など）</summary>
        Corrupt,

        /// <summary>ファイルはあるが読み取れなかった（中身は無事）</summary>
        Unavailable,
    }

    /// <summary>直近の書き込み用読み取りで壊れた JSON を見たか（次の保存で <c>.corrupt</c> へ退避する）</summary>
    private bool _pendingCorruptBackup;

    /// <summary>壊れていた接続情報ファイルの退避先（<c>connections.json.corrupt</c>）</summary>
    /// <remarks>
    /// 退避は<b>最初の 1 回だけ</b>（既に存在すれば上書きしない）。残したいのは
    /// 「最後に正しく読めていた状態にいちばん近いもの」＝最も古い退避で、上書きし続けると
    /// 空データで書き直したあとの無害なファイルへ入れ替わってしまう。
    /// </remarks>
    public string CorruptBackupPath => ConnectionsPath + ".corrupt";

    /// <summary>接続情報（プロファイル一覧＋前回接続）を読み込み、失敗の種類まで返す</summary>
    /// <remarks>
    /// 分類の意味は <see cref="ConnectionDataStatus"/> を参照。「ファイルが無い」と
    /// 「あるのに読めない」を区別するのが要で、区別を捨てると保存側が
    /// 読めなかった中身を空データで塗り潰す（＝プロファイルの全消失）。
    /// </remarks>
    private (SqlConnectionData Data, ConnectionDataStatus Status) ReadData()
    {
        var status = AtomicFile.TryReadAllText(ConnectionsPath, out var json);

        if (status == FileReadStatus.Unavailable)
        {
            return (new SqlConnectionData(), ConnectionDataStatus.Unavailable);
        }

        if (status == FileReadStatus.Missing)
        {
            return (new SqlConnectionData(), ConnectionDataStatus.Missing);
        }

        try
        {
            var data = JsonSerializer.Deserialize<SqlConnectionData>(json!, JsonOpts);

            return data is null
                ? (new SqlConnectionData(), ConnectionDataStatus.Corrupt)
                : (data, ConnectionDataStatus.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // 破損 JSON・旧配列形式等で UI を妨げないよう空データへフォールバックする
            return (new SqlConnectionData(), ConnectionDataStatus.Corrupt);
        }
    }

    /// <summary>接続情報（プロファイル一覧＋前回接続）を読み込む（読み取り失敗時は空データ）</summary>
    /// <remarks>
    /// <b>表示専用。</b>読めなかったときも空データで続行して UI を止めない（並べるものが無いだけで
    /// 何も失われない）。保存の起点にしてはいけない——そのときは
    /// <see cref="LoadDataForWrite"/> を使う。
    /// </remarks>
    private SqlConnectionData LoadData() => ReadData().Data;

    /// <summary>保存（read-modify-write）の起点として接続情報を読み込む</summary>
    /// <remarks>
    /// 読み取れなかったときは<b>何も書かせず</b>
    /// <see cref="ConnectionProfileStoreUnavailableException"/> を投げる。ここで空データを返すと、
    /// 呼び出し側がそれを変更して保存し、登録済みプロファイル・前回接続が全件消える。
    /// あわせて「壊れた JSON を読んだ」ことを覚え、次の <see cref="SaveData"/> が
    /// 上書きの前に退避できるようにする。
    /// </remarks>
    /// <exception cref="ConnectionProfileStoreUnavailableException">接続情報ファイルを読み取れなかった</exception>
    private SqlConnectionData LoadDataForWrite()
    {
        var (data, status) = ReadData();

        if (status == ConnectionDataStatus.Unavailable)
        {
            throw new ConnectionProfileStoreUnavailableException(ConnectionsPath);
        }

        _pendingCorruptBackup = status == ConnectionDataStatus.Corrupt;

        return data;
    }

    /// <summary>接続情報（プロファイル一覧＋前回接続）を保存する</summary>
    /// <remarks>
    /// 書き込みは <see cref="AtomicFile.WriteAllText"/> による原子的書き込み。素の
    /// <c>File.WriteAllText</c> は既存ファイルを切り詰めてから書くため、途中でプロセスが落ちると
    /// connections.json が破損した JSON になり、読込側（<see cref="LoadData"/>）が空データへ
    /// フォールバックした状態のまま次の read-modify-write 保存（<see cref="SaveAll"/> /
    /// <see cref="SaveLastUsed"/>）で他のプロファイル・前回接続情報が巻き添えで消失する。
    /// これを防ぐのがここでの原子的書き込みの目的（なお DPAPI 暗号ファイルの書き出しは
    /// <see cref="SaveSecret(string, string)"/> の単純書き込みのままで、本メソッドの対象外）。
    /// <see cref="SaveSecret(string, string)"/> が失敗すると <c>SavePassword=true</c> だけが確定するが、
    /// 読み出しは空パスワード＝再入力へ安全に劣化するため、原子性の対象へは含めない（意図的な割り切り）。
    /// </remarks>
    private void SaveData(SqlConnectionData data)
    {
        Directory.CreateDirectory(_folder);
        BackUpCorruptFileOnce();
        var json = JsonSerializer.Serialize(data, JsonOpts);
        AtomicFile.WriteAllText(ConnectionsPath, json);
    }

    /// <summary>壊れていた接続情報ファイルを、上書きしてしまう前に 1 回だけ退避する</summary>
    /// <remarks>
    /// 退避はベストエフォート（失敗しても保存は続ける）。保存そのものを止めてまで守る価値は無い一方、
    /// 手掛かりを残さず上書きすると「なぜ登録済みの接続が消えたのか」を後から調べる手段が消える。
    /// </remarks>
    private void BackUpCorruptFileOnce()
    {
        if (!_pendingCorruptBackup)
        {
            return;
        }

        try
        {
            if (File.Exists(ConnectionsPath) && !File.Exists(CorruptBackupPath))
            {
                File.Copy(ConnectionsPath, CorruptBackupPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 退避できなくても保存は続ける（退避は手掛かりを残すための付随処理）
        }
    }

    /// <summary>すべてのプロファイルを名前順で読み込む（読み取り失敗時は空一覧を返す）</summary>
    public List<SqlConnectionProfile> LoadAll() =>
        LoadData().Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>前回使用した接続情報を読み込む</summary>
    /// <remarks>パスワードは <see cref="SqlConnectionProfile.SavePassword"/> が有効な場合のみ復号して返す</remarks>
    public (SqlConnectionProfile Profile, string Password)? LoadLastUsed()
    {
        var profile = LoadData().LastUsed;

        if (profile is null)
        {
            return null;
        }

        var password = profile.SavePassword ? LoadSecret(LastConnectionSecretPath()) : string.Empty;
        return (profile, password);
    }

    /// <summary>すべてのプロファイルを保存する（前回接続情報は温存する）</summary>
    /// <exception cref="ConnectionProfileStoreUnavailableException">
    /// 接続情報ファイルを読み取れず保存を中止した（ファイルの中身は操作前のまま）
    /// </exception>
    public void SaveAll(IEnumerable<SqlConnectionProfile> profiles)
    {
        // read-modify-write で Profiles のみ差し替え、LastUsed を消さない
        var data = LoadDataForWrite();
        data.Profiles = profiles.ToList();
        SaveData(data);
    }

    /// <summary>前回使用した接続情報を保存する</summary>
    /// <remarks>
    /// 登録済みプロファイル一覧とは別セクションで管理し、次回ダイアログ表示時の初期値復元に用いる。
    /// <see cref="SqlConnectionProfile.SavePassword"/> は <see cref="NormalizeSavePassword"/> が正規化する
    /// （渡したインスタンスも書き換わる）。
    /// <b>読み取りに失敗したときは渡したインスタンスも書き換えない</b>——保存を中止した以上、
    /// 呼び出し側の状態にも副作用を残さないため、正規化より先に読み取りを済ませる。
    /// </remarks>
    /// <exception cref="ConnectionProfileStoreUnavailableException">
    /// 接続情報ファイルを読み取れず保存を中止した（ファイルの中身は操作前のまま）
    /// </exception>
    public void SaveLastUsed(SqlConnectionProfile profile, string password)
    {
        // read-modify-write で LastUsed のみ差し替え、Profiles を消さない
        var data = LoadDataForWrite();
        var hasPassword = NormalizeSavePassword(profile, password);
        data.LastUsed = profile;
        SaveData(data);

        // パスワード保存が無効化された場合は残存する暗号ファイルを確実に削除する
        if (hasPassword)
        {
            SaveSecret(LastConnectionSecretPath(), password);
        }
        else
        {
            DeleteSecret(LastConnectionSecretPath());
        }
    }

    /// <summary>
    /// 保存する <see cref="SqlConnectionProfile.SavePassword"/> を、実際に暗号ファイルを持つかへ揃える
    /// （揃えた結果＝暗号ファイルを書くかどうかを返す）
    /// </summary>
    /// <remarks>
    /// 空のパスワードでは暗号ファイルを書かないため、フラグだけを立てて保存すると
    /// 「保存済みと表示されるのに復元するものが無い」状態が残る。JSON のフラグと暗号ファイルの有無は
    /// 常に一致させる（読み出し側はフラグを見て復号するかを決めており、食い違うと
    /// 保存したつもりのパスワードが黙って空で戻る）。<b>渡されたインスタンスを書き換える</b>——
    /// 保存した内容と呼び出し側が持つプロファイルを食い違わせないため。
    /// </remarks>
    private static bool NormalizeSavePassword(SqlConnectionProfile profile, string password)
    {
        profile.SavePassword = profile.SavePassword && !string.IsNullOrEmpty(password);
        return profile.SavePassword;
    }

    /// <summary>プロファイルを 1 件追加または更新し、必要に応じてパスワードを暗号化保存する</summary>
    /// <param name="profile">保存対象 Id が既存と一致すれば上書き、なければ追加する</param>
    /// <param name="password">パスワード <see cref="SqlConnectionProfile.SavePassword"/> が <c>true</c> の場合のみ保存する</param>
    /// <param name="replacedProfileId">
    /// 同時に削除するプロファイルの Id（名前の変更で既存を置き換えるときに渡す。<c>null</c> なら削除しない）
    /// </param>
    /// <remarks>
    /// <see cref="SqlConnectionProfile.SavePassword"/> は <see cref="NormalizeSavePassword"/> が正規化する
    /// （渡したインスタンスも書き換わる）。<b>読み取りに失敗したときは渡したインスタンスも書き換えない</b>。
    /// <para>
    /// 読み取りは 1 回だけ（<see cref="LoadDataForWrite"/>）。読み取ってから書くまでに 2 回読むと、
    /// 1 回目が失敗して 2 回目が成功した瞬間に「空の一覧＋既存の前回接続」を書いてしまう。
    /// </para>
    /// <para>
    /// <paramref name="replacedProfileId"/> を <see cref="Delete"/> の別呼び出しで済ませてはいけない。
    /// 2 回の読み書きになり、2 回目が読めなかったときに<b>置き換え先だけが消えて対象は変わらない</b>
    /// 状態が残る（名前の変更でこれが起きると、一覧には消えたはずのプロファイルが居座る）。
    /// </para>
    /// </remarks>
    /// <exception cref="ConnectionProfileStoreUnavailableException">
    /// 接続情報ファイルを読み取れず保存を中止した（ファイルの中身は操作前のまま）
    /// </exception>
    public void Upsert(
        SqlConnectionProfile profile,
        string password,
        Guid? replacedProfileId = null
    )
    {
        var data = LoadDataForWrite();
        var hasPassword = NormalizeSavePassword(profile, password);

        if (replacedProfileId is { } replacedId && replacedId != profile.Id)
        {
            data.Profiles.RemoveAll(p => p.Id == replacedId);
        }

        var idx = data.Profiles.FindIndex(p => p.Id == profile.Id);

        if (idx >= 0)
        {
            data.Profiles[idx] = profile;
        }
        else
        {
            data.Profiles.Add(profile);
        }

        SaveData(data);

        // 置き換えたプロファイルの暗号ファイルは、JSON の書き込みが成功したあとで消す
        // （先に消すと、書き込みに失敗したとき「一覧には居るのにパスワードだけ無い」状態が残る）
        if (replacedProfileId is { } removedId && removedId != profile.Id)
        {
            DeleteSecret(removedId);
        }

        // 保存無効・空パスワード時は残存する暗号ファイルを削除する
        if (hasPassword)
        {
            SaveSecret(profile.Id, password);
        }
        else
        {
            DeleteSecret(profile.Id);
        }
    }

    /// <summary>指定 ID のプロファイルと暗号化パスワードを削除する</summary>
    /// <remarks>読み取りが 1 回だけである理由は <see cref="Upsert"/> と同じ</remarks>
    /// <exception cref="ConnectionProfileStoreUnavailableException">
    /// 接続情報ファイルを読み取れず削除を中止した（ファイルの中身は操作前のまま・暗号ファイルも消さない）
    /// </exception>
    public void Delete(Guid id)
    {
        var data = LoadDataForWrite();
        data.Profiles.RemoveAll(p => p.Id == id);
        SaveData(data);
        DeleteSecret(id);
    }

    /// <summary>指定 ID のプロファイルに紐づくパスワードを復号して返す（無ければ空文字）</summary>
    public string LoadPassword(Guid id)
    {
        var path = SecretPath(id);
        return LoadSecret(path);
    }

    /// <summary>暗号ファイルを復号してパスワード文字列を返す（DPAPI 無効時は平文として読む）</summary>
    private string LoadSecret(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);

            if (_useDpapi)
            {
                var data = ProtectedData.Unprotect(
                    bytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(data);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // 別ユーザーで暗号化された等で復号失敗した場合は空文字を返す
            return string.Empty;
        }
    }

    /// <summary>プロファイル ID に対応する暗号ファイルへパスワードを保存する</summary>
    private void SaveSecret(Guid id, string password) => SaveSecret(SecretPath(id), password);

    /// <summary>指定パスへパスワードを保存する（DPAPI 有効時は暗号化する）</summary>
    private void SaveSecret(string path, string password)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var raw = Encoding.UTF8.GetBytes(password);
        var bytes = _useDpapi
            ? ProtectedData.Protect(
                raw,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            )
            : raw;
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>プロファイル ID に対応する暗号ファイルを削除する</summary>
    private void DeleteSecret(Guid id) => DeleteSecret(SecretPath(id));

    /// <summary>指定パスの暗号ファイルが存在すれば削除する</summary>
    private void DeleteSecret(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>プロファイル ID から暗号ファイルのパスを生成する</summary>
    private string SecretPath(Guid id) => Path.Combine(SecretsFolder, id.ToString("N") + ".dat");

    /// <summary>前回接続情報用の暗号ファイルのパスを生成する</summary>
    private string LastConnectionSecretPath() => Path.Combine(SecretsFolder, "last-connection.dat");
}
