using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 外部キーを付ける文を、断られうる順の少ないものから安定に並べる（DDL が暗黙コミットされる方言）
/// </summary>
/// <remarks>
/// <para>
/// 断られた文の後ろにある付け直しは走らず、同じ計画で先に外した外部キーが消えたまま残る。外部キーを付ける文は互いに依存せず、
/// 後ろに制約の文が無いので、断られうる文ほど後ろへ回す。
/// </para>
/// <list type="number">
/// <item>そのまま付け直す文（同じ計画が外す外部キーと、子の表・名前・列の組の集合・参照アクションが同じ）。</item>
/// <item>定義を変えて付け直す文（同じ計画が外す外部キーと、子の表・名前が同じで、列の組の集合か参照アクションが違う）。</item>
/// <item>新しい外部キーを付ける文。</item>
/// <item>参照先の列が同期後の主キーとも一意制約とも列の集合で一致しない文（候補キーを証明できない）。</item>
/// </list>
/// <para>
/// 外す文と組む名前は、文の値から求めた実効名（<see cref="ForeignKeyStepNames"/>。外す文のリレーションは組み立ての途中の文が持つ
/// DB の外部キーのリレーション）。列の組は並びを見ずに対応の集合で比べる。DB の今の図が無ければ並べない。
/// </para>
/// </remarks>
internal static class ForeignKeyAddOrder
{
    /// <summary>外部キーを付ける文を並べる</summary>
    /// <param name="steps">種類の固定の順に並べた文</param>
    /// <param name="postSync">同期後の事実</param>
    /// <param name="live">DB の今の図（無ければ並べない）</param>
    /// <param name="capabilities">方言の宣言</param>
    /// <returns>外部キーを付ける文だけを並べ替えた並び（ほかの文の位置は変えない）</returns>
    public static List<ScheduledStep> Arrange(
        List<ScheduledStep> steps,
        PostSyncSchema postSync,
        LiveSchema? live,
        SyncDialectCapabilities capabilities
    )
    {
        if (live is null)
        {
            return steps;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var postSyncUniques = postSync.UniqueColumnSets;
        var drops = steps
            .Where(s => s.Step is DropForeignKeyStep)
            .Select(s =>
            {
                var drop = (DropForeignKeyStep)s.Step;
                return (
                    Step: drop,
                    Name: ForeignKeyStepNames.Dropped(drop, s.Relationship, live, limit)
                );
            })
            .ToList();
        var ordered = new Queue<ScheduledStep>(
            steps.Where(s => s.Step is AddForeignKeyStep).OrderBy(Risk)
        );

        return [.. steps.Select(s => s.Step is AddForeignKeyStep ? ordered.Dequeue() : s)];

        // 断られうる順（0＝そのまま付け直す・1＝定義を変えて付け直す・2＝新しい外部キー・3＝証明できない）
        int Risk(ScheduledStep scheduled)
        {
            var add = (AddForeignKeyStep)scheduled.Step;

            if (!IsProven(add))
            {
                return 3;
            }

            var child = SchemaNames.ToStoredForm(add.ChildTable);
            var name = ForeignKeyStepNames.Added(add, limit);
            var drop = drops.FirstOrDefault(d =>
                SchemaNames.AreSame(SchemaNames.ToStoredForm(d.Step.ChildTable), child)
                && SchemaNames.AreSame(d.Name, name)
            );

            if (drop.Step is null)
            {
                return 2;
            }

            return
                PairSet(drop.Step.ColumnPairs) == PairSet(add.ColumnPairs)
                && drop.Step.OnDelete == add.OnDelete
                && drop.Step.OnUpdate == add.OnUpdate
                ? 0
                : 1;
        }

        // 参照先の列が同期後の主キーか一意制約と列の集合で一致するか（参照先の表の主キーが分からない・列の組が無い文は証明済み）
        bool IsProven(AddForeignKeyStep add)
        {
            var parentTable = SchemaNames.ToStoredForm(add.ParentTable);
            var postSyncKey = postSync.PrimaryKeyOf(parentTable);
            var referencedColumns = ForeignKeyColumnPairResolver
                .ParentColumns(add.ColumnPairs)
                .ToList();

            return postSyncKey is null
                || referencedColumns.Count == 0
                || SyncPlanner.StaysCandidateKey(
                    postSyncKey,
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                );
        }
    }

    /// <summary>列の組の対応の集合（並びは比べない）</summary>
    private static string PairSet(IReadOnlyList<ForeignKeyColumnNamePair> pairs) =>
        string.Join(
            ",",
            pairs
                .Select(p => $"{SchemaNames.Key(p.ParentColumn)}>{SchemaNames.Key(p.ChildColumn)}")
                .Order(StringComparer.Ordinal)
        );
}
