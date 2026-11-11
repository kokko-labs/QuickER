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
                NotGenerated.Accepted(
                    "null-unique-values",
                    "一意な値の null",
                    "同期のモデルの図の一意な列（model_folders.name・model_items.code）は NOT NULL で、null を作るには図を変える必要がある。docs の「SQL Server なら NULL も値として数える」の約束は SyncFixture の実 SQL Server のテストが持つ",
                    "`SyncSqlServerRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOrSettled`・`SyncSqlServerRuntimeTests.ServerCheckUniqueness_ComparesNullsAsValues`・`SyncSqlServerHttpRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOverHttp`"
                ),
                NotGenerated.Accepted(
                    "empty-blobs",
                    "書き手が書く空の blob",
                    "同期は除外した NOT NULL の blob を、コピーしていない行で空のバイト列で埋める（FillUnsetUnboundedBinaries）。書き手が書いた空の blob と区別できず、空の blob の期待（N12）が成り立たない",
                    Random
                        + " の行 `n12-*`・`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`"
                ),
                NotGenerated.Accepted(
                    "mode-policy-switch-within-seed",
                    "1 つの種の中でモード・ポリシーを切り替える",
                    "モードとポリシーは種の番号から決めて種の中で固定する（24 種ごとに全積）。前の実行の設定が残る壊し方は今の種の列で赤になる（後勝ちの実行が受理記録を残さない・ポリシーやモードを最初の実行のまま使い回す）。種の中の切り替えは、静止化が毎周 Collect → ServerWins → 後勝ちへ切り替えることが受け持つ",
                    Random + "・`SyncModel/Outcomes`"
                ),
                NotGenerated.Accepted(
                    "cross-table-cycle-writes",
                    "表どうしの循環（フォルダーが品目を指す列）を書く手",
                    "並行の書き手が行の循環を作るのを生成器は防げず（端末 1 がフォルダーを品目にピン留めし、端末 2 がその品目をそのフォルダーの下へ移す）、サーバーで消えた循環の行は端末に残る（docs の既知の割り切り）ので、静止化が終わらない種が出て、受け入れの署名で外すとその種の後ろの不具合を隠す。表の循環の経路は決まった形の行が持つ",
                    Random
                        + " の行 `cycle-pin-to-new-item-uploads-in-rounds` / `cycle-pinned-item-is-read-back-before-folder` / `cycle-serverwins-restores-folder-pinned-to-new-item` / `refresh-empties-tables-in-a-foreign-key-cycle` / `refresh-force-in-a-foreign-key-cycle` / `refresh-failed-wipe-in-a-foreign-key-cycle`"
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
