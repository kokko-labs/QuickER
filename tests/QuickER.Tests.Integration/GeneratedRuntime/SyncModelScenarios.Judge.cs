using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 判定役の対照の行（<c>judge-*</c>）。端末のエンジンを外から壊す包み（<see cref="SyncModelJudgeDevice"/>）で違反の形を作り、
/// 判定役が宣言した署名の集合どおりに名指しすることを確かめる。製品の本当の振る舞いや利用者の正当な手では作らない。直結の
/// 転送だけで流す（包みがサーバーへ直接書く）
/// </summary>
internal static partial class SyncModelScenarios
{
    /// <summary>
    /// 直結だけ（判定役の行は転送に依らない。<see cref="All"/> の初期化から呼ばれるので、静的なフィールドの初期化の順に
    /// 依らないよう呼ぶたびに作る）
    /// </summary>
    private static IReadOnlySet<SyncModelTransport> DirectOnly =>
        new HashSet<SyncModelTransport> { SyncModelTransport.Direct };

    private static IEnumerable<SyncModelScenario> JudgeScenarios()
    {
        // 照合 1：後勝ちの同期の中で、端末が持ち分を持たない行（サーバーの他者が変えた後の古い写し）の古い内容をサーバーへ書く
        yield return new(
            "judge-stale-overwrite-is-a-violation",
            [Folder(SV, 950)],
            [
                new UpdateFolderOp(SV, Direct, 950, null, "server-new"),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [new JudgeBreak(JudgeBreakKind.WriteLocalToServer, D1, 950, OnRun: 2)]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/stale-overwrite:model_folders",
                "3/lost:model_folders:value",
            },
        };

        // 照合 2（上書き）：持ち越しを宣言したエンジンで、後勝ち＋ServerWins の実行が何も戻さずに終わった行（決定）を、後の
        // Collect の実行がサーバーへ書く（記録は残っているので、書くのは製品のエンジン自身）
        yield return new(
            "judge-carry-overwrite-is-a-violation",
            [Folder(SV, 951)],
            [
                new UpdateFolderOp(D1, Direct, 951, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(true, [new JudgeBreak(JudgeBreakKind.SkipRun, D1, 951, OnRun: 2)]),
            ExpectedJudgeSignatures = new HashSet<string> { "5/carry:overwrote:model_folders" },
        };

        // 照合 2（blob の上書き）：持ち越しを宣言したエンジンで、後勝ち＋ServerWins の実行が何も戻さずに終わった品目の blob
        // （決定）を、決定の後に行を編集しないまま、後の Collect の実行がサーバーへ書く
        yield return new(
            "judge-carry-blob-overwrite-is-a-violation",
            [Folder(SV, 977), Item(SV, 977, folder: 977)],
            [
                new WritePayloadOp(D1, 977, "0A0977"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(true, [new JudgeBreak(JudgeBreakKind.SkipRun, D1, 977, OnRun: 2)]),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/carry:overwrote:model_items:payload",
            },
        };

        // 照合 2（品目の行の上書き）：持ち越しを宣言したエンジンで、後勝ち＋ServerWins の実行が何も戻さずに終わった品目の行
        // （決定）を、決定の後に行も blob も編集しないまま、後の Collect の実行がサーバーへ書く（blob の編集で行の決定を外す
        // 逆向きの規則は、blob が変わっていない品目には掛からない）
        yield return new(
            "judge-carry-item-row-overwrite-is-a-violation",
            [Folder(SV, 976), Item(SV, 976, folder: 976)],
            [
                new UpdateItemOp(D1, Direct, 976, 976, null, "c-976", "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(true, [new JudgeBreak(JudgeBreakKind.SkipRun, D1, 976, OnRun: 2)]),
            ExpectedJudgeSignatures = new HashSet<string> { "5/carry:overwrote:model_items" },
        };

        // 照合 2（エンジンが写した blob）：持ち越しを宣言したエンジンで、品目の行の決定の後に、サーバーの他者が書いた blob を
        // エンジンが端末へ写す（端末の書き込みではない）。端末の blob の内容は決定を作ったときと変わるが、行の決定は外れず、
        // 後の実行が行の内容をサーバーへ書けば上書きとして名指しする
        yield return new(
            "judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation",
            [Folder(SV, 975), Item(SV, 975, folder: 975)],
            [
                new UpdateItemOp(D1, Direct, 975, 975, null, "c-975", "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                new WritePayloadOp(SV, 975, "0B0975"),
                Sync(D1),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 975, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 975, OnRun: 3),
                    new JudgeBreak(JudgeBreakKind.WriteLocalWithoutForeignKeys, D1, 975, OnRun: 3)
                    {
                        Statements =
                        [
                            "UPDATE model_items SET payload = X'0B0975' WHERE item_id = 975",
                        ],
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "5/carry:overwrote:model_items" },
        };

        // 照合 2（行の単位）：持ち越しを宣言したエンジンで、行と blob の決定を持つ品目の行を決定の後に編集すると、同じ行の
        // blob の決定も外れる。後の Collect の実行が行と一緒に blob を送っても上書きにしない
        yield return new(
            "judge-carry-blob-decision-voided-by-row-edit-is-not-overwrite",
            [Folder(SV, 978), Item(SV, 978, folder: 978)],
            [
                new UpdateItemOp(D1, Direct, 978, 978, null, "c-978", "local-edit"),
                new WritePayloadOp(D1, 978, "0A0978"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                new UpdateItemOp(D1, Direct, 978, 978, null, "c-978", "edited-after-decision"),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(true, [new JudgeBreak(JudgeBreakKind.SkipRun, D1, 978, OnRun: 2)]),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // 照合 2（行の単位の逆向き）：持ち越しを宣言したエンジンで、決定の後に blob だけを書いた行は、行の決定も外れる。
        // 後の実行が行の内容を blob と一緒に送っても、決定の内容の上書きにしない（決定を作ったときに blob の持ち分が無く、
        // blob の決定が無い形）
        yield return new(
            "judge-carry-row-decision-voided-by-blob-edit-is-not-overwrite",
            [Folder(SV, 979), Item(SV, 979, folder: 979)],
            [
                new UpdateItemOp(D1, Direct, 979, 979, null, "c-979", "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                new WritePayloadOp(D1, 979, "0A0979"),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(true, [new JudgeBreak(JudgeBreakKind.SkipRun, D1, 979, OnRun: 2)]),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // 照合 2（果たされた決定）：持ち越しを宣言したエンジンで、後の実行がその行を端末でサーバーの内容へ戻し、記録を消し、
        // DiscardedByServerWins で報告する。消失なしの期待から外れる
        yield return new(
            "judge-carry-fulfilled-is-not-lost",
            [Folder(SV, 952)],
            [
                new UpdateFolderOp(D1, Direct, 952, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 952, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 952, OnRun: 3)
                    {
                        ClearsRecords = true,
                        ReportsDiscarded = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // 根拠の確認（持ち越した決定を果たした決着）：持ち越しを宣言したエンジンで、決定の行を後の後勝ち＋ServerWins の実行が
        // 端末でサーバーの内容へ戻し、記録を消し、DiscardedByServerWins で報告する。サーバーが断った決着ではないので、根拠の
        // 確認（3/spurious）に掛けない
        yield return new(
            "judge-carry-fulfilled-by-lww-serverwins-is-not-spurious",
            [Folder(SV, 966)],
            [
                new UpdateFolderOp(D1, Direct, 966, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 966, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 966, OnRun: 3)
                    {
                        ClearsRecords = true,
                        ReportsDiscarded = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // 根拠の確認（宣言なしの対照）：持ち越しを宣言しないエンジンで、台帳が決定を持つ行を、後の後勝ち＋ServerWins の実行が
        // 端末でサーバーの内容へ戻し、記録を消し、DiscardedByServerWins で報告する。宣言の無いエンジンは決定を果たさないので、
        // この決着はサーバーが断った決着として根拠を確かめ、根拠が無いので 3/spurious になる
        yield return new(
            "judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious",
            [Folder(SV, 968)],
            [
                new UpdateFolderOp(D1, Direct, 968, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 968, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 968, OnRun: 3)
                    {
                        ClearsRecords = true,
                        ReportsDiscarded = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "3/spurious:model_folders:settled-upsert:no-basis",
            },
        };

        // 根拠の確認（対照）：決定を持たない行を、後勝ち＋ServerWins の実行が、サーバーが断っていないのに端末でサーバーの内容へ
        // 戻し、記録を消し、DiscardedByServerWins で報告する（親も一意な値の持ち主もサーバーにある更新の決着）
        yield return new(
            "judge-lww-serverwins-settlement-without-basis-is-spurious",
            [Folder(SV, 967)],
            [
                new UpdateFolderOp(D1, Direct, 967, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 967, OnRun: 2)
                    {
                        ClearsRecords = true,
                        ReportsDiscarded = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "3/spurious:model_folders:settled-upsert:no-basis",
            },
        };

        // 根拠の確認（付け替えの対照）：端末がフォルダー 982 の親を 980 から 981 へ付け替え、980 を消す（どちらも未送信）。実行は
        // 何も送らずに 980 の削除を DeleteRefusedOnServer で報告する。982 の付け替えは競合にも決着にも持ち越した決定を果たした
        // 行にも入らないので、届いたとみなして根拠を打ち消し、サーバーで 982 がまだ 980 を参照していても pending-reparent になる
        yield return new(
            "judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious",
            [Folder(SV, 980), Folder(SV, 981), Folder(SV, 982, parent: 980)],
            [
                new UpdateFolderOp(D1, Direct, 982, 981, "f982"),
                new DeleteFolderOp(D1, Direct, 980),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 980, OnRun: 2)
                    {
                        ReportsDeleteRefused = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "3/spurious:model_folders:DeleteRefusedOnServer:pending-reparent",
            },
        };

        // 根拠の確認（値の手放しの対照）：端末がフォルダー 983 の name を手放し（f983 → f983-released）、984 がその値を取る
        // （どちらも未送信・手放しを先に書く。逆の順は端末の一意制約が断る）。実行は何も送らずに 984 の UniqueValueTakenOnServer
        // を報告する。983 の手放しは競合にも決着にも入らず、手放す先の値に持ち主も親も無い（断られる根拠が無い）ので、届いたと
        // みなして割り引き、サーバーで 983 がまだ f983 を持っていても pending-release になる。割り引きを外すと 983 が根拠に
        // 数えられて違反が出なくなる
        yield return new(
            "judge-unfulfilled-release-unique-refusal-without-basis-is-spurious",
            [Folder(SV, 983), Folder(SV, 984)],
            [
                new UpdateFolderOp(D1, Direct, 983, null, "f983-released"),
                new UpdateFolderOp(D1, Direct, 984, null, "f983"),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 984, OnRun: 2)
                    {
                        ReportsConflict = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "3/spurious:model_folders:UniqueValueTakenOnServer:pending-release",
            },
        };

        // 照合 2（決定の後の編集）：持ち越しを宣言しないエンジンで、決定の後に端末が編集した行を、後の Collect の実行が報告なしに
        // サーバーの内容へ戻し、記録も消す
        yield return new(
            "judge-carry-edit-lost-is-a-violation",
            [Folder(SV, 953)],
            [
                new UpdateFolderOp(D1, Direct, 953, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                new UpdateFolderOp(D1, Direct, 953, null, "edited-after"),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 953, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 953, OnRun: 3)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/carry:edit-lost:model_folders",
                "3/lost:model_folders:value",
            },
        };

        // 照合 2（宣言なしの破棄）：持ち越しを宣言しないエンジンで、決定の行を後の Collect の実行が黙ってサーバーの内容へ戻し、
        // 記録も消す。宣言が無いので消失なしの期待から外れず、3/lost になる（5/ は数えるだけの種類に入る）
        yield return new(
            "judge-undeclared-carry-discard-is-lost",
            [Folder(SV, 954)],
            [
                new UpdateFolderOp(D1, Direct, 954, null, "local-edit"),
                Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 954, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 954, OnRun: 3)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "3/lost:model_folders:value" },
        };

        // 照合 3：Collect の実行の中で、持ち分のある行（ServerWins を一度も通っていない）を報告なしにサーバーの内容へ戻し、記録も消す
        yield return new(
            "judge-local-discard-is-a-violation",
            [Folder(SV, 955)],
            [new UpdateFolderOp(D1, Direct, 955, null, "local-edit"), Sync(D1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 955, OnRun: 2)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/local-discard:model_folders",
                "3/lost:model_folders:value",
            },
        };

        // 照合 3（例外で終わった実行が持ち込んだ行の編集）：例外で終わる実行がサーバーのフォルダーを端末へ持ち込み、端末が
        // その行を編集した後、Collect の同期の中で報告なしにサーバーの内容へ戻して記録も消す（数えるだけにするのは、持ち込んだ
        // 行の削除の持ち分だけ。編集の持ち分の消失は違反のまま）
        yield return new(
            "judge-local-discard-of-edit-on-row-brought-in-by-thrown-run-is-a-violation",
            [],
            [
                Folder(SV, 963),
                new ExpectThrowOp(D1),
                Sync(D1),
                new UpdateFolderOp(D1, Direct, 963, null, "local-edit"),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.BringInFromServer, D1, 963, OnRun: 2)
                    {
                        Throws = true,
                    },
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 963, OnRun: 3)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            // 書き手が 2 人（サーバーの他者と端末）のキーなので、消失なしの表明（3/lost）は期待を決めない
            ExpectedJudgeSignatures = new HashSet<string> { "5/local-discard:model_folders" },
        };

        // 照合 3（blob の読み出しの失敗なしで持ち込んだ行の削除）：例外で終わる実行がサーバーのフォルダーを端末へ持ち込み
        // （blob の読み出しの失敗は当たっていない）、端末がその行を消した後、次の実行が報告なしに行を戻して記録も消す（数える
        // だけにするのは、blob の読み出しの失敗が当たって持ち込まれた行だけ）
        yield return new(
            "judge-local-discard-of-delete-on-row-brought-in-without-blob-failure-is-a-violation",
            [],
            [
                Folder(SV, 964),
                new ExpectThrowOp(D1),
                Sync(D1),
                new DeleteFolderOp(D1, Direct, 964),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.BringInFromServer, D1, 964, OnRun: 2)
                    {
                        Throws = true,
                    },
                    new JudgeBreak(JudgeBreakKind.BringInFromServer, D1, 964, OnRun: 3)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            // 書き手が 2 人（サーバーの他者と端末）のキーなので、消失なしの表明（3/lost）は期待を決めない
            ExpectedJudgeSignatures = new HashSet<string> { "5/local-discard:model_folders" },
        };

        // 照合 1（blob の読み出しの失敗なしで戻した行の送り直し）：ServerWins の同期が、端末の編集したフォルダーを報告なしに
        // サーバーの内容へ戻してから例外で終わり（記録は残す。blob の読み出しの失敗もほかの書き手の変更も無い）、サーバーの
        // 他者がその行を変えた後、後勝ちの同期が残った記録で戻した古い写しを送る（書くのは製品のエンジン自身。数えるだけに
        // するのは、行を書いた後で例外になる窓に当たる行だけ）
        yield return new(
            "judge-resend-after-thrown-serverwins-restore-without-blob-failure-is-a-violation",
            [Folder(SV, 965)],
            [
                new UpdateFolderOp(D1, Direct, 965, null, "local-edit"),
                new ExpectThrowOp(D1),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
                new UpdateFolderOp(SV, Direct, 965, null, "server-new"),
                Sync(D1, mode: SyncMode.LastWriteWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 965, OnRun: 2)
                    {
                        Throws = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/local-discard:model_folders",
                "5/stale-overwrite:model_folders",
            },
        };

        // 照合 5：サーバーの他者が端末の編集した行を変えた後、Collect の同期の中で、エンジンを呼ばずに端末の内容をサーバーへ書き、
        // 記録を消す（版で守る実行が、端末が見ていないほかの書き手の内容を、競合を報告せずに上書きする）
        yield return new(
            "judge-unreported-overwrite-is-a-violation",
            [Folder(SV, 957)],
            [
                new UpdateFolderOp(D1, Direct, 957, null, "local-edit"),
                new UpdateFolderOp(SV, Direct, 957, null, "server-new"),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 957, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.WriteLocalToServer, D1, 957, OnRun: 2)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/unreported-overwrite:model_folders",
            },
        };

        // 照合 6：フォルダーを除外した Versioned の同期の中で、端末の編集したフォルダーの内容をサーバーへ書く（範囲の外の行を書く）
        yield return new(
            "judge-out-of-scope-write-is-a-violation",
            [Folder(SV, 958)],
            [
                new UpdateFolderOp(D1, Direct, 958, null, "local-edit"),
                Sync(D1, excluded: [ModelTables.Folders]),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalToServer, D1, 958, OnRun: 2)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "5/out-of-scope-write:model_folders" },
        };

        // 照合 7：Versioned＋ServerWins の同期の中で、エンジンを呼ばずに端末の編集したフォルダーの内容をサーバーへ書く
        yield return new(
            "judge-serverwins-wrote-is-a-violation",
            [Folder(SV, 959)],
            [
                new UpdateFolderOp(D1, Direct, 959, null, "local-edit"),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 959, OnRun: 2),
                    new JudgeBreak(JudgeBreakKind.WriteLocalToServer, D1, 959, OnRun: 2)
                    {
                        ClearsRecords = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "5/serverwins-wrote:model_folders" },
        };

        // 照合 8：端末が書いていないフォルダーについて、実行の結果に追加・更新の競合を足す（持ち分の無い行を送った形）
        yield return new("judge-unowned-send-is-a-violation", [Folder(SV, 960)], [Sync(D1)])
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 960, OnRun: 2)
                    {
                        ReportsConflict = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/unowned-send:model_folders",
                "3/spurious:model_folders:UniqueValueTakenOnServer:no-basis",
            },
        };

        // 照合 8（片付いた記録の後）：端末が新しいフォルダーを入れてすぐ消し（差し引き何も書いていない）、例外の無い同期が
        // その記録を片付けた後で、次の実行の結果にそのフォルダーの追加・更新の競合を足す（記録が残っているかもしれない
        // キーの控えは、片付けた実行の終わりに外れている）
        yield return new(
            "judge-unowned-send-after-settled-record-is-a-violation",
            [Folder(SV, 962)],
            [
                new AddFolderOp(D1, Direct, 1962, null, "added-then-deleted"),
                new DeleteFolderOp(D1, Direct, 1962),
                Sync(D1),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.SkipRun, D1, 1962, OnRun: 3)
                    {
                        ReportsConflict = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/unowned-send:model_folders",
                "3/spurious:model_folders:UniqueValueTakenOnServer:no-basis",
            },
        };

        // 照合 3（例外の実行の戻し）：端末が編集したフォルダーを、Collect の同期の中でサーバーの内容へ戻し、記録も消してから、
        // 結果を返さずに例外で終わる
        yield return new(
            "judge-thrown-run-restore-is-a-violation",
            [Folder(SV, 961)],
            [
                new UpdateFolderOp(D1, Direct, 961, null, "local-edit"),
                new ExpectThrowOp(D1),
                Sync(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.RestoreFromServer, D1, 961, OnRun: 2)
                    {
                        ClearsRecords = true,
                        Throws = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "5/local-discard:model_folders",
                "3/lost:model_folders:value",
            },
        };

        // 照合 4：状態の面の「サーバーが知っているか」を反転して返す。端末に行が無く削除の未送信がある形（受け取った行の削除）を作る
        yield return new(
            "judge-server-knows-facet-is-compared",
            [Folder(SV, 956)],
            [new DeleteFolderOp(D1, Direct, 956), Sync(D1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(false, []) { FlipsServerKnows = [D1] },
            ExpectedJudgeSignatures = new HashSet<string> { "5/facet:server-knows:model_folders" },
        };

        // 外部キー（前からある違反）：端末に利用者の手が作った親の無いラベルがある状態で、それに触れない同期（Versioned は版なしの
        // ラベルに触れない）を流す。同期の後も違反は残るが、前からある違反なので数えない
        yield return new(
            "judge-foreign-key-existing-violation-is-not-counted",
            [Folder(SV, 970)],
            [OrphanLabel(D1, "orphan-a", 9701), Sync(D1), ExpectOrphanLabels(D1, 1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(false, []),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // 外部キー（新しい違反）：同期の中でエンジンが外部キーの検査を切ったまま親の無いラベルを書く
        yield return new(
            "judge-foreign-key-new-violation-is-counted",
            [Folder(SV, 971)],
            [Sync(D1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalWithoutForeignKeys, D1, 971, OnRun: 2)
                    {
                        Statements = [OrphanLabelSql("orphan-b", 9711)],
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "4/foreign-key:model_labels" },
        };

        // 外部キー（例外で終わった実行の新しい違反）：同期の中でエンジンが外部キーの検査を切ったまま親の無いラベルを書き、
        // 結果を返さずに例外で終わる
        yield return new(
            "judge-foreign-key-new-violation-in-thrown-run-is-counted",
            [Folder(SV, 974)],
            [new ExpectThrowOp(D1), Sync(D1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalWithoutForeignKeys, D1, 974, OnRun: 2)
                    {
                        Statements = [OrphanLabelSql("orphan-t", 9741)],
                        Throws = true,
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "4/foreign-key:model_labels" },
        };

        // 外部キー（入れ替わった違反）：前からある親の無いラベル A を、同期の中でエンジンが消して別の親の無いラベル B を入れる
        // （違反の件数は同じ）
        yield return new(
            "judge-foreign-key-swapped-violation-is-counted",
            [Folder(SV, 972)],
            [OrphanLabel(D1, "orphan-a", 9721), Sync(D1), ExpectOrphanLabels(D1, 1)]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalWithoutForeignKeys, D1, 972, OnRun: 2)
                    {
                        Statements =
                        [
                            "DELETE FROM model_labels WHERE label_code = 'orphan-a'",
                            OrphanLabelSql("orphan-b", 9721),
                        ],
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string> { "4/foreign-key:model_labels" },
        };

        // 外部キー（入れ直した違反）：前からある親の無いラベル A を、同期の中でエンジンが消して同じキーで入れ直す（詰め物の行を
        // 挟んで rowid を変える）。同じ違反なので数えない
        yield return new(
            "judge-foreign-key-reinserted-violation-is-not-counted",
            [Folder(SV, 973)],
            [
                OrphanLabel(D1, "orphan-a", 9731),
                new ExpectLocalSqlOp(
                    D1,
                    "SELECT rowid FROM model_labels WHERE label_code = 'orphan-a'",
                    "1"
                ),
                Sync(D1),
                new ExpectLocalSqlOp(
                    D1,
                    "SELECT rowid FROM model_labels WHERE label_code = 'orphan-a'",
                    "2"
                ),
                ExpectOrphanLabels(D1, 1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                false,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalWithoutForeignKeys, D1, 973, OnRun: 2)
                    {
                        Statements =
                        [
                            "DELETE FROM model_labels WHERE label_code = 'orphan-a'",
                            "INSERT INTO model_labels (label_code, folder_id, caption) "
                                + "VALUES ('filler', 973, 'filler')",
                            OrphanLabelSql("orphan-a", 9731),
                            "DELETE FROM model_labels WHERE label_code = 'filler'",
                        ],
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>(),
        };

        // blob の期待（遅れている blob の付け替え）：サーバーが品目 1 の blob を書き直した後、降ろす同期で写しが落ち、端末の
        // blob は前の値のまま。端末が品目 1 を新しいキーへ付け替えた後の実行の前に、エンジンが付け替え先の写し待ちを忘れて
        // blob の列を全部名指しする（端末の前の blob を送る）。付け替え先の blob の期待はサーバーの元のキーの blob なので、
        // 送られた前の blob は誰も付け替え先へ書いていない値になり（照合の blob の期待）、付け替え先の blob の持ち分（送るべき
        // blob）は端末からもサーバーからも消える（照合 3）
        yield return new(
            "judge-moved-behind-blob-sent-from-device-is-invented",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                new WritePayloadOp(SV, 1, "0C0C"),
                new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                new ExpectThrowOp(D1),
                Plain(D1),
                new ExpectPayloadOp(D1, 1, "0A0B0001"),
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
            ]
        )
        {
            Transports = DirectOnly,
            JudgeDevice = new(
                true,
                [
                    new JudgeBreak(JudgeBreakKind.WriteLocalBeforeRun, D1, 1, OnRun: 3)
                    {
                        Statements =
                        [
                            "UPDATE quicker_sync_row SET blob_columns = 'Payload', blob_owed = '' "
                                + "WHERE table_name = 'model_items' AND dirty = 1 AND deleted = 0 "
                                + "AND blob_owed <> ''",
                        ],
                    },
                ]
            ),
            ExpectedJudgeSignatures = new HashSet<string>
            {
                "3/blob-invented:model_items",
                "5/local-discard:model_items:payload",
            },
        };
    }

    /// <summary>親のフォルダーが無いラベルを入れる文（外部キーの検査を切った接続で流す）</summary>
    private static string OrphanLabelSql(string code, int missingFolderId) =>
        "INSERT INTO model_labels (label_code, folder_id, caption) "
        + $"VALUES ('{code}', {missingFolderId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'orphan')";

    /// <summary>
    /// 端末が前から持っていた親の無いラベルを入れる（外部キーの検査を切り、記録しない書き込みの口を通す＝利用者が送る編集でない）
    /// </summary>
    private static LocalSqlOp OrphanLabel(ModelActor device, string code, int missingFolderId) =>
        new(device, OrphanLabelSql(code, missingFolderId)) { Untracked = true };

    /// <summary>端末の親の無いラベルの件数を確かめる</summary>
    private static ExpectLocalSqlOp ExpectOrphanLabels(ModelActor device, int count) =>
        new(
            device,
            "SELECT COUNT(*) FROM model_labels l "
                + "WHERE NOT EXISTS (SELECT 1 FROM model_folders f WHERE f.folder_id = l.folder_id)",
            count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
}
