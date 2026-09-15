using Npgsql;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から PostgreSQL の接続文字列を構築する</summary>
/// <remarks>
/// PostgreSQL の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.AzureAd"/> が来ても同様）。SQL Server 固有の <c>TrustServerCertificate</c> /
/// <c>ServiceName</c> は無視する。
/// </remarks>
public static class PostgreSqlConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            // ポート未指定時は PostgreSQL の既定ポート 5432 を用いる
            Port = settings.Port ?? 5432,
            Database = settings.Database,
            Username = settings.UserId,
            Password = settings.Password,
            Timeout = settings.ConnectTimeoutSeconds,
            ApplicationName = "QuickER",
        };

        // TLS 要求水準は「未指定なら代入しない」＝キーワードを載せずドライバ既定に委ねる。
        // NpgsqlConnectionStringBuilder はドライバ既定と同じ値を代入しても SSL Mode= を書き出すため、
        // 未指定との区別は代入の有無でしか表せない（＝既存の接続文字列を変えないための唯一の方法）
        if (ToNpgsqlSslMode(settings.SslMode) is { } sslMode)
        {
            b.SslMode = sslMode;
        }

        return b.ConnectionString;
    }

    /// <summary>方言中立の TLS 要求水準を Npgsql の <see cref="SslMode"/> へ対応付ける（未指定は <c>null</c>）</summary>
    /// <remarks>
    /// 未定義の列挙値は既定へ落とさず例外にする——黙って <c>null</c>（＝キーワードなし）へ倒すと、
    /// 検証を要求したつもりの設定が無言で「証明書を検証しない」既定へ降格する。
    /// </remarks>
    private static SslMode? ToNpgsqlSslMode(DbSslMode mode) =>
        mode switch
        {
            DbSslMode.Unspecified => null,
            DbSslMode.Disable => SslMode.Disable,
            DbSslMode.Prefer => SslMode.Prefer,
            DbSslMode.Require => SslMode.Require,
            DbSslMode.VerifyCa => SslMode.VerifyCA,
            DbSslMode.VerifyFull => SslMode.VerifyFull,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown SSL mode for PostgreSQL."
            ),
        };
}
