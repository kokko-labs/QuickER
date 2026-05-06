using System.Collections.Generic;
using System.Linq;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// 既存 DB のスキーマ (importer 取得結果) と現在のダイアグラム (Entities/Relationships) を比較して
/// <see cref="SchemaDiff"/> を生成します。リネームは扱わず「同名 = 同一」の前提です。
/// </summary>
public class SchemaDiffService
{
    /// <summary>
    /// 差分を計算します。
    /// </summary>
    /// <param name="liveEntities">DB から取得した現在のエンティティ。</param>
    /// <param name="liveRelationships">DB から取得した現在のリレーション。</param>
    /// <param name="targetEntities">ダイアグラム上のエンティティ (= 期待状態)。</param>
    /// <param name="targetRelationships">ダイアグラム上のリレーション。</param>
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
                // 新規テーブル
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
                    if (!IsSameType(lcol.DataType, tcol.DataType))
                    {
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
                                Description = $"列 [{name}].[{cname}] 型を {lcol.DataType} → {tcol.DataType} に変更",
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
        // (Parent, Child) ペアでキー化。テーブル名は正規化済みに合わせる。
        var liveFkPairs = liveRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakePair(r, liveEntities))
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var rel in targetRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var pair = MakePair(rel, targetEntities);

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

            var pkCol = parent.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            if (pkCol is null)
            {
                continue;
            }

            // 規約: FK 列名は <ParentTable>_<PkCol>。子テーブルにこの列があるか PK 名と同名の列があれば採用。
            var fkColName = ResolveFkColumnName(child, parent, pkCol);
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

        // 既存 DB にあるが ER 図にない FK は削除候補 (フェーズ2)
        // 注意: 取得側 Relationship には FK 名が含まれないため、ここでは「親→子のペアが消えた」ケースだけ示す。
        var targetFkPairs = targetRelationships
            .Where(r => r.Type != RelationshipType.ManyToMany)
            .Select(r => MakePair(r, targetEntities))
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var rel in liveRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var pair = MakePair(rel, liveEntities);

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
                    IsSelected = false,
                    Description = $"外部キー [{NormalizeTable(child)}] → [{NormalizeTable(parent)}] を削除",
                }
            );
        }

        return diff;
    }

    /// <summary>
    /// DB 側とダイアグラム側で「同一列集合だが順序のみ異なる」テーブル名一覧を返します。
    /// 列追加/削除がある場合は、列順差分としては扱いません。
    /// </summary>
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

    /// <summary>テーブル名を「schema.name」または「name」の正規形に揃えます。</summary>
    public static string NormalizeTable(Entity e)
    {
        return e.TableName.Trim();
    }

    /// <summary>(Parent正規名, Child正規名) のタプルを生成。テーブルが見つからない場合 null。</summary>
    private static (string Parent, string Child)? MakePair(Relationship rel, IReadOnlyList<Entity> entities)
    {
        var parent = entities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
        var child = entities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

        if (parent is null || child is null)
        {
            return null;
        }

        return (NormalizeTable(parent).ToLowerInvariant(), NormalizeTable(child).ToLowerInvariant());
    }

    /// <summary>
    /// 子テーブル側の FK 列名を解決します。
    /// 優先順位: 1) <c>&lt;ParentTable&gt;_&lt;PkCol&gt;</c> 命名の列, 2) PK 列と同名の列, 3) <c>IsForeignKey</c> フラグの列。
    /// 該当なしなら null。
    /// </summary>
    private static string? ResolveFkColumnName(Entity child, Entity parent, Column pkCol)
    {
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

    private static bool IsSameType(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 同一列集合（件数・名前一致）のテーブルで、列順のみ変更されているかを判定します。
    /// </summary>
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

    private static string Truncate(string s, int max = 30) => s.Length <= max ? s : s.Substring(0, max) + "…";
}
