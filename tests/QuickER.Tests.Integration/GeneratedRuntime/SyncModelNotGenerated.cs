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
                    "null-unique-values",
                    "一意な値の null",
                    "同期のモデルの図の一意な列（model_folders.name・model_items.code）は NOT NULL で、null を作るには図を変える必要がある。docs の「競合」の節の「サーバーが SQL Server なら NULL も値として数える」の約束は SyncFixture の実 SQL Server のテストが持つ",
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
                    "モードとポリシーは種の番号から決めて種の中で固定する（24 種ごとに全積）。前の実行の設定が残る壊し方は今の種の列で赤になる（後勝ちの実行が送った行の基準の版を行の状態に書かない・ポリシーやモードを最初の実行のまま使い回す）。種の中の切り替えは、静止化が毎周 Collect → ServerWins → 後勝ちへ切り替えることが受け持つ",
                    Random + "・`SyncModel/Outcomes`"
                ),
                NotGenerated.Pending(
                    "cross-table-cycle-writes",
                    "表どうしの循環（フォルダーが品目を指す列）を書く手",
                    "R3 の後の生成器の区切り",
                    "生成器は書き手の DB で成功する手だけを選び、循環に入った行の削除は書き手の DB の外部キーが断るので、循環の列を書く手を足すには、消す前に循環を外す手を選ぶ規則も要る（並行の書き手が行の循環を作るのは防げない）。今のエンジンは循環の行を 1 つの適用で降ろし、削除伝搬も外部キーの検査を切った同じ適用で流す（src/QuickER.Runtime.Sync/CLAUDE.md「段の順と適用」）ので、作らない事実の理由は無い。表の循環の経路は決まった形の行が持つ",
                    Random
                        + " の行 `cycle-pin-to-new-item-uploads-in-rounds` / `cycle-pinned-item-is-read-back-before-folder` / `cycle-serverwins-restores-folder-pinned-to-new-item` / `refresh-empties-tables-in-a-foreign-key-cycle` / `refresh-force-in-a-foreign-key-cycle`"
                ),
                NotGenerated.Accepted(
                    "refresh-interrupt-apply-skip",
                    "洗い替え・割り込み・ピン留め",
                    "生成器は毎歩実際の DB を読んで操作を選ぶので、これらを混ぜると既存の種の操作の列が変わる。決まった形のシナリオ（割り込みの網）が受け持つ",
                    "`SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`"
                ),
                NotGenerated.Accepted(
                    "blobs-not-carried",
                    "実行ごとに blob を運ぶかを変える",
                    "docs の「無制限バイナリ列」の節（列を運ぶかどうかは端末ごとに決め、変えない）で、運ばない実行を挟むと収束の表明が成り立たない。乱数の列は常に運び、運ばない実行は対照のシナリオが持つ",
                    "`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`"
                ),
                NotGenerated.Pending(
                    "exact-unique-swap",
                    "一意な値の厳密な入れ替え（循環）",
                    "R3 の後の生成器の区切り",
                    "付け替えの形は値を空けた後に取る順だけを作り、2 行以上が互いの値を持ち合う形は作らない。docs の「競合」の節は、一意な値を入れ替えた行・回転した行が 1 回の適用で降りると約束し、決まった形の行がそれを表明するので、作らない事実の理由は無い。乱数の列へ入れるには、入れ替えを作る手と、端末の入れ替えを送るときの期待を判定役に足す必要がある",
                    Random
                        + " の行 `swap-codes-settles-in-one-sync` / `swap-folder-names-settles-in-one-sync`"
                ),
                NotGenerated.Accepted(
                    "server-extras",
                    "サーバーの他者の Extras の操作（作り直し・失敗する書き込み・キーだけのスタブ・持っていた実体での削除）",
                    "乱数の列ではサーバーの他者に Extras の操作を割り当てない。網羅の行が 4 つの操作を流す",
                    Random + " の行 `coverage-server-delete-stub`"
                ),
                NotGenerated.Accepted(
                    "untracked-rows-with-server-keys",
                    "Repository を通らずに端末へ入れた行で、サーバーにあるキーを持つもの（古いサーバーの写しの持ち込み）",
                    "docs の「既知の割り切り」の最初の項目は、記録を通らずに入った行（端末にもサーバーにも同じ行がある行）へ書く前に、同期か洗い替えを 1 回流すよう求める。最初の形 Untracked の端末は最初の同期の前に書くので、サーバーにあるキーの行を作ると docs の使い方の外の書き込みになる。その書き込みの結果（更新は Collect の同期のたびに DuplicateOnServer、削除は送られずにサーバーの行が戻る）は同期のエンジンの決まった形の行 stateless-* が固定する。生成器は追跡しない行のキーを端末のキーの範囲の別の帯に置く",
                    Random
                ),
                NotGenerated.Pending(
                    "imported-mirror-versions",
                    "ミラー版つきの行を端末へ持ち込む形",
                    "R3 の後の生成器の区切り",
                    "再開点はテーブルごとに保存してローカルの行から導かない（docs の「どこに何を作るか」）ので、行の版の列は再開点に関わらない。版の列は、送信の段が、エンジンが最後に入れた他者の内容の版と比べて元の版を選ぶ入力になり、その振る舞いは docs の「取得の前に読んだ実体の保存」が書くので、作らない事実の理由は無い。生成器に、記録させずに版の列を書く手が無い",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "copied-device-databases",
                    "別の端末やバックアップの DB の写し（行の状態の表・再開点の表ごと）で始める端末",
                    "docs に約束が無い。行の状態の表（未送信の印・基準の版）と表ごとの再開点は端末の DB の中の記録で、サーバーは端末ごとの状態を持たず、生成されるランタイムは端末の識別子を持たないので、複製した 2 台は同じ未送信の変更を二重に上げ、時間を戻した端末の基準の版と再開点は古い",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "four-or-more-devices",
                    "端末 4 台以上の世界",
                    "同期のエンジンが分岐に読むのは、その端末の手元の記録（行の状態の表＝未送信の印・基準の版・blob の写し待ち、表ごとの再開点、端末の行の版の列）とサーバーの行と版だけで、サーバーは端末ごとの状態を持たない。3 台でどの端末にもほかの端末が 2 台そろい、その 2 台の書き込みが重なる形が生まれ、4 台目でエンジンが新しく読むものは無い",
                    Random
                ),
                NotGenerated.Accepted(
                    "small-download-row-limit",
                    "既定より小さい取得の行数の上限（SyncOptions.DownloadRowLimit）",
                    "乱数の同期の手は上限を製品の既定（300,000 行）のまま渡し、乱数の世界の行数は上限に届かない。小さい上限の水準を足すと、上限を超えて断られた実行を判定役が扱う必要がある。上限を超えて断る形・超えても降ろす形は決まった形の行と決定的なシナリオが持つ",
                    Random
                        + " の行 `dl-row-limit-exceeded-with-unsent-change-refuses-and-applies-nothing` / `dl-row-limit-exceeded-after-upload-applies-everything` / `dl-row-limit-equal-to-fetched-rows-is-not-refused` / `refresh-force-over-row-limit-discards-unsent-and-is-not-refused`・`table:SyncEngineFixed` の行 `refresh-over-row-limit-with-change-written-during-fetch-is-refused` / `refresh-force-over-row-limit-with-discarded-key-rewritten-during-fetch-is-refused`"
                ),
                NotGenerated.Accepted(
                    "stale-entity-saves",
                    "同期が降ろす前に読んだ実体を、降ろした後に保存する書き込み（古い読みの保存）",
                    "生成器は手のたびに端末の DB を読んでから書くので、手元に持ち越した古い実体で保存する手を作らない。古い読みの保存を送信の段が見分ける形・見分けない形は決まった形の行が持つ",
                    "`table:SyncEngineFixed` の行 `stale-entity-saved-after-download-collect-reports-modified-on-server` / `stale-entity-saved-after-own-upload-is-not-a-conflict`"
                ),
            ]
        );
}
