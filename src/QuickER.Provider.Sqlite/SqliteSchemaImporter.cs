using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.Sqlite;

/// <summary>SQLite のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <para>
/// <c>sqlite_master</c> で通常テーブルを列挙し、各テーブルの <c>PRAGMA table_info</c> /
/// <c>PRAGMA foreign_key_list</c> / <c>PRAGMA index_list</c> ＋ <c>PRAGMA index_info</c> で
/// 列・主キー・外部キー・一意制約を取得する。<c>sqlite_sequence</c> / <c>sqlite_stat*</c> 等の
/// 内部テーブルや <c>sqlite_</c> で始まるシステムテーブルは除外する。
/// </para>
/// <para>
/// ビュー・仮想テーブル（FTS5 / R*Tree 等）・付属表（シャドウテーブル）は対象外。<c>sqlite_master</c> の
/// <c>type</c> 列だけでは仮想テーブル本体も付属表も通常テーブルと同じ <c>'table'</c> として現れるため、
/// <c>PRAGMA table_list</c>（SQLite 3.37 以降・本リポジトリの同梱 SQLitePCLRaw.bundle_e_sqlite3 3.0.5 が
/// 満たす。実測 sqlite_version() = 3.53.4）が返す種別（<c>'table'</c> / <c>'view'</c> / <c>'shadow'</c> /
/// <c>'virtual'</c>）で判別し、<c>'virtual'</c> は取込から除外して警告を積み、<c>'shadow'</c> は無言で除外する。
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
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var result = await ImportAsync(conn, cancellationToken, commandTimeoutSeconds)
            .ConfigureAwait(false);
        return new SchemaImportResult
        {
            Entities = result.Entities,
            Relationships = result.Relationships,
            AuxiliaryObjects = result.AuxiliaryObjects,
            TableCreateSql = result.TableCreateSql,
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

        /// <summary>取得した補助オブジェクト（インデックス・トリガー・テーブルレベル一意制約）</summary>
        public List<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = new();

        /// <summary>テーブル名 → <c>CREATE TABLE</c> 文全文（再構築で失われる列属性の検出材料）</summary>
        public Dictionary<string, string> TableCreateSql { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム／主キー→一意制約→外部キーの順に段階的に補完していく</remarks>
    /// <param name="conn">既に開かれた接続</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。既定値付きで <paramref name="ct"/> の後ろに置くのは、
    /// 既存の位置指定呼び出し（統合テストの <c>ImportAsync(conn, ct)</c>）を壊さないため。
    /// </param>
    public async Task<SchemaResult> ImportAsync(
        SqliteConnection conn,
        CancellationToken ct = default,
        int commandTimeoutSeconds = DbCommands.DefaultTimeoutSeconds
    )
    {
        var (tables, tableCreateSql, tableWarnings) = await LoadTablesAsync(
                conn,
                commandTimeoutSeconds,
                ct
            )
            .ConfigureAwait(false);

        // 各テーブルの列・主キーは PRAGMA table_xinfo でまとめて取得する
        foreach (var entry in tables.Values)
        {
            await LoadColumnsAndPrimaryKeyAsync(
                    conn,
                    entry,
                    tableWarnings,
                    commandTimeoutSeconds,
                    ct
                )
                .ConfigureAwait(false);
        }

        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(
                conn,
                tables,
                tableWarnings,
                commandTimeoutSeconds,
                ct
            )
            .ConfigureAwait(false);
        var aux = await LoadAuxiliaryObjectsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);

        // DDL へ出せない型表記は、後で DDL / 同期を叩いた瞬間に図全体を止める。取込完了時に名指しする
        var warnings = tableWarnings
            .Concat(
                SchemaImportWarnings.DetectUnemittableColumnTypes(
                    tables.Values.Select(entry => entry.Entity)
                )
            )
            .ToList();

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            AuxiliaryObjects = aux,
            TableCreateSql = tableCreateSql,
            Warnings = warnings,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>通常テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// type='table' の実テーブルのみを対象とし、SQLite 内部テーブル（<c>sqlite_</c> 接頭辞）を除外する。
    /// ビューは <c>type='view'</c> のため元から対象外だが、仮想テーブル本体・付属表（シャドウテーブル）は
    /// ここでは <c>type='table'</c> として一緒に取れてしまう（区別は <see cref="LoadTablesAsync"/> が
    /// <c>PRAGMA table_list</c> 種別で行う）。
    /// </remarks>
    private const string TablesSql =
        @"
SELECT name, sql
FROM sqlite_master
WHERE type = 'table' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
ORDER BY name;";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書と CREATE 文全文を構築する</summary>
    /// <remarks>
    /// <c>CREATE TABLE</c> 文の原文（<c>sqlite_master.sql</c>）も同時に持ち帰る。意味モデルには載らない
    /// 列レベル属性（<c>AUTOINCREMENT</c> / <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> / 生成列）が
    /// テーブル再構築で失われることを、同期の実行前に警告するための材料になる。
    /// <para>
    /// <c>sqlite_master</c> 上では仮想テーブル本体・付属表（シャドウテーブル）も通常テーブルと同じ
    /// <c>type='table'</c> で現れるため、先に取得した <see cref="LoadTableTypesAsync"/> の
    /// <c>PRAGMA table_list</c> 種別で除外する。仮想テーブル本体は
    /// <see cref="SchemaImportWarningKind.VirtualTableExcluded"/> で名指しし、付属表は無言で除外する
    /// （付属表ごとの警告は本体の警告 1 件に代表させる）。
    /// </para>
    /// </remarks>
    private static async Task<(
        Dictionary<string, SchemaTableEntry> Tables,
        Dictionary<string, string> CreateSql,
        List<SchemaImportWarning> Warnings
    )> LoadTablesAsync(SqliteConnection conn, int commandTimeoutSeconds, CancellationToken ct)
    {
        var tableTypes = await LoadTableTypesAsync(conn, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);

        var dict = new Dictionary<string, SchemaTableEntry>(StringComparer.OrdinalIgnoreCase);
        var createSql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<SchemaImportWarning>();
        await using var cmd = DbCommands.Create(conn, TablesSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);

            if (tableTypes.TryGetValue(name, out var type))
            {
                if (string.Equals(type, "virtual", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(
                        new SchemaImportWarning(SchemaImportWarningKind.VirtualTableExcluded, name)
                    );
                    continue;
                }

                if (string.Equals(type, "shadow", StringComparison.OrdinalIgnoreCase))
                {
                    // 付属表は本体の VirtualTableExcluded 警告 1 件が代表するため無言で除外する
                    continue;
                }
            }

            var entry = new SchemaTableEntry
            {
                Key = name,
                Entity = new Entity { TableName = name, Columns = new List<Column>() },
            };

            dict[entry.Key] = entry;

            if (!reader.IsDBNull(1))
            {
                createSql[name] = reader.GetString(1);
            }
        }

        return (dict, createSql, warnings);
    }

    /// <summary>
    /// <c>PRAGMA table_list</c> から main スキーマの名前→種別（<c>table</c> / <c>view</c> / <c>shadow</c> /
    /// <c>virtual</c>）の対応を読み込む。
    /// </summary>
    /// <remarks>
    /// <c>schema</c> 列は main / temp の 2 系統を返すため main に限定する（一時テーブルは取込対象外）。
    /// </remarks>
    private static async Task<Dictionary<string, string>> LoadTableTypesAsync(
        SqliteConnection conn,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = DbCommands.Create(conn, "PRAGMA table_list;", commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);

            if (!string.Equals(schema, "main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = reader.GetString(1);
            var type = reader.GetString(2);
            result[name] = type;
        }

        return result;
    }

    /// <summary>1 テーブルの列定義と主キーを PRAGMA table_xinfo から読み込む</summary>
    /// <remarks>
    /// <para>
    /// table_xinfo の列は cid / name / type（宣言型）/ notnull / dflt_value /
    /// pk（PK なら構成順の 1 始まり）/ hidden。
    /// 宣言型はそのまま <see cref="Column.DataType"/> へ保持する（SQLite は verbatim に保存するため）。
    /// 行は cid（列定義順）で返るため、主キーの構成順は pk 値の昇順へ並べ替えてから記録する。
    /// </para>
    /// <para>
    /// <c>table_info</c> ではなく <c>table_xinfo</c> を使うのは、前者が<b>生成列を 1 行も返さない</b>ため
    /// （実測で確認）。生成列は hidden = 2（VIRTUAL）/ 3（STORED）として xinfo にだけ現れ、
    /// <see cref="Column.IsComputed"/> を立てて取り込む。hidden = 1 は仮想テーブルの隠し列で、
    /// 仮想テーブル自体を取り込まない（<see cref="SchemaImportWarningKind.VirtualTableExcluded"/>）以上
    /// 通常は現れないが、通常テーブルの列でない以上ここでも取り込まない。
    /// </para>
    /// <para>
    /// 生成列の式は PRAGMA からは取れない（<c>dflt_value</c> は NULL）ため、警告の
    /// <c>Detail</c> は空になる。
    /// </para>
    /// </remarks>
    private static async Task LoadColumnsAndPrimaryKeyAsync(
        SqliteConnection conn,
        SchemaTableEntry entry,
        List<SchemaImportWarning> warnings,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(
            conn,
            $"PRAGMA table_xinfo({SqliteIdentifier.QuoteSimple(entry.Key)});",
            commandTimeoutSeconds
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        // 主キー構成列を (pk 値, 列) で集め、読み終えてから構成順に並べ替える
        var primaryKeyColumns = new List<(long Ordinal, Column Column)>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var colName = reader.GetString(1);
            var declaredType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var notNull = reader.GetInt64(3) != 0;
            var pkOrdinal = reader.GetInt64(5);
            var hidden = reader.GetInt64(6);

            // hidden = 1 は仮想テーブルの隠し列＝通常テーブルの列ではないため取り込まない
            if (hidden == 1)
            {
                continue;
            }

            // hidden = 2（VIRTUAL）/ 3（STORED）は生成列
            var isComputed = hidden is 2 or 3;

            var col = new Column
            {
                Name = colName,
                // 宣言型が空（型なし宣言）の場合は SQLite の親和性上 BLOB 相当だが、
                // 生成・型変換のため既定型としてカタログ既定を補う
                DataType = string.IsNullOrWhiteSpace(declaredType) ? "BLOB" : declaredType,
                IsPrimaryKey = pkOrdinal > 0,
                // PK 列は NULL 非許容へ補正する
                IsNullable = pkOrdinal > 0 ? false : !notNull,
                IsComputed = isComputed,
            };

            if (isComputed)
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.ComputedColumnExpressionLost,
                        entry.Entity.TableName,
                        colName
                    )
                );
            }

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;

            if (pkOrdinal > 0)
            {
                primaryKeyColumns.Add((pkOrdinal, col));
            }
        }

        foreach (var (_, column) in primaryKeyColumns.OrderBy(pair => pair.Ordinal))
        {
            entry.Entity.PrimaryKeyColumnIds.Add(column.Id);
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
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var builder = new UniqueConstraintImportBuilder();

        foreach (var entry in tables.Values)
        {
            // index_list の列は seq / name / unique(0/1) / origin('c'=CREATE UNIQUE, 'u'=UNIQUE 制約, 'pk'=主キー) / partial
            var uniqueIndexes = new List<string>();
            await using (
                var listCmd = DbCommands.Create(
                    conn,
                    $"PRAGMA index_list({SqliteIdentifier.QuoteSimple(entry.Key)});",
                    commandTimeoutSeconds
                )
            )
            {
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
                await using var infoCmd = DbCommands.Create(
                    conn,
                    $"PRAGMA index_info({SqliteIdentifier.QuoteSimple(indexName)});",
                    commandTimeoutSeconds
                );
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
    /// <para>
    /// foreign_key_list の列は id / seq / table（参照先）/ from（子側列）/ to（親側列）/
    /// on_update / on_delete / match。同一 id が複合 FK の構成列を表すため、id ごとに集約する。
    /// </para>
    /// <para>
    /// SQLite は <c>CREATE TABLE</c> 時に参照先テーブルの実在を検査しないため、参照先が取込範囲
    /// （<paramref name="tables"/>）に無い FK が存在し得る（存在しないテーブルを参照する定義・
    /// 参照先を後から DROP した定義）。そのままではリレーションにできないため、
    /// <see cref="SchemaImportWarningKind.ForeignKeyOutsideScope"/> で名指しして除外する
    /// （<c>Subject</c> = 合成 FK 名 / <c>Detail</c> = 参照先テーブル名）。複合 FK の構成列は
    /// 同一 <c>id</c> に複数行で現れるため、警告は <c>id</c> 単位で 1 件にまとめる。
    /// </para>
    /// <para>
    /// 参照先テーブル自体は存在するが、参照先列（<c>to</c>）が省略された暗黙参照で主キーの列数と
    /// FK の列数が食い違う（<see cref="ResolvePrimaryKeyColumn"/> が解決不能）場合は、無効な
    /// スキーマ定義として従来どおり無告知で除外する（参照先が範囲内に実在する以上「範囲外」ではなく、
    /// 専用の警告種別を新設するほどの実例が無いため）。
    /// </para>
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        SqliteConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        List<SchemaImportWarning> warnings,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var builder = new ForeignKeyRelationshipBuilder();

        foreach (var entry in tables.Values)
        {
            await using var cmd = DbCommands.Create(
                conn,
                $"PRAGMA foreign_key_list({SqliteIdentifier.QuoteSimple(entry.Key)});",
                commandTimeoutSeconds
            );
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            // 同一 FK（id）で参照先テーブルが取込範囲に無いことを検出したら、複合 FK でも
            // 警告 1 件にまとめる（id 単位で重複を抑止）
            var missingRefWarned = new HashSet<long>();

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var seq = reader.GetInt64(1); // 同一 FK 内の構成列番号（0 始まり）
                var refTable = reader.GetString(2); // 参照先（親）テーブル
                var fromCol = reader.GetString(3); // 子側（FK 保有）列
                var toCol = reader.IsDBNull(4) ? null : reader.GetString(4); // 親側列
                var onUpdate = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(5) ? null : reader.GetString(5)
                );
                var onDelete = ForeignKeyReferentialActionHelper.Parse(
                    reader.IsDBNull(6) ? null : reader.GetString(6)
                );

                // SQLite の FK には制約名が無いため、テーブル名＋id で安定した合成名を作る
                var fkName = $"FK_{entry.Key}_{refTable}_{id}";

                // 参照先テーブルが取込範囲に無い（実在しない／取り込めなかった）場合は
                // リレーションにできないため、警告して除外する
                if (!tables.ContainsKey(refTable))
                {
                    if (missingRefWarned.Add(id))
                    {
                        warnings.Add(
                            new SchemaImportWarning(
                                SchemaImportWarningKind.ForeignKeyOutsideScope,
                                entry.Entity.TableName,
                                fkName,
                                refTable
                            )
                        );
                    }

                    continue;
                }

                // 参照先列（to）が NULL の場合は親テーブルの主キーを参照する（SQLite の暗黙参照）。
                // 暗黙参照の構成列は seq 順に主キーの実効順と対応するため、seq で引き当てる
                var refCol = toCol ?? ResolvePrimaryKeyColumn(tables, refTable, seq);

                if (refCol is null)
                {
                    continue;
                }

                builder.Add(fkName, entry.Key, fromCol, refTable, refCol, onDelete, onUpdate);
            }
        }

        return builder.Build(tables);
    }

    /// <summary>インデックス・トリガーの CREATE SQL 全文を取得するクエリ</summary>
    private const string AuxiliaryObjectsSql =
        @"
SELECT type, name, tbl_name, sql
FROM sqlite_master
WHERE type IN ('index','trigger') AND sql IS NOT NULL AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
ORDER BY name;";

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
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        var aux = new List<SchemaAuxiliaryObject>();

        // ---- インデックス・トリガー（CREATE SQL 全文を温存する）----
        await using (var cmd = DbCommands.Create(conn, AuxiliaryObjectsSql, commandTimeoutSeconds))
        {
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

    /// <summary>参照先テーブルの主キー構成列名を解決する（参照先列が省略された FK 用のフォールバック）</summary>
    /// <remarks>
    /// <c>REFERENCES t</c> 形の暗黙参照は親テーブルの主キーを構成順に参照するため、
    /// <c>foreign_key_list</c> の <c>seq</c>（0 始まり）で実効順
    /// （<see cref="Entity.GetPrimaryKeyColumnsInOrder"/>）の同じ位置の列を引き当てる。
    /// 範囲外（主キーの列数と FK の列数が食い違う不正なスキーマ）は解決不能として null を返す。
    /// </remarks>
    private static string? ResolvePrimaryKeyColumn(
        Dictionary<string, SchemaTableEntry> tables,
        string tableName,
        long seq
    )
    {
        if (!tables.TryGetValue(tableName, out var entry))
        {
            return null;
        }

        var pks = entry.Entity.GetPrimaryKeyColumnsInOrder();

        return seq >= 0 && seq < pks.Count ? pks[(int)seq].Name : null;
    }
}
