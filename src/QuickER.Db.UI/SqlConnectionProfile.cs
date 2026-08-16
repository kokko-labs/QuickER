using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Db.UI;

/// <summary>名前を付けて保存可能な DB 接続プロファイル（多 DBMS 対応）</summary>
/// <remarks>
/// パスワード以外を JSON に永続化する パスワードは <see cref="SqlConnectionProfileStore"/> が
/// DPAPI で別途暗号化保存する。JSON プロパティ名は旧形式との互換を保つため変更しない
/// （<see cref="Dbms"/> を欠く旧データは既定の <c>sqlserver</c> として読み込まれる）。
/// </remarks>
public class SqlConnectionProfile
{
    /// <summary>プロファイルの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>一覧での選択ラベルとなる表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ターゲット DBMS のプロバイダ識別名（欠落した旧データは既定 sqlserver）</summary>
    public string Dbms { get; set; } = SqlServerProvider.ProviderName;

    /// <summary>サーバー名（ホスト）</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>接続ポート（未指定時は方言の既定ポートを用いる）</summary>
    public int? Port { get; set; }

    /// <summary>データベース名</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>認証方式（Windows / AzureAd は SQL Server のみ）</summary>
    public DbAuthMode AuthMode { get; set; } = DbAuthMode.Windows;

    /// <summary>ユーザー名（認証方式に応じて使用）</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>サーバー証明書を信頼するかどうか（SQL Server 固有）</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>サービス名（Oracle 固有・将来使用）</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>データベースファイルのパス（SQLite 固有。旧データでは欠落＝空文字）</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>接続タイムアウト（秒）</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>コマンド実行タイムアウト（秒）</summary>
    /// <remarks>
    /// キーを持たない旧プロファイル JSON は既定値（<see cref="DbCommands.DefaultTimeoutSeconds"/>）で読み込まれる
    /// ＝従来のハードコード値と同じため、既存プロファイルの挙動は変わらない。
    /// </remarks>
    public int CommandTimeoutSeconds { get; set; } = DbCommands.DefaultTimeoutSeconds;

    /// <summary>パスワードを暗号化保存するかどうか</summary>
    public bool SavePassword { get; set; }

    /// <summary>プロファイルから方言中立の接続設定を構築する（パスワードは引数で別途指定する）</summary>
    public DbConnectionSettings ToSettings(string password) =>
        new()
        {
            Host = Server,
            Port = Port,
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            Password = password,
            TrustServerCertificate = TrustServerCertificate,
            ServiceName = ServiceName,
            FilePath = FilePath,
            ConnectTimeoutSeconds = ConnectTimeoutSeconds,
            CommandTimeoutSeconds = CommandTimeoutSeconds,
        };
}
