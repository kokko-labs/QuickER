using MySqlConnector;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から MySQL の接続文字列を構築する</summary>
/// <remarks>
/// MySQL の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.AzureAd"/> が来ても同様）。SQL Server 固有の <c>TrustServerCertificate</c> /
/// Oracle 固有の <c>ServiceName</c> は無視する。
/// </remarks>
public static class MySqlConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    /// <param name="settings">共通接続設定</param>
    /// <param name="allowUserVariables">
    /// <c>@fk</c> 等のユーザー変数を使うプリペアド動的 SQL を実行する場合は <c>true</c>。
    /// スキーマ同期の Executor から呼ぶ場合のみ有効化する。
    /// </param>
    public static string Build(DbConnectionSettings settings, bool allowUserVariables = false)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            // ポート未指定時は MySQL の既定ポート 3306 を用いる
            Port = (uint)(settings.Port ?? 3306),
            Database = settings.Database,
            UserID = settings.UserId,
            Password = settings.Password,
            ConnectionTimeout = (uint)settings.ConnectTimeoutSeconds,
            ApplicationName = "QuickER",
            AllowUserVariables = allowUserVariables,
        };

        // TLS 要求水準は「未指定なら代入しない」＝キーワードを載せずドライバ既定に委ねる。
        // MySqlConnectionStringBuilder はドライバ既定と同じ値を代入しても SSL Mode= を書き出すため、
        // 未指定との区別は代入の有無でしか表せない（＝既存の接続文字列を変えないための唯一の方法）
        if (ToMySqlSslMode(settings.SslMode) is { } sslMode)
        {
            b.SslMode = sslMode;
        }

        return b.ConnectionString;
    }

    /// <summary>方言中立の TLS 要求水準を <see cref="MySqlSslMode"/> へ対応付ける（未指定は <c>null</c>）</summary>
    /// <remarks>
    /// <para>
    /// 未定義の列挙値は既定へ落とさず例外にする——黙って <c>null</c>（＝キーワードなし）へ倒すと、
    /// 検証を要求したつもりの設定が無言で「証明書を検証しない」既定へ降格する。
    /// </para>
    /// <para>
    /// <see cref="MySqlSslMode.Disabled"/> と <see cref="MySqlSslMode.None"/> は同一値のため、
    /// 書き出されるキーワードは <c>SSL Mode=None</c> になる。
    /// </para>
    /// </remarks>
    private static MySqlSslMode? ToMySqlSslMode(DbSslMode mode) =>
        mode switch
        {
            DbSslMode.Unspecified => null,
            DbSslMode.Disable => MySqlSslMode.Disabled,
            DbSslMode.Prefer => MySqlSslMode.Preferred,
            DbSslMode.Require => MySqlSslMode.Required,
            DbSslMode.VerifyCa => MySqlSslMode.VerifyCA,
            DbSslMode.VerifyFull => MySqlSslMode.VerifyFull,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown SSL mode for MySQL."
            ),
        };
}
