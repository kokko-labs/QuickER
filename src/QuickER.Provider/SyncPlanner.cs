using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider;

/// <summary>
/// 差分項目から方言中立の実行計画（<see cref="SyncPlan"/>）を組み立てるサービス。
/// </summary>
/// <remarks>
/// <para>
/// 選択済みの差分を「依存関係で失敗しない固定順序」でセクション化するのが責務。方言別レンダラー
/// （<see cref="ISyncScriptBuilder"/> の実装）はこの計画を SQL へ変換するだけでよく、選択フィルタ・
/// 出力順序・種別グループ化の知識を各方言に重複させない。
/// </para>
/// <para>
/// 現状（Phase 1）は方言に依らず同一の計画を返す。将来フェーズでは <paramref name="capabilities"/> を
/// 参照し、逐次 DDL で表現できない変更（SQLite のテーブル再構築・MySQL のネイティブ列順変更など）を
/// セクションへ振り分ける拡張点になる。
/// </para>
/// </remarks>
public sealed class SyncPlanner
{
    /// <summary>
    /// セクションの出力順序。依存関係による失敗を避けるため、
    /// テーブル / 列の追加 → FK 解除 → 列 / テーブル削除 → FK 追加 → 説明設定の順に並べる。
    /// </summary>
    private static readonly SchemaDiffKind[] SectionOrder =
    [
        SchemaDiffKind.AddTable,
        SchemaDiffKind.AddColumn,
        SchemaDiffKind.AlterColumn,
        SchemaDiffKind.DropForeignKey,
        SchemaDiffKind.DropColumn,
        SchemaDiffKind.DropTable,
        SchemaDiffKind.AddForeignKey,
        SchemaDiffKind.SetTableDescription,
        SchemaDiffKind.SetColumnDescription,
    ];

    /// <summary>選択済みの差分項目から実行計画を組み立てる</summary>
    /// <param name="items">全差分項目（未選択・情報表示専用の項目を含んでよい）</param>
    /// <param name="capabilities">
    /// 対象方言の同期ケーパビリティ。Phase 1 では計画へ影響しないが、将来のセクション振り分けに用いる。
    /// </param>
    /// <returns>選択済み項目のみを固定順序でセクション化した計画（空セクションは含まない）</returns>
    public SyncPlan BuildPlan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities
    )
    {
        // 選択済みの項目のみを対象にする（未選択は完全に除外）
        var selected = items.Where(i => i.IsSelected).ToList();

        var sections = new List<SyncPlanSection>();

        foreach (var kind in SectionOrder)
        {
            // 種別ごとに抽出する。RebuildTable 等 SectionOrder に無い種別はここで自然に除外される
            // （現状 RebuildTable は情報表示専用で SQL 生成対象外）
            var subset = selected.Where(i => i.Kind == kind).ToList();

            if (subset.Count == 0)
            {
                continue;
            }

            sections.Add(new SyncPlanSection { Kind = kind, Items = subset });
        }

        return new SyncPlan { Sections = sections };
    }
}
