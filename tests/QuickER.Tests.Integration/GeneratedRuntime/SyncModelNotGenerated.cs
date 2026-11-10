using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの乱数の操作の列の生成器（<c>SyncModelGenerator</c>）が作らない形の宣言。乱数の列のテストと実行の結果の表が同じ生成器の入力を使う
/// （QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class SyncModelNotGenerated
{
    private const string Random = "`SyncModel/Random`";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "SyncModelGenerator",
            [
                ProtectionNames.EnginePart(EngineIds.SyncModel, "Random"),
                ProtectionNames.EnginePart(EngineIds.SyncModel, "Outcomes"),
            ],
            [
                NotGenerated.Accepted(
                    "failing-writes",
                    "書き手の DB で失敗する書き込み（親の無い行・子の残る親の削除・一意制約の重複）",
                    "書き込みは書き手の DB で成功する操作だけを選び、失敗する書き込みは失敗の注入の段（因子 FaultKind）で別に作る",
                    Random
                ),
                NotGenerated.Accepted(
                    "external-table-substitute",
                    "外部の ISyncTable 実装の代役",
                    "エンジンの内部の面を持たない実装は、外部キーで断られた書き込みの報告などを持たず公開の面どおりに例外を投げる（その差は不具合ではない）。代役は決まった形のシナリオだけが使う",
                    Random
                        + "（外部の表の決まった形のシナリオ 26-external-table-overwrites-pending-edit）"
                ),
                NotGenerated.Pending(
                    "same-key-by-two-writers",
                    "書き手どうしが同じキーを作る",
                    "G4",
                    "キーは書き手ごとに別の範囲から採る（端末 1＝1000 台・端末 2＝2000 台・サーバーの他者＝3000 台）。エンジンは同じキーの挿入を DuplicateOnServer で分岐する",
                    Random + " の行 `coverage-duplicate-on-server`"
                ),
                NotGenerated.Pending(
                    "unique-value-spellings",
                    "一意な値の別表記（大文字小文字・末尾の空白・非 ASCII）・空・null・大きな blob",
                    "G4",
                    "値は種の中で一意な ASCII の連番。一意な値の検索と、SQL Server と SQLite の照合順序の差はこれらの値で分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "multiple-excluded-tables",
                    "同期の実行で 2 表以上を除外する",
                    "G4",
                    "除外はいつも 1 表。エンジンは実行ごとの除外の範囲で分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "mode-policy-switch-within-seed",
                    "1 つの種の中でモード・ポリシーを切り替える",
                    "G4",
                    "モードとポリシーは種の番号から決めて種の中で固定する（全組を出すため）。エンジンは実行ごとのモード・ポリシーで分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "cross-table-cycle-writes",
                    "表どうしの循環（フォルダーが品目を指す列）を書く手",
                    "G4",
                    "乱数の列は循環の列を書かない。循環のある範囲の洗い替えは決まった形の行だけが受け持つ",
                    Random
                        + " の行 `refresh-empties-tables-in-a-foreign-key-cycle` / `refresh-force-in-a-foreign-key-cycle` / `refresh-failed-wipe-in-a-foreign-key-cycle`"
                ),
                NotGenerated.Accepted(
                    "refresh-interrupt-apply-skip",
                    "洗い替え・割り込み・適用の Before フックの飛ばし・ピン留め",
                    "生成器は毎歩実際の DB を読んで操作を選ぶので、これらを混ぜると既存の種の操作の列が変わる。決まった形のシナリオ（割り込みの網）が受け持つ",
                    "`SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`"
                ),
                NotGenerated.Accepted(
                    "blobs-not-carried",
                    "実行ごとに blob を運ぶかを変える",
                    "docs の割り切り（運ぶかは端末ごとにそろえる）で、運ばない実行を挟むと収束の表明が成り立たない。乱数の列は常に運び、運ばない実行は対照のシナリオが持つ",
                    "`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`"
                ),
                NotGenerated.Accepted(
                    "exact-unique-swap",
                    "一意な値の厳密な入れ替え（循環）",
                    "同じバッチの入れ替えは docs の既知の割り切りで、静止化が終わらないことを受け入れている。付け替えの形は値を空けた後に取る順だけを作る",
                    Random
                        + " の行 `swap-without-refresh-does-not-settle` / `swap-folder-names-without-refresh-does-not-settle`"
                ),
                NotGenerated.Accepted(
                    "server-extras",
                    "サーバーの他者の Extras の操作（作り直し・失敗する書き込み・キーだけのスタブ・持っていた実体での削除）",
                    "乱数の列ではサーバーの他者に Extras の操作を割り当てない。網羅の行が 4 つの操作を流す",
                    Random + " の行 `coverage-server-delete-stub`"
                ),
                NotGenerated.Pending(
                    "more-devices-and-preloaded-devices",
                    "端末 3 台以上・端末が最初から行を持つ",
                    "同期のモデルの残りの区切り（未起動）",
                    "世界は端末 2 台で、端末は空から始める。ジャーナル・ミラー版・受理記録は端末ごとに持つので、3 台目と最初から持つ行の扱いを総当たりが通らない",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
