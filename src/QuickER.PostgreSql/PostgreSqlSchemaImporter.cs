using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>PostgreSQL のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <c>public</c> スキーマの通常テーブルのみを対象とする（SQL Server 版の <c>dbo</c> 相当）。
/// <c>information_schema</c> 系と <c>pg_catalog</c> を用い、複合主キーは順序を保持する。
/// 参照先列集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
/// </remarks>
public class PostgreSqlSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await ImportAsync(conn, cancellationToken).ConfigureAwait(false);
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

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    public async Task<SchemaResult> ImportAsync(
        NpgsqlConnection conn,
        CancellationToken ct = default
    )
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
        /// <summary>テーブル名（public スキーマの素の名前）</summary>
        public string Name { get; init; } = "";

        /// <summary>構築中のエンティティ</summary>
        public Entity Entity { get; init; } = new();

        /// <summary>列名からカラムを引くための索引（後続の PK / 説明 / FK 反映に用いる）</summary>
        public Dictionary<string, Column> ColumnsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>テーブルを一意に識別するキー（public スキーマのテーブル名）</summary>
        public string Key => Name;
    }

    /// <summary>public スキーマの通常テーブル一覧を取得するクエリ</summary>
    private const string TablesSql =
        @"
SELECT c.relname AS table_name
FROM pg_catalog.pg_class c
JOIN pg_catalog.pg_namespace n ON c.relnamespace = n.oid
WHERE n.nspname = 'public' AND c.relkind = 'r'
ORDER BY c.relname;";

    /// <summary>public スキーマ全テーブルのカラム定義を序数順に取得するクエリ</summary>
    private const string ColumnsSql =
        @"
SELECT table_name, column_name, data_type, udt_name,
       character_maximum_length, numeric_precision, numeric_scale, datetime_precision,
       is_nullable, ordinal_position
FROM information_schema.columns
WHERE table_schema = 'public'
ORDER BY table_name, ordinal_position;";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    private const string PrimaryKeysSql =
        @"
SELECT c.relname AS table_name, a.attname AS column_name, k.n AS ordinal
FROM pg_catalog.pg_constraint con
JOIN pg_catalog.pg_class c ON con.conrelid = c.oid
JOIN pg_catalog.pg_namespace ns ON c.relnamespace = ns.oid
CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, n)
JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum = k.attnum
WHERE con.contype = 'p' AND ns.nspname = 'public'
ORDER BY c.relname, k.n;";

    /// <summary>主キー以外の一意制約の構成列を取得するクエリ（1 対 1 判定に用いる）</summary>
    private const string UniqueConstraintSql =
        @"
SELECT c.relname AS table_name, con.conname AS constraint_name, a.attname AS column_name, k.n AS ordinal
FROM pg_catalog.pg_constraint con
JOIN pg_catalog.pg_class c ON con.conrelid = c.oid
JOIN pg_catalog.pg_namespace ns ON c.relnamespace = ns.oid
CROSS JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS k(attnum, n)
JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum = k.attnum
WHERE con.contype = 'u' AND ns.nspname = 'public'
ORDER BY c.relname, con.conname, k.n;";

    /// <summary>外部キーの親子テーブル・列・参照アクションを取得するクエリ</summary>
    /// <remarks>
    /// <c>confdeltype</c> / <c>confupdtype</c> は c=Cascade, n=SetNull, a=NoAction, r=Restrict（NoAction 扱い）,
    /// d=SetDefault を表す。<c>conkey</c> / <c>confkey</c> の序数を突き合わせて複合 FK の列対応を復元する。
    /// </remarks>
    private const string ForeignKeysSql =
        @"
SELECT
    con.conname AS fk_name,
    child.relname AS parent_table, ca.attname AS parent_column,
    parent.relname AS ref_table, pa.attname AS ref_column,
    cols.n AS ordinal,
    -- confdeltype / confupdtype は内部型 char（1 バイト）のため、
    -- Npgsql が String として読めるよう text へキャストする
    con.confdeltype::text AS delete_action,
    con.confupdtype::text AS update_action
FROM pg_catalog.pg_constraint con
JOIN pg_catalog.pg_class child ON con.conrelid = child.oid
JOIN pg_catalog.pg_namespace ns ON child.relnamespace = ns.oid
JOIN pg_catalog.pg_class parent ON con.confrelid = parent.oid
CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS cols(conkey, confkey, n)
JOIN pg_catalog.pg_attribute ca ON ca.attrelid = child.oid AND ca.attnum = cols.conkey
JOIN pg_catalog.pg_attribute pa ON pa.attrelid = parent.oid AND pa.attnum = cols.confkey
WHERE con.contype = 'f' AND ns.nspname = 'public'
ORDER BY con.conname, cols.n;";

    /// <summary>テーブル・カラムのコメント（obj_description / col_description）を一括取得するクエリ</summary>
    private const string DescriptionsSql =
        @"
SELECT c.relname AS table_name, a.attname AS column_name,
       col_description(c.oid, a.attnum) AS column_comment,
       obj_description(c.oid, 'pg_class') AS table_comment
FROM pg_catalog.pg_class c
JOIN pg_catalog.pg_namespace n ON c.relnamespace = n.oid
LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
WHERE n.nspname = 'public' AND c.relkind = 'r';";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    private static async Task<Dictionary<string, TableEntry>> LoadTablesAsync(
        NpgsqlConnection conn,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, TableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new NpgsqlCommand(TablesSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var entry = new TableEntry
            {
                Name = name,
                Entity = new Entity { TableName = name, Columns = new List<Column>() },
            };

            dict[entry.Key] = entry;
        }

        return dict;
    }

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        NpgsqlConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new NpgsqlCommand(ColumnsSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);

            if (!tables.TryGetValue(table, out var entry))
            {
                continue;
            }

            var colName = reader.GetString(1);
            var dataType = reader.GetString(2);
            var udtName = reader.IsDBNull(3) ? null : reader.GetString(3);
            int? charMaxLen = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4));
            int? numPrec = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));
            int? numScale = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6));
            int? dtPrec = reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7));
            var isNullable = string.Equals(
                reader.GetString(8),
                "YES",
                StringComparison.OrdinalIgnoreCase
            );

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(
                    dataType,
                    udtName,
                    charMaxLen,
                    numPrec,
                    numScale,
                    dtPrec
                ),
                IsNullable = isNullable,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正する</summary>
    private static async Task LoadPrimaryKeysAsync(
        NpgsqlConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new NpgsqlCommand(PrimaryKeysSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!tables.TryGetValue(reader.GetString(0), out var entry))
            {
                continue;
            }

            if (entry.ColumnsByName.TryGetValue(reader.GetString(1), out var col))
            {
                col.IsPrimaryKey = true;
                col.IsNullable = false;
            }
        }
    }

    /// <summary>テーブル・カラムのコメントを取得し、エンティティ・カラムの説明へ反映する</summary>
    private static async Task LoadDescriptionsAsync(
        NpgsqlConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new NpgsqlCommand(DescriptionsSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!tables.TryGetValue(reader.GetString(0), out var entry))
            {
                continue;
            }

            // テーブルコメント（全行に同値で載るため空でなければ設定する）
            if (!reader.IsDBNull(3))
            {
                entry.Entity.Description = reader.GetString(3);
            }

            // カラムコメント（列名が NULL の行はテーブルのみの行）
            if (!reader.IsDBNull(1) && !reader.IsDBNull(2))
            {
                var colName = reader.GetString(1);

                if (entry.ColumnsByName.TryGetValue(colName, out var col))
                {
                    col.Description = reader.GetString(2);
                }
            }
        }
    }

    /// <summary>外部キーを読み込み、複合列を集約してリレーションへ変換する</summary>
    /// <remarks>参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する</remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        NpgsqlConnection conn,
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

        await using var cmd = new NpgsqlCommand(ForeignKeysSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var parentKey = reader.GetString(1); // FK 保有テーブル（子）
            var parentCol = reader.GetString(2);
            var refKey = reader.GetString(3); // 参照先テーブル（親・PK 側）
            var refCol = reader.GetString(4);
            var deleteAction = MapReferentialAction(reader.IsDBNull(6) ? null : reader.GetString(6));
            var updateAction = MapReferentialAction(reader.IsDBNull(7) ? null : reader.GetString(7));

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

    /// <summary>テーブルごとの一意制約列集合を取得する</summary>
    /// <returns>テーブル名 → 各一意制約の列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        NpgsqlConnection conn,
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, List<string>>();

        await using var cmd = new NpgsqlCommand(UniqueConstraintSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var con = reader.GetString(1);
            var col = reader.GetString(2);
            var compositeKey = key + "::" + con;

            if (!current.TryGetValue(compositeKey, out var list))
            {
                list = new List<string>();
                current[compositeKey] = list;
            }

            list.Add(col);

            if (!result.ContainsKey(key))
            {
                result[key] = new List<string[]>();
            }
        }

        foreach (var kv in current)
        {
            var tableKey = kv.Key.Substring(0, kv.Key.IndexOf("::", StringComparison.Ordinal));

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

    /// <summary>pg_constraint.confdeltype / confupdtype の 1 文字コードを参照アクションへ変換する</summary>
    /// <remarks>c=Cascade / n=SetNull / d=SetDefault / a=NoAction / r=Restrict（NoAction 扱い）</remarks>
    private static ForeignKeyReferentialAction MapReferentialAction(string? code) =>
        code switch
        {
            "c" => ForeignKeyReferentialAction.Cascade,
            "n" => ForeignKeyReferentialAction.SetNull,
            "d" => ForeignKeyReferentialAction.SetDefault,
            _ => ForeignKeyReferentialAction.NoAction,
        };

    /// <summary>PostgreSQL の型情報を <c>varchar(50)</c> や <c>numeric(10,2)</c> 等の表示形式へ整形する</summary>
    /// <remarks>
    /// <c>information_schema.columns.data_type</c> は正規名（<c>character varying</c> 等）を返すため、
    /// <see cref="PostgreSqlTypeCatalog"/> が解析できる別名（<c>varchar</c> 等）へ寄せて長さ・精度を付与する。
    /// </remarks>
    public static string FormatDataType(
        string dataType,
        string? udtName,
        int? charMaxLen,
        int? numPrec,
        int? numScale,
        int? dtPrec
    )
    {
        var dt = dataType.ToLowerInvariant();

        switch (dt)
        {
            case "character varying":
                return charMaxLen is null ? "varchar" : $"varchar({charMaxLen})";

            case "character":
                return charMaxLen is null ? "char" : $"char({charMaxLen})";

            case "numeric":
            case "decimal":
                if (numPrec is null)
                {
                    return "numeric";
                }

                return numScale is > 0 ? $"numeric({numPrec},{numScale})" : $"numeric({numPrec})";

            case "timestamp without time zone":
                return dtPrec is null ? "timestamp" : $"timestamp({dtPrec})";

            case "timestamp with time zone":
                return dtPrec is null ? "timestamptz" : $"timestamptz({dtPrec})";

            case "time without time zone":
                return dtPrec is null ? "time" : $"time({dtPrec})";

            case "double precision":
                return "double precision";

            // information_schema が 'USER-DEFINED' / 'ARRAY' 等を返す場合は udt_name（uuid / jsonb 等）を優先する
            case "user-defined":
            case "array":
                return udtName ?? dt;

            default:
                return dt;
        }
    }
}
