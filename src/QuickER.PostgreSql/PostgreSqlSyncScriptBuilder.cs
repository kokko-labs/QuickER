using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から PostgreSQL 用の DDL バッチを生成する</summary>
/// <remarks>
/// 依存関係による失敗を避けるため、以下の順序で出力する。文はすべて <c>;</c> で終端し、<c>GO</c> は使わない。
/// <list type="number">
///   <item>AddTable</item>
///   <item>AddColumn</item>
///   <item>AlterColumn</item>
///   <item>DropForeignKey（列・テーブル削除より前に外す）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddForeignKey</item>
///   <item>SetTableDescription / SetColumnDescription（COMMENT ON）</item>
/// </list>
/// </remarks>
public sealed class PostgreSqlSyncScriptBuilder : ISyncScriptBuilder
{
    /// <summary>選択された差分項目のみを PostgreSQL DDL へ変換する</summary>
    public string Build(IEnumerable<SchemaDiffItem> items)
    {
        var sb = new StringBuilder();
        var list = items.Where(i => i.IsSelected).ToList();

        WriteSection(sb, list, SchemaDiffKind.AddTable, AppendCreateTable);
        WriteSection(sb, list, SchemaDiffKind.AddColumn, AppendAddColumn);
        WriteSection(sb, list, SchemaDiffKind.AlterColumn, AppendAlterColumn);
        WriteSection(sb, list, SchemaDiffKind.DropForeignKey, AppendDropForeignKey);
        WriteSection(sb, list, SchemaDiffKind.DropColumn, AppendDropColumn);
        WriteSection(sb, list, SchemaDiffKind.DropTable, AppendDropTable);
        WriteSection(sb, list, SchemaDiffKind.AddForeignKey, AppendAddForeignKey);
        WriteSection(sb, list, SchemaDiffKind.SetTableDescription, AppendSetTableDescription);
        WriteSection(sb, list, SchemaDiffKind.SetColumnDescription, AppendSetColumnDescription);

        return sb.ToString();
    }

    /// <summary>指定種別の差分のみを抽出し、見出しコメント付きで 1 セクション分を書き出す</summary>
    private static void WriteSection(
        StringBuilder sb,
        List<SchemaDiffItem> all,
        SchemaDiffKind kind,
        System.Action<StringBuilder, SchemaDiffItem> writer
    )
    {
        var subset = all.Where(i => i.Kind == kind).ToList();

        if (subset.Count == 0)
        {
            return;
        }

        sb.AppendLine($"-- ===== {kind} ({subset.Count} items) =====");

        foreach (var item in subset)
        {
            writer(sb, item);
        }

        sb.AppendLine();
    }

    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約を含む）を生成する</summary>
    private static void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        var pks = e.Columns.Where(c => c.IsPrimaryKey).ToList();
        sb.AppendLine($"CREATE TABLE {PgIdentifier.Quote(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)}";

            // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || pks.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => PgIdentifier.QuoteSimple(p.Name)));
            sb.AppendLine(
                $"    CONSTRAINT \"PK_{PgIdentifier.SafeName(item.TableName)}\" PRIMARY KEY ({pkCols})"
            );
        }

        sb.AppendLine(");");
    }

    /// <summary>ALTER TABLE ... ADD COLUMN（列追加）文を生成する</summary>
    private static void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"ADD COLUMN {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)};"
        );
    }

    /// <summary>ALTER TABLE ... ALTER COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// PostgreSQL は型変更と NULL 制約変更を別の文で表現する。
    /// 型は <c>ALTER COLUMN ... TYPE 新型</c>、NULL 制約は <c>SET NOT NULL</c> / <c>DROP NOT NULL</c> を用いる
    /// </remarks>
    private static void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        var table = PgIdentifier.Quote(item.TableName);
        var column = PgIdentifier.QuoteSimple(col.Name);

        sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} TYPE {col.DataType};");

        // 主キー列または NULL 非許容なら NOT NULL を設定し、それ以外は NOT NULL を外す
        var nullClause = col.IsPrimaryKey || !col.IsNullable ? "SET NOT NULL" : "DROP NOT NULL";
        sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} {nullClause};");
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    private static void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {PgIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    private static void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {PgIdentifier.Quote(item.TableName)};");
    }

    /// <summary>外部キー制約を追加する ALTER TABLE 文を生成する</summary>
    private static void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var pkCol = ResolveReferencedColumn(item);

        // 参照先列が特定できない場合は不正な DDL を出さず、コメントでスキップを明示する
        if (pkCol is null || item.ColumnName is null)
        {
            sb.AppendLine(
                // スキップ理由の識別子は生成 SQL の決定性を保つため方言中立・カルチャ非依存にする
                // （表示用の item.Description は UI 言語で変わるため使わない）
                $"-- Skipped: could not resolve the column required to add the foreign key. ({SchemaDiffService.NormalizeTable(item.ChildEntity)} -> {SchemaDiffService.NormalizeTable(item.ParentEntity)})"
            );
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var fkName = string.IsNullOrWhiteSpace(item.Relationship?.ConstraintName)
            ? $"FK_{PgIdentifier.SafeName(childTbl)}_{PgIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;
        var referentialActions = BuildReferentialActionClause(item.Relationship);
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(childTbl)} ADD CONSTRAINT \"{PgIdentifier.Escape(fkName)}\" "
                + $"FOREIGN KEY ({PgIdentifier.QuoteSimple(item.ColumnName)}) "
                + $"REFERENCES {PgIdentifier.Quote(parentTbl)} ({PgIdentifier.QuoteSimple(pkCol.Name)}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば <c>IF EXISTS</c> 付きで直接 DROP する 不明な場合は親子テーブル名から
    /// システムカタログを逆引きし、DO ブロックで動的に削除する
    /// </remarks>
    private static void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は IF EXISTS のうえ直接 DROP する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            sb.AppendLine(
                $"ALTER TABLE {PgIdentifier.Quote(childTbl)} "
                    + $"DROP CONSTRAINT IF EXISTS \"{PgIdentifier.Escape(item.ForeignKeyName)}\";"
            );
            return;
        }

        // 制約名不明時は親子テーブル名からシステムカタログを逆引きして DO ブロックで削除する
        var childName = PgIdentifier.EscapeStringLiteral(PgIdentifier.TableNameOnly(childTbl));
        var parentName = PgIdentifier.EscapeStringLiteral(PgIdentifier.TableNameOnly(parentTbl));
        sb.AppendLine("DO $$");
        sb.AppendLine("DECLARE fk_name text;");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    SELECT con.conname INTO fk_name");
        sb.AppendLine("    FROM pg_constraint con");
        sb.AppendLine("    JOIN pg_class child ON con.conrelid = child.oid");
        sb.AppendLine("    JOIN pg_class parent ON con.confrelid = parent.oid");
        sb.AppendLine(
            $"    WHERE con.contype = 'f' AND child.relname = '{childName}' AND parent.relname = '{parentName}'"
        );
        // 別スキーマの同名テーブルを誤って対象にしないよう public に限定する（取込と同じスコープ）
        sb.AppendLine(
            "        AND child.relnamespace = 'public'::regnamespace AND parent.relnamespace = 'public'::regnamespace;"
        );
        sb.AppendLine("    IF fk_name IS NOT NULL THEN");
        sb.AppendLine(
            $"        EXECUTE 'ALTER TABLE {PgIdentifier.Quote(childTbl)} DROP CONSTRAINT \"' || fk_name || '\"';"
        );
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    // ---------------- COMMENT ON (説明) ----------------

    /// <summary>テーブルの説明（COMMENT ON TABLE）設定文を生成する</summary>
    private static void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        var target = PgIdentifier.Quote(item.TableName);
        // 新値が空なら IS NULL で説明を削除、それ以外は文字列リテラルで設定する
        var isClause = string.IsNullOrEmpty(newVal)
            ? "NULL"
            : $"'{PgIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine($"COMMENT ON TABLE {target} IS {isClause};");
    }

    /// <summary>カラムの説明（COMMENT ON COLUMN）設定文を生成する</summary>
    private static void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        var target =
            $"{PgIdentifier.Quote(item.TableName)}.{PgIdentifier.QuoteSimple(item.ColumnName!)}";
        var isClause = string.IsNullOrEmpty(newVal)
            ? "NULL"
            : $"'{PgIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine($"COMMENT ON COLUMN {target} IS {isClause};");
    }

    /// <summary>外部キーの参照先列を差分情報から解決する</summary>
    /// <remarks>明示指定された列を優先し、無ければ親テーブルの主キー先頭列にフォールバックする</remarks>
    private static Column? ResolveReferencedColumn(SchemaDiffItem item)
    {
        if (item.Relationship?.SourceColumnId is not null)
        {
            var byId = item.ParentEntity?.Columns.FirstOrDefault(c =>
                c.Id == item.Relationship.SourceColumnId
            );

            if (byId is not null)
            {
                return byId;
            }
        }

        return item.ParentEntity?.Columns.FirstOrDefault(c => c.IsPrimaryKey);
    }

    /// <summary>NULL 許容句を返す（主キーまたは非 NULL 許容なら NOT NULL）</summary>
    private static string GetNullabilityClause(Column column) =>
        column.IsPrimaryKey || !column.IsNullable ? "NOT NULL" : "NULL";

    /// <summary>外部キーの ON DELETE / ON UPDATE 参照アクション句を生成する</summary>
    private static string BuildReferentialActionClause(Relationship? relationship) =>
        relationship is null
            ? string.Empty
            : ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
                relationship.OnDelete,
                relationship.OnUpdate
            );
}
