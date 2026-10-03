using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>取込で現在の図を置き換える前に、確認が要るかを判定する（言語中立・UI 非依存）</summary>
/// <remarks>
/// <para>
/// DB 取込・C# コードの取込・Excel 定義書のマージ取込は、どれも「取込結果を現在の図の Id へ寄せたうえで
/// （<see cref="DiagramMergeReconciler"/>）図を丸ごと差し替える」。差し替えで失うものが無いときだけ
/// 確認を省く、という判定はこの 3 経路で同じなので、ここに 1 回だけ書く（確認文の組み立ては
/// 文言が各機能の resx にあるため、各経路と <c>ReplacementLossMessage</c> の側に置く）。
/// </para>
/// <para>
/// 構造署名（<see cref="SchemaSignature"/>）はテーブル・列の説明と名前付きクエリを見ない。署名の一致だけを
/// 根拠に確認を省くと、未保存の説明が取込値で上書きされ、参照の切れたクエリが無言で消える。
/// そのため「構造が同じ（または失うものが無い）」に加えて、壊れるクエリも説明の上書きも無いことを条件にする。
/// </para>
/// </remarks>
public static class DiagramReplacementCheck
{
    /// <summary>図を丸ごと置き換えても失うものが無いか（エンティティ・名前付きクエリ・未保存変更のいずれも無い）</summary>
    /// <remarks>
    /// エンティティ数だけで判定すると、クエリだけの図や未保存の編集内容を無確認で捨ててしまう。
    /// GUI 本体の <c>MainViewModel.HasNothingToLose</c> と同じ判定で、ホスト契約の
    /// <c>IErDiagramHost.IsEmpty</c>（＝エンティティ 0・AI チャットの自動整列用）とは意味が異なる。
    /// </remarks>
    /// <param name="current">現在の図</param>
    /// <param name="isDirty">現在の図に未保存の変更があるか</param>
    public static bool HasNothingToLose(ErDiagram current, bool isDirty) =>
        current.Entities.Count == 0 && current.Queries.Count == 0 && !isDirty;

    /// <summary>取込結果が現在の図と構造的に同一か（構造署名の比較）</summary>
    /// <param name="current">現在の図</param>
    /// <param name="entities">取込結果のエンティティ</param>
    /// <param name="relationships">取込結果のリレーション</param>
    public static bool HasSameStructure(
        ErDiagram current,
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    ) =>
        SchemaSignature.Compute(current.Entities, current.Relationships)
        == SchemaSignature.Compute(entities, relationships);

    /// <summary>マージ取込の結果で図を置き換えるとき、確認を省いてよいか</summary>
    /// <param name="hasNothingToLose">
    /// 現在の図に失うものが無いか（呼び出し側の判定。ホストが持つ述語か <see cref="HasNothingToLose"/> を渡す）
    /// </param>
    /// <param name="current">現在の図</param>
    /// <param name="merged">現在の図の Id へ寄せた取込結果</param>
    /// <param name="relationships">
    /// 置き換えに使うリレーション（通常は <see cref="DiagramMergeResult.Relationships"/>。C# コードの取込は
    /// 現在の図から参照アクション等を補った後のものを渡す）
    /// </param>
    /// <returns>構造が同じ（または失うものが無い）うえで、壊れるクエリも説明の上書きも無ければ <c>true</c></returns>
    public static bool CanSkipConfirmation(
        bool hasNothingToLose,
        ErDiagram current,
        DiagramMergeResult merged,
        IEnumerable<Relationship> relationships
    ) =>
        (hasNothingToLose || HasSameStructure(current, merged.Entities, relationships))
        && merged.BrokenQueries.Count == 0
        && merged.DescriptionOverwriteCount == 0;
}
