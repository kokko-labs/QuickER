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
        OracleConnection conn,
        CancellationToken ct = default
    )
    {
        var tables = await LoadTablesAsync(conn, ct).ConfigureAwait(false);
        await LoadColumnsAsync(conn, tables, ct).ConfigureAwait(false);
        await LoadPrimaryKeysAsync(conn, tables, ct).ConfigureAwait(false);
        await LoadDescriptionsAsync(conn, tables, ct).ConfigureAwait(false);
        var (rels, warnings) = await LoadForeignKeysAsync(conn, tables, ct).ConfigureAwait(false);

        return new SchemaResult
        {
            Entities = tables.Values.Select(t => t.Entity).ToList(),
            Relationships = rels,
            Warnings = warnings,
        };
    }

    // ---------------- 内部実装 ----------------

    /// <summary>自スキーマの通常テーブル一覧を取得するクエリ</summary>
    /// <remarks>
    /// USER_ ビューは所有オブジェクトのみを返すため、ALL_ ビューと違い owner での絞り込みが不要。
    /// ユーザー定義テーブルに限定するため、ごみ箱の <c>BIN$...</c>（<c>dropped = 'YES'</c>）・
    /// ドメインインデックス等の二次オブジェクト（<c>secondary = 'Y'</c>）・ネステッドテーブル・
    /// IOT オーバーフローセグメント（<c>SYS_IOT_OVER_...</c>）は除外する
    /// </remarks>
    private const string TablesSql =
        @"SELECT table_name FROM user_tables
WHERE dropped = 'NO'
  AND secondary = 'N'
  AND nested = 'NO'
  AND (iot_type IS NULL OR iot_type = 'IOT')
ORDER BY table_name";

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
    /// <remarks>COMMENT ON TABLE 未設定のテーブルは comments が NULL になるため、ここで除外する</remarks>
    private const string TableCommentsSql =
        "SELECT table_name, comments FROM user_tab_comments WHERE comments IS NOT NULL";

    /// <summary>カラムコメントを取得するクエリ</summary>
    /// <remarks>COMMENT ON COLUMN 未設定の列は comments が NULL になるため、ここで除外する</remarks>
    private const string ColumnCommentsSql =
        "SELECT table_name, column_name, comments FROM user_col_comments WHERE comments IS NOT NULL";

    /// <summary>テーブル一覧を読み込み、テーブル名をキーとするエントリ辞書を構築する</summary>
    private static async Task<Dictionary<string, SchemaTableEntry>> LoadTablesAsync(
        OracleConnection conn,
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

    /// <summary>各テーブルへカラム定義を読み込み、型表記を整形して追加する</summary>
    private static async Task LoadColumnsAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
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
                DataType = FormatDataType(
                    dataType,
                    dataPrecision,
                    dataScale,
                    charLength,
                    dataLength
                ),
                IsNullable = isNullable,
            };

            entry.Entity.Columns.Add(col);
            entry.ColumnsByName[colName] = col;
        }
    }

    /// <summary>主キー構成列に IsPrimaryKey を立て、NULL 不可へ補正する</summary>
    private static async Task LoadPrimaryKeysAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
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
        Dictionary<string, SchemaTableEntry> tables,
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
    /// <remarks>
    /// 参照先列の集合が主キーまたは一意制約と一致する場合は 1 対 1、それ以外は 1 対多と判定する。
    /// 複合外部キーは列対応を失うため、その旨の警告もあわせて返す。
    /// </remarks>
    private static async Task<(
        List<Relationship> Relationships,
        List<CompositeForeignKeyImportWarning> Warnings
    )> LoadForeignKeysAsync(
        OracleConnection conn,
        Dictionary<string, SchemaTableEntry> tables,
        CancellationToken ct
    )
    {
        // 親テーブルの一意制約列集合を取得し、1 対 1 判定に用いる
        var uniqueSets = await LoadUniqueColumnSetsAsync(conn, ct).ConfigureAwait(false);

        var builder = new ForeignKeyRelationshipBuilder();

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
            var deleteAction = MapReferentialAction(
                reader.IsDBNull(6) ? null : reader.GetString(6)
            );

            builder.Add(
                fkName,
                childKey,
                childCol,
                refKey,
                refCol,
                deleteAction,
                // Oracle に ON UPDATE は存在しないため常に NoAction で取り込む
                ForeignKeyReferentialAction.NoAction
            );
        }

        var rels = builder.Build(tables, uniqueSets);

        return (rels, builder.CompositeForeignKeyWarnings.ToList());
    }

    /// <summary>テーブルごとの一意制約列集合を取得する</summary>
    /// <returns>テーブル名 → 各一意制約の列名配列リスト</returns>
    private static async Task<Dictionary<string, List<string[]>>> LoadUniqueColumnSetsAsync(
        OracleConnection conn,
        CancellationToken ct
    )
    {
        var builder = new UniqueColumnSetBuilder();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = UniqueConstraintSql;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var con = reader.GetString(1);
            var col = reader.GetString(2);
            builder.Add(key, con, col);
        }

        return builder.Build();
    }

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
