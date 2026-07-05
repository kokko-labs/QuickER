using System.Linq;
using System.Text;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// ER 図から DB 方言別の DDL（<c>CREATE TABLE</c> / <c>ALTER TABLE ... ADD CONSTRAINT</c>）を生成する基底クラス
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>エンティティごとに <c>CREATE TABLE</c> を出力し、PK 列は <c>CONSTRAINT PK_テーブル名 PRIMARY KEY</c>（識別子は方言のクォート方式）として末尾にまとめる（複合 PK 対応）</item>
///   <item>1対多 / 1対1 のリレーションは <c>FOREIGN KEY</c> 制約として出力。多対多はジャンクションテーブルが必要なためコメント行のみ出力</item>
///   <item>識別子のクォート方式・エスケープ規則・FK 参照アクション句の組み立ては派生クラスが方言ごとに実装する（テンプレートメソッドパターン）</item>
/// </list>
/// </remarks>
public abstract class DdlGeneratorBase : IDdlGenerator
{
    /// <summary>ER 図定義から DDL 文字列を生成する</summary>
    /// <param name="diagram">対象の ER 図定義</param>
    /// <returns>対象方言の DDL スクリプト</returns>
    /// <remarks>
    /// 既定は「全テーブルを CREATE → FK を後段の ALTER TABLE で張る」共通経路。
    /// ALTER TABLE ADD CONSTRAINT を使えない方言（SQLite 等）は、インライン制約で組み立てるため本メソッドを上書きする
    /// </remarks>
    public virtual string Build(ErDiagram diagram)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- QuickER によって自動生成された DDL");
        sb.AppendLine($"-- 生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 先に全テーブルを作成し、FOREIGN KEY は後段で ALTER TABLE 追加する
        // （テーブル定義順に依存せず参照整合性制約を張れるようにするため）
        foreach (var entity in diagram.Entities)
        {
            var table = entity.TableName;
            var pks = entity.Columns.Where(c => c.IsPrimaryKey).ToList();
            sb.AppendLine($"CREATE TABLE {QuoteQualifiedName(table)} (");

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var col = entity.Columns[i];
                // PK 列は IsNullable の設定値に関わらず NOT NULL を強制する
                var line =
                    $"    {QuoteSimpleName(col.Name)} {col.DataType} {(col.IsPrimaryKey || !col.IsNullable ? "NOT NULL" : "NULL")}";

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
                var pkCols = string.Join(", ", pks.Select(p => QuoteSimpleName(p.Name)));
                sb.AppendLine(
                    $"    CONSTRAINT {QuoteConstraintName($"PK_{SafeName(table)}")} PRIMARY KEY ({pkCols})"
                );
            }

            sb.AppendLine(");");
            sb.AppendLine();
        }

        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        foreach (var rel in diagram.Relationships)
        {
            // 1対多 / 1対1 とも、親 (Source) の PK を子 (Target) が外部キーとして参照する。
            // 参照先エンティティが解決できないリレーションは出力対象外とする
            if (
                !entitiesById.TryGetValue(rel.SourceEntityId, out var pkEntity)
                || !entitiesById.TryGetValue(rel.TargetEntityId, out var fkEntity)
            )
            {
                continue;
            }

            // 多対多はジャンクションテーブルが必要なのでコメントのみ出力する
            if (rel.Type == RelationshipType.ManyToMany)
            {
                sb.AppendLine(
                    $"-- 多対多 ({pkEntity.TableName} ⇄ {fkEntity.TableName}): ジャンクションテーブルを別途定義してください。"
                );
                continue;
            }

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
                fkColName = SafeName(pkEntity.TableName) + "_" + pkCol.Name;
            }

            var fkTable = fkEntity.TableName;
            var pkTable = pkEntity.TableName;
            // 制約名はモデルの値を優先し、未設定なら FK_子_親 の命名規則で生成する
            var constraintName = string.IsNullOrWhiteSpace(rel.ConstraintName)
                ? $"FK_{SafeName(fkTable)}_{SafeName(pkTable)}"
                : rel.ConstraintName;

            AppendBeforeForeignKeyStatement(sb, rel);

            var referentialActions = BuildReferentialActionClause(rel);

            sb.AppendLine(
                $"ALTER TABLE {QuoteQualifiedName(fkTable)} ADD CONSTRAINT {QuoteConstraintName(constraintName)} "
                    + $"FOREIGN KEY ({QuoteSimpleName(fkColName)}) REFERENCES {QuoteQualifiedName(pkTable)} ({QuoteSimpleName(pkCol.Name)}){referentialActions};"
            );
        }

        return sb.ToString();
    }

    /// <summary>テーブル名を方言のクォート方式で修飾する（<c>schema.table</c> はスキーマ・テーブルを個別にクォートして分割する）</summary>
    protected abstract string QuoteQualifiedName(string name);

    /// <summary>カラム名など単一識別子を方言のクォート方式でクォートする</summary>
    protected abstract string QuoteSimpleName(string name);

    /// <summary>制約名などに使う安全な ID を生成する（"." と空白を "_" へ置換）</summary>
    protected abstract string SafeName(string name);

    /// <summary>制約名を方言のクォート方式でクォートする（エスケープ込み）</summary>
    protected abstract string QuoteConstraintName(string constraintName);

    /// <summary><c>ON DELETE</c> / <c>ON UPDATE</c> の参照アクション句を組み立てる</summary>
    /// <remarks>既定実装は共通ヘルパーへ委譲する。方言固有の制限がある場合は派生クラスで上書きする</remarks>
    protected virtual string BuildReferentialActionClause(Relationship relationship) =>
        ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
            relationship.OnDelete,
            relationship.OnUpdate
        );

    /// <summary>FK の <c>ALTER TABLE</c> 文を出力する直前に追加行を出力するフック（既定は何もしない）</summary>
    /// <remarks>Oracle の <c>ON UPDATE</c> 非対応の注意コメントなど、方言固有の前置き出力に用いる</remarks>
    protected virtual void AppendBeforeForeignKeyStatement(
        StringBuilder sb,
        Relationship relationship
    ) { }
}
