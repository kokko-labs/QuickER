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
                NotGenerated.Pending(
                    "alternate-key-handoff",
                    "同じ保存で一方の行が代替キーを手放し、もう一方の行が取る（入れ替えを含む）",
                    "G3",
                    "代替キーを取る役は、消した行の code を取る形と誰も持たない値へ変える形だけ。EF Core 版の 2 つめの文脈の書き込みと文の順はこの形で分岐する",
                    Cases + " の行 `alternate-key-taken-from-deleted-kit-*`"
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
