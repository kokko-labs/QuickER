using System.Collections.Generic;
using System.Linq;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>既存 DB スキーマと現在のダイアグラムを比較して <see cref="SchemaDiff"/> を生成するサービス</summary>
/// <remarks>リネームは扱わず「同名 = 同一」を前提とする 名称が変われば削除＋追加として検出する</remarks>
public class SchemaDiffService
{
    /// <summary>DB 現状とダイアグラムの目標状態を突き合わせて差分項目を計算する</summary>
    /// <param name="liveEntities">DB から取得した現在のエンティティ</param>
    /// <param name="liveRelationships">DB から取得した現在のリレーション</param>
    /// <param name="targetEntities">ダイアグラム上のエンティティ（期待状態）</param>
    /// <param name="targetRelationships">ダイアグラム上のリレーション</param>
    public SchemaDiff Compute(
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships
    )
    {
        var diff = new SchemaDiff();

        var liveByName = liveEntities.ToDictionary(NormalizeTable, StringComparer.OrdinalIgnoreCase);
        var targetByName = targetEntities.ToDictionary(NormalizeTable, StringComparer.OrdinalIgnoreCase);

        // ---------- テーブル/カラムの差分 ----------
        foreach (var (name, target) in targetByName)
        {
            if (!liveByName.TryGetValue(name, out var live))
            {
                // DB に存在しないテーブルは新規作成として扱う
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AddTable,
                        TableName = name,
                        Entity = target,
                        Description = $"テーブル [{name}] を作成 (列 {target.Columns.Count} 件)",
                    }
                );

                // 新規テーブル: テーブル説明
                var newTblDesc = target.Description ?? string.Empty;

                if (!string.IsNullOrEmpty(newTblDesc))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.SetTableDescription,
                            TableName = name,
                            Entity = target,
                            NewDescription = newTblDesc,
                            OldDescription = null,
                            Description = $"テーブル [{name}] の説明を設定: \"{Truncate(newTblDesc)}\"",
                        }
                    );
                }

                // 新規テーブル: 各列の説明
                foreach (var c in target.Columns)
                {
                    if (string.IsNullOrEmpty(c.Description))
                    {
                        continue;
                    }

                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.SetColumnDescription,
                            TableName = name,
                            ColumnName = c.Name,
                            Entity = target,
                            Column = c,
                            NewDescription = c.Description,
                            OldDescription = null,
                            Description = $"列 [{name}].[{c.Name}] の説明を設定: \"{Truncate(c.Description)}\"",
                        }
                    );
                }

                continue;
            }

            // 既存テーブル: カラム差分
            var liveCols = live.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            // テーブル説明 (MS_Description) の差分
            var targetTableDesc = target.Description ?? string.Empty;
            var liveTableDesc = live.Description ?? string.Empty;

            if (!string.Equals(targetTableDesc, liveTableDesc, StringComparison.Ordinal))
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.SetTableDescription,
                        TableName = name,
                        Entity = target,
                        NewDescription = targetTableDesc,
                        OldDescription = liveTableDesc,
                        Description = string.IsNullOrEmpty(targetTableDesc)
                            ? $"テーブル [{name}] の説明を削除"
                            : $"テーブル [{name}] の説明を更新: \"{Truncate(targetTableDesc)}\"",
                    }
                );
            }

            foreach (var (cname, tcol) in targetCols)
            {
                if (!liveCols.TryGetValue(cname, out var lcol))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = name,
                            ColumnName = cname,
                            Entity = target,
                            Column = tcol,
                            Description = $"列 [{name}].[{cname}] {tcol.DataType} を追加",
                        }
                    );

                    // 新規列に説明があれば、列追加と一緒に説明を設定する
                    if (!string.IsNullOrEmpty(tcol.Description))
                    {
                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.SetColumnDescription,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                NewDescription = tcol.Description,
                                OldDescription = null,
                                Description = $"列 [{name}].[{cname}] の説明を設定: \"{Truncate(tcol.Description)}\"",
                            }
                        );
                    }
                }
                else
                {
                    if (!IsSameType(lcol.DataType, tcol.DataType) || lcol.IsNullable != tcol.IsNullable)
                    {
                        var changeParts = new List<string>();

                        if (!IsSameType(lcol.DataType, tcol.DataType))
                        {
                            changeParts.Add($"型を {lcol.DataType} → {tcol.DataType} に変更");
                        }

                        if (lcol.IsNullable != tcol.IsNullable)
                        {
                            changeParts.Add($"NULL許容を {(lcol.IsNullable ? "許可" : "禁止")} → {(tcol.IsNullable ? "許可" : "禁止")} に変更");
                        }

                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                OldColumn = lcol,
                                IsSelected = false,
                                Description = $"列 [{name}].[{cname}] " + string.Join(" / ", changeParts),
                            }
                        );
                    }

                    var newColDesc = tcol.Description ?? string.Empty;
                    var oldColDesc = lcol.Description ?? string.Empty;

                    if (!string.Equals(newColDesc, oldColDesc, StringComparison.Ordinal))
                    {
                        diff.Items.Add(
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.SetColumnDescription,
                                TableName = name,
                                ColumnName = cname,
                                Entity = target,
                                Column = tcol,
                                NewDescription = newColDesc,
                                OldDescription = oldColDesc,
                                Description = string.IsNullOrEmpty(newColDesc)
                                    ? $"列 [{name}].[{cname}] の説明を削除"
                                    : $"列 [{name}].[{cname}] の説明を更新: \"{Truncate(newColDesc)}\"",
                            }
                        );
                    }
                }
            }

            foreach (var (cname, lcol) in liveCols)
            {
                if (!targetCols.ContainsKey(cname))
                {
                    diff.Items.Add(
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = name,
                            ColumnName = cname,
                            Entity = live,
                            Column = lcol,
                            IsSelected = false,
                            Description = $"列 [{name}].[{cname}] ({lcol.DataType}) を削除",
                        }
                    );
                }
            }
        }

        foreach (var (name, live) in liveByName)
        {
            if (!targetByName.ContainsKey(name))
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropTable,
                        TableName = name,
                        Entity = live,
                        IsSelected = false,
                        Description = $"テーブル [{name}] を削除",
                    }
                );
            }
        }

        // ---------- 外部キーの差分 ----------
        // 親・子・列・制約名などのシグネチャでキー化し、同一 FK の有無を集合比較で判定する
        var liveFkPairs = liveRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakeForeignKeySignature(r, liveEntities))
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var rel in targetRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var pair = MakeForeignKeySignature(rel, targetEntities);

            if (pair is null)
            {
                continue;
            }

            if (liveFkPairs.Contains(pair.Value))
            {
                continue;
            }

            var parent = targetEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = targetEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            var pkCol = ResolveReferencedColumn(rel, parent);

            if (pkCol is null)
            {
                continue;
            }

            // 明示選択された FK 列を優先し、未設定時のみ命名規約で補完する
            var fkColName = ResolveFkColumnName(rel, child, parent, pkCol);
            diff.Items.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = NormalizeTable(child),
                    ColumnName = fkColName,
                    Entity = child,
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    Description = fkColName is not null
                        ? $"外部キー [{NormalizeTable(child)}].[{fkColName}] → [{NormalizeTable(parent)}].[{pkCol.Name}] を追加"
                        : $"外部キー [{NormalizeTable(child)}] → [{NormalizeTable(parent)}] を追加 (※ FK 列が未定義のためスクリプトはスキップされます)",
                }
            );
        }

        // DB にあるが ER 図にない FK は削除候補とする
        // 取得側 Relationship には FK 名が無いため、ここでは「親→子ペアの消失」ケースのみ検出する
        var targetFkPairs = targetRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakeForeignKeySignature(r, targetEntities))
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var rel in liveRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var pair = MakeForeignKeySignature(rel, liveEntities);

            if (pair is null)
            {
                continue;
            }

            if (targetFkPairs.Contains(pair.Value))
            {
                continue;
            }

            var parent = liveEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = liveEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            diff.Items.Add(
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropForeignKey,
                    TableName = NormalizeTable(child),
                    Entity = child,
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = rel,
                    ForeignKeyName = rel.ConstraintName,
                    IsSelected = false,
                    Description = $"外部キー [{NormalizeTable(child)}] → [{NormalizeTable(parent)}] を削除",
                }
            );
        }

        return diff;
    }

    /// <summary>同一列集合のまま順序のみ異なるテーブル名の一覧を返す</summary>
    /// <remarks>列の追加・削除を伴う場合は列順差分として扱わない（ALTER で表現できないため別管理とする）</remarks>
    public static IReadOnlyList<string> DetectColumnOrderChanges(IReadOnlyList<Entity> liveEntities, IReadOnlyList<Entity> targetEntities)
    {
        var changed = new List<string>();
        var liveByName = liveEntities.ToDictionary(NormalizeTable, StringComparer.OrdinalIgnoreCase);

        foreach (var target in targetEntities)
        {
            var name = NormalizeTable(target);

            if (!liveByName.TryGetValue(name, out var live))
            {
                continue;
            }

            if (HasColumnOrderChanged(live, target))
            {
                changed.Add(name);
            }
        }

        return changed;
    }

    /// <summary>テーブル名を比較用に正規化する（前後空白を除去する）</summary>
    public static string NormalizeTable(Entity e)
    {
        return e.TableName.Trim();
    }

    /// <summary>外部キーの同一性比較に使うシグネチャ（親子・列・制約名・参照アクション）を生成する</summary>
    /// <returns>親子いずれかの参照先・参照列が解決できない場合は null</returns>
    private static (
        string Parent,
        string ParentColumn,
        string Child,
        string ChildColumn,
        string ConstraintName,
        ForeignKeyReferentialAction OnDelete,
        ForeignKeyReferentialAction OnUpdate
    )? MakeForeignKeySignature(Relationship rel, IReadOnlyList<Entity> entities)
    {
        var parent = entities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
        var child = entities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

        if (parent is null || child is null)
        {
            return null;
        }

        var parentColumn = ResolveReferencedColumn(rel, parent);

        if (parentColumn is null)
        {
            return null;
        }

        var childColumnName = ResolveFkColumnName(rel, child, parent, parentColumn);

        if (childColumnName is null)
        {
            return null;
        }

        return (
            NormalizeTable(parent).ToLowerInvariant(),
            parentColumn.Name.ToLowerInvariant(),
            NormalizeTable(child).ToLowerInvariant(),
            childColumnName.ToLowerInvariant(),
            rel.ConstraintName?.Trim().ToLowerInvariant() ?? string.Empty,
            rel.OnDelete,
            rel.OnUpdate
        );
    }

    /// <summary>子テーブル側の外部キー列名を解決する</summary>
    /// <remarks>
    /// 明示指定列 → <c>&lt;ParentTable&gt;_&lt;PkCol&gt;</c> 命名列 → PK 列と同名の列 →
    /// <c>IsForeignKey</c> フラグの列、の優先順で探索する 該当なしなら null
    /// </remarks>
    private static string? ResolveFkColumnName(Relationship rel, Entity child, Entity parent, Column pkCol)
    {
        if (rel.TargetColumnId is not null)
        {
            var byId = child.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId);

            if (byId is not null)
            {
                return byId.Name;
            }
        }

        var parentName = NormalizeTable(parent).Replace(".", "_");
        var conv = parentName + "_" + pkCol.Name;
        var byConv = child.Columns.FirstOrDefault(c => string.Equals(c.Name, conv, StringComparison.OrdinalIgnoreCase));

        if (byConv is not null)
        {
            return byConv.Name;
        }

        var byPkName = child.Columns.FirstOrDefault(c => !c.IsPrimaryKey && string.Equals(c.Name, pkCol.Name, StringComparison.OrdinalIgnoreCase));

        if (byPkName is not null)
        {
            return byPkName.Name;
        }

        var byFlag = child.Columns.FirstOrDefault(c => c.IsForeignKey);
        return byFlag?.Name;
    }

    /// <summary>親テーブル側の参照先列を解決する（明示指定が無ければ主キーを採用する）</summary>
    private static Column? ResolveReferencedColumn(Relationship rel, Entity parent)
    {
        if (rel.SourceColumnId is not null)
        {
            var byId = parent.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);

            if (byId is not null)
            {
                return byId;
            }
        }

        return parent.Columns.FirstOrDefault(c => c.IsPrimaryKey);
    }

    /// <summary>データ型を大文字小文字・前後空白を無視して同一とみなせるか判定する</summary>
    private static bool IsSameType(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>列集合が一致するテーブルで列順のみが変更されているかを判定する</summary>
    private static bool HasColumnOrderChanged(Entity live, Entity target)
    {
        if (live.Columns.Count != target.Columns.Count)
        {
            return false;
        }

        var liveNames = live.Columns.Select(c => c.Name).ToList();
        var targetNames = target.Columns.Select(c => c.Name).ToList();

        var liveSet = new HashSet<string>(liveNames, StringComparer.OrdinalIgnoreCase);
        var targetSet = new HashSet<string>(targetNames, StringComparer.OrdinalIgnoreCase);

        if (!liveSet.SetEquals(targetSet))
        {
            return false;
        }

        for (var i = 0; i < liveNames.Count; i++)
        {
            if (!string.Equals(liveNames[i], targetNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>説明文を指定長で切り詰め、超過時は末尾に省略記号を付ける</summary>
    private static string Truncate(string s, int max = 30) => s.Length <= max ? s : s.Substring(0, max) + "…";
}
