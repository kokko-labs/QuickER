using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ERDesigner.Models;
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
///   <item>識別子は <see cref="SqlIdentifier"/> で括弧付け (<c>schema.table</c> は <c>[schema].[table]</c> に分割、<c>]</c> はエスケープ)。</item>
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
            var pks = entity.Columns.Where(c => c.IsPrimaryKey).ToList();
            sb.AppendLine($"CREATE TABLE {SqlIdentifier.Bracket(table)} (");

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var col = entity.Columns[i];
                var line = $"    {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {(col.IsPrimaryKey || !col.IsNullable ? "NOT NULL" : "NULL")}";

                // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
                if (i < entity.Columns.Count - 1 || pks.Count > 0)
                {
                    line += ",";
                }

                sb.AppendLine(line);
            }

            // PRIMARY KEY 制約（複数 PK 対応）
            if (pks.Count > 0)
            {
                var pkCols = string.Join(", ", pks.Select(p => SqlIdentifier.BracketSimple(p.Name)));
                sb.AppendLine($"    CONSTRAINT [PK_{SqlIdentifier.SafeName(table)}] PRIMARY KEY ({pkCols})");
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

            // 1対多 / 1対1 とも、起点 (Source) の PK を終点 (Target) に外部キーとして接続する
            var pkEntity = rel.Source;
            var fkEntity = rel.Target;

            var pkCol = rel.SourceColumnId is not null
                ? pkEntity.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId) ?? pkEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey)
                : pkEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            if (pkCol is null)
            {
                continue;
            }

            var fkColName = rel.TargetColumnId is not null ? fkEntity.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId)?.Name : null;

            if (string.IsNullOrWhiteSpace(fkColName))
            {
                fkColName = SqlIdentifier.SafeName(pkEntity.TableName) + "_" + pkCol.Name;
            }

            var fkTable = fkEntity.TableName;
            var pkTable = pkEntity.TableName;
            var constraintName = string.IsNullOrWhiteSpace(rel.ConstraintName)
                ? $"FK_{SqlIdentifier.SafeName(fkTable)}_{SqlIdentifier.SafeName(pkTable)}"
                : rel.ConstraintName;
            var referentialActions = BuildReferentialActionClause(rel);

            sb.AppendLine(
                $"ALTER TABLE {SqlIdentifier.Bracket(fkTable)} ADD CONSTRAINT [{SqlIdentifier.Escape(constraintName)}] "
                    + $"FOREIGN KEY ({SqlIdentifier.BracketSimple(fkColName)}) REFERENCES {SqlIdentifier.Bracket(pkTable)} ({SqlIdentifier.BracketSimple(pkCol.Name)}){referentialActions};"
            );
        }

        return sb.ToString();
    }

    /// <summary>参照アクション句を組み立てます。</summary>
    private static string BuildReferentialActionClause(RelationshipViewModel relationship) =>
        ForeignKeyReferentialActionHelper.BuildReferentialActionClause(relationship.OnDelete, relationship.OnUpdate);

    /// <summary>DDL をファイルに書き出します。</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/>。</param>
    /// <param name="path">出力先ファイルパス。</param>
    public static void SaveTo(MainViewModel vm, string path) => File.WriteAllText(path, Build(vm), Encoding.UTF8);
}
