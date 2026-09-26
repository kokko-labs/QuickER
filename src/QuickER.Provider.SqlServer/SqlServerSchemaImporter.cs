using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>指定の接続設定で接続を開きスキーマを取得する</summary>
    public async Task<SchemaResult> ImportAsync(
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
    public async Task<SchemaResult> ImportAsync(
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
    /// </remarks>
    private const string TablesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.is_ms_shipped = 0
  AND t.temporal_type <> 1
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
       gc.generated_always_type AS GENERATED_ALWAYS_TYPE
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
        var dict = new Dictionary<string, SchemaTableEntry>(StringComparer.OrdinalIgnoreCase);
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
                Entity = new Entity { TableName = displayName, Columns = new List<Column>() },
            };
        }

        return dict;
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

    /// <summary>取込対象として採用したテーブルを、大文字小文字まで一致させて引く</summary>
    /// <remarks>
    /// テーブル辞書は 5 方言共通の大文字小文字非依存なので、素で引くと「衝突して捨てたほうのテーブル」の
    /// 列・主キー・説明・一意制約・外部キーが、採用したほうのエンティティへ吸い寄せられて混ざる。
    /// カタログビューが返す名前はどのクエリでも同じ実体名なので、ここで序数一致まで求めても
    /// 正当な行を取りこぼすことはない。
    /// </remarks>
    private static bool TryGetExactTable(
        Dictionary<string, SchemaTableEntry> tables,
        string schema,
        string name,
        [NotNullWhen(true)] out SchemaTableEntry? entry
    )
    {
        var key = TableKey(schema, name);
        return tables.TryGetValue(key, out entry)
            && string.Equals(entry.Key, key, StringComparison.Ordinal);
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

            if (!TryGetExactTable(tables, schema, table, out var entry))
            {
                continue;
            }

            var colName = reader.GetString(2);
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

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(dataType, maxLen, numPrec, numScale),
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

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
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
            if (!TryGetExactTable(tables, reader.GetString(0), reader.GetString(1), out var entry))
            {
                continue;
            }

            if (entry.ColumnsByName.TryGetValue(reader.GetString(2), out var col))
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

            if (!TryGetExactTable(tables, schema, table, out var entry))
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

                if (entry.ColumnsByName.TryGetValue(colName, out var col))
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
                !TryGetExactTable(tables, parentSchema, parentTable, out _)
                || !TryGetExactTable(tables, refSchema, refTable, out _)
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
            if (!TryGetExactTable(tables, schema, table, out var entry))
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
                if (!TryGetExactTable(tables, schema, table, out _))
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
    /// <remarks>可変長型の最大長 -1 は <c>(max)</c> として表現する</remarks>
    public static string FormatDataType(string dataType, int? maxLen, int? precision, int? scale)
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

                return scale is > 0 ? $"{dt}({precision},{scale})" : $"{dt}({precision})";

            default:
                return dt;
        }
    }
}
