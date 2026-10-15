using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
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
/// <para>
/// 同じ入口で、方言の識別子の長さの上限を超える名前と、方言が表せない文字（MySQL の BMP の外の文字）を含む名前も
/// 場所を名指しして拒否する（判定は <see cref="IdentifierLengthLimit.AcceptsTableName"/> /
/// <see cref="IdentifierLengthLimit.Accepts"/>＝Model の宣言 1 か所）。そのまま出すと SQL Server・MySQL・Oracle は DDL の適用で断り、
/// PostgreSQL は黙って切り詰め（取り込み直すと別の名前・上限の後ろでだけ違う 2 つの名前は同じ名前）、MySQL は BMP の外の文字を
/// 黙って <c>?</c> へ置き換える。QuickER が付ける既定名は生成のときに上限へ収めている（<see cref="IdentifierLengthLimit.Fit"/>）ので、
/// ここで断るのは利用者が付けた名前（と DB から取り込んだ名前）だけになる。
/// </para>
/// </remarks>
public static class SqlNameText
{
    /// <summary>名前が SQL へ出せる形か（改行・制御文字を含まないか）</summary>
    /// <remarks>判定規則は <see cref="SqlComment.ContainsControlCharacter"/> と共有する（片方だけ変えないこと）</remarks>
    public static bool IsSafe(string? name) => !SqlComment.ContainsControlCharacter(name);

    /// <summary>図の全名前を検証し、出せない名前があれば名指しで例外を投げる（DDL 生成の入口）</summary>
    /// <param name="diagram">図</param>
    /// <param name="limit">DDL の方言の識別子の長さの上限（方言の識別子クラスの <c>Limit</c>）</param>
    /// <exception cref="InvalidOperationException">
    /// 改行・制御文字を含む名前、または上限を超える・表せない文字を含む名前がある場合
    /// </exception>
    public static void Validate(ErDiagram diagram, IdentifierLengthLimit limit) =>
        Validate(DiagramNames(diagram), limit);

    /// <summary>実行計画に現れる全名前を検証し、出せない名前があれば名指しで例外を投げる（同期生成の入口）</summary>
    /// <remarks>
    /// 名前を出力しうる経路をすべて拾う＝セクション項目（テーブル・列・制約・親子テーブル）、
    /// テーブル再構築の合成後定義とインライン外部キー、ネイティブ列順変更の移動列。
    /// </remarks>
    /// <param name="plan">実行計画</param>
    /// <param name="limit">同期の方言の識別子の長さの上限（方言の識別子クラスの <c>Limit</c>）</param>
    /// <exception cref="InvalidOperationException">
    /// 改行・制御文字を含む名前、または上限を超える・表せない文字を含む名前がある場合
    /// </exception>
    public static void Validate(SyncPlan plan, IdentifierLengthLimit limit) =>
        Validate(PlanNames(plan), limit);

    /// <summary>名前の一覧を、制御文字 → 長さと文字の順に検証する（例外文は英語が正本）</summary>
    /// <remarks>
    /// 名前そのものを文面へ載せるが、表示前に制御文字を空白へ畳む（例外文を表示する側の行構造を壊さないため）。
    /// 制御文字を先に見るのは、制御文字を含む名前の長さを数えても意味が無いため。
    /// </remarks>
    private static void Validate(List<NameOccurrence> names, IdentifierLengthLimit limit)
    {
        var unsafeNames = names
            .Where(occurrence => !IsSafe(occurrence.Name))
            .Select(occurrence => occurrence.Locate(SqlComment.Sanitize(occurrence.Name)))
            .ToList();

        if (unsafeNames.Count > 0)
        {
            throw new InvalidOperationException(
                "The following names contain a line break or control character and cannot be written into SQL safely: "
                    + string.Join(", ", unsafeNames.Distinct(StringComparer.Ordinal))
                    + ". Remove the line breaks and control characters from the names in the diagram."
            );
        }

        var rejected = new List<string>();

        foreach (var occurrence in names)
        {
            if (string.IsNullOrEmpty(occurrence.Name))
            {
                continue;
            }

            if (limit.DescribeViolation(occurrence.Name, occurrence.IsTable) is { } reason)
            {
                rejected.Add($"{occurrence.Locate(occurrence.Name)} {reason}");
            }
        }

        if (rejected.Count > 0)
        {
            throw new InvalidOperationException(
                "The following names cannot be used in this database: "
                    + string.Join("; ", rejected.Distinct(StringComparer.Ordinal))
                    + ". Shorten or rename them in the diagram."
            );
        }
    }

    /// <summary>名前と、テーブル名として測るか・置き場所の表記</summary>
    private readonly record struct NameOccurrence(
        string? Name,
        bool IsTable,
        Func<string, string> Locate
    );

    /// <summary>図が DDL へ出す名前の一覧</summary>
    private static List<NameOccurrence> DiagramNames(ErDiagram diagram)
    {
        var names = new List<NameOccurrence>();

        foreach (var entity in diagram.Entities)
        {
            AddEntityNames(names, entity.TableName, entity);
        }

        foreach (var relationship in diagram.Relationships)
        {
            names.Add(new(relationship.ConstraintName, false, ForeignKeyLocation));
        }

        return names;
    }

    /// <summary>実行計画が同期スクリプトへ出す名前の一覧</summary>
    private static List<NameOccurrence> PlanNames(SyncPlan plan)
    {
        var names = new List<NameOccurrence>();

        foreach (var item in plan.Sections.SelectMany(section => section.Items))
        {
            var table = item.TableName;

            names.Add(new(table, true, TableLocation));
            names.Add(new(item.ColumnName, false, name => ColumnLocation(table, name)));
            names.Add(new(item.Column?.Name, false, name => ColumnLocation(table, name)));
            names.Add(new(item.OldColumn?.Name, false, name => ColumnLocation(table, name)));
            names.Add(new(item.UniqueConstraintName, false, name => UniqueLocation(table, name)));
            names.Add(new(item.ForeignKeyName, false, ForeignKeyLocation));
            names.Add(new(item.Relationship?.ConstraintName, false, ForeignKeyLocation));

            foreach (var column in item.UniqueConstraintColumns)
            {
                names.Add(new(column, false, name => ColumnLocation(table, name)));
            }

            foreach (var pair in item.ForeignKeyColumnPairs)
            {
                names.Add(new(pair.ParentColumn, false, name => ColumnLocation(table, name)));
                names.Add(new(pair.ChildColumn, false, name => ColumnLocation(table, name)));
            }

            if (item.Entity is not null)
            {
                AddEntityNames(names, table, item.Entity);
            }

            names.Add(new(item.ParentEntity?.TableName, true, TableLocation));
            names.Add(new(item.ChildEntity?.TableName, true, TableLocation));
        }

        foreach (var rebuild in plan.Rebuilds)
        {
            AddEntityNames(names, rebuild.TableName, rebuild.NewDefinition);
            names.Add(new(rebuild.TableName, true, TableLocation));

            foreach (var column in rebuild.CopyColumns)
            {
                names.Add(new(column, false, name => ColumnLocation(rebuild.TableName, name)));
            }

            foreach (var foreignKey in rebuild.ForeignKeys)
            {
                names.Add(new(foreignKey.ConstraintName, false, ForeignKeyLocation));
                names.Add(new(foreignKey.ParentTable, true, TableLocation));

                foreach (var column in foreignKey.ChildColumns.Concat(foreignKey.ParentColumns))
                {
                    names.Add(new(column, false, name => ColumnLocation(rebuild.TableName, name)));
                }
            }
        }

        foreach (var reorder in plan.Reorders)
        {
            names.Add(new(reorder.TableName, true, TableLocation));

            foreach (var move in reorder.Moves)
            {
                names.Add(
                    new(move.Column.Name, false, name => ColumnLocation(reorder.TableName, name))
                );
                names.Add(
                    new(move.AfterColumn, false, name => ColumnLocation(reorder.TableName, name))
                );
            }
        }

        return names;
    }

    /// <summary>エンティティのテーブル名・列名・一意制約名をまとめて足す</summary>
    private static void AddEntityNames(List<NameOccurrence> names, string tableName, Entity entity)
    {
        names.Add(new(entity.TableName, true, TableLocation));

        foreach (var column in entity.Columns)
        {
            names.Add(new(column.Name, false, name => ColumnLocation(tableName, name)));
        }

        foreach (var constraint in entity.UniqueConstraints)
        {
            names.Add(new(constraint.Name, false, name => UniqueLocation(tableName, name)));
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
}
