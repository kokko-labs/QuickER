using System;
using System.Collections.Generic;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// いま選んでいる項目の並びと、その並びから求めた同期後の事実。選んだ項目を外す規則（<see cref="SelectionRule"/>）は、同期後の事実を
/// ここから読む（規則は自分で <see cref="PostSyncSchema"/> を作らない）
/// </summary>
/// <remarks>
/// <para>
/// 1 つの状態は 1 つの並びに対応する。除外の周（<see cref="SyncPlanner"/>）が、最初の並びから 1 つ作り、規則の取り下げを当てて並びが
/// 変わるたびに、変わった後の並びから作り直す。取り下げを返さなかった規則の次の規則は、同じ状態を読む。
/// </para>
/// <para>
/// 同期後の事実は 2 つの並びから求める。作り直し（外部キー・一意制約）を展開しない並び（<see cref="PostSync"/>）と、削除と追加へ
/// 展開した並び（<see cref="ExpandedPostSync"/>）。時点で答えの変わる材料（外す一意制約・同期後の一意制約・外す外部キー）は展開した
/// 並びから読む（作り直す制約は、いったん外れてから付け直る）。2 つを 1 つへそろえない。
/// </para>
/// <para>
/// どの材料も、初めて読まれたときに求めて覚える（読まない規則しか掛からない周では作らない）。
/// </para>
/// </remarks>
internal sealed class SelectionState
{
    private readonly SelectionContext _context;
    private PostSyncSchema? _postSync;
    private PostSyncSchema? _expandedPostSync;
    private RemainingLiveConstraints? _remainingConstraints;

    private SelectionState(IReadOnlyList<SchemaDiffItem> items, SelectionContext context)
    {
        Items = items;
        _context = context;
    }

    /// <summary>いま選んでいる項目の並びから状態を作る</summary>
    /// <param name="items">いま選んでいる項目の並び（作り直しは展開前。作った後に書き換えない）</param>
    /// <param name="context">選んだ項目に依らない文脈</param>
    public static SelectionState Of(
        IReadOnlyList<SchemaDiffItem> items,
        SelectionContext context
    ) => new(items, context);

    /// <summary>いま選んでいる項目の並び（作り直しは展開前）</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; }

    /// <summary>作り直しを展開しない並びの同期後の事実（live の図が無ければ、live に依る材料は空か図の値で答える）</summary>
    public PostSyncSchema PostSync =>
        _postSync ??= PostSyncSchema.From(
            PlanStage.ExclusionRound,
            Items,
            _context.LiveSchema,
            _context.Capabilities
        );

    /// <summary>作り直しを削除と追加へ展開した並びの同期後の事実</summary>
    public PostSyncSchema ExpandedPostSync =>
        _expandedPostSync ??= PostSyncSchema.From(
            PlanStage.ExclusionRoundExpanded,
            SyncPlanner.ExpandAlters(Items),
            _context.LiveSchema,
            _context.Capabilities
        );

    /// <summary>同期後も残る DB の外部キー・一意制約（展開した並びから求める。作り直す制約は残る側に数えない）</summary>
    /// <exception cref="InvalidOperationException">live の図が無い（残る制約は live の図からしか求められない）</exception>
    public RemainingLiveConstraints RemainingConstraints =>
        _remainingConstraints ??= RemainingLiveConstraints.From(
            ExpandedPostSync,
            _context.Capabilities,
            _context.LiveSchema
                ?? throw new InvalidOperationException(
                    "The constraints that remain after the sync need the live schema."
                )
        );
}
