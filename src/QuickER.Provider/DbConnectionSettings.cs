using System.Text.Json.Serialization;

namespace QuickER.Provider;

/// <summary>DB 接続の認証方式（方言横断の共通表現）</summary>
public enum DbAuthMode
{
    /// <summary>Windows 統合認証（SQL Server のみ）</summary>
    Windows,

    /// <summary>ユーザー名 / パスワード認証</summary>
    UsernamePassword,

    /// <summary>Azure AD 認証（SQL Server のみ）</summary>
    AzureAd,
}

/// <summary>DB 接続の TLS（通信路の暗号化）要求水準（方言横断の共通表現）</summary>
/// <remarks>
/// <para>
/// 既定は <see cref="Unspecified"/>＝接続文字列へ TLS のキーワードを載せず、ドライバ既定に委ねる。
/// PostgreSQL（Npgsql）・MySQL（MySqlConnector）のドライバ既定はいずれも
/// <see cref="Prefer"/> 相当（暗号化できるなら暗号化するが<b>サーバー証明書を検証しない</b>）で、
/// 中間者攻撃は検知できない。検証まで求めるなら <see cref="VerifyCa"/> / <see cref="VerifyFull"/> を選ぶ。
/// </para>
/// <para>
/// 対応するのは、この水準をそのまま表す接続文字列キーワードを持つ PostgreSQL / MySQL のみ。
/// SQL Server は別の面（<see cref="DbConnectionSettings.TrustServerCertificate"/>）を持ち、
/// Oracle / SQLite はこの設定を無視する。
/// </para>
/// <para>
/// Oracle を対象外にしたのは、管理対象ドライバの TLS が<b>この列挙と同じ形をしていない</b>ため。
/// 切替は接続文字列キーワードではなく DataSource のプロトコル接頭辞
/// （<c>tcps://host:2484/service</c>）で、(1) 既定のリスナーポートも 1521 から変える必要があり
/// 「1 項目」で閉じない、(2) TCPS は常に CA 検証を伴うため <see cref="Require"/>
/// （暗号化するが検証しない）に対応する形が無い、(3) tcp か tcps かの二択で
/// <see cref="Prefer"/>（交渉して可能なら暗号化）も表せない——という 3 点の非対称がある。
/// Oracle の TLS 設定方法は docs/database.md の接続節に記載する。
/// </para>
/// <para>
/// <b>JSON へは名前で永続化する</b>（<see cref="JsonStringEnumConverter{TEnum}"/>）。整数で保存すると、
/// 将来この列挙の途中へメンバーを挿入した瞬間、保存済みの設定が無言で別の水準として読み直される
/// （例: <c>5</c> が <see cref="VerifyFull"/> から <see cref="VerifyCa"/> へずれる）——
/// この機能が防ごうとしている「検証を要求したつもりが降格する」事故そのものになる。
/// 既存の整数値で書かれたファイルは、System.Text.Json の既定（<c>allowIntegerValues: true</c>）で
/// 従来どおり読める。
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DbSslMode>))]
public enum DbSslMode
{
    /// <summary>未指定（接続文字列へキーワードを載せず、ドライバ既定に委ねる）</summary>
    Unspecified,

    /// <summary>暗号化しない（平文接続）</summary>
    Disable,

    /// <summary>可能なら暗号化する（サーバー証明書は検証しない）</summary>
    Prefer,

    /// <summary>暗号化を必須にする（サーバー証明書は検証しない）</summary>
    Require,

    /// <summary>暗号化を必須にし、証明書の発行者（CA）まで検証する</summary>
    VerifyCa,

    /// <summary>暗号化を必須にし、CA に加えてホスト名の一致まで検証する</summary>
    VerifyFull,
}

/// <summary>DB 接続の共通設定。方言固有フィールドも単純さ優先でここに持つ</summary>
public class DbConnectionSettings
{
    /// <summary>接続先ホスト（SQL Server ではサーバー名。インスタンス表記可）</summary>
    public string Host { get; set; } = "";

    /// <summary>接続ポート。null の場合は方言の既定ポートを用いる</summary>
    public int? Port { get; set; }

    /// <summary>データベース名</summary>
    public string Database { get; set; } = "";

    /// <summary>認証方式（Windows / AzureAd は SQL Server のみ）</summary>
    public DbAuthMode AuthMode { get; set; } = DbAuthMode.Windows;

    /// <summary>ユーザー名（<see cref="DbAuthMode.UsernamePassword"/> 等で使用）</summary>
    public string UserId { get; set; } = "";

    /// <summary>パスワード（<see cref="DbAuthMode.UsernamePassword"/> 等で使用）</summary>
    public string Password { get; set; } = "";

    /// <summary>サーバー証明書を信頼するか（SQL Server 固有）</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>TLS（通信路の暗号化）の要求水準（PostgreSQL / MySQL 固有）</summary>
    /// <remarks>
    /// 既定の <see cref="DbSslMode.Unspecified"/> は接続文字列へキーワードを載せない＝
    /// ドライバ既定のまま（この設定を追加する前と接続文字列がバイト単位で同一）。
    /// 水準ごとの意味は <see cref="DbSslMode"/> を参照。
    /// </remarks>
    public DbSslMode SslMode { get; set; } = DbSslMode.Unspecified;

    /// <summary>接続タイムアウト（秒）</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>コマンド実行タイムアウト（秒）</summary>
    /// <remarks>
    /// スキーマ取込・スキーマ同期の各 SQL に適用する（接続確立までの時間である
    /// <see cref="ConnectTimeoutSeconds"/> とは別物）。<c>0</c> は ADO.NET の規約どおり「無制限」を意味し、
    /// 負値は不正（<see cref="DbCommands.Create"/> が <see cref="ArgumentOutOfRangeException"/> を投げる）。
    /// 接続文字列キーワードには載せない——方言によってキーワードの有無・名前が割れるため、
    /// コマンド生成時に <see cref="DbCommands"/> 経由で設定する。
    /// </remarks>
    public int CommandTimeoutSeconds { get; set; } = DbCommands.DefaultTimeoutSeconds;

    /// <summary>サービス名（Oracle 固有・将来使用）</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>データベースファイルのパス（SQLite 固有。ファイル型 DB はサーバー系フィールドの代わりにこれを使う）</summary>
    public string FilePath { get; set; } = "";
}
