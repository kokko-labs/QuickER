using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.Oracle;

/// <summary>Oracle のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <para>
/// 接続ユーザーの自スキーマ（<c>user_*</c> ビュー）のみを対象とする。
/// <c>user_tables</c> / <c>user_tab_columns</c> / <c>user_constraints</c> / <c>user_cons_columns</c> /
/// <c>user_tab_comments</c> / <c>user_col_comments</c> を用い、複合主キーは順序を保持する。
/// </para>
/// <para>
/// 参照先列集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する
/// （PostgreSQL 版と同一の意味論）。参照アクションは <c>user_constraints.delete_rule</c> を用い、
/// Oracle に <c>ON UPDATE</c> は存在しないため <see cref="Relationship.OnUpdate"/> は常に
/// <see cref="ForeignKeyReferentialAction.NoAction"/> で取り込む。
/// </para>
/// </remarks>
public class OracleSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await ImportAsync(conn, cancellationToken, commandTimeoutSeconds)
            .ConfigureAwait(false);
        return new SchemaImportResult
        {
            Entities = result.Entities,
            Relationships = result.Relationships,
            Warnings = result.Warnings,
        };
    }

    /// <summary>取得したスキーマを格納する結果 DTO</summary>
    public sealed class SchemaResult
    {
        /// <summary>取得したエンティティ一覧</summary>
        public List<Entity> Entities { get; init; } = new();

        /// <summary>取得したリレーション一覧</summary>
        public List<Relationship> Relationships { get; init; } = new();

        /// <summary>取込で宣言どおりには写し取れなかった箇所の警告</summary>
        public List<SchemaImportWarning> Warnings { get; init; } = new();
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    /// <param name="conn">既に開かれた接続</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。既定値付きで <paramref name="ct"/> の後ろに置くのは、
    /// 既存の位置指定呼び出し（統合テストの <c>ImportAsync(conn, ct)</c>）を壊さないため。
    /// </param>
    public async Task<SchemaResult> ImportAsync(
        OracleConnection conn,
        CancellationToken ct = default,
        int commandTimeoutSeconds = DbCommands.DefaultTimeoutSeconds
    )
    {
        var warnings = new List<SchemaImportWarning>();
        var tables = await LoadTablesAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        await LoadOutOfScopeForeignKeysAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);

        // 列が 1 本も取れなかったテーブルは DDL を生成できないため、黙って通さず名指しで告げる
        warnings.AddRange(
            tables
                .Values.Where(entry => entry.Entity.Columns.Count == 0)
                .Select(entry => new SchemaImportWarning(
                    SchemaImportWarningKind.TableColumnsUnavailable,
                    entry.Entity.TableName
                ))
        );

        // DDL へ出せない型表記は、後で DDL / 同期を叩いた瞬間に図全体を止める。取込完了時に名指しする
        warnings.AddRange(
            SchemaImportWarnings.DetectUnemittableColumnTypes(
                tables.Values.Select(entry => entry.Entity)
            )
        );

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            Warnings = warnings,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>自スキーマの通常テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// <para>
    /// USER_ ビューは所有オブジェクトのみを返すため、ALL_ ビューと違い owner での絞り込みが不要。
    /// ユーザー定義テーブルに限定するため、ごみ箱の <c>BIN$...</c>（<c>dropped = 'YES'</c>）・
    /// ドメインインデックス等の二次オブジェクト（<c>secondary = 'Y'</c>）・ネステッドテーブル・
    /// IOT オーバーフローセグメント（<c>SYS_IOT_OVER_...</c>）は除外する。
    /// </para>
    /// <para>
    /// 一時表（<c>temporary = 'Y'</c>）とマテリアライズドビューのコンテナ表も除外する。
    /// どちらも <c>user_tables</c> には通常テーブルとして現れるが、実体はセッション作業域とビューの結果で、
    /// ER 図のエンティティではない（「ビューは取り込まない」という取込範囲の宣言に実装を合わせる）。
    /// </para>
    /// </remarks>
    private const string TablesSql =
        @"SELECT table_name FROM user_tables
WHERE dropped = 'NO'
  AND secondary = 'N'
  AND nested = 'NO'
  AND temporary = 'N'
  AND (iot_type IS NULL OR iot_type = 'IOT')
  AND table_name NOT IN (SELECT mview_name FROM user_mviews)
-- NLSSORT の BINARY 指定は、セッションの NLS_SORT 次第で DUP と 小文字の dup の順序が変わるため
-- （衝突時にどちらを採るかを決定的にする）
ORDER BY NLSSORT(table_name, 'NLS_SORT=BINARY')";

    /// <summary>自スキーマ全テーブルのカラム定義を序数順に取得するクエリ</summary>
    /// <remarks>
    /// <para>
    /// <c>char_length</c> は CHAR / VARCHAR2 / NCHAR / NVARCHAR2 系のみ有効な文字数であり、
    /// <c>RAW</c> 等のバイト列型では常に 0 になる。バイト長が必要な型のために <c>data_length</c> も取得する。
    /// </para>
    /// <para>
    /// <c>char_used</c> は長さの単位（<c>'B'</c>=バイト / <c>'C'</c>=文字）。<c>VARCHAR2(50 CHAR)</c> と
    /// <c>VARCHAR2(50)</c> はマルチバイト文字セットでは別物なので、単位語まで含めて持ち帰る。
    /// </para>
    /// </remarks>
    private const string ColumnsSql =
        @"SELECT table_name, column_name, data_type, data_precision, data_scale, char_length, nullable, column_id, data_length, char_used
FROM user_tab_columns
ORDER BY table_name, column_id";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    /// <remarks>
    /// <c>status = 'ENABLED'</c> で絞るのは、DISABLE された制約は実際には一意性も参照整合性も強制されず、
    /// 図へ取り込むと「DB には無い保証」を宣言してしまうため（ENABLE 済みの制約だけが実在する制約）。
    /// PK / UNIQUE / FK の 3 クエリで同じ規則を用いる。
    /// </remarks>
    private const string PrimaryKeysSql =
        @"SELECT cc.table_name, cc.column_name, cc.position
FROM user_constraints c
JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
WHERE c.constraint_type = 'P' AND c.status = 'ENABLED'
ORDER BY cc.table_name, cc.position";

    /// <summary>UNIQUE 制約の構成列を宣言順に取得するクエリ（モデルの一意制約・1 対 1 判定に用いる）</summary>
    /// <remarks>
    /// <c>constraint_type = 'U'</c> の真の UNIQUE 制約のみを対象とする（<c>CREATE UNIQUE INDEX</c> による
    /// 素の一意インデックスは user_constraints に現れないため自然に除外される）。position が宣言順を表す
    /// </remarks>
    private const string UniqueConstraintSql =
        @"SELECT cc.table_name, cc.constraint_name, cc.column_name, cc.position
FROM user_constraints c
JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
WHERE c.constraint_type = 'U' AND c.status = 'ENABLED'
ORDER BY cc.table_name, cc.constraint_name, cc.position";

    /// <summary>外部キーの親子テーブル・列・削除アクションを取得するクエリ</summary>
    /// <remarks>
    /// <c>delete_rule</c> は CASCADE / SET NULL / NO ACTION を表す。
    /// 子側は <c>user_cons_columns</c>、親側は参照先制約 <c>r_constraint_name</c> の構成列を position で突き合わせる。
    /// <c>status = 'ENABLED'</c> の理由は <see cref="PrimaryKeysSql"/> と同じ。
    /// </remarks>
    private const string ForeignKeysSql =
        @"SELECT
    c.constraint_name AS fk_name,
    cc.table_name AS child_table,
    cc.column_name AS child_column,
    rc.table_name AS ref_table,
    rcc.column_name AS ref_column,
    cc.position AS ordinal,
    c.delete_rule AS delete_rule
FROM user_constraints c
JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
JOIN user_constraints rc ON c.r_constraint_name = rc.constraint_name AND c.r_owner = rc.owner
JOIN user_cons_columns rcc ON rc.constraint_name = rcc.constraint_name AND cc.position = rcc.position
WHERE c.constraint_type = 'R' AND c.status = 'ENABLED'
ORDER BY c.constraint_name, cc.position";

    /// <summary>取込範囲（接続ユーザーの自スキーマ）の外を参照する外部キーを列挙するクエリ</summary>
    /// <remarks>
    /// <see cref="ForeignKeysSql"/> は親側を <c>user_constraints</c>（＝自スキーマ）へ内部結合しているため、
    /// 他スキーマを参照する FK は結果から自然に消える。消えること自体は正しい（参照先テーブルが図に無い）が、
    /// 従来は無告知だったので、ここで拾って警告に載せる。<c>USER_</c> ビューの <c>owner</c> は常に接続ユーザー
    /// なので、<c>r_owner &lt;&gt; owner</c> がそのまま「範囲外への参照」を意味する。
    /// </remarks>
    private const string OutOfScopeForeignKeysSql =
        @"SELECT c.constraint_name, c.table_name, c.r_owner
FROM user_constraints c
WHERE c.constraint_type = 'R' AND c.status = 'ENABLED' AND c.r_owner <> c.owner
ORDER BY c.constraint_name";

    /// <summary>テーブルコメントを取得するクエリ</summary>
    /// <remarks>COMMENT ON TABLE 未設定のテーブルは comments が NULL になるため、ここで除外する</remarks>
    private const string TableCommentsSql =
        "SELECT table_name, comments FROM user_tab_comments WHERE comments IS NOT NULL";

    /// <summary>カラムコメントを取得するクエリ</summary>
    /// <remarks>COMMENT ON COLUMN 未設定の列は comments が NULL になるため、ここで除外する</remarks>
    private const string ColumnCommentsSql =
        "SELECT table_name, column_name, comments FROM user_col_comments WHERE comments IS NOT NULL";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    /// <remarks>
    /// 辞書は 5 方言共通の大文字小文字非依存だが、Oracle は引用識別子で <c>"dup"</c> と <c>DUP</c> を
    /// 共存させられる。黙って上書きすると 1 エンティティへ潰れて両テーブルの列が混ざるため、
    /// 後着（<c>table_name</c> 昇順で後ろ）を取り込まず警告として告げる。
    /// </remarks>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        OracleConnection conn,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, SchemaTableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = DbCommands.Create(conn, TablesSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);

            if (dict.TryGetValue(name, out var existing))
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.TableNameCollision,
                        name,
                        name,
                        existing.Entity.TableName
                    )
                );
                continue;
            }

            dict[name] = new SchemaTableEntry
            {
                Key = name,
                Entity = new Entity { TableName = name, Columns = new List<Column>() },
            };
        }

        return dict;
    }

    /// <summary>取込範囲外を参照する外部キーを拾い、除外したことを警告として積む</summary>
    private static async Task LoadOutOfScopeForeignKeysAsync(
        OracleConnection conn,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(
            conn,
            OutOfScopeForeignKeysSql,
            commandTimeoutSeconds
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            warnings.Add(
                new SchemaImportWarning(
                    SchemaImportWarningKind.ForeignKeyOutsideScope,
                    reader.GetString(1),
                    reader.GetString(0),
                    reader.GetString(2)
                )
            );
        }
    }

    /// <summary>取込対象として採用したテーブルを、大文字小文字まで一致させて引く</summary>
    /// <remarks>
    /// テーブル辞書は 5 方言共通の大文字小文字非依存なので、素で引くと「衝突して捨てたほうのテーブル」の
    /// 列・制約・説明が、採用したほうのエンティティへ吸い寄せられて混ざる。USER_ ビューが返す名前は
    /// どのクエリでも同じ実体名なので、ここで序数一致まで求めても正当な行を取りこぼすことはない。
    /// </remarks>
    private static bool TryGetExactTable(
        Dictionary<string, SchemaTableEntry> tables,
        string tableName,
        [NotNullWhen(true)] out SchemaTableEntry? entry
    ) =>
        tables.TryGetValue(tableName, out entry)
        && string.Equals(entry.Key, tableName, StringComparison.Ordinal);

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, ColumnsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);

            if (!TryGetExactTable(tables, table, out var entry))
            {
                continue;
            }

            var colName = reader.GetString(1);
            var dataType = reader.GetString(2);
            int? dataPrecision = reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3));
            int? dataScale = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
            int? charLength = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
            // nullable は 'Y' / 'N'
            var isNullable = string.Equals(
                reader.GetString(6),
                "Y",
                StringComparison.OrdinalIgnoreCase
            );
            int? dataLength = reader.IsDBNull(8) ? null : Convert.ToInt32(reader.GetValue(8));
            var charUsed = reader.IsDBNull(9) ? null : reader.GetString(9);

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(
                    dataType,
                    dataPrecision,
                    dataScale,
                    charLength,
                    dataLength,
                    charUsed
                ),
                IsNullable = isNullable,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正し、構成順を記録する</summary>
    /// <remarks>
    /// 構成順はクエリの <c>ORDER BY</c>（<c>cc.position</c> 昇順）に任せ、到着順でそのまま積む。
    /// </remarks>
    private static async Task LoadPrimaryKeysAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, PrimaryKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!TryGetExactTable(tables, reader.GetString(0), out var entry))
            {
                continue;
            }

            if (entry.ColumnsByName.TryGetValue(reader.GetString(1), out var col))
            {
                col.IsPrimaryKey = true;
                col.IsNullable = false;
                entry.Entity.PrimaryKeyColumnIds.Add(col.Id);
            }
        }
    }

    /// <summary>テーブル・カラムのコメントを取得し、エンティティ・カラムの説明へ反映する</summary>
    private static async Task LoadDescriptionsAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        // テーブルコメント
        await using (var cmd = DbCommands.Create(conn, TableCommentsSql, commandTimeoutSeconds))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (
                    TryGetExactTable(tables, reader.GetString(0), out var entry)
                    && !reader.IsDBNull(1)
                )
                {
                    entry.Entity.Description = reader.GetString(1);
                }
            }
        }

        // カラムコメント
        await using (var cmd = DbCommands.Create(conn, ColumnCommentsSql, commandTimeoutSeconds))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!TryGetExactTable(tables, reader.GetString(0), out var entry))
                {
                    continue;
                }

                if (
                    !reader.IsDBNull(2)
                    && entry.ColumnsByName.TryGetValue(reader.GetString(1), out var col)
                )
                {
                    col.Description = reader.GetString(2);
                }
            }
        }
    }

    /// <summary>外部キーを読み込み、複合列を集約してリレーションへ変換する</summary>
    /// <remarks>
    /// 参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var builder = new ForeignKeyRelationshipBuilder();

        await using var cmd = DbCommands.Create(conn, ForeignKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var childKey = reader.GetString(1); // FK 保有テーブル（子）
            var childCol = reader.GetString(2);
            var refKey = reader.GetString(3); // 参照先テーブル（親・PK 側）
            var refCol = reader.GetString(4);
            var deleteAction = MapReferentialAction(
                reader.IsDBNull(6) ? null : reader.GetString(6)
            );

            // 衝突して捨てたテーブルが両端のどちらかなら、そのリレーションは作らない
            if (
                !TryGetExactTable(tables, childKey, out _)
                || !TryGetExactTable(tables, refKey, out _)
            )
            {
                continue;
            }

            builder.Add(
                fkName,
                childKey,
                childCol,
                refKey,
                refCol,
                deleteAction,
                // Oracle に ON UPDATE は存在しないため常に NoAction で取り込む
                ForeignKeyReferentialAction.NoAction
            );
        }

        return builder.Build(tables);
    }

    /// <summary>UNIQUE 制約を読み込み、各エンティティの一意制約としてモデルへ載せる</summary>
    private static async Task LoadUniqueConstraintsAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var builder = new UniqueConstraintImportBuilder();

        await using (var cmd = DbCommands.Create(conn, UniqueConstraintSql, commandTimeoutSeconds))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);

                // 衝突して捨てたテーブルの制約を、採用したほうへ付け替えない
                if (!TryGetExactTable(tables, key, out _))
                {
                    continue;
                }

                var constraintName = reader.GetString(1);
                var col = reader.GetString(2);
                builder.Add(key, constraintName, col, constraintName);
            }
        }

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());
    }

    /// <summary>user_constraints.delete_rule を参照アクションへ変換する</summary>
    /// <remarks>CASCADE / SET NULL / NO ACTION（既定）。Oracle に更新側のアクションは無い</remarks>
    private static ForeignKeyReferentialAction MapReferentialAction(string? rule) =>
        ForeignKeyReferentialActionHelper.Parse(rule);

    /// <summary>Oracle の型情報を <c>NUMBER(10,2)</c> / <c>VARCHAR2(50)</c> / <c>TIMESTAMP(6)</c> 等の表示形式へ整形する</summary>
    /// <remarks>
    /// <para>
    /// <c>user_tab_columns</c> の <c>data_type</c> と <c>data_precision</c> / <c>data_scale</c> / <c>char_length</c> から
    /// <see cref="OracleTypeCatalog"/> が解析できる表記を組み立てる。
    /// </para>
    /// <para>
    /// <c>TIMESTAMP</c> 系は <c>data_type</c> が既に <c>"TIMESTAMP(6)"</c> /
    /// <c>"TIMESTAMP(6) WITH TIME ZONE"</c> の形式を含むため、そのまま採用する。
    /// </para>
    /// <para>
    /// <c>char_length</c> は CHAR / VARCHAR2 / NCHAR / NVARCHAR2 系のみ有効な文字数で、
    /// <c>RAW</c> 等のバイト列型では常に 0 を返すため、そちらは <paramref name="dataLength"/>（バイト長）を用いる。
    /// </para>
    /// <para>
    /// <paramref name="charUsed"/> が <c>'C'</c>（文字単位）のとき <c>VARCHAR2</c> / <c>CHAR</c> には単位語を
    /// 付けて <c>VARCHAR2(50 CHAR)</c> と書き出す。既定の <c>'B'</c>（バイト単位）は単位語なし＝従来どおりの表記。
    /// <c>NVARCHAR2</c> / <c>NCHAR</c> は常に文字単位で <c>char_used</c> も <c>'C'</c> を返すが、
    /// Oracle の構文が単位語を受け付けないため付けない。
    /// </para>
    /// </remarks>
    public static string FormatDataType(
        string dataType,
        int? dataPrecision,
        int? dataScale,
        int? charLength,
        int? dataLength = null,
        string? charUsed = null
    )
    {
        var upper = dataType.ToUpperInvariant();

        // TIMESTAMP 系（WITH TIME ZONE 等を含む）は data_type が既に精度・修飾を持つためそのまま返す
        if (upper.StartsWith("TIMESTAMP", StringComparison.Ordinal))
        {
            return dataType;
        }

        switch (upper)
        {
            case "NUMBER":
                if (dataPrecision is null)
                {
                    // NUMBER(*,s) は「精度は最大・スケールは s」の宣言で、data_precision が null・
                    // data_scale が非 null で返る。早期 return するとスケードが落ちて素の NUMBER に化ける
                    return dataScale is not null and not 0 ? $"NUMBER(*,{dataScale})" : "NUMBER";
                }

                // スケール 0 と未指定は user_tab_columns 上で区別できないため精度のみ。
                // 負のスケール（NUMBER(10,-2) = 100 の倍数へ丸める）は宣言どおりに書き出す
                return dataScale is not null and not 0
                    ? $"NUMBER({dataPrecision},{dataScale})"
                    : $"NUMBER({dataPrecision})";

            case "VARCHAR2":
            case "CHAR":
                if (charLength is null)
                {
                    return upper;
                }

                return string.Equals(charUsed, "C", StringComparison.OrdinalIgnoreCase)
                    ? $"{upper}({charLength} CHAR)"
                    : $"{upper}({charLength})";

            case "NVARCHAR2":
            case "NCHAR":
                return charLength is null ? upper : $"{upper}({charLength})";

            case "RAW":
                // char_length は RAW では常に 0 のため、バイト長 data_length を用いる
                return dataLength is null or 0 ? "RAW" : $"RAW({dataLength})";

            case "FLOAT":
                // FLOAT(b) は 2 進精度。精度があれば付与する（正規型では Float64 として解釈される）
                return dataPrecision is null ? "FLOAT" : $"FLOAT({dataPrecision})";

            default:
                // BINARY_FLOAT / BINARY_DOUBLE / DATE / CLOB / NCLOB / BLOB / XMLTYPE 等はそのまま
                return upper;
        }
    }
}
