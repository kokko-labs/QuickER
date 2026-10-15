using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.SqlServer;

/// <summary>SQL Server のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <c>sys.tables</c> / <c>INFORMATION_SCHEMA</c> 系と <c>sys.foreign_keys</c> を用い、ユーザー定義テーブルのみ対象とする
/// （<c>is_ms_shipped</c> と拡張プロパティ <c>microsoft_database_tools_support</c> で sysdiagrams 等のツール用テーブルを除外）
/// 複合主キーは順序を保持する 多対多は中間テーブルとして 1 対多 × 2 の形で表現する
/// <para>
/// 次の 2 つも取り込まず、除外したことを警告で告げる:
/// テンポラルテーブルの履歴表（<c>temporal_type = 1</c>・本表は取り込む）と、
/// 無効化された外部キー（<c>is_disabled = 1</c>＝<c>NOCHECK CONSTRAINT</c>。
/// <c>WITH NOCHECK</c> で追加されたが現在は有効な外部キーは取り込む）。
/// 計算列と本表の期間列（<c>GENERATED ALWAYS AS ROW START / END</c>）は
/// <see cref="Column.IsComputed"/> を立てて取り込む（式は意味モデルに載らないので警告で告げる）。
/// </para>
/// </remarks>
public class SqlServerSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ImportAsync(conn, cancellationToken, commandTimeoutSeconds)
            .ConfigureAwait(false);
    }

    /// <summary>指定の接続設定で接続を開きスキーマを取得する</summary>
    public async Task<SchemaImportResult> ImportAsync(
        SqlConnectionSettings settings,
        CancellationToken ct = default
    )
    {
        var connStr = settings.Build();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await ImportAsync(conn, ct, settings.CommandTimeoutSeconds).ConfigureAwait(false);
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    /// <param name="conn">既に開かれた接続</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。既定値付きで <paramref name="ct"/> の後ろに置くのは、
    /// 既存の位置指定呼び出し（統合テストの <c>ImportAsync(conn, ct)</c>）を壊さないため。
    /// </param>
    public async Task<SchemaImportResult> ImportAsync(
        SqlConnection conn,
        CancellationToken ct = default,
        int commandTimeoutSeconds = DbCommands.DefaultTimeoutSeconds
    )
    {
        var warnings = new List<SchemaImportWarning>();
        var tables = await LoadTablesAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadTemporalHistoryTablesAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadExcludedObjectsAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, warnings, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        await LoadDisabledForeignKeysAsync(conn, tables, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadPartitionedTablesAsync(conn, tables, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        var defaults = await LoadColumnAttributesAsync(
                conn,
                tables,
                commandTimeoutSeconds,
                warnings,
                ct
            )
            .ConfigureAwait(false);

        // DDL へ出せない型表記は、後で DDL / 同期を叩いた瞬間に図全体を止める。取込完了時に名指しする
        warnings.AddRange(
            SchemaImportWarnings.DetectUnemittableColumnTypes(
                tables.Values.Select(entry => entry.Entity)
            )
        );

        // 前後に空白のある DB の名前は、図では空白を除いた名前になる。取込完了時に名指しする
        warnings.AddRange(SchemaImportWarnings.DetectTrimmedNames(tables.Values));

        return new SchemaImportResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            Warnings = warnings,
            ColumnDefaults = defaults,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>スキーマ・テーブル名からテーブルキー（<c>[schema].[name]</c> 形式）を組み立てる</summary>
    private static string TableKey(string schema, string name) => $"[{schema}].[{name}]";

    /// <summary>スキーマ・テーブル名から表示名（<c>dbo</c> なら素の名前、他は <c>schema.name</c>）を組み立てる</summary>
    private static string DisplayTableName(string schema, string name) =>
        schema == "dbo" ? name : $"{schema}.{name}";

    /// <summary>ユーザー定義テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// SSMS のオブジェクトエクスプローラーと同じ基準でシステム由来のテーブルを除外する:
    /// <c>is_ms_shipped = 1</c>（Microsoft 出荷物）と、拡張プロパティ
    /// <c>microsoft_database_tools_support</c> が付いたツール用テーブル（sysdiagrams 等）。
    /// <c>temporal_type = 1</c>（テンポラルテーブルの履歴表）も除外する
    /// （<see cref="TemporalHistoryTablesSql"/> が別途拾って警告に載せる。本表＝
    /// <c>temporal_type = 2</c> は通常テーブルとして取り込む＝この条件では除外しない）。
    /// <c>temporal_type</c> は SQL Server 2016 で追加された列。
    /// 外部テーブル（<c>is_external = 1</c>）も、データが DB の外にあり DDL・同期で作り直せないため除外する
    /// （<see cref="ExcludedObjectsSql"/> が名指しして告げる）。
    /// </remarks>
    private const string TablesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.is_ms_shipped = 0
  AND t.temporal_type <> 1
  AND t.is_external = 0
  AND NOT EXISTS (
      SELECT 1
      FROM sys.extended_properties ep
      WHERE ep.class = 1
        AND ep.major_id = t.object_id
        AND ep.minor_id = 0
        AND ep.name = N'microsoft_database_tools_support'
  )
-- COLLATE ... BIN2 で並べるのは、DB の既定照合順序次第で Dup と dup の順序が変わるため
-- （衝突時にどちらを採るかを決定的にする。PostgreSQL の COLLATE C / Oracle の NLSSORT BINARY と同じ狙い）
ORDER BY s.name COLLATE Latin1_General_BIN2, t.name COLLATE Latin1_General_BIN2;";

    /// <summary>取り込まないオブジェクト（ビュー・外部テーブル）を名指しするクエリ</summary>
    /// <remarks>取込の対象（<see cref="TablesSql"/>）は変えず、告げるためだけに読む。</remarks>
    private const string ExcludedObjectsSql =
        @"
SELECT o.OBJECT_SCHEMA, o.OBJECT_NAME, o.KIND
FROM (
    SELECT s.name AS OBJECT_SCHEMA, v.name AS OBJECT_NAME, 'view' AS KIND
    FROM sys.views v
    JOIN sys.schemas s ON v.schema_id = s.schema_id
    WHERE v.is_ms_shipped = 0
    UNION ALL
    SELECT s.name, t.name, 'external'
    FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.is_ms_shipped = 0 AND t.is_external = 1
) o
ORDER BY o.OBJECT_SCHEMA COLLATE Latin1_General_BIN2, o.OBJECT_NAME COLLATE Latin1_General_BIN2;";

    /// <summary>テンポラルテーブルの履歴表と、その本表名を取得するクエリ</summary>
    /// <remarks>
    /// 履歴表自身（<c>temporal_type = 1</c>）を本表（<c>bt</c>）へ <c>history_table_id</c> で結合し、
    /// 告知に必要な本表名を一緒に持ち帰る。<c>temporal_type</c> / <c>history_table_id</c> はいずれも
    /// SQL Server 2016 で追加された列。
    /// </remarks>
    private const string TemporalHistoryTablesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME, bs.name AS BASE_SCHEMA, bt.name AS BASE_TABLE
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.tables bt ON bt.history_table_id = t.object_id
JOIN sys.schemas bs ON bt.schema_id = bs.schema_id
WHERE t.temporal_type = 1
ORDER BY s.name, t.name;";

    /// <summary>全テーブルのカラム定義を序数順に取得するクエリ</summary>
    /// <remarks>
    /// <para>
    /// 計算列（<c>AS (式)</c>）は <c>INFORMATION_SCHEMA.COLUMNS</c> からは判別できないため
    /// <c>sys.computed_columns</c> を左結合する。行の有無が計算列かどうかで、
    /// <c>definition</c>（式）は <c>VIEW DEFINITION</c> 権限が無いと NULL になり得るので、
    /// 判定には使わず <c>column_id</c> の有無で見る。
    /// </para>
    /// <para>
    /// テンポラルテーブルの期間列（<c>GENERATED ALWAYS AS ROW START / END</c>）も同じ
    /// 「書き込みを受け付けない」性質を持つため <c>sys.columns.generated_always_type</c>
    /// （1 = ROW START・2 = ROW END）を左結合して同じ判定へ合流させる（0 と 1・2 以外の値は
    /// 台帳テーブル〔SQL Server 2022 以降〕専用のトランザクション ID / シーケンス番号列で対象外
    /// ＝<c>IN (1, 2)</c> で明示的に絞る）。<c>HIDDEN</c> 修飾（<c>generated_always_type</c> は
    /// <c>SELECT *</c> の可視性に関係なくカタログへ現れる）でも取得できることを統合テストで固定する。
    /// <c>generated_always_type</c> は SQL Server 2016 で追加された列。
    /// </para>
    /// </remarks>
    private const string ColumnsSql =
        @"
SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
       c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE, c.ORDINAL_POSITION,
       CASE WHEN cc.column_id IS NOT NULL OR ISNULL(gc.generated_always_type, 0) IN (1, 2) THEN 1 ELSE 0 END AS IS_COMPUTED,
       cc.definition AS COMPUTED_DEFINITION,
       gc.generated_always_type AS GENERATED_ALWAYS_TYPE,
       c.DATETIME_PRECISION
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN sys.computed_columns cc
       ON cc.object_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + N'.' + QUOTENAME(c.TABLE_NAME))
      AND cc.name = c.COLUMN_NAME
LEFT JOIN sys.columns gc
       ON gc.object_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + N'.' + QUOTENAME(c.TABLE_NAME))
      AND gc.name = c.COLUMN_NAME
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    private const string PrimaryKeysSql =
        @"
SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME, kcu.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
 AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
 AND tc.TABLE_NAME = kcu.TABLE_NAME
WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
ORDER BY kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.ORDINAL_POSITION;";

    /// <summary>外部キーの親子テーブル・列・参照アクションを取得するクエリ</summary>
    /// <remarks>
    /// <c>is_disabled = 1</c>（<c>NOCHECK CONSTRAINT</c> で無効化された FK）は参照整合性を何も
    /// 強制しないため除外する（<see cref="DisabledForeignKeysSql"/> が別途拾って警告に載せる）。
    /// <c>is_not_trusted = 1</c>（<c>WITH NOCHECK</c> で追加されたが現在は有効）はこの条件では
    /// 除外しない＝以後の書き込みには強制が効くため、通常どおり取り込む。
    /// </remarks>
    private const string ForeignKeysSql =
        @"
SELECT
    fk.name AS FkName,
    SCHEMA_NAME(tp.schema_id) AS ParentSchema, tp.name AS ParentTable, cp.name AS ParentColumn,
    SCHEMA_NAME(tr.schema_id) AS RefSchema, tr.name AS RefTable, cr.name AS RefColumn,
    fkc.constraint_column_id AS Ordinal,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables  tp ON fkc.parent_object_id = tp.object_id
JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
JOIN sys.tables  tr ON fkc.referenced_object_id = tr.object_id
JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
WHERE fk.is_disabled = 0
ORDER BY fk.name, fkc.constraint_column_id;";

    /// <summary>無効化された外部キー（<c>is_disabled = 1</c>）を、告知のためだけに列挙するクエリ</summary>
    /// <remarks>
    /// <see cref="ForeignKeysSql"/> は列展開が要るため無効な FK を素通しで除外しているだけで、
    /// 除外した FK 名を名指しできない。ここでは列を持たない 1 行 1 FK の軽いクエリで、
    /// 除外した FK を漏れなく告知する（既存の <see cref="ForeignKeysSql"/> の意味は変えない）。
    /// </remarks>
    private const string DisabledForeignKeysSql =
        @"
SELECT fk.name AS FkName, SCHEMA_NAME(tp.schema_id) AS ParentSchema, tp.name AS ParentTable
FROM sys.foreign_keys fk
JOIN sys.tables tp ON fk.parent_object_id = tp.object_id
WHERE fk.is_disabled = 1
ORDER BY fk.name;";

    /// <summary>UNIQUE 制約の構成列を宣言順に取得するクエリ（モデルの一意制約・1 対 1 判定に用いる）</summary>
    /// <remarks>
    /// <c>is_unique_constraint = 1</c> で「真の UNIQUE 制約」に限定する。
    /// <c>CREATE UNIQUE INDEX</c> による素の一意インデックス（フィルター付きを含む）は
    /// 制約ではないため取り込まない（5 方言で線引きを揃えるため）。
    /// </remarks>
    private const string UniqueConstraintSql =
        @"
SELECT SCHEMA_NAME(t.schema_id) AS TableSchema, t.name AS TableName, i.name AS IndexName,
       c.name AS ColumnName, ic.key_ordinal AS Ordinal
FROM sys.indexes i
JOIN sys.tables  t  ON i.object_id = t.object_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.is_unique_constraint = 1 AND i.is_primary_key = 0 AND ic.is_included_column = 0
ORDER BY t.schema_id, t.name, i.name, ic.key_ordinal;";

    /// <summary>テーブル・カラムの拡張プロパティ MS_Description を一括取得するクエリ（minor_id=0 がテーブルレベル）</summary>
    private const string DescriptionsSql =
        @"
SELECT
    s.name        AS SchemaName,
    t.name        AS TableName,
    c.name        AS ColumnName,
    CAST(ep.value AS nvarchar(MAX)) AS Description
FROM sys.extended_properties ep
JOIN sys.tables t  ON ep.major_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.columns c
       ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
WHERE ep.class = 1 AND ep.name = N'MS_Description';";

    /// <summary>テーブル一覧を読み込み、テーブルキーをキーとするエントリ辞書を構築する</summary>
    /// <remarks>
    /// 辞書は 5 方言共通の大文字小文字非依存だが、SQL Server も大文字小文字を区別する照合順序
    /// （例: <c>Latin1_General_100_CS_AS</c>）の DB では <c>Dup</c> と <c>dup</c> が共存する。
    /// 黙って上書きすると 1 エンティティへ潰れて両テーブルの列が混ざるため、
    /// 後着（<c>BIN2</c> 昇順で後ろ）を取り込まず警告として告げる。
    /// </remarks>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        SqlConnection conn,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, SchemaTableEntry>(SchemaTableEntry.KeyComparer);
        await using var cmd = DbCommands.Create(conn, TablesSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var key = TableKey(schema, name);
            var displayName = DisplayTableName(schema, name);

            if (dict.TryGetValue(key, out var existing))
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.TableNameCollision,
                        displayName,
                        displayName,
                        existing.Entity.TableName
                    )
                );
                continue;
            }

            dict[key] = new SchemaTableEntry
            {
                Key = key,
                RawName = displayName,
                Entity = new Entity { TableName = displayName, Columns = new List<Column>() },
            };
        }

        return dict;
    }

    /// <summary>パーティション表（ヒープ・クラスター化インデックスのデータ領域がパーティション構成）を名指しするクエリ</summary>
    private const string PartitionedTablesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE EXISTS (
    SELECT 1
    FROM sys.indexes i
    JOIN sys.partition_schemes ps ON i.data_space_id = ps.data_space_id
    WHERE i.object_id = t.object_id AND i.index_id IN (0, 1)
)
ORDER BY s.name COLLATE Latin1_General_BIN2, t.name COLLATE Latin1_General_BIN2;";

    /// <summary>図が持たない列の属性（既定値・別名型・IDENTITY）を名指しするクエリ</summary>
    /// <remarks>
    /// 取込の列の読み込みは変えず、告げるためだけに読む。別名型は利用者が定義した型のうち、
    /// CLR 型（<c>is_assembly_type</c>）とテーブル型（<c>is_table_type</c>）でないもの（<c>CREATE TYPE … FROM</c>）。
    /// </remarks>
    private const string ColumnAttributesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME, c.name AS COLUMN_NAME,
       dc.definition AS DEFAULT_DEFINITION,
       CASE WHEN ty.is_user_defined = 1 AND ty.is_assembly_type = 0 AND ty.is_table_type = 0
            THEN SCHEMA_NAME(ty.schema_id) + N'.' + ty.name END AS ALIAS_TYPE,
       c.is_identity AS IS_IDENTITY,
       CAST(ic.seed_value AS nvarchar(40)) AS IDENTITY_SEED,
       CAST(ic.increment_value AS nvarchar(40)) AS IDENTITY_INCREMENT
FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE dc.object_id IS NOT NULL
   OR c.is_identity = 1
   OR (ty.is_user_defined = 1 AND ty.is_assembly_type = 0 AND ty.is_table_type = 0)
ORDER BY s.name COLLATE Latin1_General_BIN2, t.name COLLATE Latin1_General_BIN2, c.column_id;";

    /// <summary>パーティション表を、パーティション定義を持たない通常の表として取り込んだことを告げる</summary>
    private static async Task LoadPartitionedTablesAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, PartitionedTablesSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (
                SchemaTableEntry.TryGetExact(
                    tables,
                    TableKey(reader.GetString(0), reader.GetString(1)),
                    out var entry
                )
            )
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.PartitionDefinitionLost,
                        entry.Entity.TableName
                    )
                );
            }
        }
    }

    /// <summary>SQL Server が <c>DEFAULT NULL</c> をカタログ（<c>sys.default_constraints.definition</c>）に書く表記</summary>
    private static readonly string[] NullDefaultForms = ["(NULL)", "((NULL))"];

    /// <summary>図が持たない列の属性を、列ごとの取込警告（段 1）で名指しし、既定値の事実を返す</summary>
    /// <remarks>
    /// SQL Server の IDENTITY は既定値を持てないので、既定値は自動採番と重ならない。値が NULL の既定値
    /// （<c>(NULL)</c>）は告げない。
    /// </remarks>
    private static async Task<List<SchemaColumnDefault>> LoadColumnAttributesAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        var defaults = new List<SchemaColumnDefault>();
        await using var cmd = DbCommands.Create(conn, ColumnAttributesSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (
                !SchemaTableEntry.TryGetExact(
                    tables,
                    TableKey(reader.GetString(0), reader.GetString(1)),
                    out var entry
                ) || !entry.TryGetColumnExact(reader.GetString(2), out var column)
            )
            {
                continue;
            }

            var table = entry.Entity.TableName;
            var definition = reader.IsDBNull(3) ? null : reader.GetString(3);

            if (!SchemaImportWarnings.IsNullDefault(definition, NullDefaultForms))
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.ColumnDefaultLost,
                        table,
                        column.Name,
                        definition!
                    )
                );
                defaults.Add(new SchemaColumnDefault(table, column.Name, definition!));
            }

            if (!reader.IsDBNull(4))
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.AliasTypeFlattened,
                        table,
                        column.Name,
                        reader.GetString(4)
                    )
                );
            }

            // IDENTITY は指定した値を拒む（IDENTITY_INSERT を除く）ので、図に印を付けて告げる
            if (reader.GetBoolean(5))
            {
                column.IsIdentity = true;
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.IdentityColumnMarked,
                        table,
                        column.Name,
                        $"IDENTITY({reader.GetString(6)},{reader.GetString(7)})"
                    )
                );
            }
        }

        return defaults;
    }

    /// <summary>取り込まないオブジェクトを、種別ごとの取込警告（段 2）で名指しする</summary>
    private static async Task LoadExcludedObjectsAsync(
        SqlConnection conn,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, ExcludedObjectsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = DisplayTableName(reader.GetString(0), reader.GetString(1));
            var kind = reader.GetString(2) switch
            {
                "view" => SchemaImportWarningKind.ViewExcluded,
                "external" => SchemaImportWarningKind.ExternalTableExcluded,
                var other => throw new InvalidOperationException(
                    $"Unexpected object kind '{other}' for '{name}'."
                ),
            };
            warnings.Add(new SchemaImportWarning(kind, name));
        }
    }

    /// <summary>テンポラルテーブルの履歴表を洗い出し、取り込まなかったことを本表名つきで告げる</summary>
    /// <remarks>
    /// 履歴表自体は <see cref="TablesSql"/> の <c>temporal_type &lt;&gt; 1</c> により
    /// テーブル辞書へは元から入らない。ここでは告知専用に別途拾う（既存クエリの意味は変えない）。
    /// </remarks>
    private static async Task LoadTemporalHistoryTablesAsync(
        SqlConnection conn,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(
            conn,
            TemporalHistoryTablesSql,
            commandTimeoutSeconds
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var historyName = DisplayTableName(reader.GetString(0), reader.GetString(1));
            var baseName = DisplayTableName(reader.GetString(2), reader.GetString(3));

            warnings.Add(
                new SchemaImportWarning(
                    SchemaImportWarningKind.TemporalHistoryTableExcluded,
                    historyName,
                    Detail: baseName
                )
            );
        }
    }

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        List<SchemaImportWarning> warnings,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, ColumnsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);

            if (!SchemaTableEntry.TryGetExact(tables, TableKey(schema, table), out var entry))
            {
                continue;
            }

            var colName = reader.GetString(2);

            // 同じテーブルに同じ名前（SchemaNames の規則）の列があれば、後着は取り込まずに告げる
            if (!entry.TryBeginColumn(colName, warnings))
            {
                continue;
            }
            var dataType = reader.GetString(3);
            int? maxLen = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
            int? numPrec = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
            int? numScale = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6));
            var isNullable = string.Equals(
                reader.GetString(7),
                "YES",
                StringComparison.OrdinalIgnoreCase
            );

            // 計算列は sys.computed_columns に行がある列＝式は図に載せず「書き込めない」事実だけを運ぶ
            var isComputed = Convert.ToInt32(reader.GetValue(9)) != 0;

            // テンポラルテーブルの期間列（1 = ROW START・2 = ROW END）。HIDDEN でもここには現れる
            int? generatedAlwaysType = reader.IsDBNull(11)
                ? null
                : Convert.ToInt32(reader.GetValue(11));
            var isPeriodColumn = generatedAlwaysType is 1 or 2;

            // 小数秒の桁数（datetime2 / time / datetimeoffset で使う）。読まないと datetime2(3) が
            // datetime2（＝7 桁）として図へ入り、その図からの同期が精度を黙って 3 → 7 に変える
            int? datetimePrecision = reader.IsDBNull(12)
                ? null
                : Convert.ToInt32(reader.GetValue(12));

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(dataType, maxLen, numPrec, numScale, datetimePrecision),
                IsNullable = isNullable,
                IsComputed = isComputed,
            };

            if (isComputed)
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.ComputedColumnExpressionLost,
                        entry.Entity.TableName,
                        colName,
                        // 期間列は式ではなく生成規則を運ぶ。式は警告文へそのまま載るため、
                        // 行構造を壊さないよう制御文字を畳んでおく
                        isPeriodColumn
                            ? (
                                generatedAlwaysType == 1
                                    ? "GENERATED ALWAYS AS ROW START"
                                    : "GENERATED ALWAYS AS ROW END"
                            )
                            : SqlComment.Sanitize(
                                reader.IsDBNull(10) ? string.Empty : reader.GetString(10)
                            )
                    )
                );
            }

            entry.AddColumn(col, colName);
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正し、構成順を記録する</summary>
    /// <remarks>
    /// 構成順はクエリの <c>ORDER BY</c>（<c>ORDINAL_POSITION</c> 昇順）に任せ、到着順でそのまま積む。
    /// </remarks>
    private static async Task LoadPrimaryKeysAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, PrimaryKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (
                !SchemaTableEntry.TryGetExact(
                    tables,
                    TableKey(reader.GetString(0), reader.GetString(1)),
                    out var entry
                )
            )
            {
                continue;
            }

            if (entry.TryGetColumnExact(reader.GetString(2), out var col))
            {
                col.IsPrimaryKey = true;
                col.IsNullable = false;
                entry.Entity.PrimaryKeyColumnIds.Add(col.Id);
            }
        }
    }

    /// <summary>
    /// 拡張プロパティ <c>MS_Description</c> を取得し、エンティティ・カラムの説明へ反映する
    /// </summary>
    private static async Task LoadDescriptionsAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, DescriptionsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);

            if (!SchemaTableEntry.TryGetExact(tables, TableKey(schema, table), out var entry))
            {
                continue;
            }

            var description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

            if (reader.IsDBNull(2))
            {
                // 列名が NULL の行はテーブルレベルの説明
                entry.Entity.Description = description;
            }
            else
            {
                var colName = reader.GetString(2);

                if (entry.TryGetColumnExact(colName, out var col))
                {
                    col.Description = description;
                }
            }
        }
    }

    /// <summary>外部キーを読み込み、複合列を集約してリレーションへ変換する</summary>
    /// <remarks>
    /// 参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
    /// 衝突して捨てたテーブルが親・子どちらか一方でも該当する外部キーは取り込まない。
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        SqlConnection conn,
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
            var parentSchema = reader.GetString(1);
            var parentTable = reader.GetString(2);
            var parentCol = reader.GetString(3);
            var refSchema = reader.GetString(4);
            var refTable = reader.GetString(5);
            var refCol = reader.GetString(6);
            var deleteAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(8) ? null : reader.GetString(8)
            );
            var updateAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(9) ? null : reader.GetString(9)
            );

            // 衝突して捨てたテーブルが両端のどちらかなら、そのリレーションは作らない
            if (
                !SchemaTableEntry.TryGetExact(tables, TableKey(parentSchema, parentTable), out _)
                || !SchemaTableEntry.TryGetExact(tables, TableKey(refSchema, refTable), out _)
            )
            {
                continue;
            }

            var parentKey = TableKey(parentSchema, parentTable);
            var refKey = TableKey(refSchema, refTable);

            builder.Add(fkName, parentKey, parentCol, refKey, refCol, deleteAction, updateAction);
        }

        return builder.Build(tables);
    }

    /// <summary>無効化された外部キーを洗い出し、取り込まなかったことを制約名つきで告げる</summary>
    /// <remarks>
    /// <see cref="ForeignKeysSql"/> の <c>WHERE fk.is_disabled = 0</c> により無効な FK はそもそも
    /// リレーションへ現れない。ここでは告知専用に別途拾う（既存クエリの意味は変えない）。
    /// </remarks>
    private static async Task LoadDisabledForeignKeysAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(
            conn,
            DisabledForeignKeysSql,
            commandTimeoutSeconds
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var schema = reader.GetString(1);
            var table = reader.GetString(2);

            // 衝突して捨てたテーブルの FK を、採用したほうへ付け替えない
            if (!SchemaTableEntry.TryGetExact(tables, TableKey(schema, table), out var entry))
            {
                continue;
            }

            warnings.Add(
                new SchemaImportWarning(
                    SchemaImportWarningKind.DisabledConstraintExcluded,
                    entry.Entity.TableName,
                    fkName,
                    "FOREIGN KEY"
                )
            );
        }
    }

    /// <summary>UNIQUE 制約を読み込み、各エンティティの一意制約としてモデルへ載せる</summary>
    private static async Task LoadUniqueConstraintsAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var builder = new UniqueConstraintImportBuilder();

        await using (var cmd = DbCommands.Create(conn, UniqueConstraintSql, commandTimeoutSeconds))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);

                // 衝突して捨てたテーブルの制約を、採用したほうへ付け替えない
                if (!SchemaTableEntry.TryGetExact(tables, TableKey(schema, table), out _))
                {
                    continue;
                }

                var key = TableKey(schema, table);
                var constraintName = reader.GetString(2);
                var col = reader.GetString(3);
                builder.Add(key, constraintName, col, constraintName);
            }
        }

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());
    }

    /// <summary>SQL Server の型情報を <c>nvarchar(50)</c> や <c>decimal(10,2)</c> 等の表示形式へ整形する</summary>
    /// <remarks>
    /// 可変長型の最大長 -1 は <c>(max)</c> として表現する。数値と小数秒の桁数は書き下ろしたうえで
    /// <see cref="SqlServerTypeNotation.Normalize"/> を通す（既定の精度・スケール 0 を省く）＝差分の比較が
    /// 図の表記に掛ける正規化と同じ関数なので、取込の出力はその不動点になる。
    /// </remarks>
    /// <param name="dataType">カタログの型名（<c>DATA_TYPE</c>）</param>
    /// <param name="maxLen">文字列・バイナリの最大長（<c>CHARACTER_MAXIMUM_LENGTH</c>）</param>
    /// <param name="precision">数値の精度（<c>NUMERIC_PRECISION</c>）</param>
    /// <param name="scale">数値のスケール（<c>NUMERIC_SCALE</c>）</param>
    /// <param name="datetimePrecision">小数秒の桁数（<c>DATETIME_PRECISION</c>。datetime2 / time / datetimeoffset で使う）</param>
    public static string FormatDataType(
        string dataType,
        int? maxLen,
        int? precision,
        int? scale,
        int? datetimePrecision = null
    )
    {
        var dt = dataType.ToLowerInvariant();

        switch (dt)
        {
            case "char":
            case "varchar":
            case "nchar":
            case "nvarchar":
            case "binary":
            case "varbinary":

                if (maxLen is null)
                {
                    return dt;
                }

                return maxLen == -1 ? $"{dt}(max)" : $"{dt}({maxLen})";

            case "decimal":
            case "numeric":

                if (precision is null)
                {
                    return dt;
                }

                return SqlServerTypeNotation.Normalize($"{dt}({precision},{scale ?? 0})");

            case "datetime2":
            case "time":
            case "datetimeoffset":

                if (datetimePrecision is null)
                {
                    return dt;
                }

                return SqlServerTypeNotation.Normalize($"{dt}({datetimePrecision})");

            default:
                return dt;
        }
    }
}
