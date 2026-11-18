namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 計画器が自分で作った差分の項目に付ける印（どこから来た項目か・外して付け直す対）
/// </summary>
/// <remarks>
/// <para>
/// 利用者が選んだ項目は印を持たない。印は <see cref="Implicit"/>・<see cref="Expanded"/>・<see cref="Rearranged"/>・<see cref="Replacement"/> のどれか
/// 1 つで作り、出どころの 3 つのメンバーはちょうど 1 つだけが値を持つ。
/// </para>
/// <para>
/// 計画・同期スクリプト・警告は印を読まない。読むのは、計画を文（<see cref="SyncStep"/>）の並びとして読み取る側だけ。
/// </para>
/// </remarks>
internal sealed record PlanItemMark
{
    private PlanItemMark() { }

    /// <summary>利用者の選んでいない項目を足した理由（暗黙の項目だけ。ほかは null）</summary>
    public ImplicitStepReason? ImplicitReason { get; private init; }

    /// <summary>
    /// 元になった選んだ項目（作り直しを展開した削除と追加は元の作り直しの項目、列の組を並べ替えた外部キーの追加の複製は元の追加の項目。
    /// ほかは null）
    /// </summary>
    public SchemaDiffItem? DerivedFrom { get; private init; }

    /// <summary>この項目が置き換えた選んだ項目（一部だけを外して残した代わりの項目だけ。ほかは null）</summary>
    public SchemaDiffItem? Replaces { get; private init; }

    /// <summary>
    /// 外して付け直す対（外す項目と付け直す項目が別々のときに、2 つが同じ印を持つ。1 つの項目が解除と付与を兼ねる主キーの変更と、
    /// 対の無い項目は null）
    /// </summary>
    public ReaddPair? Pair { get; private init; }

    /// <summary>利用者の選んでいない項目に付ける印を作る</summary>
    /// <param name="reason">足した理由</param>
    /// <param name="pair">外して付け直す対（付け直さない項目は null）</param>
    /// <returns>暗黙の項目の印</returns>
    public static PlanItemMark Implicit(ImplicitStepReason reason, ReaddPair? pair = null) =>
        new() { ImplicitReason = reason, Pair = pair };

    /// <summary>作り直しを展開した削除・追加に付ける印を作る</summary>
    /// <param name="alter">展開の元になった作り直しの項目</param>
    /// <param name="pair">削除と追加の対（外す側の定義が無く追加だけに展開したときは null）</param>
    /// <returns>展開した項目の印</returns>
    public static PlanItemMark Expanded(SchemaDiffItem alter, ReaddPair? pair) =>
        new() { DerivedFrom = alter, Pair = pair };

    /// <summary>列の組を並べ替えた外部キーの追加の複製に付ける印を作る</summary>
    /// <param name="source">複製の元の外部キーの追加の項目</param>
    /// <returns>並べ替えた複製の印</returns>
    public static PlanItemMark Rearranged(SchemaDiffItem source) => new() { DerivedFrom = source };

    /// <summary>選んだ項目の一部だけを外して残した代わりの項目に付ける印を作る</summary>
    /// <param name="original">置き換えた選んだ項目</param>
    /// <returns>代わりの項目の印</returns>
    public static PlanItemMark Replacement(SchemaDiffItem original) =>
        new() { Replaces = original };
}
