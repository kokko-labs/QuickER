using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QuickER.Oracle;
using QuickER.Provider;
using Testcontainers.Oracle;

namespace QuickER.Tests.Integration;

/// <summary>
/// Oracle の Testcontainers コンテナをコレクション内で 1 回だけ起動し共有するフィクスチャ。
/// </summary>
/// <remarks>
/// <para>
/// <c>gvenzl/oracle-free:23-slim-faststart</c>（faststart 変種は起動が速い）を
/// <see cref="InitializeAsync"/> で起動する。Docker 不在・起動失敗時は例外を握って
/// <see cref="IsAvailable"/> を <c>false</c> にし、<see cref="UnavailableReason"/> に理由を保持する
/// （フィクスチャ自体は失敗させない）。各統合テストは冒頭で
/// <c>Assert.SkipUnless(fixture.IsAvailable, ...)</c> によりスキップする。
/// </para>
/// <para>
/// テスト間の独立性は、コンテナ（＝データベース）を使い回しつつ各テストの冒頭で
/// <see cref="ResetSchemaAsync"/>（自スキーマの全テーブルを <c>DROP TABLE ... CASCADE CONSTRAINTS PURGE</c> で削除する
/// PL/SQL 無名ブロック）を実行し、クリーンな状態から始めることで確保する。
/// </para>
/// <para>
/// ODP.NET（Oracle.ManagedDataAccess.Core）は 1 コマンドで複数文を実行できないため、
/// <see cref="ExecuteAsync"/> は <see cref="OracleSchemaSyncExecutor"/> と同じ「/」行区切りの規約に加え、
/// 「/」行が 1 つも無いスクリプト（<c>OracleDdlGenerator</c> の生出力等、<c>;</c> 終端の文が並ぶだけのもの）も
/// 文単位に分割できるよう拡張したロジックで、1 文ずつ順次実行する。
/// </para>
/// </remarks>
public sealed class OracleContainerFixture : IAsyncLifetime
{
    /// <summary>コンテナの既定サービス名（<c>gvenzl/oracle-free:23</c> 系の既定 PDB 名）</summary>
    private const string ServiceName = "FREEPDB1";

    /// <summary>コンテナの既定ユーザー名</summary>
    private const string Username = "oracle";

    /// <summary>コンテナの既定パスワード</summary>
    private const string Password = "oracle";

    /// <summary>共有する Oracle コンテナ（Docker 不在時は起動されないため <c>null</c>）</summary>
    private OracleContainer? _container;

    /// <summary>コンテナが起動しテストを実行できるかどうか（<c>false</c> ならテストはスキップ）</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>利用不可の場合の理由（Docker 不在・起動失敗時のメッセージ）</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>コンテナへの ADO.NET 接続文字列（<see cref="IsAvailable"/> が <c>true</c> のときのみ有効）</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>コンテナを起動する。Docker 不在・起動失敗は握りつぶし <see cref="IsAvailable"/> を <c>false</c> にする</summary>
    /// <remarks>初回はイメージ pull が GB 級のため数分かかることがある</remarks>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart")
                .WithUsername(Username)
                .WithPassword(Password)
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = BuildAdoConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex) when (!DockerRequirement.IsStrict)
        {
            // Docker が無い・デーモンに接続できない等の場合はテストをスキップさせる。
            // 厳格モード（QUICKER_REQUIRE_DOCKER=1＝Docker があるはずの環境）ではフィルタが成立せず
            // そのまま失敗する＝壊れた Docker 構成がスキップ緑に化けない（DockerRequirement を参照）
            IsAvailable = false;
            UnavailableReason =
                $"Oracle コンテナを起動できませんでした（Docker 不在または起動失敗）: {ex}";
        }
    }

    /// <summary>コンテナを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>接続ユーザーの自スキーマの全テーブルを削除し、各テストをクリーンな状態から始める</summary>
    /// <remarks>データベースは使い回し、スキーマ（自ユーザーの全オブジェクト）のみ初期化する方式でテスト間の独立性を確保する</remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        // ごみ箱の BIN$ テーブル（dropped = 'YES'）は DROP できないため除外し、最後にごみ箱を空にする
        const string ResetBlock = """
            BEGIN
                FOR t IN (SELECT table_name FROM user_tables WHERE dropped = 'NO') LOOP
                    EXECUTE IMMEDIATE 'DROP TABLE "' || t.table_name || '" CASCADE CONSTRAINTS PURGE';
                END LOOP;
                EXECUTE IMMEDIATE 'PURGE RECYCLEBIN';
            END;
            """;

        await using var conn = new OracleConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ResetBlock;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>コンテナに対して開いた新しい接続を返す（呼び出し側で破棄する）</summary>
    public async Task<OracleConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var conn = new OracleConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    /// <summary>
    /// 任意の SQL スクリプトをコンテナ上で実行するヘルパー（DDL のセットアップ・生成 DDL の実行用）。
    /// </summary>
    /// <remarks>
    /// ODP.NET は複数文を 1 コマンドで実行できないため、<see cref="SplitStatements"/> で文単位に分割し、
    /// 通常文は末尾 <c>;</c> を除去、PL/SQL 無名ブロック（DECLARE / BEGIN 開始）は <c>;</c>（<c>END;</c>）を保持したまま順次実行する。
    /// </remarks>
    public async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        var statements = SplitStatements(sql);

        if (statements.Count == 0)
        {
            return;
        }

        await using var conn = new OracleConnection(ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        foreach (var stmt in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>コンテナの接続情報から共通接続設定 <see cref="DbConnectionSettings"/> を組み立てる</summary>
    /// <remarks>接続文字列ファクトリの実接続検証（D）で用いる</remarks>
    public DbConnectionSettings ToDbConnectionSettings()
    {
        var container = _container!;
        return new DbConnectionSettings
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(1521),
            ServiceName = ServiceName,
            Database = ServiceName,
            UserId = Username,
            Password = Password,
            AuthMode = DbAuthMode.UsernamePassword,
        };
    }

    /// <summary>コンテナのホスト・ポートから ODP.NET 用の EZConnect 接続文字列を組み立てる</summary>
    private string BuildAdoConnectionString()
    {
        var container = _container!;
        var b = new OracleConnectionStringBuilder
        {
            DataSource =
                $"{container.Hostname}:{container.GetMappedPublicPort(1521)}/{ServiceName}",
            UserID = Username,
            Password = Password,
        };
        return b.ConnectionString;
    }

    /// <summary>
    /// スクリプトを文単位に分割する。<see cref="OracleSchemaSyncExecutor.SplitStatements"/> と同じ規約
    /// （「/」のみの行を区切りとし、通常文は末尾 <c>;</c> を除去、PL/SQL ブロックは保持）に加え、
    /// 「/」行が 1 つも含まれないスクリプト（<see cref="QuickER.Oracle.OracleDdlGenerator"/> の生出力等）は
    /// 各行末の <c>;</c> を文の区切りとして扱う（PL/SQL ブロックは含まれない前提）。
    /// </summary>
    internal static List<string> SplitStatements(string script)
    {
        var normalized = script.Replace("\r\n", "\n");

        // 「/」のみの行が 1 つでもあれば、Executor と同じ「/」区切り規約に従う
        var hasSlashSeparator = normalized.Split('\n').Any(line => line.Trim() == "/");

        return hasSlashSeparator ? SplitBySlash(normalized) : SplitBySemicolon(normalized);
    }

    /// <summary>「/」のみの行で分割する（<see cref="OracleSchemaSyncExecutor.SplitStatements"/> と同じロジック）</summary>
    private static List<string> SplitBySlash(string normalized)
    {
        var statements = new List<string>();
        var current = new List<string>();

        foreach (var rawLine in normalized.Split('\n'))
        {
            if (rawLine.Trim() == "/")
            {
                AddIfMeaningful(statements, current);
                current.Clear();
                continue;
            }

            current.Add(rawLine);
        }

        AddIfMeaningful(statements, current);
        return statements;
    }

    /// <summary>
    /// 「/」区切りを含まないスクリプトを <c>;</c> 終端の文単位に分割する。
    /// コメント専用行（<c>--</c> 始まり）・空行は除去し、DECLARE / BEGIN ブロックは考慮しない
    /// （<see cref="QuickER.Oracle.OracleDdlGenerator"/> は PL/SQL ブロックを出力しないため）。
    /// </summary>
    private static List<string> SplitBySemicolon(string normalized)
    {
        var statements = new List<string>();
        var current = new List<string>();

        foreach (var rawLine in normalized.Split('\n'))
        {
            var trimmed = rawLine.Trim();

            // コメント専用行・空行はスキップ（文の途中に混ざることは想定しない）
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            current.Add(rawLine);

            if (trimmed.EndsWith(';'))
            {
                AddIfMeaningful(statements, current);
                current.Clear();
            }
        }

        AddIfMeaningful(statements, current);
        return statements;
    }

    /// <summary>蓄積した行を 1 文として整形し、意味がある場合のみ追加する</summary>
    private static void AddIfMeaningful(List<string> statements, List<string> lines)
    {
        var block = string.Join("\n", lines).Trim();

        if (block.Length == 0)
        {
            return;
        }

        if (IsCommentOnly(block))
        {
            return;
        }

        if (IsPlSqlBlock(block))
        {
            statements.Add(block);
        }
        else
        {
            statements.Add(block.TrimEnd().TrimEnd(';').TrimEnd());
        }
    }

    /// <summary>文が DECLARE / BEGIN で始まる PL/SQL 無名ブロックかどうかを判定する</summary>
    private static bool IsPlSqlBlock(string block)
    {
        var head = block.TrimStart();
        return head.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>文がコメント行（-- 始まり）と空行のみで構成されているかどうかを判定する</summary>
    private static bool IsCommentOnly(string block)
    {
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

/// <summary>Oracle 統合テスト用のコレクション定義（コンテナをコレクション内で共有する）</summary>
[CollectionDefinition(Name)]
public sealed class OracleContainerCollection : ICollectionFixture<OracleContainerFixture>
{
    /// <summary>コレクション名（各統合テストクラスの <c>[Collection]</c> で参照する）</summary>
    public const string Name = "Oracle Integration";
}
