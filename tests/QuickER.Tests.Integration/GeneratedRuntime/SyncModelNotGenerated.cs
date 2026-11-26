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
                    "`SyncSupportAdoSqlServerFixtureRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOrSettled`・`SyncSupportAdoSqlServerFixtureRuntimeTests.ServerCheckUniqueness_ComparesNullsAsValues`・`SyncSupportRemoteAdoSqlServerFixtureRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOverHttp`"
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
                NotGenerated.Accepted(
                    "transport-wide-seed-mode-policy",
                    "HTTP・実 SQL Server の転送の普段の広い世界の種で、後勝ち × ServerWins と 後勝ち × LocalWins",
                    "普段の広い世界の種は 4 種で、モード×ポリシーの 6 組のうち 4 組しか作れない（選び方は組が重ならない形で、4 組を作る）。広い世界の種が見るのは端末 3 台と端末ごとの最初の形で、モード×ポリシーの 6 組は同じ転送の普通の種（普段 10 種）が全部作り、夜間の広い世界の種（40 種）も 6 組を作る",
                    "`EngineFingerprintDriftTests.SyncModel_TransportSeedsAreRecorded`"
                ),
                NotGenerated.Accepted(
                    "refresh-interrupt-apply-skip",
                    "洗い替え・割り込み",
                    "生成器は毎歩実際の DB を読んで操作を選ぶので、これらを混ぜると既存の種の操作の列が変わる。決まった形のシナリオ（割り込みの網）が受け持つ",
                    "`SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`"
                ),
                NotGenerated.Accepted(
                    "blobs-not-carried",
                    "実行ごとに blob を運ぶかを変える",
                    "docs の「無制限バイナリ列」の節（列を運ぶかどうかは端末ごとに決め、変えない）で、運ばない実行を挟むと収束の表明が成り立たない。乱数の列は常に運び、運ばない実行は対照のシナリオが持つ",
                    "`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`"
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
                NotGenerated.Accepted(
                    "imported-stale-mirror-versions",
                    "サーバーの今の版より古い版（バックアップの写しなど）を、記録させずに端末の行へ持ち込む形",
                    "docs の「既知の割り切り」の最初の項目（記録を通らずに入った行へ書く前に、同期か洗い替えを 1 回流す）の外の使い方。再開点は表ごとに保存し（docs の「どこに何を作るか」）、古い版を持ち込んだ行はサーバーで変わらない限り後の同期で降り直さない（書き直すのは洗い替えだけ）ので、書く前に同期を流しても古い版のまま送られる。生成器は、端末とサーバーの両方にある行を、サーバーの今の内容と版で書き換える形（mirror-import＝MirrorServerRowOp）だけを作る。版の列が元の版を選ぶ入力になる古い読みの保存は、決まった形の行が持つ",
                    "`table:SyncEngineFixed` の行 `stale-entity-saved-then-raw-sql-update-collect-reports-modified-on-server`"
                ),
                NotGenerated.Accepted(
                    "mirror-import-events",
                    "イベントの行のミラー版の持ち込み",
                    "送信の段が行の版と最後に入れた他者の内容の版を比べる部品（SyncState.IsOlderVersion・WriteAppliedRowAsync）は表に依らず同じで、イベントの表の版の列はフォルダー・品目と同じ rowversion。日時のキーの表を写す手は、キーの文字列の作り方（Kind）を写しの文に持ち込むだけで、エンジンが新しく読むものが無い。生成器はフォルダーと品目の行だけを写す",
                    Random
                ),
                NotGenerated.Accepted(
                    "rekey-rows-with-children",
                    "子のある品目（参照するイベント・ピン留めするフォルダー）の主キーの付け替えと、フォルダー・ラベル・イベントの主キーの付け替え",
                    "同期のモデルの図の外部キーはすべて NO ACTION（SyncModelFixture の図）で、SQLite も SQL Server も子のある行の主キーの変更を断る（書き手の DB で失敗する手になる）。主キーを変える更新を記録するトリガーは表に依らず同じ形（SyncLocalTracking.TriggersFor の更新のトリガー）で、除外した無制限バイナリ列を持つのは品目だけなので、生成器は子の無い品目の付け替えだけを作る。ほかの表の主キーの変更の記録はトリガーの記録の表が持つ",
                    Random
                        + " の行 `rekey-onto-locally-deleted-key-uploads-rows-and-blobs-collect`・`table:SyncTriggerRecording` の行 `primary-key-change-int` / `primary-key-change-text` / `primary-key-change-datetime`"
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
                    "生成器は手のたびに端末の DB を読んでから書くので、手元に持ち越した古い実体で保存する手を作らない。古い読みの保存を送信の段が見分ける形・見分けない形は決まった形の行が持つ。決まった形の行は直結 × SQLite のサーバー役の世界だけで流す（ハーネスがそのサーバー役に縛られる）が、見分けは転送に依らない：送信の段は端末の DB だけで決め（行の状態の表の applied_version と、行の版を SyncState.IsOlderVersion＝先頭の 0 を落として長さとバイトで比べる）、どの転送の版も 8 バイトのビッグエンディアン（SQLite のサーバー役の採番・SQL Server の rowversion・HTTP は Base64 で往復）。選んだ元の版でサーバーが ModifiedOnServer を返すのは版で守る更新で、その道は全転送で流す決定的なシナリオの版の競合の行が持つ",
                    "`table:SyncEngineFixed` の行 `stale-entity-saved-after-download-collect-reports-modified-on-server` / `stale-entity-saved-after-own-upload-is-not-a-conflict`"
                ),
                NotGenerated.Accepted(
                    "pending-discard",
                    "未送信の一覧と捨てる口（GetPendingChangesAsync・DiscardPendingChangesAsync・DiscardAllPendingChangesAsync）を呼ぶ手",
                    "乱数の操作の列は捨てる口を呼ばない（呼んだ行は次の実行でサーバーの行に戻るので、捨てた変更の期待を判定役に足す必要がある）。決まった形の行は直結 × SQLite のサーバー役の世界だけで流すが、口は転送に依らない：一覧と捨てる印（持ち越した ServerWins の決定と同じ sw_seq）は端末の状態の表だけを読み書きし、サーバーの差分ソースに触れない。捨てた行が次の実行でサーバーの行に戻る道は、持ち越した ServerWins の決定を果たす道と同じで、その道は全転送で流す決定的なシナリオが持つ（"
                        + Random
                        + " の行 `cycle-serverwins-restores-folder-pinned-to-new-item` / `fk-lww-serverwins-label-delete-refused-while-item-reparent-is-held` / `blob-thrown-serverwins-then-collect-overwrites-newer-server-blob`）",
                    "`table:SyncEngineFixed` の行 `pending-discard-settles-collect-conflict-with-server-row` / `pending-discard-with-stale-row-is-not-discarded` / `pending-discard-is-replaced-by-refresh-without-force`"
                ),
            ]
        );
}
