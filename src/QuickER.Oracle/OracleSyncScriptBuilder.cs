using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から Oracle 用の DDL バッチを生成する</summary>
/// <remarks>
/// <para>
/// 依存関係による失敗を避けるため、以下の順序で出力する。
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
/// </para>
/// <para>
/// 文の区切りは SQL*Plus 流儀に従う。各文は <c>;</c> で終端し、<b>文と文の間に「/」のみの行</b>を置く。
/// PL/SQL の無名ブロックも <c>END;</c> の後に <c>/</c> 行を置く。この規約は
/// <see cref="OracleSchemaSyncExecutor"/> と対になっており、実行側は「/」のみの行で分割して 1 文ずつ実行する。
/// </para>
/// </remarks>
public sealed class OracleSyncScriptBuilder : ISyncScriptBuilder
{
    /// <summary>選択された差分項目のみを Oracle DDL へ変換する</summary>
    public string Build(IEnumerable<SchemaDiffItem> items)
    {
        var list = items.Where(i => i.IsSelected).ToList();
        var sb = new StringBuilder();

        // 各文（末尾 ; を含む）を蓄積し、最後に「/」のみの行で連結する
        var statements = new List<string>();

        WriteSection(statements, list, SchemaDiffKind.AddTable, AppendCreateTable);
        WriteSection(statements, list, SchemaDiffKind.AddColumn, AppendAddColumn);
        WriteSection(statements, list, SchemaDiffKind.AlterColumn, AppendAlterColumn);
        WriteSection(statements, list, SchemaDiffKind.DropForeignKey, AppendDropForeignKey);
        WriteSection(statements, list, SchemaDiffKind.DropColumn, AppendDropColumn);
        WriteSection(statements, list, SchemaDiffKind.DropTable, AppendDropTable);
        WriteSection(statements, list, SchemaDiffKind.AddForeignKey, AppendAddForeignKey);
        WriteSection(
            statements,
            list,
            SchemaDiffKind.SetTableDescription,
            AppendSetTableDescription
        );
        WriteSection(
            statements,
            list,
            SchemaDiffKind.SetColumnDescription,
            AppendSetColumnDescription
        );

        for (var i = 0; i < statements.Count; i++)
        {
            sb.Append(statements[i]);

            if (!statements[i].EndsWith('\n'))
            {
                sb.AppendLine();
            }

            // 文と文の間に「/」のみの行を置く（最後の文の後にも置き、実行側の分割を安定させる）
            sb.AppendLine("/");
        }

        return sb.ToString();
    }

    /// <summary>指定種別の差分のみを抽出し、各文（末尾 ; 付き）を <paramref name="statements"/> へ追加する</summary>
    private static void WriteSection(
        List<string> statements,
        List<SchemaDiffItem> all,
        SchemaDiffKind kind,
        System.Func<SchemaDiffItem, string?> writer
    )
    {
        foreach (var item in all.Where(i => i.Kind == kind))
        {
            var stmt = writer(item);

            if (!string.IsNullOrWhiteSpace(stmt))
            {
                statements.Add(stmt);
            }
        }
    }

    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約を含む）を生成する</summary>
    private static string AppendCreateTable(SchemaDiffItem item)
    {
        var e = item.Entity!;
        var pks = e.Columns.Where(c => c.IsPrimaryKey).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {OracleIdentifier.Quote(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {OracleIdentifier.QuoteSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)}";

            // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || pks.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => OracleIdentifier.QuoteSimple(p.Name)));
            sb.AppendLine(
                $"    CONSTRAINT \"PK_{OracleIdentifier.SafeName(item.TableName)}\" PRIMARY KEY ({pkCols})"
            );
        }

        sb.Append(");");
        return sb.ToString();
    }

    /// <summary>ALTER TABLE ... ADD（列追加）文を生成する</summary>
    private static string AppendAddColumn(SchemaDiffItem item)
    {
        var col = item.Column!;
        // NULL は Oracle の既定のため句を付けず、余分な空白が入らないよう組み立てる
        var nullClause = GetNullabilityClauseForAdd(col);
        var colDef = string.IsNullOrEmpty(nullClause)
            ? $"{OracleIdentifier.QuoteSimple(col.Name)} {col.DataType}"
            : $"{OracleIdentifier.QuoteSimple(col.Name)} {col.DataType} {nullClause}";
        return $"ALTER TABLE {OracleIdentifier.Quote(item.TableName)} ADD ({colDef});";
    }

    /// <summary>ALTER TABLE ... MODIFY（列定義変更）文を生成する</summary>
    /// <remarks>
    /// Oracle は型変更と NULL 制約変更を <c>MODIFY</c> 1 文で表現する。
    /// NULL→NOT NULL と NOT NULL→NULL の切替はいずれも明示的に句を付ける。
    /// </remarks>
    private static string AppendAlterColumn(SchemaDiffItem item)
    {
        var col = item.Column!;
        var nullClause = col.IsPrimaryKey || !col.IsNullable ? "NOT NULL" : "NULL";
        return $"ALTER TABLE {OracleIdentifier.Quote(item.TableName)} "
            + $"MODIFY ({OracleIdentifier.QuoteSimple(col.Name)} {col.DataType} {nullClause});";
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    private static string AppendDropColumn(SchemaDiffItem item)
    {
        return $"ALTER TABLE {OracleIdentifier.Quote(item.TableName)} "
            + $"DROP COLUMN {OracleIdentifier.QuoteSimple(item.ColumnName!)};";
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    private static string AppendDropTable(SchemaDiffItem item)
    {
        return $"DROP TABLE {OracleIdentifier.Quote(item.TableName)};";
    }

    /// <summary>外部キー制約を追加する ALTER TABLE 文を生成する</summary>
    /// <remarks>Oracle は <c>ON UPDATE</c> を出力しない。指定がある場合は注意コメントを先頭に付す</remarks>
    private static string? AppendAddForeignKey(SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return null;
        }

        var pkCol = ResolveReferencedColumn(item);

        // 参照先列が特定できない場合は不正な DDL を出さず、コメントでスキップを明示する
        if (pkCol is null || item.ColumnName is null)
        {
            // スキップ理由の識別子は生成 SQL の決定性を保つため方言中立・カルチャ非依存にする
            // （表示用の item.Description は UI 言語で変わるため使わない）
            return $"-- Skipped: could not resolve the column required to add the foreign key. ({SchemaDiffService.NormalizeTable(item.ChildEntity)} -> {SchemaDiffService.NormalizeTable(item.ParentEntity)})";
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var fkName = string.IsNullOrWhiteSpace(item.Relationship?.ConstraintName)
            ? $"FK_{OracleIdentifier.SafeName(childTbl)}_{OracleIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;

        var sb = new StringBuilder();

        // ON UPDATE が指定されていても Oracle では無視される旨を注意コメントで残す
        if (
            item.Relationship is not null
            && item.Relationship.OnUpdate != ForeignKeyReferentialAction.NoAction
        )
        {
            sb.AppendLine("-- Note: Oracle does not support ON UPDATE; ignoring it");
        }

        var deleteClause = item.Relationship is null
            ? string.Empty
            : OracleReferentialAction.BuildOnDeleteClause(item.Relationship.OnDelete);

        sb.Append(
            $"ALTER TABLE {OracleIdentifier.Quote(childTbl)} ADD CONSTRAINT \"{OracleIdentifier.Escape(fkName)}\" "
                + $"FOREIGN KEY ({OracleIdentifier.QuoteSimple(item.ColumnName)}) "
                + $"REFERENCES {OracleIdentifier.Quote(parentTbl)} ({OracleIdentifier.QuoteSimple(pkCol.Name)}){deleteClause};"
        );
        return sb.ToString();
    }

    /// <summary>外部キー制約を削除する文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば <c>ALTER TABLE ... DROP CONSTRAINT</c> を直接出力する。
    /// 不明な場合は親子テーブル名から <c>user_constraints</c> を逆引きする PL/SQL 無名ブロックで動的に削除する。
    /// </remarks>
    private static string AppendDropForeignKey(SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return string.Empty;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は直接 DROP する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            return $"ALTER TABLE {OracleIdentifier.Quote(childTbl)} "
                + $"DROP CONSTRAINT \"{OracleIdentifier.Escape(item.ForeignKeyName)}\";";
        }

        // 制約名不明時は親子テーブル名から user_constraints を逆引きして PL/SQL ブロックで削除する。
        // 識別子はクォート付き作成のため、大文字小文字を保持した実名でカタログを照合する。
        var childName = OracleIdentifier.EscapeStringLiteral(
            OracleIdentifier.TableNameOnly(childTbl)
        );
        var parentName = OracleIdentifier.EscapeStringLiteral(
            OracleIdentifier.TableNameOnly(parentTbl)
        );
        var childQuoted = OracleIdentifier.Quote(childTbl).Replace("'", "''");

        var sb = new StringBuilder();
        sb.AppendLine("DECLARE");
        sb.AppendLine("    v_name user_constraints.constraint_name%TYPE;");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    SELECT c.constraint_name INTO v_name");
        sb.AppendLine("    FROM user_constraints c");
        sb.AppendLine(
            "    JOIN user_constraints r ON c.r_constraint_name = r.constraint_name AND c.r_owner = r.owner"
        );
        sb.AppendLine(
            $"    WHERE c.constraint_type = 'R' AND c.table_name = '{childName}' AND r.table_name = '{parentName}';"
        );
        sb.AppendLine(
            $"    EXECUTE IMMEDIATE 'ALTER TABLE {childQuoted} DROP CONSTRAINT \"' || v_name || '\"';"
        );
        sb.AppendLine("EXCEPTION");
        sb.AppendLine("    WHEN NO_DATA_FOUND THEN NULL;");
        sb.Append("END;");
        return sb.ToString();
    }

    // ---------------- COMMENT ON (説明) ----------------

    /// <summary>テーブルの説明（COMMENT ON TABLE）設定文を生成する</summary>
    /// <remarks>Oracle は <c>IS NULL</c> 構文が使えないため、削除は空文字 <c>IS ''</c> で表現する</remarks>
    private static string AppendSetTableDescription(SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        var target = OracleIdentifier.Quote(item.TableName);
        return $"COMMENT ON TABLE {target} IS '{OracleIdentifier.EscapeStringLiteral(newVal)}';";
    }

    /// <summary>カラムの説明（COMMENT ON COLUMN）設定文を生成する</summary>
    private static string AppendSetColumnDescription(SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        var target =
            $"{OracleIdentifier.Quote(item.TableName)}.{OracleIdentifier.QuoteSimple(item.ColumnName!)}";
        return $"COMMENT ON COLUMN {target} IS '{OracleIdentifier.EscapeStringLiteral(newVal)}';";
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

    /// <summary>NULL 許容句を返す（主キーまたは非 NULL 許容なら NOT NULL、それ以外は空）</summary>
    /// <remarks>Oracle の ADD 句では NULL は既定のため明示しない</remarks>
    private static string GetNullabilityClauseForAdd(Column column) =>
        column.IsPrimaryKey || !column.IsNullable ? "NOT NULL" : string.Empty;

    /// <summary>CREATE TABLE 内の NULL 許容句を返す（主キーまたは非 NULL 許容なら NOT NULL、それ以外は NULL）</summary>
    private static string GetNullabilityClause(Column column) =>
        column.IsPrimaryKey || !column.IsNullable ? "NOT NULL" : "NULL";
}
