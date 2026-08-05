using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>MySQL のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// 接続先データベース（<c>DATABASE()</c>）の通常テーブルのみを対象とする。
/// <c>information_schema</c> の TABLES / COLUMNS / KEY_COLUMN_USAGE / TABLE_CONSTRAINTS /
/// REFERENTIAL_CONSTRAINTS / STATISTICS を用い、複合主キーは順序を保持する。
/// 型は <c>COLUMN_TYPE</c> 列（<c>varchar(50)</c> / <c>tinyint(1)</c> / <c>decimal(10,2)</c> 等、
/// カタログがそのまま解析できる表記）をそのまま採用する。
/// 参照先列集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
/// </remarks>
public class MySqlSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await ImportAsync(conn, cancellationToken).ConfigureAwait(false);
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

        /// <summary>意味モデルへ完全には写し取れなかった箇所の警告（現状は複合外部キーの列対応喪失のみ）</summary>
        public List<CompositeForeignKeyImportWarning> Warnings { get; init; } = new();
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    public async Task<SchemaResult> ImportAsync(
        MySqlConnection conn,
        CancellationToken ct = default
    )
    {
        var tables = await LoadTablesAsync(conn, ct).ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, ct).ConfigureAwait(false);
        var (rels, warnings) = await LoadForeignKeysAsync(conn, tables, ct).ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            Warnings = warnings,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>接続先 DB の通常テーブル一覧・テーブルコメントを取得するクエリ</summary>
    /// <remarks>MySQL では「スキーマ」="データベース" のため、TABLE_SCHEMA = DATABASE() で接続先 DB のみに絞る</remarks>
    private const string TablesSql =
        @"
SELECT TABLE_NAME, TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;";

    /// <summary>全テーブルのカラム定義を序数順に取得するクエリ</summary>
    /// <remarks>
    /// 型は COLUMN_TYPE（varchar(50) / tinyint(1) / decimal(10,2) 等）をそのまま採用する。
    /// information_schema のテーブル・カラム名は環境の照合順序次第で大文字小文字表記が揺れるため、
    /// 突き合わせ側の辞書は <see cref="StringComparer.OrdinalIgnoreCase"/> で受ける
    /// </remarks>
    private const string ColumnsSql =
        @"
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_COMMENT, ORDINAL_POSITION
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_NAME, ORDINAL_POSITION;";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    private const string PrimaryKeysSql =
        @"
SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'
ORDER BY TABLE_NAME, ORDINAL_POSITION;";

    /// <summary>主キー以外の一意制約の構成列を取得するクエリ（1 対 1 判定に用いる）</summary>
    /// <remarks>STATISTICS の NON_UNIQUE = 0 かつ主キー以外のインデックスを一意制約とみなす</remarks>
    private const string UniqueConstraintSql =
        @"
SELECT TABLE_NAME, INDEX_NAME, COLUMN_NAME, SEQ_IN_INDEX
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 AND INDEX_NAME <> 'PRIMARY'
ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX;";

    /// <summary>外部キーの親子テーブル・列・参照アクションを取得するクエリ</summary>
    /// <remarks>
    /// KEY_COLUMN_USAGE から列対応（複合 FK は POSITION_IN_UNIQUE_CONSTRAINT の順）を、
    /// REFERENTIAL_CONSTRAINTS から DELETE_RULE / UPDATE_RULE を取得する。
    /// </remarks>
    private const string ForeignKeysSql =
        @"
SELECT
    kcu.CONSTRAINT_NAME AS fk_name,
    kcu.TABLE_NAME AS parent_table, kcu.COLUMN_NAME AS parent_column,
    kcu.REFERENCED_TABLE_NAME AS ref_table, kcu.REFERENCED_COLUMN_NAME AS ref_column,
    kcu.ORDINAL_POSITION AS ordinal,
    rc.DELETE_RULE AS delete_action,
    rc.UPDATE_RULE AS update_action
FROM information_schema.KEY_COLUMN_USAGE kcu
JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
    ON rc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
    AND rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE kcu.CONSTRAINT_SCHEMA = DATABASE() AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;";

    /// <summary>テーブル一覧・テーブルコメントを読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        MySqlConnection conn,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, SchemaTableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new MySqlCommand(TablesSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var comment = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var entry = new SchemaTableEntry
            {
                Key = name,
                Entity = new Entity
                {
                    TableName = name,
                    Columns = new List<Column>(),
                    Description = comment,
                },
            };

            dict[entry.Key] = entry;
        }

        return dict;
    }

    /// <summary>各テーブルへカラム定義を読み込み、COLUMN_TYPE をそのまま型表記として追加する</summary>
    private static async Task LoadColumnsAsync(
        MySqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new MySqlCommand(ColumnsSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);

            if (!tables.TryGetValue(table, out var entry))
            {
                continue;
            }

            var colName = reader.GetString(1);
            var columnType = reader.GetString(2);
            var isNullable = string.Equals(
                reader.GetString(3),
                "YES",
                StringComparison.OrdinalIgnoreCase
            );
            var comment = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

            var col = new Column
            {
                Name = colName,
                DataType = columnType,
                IsNullable = isNullable,
                Description = comment,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正する</summary>
    private static async Task LoadPrimaryKeysAsync(
        MySqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        await using var cmd = new MySqlCommand(PrimaryKeysSql, conn);
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

    /// <summary>外部キーを読み込み、複合列を集約してリレーションへ変換する</summary>
    /// <remarks>
    /// 参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
    /// 複合外部キーは列対応を失うため、その旨の警告もあわせて返す。
    /// </remarks>
    private static async Task<(
        List<Relationship> Relationships,
        List<CompositeForeignKeyImportWarning> Warnings
    )> LoadForeignKeysAsync(
        MySqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        // 親テーブルの一意制約列集合を取得し、1 対 1 判定に用いる
        var uniqueSets = await LoadUniqueColumnSetsAsync(conn, ct).ConfigureAwait(false);

        var builder = new ForeignKeyRelationshipBuilder();

        await using (var cmd = new MySqlCommand(ForeignKeysSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var fkName = reader.GetString(0);
                var parentKey = reader.GetString(1); // FK 保有テーブル（子）
                var parentCol = reader.GetString(2);
                var refKey = reader.GetString(3); // 参照先テーブル（親・PK 側）
                var refCol = reader.GetString(4);
                var deleteAction = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(6) ? null : reader.GetString(6)
                );
                var updateAction = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(7) ? null : reader.GetString(7)
                );

                builder.Add(
                    fkName,
                    parentKey,
                    parentCol,
                    refKey,
                    refCol,
                    deleteAction,
                    updateAction
                );
            }
        }

        var rels = builder.Build(tables, uniqueSets);

        return (rels, builder.CompositeForeignKeyWarnings.ToList());
    }

    /// <summary>テーブルごとの一意制約列集合を取得する</summary>
    /// <returns>テーブル名 → 各一意制約の列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        MySqlConnection conn,
        CancellationToken ct
    )
    {
        var builder = new UniqueColumnSetBuilder();

        await using var cmd = new MySqlCommand(UniqueConstraintSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var indexName = reader.GetString(1);
            var col = reader.GetString(2);
            builder.Add(key, indexName, col);
        }

        return builder.Build();
    }
}
