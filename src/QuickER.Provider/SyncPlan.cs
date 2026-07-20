using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>
/// 同期スクリプトの 1 セクション分（同一種別の差分項目の集合）を表す実行計画の単位。
/// </summary>
/// <remarks>
/// セクションは <see cref="SyncPlanner"/> が固定順序で並べて <see cref="SyncPlan"/> を構成する。
/// 方言別レンダラー（<see cref="ISyncScriptBuilder"/> の実装）はこの単位を消費して SQL 化する。
/// </remarks>
public sealed class SyncPlanSection
{
    /// <summary>このセクションが担当する差分種別。</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>このセクションに含まれる差分項目（入力の出現順を保持する）。</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; init; } = [];
}

/// <summary>
/// 差分項目から組み立てた方言中立の実行計画。
/// </summary>
/// <remarks>
/// <see cref="SyncPlanner.BuildPlan"/> が選択済み項目を種別ごとにグループ化し、依存関係を満たす
/// 固定順序でセクションを並べたもの。方言別レンダラーはこの計画を SQL へ変換するだけでよい。
/// </remarks>
public sealed class SyncPlan
{
    /// <summary>実行順に並んだセクション一覧（空セクションは含まない）。</summary>
    public IReadOnlyList<SyncPlanSection> Sections { get; init; } = [];

    /// <summary>生成対象のセクションが 1 件も無いか。</summary>
    public bool IsEmpty => Sections.Count == 0;
}
