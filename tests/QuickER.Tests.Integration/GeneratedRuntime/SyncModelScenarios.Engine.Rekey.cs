using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 端末が品目の主キーを生の SQL で付け替える（<see cref="RekeyItemOp"/>）シナリオ。付け替え先 3 通り（新しいキー・端末が消した
/// サーバーが知るキー・仮のキー経由の入れ替え）× ポリシー 3 通り（Collect・LocalWins・ServerWins）。転送はすべて。
/// </summary>
/// <remarks>
/// <para>
/// 初期データはフォルダー 1 と品目 1・2（blob は 0A0B0001・0A0B0002。品目 1 の版が古い）。端末 1 が付け替えて blob を運ぶ同期を
/// 1 回流す。どの形も書き手は端末 1 だけなので、競合の根拠は無い。
/// </para>
/// <list type="bullet">
/// <item>Collect・LocalWins：同期は競合を報告せず、未送信を残さず、サーバーの行と blob が端末の付け替えた内容になる（付け替えた行は、
/// 古いキーの行を消して新しいキーで入れ直したのと同じく、全列で届く）。入れ替えだけは、上げる 2 つの更新が一意な値を入れ替えるので
/// 両方とも UniqueValueTakenOnServer で断られ（版の競合にはしない）、端末が片方の値を空けた次の同期で届く。</item>
/// <item>ServerWins：Versioned の ServerWins は何も送らずに範囲の未送信を捨て、サーバーの行で戻す（docs の「競合」の節）。サーバーは
/// 初期データのまま、端末の行と blob はサーバーの行と blob に戻り、未送信を残さない（戻した行の blob の対照）。</item>
/// </list>
/// <para>
/// blob を運ばない同期の組は置かない：運ばない実行は blob の列を持たずに記録を決着させる（docs の「無制限バイナリ列」の割り切り。
/// 対照は <c>SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation</c>）ので、付け替えの記録が blob の列を持つかで結果が
/// 変わらず、乱数の列も常に運ぶ。後勝ちのモードも置かない：後勝ちは版を照合せずに上書きするので、付け替えた行が版の比べ方で
/// 競合にされる形を通らず、blob を送るかの形は Versioned の Collect・LocalWins と同じ記録を読む。
/// </para>
/// </remarks>
internal static partial class SyncModelScenarios
{
    /// <summary>付け替えの新しいキー・仮のキー（端末 1 の付け替えの帯）</summary>
    private const int RekeyFreshKey = 1000 + SyncModelGenerator.RekeyKeyOffset + 1;

    private static IEnumerable<SyncModelScenario> EngineRekeyScenarios()
    {
        // シナリオの一覧の静的な初期化の中で呼ばれるので、表は静的なフィールドにせずここで作る
        string[] initialItems =
        [
            "1 folder=1 label=null code=c-1 title=t1",
            "2 folder=1 label=null code=c-2 title=t2",
        ];
        // 行の名前（Collect・LocalWins の上げ、ServerWins の戻し）は、作らない形の宣言・CLAUDE.md が名指せるよう文字列のまま書く
        (string[] Names, RekeyItemOp Op, ModelOp[] Uploaded)[] shapes =
        [
            // 端末にもサーバーにも無い新しいキーへ移す：古いキーは消え、新しいキーの行と blob が届く
            (
                [
                    "rekey-fresh-key-uploads-rows-and-blobs-collect",
                    "rekey-fresh-key-uploads-rows-and-blobs-localwins",
                    "rekey-fresh-key-serverwins-restores-rows-and-blobs",
                ],
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                [
                    Expect
                        .Rows(SV, I)
                        .Contains($"{RekeyFreshKey} folder=1 label=null code=c-1 title=t1")
                        .Absent("1 *"),
                    new ExpectPayloadOp(SV, RekeyFreshKey, "0A0B0001"),
                ]
            ),
            // 品目 2 を消し、品目 1 をそのキー（サーバーが知るキー）へ移す：サーバーの品目 2 の行と blob が品目 1 の内容になる
            (
                [
                    "rekey-onto-locally-deleted-key-uploads-rows-and-blobs-collect",
                    "rekey-onto-locally-deleted-key-uploads-rows-and-blobs-localwins",
                    "rekey-onto-locally-deleted-key-serverwins-restores-rows-and-blobs",
                ],
                new RekeyItemOp(D1, 1, 2, RekeyShape.OntoDeleted),
                [
                    Expect
                        .Rows(SV, I)
                        .Contains("2 folder=1 label=null code=c-1 title=t1")
                        .Absent("1 *"),
                    new ExpectPayloadOp(SV, 2, "0A0B0001"),
                ]
            ),
            // 品目 1・2 のキーを仮のキー経由で入れ替える（code も一緒に入れ替わる）：上げる 2 つの更新は一意な値（code）を入れ替えるので、
            // どちらもサーバーで断られる（docs の「アップロードの巡」：どの書き込みも通らなくなった時点で断られたものを報告する）。
            // 端末が品目 1 の code を空けると、次の同期の巡で 2 行が届き、サーバーの 2 行の行と blob が入れ替わる
            (
                [
                    "rekey-swap-through-temporary-key-uploads-rows-and-blobs-collect",
                    "rekey-swap-through-temporary-key-uploads-rows-and-blobs-localwins",
                    "rekey-swap-through-temporary-key-serverwins-restores-rows-and-blobs",
                ],
                new RekeyItemOp(D1, 1, 2, RekeyShape.Swap, RekeyFreshKey),
                [
                    Expect
                        .Rows(SV, I)
                        .Contains(
                            "1 folder=1 label=null code=c-2x title=t2",
                            "2 folder=1 label=null code=c-1 title=t1"
                        )
                        .Absent($"{RekeyFreshKey} *"),
                    new ExpectPayloadOp(SV, 1, "0A0B0002"),
                    new ExpectPayloadOp(SV, 2, "0A0B0001"),
                ]
            ),
        ];

        foreach (var (names, op, uploaded) in shapes)
        {
            foreach (
                var (policy, name) in new[]
                {
                    (SyncConflictPolicy.Collect, names[0]),
                    (SyncConflictPolicy.LocalWins, names[1]),
                }
            )
            {
                yield return op.Shape == RekeyShape.Swap
                    ? RekeySwap(name, op, policy, initialItems, uploaded)
                    : Rekey(
                        name,
                        op,
                        policy,
                        [Expect.Result(D1).Absent("conflict *"), .. uploaded]
                    );
            }

            yield return Rekey(
                names[2],
                op,
                SyncConflictPolicy.ServerWins,
                [
                    Expect.Rows(SV, I).Contains(initialItems).Absent($"{RekeyFreshKey} *"),
                    Expect.Rows(D1, I).Contains(initialItems).Absent($"{RekeyFreshKey} *"),
                    new ExpectPayloadOp(SV, 1, "0A0B0001"),
                    new ExpectPayloadOp(SV, 2, "0A0B0002"),
                    new ExpectPayloadOp(D1, 1, "0A0B0001"),
                    new ExpectPayloadOp(D1, 2, "0A0B0002"),
                ]
            );
        }
    }

    /// <summary>
    /// 入れ替えのシナリオ 1 本：端末 1 が品目 1・2 のキーを入れ替えて同期すると、2 行とも一意な値の重なりで断られて
    /// UniqueValueTakenOnServer を報告し（版の競合 ModifiedOnServer にはしない）、サーバーは初期データのまま、端末は入れ替えた行と
    /// blob を未送信で持ち続ける。端末が品目 1 の code を空けてから同期すると、2 行が届いて渡した期待どおりになる
    /// </summary>
    private static SyncModelScenario RekeySwap(
        string name,
        RekeyItemOp rekey,
        SyncConflictPolicy policy,
        string[] initialItems,
        IReadOnlyList<ModelOp> expect
    ) =>
        Moved(
            name,
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                rekey,
                Plain(D1, policy: policy),
                Expect
                    .Result(D1)
                    .Contains(
                        "conflict model_items key=1 op=Upsert reason=UniqueValueTakenOnServer *",
                        "conflict model_items key=2 op=Upsert reason=UniqueValueTakenOnServer *"
                    )
                    .Absent("* reason=ModifiedOnServer *"),
                Expect.Pending(D1).Exact($"{I} key=1 row=present", $"{I} key=2 row=present"),
                Expect.Rows(SV, I).Contains(initialItems),
                new ExpectPayloadOp(SV, 1, "0A0B0001"),
                new ExpectPayloadOp(SV, 2, "0A0B0002"),
                new ExpectPayloadOp(D1, 1, "0A0B0002"),
                new ExpectPayloadOp(D1, 2, "0A0B0001"),
                new UpdateItemOp(D1, Direct, 1, 1, null, "c-2x", "t2"),
                Plain(D1, policy: policy),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                .. expect,
            ]
        );

    /// <summary>
    /// 付け替えのシナリオ 1 本：初期データのフォルダー 1・品目 1・2 の上で、端末 1 が付け替えて blob を運ぶ同期を 1 回流し、未送信が
    /// 残らないことと、渡した期待を表明する
    /// </summary>
    private static SyncModelScenario Rekey(
        string name,
        RekeyItemOp rekey,
        SyncConflictPolicy policy,
        IReadOnlyList<ModelOp> expect
    ) =>
        Moved(
            name,
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [rekey, Plain(D1, policy: policy), Expect.Pending(D1).Empty(), .. expect]
        );
}
