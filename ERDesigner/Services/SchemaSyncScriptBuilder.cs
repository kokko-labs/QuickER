using System.Collections.Generic;
using System.Linq;
using System.Text;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// <see cref="SchemaDiff"/> から SQL Server 用の T-SQL バッチを生成します。
/// 出力順序:
///   1) AddTable
///   2) AddColumn
///   3) AlterColumn (フェーズ2)
///   4) DropForeignKey (フェーズ2 / 列・テーブル削除より前)
///   5) DropColumn (フェーズ2)
///   6) DropTable (フェーズ2)
///   7) AddForeignKey
/// </summary>
public static class SchemaSyncScriptBuilder
{
    /// <summary>選択された差分項目のみを T-SQL に変換します。</summary>
    public static string Build(IEnumerable<SchemaDiffItem> items)
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

        return sb.ToString();
    }

    private static void WriteSection(
        StringBuilder sb,
        List<SchemaDiffItem> all,
        SchemaDiffKind kind,
        System.Action<StringBuilder, SchemaDiffItem> writer)
    {
        var subset = all.Where(i => i.Kind == kind).ToList();
        if (subset.Count == 0) return;
        sb.AppendLine($"-- ===== {kind} ({subset.Count} 件) =====");
        foreach (var item in subset) writer(sb, item);
        sb.AppendLine();
    }

    private static string Bracket(string name)
    {
        // "schema.table" のように . を含む場合は両方をブラケットで囲む
        if (name.Contains('.'))
        {
            var parts = name.Split('.', 2);
            return $"[{parts[0]}].[{parts[1]}]";
        }
        return $"[{name}]";
    }

    private static void AppendCreateTable(StringBuilder sb, SchemaDiffItem item)
    {
        var e = item.Entity!;
        sb.AppendLine($"CREATE TABLE {Bracket(item.TableName)} (");
        for (int i = 0; i < e.Columns.Count; i++)
        {
            var col = e.Columns[i];
            var line = $"    [{col.Name}] {col.DataType}";
            if (col.IsPrimaryKey) line += " NOT NULL";
            if (i < e.Columns.Count - 1) line += ",";
            sb.AppendLine(line);
        }
        var pks = e.Columns.Where(c => c.IsPrimaryKey).ToList();
        if (pks.Count > 0)
        {
            // 末尾の改行を除去してカンマ追加
            sb.Length -= System.Environment.NewLine.Length;
            if (!sb.ToString().TrimEnd().EndsWith(",")) sb.AppendLine(",");
            else sb.AppendLine();
            var pkCols = string.Join(", ", pks.Select(p => $"[{p.Name}]"));
            sb.AppendLine($"    CONSTRAINT [PK_{SafeName(item.TableName)}] PRIMARY KEY ({pkCols})");
        }
        sb.AppendLine(");");
        sb.AppendLine("GO");
    }

    private static void AppendAddColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        // 既存テーブルへの ADD では NOT NULL は安全のため付与しない (既存行の問題回避)
        sb.AppendLine($"ALTER TABLE {Bracket(item.TableName)} ADD [{col.Name}] {col.DataType} NULL;");
        sb.AppendLine("GO");
    }

    private static void AppendAlterColumn(StringBuilder sb, SchemaDiffItem item)
    {
        var col = item.Column!;
        sb.AppendLine($"ALTER TABLE {Bracket(item.TableName)} ALTER COLUMN [{col.Name}] {col.DataType} NULL;");
        sb.AppendLine("GO");
    }

    private static void AppendDropColumn(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"ALTER TABLE {Bracket(item.TableName)} DROP COLUMN [{item.ColumnName}];");
        sb.AppendLine("GO");
    }

    private static void AppendDropTable(StringBuilder sb, SchemaDiffItem item)
    {
        sb.AppendLine($"DROP TABLE {Bracket(item.TableName)};");
        sb.AppendLine("GO");
    }

    private static void AppendAddForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null) return;
        var pkCol = item.ParentEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkCol is null || item.ColumnName is null)
        {
            sb.AppendLine($"-- スキップ: 外部キー追加に必要な列が解決できませんでした。 ({item.Description})");
            return;
        }
        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTbl = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var fkName = $"FK_{SafeName(childTbl)}_{SafeName(parentTbl)}";
        sb.AppendLine(
            $"ALTER TABLE {Bracket(childTbl)} ADD CONSTRAINT [{fkName}] " +
            $"FOREIGN KEY ([{item.ColumnName}]) REFERENCES {Bracket(parentTbl)} ([{pkCol.Name}]);");
        sb.AppendLine("GO");
    }

    private static void AppendDropForeignKey(StringBuilder sb, SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null) return;
        var childTbl = SchemaDiffService.NormalizeTable(item.ChildEntity);
        // FK 名が分からない場合は sys カタログから探して動的に DROP する
        sb.AppendLine($"DECLARE @fk sysname;");
        sb.AppendLine($"SELECT @fk = fk.name FROM sys.foreign_keys fk");
        sb.AppendLine($"  JOIN sys.tables tp ON fk.parent_object_id = tp.object_id");
        sb.AppendLine($"  JOIN sys.tables tr ON fk.referenced_object_id = tr.object_id");
        sb.AppendLine($"WHERE tp.name = N'{TableNameOnly(childTbl)}'");
        sb.AppendLine($"  AND tr.name = N'{TableNameOnly(SchemaDiffService.NormalizeTable(item.ParentEntity))}';");
        sb.AppendLine($"IF @fk IS NOT NULL EXEC('ALTER TABLE {Bracket(childTbl)} DROP CONSTRAINT [' + @fk + ']');");
        sb.AppendLine("GO");
    }

    private static string SafeName(string name) => name.Replace(".", "_").Replace(" ", "_");

    private static string TableNameOnly(string fullName)
        => fullName.Contains('.') ? fullName.Split('.', 2)[1] : fullName;
}
