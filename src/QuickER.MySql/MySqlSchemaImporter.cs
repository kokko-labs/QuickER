using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new MySqlConnection(connectionString);
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

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    /// <param name="conn">既に開かれた接続</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。既定値付きで <paramref name="ct"/> の後ろに置くのは、
    /// 既存の位置指定呼び出し（統合テストの <c>ImportAsync(conn, ct)</c>）を壊さないため。
    /// </param>
    public async Task<SchemaResult> ImportAsync(
        MySqlConnection conn,
        CancellationToken ct = default,
        int commandTimeoutSeconds = DbCommands.DefaultTimeoutSeconds
    )
    {
        var warnings = new List<SchemaImportWarning>();
        var tables = await LoadTablesAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);

        // 列が 1 本も取れなかったテーブルは DDL を生成できないため、黙って通さず名指しで告げる
        // （information_schema.COLUMNS は MySQL でも権限でフィルタされる）
        warnings.AddRange(
            tables
                .Values.Where(entry => entry.Entity.Columns.Count == 0)
                .Select(entry => new SchemaImportWarning(
                    SchemaImportWarningKind.TableColumnsUnavailable,
                    entry.Entity.TableName
                ))
        );

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

    /// <summary>接続先 DB の通常テーブル一覧・テーブルコメントを取得するクエリ</summary>
    /// <remarks>MySQL では「スキーマ」="データベース" のため、TABLE_SCHEMA = DATABASE() で接続先 DB のみに絞る</remarks>
    private const string TablesSql =
        @"
SELECT TABLE_NAME, TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
-- BINARY で並べるのは、information_schema の既定照合順序が大文字小文字を区別せず
-- `Dup` と `dup` の順序が環境依存になるため（衝突時にどちらを採るかを決定的にする）
ORDER BY BINARY TABLE_NAME;";

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

    /// <summary>UNIQUE 制約の構成列を宣言順に取得するクエリ（モデルの一意制約・1 対 1 判定に用いる）</summary>
    /// <remarks>
    /// MySQL は UNIQUE 制約と一意インデックスを区別しないため、STATISTICS の
    /// <c>NON_UNIQUE = 0</c> かつ主キー以外のインデックスを一意制約とみなす。
    /// ただし列そのものを一意にしないもの——プレフィックスインデックス（<c>SUB_PART IS NOT NULL</c>）と
    /// 関数インデックス（<c>COLUMN_NAME IS NULL</c>）——は、意味モデルの UNIQUE (列…) として
    /// 再現できないためインデックスごと除外する（1 列でも該当すればそのインデックス全体を落とす）。
    /// </remarks>
    private const string UniqueConstraintSql =
        @"
SELECT s.TABLE_NAME, s.INDEX_NAME, s.COLUMN_NAME, s.SEQ_IN_INDEX
FROM information_schema.STATISTICS s
WHERE s.TABLE_SCHEMA = DATABASE() AND s.NON_UNIQUE = 0 AND s.INDEX_NAME <> 'PRIMARY'
  AND NOT EXISTS (
      SELECT 1
      FROM information_schema.STATISTICS x
      WHERE x.TABLE_SCHEMA = s.TABLE_SCHEMA
        AND x.TABLE_NAME = s.TABLE_NAME
        AND x.INDEX_NAME = s.INDEX_NAME
        AND (x.SUB_PART IS NOT NULL OR x.COLUMN_NAME IS NULL)
  )
ORDER BY s.TABLE_NAME, s.INDEX_NAME, s.SEQ_IN_INDEX;";

    /// <summary>外部キーの親子テーブル・列・参照アクションを取得するクエリ</summary>
    /// <remarks>
    /// <para>
    /// KEY_COLUMN_USAGE から列対応（複合 FK は POSITION_IN_UNIQUE_CONSTRAINT の順）を、
    /// REFERENTIAL_CONSTRAINTS から DELETE_RULE / UPDATE_RULE を取得する。
    /// </para>
    /// <para>
    /// 参照先のデータベース <c>ref_schema</c> も選ぶ。取込範囲は接続先 DB だけなので、他 DB を参照する FK は
    /// 参照先テーブルが図に無く、リレーションを作れない。クエリで落とさず C# 側で弾くのは、
    /// 「黙って消えた」ではなく警告として告げるため。
    /// </para>
    /// </remarks>
    private const string ForeignKeysSql =
        @"
SELECT
    kcu.CONSTRAINT_NAME AS fk_name,
    kcu.TABLE_NAME AS parent_table, kcu.COLUMN_NAME AS parent_column,
    kcu.REFERENCED_TABLE_NAME AS ref_table, kcu.REFERENCED_COLUMN_NAME AS ref_column,
    kcu.ORDINAL_POSITION AS ordinal,
    rc.DELETE_RULE AS delete_action,
    rc.UPDATE_RULE AS update_action,
    kcu.REFERENCED_TABLE_SCHEMA AS ref_schema,
    -- 範囲内かどうかの判定は MySQL 自身に任せる（DB 名の照合順序・大文字小文字の扱いが環境依存のため、
    -- C# 側で接続先 DB 名と文字列比較すると環境によって全 FK を取りこぼす）
    CASE WHEN kcu.REFERENCED_TABLE_SCHEMA = DATABASE() THEN 1 ELSE 0 END AS ref_in_scope
FROM information_schema.KEY_COLUMN_USAGE kcu
JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
    ON rc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
    AND rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
WHERE kcu.CONSTRAINT_SCHEMA = DATABASE() AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY kcu.CONSTRAINT_NAME, kcu.ORDINAL_POSITION;";

    /// <summary>テーブル一覧・テーブルコメントを読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    /// <remarks>
    /// 辞書は 5 方言共通の大文字小文字非依存だが、MySQL も <c>lower_case_table_names = 0</c>
    /// （Linux の既定）では <c>Dup</c> と <c>dup</c> が共存する。黙って上書きすると 1 エンティティへ潰れて
    /// 両テーブルの列が混ざるため、後着（<c>BINARY TABLE_NAME</c> 昇順で後ろ）を取り込まず警告として告げる。
    /// </remarks>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        MySqlConnection conn,
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
            var name = reader.GetString(0);
            var comment = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);

            if (dict.TryGetValue(name, out var existing))
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.TableNameCollision,
                        name,
                        name,
                        existing.Entity.TableName
                    )
                );
                continue;
            }

            dict[name] = new SchemaTableEntry
            {
                Key = name,
                Entity = new Entity
                {
                    TableName = name,
                    Columns = new List<Column>(),
                    Description = comment,
                },
            };
        }

        return dict;
    }

    /// <summary>取込対象として採用したテーブルを、大文字小文字まで一致させて引く</summary>
    /// <remarks>
    /// テーブル辞書は 5 方言共通の大文字小文字非依存なので、素で引くと「衝突して捨てたほうのテーブル」の
    /// 列・制約が、採用したほうのエンティティへ吸い寄せられて混ざる。information_schema が返す名前は
    /// どのクエリでも同じ実体名なので、ここで序数一致まで求めても正当な行を取りこぼすことはない。
    /// </remarks>
    private static bool TryGetExactTable(
        Dictionary<string, SchemaTableEntry> tables,
        string tableName,
        [NotNullWhen(true)] out SchemaTableEntry? entry
    ) =>
        tables.TryGetValue(tableName, out entry)
        && string.Equals(entry.Key, tableName, StringComparison.Ordinal);

    /// <summary>各テーブルへカラム定義を読み込み、COLUMN_TYPE をそのまま型表記として追加する</summary>
    private static async Task LoadColumnsAsync(
        MySqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, ColumnsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);

            if (!TryGetExactTable(tables, table, out var entry))
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
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, PrimaryKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!TryGetExactTable(tables, reader.GetString(0), out var entry))
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
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        MySqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        var builder = new ForeignKeyRelationshipBuilder();
        // 取込範囲外を参照する FK は制約単位で 1 度だけ告げる（複合 FK は行が複数出るため）
        var reportedOutOfScope = new HashSet<string>(StringComparer.Ordinal);

        await using (var cmd = DbCommands.Create(conn, ForeignKeysSql, commandTimeoutSeconds))
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

                // 参照先が接続先 DB の外＝図に親テーブルが無いのでリレーションを作れない。除外して告げる
                if (Convert.ToInt32(reader.GetValue(9)) == 0)
                {
                    if (reportedOutOfScope.Add(fkName))
                    {
                        var refSchema = reader.IsDBNull(8) ? "" : reader.GetString(8);
                        warnings.Add(
                            new SchemaImportWarning(
                                SchemaImportWarningKind.ForeignKeyOutsideScope,
                                parentKey,
                                fkName,
                                $"{refSchema}.{refKey}"
                            )
                        );
                    }

                    continue;
                }

                // 衝突して捨てたテーブルが両端のどちらかなら、そのリレーションは作らない
                if (
                    !TryGetExactTable(tables, parentKey, out _)
                    || !TryGetExactTable(tables, refKey, out _)
                )
                {
                    continue;
                }

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

        return builder.Build(tables);
    }

    /// <summary>UNIQUE 制約を読み込み、各エンティティの一意制約としてモデルへ載せる</summary>
    private static async Task LoadUniqueConstraintsAsync(
        MySqlConnection conn,
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
                var key = reader.GetString(0);

                // 衝突して捨てたテーブルの制約を、採用したほうへ付け替えない
                if (!TryGetExactTable(tables, key, out _))
                {
                    continue;
                }

                var indexName = reader.GetString(1);
                var col = reader.GetString(2);
                builder.Add(key, indexName, col, indexName);
            }
        }

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());
    }
}
