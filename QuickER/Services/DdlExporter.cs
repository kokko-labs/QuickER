using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuickER.Model;
using QuickER.ViewModels;

using QuickER.SqlServer;

namespace QuickER.Services;

/// <summary>
/// ER 図から SQL Server 向けの DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成するエクスポーター
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>エンティティごとに <c>CREATE TABLE</c> を出力し、PK 列は <c>CONSTRAINT [PK_テーブル名] PRIMARY KEY</c> として末尾にまとめる（複合 PK 対応）</item>
///   <item>1対多 / 1対1 のリレーションは <c>FOREIGN KEY</c> 制約として出力。多対多はジャンクションテーブルが必要なためコメント行のみ出力</item>
///   <item>識別子は <see cref="SqlIdentifier"/> で角括弧付けする（<c>schema.table</c> は <c>[schema].[table]</c> に分割、<c>]</c> は <c>]]</c> にエスケープ）</item>
/// </list>
/// </remarks>
public static class DdlExporter
{
    /// <summary>ER 図の現在の状態から DDL 文字列を生成する</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    /// <returns>SQL Server 用の DDL スクリプト</returns>
    public static string Build(MainViewModel vm)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ER Designer によって自動生成された DDL");
        sb.AppendLine($"-- 生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 先に全テーブルを作成し、FOREIGN KEY は後段で ALTER TABLE 追加する
        // （テーブル定義順に依存せず参照整合性制約を張れるようにするため）
        foreach (var entity in vm.Entities)
        {
            var table = entity.TableName;
            var pks = entity.Columns.Where(c => c.IsPrimaryKey).ToList();
            sb.AppendLine($"CREATE TABLE {SqlIdentifier.Bracket(table)} (");

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var col = entity.Columns[i];
                // PK 列は IsNullable の設定値に関わらず NOT NULL を強制する
                var line =
                    $"    {SqlIdentifier.BracketSimple(col.Name)} {col.DataType} {(col.IsPrimaryKey || !col.IsNullable ? "NOT NULL" : "NULL")}";

                // 後続のカラム行、または PRIMARY KEY 制約行が続く場合は区切りのカンマを付ける
                if (i < entity.Columns.Count - 1 || pks.Count > 0)
                {
                    line += ",";
                }

                sb.AppendLine(line);
            }

            // PRIMARY KEY 制約（複合 PK 対応のため列定義とは分離して出力）
            if (pks.Count > 0)
            {
                var pkCols = string.Join(
                    ", ",
                    pks.Select(p => SqlIdentifier.BracketSimple(p.Name))
                );
                sb.AppendLine(
                    $"    CONSTRAINT [PK_{SqlIdentifier.SafeName(table)}] PRIMARY KEY ({pkCols})"
                );
            }

            sb.AppendLine(");");
            sb.AppendLine();
        }

        foreach (var rel in vm.Relationships)
        {
            // 多対多はジャンクションテーブルが必要なのでコメントのみ出力する
            if (rel.Type == Model.RelationshipType.ManyToMany)
            {
                sb.AppendLine(
                    $"-- 多対多 ({rel.Source.TableName} ⇄ {rel.Target.TableName}): ジャンクションテーブルを別途定義してください。"
                );
                continue;
            }

            // 1対多 / 1対1 とも、親 (Source) の PK を子 (Target) が外部キーとして参照する
            var pkEntity = rel.Source;
            var fkEntity = rel.Target;

            // 参照カラムが明示されていればそれを優先し、未指定なら親の PK 列にフォールバックする
            var pkCol = rel.SourceColumnId is not null
                ? pkEntity.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId)
                    ?? pkEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey)
                : pkEntity.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            // 親側に参照可能な列がなければ FK を生成できないためスキップする
            if (pkCol is null)
            {
                continue;
            }

            var fkColName = rel.TargetColumnId is not null
                ? fkEntity.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId)?.Name
                : null;

            // 子側カラム未指定時は「親テーブル名_PK列名」を FK カラム名として採用する
            if (string.IsNullOrWhiteSpace(fkColName))
            {
                fkColName = SqlIdentifier.SafeName(pkEntity.TableName) + "_" + pkCol.Name;
            }

            var fkTable = fkEntity.TableName;
            var pkTable = pkEntity.TableName;
            // 制約名はモデルの値を優先し、未設定なら FK_子_親 の命名規則で生成する
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

    /// <summary><c>ON DELETE</c> / <c>ON UPDATE</c> の参照アクション句を組み立てる</summary>
    private static string BuildReferentialActionClause(RelationshipViewModel relationship) =>
        ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
            relationship.OnDelete,
            relationship.OnUpdate
        );

    /// <summary>DDL を UTF-8 でファイルに書き出す</summary>
    /// <param name="vm">対象の <see cref="MainViewModel"/></param>
    /// <param name="path">出力先ファイルパス</param>
    public static void SaveTo(MainViewModel vm, string path) =>
        File.WriteAllText(path, Build(vm), Encoding.UTF8);
}
