using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 図の名前（テーブル名・列名・制約名）が DDL・同期スクリプトへ出せる形かを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 名前は GUI の自由入力・DB 取込・DBML/Excel 取込・MCP/AI 経由の任意文字列で、改行・制御文字を入れられる。
/// 出力側には 2 つの防壁がある——<c>--</c> 行コメントへ載せるときの <see cref="SqlComment.Sanitize"/> と、
/// 識別子として出すときの方言別 <c>Escape</c>——が、埋め込み箇所は DDL 生成・同期スクリプト生成の 5 方言に
/// 散っており、<b>1 箇所でも通し忘れるとそこが即座に武器になる</b>（コメント行の突き破りは任意 SQL の実行）。
/// </para>
/// <para>
/// そこで型表記（<see cref="SqlTypeText"/>）と同じ位置＝生成の入口で、名前の改行・制御文字を名指しで拒否する。
/// 出力側の防壁は残したうえでの二重化で、「単一の漏れが致命傷にならない」ことを構造で担保する。
/// </para>
/// <para>
/// 止めるのは改行・制御文字だけ。<c>"</c> / <c>]</c> / <c>'</c> / <c>$</c> は DB 取込で実在し得る名前で、
/// 出力側のエスケープで正しく扱えるため止める理由が無い（C# コード生成側の生成前診断と同じ線引き）。
/// </para>
/// </remarks>
public static class SqlNameText
{
    /// <summary>名前が SQL へ出せる形か（改行・制御文字を含まないか）</summary>
    /// <remarks>判定規則は <see cref="SqlComment.ContainsControlCharacter"/> と共有する（片方だけ変えないこと）</remarks>
    public static bool IsSafe(string? name) => !SqlComment.ContainsControlCharacter(name);

    /// <summary>図の全名前を検証し、改行・制御文字を含むものがあれば名指しで例外を投げる（DDL 生成の入口）</summary>
    /// <exception cref="InvalidOperationException">改行・制御文字を含む名前がある場合</exception>
    public static void Validate(ErDiagram diagram)
    {
        var offenders = new List<string>();

        foreach (var entity in diagram.Entities)
        {
            AddEntityNames(offenders, entity.TableName, entity);
        }

        foreach (var relationship in diagram.Relationships)
        {
            Add(offenders, relationship.ConstraintName, ForeignKeyLocation);
        }

        Throw(offenders);
    }

    /// <summary>実行計画に現れる全名前を検証し、改行・制御文字を含むものがあれば名指しで例外を投げる（同期生成の入口）</summary>
    /// <remarks>
    /// 名前を出力しうる経路をすべて拾う＝セクション項目（テーブル・列・制約・親子テーブル）、
    /// テーブル再構築の合成後定義とインライン外部キー、ネイティブ列順変更の移動列。
    /// </remarks>
    /// <exception cref="InvalidOperationException">改行・制御文字を含む名前がある場合</exception>
    public static void Validate(SyncPlan plan)
    {
        var offenders = new List<string>();

        foreach (var item in plan.Sections.SelectMany(section => section.Items))
        {
            var table = item.TableName;

            Add(offenders, table, TableLocation);
            Add(offenders, item.ColumnName, name => ColumnLocation(table, name));
            Add(offenders, item.Column?.Name, name => ColumnLocation(table, name));
            Add(offenders, item.OldColumn?.Name, name => ColumnLocation(table, name));
            Add(offenders, item.UniqueConstraintName, name => UniqueLocation(table, name));
            Add(offenders, item.ForeignKeyName, ForeignKeyLocation);
            Add(offenders, item.Relationship?.ConstraintName, ForeignKeyLocation);

            foreach (var column in item.UniqueConstraintColumns)
            {
                Add(offenders, column, name => ColumnLocation(table, name));
            }

            foreach (var pair in item.ForeignKeyColumnPairs)
            {
                Add(offenders, pair.ParentColumn, name => ColumnLocation(table, name));
                Add(offenders, pair.ChildColumn, name => ColumnLocation(table, name));
            }

            if (item.Entity is not null)
            {
                AddEntityNames(offenders, table, item.Entity);
            }

            Add(offenders, item.ParentEntity?.TableName, TableLocation);
            Add(offenders, item.ChildEntity?.TableName, TableLocation);
        }

        foreach (var rebuild in plan.Rebuilds)
        {
            AddEntityNames(offenders, rebuild.TableName, rebuild.NewDefinition);
            Add(offenders, rebuild.TableName, TableLocation);

            foreach (var column in rebuild.CopyColumns)
            {
                Add(offenders, column, name => ColumnLocation(rebuild.TableName, name));
            }

            foreach (var foreignKey in rebuild.ForeignKeys)
            {
                Add(offenders, foreignKey.ConstraintName, ForeignKeyLocation);
                Add(offenders, foreignKey.ParentTable, TableLocation);

                foreach (var column in foreignKey.ChildColumns.Concat(foreignKey.ParentColumns))
                {
                    Add(offenders, column, name => ColumnLocation(rebuild.TableName, name));
                }
            }
        }

        foreach (var reorder in plan.Reorders)
        {
            Add(offenders, reorder.TableName, TableLocation);

            foreach (var move in reorder.Moves)
            {
                Add(offenders, move.Column.Name, name => ColumnLocation(reorder.TableName, name));
                Add(offenders, move.AfterColumn, name => ColumnLocation(reorder.TableName, name));
            }
        }

        Throw(offenders);
    }

    /// <summary>エンティティのテーブル名・列名・一意制約名をまとめて検査する</summary>
    private static void AddEntityNames(List<string> offenders, string tableName, Entity entity)
    {
        Add(offenders, entity.TableName, TableLocation);

        foreach (var column in entity.Columns)
        {
            Add(offenders, column.Name, name => ColumnLocation(tableName, name));
        }

        foreach (var constraint in entity.UniqueConstraints)
        {
            Add(offenders, constraint.Name, name => UniqueLocation(tableName, name));
        }
    }

    /// <summary>名前が安全でなければ、場所の表記を作って一覧へ足す</summary>
    private static void Add(
        List<string> offenders,
        string? name,
        Func<string, string> describeLocation
    )
    {
        if (!IsSafe(name))
        {
            offenders.Add(describeLocation(SqlComment.Sanitize(name)));
        }
    }

    /// <summary>場所の表記（SQL の綴りを使って言語中立にする＝C# 生成前診断と同じ流儀）</summary>
    private static string TableLocation(string name) => $"TABLE '{name}'";

    /// <inheritdoc cref="TableLocation" />
    private static string ColumnLocation(string? tableName, string name) =>
        $"COLUMN '{SqlComment.Sanitize(tableName)}'.'{name}'";

    /// <inheritdoc cref="TableLocation" />
    private static string UniqueLocation(string? tableName, string name) =>
        $"UNIQUE '{SqlComment.Sanitize(tableName)}'.'{name}'";

    /// <inheritdoc cref="TableLocation" />
    private static string ForeignKeyLocation(string name) => $"FOREIGN KEY '{name}'";

    /// <summary>安全でない名前があれば、場所を列挙した例外を投げる（例外文は英語が正本）</summary>
    /// <remarks>
    /// 名前そのものを文面へ載せるが、表示前に制御文字を空白へ畳む（例外文を表示する側の行構造を壊さないため）。
    /// </remarks>
    private static void Throw(List<string> offenders)
    {
        if (offenders.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "The following names contain a line break or control character and cannot be written into SQL safely: "
                + string.Join(", ", offenders.Distinct(StringComparer.Ordinal))
                + ". Remove the line breaks and control characters from the names in the diagram."
        );
    }
}
