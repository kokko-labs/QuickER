using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QuickER.Provider;
using QuickER.Provider.Oracle;
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
/// <see cref="ExecuteAsync"/> は <see cref="OracleSchemaSyncExecutor.SplitStatements"/>（SQL*Plus と同じ分割）で
/// 文単位に分け、1 文ずつ順次実行する。
/// </para>
/// <para>
/// フィクスチャの接続は ODP.NET の文のキャッシュを使わない（<see cref="BuildAdoConnectionString"/>）。
/// <see cref="ResetSchemaAsync"/> は、スキーマを空にする前に、ユーザーの各セッションの開いたカーソルが上限の半分を超えていないことを確かめる
/// （<see cref="AssertOpenCursorsWithinLimitAsync"/>）。
/// </para>
/// </remarks>
public sealed class OracleContainerFixture : IAsyncLifetime
{
    /// <summary>コンテナの既定サービス名（<c>gvenzl/oracle-free:23</c> 系の既定 PDB 名）</summary>
    private const string ServiceName = "FREEPDB1";

    /// <summary>コンテナの既定ユーザー名</summary>
    private const string Username = "oracle";

    /// <summary>コンテナの既定パスワード（アプリユーザーと SYS / SYSTEM で共通）</summary>
    private const string Password = "oracle";

    /// <summary>スキーマ（ユーザー）を作れる管理ユーザー名</summary>
    private const string SystemUsername = "system";

    /// <summary>共有する Oracle コンテナ（Docker 不在時は起動されないため <c>null</c>）</summary>
    private OracleContainer? _container;

    /// <summary>コンテナが起動しテストを実行できるかどうか（<c>false</c> ならテストはスキップ）</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>利用不可の場合の理由（Docker 不在・起動失敗時のメッセージ）</summary>
    public string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>コンテナへの ADO.NET 接続文字列（<see cref="IsAvailable"/> が <c>true</c> のときのみ有効）</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// SYSTEM（管理ユーザー）としての接続文字列。スキーマを 1 つ増やす必要があるテストだけが使う。
    /// </summary>
    /// <remarks>
    /// これで作った別スキーマのオブジェクトは <see cref="ResetSchemaAsync"/> の対象外（自スキーマしか消さない）
    /// なので、使うテストは自分で後始末すること。
    /// </remarks>
    public string SystemConnectionString { get; private set; } = string.Empty;

    /// <summary>コンテナを起動する。Docker 不在・起動失敗は握りつぶし <see cref="IsAvailable"/> を <c>false</c> にする</summary>
    /// <remarks>初回はイメージ pull が GB 級のため数分かかることがある</remarks>
    public async ValueTask InitializeAsync()
    {
        if (DockerRequirement.SkipReason(nameof(OracleContainerFixture)) is { } skip)
        {
            IsAvailable = false;
            UnavailableReason = skip;
            return;
        }

        try
        {
            _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart")
                .WithUsername(Username)
                .WithPassword(Password)
                // SYS / SYSTEM のパスワードを固定する。アプリユーザー（CONNECT + RESOURCE 相当）は
                // CREATE USER できないため、「別スキーマを参照する外部キー」を作るテストが
                // SYSTEM で 2 つ目のスキーマを用意できるようにするための逃がし口
                // （MySQL フィクスチャの RootConnectionString と同じ流儀）
                .WithEnvironment("ORACLE_PASSWORD", Password)
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = BuildAdoConnectionString();
            SystemConnectionString = BuildAdoConnectionString(SystemUsername);
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
    /// <remarks>
    /// データベースは使い回し、スキーマ（自ユーザーの全オブジェクト）のみ初期化する方式でテスト間の独立性を確保する。
    /// 消す前に、開いたカーソルの溜まりを確かめる（<see cref="AssertOpenCursorsWithinLimitAsync"/>）。リセットそのものが ORA-01000 で落ちると
    /// 溜めたセッションと文が分からないので、消した後でなく前に見る
    /// </remarks>
    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        await AssertOpenCursorsWithinLimitAsync(ct).ConfigureAwait(false);

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

    /// <summary>
    /// ユーザーのどのセッションも、開いたカーソル（<c>opened cursors current</c>）が上限（<c>open_cursors</c>）の半分以下であることを確かめ、
    /// 超えていればそのセッションと開いている文の上位を名指しして赤にする
    /// </summary>
    /// <remarks>
    /// <para>
    /// ODP.NET の文のキャッシュ（既定の <c>Self Tuning</c>）は、違う文を流したプールのセッションに、上限の約 12 手前までカーソルを開いたまま持つ。
    /// そのセッションで同時に開くカーソル（DDL の再帰の SQL など）が残りを食うと、リセットの PL/SQL が ORA-01000 で落ちる。
    /// 失敗が溜めたテストでなく後のテストのリセットに出て、しかもまれにしか出ないので、溜まった時点で名指しする。
    /// </para>
    /// <para>
    /// しきい値の半分は、フィクスチャの接続が文のキャッシュを使わないときの実測の最大（48）より十分上で、キャッシュが上限近くまで育ったセッション（287〜295）より
    /// 十分下にある。読み取りは SYSTEM の接続で、全テストのリセットで足しても 1 回 1.4 ms 前後（Oracle のコレクション全体の約 0.3%）。
    /// </para>
    /// </remarks>
    private async Task AssertOpenCursorsWithinLimitAsync(CancellationToken ct)
    {
        await using var sys = new OracleConnection(SystemConnectionString);
        await sys.OpenAsync(ct).ConfigureAwait(false);
        _openCursorsLimit ??= await ReadOpenCursorsLimitAsync(sys, ct).ConfigureAwait(false);
        var threshold = _openCursorsLimit.Value / 2;
        (string Sid, long Count)? worst = null;

        await using (var cmd = sys.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.sid, st.value
                FROM v$session s
                JOIN v$sesstat st ON st.sid = s.sid
                JOIN v$statname nm ON nm.statistic# = st.statistic#
                WHERE nm.name = 'opened cursors current' AND s.username = :username
                ORDER BY st.value DESC
                FETCH FIRST 1 ROWS ONLY
                """;
            cmd.Parameters.Add(new OracleParameter("username", Username.ToUpperInvariant()));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                worst = (
                    Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!,
                    Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)
                );
            }
        }

        if (worst is not { } session || session.Count <= threshold)
        {
            return;
        }

        var statements = new List<string>();

        await using (var cmd = sys.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*), cursor_type, SUBSTR(sql_text, 1, 60)
                FROM v$open_cursor
                WHERE sid = :sid
                GROUP BY cursor_type, SUBSTR(sql_text, 1, 60)
                ORDER BY 1 DESC
                FETCH FIRST 5 ROWS ONLY
                """;
            cmd.Parameters.Add(new OracleParameter("sid", session.Sid));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                statements.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{reader.GetValue(0)} x {reader.GetValue(1)}: {reader.GetValue(2)}"
                    )
                );
            }
        }

        throw new Xunit.Sdk.XunitException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Oracle のセッション sid={session.Sid} の開いたカーソルが {session.Count} で、上限 {_openCursorsLimit} の半分を超えた。"
            )
                + "前に流れたテストが、文のキャッシュを使う接続（フィクスチャの接続文字列でない接続）で違う文を流したか、カーソルを閉じ忘れている"
                + "（tests/QuickER.Tests/TestSupport/CLAUDE.md「Oracle のコンテナの開いたカーソル」）。開いている文の上位：\n  "
                + string.Join("\n  ", statements)
        );
    }

    /// <summary>データベースの <c>open_cursors</c> を読む</summary>
    private static async Task<int> ReadOpenCursorsLimitAsync(
        OracleConnection sys,
        CancellationToken ct
    )
    {
        await using var cmd = sys.CreateCommand();
        cmd.CommandText = "SELECT value FROM v$parameter WHERE name = 'open_cursors'";
        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>開いたカーソルのしきい値の元の <c>open_cursors</c>（最初のリセットで読む）</summary>
    private int? _openCursorsLimit;

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
    public async Task ExecuteAsync(string sql, CancellationToken ct = default) =>
        await ExecuteAsAsync(ConnectionString, sql, ct).ConfigureAwait(false);

    /// <summary>SYSTEM（管理ユーザー）として SQL スクリプトを実行する（スキーマの追加・撤去用）</summary>
    public async Task ExecuteAsSystemAsync(string sql, CancellationToken ct = default) =>
        await ExecuteAsAsync(SystemConnectionString, sql, ct).ConfigureAwait(false);

    /// <summary>指定の接続文字列で SQL スクリプトを文単位に実行する</summary>
    private static async Task ExecuteAsAsync(
        string connectionString,
        string sql,
        CancellationToken ct
    )
    {
        var statements = SplitStatements(sql);

        if (statements.Count == 0)
        {
            return;
        }

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        foreach (var stmt in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = stmt;
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>コンテナ内の SQL*Plus でスクリプトを流し、終了コードと出力を返す</summary>
    /// <remarks>
    /// 利用者が出力したスクリプトを手で流すときと同じ経路を確かめるためのもの。先頭に
    /// <c>WHENEVER SQLERROR EXIT FAILURE</c> を置くので、どれか 1 文でも ORA エラーになれば終了コードが 0 でなくなる
    /// （同じ文が 2 回実行されたときの ORA-00955 / ORA-01430 もここで表に出る）。置換変数の設定（<c>SET DEFINE</c>）は
    /// 触らない＝利用者の既定のまま流す。
    /// </remarks>
    public async Task<(long ExitCode, string Output)> RunSqlPlusAsync(
        string script,
        CancellationToken ct = default
    )
    {
        var container = _container!;
        const string ScriptPath = "/tmp/quicker-sqlplus.sql";
        var full =
            "WHENEVER SQLERROR EXIT FAILURE\n" + script.Replace("\r\n", "\n") + "\nEXIT SUCCESS\n";
        await container
            .CopyAsync(System.Text.Encoding.UTF8.GetBytes(full), ScriptPath, ct: ct)
            .ConfigureAwait(false);
        var result = await container
            .ExecAsync(
                [
                    "sqlplus",
                    "-S",
                    "-L",
                    $"{Username}/{Password}@//localhost:1521/{ServiceName}",
                    $"@{ScriptPath}",
                ],
                ct
            )
            .ConfigureAwait(false);
        return (result.ExitCode ?? -1, result.Stdout + result.Stderr);
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
    /// <remarks>
    /// 文のキャッシュを使わない（<c>Self Tuning=false</c>。<c>Statement Cache Size</c> の既定は 0）。各テストはスキーマを作り直すので、
    /// キャッシュは消えた表の文のカーソルを持つだけで、簡易 DSL の総当たりのように違う文を流すと、プールのセッションに上限の約 12 手前まで
    /// カーソルが開いたまま溜まる。<c>Statement Cache Purge=true</c> はプールへ戻すときにしか閉じず、同じ接続の中では溜まるので使わない
    /// </remarks>
    private string BuildAdoConnectionString(string? userId = null)
    {
        var container = _container!;
        var b = new OracleConnectionStringBuilder
        {
            DataSource =
                $"{container.Hostname}:{container.GetMappedPublicPort(1521)}/{ServiceName}",
            UserID = userId ?? Username,
            Password = Password,
            SelfTuning = false,
        };
        return b.ConnectionString;
    }

    /// <summary>スクリプトを文単位に分割する（実行器 <see cref="OracleSchemaSyncExecutor.SplitStatements"/> と同じ分割）</summary>
    /// <remarks>
    /// 実行器は SQL*Plus と同じ規則（通常文は行末の <c>;</c>、PL/SQL ブロックは「/」のみの行）で分割するので、
    /// 同期スクリプトも DDL の生出力も、テスト用のセットアップ SQL もこれ 1 つで分割できる。
    /// </remarks>
    internal static List<string> SplitStatements(string script) =>
        OracleSchemaSyncExecutor.SplitStatements(script);
}

/// <summary>Oracle 統合テスト用のコレクション定義（コンテナをコレクション内で共有する）</summary>
[CollectionDefinition(Name)]
public sealed class OracleContainerCollection : ICollectionFixture<OracleContainerFixture>
{
    /// <summary>コレクション名（各統合テストクラスの <c>[Collection]</c> で参照する）</summary>
    public const string Name = "Oracle Integration";
}
