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
            AuxiliaryObjects = result.AuxiliaryObjects,
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

        /// <summary>取得した補助オブジェクト（インデックス・トリガー・テーブルレベル一意制約）</summary>
        public List<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = new();

        /// <summary>意味モデルへ完全には写し取れなかった箇所の警告（現状は複合外部キーの列対応喪失のみ）</summary>
        public List<CompositeForeignKeyImportWarning> Warnings { get; init; } = new();
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

        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, ct).ConfigureAwait(false);
        var (rels, warnings) = await LoadForeignKeysAsync(conn, tables, ct).ConfigureAwait(false);
        var aux = await LoadAuxiliaryObjectsAsync(conn, tables, ct).ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            AuxiliaryObjects = aux,
            Warnings = warnings,
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

    /// <summary>テーブルレベルの UNIQUE 制約を PRAGMA index_list / index_info から読み込み、モデルへ載せる</summary>
    /// <remarks>
    /// <para>
    /// 対象は <c>origin='u'</c>（<c>CREATE TABLE</c> 内の <c>UNIQUE</c> 句）のみに限定する。
    /// <c>origin='c'</c>（<c>CREATE UNIQUE INDEX</c>）は制約ではなくインデックスなので取り込まない
    /// （5 方言で「真の UNIQUE 制約のみ」に線引きを揃えるため）。<c>origin='pk'</c> は主キー判定側の担当。
    /// </para>
    /// <para>
    /// SQLite の <c>UNIQUE</c> 句は <c>sqlite_autoindex_*</c> という自動名しか持たず、DDL へ書き戻す名前として
    /// 意味を成さないため、モデルの制約名は <c>null</c>（＝出力時に合成する）とする。
    /// </para>
    /// </remarks>
    private static async Task LoadUniqueConstraintsAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        var builder = new UniqueConstraintImportBuilder();

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

                    if (isUnique && string.Equals(origin, "u", StringComparison.OrdinalIgnoreCase))
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
                    // index_info の列は seqno / cid / name（構成列名。seqno 昇順で宣言順を保つ）
                    if (!infoReader.IsDBNull(2))
                    {
                        // 集約キーには自動名を使うが、モデルへ保存する名前は null にする
                        builder.Add(
                            entry.Key,
                            indexName,
                            infoReader.GetString(2),
                            persistedName: null
                        );
                    }
                }
            }
        }

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());
    }

    /// <summary>外部キーを PRAGMA foreign_key_list で読み込み、リレーションへ変換する</summary>
    /// <remarks>
    /// foreign_key_list の列は id / seq / table（参照先）/ from（子側列）/ to（親側列）/
    /// on_update / on_delete / match。同一 id が複合 FK の構成列を表すため、id ごとに集約する。
    /// 複合外部キーは列対応を失うため、その旨の警告もあわせて返す。
    /// </remarks>
    private static async Task<(
        List<Relationship> Relationships,
        List<CompositeForeignKeyImportWarning> Warnings
    )> LoadForeignKeysAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
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

        var rels = builder.Build(tables);

        return (rels, builder.CompositeForeignKeyWarnings.ToList());
    }

    /// <summary>
    /// テーブルに付随する補助オブジェクト（インデックス・トリガー）を収集する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sqlite_master</c> から <c>sql IS NOT NULL</c>（＝ユーザー定義の CREATE 文がある）かつ <c>sqlite_</c>
    /// 接頭辞でないものを取り込み、CREATE SQL 全文を温存する（自動インデックス <c>sqlite_autoindex_*</c> は
    /// <c>sql IS NULL</c> のため除外される）。
    /// </para>
    /// <para>
    /// テーブルレベルの一意制約（<c>CREATE TABLE</c> 内の <c>UNIQUE (...)</c>）はここでは扱わない。
    /// 意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本で、取込は
    /// <see cref="UniqueConstraintImportBuilder"/> が担う。
    /// </para>
    /// </remarks>
    private static async Task<List<SchemaAuxiliaryObject>> LoadAuxiliaryObjectsAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        var aux = new List<SchemaAuxiliaryObject>();

        // ---- インデックス・トリガー（CREATE SQL 全文を温存する）----
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                @"
SELECT type, name, tbl_name, sql
FROM sqlite_master
WHERE type IN ('index','trigger') AND sql IS NOT NULL AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                var tableName = reader.GetString(2);
                var sql = reader.GetString(3);

                // 取込対象テーブルに紐づくものだけを収集する（ビュー等に対する定義は対象外）
                if (!tables.ContainsKey(tableName))
                {
                    continue;
                }

                aux.Add(
                    new SchemaAuxiliaryObject
                    {
                        TableName = tableName,
                        Name = name,
                        Kind = string.Equals(type, "trigger", StringComparison.OrdinalIgnoreCase)
                            ? SchemaAuxiliaryObjectKind.Trigger
                            : SchemaAuxiliaryObjectKind.Index,
                        CreateSql = sql,
                    }
                );
            }
        }

        return aux;
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
