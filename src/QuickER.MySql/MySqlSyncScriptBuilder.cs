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
///   <item>DropForeignKey（FK 依存列の型変更・列/テーブル削除より前に外す）</item>
///   <item>DropUniqueConstraint（構成列の定義変更・主キー変更より前に外す）</item>
///   <item>AlterPrimaryKey / Drop フェーズ（旧主キー制約の解除。旧主キー列の NULL 許容化を通すため列定義変更より前）</item>
///   <item>AlterColumn</item>
///   <item>AlterPrimaryKey / Add フェーズ（新主キー制約の付与。新主キー列の NOT NULL 化を済ませた後に行う）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddUniqueConstraint（FK が候補キーとして参照しうるため FK 追加より前に張る）</item>
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
///   <item>
///   AlterPrimaryKey の <c>DROP PRIMARY KEY</c> は主キーが無いテーブルではエラーになるため、
///   <c>information_schema</c> を逆引きして主キーが在るときだけプリペアド動的 SQL で実行する。
///   付与側は MySQL の主キー名が <c>PRIMARY</c> 固定のため <c>CONSTRAINT</c> 名を指定しない。
///   </item>
/// </list>
/// </para>
/// </remarks>
public sealed class MySqlSyncScriptBuilder : SyncScriptBuilderBase
{
    // ---------------- 各種 DDL ----------------

    /// <summary>CREATE TABLE 文（主キー制約を含む）を生成する</summary>
    protected override void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        var pks = e.Columns.Where(c => c.IsPrimaryKey).ToList();
        sb.AppendLine($"CREATE TABLE {MySqlIdentifier.Quote(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {MySqlIdentifier.QuoteSimple(col.Name)} {col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)}";

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
    protected override void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
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
    protected override void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {BuildColumnDefinition(col)};"
        );
    }

    /// <summary>主キー変更の解除フェーズ（旧主キー制約の DROP）文を生成する</summary>
    /// <remarks>
    /// <c>ALTER TABLE ... DROP PRIMARY KEY</c> は主キーが無いテーブルに対してエラーになるため、
    /// <c>information_schema.TABLE_CONSTRAINTS</c> を逆引きし、主キーが在るときだけプリペアド動的 SQL で
    /// 実行する（無ければ無害な <c>DO 0</c>）。
    /// </remarks>
    protected override void AppendDropPrimaryKey(StringBuilder sb, SchemaDiffItem item)
    {
        var table = MySqlIdentifier.Quote(item.TableName);
        var tableName = MySqlIdentifier.EscapeStringLiteral(
            MySqlIdentifier.TableNameOnly(item.TableName)
        );

        // 主キーの有無を information_schema で確かめてからプリペアド動的 SQL で外す。
        // 接続文字列に AllowUserVariables=true が付与されている前提（Executor 側で付与）。
        // SELECT ... INTO は該当行が無いとユーザー変数を書き換えないため、事前に NULL で初期化する。
        sb.AppendLine("SET @pk = NULL;");
        sb.AppendLine("SELECT tc.CONSTRAINT_NAME INTO @pk");
        sb.AppendLine("FROM information_schema.TABLE_CONSTRAINTS tc");
        sb.AppendLine(
            $"WHERE tc.CONSTRAINT_SCHEMA = DATABASE() AND tc.TABLE_NAME = '{tableName}' "
                + "AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY' LIMIT 1;"
        );
        // 主キーが無ければ無害な DO 0 を実行する
        sb.AppendLine(
            $"SET @sql = IF(@pk IS NULL, 'DO 0', 'ALTER TABLE {table} DROP PRIMARY KEY');"
        );
        sb.AppendLine("PREPARE stmt FROM @sql;");
        sb.AppendLine("EXECUTE stmt;");
        sb.AppendLine("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>主キー変更の付与フェーズ（新主キー制約の ADD）文を生成する</summary>
    /// <remarks>
    /// 新しい主キー構成は <see cref="SchemaDiffItem.Entity"/>（target 側エンティティ）の主キー列を列定義順に読む。
    /// MySQL の主キー名は <c>PRIMARY</c> 固定のため <c>CONSTRAINT</c> 名は指定しない。
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

        var pkCols = string.Join(", ", pks.Select(p => MySqlIdentifier.QuoteSimple(p.Name)));
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} ADD PRIMARY KEY ({pkCols});"
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
            MySqlIdentifier.SafeName
        );
        var cols = string.Join(
            ", ",
            item.UniqueConstraintColumns.Select(MySqlIdentifier.QuoteSimple)
        );
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} ADD CONSTRAINT `{MySqlIdentifier.Escape(name)}` "
                + $"UNIQUE ({cols});"
        );
    }

    /// <summary>一意制約を削除する ALTER TABLE ... DROP INDEX 文を生成する</summary>
    /// <remarks>
    /// MySQL の一意制約は実体が一意インデックスで、<c>DROP CONSTRAINT</c> は 8.0.19 未満で受け付けられない。
    /// どの版でも通る <c>DROP INDEX</c> 構文を使う（制約名＝インデックス名）。
    /// </remarks>
    protected override void AppendDropUniqueConstraint(StringBuilder sb, SchemaDiffItem item)
    {
        var name = UniqueConstraintNaming.Resolve(
            item.UniqueConstraintName,
            item.TableName,
            item.UniqueConstraintColumns,
            MySqlIdentifier.SafeName
        );
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"DROP INDEX {MySqlIdentifier.QuoteSimple(name)};"
        );
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    protected override void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"DROP COLUMN {MySqlIdentifier.QuoteSimple(item.ColumnName!)};"
        );
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    protected override void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {MySqlIdentifier.Quote(item.TableName)};");
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
            ? $"FK_{MySqlIdentifier.SafeName(childTbl)}_{MySqlIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;
        var referentialActions = SyncScriptBuilderHelper.BuildReferentialActionClause(
            item.Relationship
        );
        // 複合外部キーは構成列を宣言順にカンマ区切りで並べる（単列なら従来と同一の出力）
        var childColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ChildColumns(columnPairs)
                .Select(MySqlIdentifier.QuoteSimple)
        );
        var parentColumnList = string.Join(
            ", ",
            ForeignKeyColumnPairResolver
                .ParentColumns(columnPairs)
                .Select(MySqlIdentifier.QuoteSimple)
        );

        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(childTbl)} ADD CONSTRAINT `{MySqlIdentifier.Escape(fkName)}` "
                + $"FOREIGN KEY ({childColumnList}) "
                + $"REFERENCES {MySqlIdentifier.Quote(parentTbl)} ({parentColumnList}){referentialActions};"
        );
    }

    /// <summary>外部キー制約を削除する ALTER TABLE ... DROP FOREIGN KEY 文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば直接 <c>DROP FOREIGN KEY</c> する。不明な場合は MySQL に DO ブロックが無いため、
    /// <c>information_schema.REFERENTIAL_CONSTRAINTS</c> から制約名を引いてプリペアド動的 SQL で削除する。
    /// FK が見つからない場合は <c>DO 0</c> を実行して無害に済ませる。
    /// </remarks>
    protected override void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
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
    protected override void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item)
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
    protected override void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item)
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
                $"-- Skipped: could not restore the definition of column {item.ColumnName}; COMMENT was not set."
            );
            return;
        }

        var definition =
            $"{col.DataType} {SyncScriptBuilderHelper.GetNullabilityClause(col)} COMMENT '{MySqlIdentifier.EscapeStringLiteral(newVal)}'";
        sb.AppendLine(
            $"ALTER TABLE {MySqlIdentifier.Quote(item.TableName)} "
                + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(col.Name)} {definition};"
        );
    }

    // ---------------- 列順変更 (MODIFY ... AFTER) ----------------

    /// <summary>ネイティブ列順変更（<c>ALTER TABLE ... MODIFY COLUMN ... AFTER</c>）を生成する</summary>
    /// <remarks>
    /// <para>
    /// テーブルごとに見出しコメントを付け、各列を「直前に置く列の直後（先頭なら <c>FIRST</c>）」へ移す
    /// <c>MODIFY COLUMN</c> を出力する。列定義は <see cref="BuildColumnDefinition"/> で完全再指定する
    /// （型・NULL 制約に加え、説明があれば <c>COMMENT</c> を含めて既存コメントの消失を防ぐ）。
    /// </para>
    /// <para>
    /// MySQL の <c>MODIFY</c> による位置変更は内部的にテーブルコピー（メタデータのみの高速 DDL にはならない）
    /// になり得る点に注意する。移動列数はプランナーが最長増加部分列で最小化している。
    /// </para>
    /// </remarks>
    protected override void AppendReorders(StringBuilder sb, SyncPlan plan)
    {
        foreach (var reorder in plan.Reorders)
        {
            // 見出し（固定文は英語が正本）
            sb.AppendLine($"-- ===== ReorderColumns: {reorder.TableName} =====");

            foreach (var move in reorder.Moves)
            {
                // AfterColumn が null なら先頭（FIRST）、それ以外は指定列の直後（AFTER 列名）
                var position = move.AfterColumn is null
                    ? "FIRST"
                    : $"AFTER {MySqlIdentifier.QuoteSimple(move.AfterColumn)}";
                sb.AppendLine(
                    $"ALTER TABLE {MySqlIdentifier.Quote(reorder.TableName)} "
                        + $"MODIFY COLUMN {MySqlIdentifier.QuoteSimple(move.Column.Name)} "
                        + $"{BuildColumnDefinition(move.Column)} {position};"
                );
            }

            sb.AppendLine();
        }
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
        sb.Append(SyncScriptBuilderHelper.GetNullabilityClause(column));
        // インライン COMMENT 句は DDL 生成と同じ表記を共有する（説明が空なら付かない）
        sb.Append(MySqlIdentifier.ColumnCommentClause(column.Description));
        return sb.ToString();
    }
}
