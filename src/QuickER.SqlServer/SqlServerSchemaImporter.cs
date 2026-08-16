using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>SQL Server のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <c>sys.tables</c> / <c>INFORMATION_SCHEMA</c> 系と <c>sys.foreign_keys</c> を用い、ユーザー定義テーブルのみ対象とする
/// （<c>is_ms_shipped</c> と拡張プロパティ <c>microsoft_database_tools_support</c> で sysdiagrams 等のツール用テーブルを除外）
/// 複合主キーは順序を保持する 多対多は中間テーブルとして 1 対多 × 2 の形で表現する
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
        };
    }

    /// <summary>取得したスキーマを格納する結果 DTO</summary>
    public sealed class SchemaResult
    {
        /// <summary>取得したエンティティ一覧</summary>
        public List<Entity> Entities { get; init; } = new();

        /// <summary>取得したリレーション一覧</summary>
        public List<Relationship> Relationships { get; init; } = new();
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
        var tables = await LoadTablesAsync(conn, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>スキーマ・テーブル名からテーブルキー（<c>[schema].[name]</c> 形式）を組み立てる</summary>
    private static string TableKey(string schema, string name) => $"[{schema}].[{name}]";

    /// <summary>ユーザー定義テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// SSMS のオブジェクトエクスプローラーと同じ基準でシステム由来のテーブルを除外する:
    /// <c>is_ms_shipped = 1</c>（Microsoft 出荷物）と、拡張プロパティ
    /// <c>microsoft_database_tools_support</c> が付いたツール用テーブル（sysdiagrams 等）
    /// </remarks>
    private const string TablesSql =
        @"
SELECT s.name AS TABLE_SCHEMA, t.name AS TABLE_NAME
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.is_ms_shipped = 0
  AND NOT EXISTS (
      SELECT 1
      FROM sys.extended_properties ep
      WHERE ep.class = 1
        AND ep.major_id = t.object_id
        AND ep.minor_id = 0
        AND ep.name = N'microsoft_database_tools_support'
  )
ORDER BY s.name, t.name;";

    /// <summary>全テーブルのカラム定義を序数順に取得するクエリ</summary>
    private const string ColumnsSql =
        @"
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE,
       CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;";

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
ORDER BY fk.name, fkc.constraint_column_id;";

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
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        SqlConnection conn,
        int commandTimeoutSeconds,
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
            var entry = new SchemaTableEntry
            {
                Key = TableKey(schema, name),
                Entity = new Entity
                {
                    TableName = schema == "dbo" ? name : $"{schema}.{name}",
                    Columns = new List<Column>(),
                },
            };

            dict[entry.Key] = entry;
        }

        return dict;
    }

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        SqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
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
            var key = TableKey(schema, table);

            if (!tables.TryGetValue(key, out var entry))
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

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(dataType, maxLen, numPrec, numScale),
                IsNullable = isNullable,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正する</summary>
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
            var key = TableKey(reader.GetString(0), reader.GetString(1));

            if (!tables.TryGetValue(key, out var entry))
            {
                continue;
            }

            if (entry.ColumnsByName.TryGetValue(reader.GetString(2), out var col))
            {
                col.IsPrimaryKey = true;
                col.IsNullable = false;
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
            var key = TableKey(schema, table);

            if (!tables.TryGetValue(key, out var entry))
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
            var parentKey = TableKey(reader.GetString(1), reader.GetString(2));
            var parentCol = reader.GetString(3);
            var refKey = TableKey(reader.GetString(4), reader.GetString(5));
            var refCol = reader.GetString(6);
            var deleteAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(8) ? null : reader.GetString(8)
            );
            var updateAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(9) ? null : reader.GetString(9)
            );

            builder.Add(fkName, parentKey, parentCol, refKey, refCol, deleteAction, updateAction);
        }

        return builder.Build(tables);
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
                var key = TableKey(reader.GetString(0), reader.GetString(1));
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
