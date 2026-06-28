using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Model;
using Microsoft.Data.SqlClient;

namespace QuickER.Services;

/// <summary>SQL Server のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <c>INFORMATION_SCHEMA</c> 系と <c>sys.foreign_keys</c> を用い、BASE TABLE のみ対象とする
/// 複合主キーは順序を保持する 多対多は中間テーブルとして 1 対多 × 2 の形で表現する
/// </remarks>
public class SqlServerSchemaImporter
{
    /// <summary>取得したスキーマを格納する結果 DTO</summary>
    public sealed class SchemaResult
    {
        /// <summary>取得したエンティティ一覧</summary>
        public List<Entity> Entities { get; init; } = new();

        /// <summary>取得したリレーション一覧</summary>
        public List<Relationship> Relationships { get; init; } = new();
    }

    /// <summary>スキーマ内容の一致比較に使う署名文字列を生成する（取込前後の置換要否判定に用いる）</summary>
    public static string ComputeSignature(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var e = string.Join(
            "|",
            entities
                .OrderBy(x => x.TableName)
                .Select(x =>
                    x.TableName
                    + ":"
                    + string.Join(
                        ",",
                        x.Columns.Select(c =>
                            c.Name
                            + "("
                            + c.DataType
                            + (c.IsPrimaryKey ? "*PK" : "")
                            + (c.IsNullable ? "*NULL" : "*NOTNULL")
                            + ")"
                        )
                    )
                )
        );
        var r = string.Join(
            "|",
            relationships
                .Select(x =>
                    x.SourceEntityId
                    + ">"
                    + x.TargetEntityId
                    + ":"
                    + x.Type
                    + ":"
                    + x.SourceColumnId
                    + ":"
                    + x.TargetColumnId
                    + ":"
                    + x.ConstraintName
                    + ":"
                    + x.OnDelete
                    + ":"
                    + x.OnUpdate
                )
                .OrderBy(s => s)
        );
        return e + "##" + r;
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
        return await ImportAsync(conn, ct).ConfigureAwait(false);
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    public async Task<SchemaResult> ImportAsync(SqlConnection conn, CancellationToken ct = default)
    {
        var tables = await LoadTablesAsync(conn, ct).ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, ct).ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, ct).ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>取込処理中にテーブルとその列を索引付きで保持する作業用エントリ</summary>
    private sealed class TableEntry
    {
        /// <summary>スキーマ名</summary>
        public string Schema { get; init; } = "";

        /// <summary>テーブル名</summary>
        public string Name { get; init; } = "";

        /// <summary>構築中のエンティティ</summary>
        public Entity Entity { get; init; } = new();

        /// <summary>列名からカラムを引くための索引（後続の PK / 説明 / FK 反映に用いる）</summary>
        public Dictionary<string, Column> ColumnsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>テーブルを一意に識別する <c>[schema].[name]</c> 形式のキー</summary>
        public string Key => $"[{Schema}].[{Name}]";
    }

    /// <summary>BASE TABLE 一覧を取得するクエリ</summary>
    private const string TablesSql =
        @"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;";

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

    /// <summary>主キー以外の一意インデックス構成列を取得するクエリ（1 対 1 判定に用いる）</summary>
    private const string UniqueIndexSql =
        @"
SELECT SCHEMA_NAME(t.schema_id) AS TableSchema, t.name AS TableName, i.name AS IndexName,
       c.name AS ColumnName, ic.key_ordinal AS Ordinal
FROM sys.indexes i
JOIN sys.tables  t  ON i.object_id = t.object_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND ic.is_included_column = 0
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
    private static async Task<Dictionary<string, TableEntry>> LoadTablesAsync(
        SqlConnection conn,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, TableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(TablesSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var entry = new TableEntry
            {
                Schema = schema,
                Name = name,
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
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(ColumnsSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var key = $"[{schema}].[{table}]";

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
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(PrimaryKeysSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = $"[{reader.GetString(0)}].[{reader.GetString(1)}]";

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
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(DescriptionsSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var key = $"[{schema}].[{table}]";

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
    /// <remarks>参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する</remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        SqlConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        // 親テーブルの一意制約列集合を取得し、1 対 1 判定に用いる
        var uniqueSets = await LoadUniqueColumnSetsAsync(conn, ct).ConfigureAwait(false);

        var rels = new List<Relationship>();
        var grouped =
            new Dictionary<
                string,
                (
                    string ParentKey,
                    string RefKey,
                    List<string> ParentCols,
                    List<string> RefCols,
                    ForeignKeyReferentialAction OnDelete,
                    ForeignKeyReferentialAction OnUpdate
                )
            >();

        await using var cmd = new SqlCommand(ForeignKeysSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var parentKey = $"[{reader.GetString(1)}].[{reader.GetString(2)}]";
            var parentCol = reader.GetString(3);
            var refKey = $"[{reader.GetString(4)}].[{reader.GetString(5)}]";
            var refCol = reader.GetString(6);
            var deleteAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(8) ? null : reader.GetString(8)
            );
            var updateAction = ForeignKeyReferentialActionHelper.Parse(
                reader.IsDBNull(9) ? null : reader.GetString(9)
            );

            if (!grouped.TryGetValue(fkName, out var g))
            {
                g = (
                    parentKey,
                    refKey,
                    new List<string>(),
                    new List<string>(),
                    deleteAction,
                    updateAction
                );
            }

            g.ParentCols.Add(parentCol);
            g.RefCols.Add(refCol);
            grouped[fkName] = g;
        }

        foreach (var (fkName, g) in grouped)
        {
            if (!tables.TryGetValue(g.ParentKey, out var parent))
            {
                continue;
            }

            if (!tables.TryGetValue(g.RefKey, out var refer))
            {
                continue;
            }

            // FK を構成する子側の列に IsForeignKey フラグを立てる
            foreach (var pc in g.ParentCols)
            {
                if (parent.ColumnsByName.TryGetValue(pc, out var pcol))
                {
                    pcol.IsForeignKey = true;
                }
            }

            // FK 列集合が主キーまたは一意制約と一致すれば 1 対 1 とみなす
            var sortedParent = g
                .ParentCols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var pkCols = parent
                .Entity.Columns.Where(c => c.IsPrimaryKey)
                .Select(c => c.Name)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var uniqueOnParent = uniqueSets.TryGetValue(g.ParentKey, out var sets)
                ? sets
                : new List<string[]>();

            var isOneToOne =
                SameSet(sortedParent, pkCols) || uniqueOnParent.Any(s => SameSet(sortedParent, s));

            rels.Add(
                new Relationship
                {
                    SourceEntityId = refer.Entity.Id, // 参照先 (PK 側) を起点として表示
                    TargetEntityId = parent.Entity.Id, // FK 保有テーブル
                    Type = isOneToOne ? RelationshipType.OneToOne : RelationshipType.OneToMany,
                    SourceColumnId =
                        g.RefCols.Count == 1
                        && refer.ColumnsByName.TryGetValue(g.RefCols[0], out var refColumn)
                            ? refColumn.Id
                            : null,
                    TargetColumnId =
                        g.ParentCols.Count == 1
                        && parent.ColumnsByName.TryGetValue(g.ParentCols[0], out var parentColumn)
                            ? parentColumn.Id
                            : null,
                    ConstraintName = fkName,
                    OnDelete = g.OnDelete,
                    OnUpdate = g.OnUpdate,
                }
            );
        }

        return rels;
    }

    /// <summary>テーブルごとの一意インデックス列集合を取得する</summary>
    /// <returns>テーブルキー → 各一意インデックスの列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        SqlConnection conn,
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, List<string>>();

        await using var cmd = new SqlCommand(UniqueIndexSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = $"[{reader.GetString(0)}].[{reader.GetString(1)}]";
            var idx = reader.GetString(2);
            var col = reader.GetString(3);
            var compositeKey = key + "::" + idx;

            if (!current.TryGetValue(compositeKey, out var list))
            {
                list = new List<string>();
                current[compositeKey] = list;
            }

            list.Add(col);

            if (!result.TryGetValue(key, out _))
            {
                result[key] = new List<string[]>();
            }
        }

        foreach (var kv in current)
        {
            var tableKey = kv.Key.Substring(0, kv.Key.IndexOf("::"));

            if (!result.TryGetValue(tableKey, out var lists))
            {
                lists = new List<string[]>();
                result[tableKey] = lists;
            }

            lists.Add(kv.Value.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        return result;
    }

    /// <summary>2 つのソート済み列名集合が大文字小文字無視で完全一致するか判定する（空集合は不一致）</summary>
    private static bool SameSet(string[] a, string[] b) =>
        a.Length > 0
        && a.Length == b.Length
        && a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);

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
