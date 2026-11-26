using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 適用の試行の上限（エンジンの MaxApplyAttempts＝200）を超える深さの親子の鎖を降ろすシナリオ。転送はすべて。
/// </summary>
/// <remarks>
/// <para>
/// 適用は外部キーの違反を見つけると試行を巻き戻し、違反した子を保留する（消した親は残す）。1 つの試行が保留するのは違反した行だけ
/// なので、鎖の根が書けない（同じ適用に無い・一意な値で保留される・消せない子が残る）と、鎖の深さ d に d＋1 回の試行が要る。
/// どの行も「止まらない・収束」の表明に加えて、鎖が降りる・消えることを期待の手で表明する。
/// </para>
/// <para>
/// 根を鎖より後に書き直す形（<c>deep-chain-root-rewritten-after-chain-downloads</c>）は、1 回で全部の道では根が同じ適用に入り、
/// 小さい塊の端末（塊 2 行）では塊ごとの試行が 2 回で済むので、どちらの端末でも鎖の深さが試行の数にならない。この形の塊の道は、
/// 塊の大きさを鎖より大きくした同期のエンジンの決まった形の行（<c>chunk-deep-selfref-chain-root-in-later-chunk-settles</c>）が持つ。
/// 鎖の行の数は上限を超える最小の近く（フォルダー 210 段・表をまたぐ鎖は 105 段＝210 行）にする（実 SQL Server の転送でも流す）。
/// </para>
/// </remarks>
internal static partial class SyncModelScenarios
{
    /// <summary>鎖の根のフォルダー</summary>
    private const int ChainRoot = 3000;

    /// <summary>フォルダーの鎖の段の数（試行の上限 200 を超える）</summary>
    private const int ChainDepth = 210;

    /// <summary>表をまたぐ鎖の段の数（品目とフォルダーの組。行は 210）</summary>
    private const int CrossTableChainDepth = 105;

    /// <summary>一意な値を手放すフォルダー（初期データ。端末の未送信の編集が手放した値を持ち続ける）</summary>
    private const int ChainHolder = 10;

    /// <summary>鎖の葉（フォルダーの鎖の最後のフォルダー）</summary>
    private const int ChainLeaf = ChainRoot + ChainDepth;

    private static IEnumerable<SyncModelScenario> EngineDeepChainScenarios() =>
        [
            // 根を鎖より後に書き直す（版の順で根が鎖より後に来る）。1 回の同期で鎖と根が全部降りる
            Moved(
                "deep-chain-root-rewritten-after-chain-downloads",
                [],
                [
                    Folder(SV, ChainRoot, name: "root"),
                    new ServerChainOp(
                        ServerChainKind.Folders,
                        ChainRoot,
                        ChainRoot + 1,
                        ChainDepth
                    ),
                    Rename(SV, ChainRoot, "root-renamed"),
                    Plain(D1),
                    Expect.Result(D1).Absent("exception *", "conflict *"),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            $"{ChainRoot} parent=null name=root-renamed",
                            $"{ChainLeaf} parent={ChainLeaf - 1} name={ServerChainOp.NameOf(ChainLeaf)}"
                        ),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // 根が一意な値で保留される：サーバーでフォルダー 10 が名前 root を手放し、根がその名前を取る。端末はフォルダー 10 に
            // 未送信の編集（親の付け替え）を持つので、同期はフォルダー 10 の ModifiedOnServer を報告して端末の内容（名前 root）を
            // 残し、根はその実行では書けない。同期は止まらずに終わり、続く ServerWins の同期でフォルダー 10 が戻ると根と鎖が降りる
            Moved(
                "deep-chain-root-held-by-unique-value-downloads-after-holder-is-settled",
                [Folder(SV, ChainHolder, name: "root")],
                [
                    Rename(SV, ChainHolder, "moved"),
                    Folder(SV, ChainRoot, name: "root"),
                    new ServerChainOp(
                        ServerChainKind.Folders,
                        ChainRoot,
                        ChainRoot + 1,
                        ChainDepth
                    ),
                    new UpdateFolderOp(D1, Direct, ChainHolder, 999, "root"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains($"conflict {F} key={ChainHolder} *")
                        .Absent("exception *"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("exception *"),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            $"{ChainHolder} parent=null name=moved",
                            $"{ChainRoot} parent=null name=root",
                            $"{ChainLeaf} parent={ChainLeaf - 1} name={ServerChainOp.NameOf(ChainLeaf)}"
                        ),
                ]
            ),
            // サーバーが鎖を消す（端末に鎖が降りた後）：端末は葉の下に未送信の新しいフォルダーを持つので、削除伝搬はその親の葉を
            // 残し、葉が残るとその親も残す…と鎖を根まで残す。同期は止まらずに終わり（新しいフォルダーは ParentMissingOnServer）、
            // 続く ServerWins の同期が新しいフォルダーを捨てると鎖が消える
            Moved(
                "deep-chain-deleted-on-server-under-unsent-child-propagates-after-child-is-settled",
                [],
                [
                    Folder(SV, ChainRoot, name: "root"),
                    new ServerChainOp(
                        ServerChainKind.Folders,
                        ChainRoot,
                        ChainRoot + 1,
                        ChainDepth
                    ),
                    Plain(D1),
                    Expect.Rows(D1, F).Contains($"{ChainLeaf} *"),
                    Folder(D1, 1500, parent: ChainLeaf, name: "kid"),
                    new ServerChainOp(
                        ServerChainKind.DeleteFolders,
                        ChainRoot,
                        ChainRoot + 1,
                        ChainDepth
                    ),
                    Plain(D1),
                    Expect.Result(D1).Contains($"conflict {F} key=1500 *").Absent("exception *"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("exception *"),
                    Expect.Rows(D1, F).Absent($"{ChainRoot} *", $"{ChainLeaf} *", "1500 *"),
                ]
            ),
            // 表をまたぐ深い鎖（フォルダー → ピン留めの品目 → その品目の親のフォルダー → …）の根が一意な値で保留される形。
            // 品目とフォルダーが 1 行ずつ交互に保留されるので、105 段（210 行）で試行の上限を超える
            Moved(
                "deep-cross-table-chain-root-held-by-unique-value-downloads-after-holder-is-settled",
                [Folder(SV, ChainHolder, name: "root")],
                [
                    Rename(SV, ChainHolder, "moved"),
                    Folder(SV, ChainRoot, name: "root"),
                    new ServerChainOp(
                        ServerChainKind.FoldersAndItems,
                        ChainRoot,
                        ChainRoot + 1,
                        CrossTableChainDepth
                    ),
                    new UpdateFolderOp(D1, Direct, ChainHolder, 999, "root"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains($"conflict {F} key={ChainHolder} *")
                        .Absent("exception *"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("exception *"),
                    Expect
                        .Rows(D1, I)
                        .Contains(
                            $"{ChainRoot + CrossTableChainDepth} folder={ChainRoot + CrossTableChainDepth - 1} *"
                        ),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            $"{ChainRoot} parent=null name=root",
                            $"{ChainRoot + CrossTableChainDepth} parent=null name={ServerChainOp.NameOf(ChainRoot + CrossTableChainDepth)} pinned={ChainRoot + CrossTableChainDepth}"
                        ),
                ]
            ),
        ];
}
