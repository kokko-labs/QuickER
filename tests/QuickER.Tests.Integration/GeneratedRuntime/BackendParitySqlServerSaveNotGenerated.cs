using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server の系統の乱数の保存の生成器（<c>BackendParitySqlServerSaveGenerator</c>）が作らない形の宣言
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class BackendParitySqlServerSaveNotGenerated
{
    private const string Cases = "`BackendParitySqlServerRuntimeTests`";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "BackendParitySqlServerSaveGenerator",
            [ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "ParitySqlServerSaves")],
            [
                NotGenerated.Accepted(
                    "delete-gone-row",
                    "他者が消した行の削除",
                    "行の無い削除の戻り値は実装先で割れることを受け入れている（docs）。次元表の網羅しない組にも受け入れとして載る",
                    "`GeneratedRuntime/Parity`（行 `crud-update-missing-and-delete-twice`）"
                ),
                NotGenerated.Accepted(
                    "referenced-code-change",
                    "部品に参照された code の変更と、部品を持つ行の他者による削除",
                    "外部キーで保存の全体が断られ、ほかの行を比べなくなる。参照された代替キーの更新は決まった形のケースが受け持つ",
                    Cases
                        + " の行 `alternate-key-referenced-update` / `alternate-key-referenced-graph-save`"
                ),
                NotGenerated.Accepted(
                    "inserted-taker-before-releaser",
                    "挿入する行（新しい kit・insertWhenUpdateMissing で挿入へ切り替わる消えた kit）を、まだ code を持つ手放す行より前に渡し、その code を取る保存",
                    "実装先で結果が割れることを受け入れている（docs のグラフ保存の書く順と EF Core モードの代替キーの段落：QuickER 版はグラフの順に書いて挿入が一意制約に断られ、EF Core 版は変わった代替キーを先に書いて通す）。生成器は挿入する取る行を手放す行の直後へ移す",
                    Cases
                        + " の行 `alternate-key-new-takes-released-taker-first` / `alternate-key-gone-insert-takes-released-taker-first`"
                ),
                NotGenerated.Pending(
                    "kit-parts-writes",
                    "部品（kit_parts）の新しい行と更新",
                    "実装先の一致の残りの区切り（未起動）",
                    "保存は kits の行だけに役を引き、部品は他者の削除の材料にしか使わない。部品の書き込みは外部キーの並びと書く順で分岐する",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
