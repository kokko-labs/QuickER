using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から MySQL 用の DDL バッチを生成する</summary>
/// <remarks>
/// <para>
/// 依存関係による失敗を避けるため、以下の順序で出力する。文はすべて <c>;</c> で終端する。
/// <list type="number">
///   <item>AddTable</item>
///   <item>AddColumn</item>
///   <item>AlterColumn</item>
///   <item>DropForeignKey（列・テーブル削除より前に外す）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddForeignKey</item>
///   <item>SetTableDescription / SetColumnDescription</item>
/// </list>
/// </para>
/// <para>
/// MySQL 固有の重要事項:
/// <list type="bullet">
///   <item>
///   列変更（AlterColumn / SetColumnDescription）は <c>MODIFY COLUMN</c> による列定義の完全再指定で行う。
///   MODIFY は既存の属性（NULL 許容・COMMENT）を保持しないため、対象列に説明があれば <c>COMMENT</c> を必ず含める
///   （含めないと既存コメントが消える）。列定義は <see cref="SchemaDiffItem.Entity"/> / <see cref="SchemaDiffItem.Column"/> から復元する。
///   </item>
///   <item>テーブル説明は <c>ALTER TABLE ... COMMENT = '…'</c>（削除は空文字）で設定する。</item>
///   <item>
///   DropForeignKey は制約名既知なら <c>DROP FOREIGN KEY 名前</c> で直接外す。制約名不明時は MySQL に
///   DO ブロックが無いため、<c>information_schema</c> から制約名を引いてプリペアド動的 SQL で削除する。
///   </item>
/// </list>
/// </para>
/// </remarks>
public sealed class MySqlSyncScriptBuilder : ISyncScriptBuilder
{
    /// <summary>選択された差分項目のみを MySQL DDL へ変換する</summary>
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

        sb.AppendLine($"-- ===== {kind} ({subset.Count} 件) =====");

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
        sb.AppendLine($"CREATE TABLE {MySqlIdentifier.Quote(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {MySqlIdentifier.QuoteSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)}";

            // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || pks.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => MySqlIdentifier.QuoteSimple(p.Name)));
            sb.AppendLine(
                $"    CONSTRAINT `PK_{MySqlIdentifier.SafeName(item.TableName)}` PRIMARY KEY ({pkCols})"
            );
        }

        sb.AppendLine(");");
    }

    /// <summary>ALTER TABLE ... ADD COLUMN（列追加）文を生成する</summary>
    private static void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"ADD COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {BuildColumnDefinition(col)};"
        );
    }

    /// <summary>ALTER TABLE ... MODIFY COLUMN（列定義変更）文を生成する</summary>
    /// <remarks>
    /// MySQL の MODIFY は列定義を完全に再指定するため、型・NULL 制約に加えて
    /// 対象列に説明があれば COMMENT も含める（含めないと既存コメントが消える）。
    /// </remarks>
    private static void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {BuildColumnDefinition(col)};"
        );
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    private static void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {MySqlIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    private static void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {MySqlIdentifier.Quote(item.TableName)};");
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
                $"-- スキップ: 外部キー追加に必要な列が解決できませんでした。 ({SchemaDiffService.NormalizeTable(item.ChildEntity)} -> {SchemaDiffService.NormalizeTable(item.ParentEntity)})"
            );
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var fkName = string.IsNullOrWhiteSpace(item.Relationship?.ConstraintName)
            ? $"FK_{MySqlIdentifier.SafeName(childTbl)}_{MySqlIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;
        var referentialActions = BuildReferentialActionClause(item.Relationship);
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(childTbl)} ADD CONSTRAINT `{MySqlIdentifier.Escape(fkName)}` "
                + $"FOREIGN KEY ({MySqlIdentifier.QuoteSimple(item.ColumnName)}) "
                + $"REFERENCES {MySqlIdentifier.Quote(parentTbl)} ({MySqlIdentifier.QuoteSimple(pkCol.Name)}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP FOREIGN KEY 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば直接 <c>DROP FOREIGN KEY</c> する。不明な場合は MySQL に DO ブロックが無いため、
    /// <c>information_schema.REFERENTIAL_CONSTRAINTS</c> から制約名を引いてプリペアド動的 SQL で削除する。
    /// FK が見つからない場合は <c>DO 0</c> を実行して無害に済ませる。
    /// </remarks>
    private static void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は直接 DROP FOREIGN KEY する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            sb.AppendLine(
                $"ALTER TABLE {MySqlIdentifier.Quote(childTbl)} "
                    + $"DROP FOREIGN KEY {MySqlIdentifier.QuoteSimple(item.ForeignKeyName)};"
            );
            return;
        }

        // 制約名不明時は information_schema を逆引きしてプリペアド動的 SQL で削除する。
        // 接続文字列に AllowUserVariables=true が付与されている前提（Executor 側で付与）。
        var childName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(childTbl)
        );
        var parentName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(parentTbl)
        );
        var childQuoted = MySqlIdentifier.Quote(childTbl);

        sb.AppendLine("SELECT rc.CONSTRAINT_NAME INTO @fk");
        sb.AppendLine("FROM information_schema.REFERENTIAL_CONSTRAINTS rc");
        sb.AppendLine(
            $"WHERE rc.CONSTRAINT_SCHEMA = DATABASE() AND rc.TABLE_NAME = '{childName}' "
                + $"AND rc.REFERENCED_TABLE_NAME = '{parentName}' LIMIT 1;"
        );
        // FK が見つからなければ無害な DO 0 を実行する
        sb.AppendLine(
            $"SET @sql = IF(@fk IS NULL, 'DO 0', CONCAT('ALTER TABLE {childQuoted} DROP FOREIGN KEY `', @fk, '`'));"
        );
        sb.AppendLine("PREPARE stmt FROM @sql;");
        sb.AppendLine("EXECUTE stmt;");
        sb.AppendLine("DEALLOCATE PREPARE stmt;");
    }

    // ---------------- 説明 (COMMENT) ----------------

    /// <summary>テーブルの説明（ALTER TABLE ... COMMENT）設定文を生成する</summary>
    /// <remarks>新値が空なら空文字コメントを設定して説明を削除する</remarks>
    private static void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"COMMENT = '{MySqlIdentifier.EscapeStringLiteral(newVal)}';"
        );
    }

    /// <summary>カラムの説明を MODIFY COLUMN による完全再指定で設定する文を生成する</summary>
    /// <remarks>
    /// MODIFY は列定義を完全再指定するため、型・NULL 制約を <see cref="SchemaDiffItem.Entity"/> の
    /// 該当 Column から復元し、COMMENT には <see cref="SchemaDiffItem.NewDescription"/> を用いる。
    /// </remarks>
    private static void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
    {
        var newVal = item.NewDescription ?? string.Empty;

        // 型・NULL 制約は Entity の該当列から復元する
        var col = item.Entity?.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, item.ColumnName, StringComparison.OrdinalIgnoreCase)
        );

        if (col is null)
        {
            // 列定義が復元できない場合は不正な DDL を出さずスキップを明示する
            sb.AppendLine(
                $"-- スキップ: 列 {item.ColumnName} の定義が復元できず COMMENT を設定できませんでした。"
            );
            return;
        }

        var definition =
            $"{col.DataType} {GetNullabilityClause(col)} COMMENT '{MySqlIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {definition};"
        );
    }

    /// <summary>列定義（型・NULL 制約・COMMENT）を組み立てる</summary>
    /// <remarks>
    /// MODIFY / ADD で列定義を完全再指定する際に用いる。説明が設定されていれば COMMENT を付与し、
    /// 既存コメントの消失を防ぐ。
    /// </remarks>
    private static string BuildColumnDefinition(Column column)
    {
        var sb = new StringBuilder();
        sb.Append(column.DataType);
        sb.Append(' ');
        sb.Append(GetNullabilityClause(column));

        if (!string.IsNullOrEmpty(column.Description))
        {
            sb.Append($" COMMENT '{MySqlIdentifier.EscapeStringLiteral(column.Description)}'");
        }

        return sb.ToString();
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
