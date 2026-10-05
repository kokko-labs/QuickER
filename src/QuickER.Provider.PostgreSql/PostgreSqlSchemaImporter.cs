using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Provider.PostgreSql;

/// <summary>PostgreSQL のテーブル定義を取得し <see cref="Entity"/> / <see cref="Relationship"/> へ変換するインポーター</summary>
/// <remarks>
/// <para>
/// <c>public</c> スキーマの通常テーブルとパーティション親テーブルのみを対象とする（SQL Server 版の <c>dbo</c> 相当）。
/// 照会は <c>pg_catalog</c> を直接引き、複合主キーは順序を保持する。
/// 参照先列集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
/// </para>
/// <para>
/// 列の取得に <c>information_schema.columns</c> を使わないのは、このビューが「接続ロールがその列に
/// 何らかの権限を持つ行」だけを返すため。テーブルは見えるのに列が 1 本も返らない（＝列ゼロのテーブルとして
/// 静かに取り込まれる）構成が実在するため、権限フィルタの掛からない <c>pg_attribute</c> を情報源にする。
/// </para>
/// </remarks>
public partial class PostgreSqlSchemaImporter : ISchemaImporter
{
    /// <summary>接続文字列で接続を開きスキーマを取得する（<see cref="ISchemaImporter"/> 実装・CLI scaffold 用）</summary>
    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ImportAsync(conn, cancellationToken, commandTimeoutSeconds)
            .ConfigureAwait(false);
    }

    /// <summary>既に開かれた接続でスキーマを取得する（テストや接続再利用向け）</summary>
    /// <remarks>テーブル→カラム→主キー→説明→外部キーの順に段階的に補完していく</remarks>
    /// <param name="conn">既に開かれた接続</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。既定値付きで <paramref name="ct"/> の後ろに置くのは、
    /// 既存の位置指定呼び出し（統合テストの <c>ImportAsync(conn, ct)</c>）を壊さないため。
    /// </param>
    public async Task<SchemaImportResult> ImportAsync(
        NpgsqlConnection conn,
        CancellationToken ct = default,
        int commandTimeoutSeconds = DbCommands.DefaultTimeoutSeconds
    )
    {
        var warnings = new List<SchemaImportWarning>();
        var tables = await LoadTablesAsync(conn, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, commandTimeoutSeconds, ct).ConfigureAwait(false);
        // 一意制約は FK の 1 対 1 判定の材料になるため、外部キーより先にモデルへ載せる
        await LoadUniqueConstraintsAsync(conn, tables, commandTimeoutSeconds, ct)
            .ConfigureAwait(false);
        var rels = await LoadForeignKeysAsync(conn, tables, commandTimeoutSeconds, warnings, ct)
            .ConfigureAwait(false);

        // 列が 1 本も取れなかったテーブルは DDL を生成できないため、黙って通さず名指しで告げる
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

        return new SchemaImportResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            Warnings = warnings,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>取込対象を public スキーマの通常テーブル・パーティション親に絞る共通述語</summary>
    /// <remarks>
    /// <para>
    /// <c>relkind = 'r'</c> は通常テーブル、<c>'p'</c> はパーティション親。親は列・制約の宣言を持つ
    /// 論理的なテーブルなので取り込み、その実体である子パーティション（<c>relispartition</c>）は
    /// 同じ列を重複して持つだけなので除外する（従来は親が <c>'r'</c> でないため丸ごと落ちていた）。
    /// </para>
    /// <para>
    /// 拡張が所有するテーブル（PostGIS の <c>spatial_ref_sys</c> 等・<c>pg_depend</c> の
    /// <c>deptype = 'e'</c>）はユーザー定義でないため除外する。
    /// </para>
    /// </remarks>
    private const string TableScopePredicate =
        @"n.nspname = 'public' AND c.relkind IN ('r', 'p') AND NOT c.relispartition
  AND NOT EXISTS (
      SELECT 1
      FROM pg_catalog.pg_depend d
      WHERE d.classid = 'pg_catalog.pg_class'::regclass
        AND d.objid = c.oid
        AND d.refclassid = 'pg_catalog.pg_extension'::regclass
        AND d.deptype = 'e'
  )";

    /// <summary>public スキーマの取込対象テーブル一覧を取得するクエリ</summary>
    private const string TablesSql =
        @"
SELECT c.relname AS table_name, c.relkind = 'p' AS is_partitioned
FROM pg_catalog.pg_class c
JOIN pg_catalog.pg_namespace n ON c.relnamespace = n.oid
WHERE "
        + TableScopePredicate
        + @"
-- COLLATE ""C"" で並べるのは、DB のロケール次第で Dup と dup の順序が変わるため
-- （衝突時にどちらを採るかを決定的にする）
ORDER BY c.relname COLLATE ""C"";";

    /// <summary>取込対象テーブルのカラム定義を序数順に取得するクエリ</summary>
    /// <remarks>
    /// <para>
    /// 型表記の情報源は <c>format_type(atttypid, atttypmod)</c>＝PostgreSQL 自身が「その列の宣言型」を
    /// 書き下ろす関数で、<c>information_schema</c> の列（character_maximum_length 等）では表せない
    /// 修飾（<c>bit(8)</c> の長さ・<c>interval day to second(3)</c> の単位・配列の要素型・負のスケール）を
    /// 落とさない。出力表記は <see cref="NormalizeFormatType"/> が QuickER の短縮語彙へ寄せる。
    /// </para>
    /// <para>
    /// ドメイン型（<c>typtype = 'd'</c>）は意味モデルに対応する概念が無いため基底型へ平坦化する
    /// （再帰 CTE <c>dom</c> でドメインのドメインも終端まで辿る。修飾子は「基底側が持っていればそれ、
    /// 無ければ手前の値」＝ドメインは基底の修飾子を上書きできないため、この規則で宣言どおりになる）。
    /// ドメインの配列（<c>typcategory = 'A'</c> かつ要素がドメイン）も要素を平坦化して <c>[]</c> を付け直す。
    /// 平坦化した列は呼び出し側が警告として告げる。
    /// </para>
    /// <para>
    /// 生成列（<c>GENERATED ALWAYS AS</c>）は <c>attgenerated</c> が空でない列。式は
    /// <c>pg_get_expr(pg_attrdef.adbin, adrelid)</c> で取り出す（生成列の式は既定値と同じ
    /// <c>pg_attrdef</c> に載る）。判定を <c>= 's'</c> に狭めないのは、将来の格納方式
    /// （仮想生成列）も「書き込みを受け付けない」点では同じだから。
    /// </para>
    /// </remarks>
    private const string ColumnsSql =
        @"
WITH RECURSIVE dom AS (
    SELECT t.oid AS dom_oid, t.typbasetype AS base_oid, t.typtypmod AS base_mod, 1 AS depth
    FROM pg_catalog.pg_type t
    WHERE t.typtype = 'd'
  UNION ALL
    SELECT d.dom_oid, bt.typbasetype,
           CASE WHEN bt.typtypmod <> -1 THEN bt.typtypmod ELSE d.base_mod END, d.depth + 1
    FROM dom d
    JOIN pg_catalog.pg_type bt ON bt.oid = d.base_oid
    WHERE bt.typtype = 'd' AND d.depth < 16
), dom_base AS (
    SELECT DISTINCT ON (dom_oid) dom_oid, base_oid, base_mod
    FROM dom
    ORDER BY dom_oid, depth DESC
)
SELECT c.relname AS table_name,
       a.attname AS column_name,
       CASE
         WHEN db.dom_oid IS NOT NULL THEN pg_catalog.format_type(db.base_oid, db.base_mod)
         WHEN edb.dom_oid IS NOT NULL THEN pg_catalog.format_type(edb.base_oid, edb.base_mod) || '[]'
         ELSE pg_catalog.format_type(a.atttypid, a.atttypmod)
       END AS data_type,
       COALESCE(dt.typname, et.typname) AS domain_name,
       a.attnotnull AS not_null,
       a.attgenerated <> '' AS is_generated,
       CASE WHEN a.attgenerated <> ''
            THEN pg_catalog.pg_get_expr(ad.adbin, ad.adrelid)
       END AS generation_expression
FROM pg_catalog.pg_attribute a
JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
LEFT JOIN pg_catalog.pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
LEFT JOIN dom_base db ON db.dom_oid = a.atttypid
LEFT JOIN pg_catalog.pg_type dt ON dt.oid = db.dom_oid
LEFT JOIN dom_base edb ON t.typcategory = 'A' AND edb.dom_oid = t.typelem
LEFT JOIN pg_catalog.pg_type et ON et.oid = edb.dom_oid
WHERE "
        + TableScopePredicate
        + @"
  AND a.attnum > 0 AND NOT a.attisdropped
ORDER BY c.relname, a.attnum;";

    /// <summary>主キー制約の構成列を序数順に取得するクエリ</summary>
    /// <remarks>conkey は列番号の配列のため、unnest ... WITH ORDINALITY で行展開しつつ構成順序 n を保持する</remarks>
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

    /// <summary>UNIQUE 制約の構成列を宣言順に取得するクエリ（モデルの一意制約・1 対 1 判定に用いる）</summary>
    /// <remarks>
    /// 主キーと同様 conkey を unnest ... WITH ORDINALITY で展開する（contype = 'u'）。
    /// ordinality の n は conkey の並び＝制約の宣言順そのものなので、これで宣言順が保たれる。
    /// <c>CREATE UNIQUE INDEX</c> による素の一意インデックスは pg_constraint に現れないため自然に除外される
    /// </remarks>
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
    /// <para>
    /// <c>confdeltype</c> / <c>confupdtype</c> は c=Cascade, n=SetNull, a=NoAction, r=Restrict（NoAction 扱い）,
    /// d=SetDefault を表す。<c>conkey</c> / <c>confkey</c> の序数を突き合わせて複合 FK の列対応を復元する。
    /// </para>
    /// <para>
    /// 参照先の名前空間 <c>ref_schema</c> も選ぶ。取込範囲は <c>public</c> スキーマだけなので、他スキーマを
    /// 参照する FK は参照先テーブルが図に無く、リレーションを作れない。クエリで落とさず C# 側で弾くのは、
    /// 「黙って消えた」ではなく警告として告げるため。
    /// </para>
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
    con.confupdtype::text AS update_action,
    parent_ns.nspname AS ref_schema
FROM pg_catalog.pg_constraint con
JOIN pg_catalog.pg_class child ON con.conrelid = child.oid
JOIN pg_catalog.pg_namespace ns ON child.relnamespace = ns.oid
JOIN pg_catalog.pg_class parent ON con.confrelid = parent.oid
JOIN pg_catalog.pg_namespace parent_ns ON parent.relnamespace = parent_ns.oid
CROSS JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS cols(conkey, confkey, n)
JOIN pg_catalog.pg_attribute ca ON ca.attrelid = child.oid AND ca.attnum = cols.conkey
JOIN pg_catalog.pg_attribute pa ON pa.attrelid = parent.oid AND pa.attnum = cols.confkey
WHERE con.contype = 'f' AND ns.nspname = 'public'
ORDER BY con.conname, cols.n;";

    /// <summary>テーブル・カラムのコメント（obj_description / col_description）を一括取得するクエリ</summary>
    /// <remarks>
    /// LEFT JOIN pg_attribute はカラムを持たない（削除済み列のみ等の）テーブルでもテーブルコメントの行を残すための結合。
    /// attnum &gt; 0 でシステム列を、NOT attisdropped で削除済み列を除外する
    /// </remarks>
    private const string DescriptionsSql =
        @"
SELECT c.relname AS table_name, a.attname AS column_name,
       col_description(c.oid, a.attnum) AS column_comment,
       obj_description(c.oid, 'pg_class') AS table_comment
FROM pg_catalog.pg_class c
JOIN pg_catalog.pg_namespace n ON c.relnamespace = n.oid
LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
WHERE "
        + TableScopePredicate
        + ";";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    /// <remarks>
    /// 辞書は 5 方言共通の大文字小文字非依存だが、PostgreSQL は引用識別子で <c>"Dup"</c> と <c>dup</c> を
    /// 共存させられる。黙って上書きすると 1 エンティティへ潰れて両テーブルの列が混ざるため、
    /// 後着（<c>relname</c> 昇順で後ろ）を取り込まず警告として告げる。
    /// </remarks>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        NpgsqlConnection conn,
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
                Entity = new Entity { TableName = name, Columns = new List<Column>() },
            };

            // パーティション親は列・制約の宣言を持ち帰れるが、パーティション定義は意味モデルに無い
            // ＝この図から生成した DDL はパーティションされていないテーブルを作る
            if (reader.GetBoolean(1))
            {
                warnings.Add(
                    new SchemaImportWarning(SchemaImportWarningKind.PartitionDefinitionLost, name)
                );
            }
        }

        return dict;
    }

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        NpgsqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, ColumnsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var table = reader.GetString(0);

            if (!SchemaTableEntry.TryGetExact(tables, table, out var entry))
            {
                continue;
            }

            var colName = reader.GetString(1);
            var formatType = reader.GetString(2);
            var domainName = reader.IsDBNull(3) ? null : reader.GetString(3);
            var notNull = reader.GetBoolean(4);
            var isGenerated = reader.GetBoolean(5);

            var col = new Column
            {
                Name = colName,
                DataType = NormalizeFormatType(formatType),
                IsNullable = !notNull,
                IsComputed = isGenerated,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;

            // 生成列は式が意味モデルに載らない＝図から DDL を生成すると普通の列になる
            if (isGenerated)
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.ComputedColumnExpressionLost,
                        entry.Entity.TableName,
                        colName,
                        // 式は警告文へそのまま載るため、行構造を壊さないよう制御文字を畳んでおく
                        SqlComment.Sanitize(reader.IsDBNull(6) ? string.Empty : reader.GetString(6))
                    )
                );
            }

            // ドメイン型は基底型へ平坦化済み＝図から DDL を生成するとドメインではなく基底型の列になる
            if (domainName is not null)
            {
                warnings.Add(
                    new SchemaImportWarning(
                        SchemaImportWarningKind.DomainTypeFlattened,
                        entry.Entity.TableName,
                        colName,
                        domainName
                    )
                );
            }
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正し、構成順を記録する</summary>
    /// <remarks>
    /// 構成順はクエリの <c>ORDER BY</c>（<c>conkey</c> の序数昇順）に任せ、到着順でそのまま積む。
    /// </remarks>
    private static async Task LoadPrimaryKeysAsync(
        NpgsqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, PrimaryKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!SchemaTableEntry.TryGetExact(tables, reader.GetString(0), out var entry))
            {
                continue;
            }

            if (entry.ColumnsByName.TryGetValue(reader.GetString(1), out var col))
            {
                col.IsPrimaryKey = true;
                col.IsNullable = false;
                entry.Entity.PrimaryKeyColumnIds.Add(col.Id);
            }
        }
    }

    /// <summary>テーブル・カラムのコメントを取得し、エンティティ・カラムの説明へ反映する</summary>
    private static async Task LoadDescriptionsAsync(
        NpgsqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        CancellationToken ct
    )
    {
        await using var cmd = DbCommands.Create(conn, DescriptionsSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!SchemaTableEntry.TryGetExact(tables, reader.GetString(0), out var entry))
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
    /// <remarks>
    /// 参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
    /// </remarks>
    private static async Task<List<Relationship>> LoadForeignKeysAsync(
        NpgsqlConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        int commandTimeoutSeconds,
        List<SchemaImportWarning> warnings,
        CancellationToken ct
    )
    {
        var builder = new ForeignKeyRelationshipBuilder();
        // 取込範囲外を参照する FK は制約単位で 1 度だけ告げる（複合 FK は行が複数出るため）
        var reportedOutOfScope = new HashSet<string>(StringComparer.Ordinal);

        await using var cmd = DbCommands.Create(conn, ForeignKeysSql, commandTimeoutSeconds);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var fkName = reader.GetString(0);
            var parentKey = reader.GetString(1); // FK 保有テーブル（子）
            var parentCol = reader.GetString(2);
            var refKey = reader.GetString(3); // 参照先テーブル（親・PK 側）
            var refCol = reader.GetString(4);
            var deleteAction = MapReferentialAction(
                reader.IsDBNull(6) ? null : reader.GetString(6)
            );
            var updateAction = MapReferentialAction(
                reader.IsDBNull(7) ? null : reader.GetString(7)
            );
            var refSchema = reader.GetString(8);

            // 参照先が public 以外＝図に親テーブルが無いのでリレーションを作れない。除外して告げる
            if (!string.Equals(refSchema, "public", StringComparison.Ordinal))
            {
                if (reportedOutOfScope.Add(fkName))
                {
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
                !SchemaTableEntry.TryGetExact(tables, parentKey, out _)
                || !SchemaTableEntry.TryGetExact(tables, refKey, out _)
            )
            {
                continue;
            }

            builder.Add(fkName, parentKey, parentCol, refKey, refCol, deleteAction, updateAction);
        }

        return builder.Build(tables);
    }

    /// <summary>UNIQUE 制約を読み込み、各エンティティの一意制約としてモデルへ載せる</summary>
    private static async Task LoadUniqueConstraintsAsync(
        NpgsqlConnection conn,
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
                if (!SchemaTableEntry.TryGetExact(tables, key, out _))
                {
                    continue;
                }

                var constraintName = reader.GetString(1);
                var col = reader.GetString(2);
                builder.Add(key, constraintName, col, constraintName);
            }
        }

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());
    }

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

    /// <summary>
    /// <c>format_type()</c> が返す PostgreSQL の正準表記を、QuickER が扱う短縮語彙へ正規化する。
    /// </summary>
    /// <remarks>
    /// 規則の正本は <see cref="PostgreSqlTypeNotation.Normalize"/>。差分の比較が図の表記に掛ける正規化と同じ関数なので、
    /// 取込の出力はその不動点になる（ここだけ別の規則を持つと、図の表記と取込の表記がまた食い違う）。
    /// </remarks>
    /// <param name="formatType"><c>format_type(atttypid, atttypmod)</c> の出力</param>
    public static string NormalizeFormatType(string formatType) =>
        PostgreSqlTypeNotation.Normalize(formatType);
}
