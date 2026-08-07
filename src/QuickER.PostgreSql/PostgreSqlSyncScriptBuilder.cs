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
///   <item>DropForeignKey（FK 依存列の型変更・列/テーブル削除より前に外す）</item>
///   <item>DropUniqueConstraint（構成列の定義変更・主キー変更より前に外す）</item>
///   <item>AlterPrimaryKey / Drop フェーズ（旧主キー制約の解除。旧主キー列の NULL 許容化を通すため列定義変更より前）</item>
///   <item>AlterColumn</item>
///   <item>AlterPrimaryKey / Add フェーズ（新主キー制約の付与。新主キー列の NOT NULL 化を済ませた後に行う）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddUniqueConstraint（FK が候補キーとして参照しうるため FK 追加より前に張る）</item>
///   <item>AddForeignKey</item>
///   <item>SetTableDescription / SetColumnDescription（COMMENT ON）</item>
/// </list>
/// </remarks>
public sealed class PostgreSqlSyncScriptBuilder : SyncScriptBuilderBase
{
    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約を含む）を生成する</summary>
    protected override void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        var pks = e.Columns.Where(c => c.IsPrimaryKey).ToList();
        sb.AppendLine($"CREATE TABLE {PgIdentifier.Quote(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)}";

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
    protected override void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"ADD COLUMN {PgIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)};"
        );
    }

    /// <summary>ALTER TABLE ... ALTER COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// PostgreSQL は型変更と NULL 制約変更を別の文で表現する。
    /// 型は <c>ALTER COLUMN ... TYPE 新型</c>、NULL 制約は <c>SET NOT NULL</c> / <c>DROP NOT NULL</c> を用いる
    /// </remarks>
    protected override void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        var table = PgIdentifier.Quote(item.TableName);
        var column = PgIdentifier.QuoteSimple(col.Name);

        sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} TYPE {col.DataType};");

        // 主キー列または NULL 非許容なら NOT NULL を設定し、それ以外は NOT NULL を外す
        var nullClause = col.IsPrimaryKey || !col.IsNullable ? "SET NOT NULL" : "DROP NOT NULL";
        sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} {nullClause};");
    }

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を生成する</summary>
    /// <remarks>
    /// 旧主キーの制約名は差分項目に含まれないため、テーブル名から <c>pg_constraint</c> を逆引きし、
    /// DO ブロックで動的に DROP する（主キーが無いテーブルなら何も実行しない）。
    /// </remarks>
    protected override void AppendDropPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        var table = PgIdentifier.Quote(item.TableName);
        var tableName = PgIdentifier.EscapeStringLiteral(
            PgIdentifier.TableNameOnly(item.TableName)
        );

        // 旧主キーの制約名はシステムカタログを逆引きして特定し、見つかったときだけ DROP する
        sb.AppendLine("DO $$");
        sb.AppendLine("DECLARE pk_name text;");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    SELECT con.conname INTO pk_name");
        sb.AppendLine("    FROM pg_constraint con");
        sb.AppendLine("    JOIN pg_class tbl ON con.conrelid = tbl.oid");
        sb.AppendLine($"    WHERE con.contype = 'p' AND tbl.relname = '{tableName}'");
        // 別スキーマの同名テーブルを誤って対象にしないよう public に限定する（取込と同じスコープ）
        sb.AppendLine("        AND tbl.relnamespace = 'public'::regnamespace;");
        sb.AppendLine("    IF pk_name IS NOT NULL THEN");
        sb.AppendLine(
            $"        EXECUTE 'ALTER TABLE {table} DROP CONSTRAINT \"' || pk_name || '\"';"
        );
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を生成する</summary>
    /// <remarks>
    /// 新しい主キー構成は <see cref="SchemaDiffItem.Entity"/>（target 側エンティティ）の主キー列を列定義順に読み、
    /// 制約名は CREATE TABLE と同じ <c>PK_{テーブル名}</c> 規則で組み立てる。
    /// 主キー列が 1 つも無い場合（主キーの解除のみ）は付与文を出さない。
    /// </remarks>
    protected override void AppendAddPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        var pks = item.Entity?.Columns.Where(c => c.IsPrimaryKey).ToList() ?? [];

        // 新しい主キー列が無い（＝主キーの解除のみ）場合は付与文を出さない
        if (pks.Count == 0)
        {
            return;
        }

        var pkCols = string.Join(", ", pks.Select(p => PgIdentifier.QuoteSimple(p.Name)));
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} ADD CONSTRAINT \"PK_{PgIdentifier.SafeName(item.TableName)}\" "
                + $"PRIMARY KEY ({pkCols});"
        );
    }

    /// <summary>一意制約を追加する ALTER TABLE ... ADD CONSTRAINT ... UNIQUE 文を生成する</summary>
    protected override void AppendAddUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.UniqueConstraintColumns.Count == 0)
        {
            sb.AppendLine(SyncScriptBuilderHelper.BuildUniqueConstraintSkipComment(item));
            return;
        }

        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            PgIdentifier.SafeName
        );
        var cols = string.Join(", ", item.UniqueConstraintColumns.Select(PgIdentifier.QuoteSimple));
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} ADD CONSTRAINT \"{PgIdentifier.Escape(name)}\" "
                + $"UNIQUE ({cols});"
        );
    }

    /// <summary>一意制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    protected override void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            PgIdentifier.SafeName
        );
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"DROP CONSTRAINT \"{PgIdentifier.Escape(name)}\";"
        );
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    protected override void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {PgIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    protected override void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {PgIdentifier.Quote(item.TableName)};");
    }

    /// <summary>外部キー制約を追加する ALTER TABLE 文を生成する</summary>
    protected override void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var columnPairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

        // 構成列が特定できない場合は不正な DDL を出さず、コメントでスキップを明示する
        if (columnPairs.Count == 0)
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
        var referentialActions = SyncScriptBuilderHelper.BuildReferentialActionClause(
            item.Relationship
        );
        // 複合外部キーは構成列を宣言順にカンマ区切りで並べる（単列なら従来と同一の出力）
        var childColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver.ChildColumns(columnPairs).Select(PgIdentifier.QuoteSimple)
        );
        var parentColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver.ParentColumns(columnPairs).Select(PgIdentifier.QuoteSimple)
        );

        sb.AppendLine(
            $"ALTER TABLE {PgIdentifier.Quote(childTbl)} ADD CONSTRAINT \"{PgIdentifier.Escape(fkName)}\" "
                + $"FOREIGN KEY ({childColumnList}) "
                + $"REFERENCES {PgIdentifier.Quote(parentTbl)} ({parentColumnList}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP CONSTRAINT 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば <c>IF EXISTS</c> 付きで直接 DROP する 不明な場合は親子テーブル名から
    /// システムカタログを逆引きし、DO ブロックで動的に削除する
    /// </remarks>
    protected override void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
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
    protected override void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
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
    protected override void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        var target =
            $"{PgIdentifier.Quote(item.TableName)}.{PgIdentifier.QuoteSimple(item.ColumnName!)}";
        var isClause = string.IsNullOrEmpty(newVal)
            ? "NULL"
            : $"'{PgIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine($"COMMENT ON COLUMN {target} IS {isClause};");
    }
}
