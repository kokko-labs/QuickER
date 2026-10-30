using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>DB の来歴（DB 側の図を、どの版の QuickER の DDL で作ったか）</summary>
internal enum SyncPlanHistory
{
    /// <summary>今の版の DDL：名前の無い外部キーは、今の DDL と同じ実効名（方言の上限に収めた既定名）で作る</summary>
    Current,

    /// <summary>
    /// v0.1.0〜v0.2.0 の DDL を PostgreSQL へ流した DB：名前の無い外部キーは縮める前の既定名で書かれ、PostgreSQL が識別子の上限
    /// （63 バイト）で黙って切り詰めた名前で残る（既定名を上限に収める規則は v0.3.0 から）
    /// </summary>
    PostgreSqlUnshortened,
}

/// <summary>来歴の規則（DB 側の図の名前の無い外部キーに、その版の DDL が作る名前を付ける）</summary>
/// <remarks>
/// 公開版の DDL 生成器そのものは使わず、版ごとの規則を関数で再現する。来歴は現実にあり得る状態だけを作る
/// （その版の DDL を流してできる DB の状態。同じ名前の外部キーを同じ表に 2 本置くような、DB が断る状態は作らない）。
/// </remarks>
internal static class SyncPlanHistories
{
    /// <summary>来歴で名付けた外部キー（目標の図で名前を戻すために、子の表・子の列・親の表の名前で引く）</summary>
    /// <param name="ChildTable">子の表</param>
    /// <param name="ChildColumns">子の列（列ペアの並び）</param>
    /// <param name="ParentTable">親の表</param>
    /// <param name="Name">付けた名前</param>
    /// <param name="Changed">今の DDL の名前と違うか（来歴が実際に効いたか）</param>
    /// <param name="ExceedsLimit">縮める前の既定名が方言の上限を超えるか</param>
    internal sealed record NamedForeignKey(
        string ChildTable,
        IReadOnlyList<string> ChildColumns,
        string ParentTable,
        string Name,
        bool Changed,
        bool ExceedsLimit
    );

    /// <summary>
    /// DB 側の図の名前の無い外部キーに、来歴の規則で名前を付ける（図を書き換える）。名付けた外部キーを返す
    /// </summary>
    /// <param name="history">来歴</param>
    /// <param name="diagram">DB 側の図</param>
    /// <param name="limit">方言の識別子の長さの上限</param>
    public static IReadOnlyList<NamedForeignKey> Apply(
        SyncPlanHistory history,
        ErDiagram diagram,
        IdentifierLengthLimit limit
    )
    {
        var details = ConstraintNames.ResolveForeignKeyNameDetails(
            diagram.Entities,
            diagram.Relationships,
            limit
        );
        var named = new List<NamedForeignKey>();

        foreach (var relationship in diagram.Relationships.Where(r => r.ConstraintName is null))
        {
            if (!details.TryGetValue(relationship, out var detail))
            {
                continue;
            }

            var name = history switch
            {
                SyncPlanHistory.Current => detail.Name,
                SyncPlanHistory.PostgreSqlUnshortened => limit.Truncate(detail.UnshortenedName),
                _ => throw new ArgumentOutOfRangeException(nameof(history), history, null),
            };
            var child = diagram.Entities.Single(e => e.Id == relationship.TargetEntityId);
            var parent = diagram.Entities.Single(e => e.Id == relationship.SourceEntityId);
            named.Add(
                new NamedForeignKey(
                    child.TableName,
                    [
                        .. relationship.ColumnPairs.Select(pair =>
                            child.Columns.Single(c => c.Id == pair.TargetColumnId).Name
                        ),
                    ],
                    parent.TableName,
                    name,
                    !string.Equals(name, detail.Name, StringComparison.Ordinal),
                    !limit.Fits(detail.UnshortenedName)
                )
            );
            relationship.ConstraintName = name;
        }

        return named;
    }

    /// <summary>
    /// 目標の図で、来歴で名付けた外部キーの名前を外す（利用者の図はその外部キーに名前を付けていない＝その版の DDL が既定名で
    /// DB に作った、という来歴）
    /// </summary>
    /// <param name="named">来歴で名付けた外部キー</param>
    /// <param name="entities">目標の図の表</param>
    /// <param name="relationships">目標の図の外部キー</param>
    public static void Forget(
        IReadOnlyList<NamedForeignKey> named,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    )
    {
        foreach (var key in named)
        {
            var child = entities.Single(e =>
                string.Equals(e.TableName, key.ChildTable, StringComparison.OrdinalIgnoreCase)
            );
            var parent = entities.Single(e =>
                string.Equals(e.TableName, key.ParentTable, StringComparison.OrdinalIgnoreCase)
            );
            var relationship = relationships.Single(r =>
                r.TargetEntityId == child.Id
                && r.SourceEntityId == parent.Id
                && string.Equals(r.ConstraintName, key.Name, StringComparison.OrdinalIgnoreCase)
                && r.ColumnPairs.Select(p =>
                        child.Columns.Single(c => c.Id == p.TargetColumnId).Name
                    )
                    .SequenceEqual(key.ChildColumns, StringComparer.OrdinalIgnoreCase)
            );
            relationship.ConstraintName = null;
        }
    }
}
