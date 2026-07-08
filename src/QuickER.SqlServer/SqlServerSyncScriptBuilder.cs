using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>選択済みの <see cref="SchemaDiffItem"/> から SQL Server 用の T-SQL バッチを生成する</summary>
/// <remarks>
/// 依存関係による失敗を避けるため、以下の順序で出力する
/// <list type="number">
///   <item>AddTable</item>
///   <item>AddColumn</item>
///   <item>AlterColumn</item>
///   <item>DropForeignKey（列・テーブル削除より前に外す）</item>
///   <item>DropColumn</item>
///   <item>DropTable</item>
///   <item>AddForeignKey</item>
///   <item>SetTableDescription / SetColumnDescription（拡張プロパティ MS_Description）</item>
/// </list>
/// </remarks>
public sealed class SqlServerSyncScriptBuilder : ISyncScriptBuilder
{
    /// <summary>選択された差分項目のみを T-SQL へ変換する</summary>
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
        sb.AppendLine($"CREATE TABLE {SqlIdentifier.Bracket(item.TableName)} (");

        for (var i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line =
                $"    {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)}";

            // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
            if (i < e.Columns.Count - 1 || pks.Count > 0)
            {
                line += ",";
            }

            sb.AppendLine(line);
        }

        if (pks.Count > 0)
        {
            var pkCols = string.Join(", ", pks.Select(p => SqlIdentifier.BracketSimple(p.Name)));
            sb.AppendLine(
                $"    CONSTRAINT [PK_{SqlIdentifier.SafeName(item.TableName)}] PRIMARY KEY ({pkCols})"
            );
        }

        sb.AppendLine(");");
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... ADD（列追加）文を生成する</summary>
    private static void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(item.TableName)} "
                + $"ADD {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... ALTER COLUMN（列定義変更）文を生成する</summary>
    private static void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(item.TableName)} "
                + $"ALTER COLUMN {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {GetNullabilityClause(col)};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>ALTER TABLE ... DROP COLUMN（列削除）文を生成する</summary>
    private static void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(item.TableName)} "
                + $"DROP COLUMN {SqlIdentifier.BracketSimple(item.ColumnName!)};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>DROP TABLE（テーブル削除）文を生成する</summary>
    private static void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {SqlIdentifier.Bracket(item.TableName)};");
        sb.AppendLine("GO");
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
            ? $"FK_{SqlIdentifier.SafeName(childTbl)}_{SqlIdentifier.SafeName(parentTbl)}"
            : item.Relationship.ConstraintName!;
        var referentialActions = BuildReferentialActionClause(item.Relationship);
        sb.AppendLine(
            $"ALTER TABLE {SqlIdentifier.Bracket(childTbl)} ADD CONSTRAINT [{SqlIdentifier.Escape(fkName)}] "
                + $"FOREIGN KEY ({SqlIdentifier.BracketSimple(item.ColumnName)}) "
                + $"REFERENCES {SqlIdentifier.Bracket(parentTbl)} ({SqlIdentifier.BracketSimple(pkCol.Name)}){referentialActions};"
        );
        sb.AppendLine("GO");
    }

    /// <summary>外部キー制約を削除する文を生成する</summary>
    /// <remarks>
    /// 制約名が判明していれば存在チェック付きで直接 DROP する 不明な場合は親子テーブル名から
    /// sys.foreign_keys を逆引きし、動的 SQL で削除する
    /// </remarks>
    private static void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return;
        }

        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);

        // 制約名が判明している場合は存在チェックのうえ直接 DROP する
        if (!string.IsNullOrWhiteSpace(item.ForeignKeyName))
        {
            sb.AppendLine(
                $"IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'{SqlIdentifier.EscapeStringLiteral(item.ForeignKeyName)}')"
            );
            sb.AppendLine(
                $"    ALTER TABLE {SqlIdentifier.Bracket(childTbl)} DROP CONSTRAINT [{SqlIdentifier.Escape(item.ForeignKeyName)}];"
            );
            sb.AppendLine("GO");
            return;
        }

        // 制約名不明時は親子テーブル名からカタログビューを逆引きして特定する
        sb.AppendLine($"DECLARE @fk sysname;");
        sb.AppendLine($"SELECT @fk = fk.name FROM sys.foreign_keys fk");
        sb.AppendLine($"  JOIN sys.tables tp ON fk.parent_object_id = tp.object_id");
        sb.AppendLine($"  JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id");
        sb.AppendLine(
            $"WHERE tp.name = N'{SqlIdentifier.EscapeStringLiteral(SqlIdentifier.TableNameOnly(childTbl))}'"
        );
        sb.AppendLine(
            $"  AND tr.name = N'{SqlIdentifier.EscapeStringLiteral(SqlIdentifier.TableNameOnly(parentTbl))}';"
        );
        sb.AppendLine(
            $"IF @fk IS NOT NULL EXEC('ALTER TABLE {SqlIdentifier.Bracket(childTbl)} DROP CONSTRAINT [' + @fk + ']');"
        );
        sb.AppendLine("GO");
    }

    // ---------------- MS_Description (拡張プロパティ) ----------------

    /// <summary>テーブルの説明（MS_Description）設定文を生成する</summary>
    private static void AppendSetTableDescription(StringBuilder sb, SchemaDiffItem item) =>
        AppendDescriptionStatement(sb, item, columnLevel: false);

    /// <summary>カラムの説明（MS_Description）設定文を生成する</summary>
    private static void AppendSetColumnDescription(StringBuilder sb, SchemaDiffItem item) =>
        AppendDescriptionStatement(sb, item, columnLevel: true);

    /// <summary>拡張プロパティ MS_Description の設定・更新・削除文を生成する</summary>
    /// <remarks>
    /// 実行時点の存在状態を判定し、add / update / drop を切り替える 新値が空なら削除する
    /// </remarks>
    /// <param name="columnLevel">true でカラムレベル、false でテーブルレベルの拡張プロパティを対象とする</param>
    private static void AppendDescriptionStatement(
        StringBuilder sb,
        SchemaDiffItem item,
        bool columnLevel
    )
    {
        var schema = SqlIdentifier.SchemaOf(item.TableName);
        var table = SqlIdentifier.TableNameOnly(item.TableName);
        var newVal = item.NewDescription ?? string.Empty;

        var levelArgs =
            $"@level0type=N'SCHEMA', @level0name=N'{SqlIdentifier.EscapeStringLiteral(schema)}', "
            + $"@level1type=N'TABLE',  @level1name=N'{SqlIdentifier.EscapeStringLiteral(table)}'";

        if (columnLevel)
        {
            levelArgs +=
                $", @level2type=N'COLUMN', @level2name=N'{SqlIdentifier.EscapeStringLiteral(item.ColumnName!)}'";
        }

        var objectIdLiteral =
            $"OBJECT_ID(N'{SqlIdentifier.EscapeStringLiteral(schema)}.{SqlIdentifier.EscapeStringLiteral(table)}')";
        var minorIdCondition = columnLevel
            ? $"      AND ep.minor_id = COLUMNPROPERTY({objectIdLiteral}, N'{SqlIdentifier.EscapeStringLiteral(item.ColumnName!)}', 'ColumnId')"
            : $"      AND ep.minor_id = 0";

        if (string.IsNullOrEmpty(newVal))
        {
            // 新値が空の場合は既存の拡張プロパティを存在チェックのうえ削除する
            sb.AppendLine($"IF EXISTS (");
            sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
            sb.AppendLine($"    WHERE ep.name = N'MS_Description' AND ep.class = 1");
            sb.AppendLine($"      AND ep.major_id = {objectIdLiteral}");
            sb.AppendLine($"{minorIdCondition})");
            sb.AppendLine(
                $"    EXEC sys.sp_dropextendedproperty @name=N'MS_Description', {levelArgs};"
            );
        }
        else
        {
            // 既存があれば update、無ければ add で冪等に説明を設定する
            var escaped = SqlIdentifier.EscapeStringLiteral(newVal);
            sb.AppendLine($"IF EXISTS (");
            sb.AppendLine($"    SELECT 1 FROM sys.extended_properties ep");
            sb.AppendLine($"    WHERE ep.name = N'MS_Description' AND ep.class = 1");
            sb.AppendLine($"      AND ep.major_id = {objectIdLiteral}");
            sb.AppendLine($"{minorIdCondition})");
            sb.AppendLine(
                $"    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{escaped}', {levelArgs};"
            );
            sb.AppendLine($"ELSE");
            sb.AppendLine(
                $"    EXEC sys.sp_addextendedproperty    @name=N'MS_Description', @value=N'{escaped}', {levelArgs};"
            );
        }

        sb.AppendLine("GO");
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
