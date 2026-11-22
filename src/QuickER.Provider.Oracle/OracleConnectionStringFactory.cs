using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using QuickER.Provider;

namespace QuickER.Provider.Oracle;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から Oracle の接続文字列を構築する</summary>
/// <remarks>
/// <para>
/// Oracle の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.EntraId"/> が来ても同様）。
/// </para>
/// <para>
/// <c>DataSource</c> は EZConnect 形式 <c>host:port/service</c> で組み立てる。ポート未指定時は
/// 既定ポート（<see cref="DefaultPort"/>）を用いる。サービス名は <see cref="DbConnectionSettings.ServiceName"/> が
/// 非空ならそれを、空なら <see cref="DbConnectionSettings.Database"/> を用いる。
/// </para>
/// <para>
/// ODP.NET の文のキャッシュを使わない（<c>Self Tuning=false</c>。<c>Statement Cache Size</c> の既定は 0）。この接続文字列で開く接続
/// （取込・接続テスト・同期の実行器）が流すのは 1 回きりの DDL とカタログの読み取りで、キャッシュが効く場面が無い。既定のキャッシュは、
/// 違う文を流したプールのセッションに、上限（<c>OPEN_CURSORS</c>）の手前まで開いたままのカーソルを持つ。そのセッションで同時に開く
/// カーソル（DDL の再帰の SQL・PL/SQL のブロック）が残りを食うと、同期のスクリプトが途中で ORA-01000 になり、Oracle の DDL は暗黙に
/// コミットするので、途中まで適用されたまま残る。
/// </para>
/// </remarks>
public static class OracleConnectionStringFactory
{
    /// <summary>ポートを指定しないときに使う Oracle の既定ポート（<see cref="IDatabaseProvider.DefaultPort"/> もこの値を指す）</summary>
    public const int DefaultPort = 1521;

    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        var port = settings.Port ?? DefaultPort;
        // サービス名は ServiceName 優先、空なら Database 名で代用する
        var service = string.IsNullOrWhiteSpace(settings.ServiceName)
            ? settings.Database
            : settings.ServiceName;

        var b = new OracleConnectionStringBuilder
        {
            DataSource = string.Create(
                CultureInfo.InvariantCulture,
                $"{settings.Host}:{port}/{service}"
            ),
            UserID = settings.UserId,
            Password = settings.Password,
            ConnectionTimeout = settings.ConnectTimeoutSeconds,
            SelfTuning = false,
        };

        return b.ConnectionString;
    }
}
