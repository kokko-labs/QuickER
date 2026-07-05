using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Sqlite;

/// <summary>SQLite のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <para>
/// <c>sqlite_master</c> で通常テーブルを列挙し、各テーブルの <c>PRAGMA table_info</c> /
/// <c>PRAGMA foreign_key_list</c> / <c>PRAGMA index_list</c> ＋ <c>PRAGMA index_info</c> で
/// 列・主キー・外部キー・一意制約を取得する。<c>sqlite_sequence</c> / <c>sqlite_stat*</c> 等の
/// 内部テーブルや <c>sqlite_</c> で始まるシステムテーブルは除外する。
/// </para>
/// <para>
/// 宣言型（例: <c>NVARCHAR(50)</c>）は verbatim に保持されるため、そのままモデルの
/// <see cref="Column.DataType"/> に格納する（<see cref="SqliteTypeCatalog"/> が読み戻せる）。
/// 参照先列集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する（共有部品に委譲）。
/// </para>
/// </remarks>
public class SqliteSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new SqliteConnection(connectionString);
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
    /// <remarks>テーブル→カラム／主キー→一意制約→外部キーの順に段階的に補完していく</remarks>
    public async Task<SchemaResult> ImportAsync(
        SqliteConnection conn,
        CancellationToken ct = default
    )
    {
        var tables = await LoadTablesAsync(conn, ct).ConfigureAwait(false);

        // 各テーブルの列・主キーは PRAGMA table_info でまとめて取得する
        foreach (var entry in tables.Values)
        {
            await LoadColumnsAndPrimaryKeyAsync(conn, entry, ct).ConfigureAwait(false);
        }

        var uniqueSets = await LoadUniqueColumnSetsAsync(conn, tables, ct).ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, uniqueSets, ct).ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>通常テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// type='table' の実テーブルのみを対象とし、SQLite 内部テーブル（<c>sqlite_</c> 接頭辞）を除外する。
    /// ビュー・仮想テーブルは対象外。
    /// </remarks>
    private const string TablesSql =
        @"
SELECT name
FROM sqlite_master
WHERE type = 'table' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
ORDER BY name;";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        SqliteConnection conn,
        CancellationToken ct
    )
    {
        var dict = new Dictionary<string, SchemaTableEntry>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = TablesSql;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var entry = new SchemaTableEntry
            {
                Key = name,
                Entity = new Entity { TableName = name, Columns = new List<Column>() },
            };

            dict[entry.Key] = entry;
        }

        return dict;
    }

    /// <summary>1 テーブルの列定義と主キーを PRAGMA table_info から読み込む</summary>
    /// <remarks>
    /// table_info の列は cid / name / type（宣言型）/ notnull / dflt_value / pk（PK なら構成順の 1 始まり）。
    /// 宣言型はそのまま <see cref="Column.DataType"/> へ保持する（SQLite は verbatim に保存するため）。
    /// </remarks>
    private static async Task LoadColumnsAndPrimaryKeyAsync(
        SqliteConnection conn,
        SchemaTableEntry entry,
        CancellationToken ct
    )
    {
        await using var cmd = conn.CreateCommand();
        // PRAGMA はパラメータ化できないため、識別子として二重引用符でクォートして埋め込む
        cmd.CommandText = $"PRAGMA table_info({SqliteIdentifier.Quote(entry.Key)});";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var colName = reader.GetString(1);
            var declaredType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var notNull = reader.GetInt64(3) != 0;
            var pkOrdinal = reader.GetInt64(5);

            var col = new Column
            {
                Name = colName,
                // 宣言型が空（型なし宣言）の場合は SQLite の親和性上 BLOB 相当だが、
                // 生成・型変換のため既定型としてカタログ既定を補う
                DataType = string.IsNullOrWhiteSpace(declaredType) ? "BLOB" : declaredType,
                IsPrimaryKey = pkOrdinal > 0,
                // PK 列は NULL 非許容へ補正する
                IsNullable = pkOrdinal > 0 ? false : !notNull,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>テーブルごとの一意制約（PK 以外）列集合を PRAGMA index_list / index_info から取得する</summary>
    /// <returns>テーブルキー → 各一意インデックスの列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        var builder = new UniqueColumnSetBuilder();

        foreach (var entry in tables.Values)
        {
            // index_list の列は seq / name / unique(0/1) / origin('c'=CREATE UNIQUE, 'u'=UNIQUE 制約, 'pk'=主キー) / partial
            var uniqueIndexes = new List<string>();
            await using (var listCmd = conn.CreateCommand())
            {
                listCmd.CommandText = $"PRAGMA index_list({SqliteIdentifier.Quote(entry.Key)});";
                await using var listReader = await listCmd
                    .ExecuteReaderAsync(ct)
                    .ConfigureAwait(false);

                while (await listReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var indexName = listReader.GetString(1);
                    var isUnique = listReader.GetInt64(2) != 0;
                    var origin = listReader.IsDBNull(3) ? string.Empty : listReader.GetString(3);

                    // 主キー由来（origin='pk'）は PK 判定側で扱うため一意制約集合からは除外する
                    if (
                        isUnique && !string.Equals(origin, "pk", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        uniqueIndexes.Add(indexName);
                    }
                }
            }

            foreach (var indexName in uniqueIndexes)
            {
                await using var infoCmd = conn.CreateCommand();
                infoCmd.CommandText = $"PRAGMA index_info({SqliteIdentifier.Quote(indexName)});";
                await using var infoReader = await infoCmd
                    .ExecuteReaderAsync(ct)
                    .ConfigureAwait(false);

                while (await infoReader.ReadAsync(ct).ConfigureAwait(false))
                {
                    // index_info の列は seqno / cid / name（構成列名）
                    if (!infoReader.IsDBNull(2))
                    {
                        builder.Add(entry.Key, indexName, infoReader.GetString(2));
                    }
                }
            }
        }

        return builder.Build();
    }

    /// <summary>外部キーを PRAGMA foreign_key_list で読み込み、リレーションへ変換する</summary>
    /// <remarks>
    /// foreign_key_list の列は id / seq / table（参照先）/ from（子側列）/ to（親側列）/
    /// on_update / on_delete / match。同一 id が複合 FK の構成列を表すため、id ごとに集約する。
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        Dictionary<string, List<string[]>> uniqueSets,
        CancellationToken ct
    )
    {
        var builder = new ForeignKeyRelationshipBuilder();

        foreach (var entry in tables.Values)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA foreign_key_list({SqliteIdentifier.Quote(entry.Key)});";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var refTable = reader.GetString(2); // 参照先（親）テーブル
                var fromCol = reader.GetString(3); // 子側（FK 保有）列
                var toCol = reader.IsDBNull(4) ? null : reader.GetString(4); // 親側列
                var onUpdate = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(5) ? null : reader.GetString(5)
                );
                var onDelete = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(6) ? null : reader.GetString(6)
                );

                // 参照先列（to）が NULL の場合は親テーブルの主キーを参照する（SQLite の暗黙参照）
                var refCol = toCol ?? ResolvePrimaryKeyColumn(tables, refTable);

                if (refCol is null)
                {
                    continue;
                }

                // SQLite の FK には制約名が無いため、テーブル名＋id で安定した合成名を作る
                var fkName = $"FK_{entry.Key}_{refTable}_{id}";
                builder.Add(fkName, entry.Key, fromCol, refTable, refCol, onDelete, onUpdate);
            }
        }

        return builder.Build(tables, uniqueSets);
    }

    /// <summary>参照先テーブルの主キー先頭列名を解決する（参照先列が省略された FK 用のフォールバック）</summary>
    private static string? ResolvePrimaryKeyColumn(
        Dictionary<string, SchemaTableEntry> tables,
        string tableName
    )
    {
        if (!tables.TryGetValue(tableName, out var entry))
        {
            return null;
        }

        return entry.Entity.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name;
    }
}
