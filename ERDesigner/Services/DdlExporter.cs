using System.IO;
using System.Text;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図から SQL Server 向けの DDL (<c>CREATE TABLE</c> 文等) を生成するサービスです。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>各エンティティに対して <c>CREATE TABLE</c> を出力。</item>
///   <item><see cref="ColumnViewModel.IsPrimaryKey"/> から <c>PRIMARY KEY</c> 制約を生成。</item>
///   <item>1対多 / 1対1 のリレーションから <c>FOREIGN KEY</c> 制約を生成。</item>
/// </list>
/// </remarks>
public static class DdlExporter
{
    /// <summary>ER 図の現在の状態から DDL 文字列を生成して返します。</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/>。</param>
    /// <returns>SQL Server 用の DDL スクリプト。</returns>
    public static string Build(MainViewModel vm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ER Designer によって自動生成された DDL");
        sb.AppendLine($"-- 生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // ----- CREATE TABLE -----
        foreach (var entity in vm.Entities)
        {
            var table = entity.TableName;
            sb.AppendLine($"CREATE TABLE [{table}] (");

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var col = entity.Columns[i];
                var line = $"    [{col.Name}] {col.DataType}";

                if (col.IsPrimaryKey)
                {
                    line += " NOT NULL";
                }

                if (i < entity.Columns.Count - 1)
                {
                    line += ",";
                }

                sb.AppendLine(line);
            }

            // PRIMARY KEY 制約（複数 PK 対応）
            var pks = entity.Columns.Where(c => c.IsPrimaryKey).ToList();

            if (pks.Count > 0)
            {
                sb.Length -= Environment.NewLine.Length;

                if (!sb.ToString().TrimEnd().EndsWith(","))
                {
                    sb.AppendLine(",");
                }
                else
                {
                    sb.AppendLine();
                }

                var pkCols = string.Join(", ", pks.Select(p => $"[{p.Name}]"));
                sb.AppendLine($"    CONSTRAINT [PK_{table}] PRIMARY KEY ({pkCols})");
            }

            sb.AppendLine(");");
            sb.AppendLine();
        }

        // ----- FOREIGN KEY -----
        foreach (var rel in vm.Relationships)
        {
            // 多対多はジャンクションテーブルが必要なのでコメントだけ出力
            if (rel.Type == Models.RelationshipType.ManyToMany)
            {
                sb.AppendLine($"-- 多対多 ({rel.Source.TableName} ⇄ {rel.Target.TableName}): ジャンクションテーブルを別途定義してください。");
                continue;
            }

            // 1対多 → "1" 側のPKを "多" 側に外部キーとして接続
            // 1対1 → 起点のPKを終点に接続（暫定）
            var pkEntity = rel.Type == Models.RelationshipType.OneToMany ? rel.Source : rel.Source;
            var fkEntity = rel.Type == Models.RelationshipType.OneToMany ? rel.Target : rel.Target;

            var pkCol = pkEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            if (pkCol is null)
            {
                continue;
            }

            var fkColName = pkEntity.TableName + "_" + pkCol.Name;
            var fkTable = fkEntity.TableName;
            var pkTable = pkEntity.TableName;

            sb.AppendLine($"ALTER TABLE [{fkTable}] ADD CONSTRAINT [FK_{fkTable}_{pkTable}] " + $"FOREIGN KEY ([{fkColName}]) REFERENCES [{pkTable}] ([{pkCol.Name}]);");
        }

        return sb.ToString();
    }

    /// <summary>DDL をファイルに書き出します。</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/>。</param>
    /// <param name="path">出力先ファイルパス。</param>
    public static void SaveTo(MainViewModel vm, string path) => File.WriteAllText(path, Build(vm), Encoding.UTF8);
}
