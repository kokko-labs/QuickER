using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Oracle;

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
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new OracleConnection(connectionString);
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
    public async Task<SchemaResult> ImportAsync(OracleConnection conn, CancellationToken ct = default)
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
        /// <summary>テーブル名（自スキーマの素の名前）</summary>
        public string Name { get; init; } = "";

        /// <summary>構築中のエンティティ</summary>
        public Entity Entity { get; init; } = new();

        /// <summary>列名からカラムを引くための索引（後続の PK / 説明 / FK 反映に用いる）</summary>
        public Dictionary<string, Column> ColumnsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>テーブルを一意に識別するキー（テーブル名）</summary>
        public string Key => Name;
    }

    /// <summary>自スキーマの通常テーブル一覧を取得するクエリ</summary>
    private const string TablesSql =
        "SELECT table_name FROM user_tables ORDER BY table_name";

    /// <summary>自スキーマ全テーブルのカラム定義を序数順に取得するクエリ</summary>
    /// <remarks>
    /// <c>char_length</c> は CHAR / VARCHAR2 / NCHAR / NVARCHAR2 系のみ有効な文字数であり、
    /// <c>RAW</c> 等のバイト列型では常に 0 になる。バイト長が必要な型のために <c>data_length</c> も取得する。
    /// </remarks>
    private const string ColumnsSql =
        @"SELECT table_name, column_name, data_type, data_precision, data_scale, char_length, nullable, column_id, data_length
FROM user_tab_columns
ORDER BY table_name, column_id";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    private const string PrimaryKeysSql =
        @"SELECT cc.table_name, cc.column_name, cc.position
FROM user_constraints c
JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
WHERE c.constraint_type = 'P'
ORDER BY cc.table_name, cc.position";

    /// <summary>主キー以外の一意制約の構成列を取得するクエリ（1 対 1 判定に用いる）</summary>
    private const string UniqueConstraintSql =
        @"SELECT cc.table_name, cc.constraint_name, cc.column_name, cc.position
FROM user_constraints c
JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
WHERE c.constraint_type = 'U'
ORDER BY cc.table_name, cc.constraint_name, cc.position";

    /// <summary>外部キーの親子テーブル・列・削除アクションを取得するクエリ</summary>
    /// <remarks>
    /// <c>delete_rule</c> は CASCADE / SET NULL / NO ACTION を表す。
    /// 子側は <c>user_cons_columns</c>、親側は参照先制約 <c>r_constraint_name</c> の構成列を position で突き合わせる。
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
WHERE c.constraint_type = 'R'
ORDER BY c.constraint_name, cc.position";

    /// <summary>テーブルコメントを取得するクエリ</summary>
    private const string TableCommentsSql =
        "SELECT table_name, comments FROM user_tab_comments WHERE comments IS NOT NULL";

    /// <summary>カラムコメントを取得するクエリ</summary>
    private const string ColumnCommentsSql =
        "SELECT table_name, column_name, comments FROM user_col_comments WHERE comments IS NOT NULL";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    private static async Task<Dictionary<string, TableEntry>> LoadTablesAsync(
        OracleConnection conn,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, TableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = TablesSql;
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
        OracleConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ColumnsSql;
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

            var col = new Column
            {
                Name = colName,
                DataType = FormatDataType(dataType, dataPrecision, dataScale, charLength, dataLength),
                IsNullable = isNullable,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正する</summary>
    private static async Task LoadPrimaryKeysAsync(
        OracleConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = PrimaryKeysSql;
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
        OracleConnection conn,
        Dictionary<string, TableEntry> tables,
        CancellationToken ct
    )
    {
        // テーブルコメント
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = TableCommentsSql;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (tables.TryGetValue(reader.GetString(0), out var entry) && !reader.IsDBNull(1))
                {
                    entry.Entity.Description = reader.GetString(1);
                }
            }
        }

        // カラムコメント
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = ColumnCommentsSql;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!tables.TryGetValue(reader.GetString(0), out var entry))
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
    /// <remarks>参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する</remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        OracleConnection conn,
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
                    ForeignKeyReferentialAction OnDelete
                )
            >();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ForeignKeysSql;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var childKey = reader.GetString(1); // FK 保有テーブル（子）
            var childCol = reader.GetString(2);
            var refKey = reader.GetString(3); // 参照先テーブル（親・PK 側）
            var refCol = reader.GetString(4);
            var deleteAction = MapReferentialAction(reader.IsDBNull(6) ? null : reader.GetString(6));

            if (!grouped.TryGetValue(fkName, out var g))
            {
                g = (childKey, refKey, new List<string>(), new List<string>(), deleteAction);
            }

            g.ParentCols.Add(childCol);
            g.RefCols.Add(refCol);
            grouped[fkName] = g;
        }

        foreach (var (fkName, g) in grouped)
        {
            if (!tables.TryGetValue(g.ParentKey, out var child))
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
                if (child.ColumnsByName.TryGetValue(pc, out var pcol))
                {
                    pcol.IsForeignKey = true;
                }
            }

            // FK 列集合が主キーまたは一意制約と一致すれば 1 対 1 とみなす
            var sortedChild = g
                .ParentCols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var pkCols = child
                .Entity.Columns.Where(c => c.IsPrimaryKey)
                .Select(c => c.Name)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var uniqueOnChild = uniqueSets.TryGetValue(g.ParentKey, out var sets)
                ? sets
                : new List<string[]>();

            var isOneToOne =
                SameSet(sortedChild, pkCols) || uniqueOnChild.Any(s => SameSet(sortedChild, s));

            rels.Add(
                new Relationship
                {
                    SourceEntityId = refer.Entity.Id, // 参照先 (PK 側) を起点として表示
                    TargetEntityId = child.Entity.Id, // FK 保有テーブル
                    Type = isOneToOne ? RelationshipType.OneToOne : RelationshipType.OneToMany,
                    SourceColumnId =
                        g.RefCols.Count == 1
                        && refer.ColumnsByName.TryGetValue(g.RefCols[0], out var refColumn)
                            ? refColumn.Id
                            : null,
                    TargetColumnId =
                        g.ParentCols.Count == 1
                        && child.ColumnsByName.TryGetValue(g.ParentCols[0], out var childColumn)
                            ? childColumn.Id
                            : null,
                    ConstraintName = fkName,
                    OnDelete = g.OnDelete,
                    // Oracle に ON UPDATE は存在しないため常に NoAction で取り込む
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                }
            );
        }

        return rels;
    }

    /// <summary>テーブルごとの一意制約列集合を取得する</summary>
    /// <returns>テーブル名 → 各一意制約の列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        OracleConnection conn,
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, List<string>>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = UniqueConstraintSql;
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
    /// </remarks>
    public static string FormatDataType(
        string dataType,
        int? dataPrecision,
        int? dataScale,
        int? charLength,
        int? dataLength = null
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
                    return "NUMBER";
                }

                // スケール 0 は精度のみ、0 超はスケールも付与する
                return dataScale is > 0
                    ? $"NUMBER({dataPrecision},{dataScale})"
                    : $"NUMBER({dataPrecision})";

            case "NVARCHAR2":
            case "VARCHAR2":
            case "NCHAR":
            case "CHAR":
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
