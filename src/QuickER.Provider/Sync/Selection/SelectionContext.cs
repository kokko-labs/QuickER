using System.Collections.Generic;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

/// <summary>計画が選んだ項目を外す規則（<see cref="SelectionRule"/>）が読む、選んだ項目に依らない文脈</summary>
/// <remarks>
/// <para>
/// 計画の組み立て（<see cref="SyncPlanner.BuildPlan"/>）が、選んだ項目を外す段の前に 1 回だけ作り、すべての規則へ同じものを渡す。
/// 中身は作った後に変わらない。
/// </para>
/// <para>
/// 選んだ項目から決まる事実（同期後の主キー・残る制約など）は持たない。規則がそれぞれ受け取った項目の並びから求める。
/// </para>
/// </remarks>
internal sealed record SelectionContext
{
    /// <summary>対象方言の同期の宣言</summary>
    public required SyncDialectCapabilities Capabilities { get; init; }

    /// <summary>計画の入力（DB のビュー・トリガー・索引の判定などを読む。無ければ <c>null</c>）</summary>
    public required SyncPlanContext? PlanContext { get; init; }

    /// <summary>live の図（<see cref="LiveSchema.From"/> が作ったもの。計画の入力が無ければ <c>null</c>）</summary>
    public required LiveSchema? LiveSchema { get; init; }

    /// <summary>
    /// テーブル再構築の方言か（<see cref="SyncDialectCapabilities.SupportsAlterColumn"/> か
    /// <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が偽）
    /// </summary>
    public required bool IsRebuildDialect { get; init; }

    /// <summary>
    /// 取込が表の中の物を捨てた表 → 取込警告が名指しした物（列名・外部キー名、無ければ取込警告の種別名）。
    /// 再構築の方言でなければ空
    /// </summary>
    /// <remarks>キーは <see cref="QuickER.Model.SchemaNames.Comparer"/> で引く。</remarks>
    public required IReadOnlyDictionary<
        string,
        IReadOnlyList<string>
    > ImportDiscardedTables { get; init; }

    /// <summary>
    /// DB の外で登録した照合順序・関数を使う物があって作り直せない表 → 作り直しを止める物の名指し。
    /// 再構築の方言でなければ空
    /// </summary>
    /// <remarks>キーは <see cref="QuickER.Model.SchemaNames.Comparer"/> で引く。</remarks>
    public required IReadOnlyDictionary<
        string,
        IReadOnlyList<string>
    > RebuildBlockedTables { get; init; }
}
