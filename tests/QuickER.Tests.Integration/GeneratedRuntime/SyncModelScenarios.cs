using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// モデルの言葉で書いた決定的なシナリオ（初期データ・操作の列・必要なら最終値の期待）。
/// </summary>
/// <param name="Name">シナリオの名前（既知の失敗の一覧が名指しする）</param>
/// <param name="Setup">初期データ（サーバーの他者の書き込み。端末 2 台はこれへそろえてから始まる）</param>
/// <param name="Ops">操作の列</param>
/// <param name="Expect">
/// 静止化の後の最終値の期待（書き手が 2 人以上のキーのように、共通の表明では決められないもの）。
/// 違反は署名 <c>5/expect:{Name}</c> で報告する。
/// </param>
internal sealed record SyncModelScenario(
    string Name,
    IReadOnlyList<ModelWriteOp> Setup,
    IReadOnlyList<ModelOp> Ops,
    Func<ModelDbState, string?>? Expect = null
)
{
    /// <summary>表を外部の ISyncTable 実装の代役で包む端末</summary>
    public IReadOnlyList<ModelActor> ExternalTables { get; init; } = [];

    /// <summary>サーバー側の失敗（ServerWrite・ServerFetch・BlobDownload・BlobUpload）を仕掛けるか（実 SQL Server では流さない）</summary>
    public bool ArmsServerFaults =>
        Ops.OfType<ArmFaultOp>()
            .Any(a =>
                a.Kind
                    is ModelFaultKind.ServerWrite
                        or ModelFaultKind.ServerFetch
                        or ModelFaultKind.BlobDownload
                        or ModelFaultKind.BlobUpload
            );

    /// <summary>シナリオを流し、見つかった違反を返す</summary>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="options">世界の組み方（既定は直結・SQLite のサーバー役。外部の ISyncTable の代役はシナリオが決める）</param>
    public async Task<(IReadOnlyList<ModelFailure> Failures, IReadOnlyList<string> Log)> RunAsync(
        CancellationToken ct,
        SyncModelWorldOptions? options = null
    )
    {
        await using var run = await SyncModelRun.StartAsync(
            [.. Setup, .. SyncModelScenarios.Tail],
            ct,
            (options ?? SyncModelWorldOptions.Direct) with
            {
                ExternalTables = ExternalTables,
            }
        );

        foreach (var op in Ops)
        {
            await run.ExecuteAsync(op);
        }

        await run.FinishAsync();
        var failures = run.Failures.ToList();

        if (
            Expect is not null
            && !failures.Any(f => f.Signature.StartsWith("1/", StringComparison.Ordinal))
        )
        {
            var problem = Expect(await run.World.ReadStateAsync(ModelActor.Server, ct));

            if (problem is not null)
            {
                failures.Add(new($"5/expect:{Name}", problem));
            }
        }

        return (failures, run.Log);
    }
}

/// <summary>決定的なシナリオの一覧（指摘ごとに 1 本＋書き手が 2 人のキーのポリシーごとの期待）</summary>
internal static class SyncModelScenarios
{
    private const ModelActor D1 = ModelActor.Device1;
    private const ModelActor D2 = ModelActor.Device2;
    private const ModelActor SV = ModelActor.Server;
    private const ModelWriteVia Direct = ModelWriteVia.Direct;

    /// <summary>
    /// どのシナリオの初期データにも最後に足す行（版ありの表ごとに 1 行）。ローカルのアンカー（版列の MAX）を初期データの末尾に置き、
    /// ローカルで消した行が「アンカーより下に潜った行」になる形を作る（無いと、ローカルの行を全部消したときにアンカーが空へ
    /// 戻って全件を取り直し、順序の不具合を偶然すり抜ける）。
    /// </summary>
    public static IReadOnlyList<ModelWriteOp> Tail { get; } =
    [
        Folder(SV, 999, name: "tail"),
        Item(SV, 999, folder: 999, code: "tail"),
        new AddEventOp(SV, Direct, new DateTime(2026, 12, 31, 23, 0, 0).AddTicks(1), 999, "tail"),
    ];

    /// <summary>すべてのシナリオ</summary>
    public static IReadOnlyList<SyncModelScenario> All { get; } = Build();

    /// <summary>名前で引く</summary>
    public static SyncModelScenario Get(string name) => All.Single(s => s.Name == name);

    private static ModelSyncOp Sync(
        ModelActor device,
        SyncMode mode = SyncMode.Versioned,
        SyncConflictPolicy policy = SyncConflictPolicy.Collect,
        IReadOnlyList<string>? excluded = null,
        int batch = 500,
        bool propagateDeletes = true
    ) =>
        new(
            device,
            ModelSyncKind.Sync,
            mode,
            policy,
            excluded ?? [],
            true,
            batch,
            propagateDeletes
        );

    private static AddFolderOp Folder(
        ModelActor actor,
        int id,
        int? parent = null,
        string? name = null
    ) => new(actor, Direct, id, parent, name ?? $"f{id}");

    private static AddItemOp Item(
        ModelActor actor,
        int id,
        int folder,
        string? label = null,
        string? code = null
    ) => new(actor, Direct, id, folder, label, code ?? $"c-{id}", $"t{id}", $"0A0B{id:X4}");

    private static List<SyncModelScenario> Build()
    {
        var list = new List<SyncModelScenario>
        {
            // #12: 同じ表の中の削除伝搬が、親（小さいキー）を子より先に消す
            new(
                "12-selfref-propagation",
                [Folder(SV, 10), Folder(SV, 11, parent: 10)],
                [new DeleteFolderOp(SV, Direct, 11), new DeleteFolderOp(SV, Direct, 10), Sync(D1)]
            ),
            // #12: 同じ表の 3 段の親子がサーバーで消えたとき、削除伝搬は 1 回のランで全部を消す（参照されている親は、
            // 子を消した後の繰り返しで消す。静止化の次のランで消えると収束の表明では見えないので、ランの直後に見る）
            new(
                "12-selfref-propagation-completes-in-one-run",
                [Folder(SV, 12), Folder(SV, 13, parent: 12), Folder(SV, 14, parent: 13)],
                [
                    new DeleteFolderOp(SV, Direct, 14),
                    new DeleteFolderOp(SV, Direct, 13),
                    new DeleteFolderOp(SV, Direct, 12),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "12", null),
                    new ExpectLocalOp(D1, ModelTables.Folders, "13", null),
                    new ExpectLocalOp(D1, ModelTables.Folders, "14", null),
                ]
            ),
            // #13: サーバーにもある未送信の行（付け替えた品目）の新しい親を、削除伝搬が消す
            new(
                "13-propagation-pending-row-parent",
                [Folder(SV, 20), Folder(SV, 21), Item(SV, 30, folder: 20)],
                [
                    new UpdateItemOp(D1, Direct, 30, 21, null, "c-30", "moved"),
                    new DeleteFolderOp(SV, Direct, 21),
                    Sync(D1),
                ]
            ),
            // #13: 同じ形で、未送信の行が版なしの表（後勝ちのラン）
            new(
                "13-propagation-pending-label-parent",
                [Folder(SV, 22), Folder(SV, 23), new AddLabelOp(SV, Direct, "L22", 22, "cap")],
                [
                    new UpdateLabelOp(D1, Direct, "L22", 23, "moved"),
                    new DeleteFolderOp(SV, Direct, 23),
                    Sync(D1, mode: SyncMode.LastWriteWins),
                ]
            ),
            // #13: 打ち切りで古いまま残った子（降りてこなかった付け替え）が参照する親を、削除伝搬が消す
            new(
                "13-propagation-truncated-child",
                [
                    Folder(SV, 24),
                    Item(SV, 25, folder: 24),
                    Item(SV, 26, folder: 24),
                    Item(SV, 27, folder: 24),
                    new AddEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 3, 1, 9, 0, 0).AddTicks(1),
                        25,
                        "e"
                    ),
                ],
                [
                    new UpdateItemOp(D1, Direct, 27, 24, null, "c-27", "d1"),
                    new UpdateItemOp(SV, Direct, 27, 24, null, "c-27", "sv"),
                    new UpdateEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 3, 1, 9, 0, 0).AddTicks(1),
                        26,
                        "moved"
                    ),
                    new DeleteItemOp(SV, Direct, 25),
                    Sync(D1),
                ]
            ),
            // #15: Versioned の範囲外（版なしの表）のローカルの子を見ずに、削除伝搬が親を消す
            new(
                "15-propagation-versionless-child",
                [Folder(SV, 40), new AddLabelOp(SV, Direct, "L40", 40, "cap")],
                [new DeleteLabelOp(SV, Direct, "L40"), new DeleteFolderOp(SV, Direct, 40), Sync(D1)]
            ),
            // #15: ExcludedEntityTypes で外した表のローカルの子を見ずに、削除伝搬が親を消す
            new(
                "15-propagation-excluded-child",
                [Folder(SV, 41), Item(SV, 42, folder: 41)],
                [
                    new DeleteItemOp(SV, Direct, 42),
                    new DeleteFolderOp(SV, Direct, 41),
                    Sync(D1, excluded: [ModelTables.Items]),
                ]
            ),
            // #17: ローカルで消した親を指す新しい子が、ServerWins の解決で親より先に適用される
            new(
                "17-serverwins-deleted-parent",
                [Folder(SV, 50)],
                [
                    new DeleteFolderOp(D1, Direct, 50),
                    Folder(SV, 51, parent: 50),
                    Sync(D1),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                    // ServerWins が捨てる削除の親は、読み直して先に戻し、子も同じランで降りる
                    new ExpectLocalOp(D1, ModelTables.Folders, "50", "parent=null name=f50"),
                    new ExpectLocalOp(D1, ModelTables.Folders, "51", "parent=50 name=f51"),
                ]
            ),
            // N6: 既存の行を、同じランで新しく作った同じ表の行の下へ付け替える（271 の最初の記録が 270 より古い）。
            // 表の中の再生は最初の記録の順なので子が先に送られて断られるが、同じ表の中で進展がある限りやり直す
            new(
                "n6-upload-reparent-to-new-row",
                [Folder(SV, 271)],
                [
                    new UpdateFolderOp(D1, Direct, 271, null, "renamed"),
                    Folder(D1, 270),
                    new UpdateFolderOp(D1, Direct, 271, 270, "moved"),
                    Sync(D1),
                    Sync(D2),
                    new ExpectLocalOp(D2, ModelTables.Folders, "271", "parent=270 name=moved"),
                ]
            ),
            // N6: 同じ形を後勝ち＋ServerWins で。偽の ParentMissingOnServer を決着させて付け替えを捨てない
            new(
                "n6-reparent-lastwritewins-serverwins",
                [Folder(SV, 273)],
                [
                    new UpdateFolderOp(D1, Direct, 273, null, "renamed273"),
                    Folder(D1, 272),
                    new UpdateFolderOp(D1, Direct, 273, 272, "moved273"),
                    Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                    Sync(D2),
                    new ExpectLocalOp(D2, ModelTables.Folders, "273", "parent=272 name=moved273"),
                ]
            ),
            // N6: 同じ表の親子を両方ローカルで消し、親の記録が先（親を先に改名した）。親の削除が先に送られて断られる
            // が、子の削除の後でやり直して通る
            new(
                "n6-delete-parent-recorded-first",
                [Folder(SV, 274), Folder(SV, 275, parent: 274)],
                [
                    new UpdateFolderOp(D1, Direct, 274, null, "renamed274"),
                    new DeleteFolderOp(D1, Direct, 275),
                    new DeleteFolderOp(D1, Direct, 274),
                    Sync(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                    new ExpectLocalOp(SV, ModelTables.Folders, "274", null),
                    new ExpectLocalOp(SV, ModelTables.Folders, "275", null),
                ]
            ),
            // N6: 外部キーで断られた書き込みはサーバーに何も残さない（blob の書き込みへ進まず、受理記録も付けない）。
            // 品目 277 の blob を書き換えてから、サーバーで消えたフォルダー 278 の下へ移す
            new(
                "n6-refused-write-leaves-nothing",
                [Folder(SV, 276), Folder(SV, 278), Item(SV, 277, folder: 276)],
                [
                    new WritePayloadOp(D1, 277, "0C0C0C0C"),
                    new UpdateItemOp(D1, Direct, 277, 278, null, "c-277", "moved"),
                    new DeleteFolderOp(SV, Direct, 278),
                    Sync(D1),
                    new ExpectPayloadOp(SV, 277, "0A0B0115"),
                    new ExpectLocalOp(
                        SV,
                        ModelTables.Items,
                        "277",
                        "folder=276 label=null code=c-277 title=t277"
                    ),
                    new ExpectNoAckOp(D1, ModelTables.Items, "277"),
                ]
            ),
            // N7（種 7 の形）: LocalWins がサーバーで消えた行を作り直そうとしたが、その間にサーバーの別の行が同じ code を
            // 取っていた。例外にせず UniqueValueTakenOnServer で報告し、他者の値を奪わない（記録は残る）
            new(
                "n7-localwins-resurrect-value-taken",
                [Folder(SV, 330), Item(SV, 331, folder: 330, code: "x1")],
                [
                    new UpdateItemOp(D1, Direct, 331, 330, null, "x1", "d1"),
                    new DeleteItemOp(SV, Direct, 331),
                    Item(SV, 332, folder: 330, code: "x1"),
                    Sync(D1, policy: SyncConflictPolicy.LocalWins),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "331",
                        "folder=330 label=null code=x1 title=d1"
                    ),
                    new ExpectLocalOp(SV, ModelTables.Items, "331", null),
                ]
            ),
            // N7（種 45 の形）: 削除して同じ code で作り直したが、削除が ModifiedOnServer の競合で届かない。後へ回した
            // 挿入はそれでもぶつかるので UniqueValueTakenOnServer で報告する
            new(
                "n7-delete-modified-then-insert-same-value",
                [Folder(SV, 333), Item(SV, 334, folder: 333, code: "x2")],
                [
                    new DeleteItemOp(D1, Direct, 334),
                    Item(D1, 335, folder: 333, code: "x2"),
                    new UpdateItemOp(SV, Direct, 334, 333, null, "x2", "sv"),
                    Sync(D1),
                    new ExpectLocalOp(SV, ModelTables.Items, "335", null),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "335",
                        "folder=333 label=null code=x2 title=t335"
                    ),
                ]
            ),
            // N7（種 34 の形）: 削除して同じ code で作り直したが、削除が DeleteRefusedOnServer（サーバーで足されたイベントが
            // 参照する）で届かない。LocalWins でも他者の値は奪わず、後へ回した挿入を UniqueValueTakenOnServer で報告する
            new(
                "n7-delete-refused-then-insert-same-value",
                [Folder(SV, 336), Item(SV, 337, folder: 336, code: "x3")],
                [
                    new DeleteItemOp(D1, Direct, 337),
                    Item(D1, 338, folder: 336, code: "x3"),
                    new AddEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 5, 1, 9, 0, 0).AddTicks(1),
                        337,
                        "e"
                    ),
                    Sync(D1, policy: SyncConflictPolicy.LocalWins),
                    new ExpectLocalOp(SV, ModelTables.Items, "338", null),
                    new ExpectLocalOp(
                        SV,
                        ModelTables.Items,
                        "337",
                        "folder=336 label=null code=x3 title=t337"
                    ),
                ]
            ),
            // N1×N2（種 214 の形）: ServerWins で捨てる品目 411 を未送信のイベントが参照して残し、サーバーの同じ code の
            // 品目 413 がぶつかって品目の表で打ち切る。後ろの表（イベント）の ServerWins も同じランで適用しないと、イベントの
            // 記録が残って 411 を参照し続け、毎回打ち切る。イベントの ServerWins を適用し、後の削除伝搬が 411 を消す
            new(
                "n1-serverwins-truncation-does-not-strand-children",
                [
                    Folder(SV, 410),
                    Item(SV, 411, folder: 410, code: "k1"),
                    Item(SV, 412, folder: 410),
                    new AddEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 6, 3, 8, 0, 0).AddTicks(7),
                        412,
                        "e"
                    ),
                ],
                [
                    new UpdateItemOp(D1, Direct, 411, 410, null, "k1", "d1"),
                    new UpdateEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 6, 3, 8, 0, 0).AddTicks(7),
                        411,
                        "moved"
                    ),
                    new DeleteItemOp(SV, Direct, 411),
                    Item(SV, 413, folder: 410, code: "k1"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Events,
                        "2026-06-03 08:00:00.0000007",
                        "item=412 memo=e"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Items, "411", null),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "413",
                        "folder=410 label=null code=k1 title=t413"
                    ),
                ]
            ),
            // 新規（案 N8・種 1733 の形）: サーバーで品目 421 が code m1 を手放し、品目 422 がそれを取り、その後 421 がもう
            // 一度更新された。流れ（版の昇順）では 422 が 421 より先に来て、端末の古い 421（m1）とぶつかり、毎回打ち切る
            new(
                "n8-unique-value-moved-then-updated",
                [Folder(SV, 420), Item(SV, 421, folder: 420, code: "m1")],
                [
                    new UpdateItemOp(SV, Direct, 421, 420, null, "m2", "t421"),
                    Item(SV, 422, folder: 420, code: "m1"),
                    new UpdateItemOp(SV, Direct, 421, 420, null, "m2", "later"),
                    Sync(D1),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "422",
                        "folder=420 label=null code=m1 title=t422"
                    ),
                ]
            ),
            // N8: 引き当てた行に未送信の変更があれば、読み直して上書きせずに打ち切る
            new(
                "n8-holder-with-unsent-change-stops",
                [Folder(SV, 440), Item(SV, 441, folder: 440, code: "q1")],
                [
                    new UpdateItemOp(D1, Direct, 441, 440, null, "q1", "d1"),
                    new UpdateItemOp(SV, Direct, 441, 440, null, "q2", "t441"),
                    Item(SV, 442, folder: 440, code: "q1"),
                    new UpdateItemOp(SV, Direct, 441, 440, null, "q2", "later"),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "441",
                        "folder=440 label=null code=q1 title=d1"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Items, "442", null),
                ]
            ),
            // N8: 引き当てた行の未送信の記録をそのランの ServerWins が捨てるなら、読み直してよい
            new(
                "n8-serverwins-holder-is-read-back",
                [Folder(SV, 443), Item(SV, 444, folder: 443, code: "q3")],
                [
                    new UpdateItemOp(D1, Direct, 444, 443, null, "q3", "d1"),
                    new UpdateItemOp(SV, Direct, 444, 443, null, "q4", "t444"),
                    Item(SV, 445, folder: 443, code: "q3"),
                    new UpdateItemOp(SV, Direct, 444, 443, null, "q4", "later"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "444",
                        "folder=443 label=null code=q4 title=later"
                    ),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "445",
                        "folder=443 label=null code=q3 title=t445"
                    ),
                ]
            ),
            // N8: サーバーで値を入れ替えた 2 行（本当の入れ替え）は打ち切る（読み直しても解けない＝既知の割り切り）。
            // docs が案内する抜け道（洗い替え）で両端末とも解消する
            new(
                "n8-true-swap-stops",
                [
                    Folder(SV, 446),
                    Item(SV, 447, folder: 446, code: "s1"),
                    Item(SV, 448, folder: 446, code: "s2"),
                ],
                [
                    new UpdateItemOp(SV, Direct, 447, 446, null, "tmp", "t447"),
                    new UpdateItemOp(SV, Direct, 448, 446, null, "s1", "t448"),
                    new UpdateItemOp(SV, Direct, 447, 446, null, "s2", "t447"),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "448",
                        "folder=446 label=null code=s2 title=t448"
                    ),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, true),
                    new ModelRefreshOp(D2, SyncMode.LastWriteWins, true),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "448",
                        "folder=446 label=null code=s1 title=t448"
                    ),
                ]
            ),
            // N8: 連鎖。421→422 の付け替えに続き、423 が 422 の手放した値を取る（読み直した行が別の行とさらにぶつかる）
            new(
                "n8-chain",
                [
                    Folder(SV, 450),
                    Item(SV, 451, folder: 450, code: "c1"),
                    Item(SV, 452, folder: 450, code: "c2"),
                ],
                [
                    new UpdateItemOp(SV, Direct, 452, 450, null, "c3", "t452"),
                    new UpdateItemOp(SV, Direct, 451, 450, null, "c2", "t451"),
                    Item(SV, 453, folder: 450, code: "c1"),
                    new UpdateItemOp(SV, Direct, 452, 450, null, "c3", "later452"),
                    new UpdateItemOp(SV, Direct, 451, 450, null, "c2", "later451"),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "453",
                        "folder=450 label=null code=c1 title=t453"
                    ),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "451",
                        "folder=450 label=null code=c2 title=later451"
                    ),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "452",
                        "folder=450 label=null code=c3 title=later452"
                    ),
                ]
            ),
            // N9: サーバーでイベントを別の品目へ付け替え、元の品目を消して、同じ code で新しい品目を作る。端末の古い
            // イベントが元の品目を参照して残すので、新しい品目がぶつかって品目の表で打ち切る。打ち切りを後ろの表へ
            // 連動させないので、イベントの付け替えが降り、削除伝搬が元の品目を消して、次のランで新しい品目が入る
            // （連動させると古いイベントが直らず、毎回同じところで止まる）
            new(
                "n9-stale-child-keeps-holder",
                [
                    Folder(SV, 455),
                    Item(SV, 456, folder: 455, code: "h1"),
                    Item(SV, 457, folder: 455),
                    new AddEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 8, 1, 8, 0, 0).AddTicks(3),
                        456,
                        "e"
                    ),
                ],
                [
                    new UpdateEventOp(
                        SV,
                        Direct,
                        new DateTime(2026, 8, 1, 8, 0, 0).AddTicks(3),
                        457,
                        "moved"
                    ),
                    new DeleteItemOp(SV, Direct, 456),
                    Item(SV, 458, folder: 455, code: "h1"),
                    Sync(D1),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "458",
                        "folder=455 label=null code=h1 title=t458"
                    ),
                ]
            ),
            // N9: フォルダの表が未送信の編集（競合）で打ち切っても、品目の表は続けて降ろす。打ち切り点より後ろにいた
            // 新しいフォルダ 472 を参照する品目 473 は、フォルダをサーバーから先に読み直してから入る
            new(
                "truncated-parent-read-back-across-tables",
                [Folder(SV, 470), Folder(SV, 471)],
                [
                    new UpdateFolderOp(D1, Direct, 470, null, "d1-470"),
                    new UpdateFolderOp(SV, Direct, 470, null, "sv-470"),
                    Folder(SV, 472),
                    Item(SV, 473, folder: 472),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "470", "parent=null name=d1-470"),
                    new ExpectLocalOp(D1, ModelTables.Folders, "472", "parent=null name=f472"),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "473",
                        "folder=472 label=null code=c-473 title=t473"
                    ),
                ]
            ),
            // N9: 同じ形で、読み直す親が自己参照の連鎖（品目 477 → フォルダ 476 → 親フォルダ 475。どちらも打ち切ったフォルダ
            // の表の打ち切り点より後ろ）。親の親も先にそろえる
            new(
                "truncated-parent-read-back-selfref-chain",
                [Folder(SV, 474)],
                [
                    new UpdateFolderOp(D1, Direct, 474, null, "d1-474"),
                    new UpdateFolderOp(SV, Direct, 474, null, "sv-474"),
                    Folder(SV, 475),
                    Folder(SV, 476, parent: 475),
                    Item(SV, 477, folder: 476),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "475", "parent=null name=f475"),
                    new ExpectLocalOp(D1, ModelTables.Folders, "476", "parent=475 name=f476"),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "477",
                        "folder=476 label=null code=c-477 title=t477"
                    ),
                ]
            ),
            // N9: フォルダの表が打ち切っても品目の表は続けるが、そろえられない親（この端末で消し、サーバーに断られて未送信の
            // まま残る削除）を参照する品目 480 は入れず、品目の表がそこで止まる（外部キー違反にしない）
            new(
                "truncated-parent-held-stops-child-table",
                [Folder(SV, 478), Folder(SV, 479)],
                [
                    new UpdateFolderOp(D1, Direct, 478, null, "d1-478"),
                    new UpdateFolderOp(SV, Direct, 478, null, "sv-478"),
                    new DeleteFolderOp(D1, Direct, 479),
                    Item(SV, 480, folder: 479),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "479", null),
                    new ExpectLocalOp(D1, ModelTables.Items, "480", null),
                ]
            ),
            // #24: 一意な値の「削除→同じ値で挿入」で後ろへ回した行の子孫（同じ表の子 292・別の表の子 293）も一緒に
            // 後ろへ回す。先に送ると、まだサーバーに無い親を参照して断られる
            new(
                "24-deferred-upsert-carries-children",
                [Folder(SV, 290, name: "uniq")],
                [
                    new DeleteFolderOp(D1, Direct, 290),
                    Folder(D1, 291, name: "uniq"),
                    Folder(D1, 292, parent: 291),
                    Item(D1, 293, folder: 291),
                    Sync(D1),
                    Sync(D2),
                    new ExpectLocalOp(D2, ModelTables.Folders, "291", "parent=null name=uniq"),
                    new ExpectLocalOp(D2, ModelTables.Folders, "292", "parent=291 name=f292"),
                    new ExpectLocalOp(
                        D2,
                        ModelTables.Items,
                        "293",
                        "folder=291 label=null code=c-293 title=t293"
                    ),
                ]
            ),
            // 親の読み直し: Collect では、親の未送信の削除は捨てられないので、親を戻さずにその子で打ち切る
            new(
                "parent-readback-collect-keeps-delete",
                [Folder(SV, 52)],
                [
                    new DeleteFolderOp(D1, Direct, 52),
                    Folder(SV, 53, parent: 52),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "52", null),
                    new ExpectLocalOp(D1, ModelTables.Folders, "53", null),
                ]
            ),
            // 親の読み直し: 連鎖して読み直す途中の祖父母（265）がローカルで未送信の削除なら戻さず、孫（267）で打ち切る
            // （直接の親の削除は保留の判定が止めるが、祖父母は読み直しの側でしか見えない）
            new(
                "parent-readback-collect-keeps-grandparent-delete",
                [Folder(SV, 265)],
                [
                    new DeleteFolderOp(D1, Direct, 265),
                    Folder(SV, 266, parent: 265),
                    Folder(SV, 267, parent: 266),
                    new UpdateFolderOp(SV, Direct, 266, 265, "renamed266"),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "265", null),
                    new ExpectLocalOp(D1, ModelTables.Folders, "266", null),
                    new ExpectLocalOp(D1, ModelTables.Folders, "267", null),
                ]
            ),
            // 親の読み直し: 同じ表の 3 段で、子が親・祖父母より先に降りてきても、連鎖して読み直して 1 回で降りる
            new(
                "parent-readback-selfref-chain",
                [Folder(SV, 255)],
                [
                    Folder(SV, 256),
                    Folder(SV, 257, parent: 256),
                    Folder(SV, 258, parent: 257),
                    new UpdateFolderOp(SV, Direct, 257, 256, "renamed257"),
                    new UpdateFolderOp(SV, Direct, 256, null, "renamed256"),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Folders,
                        "256",
                        "parent=null name=renamed256"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Folders, "257", "parent=256 name=renamed257"),
                    new ExpectLocalOp(D1, ModelTables.Folders, "258", "parent=257 name=f258"),
                ]
            ),
            // 親の読み直し: 洗い替えで、親が後ろのバッチにあっても先に読み直し、そのバッチでは挿入し直さない
            new(
                "parent-readback-refresh-across-batches",
                [Folder(SV, 259)],
                [
                    Folder(SV, 260),
                    Folder(SV, 261, parent: 260),
                    new UpdateFolderOp(SV, Direct, 260, null, "renamed260"),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, true, BatchSize: 1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Folders,
                        "260",
                        "parent=null name=renamed260"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Folders, "261", "parent=260 name=f261"),
                ]
            ),
            // 親の読み直し: 読み直した親の版は受理記録に残る。ダウンロードがその親へ届く前に端末で編集しても、
            // 偽の DuplicateOnServer にならずに上がる（263 は D2 が作って改名した親・262 は打ち切りを作る未送信の行）
            new(
                "parent-readback-ack-guards-later-edit",
                [Folder(SV, 262)],
                [
                    new UpdateFolderOp(D1, Direct, 262, null, "d1"),
                    Folder(D2, 263),
                    Folder(D2, 264, parent: 263),
                    Sync(D2),
                    new UpdateFolderOp(SV, Direct, 262, null, "sv"),
                    new UpdateFolderOp(D2, Direct, 263, null, "d2"),
                    Sync(D2),
                    Sync(D1),
                    new ExpectLocalOp(D1, ModelTables.Folders, "263", "parent=null name=d2"),
                    new ExpectLocalOp(D1, ModelTables.Folders, "264", "parent=263 name=f264"),
                    new UpdateFolderOp(D1, Direct, 263, null, "d1-263"),
                    Sync(D1),
                    Sync(D2),
                    new ExpectLocalOp(D2, ModelTables.Folders, "263", "parent=null name=d1-263"),
                ]
            ),
            // #18: ServerWins が、ローカルで消した部分木を子から戻す
            new(
                "18-serverwins-restore-subtree",
                [Folder(SV, 60), Folder(SV, 61, parent: 60)],
                [
                    new DeleteFolderOp(D1, Direct, 61),
                    new DeleteFolderOp(D1, Direct, 60),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                ]
            ),
            // #20: 自己参照の表のダウンロードが版の昇順で子を親より先に適用する
            new(
                "20-download-selfref-order",
                [Folder(SV, 70)],
                [
                    Folder(SV, 71),
                    Folder(SV, 72, parent: 71),
                    new UpdateFolderOp(SV, Direct, 71, null, "renamed71"),
                    Sync(D1),
                ]
            ),
            // #20: 同じ形で、洗い替え（RefreshAsync）
            new(
                "20-refresh-selfref-order",
                [Folder(SV, 73)],
                [
                    Folder(SV, 74),
                    Folder(SV, 75, parent: 74),
                    new UpdateFolderOp(SV, Direct, 74, null, "renamed74"),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, true),
                ]
            ),
            // #21: 版なしの表で保留した行を飛ばすだけで打ち切らず、後ろの表の行がそれを参照して止まる（競合も返らない）
            new(
                "21-versionless-held-row-referenced",
                [Folder(SV, 80), Folder(SV, 82)],
                [
                    new DeleteFolderOp(D1, Direct, 80),
                    new AddLabelOp(SV, Direct, "L80", 80, "cap"),
                    Item(SV, 81, folder: 82, label: "L80"),
                    Sync(D1, mode: SyncMode.LastWriteWins),
                ]
            ),
            // 3r #4（C2）: Versioned＋ServerWins の捨てる予定の id がランの範囲で絞られず、範囲外の表（版なし）の未送信の
            // 削除を親の削除として数えないまま、それを参照する行を適用して止まる
            new(
                "c2-serverwins-out-of-scope-delete",
                [Folder(SV, 83), new AddLabelOp(SV, Direct, "L83", 83, "cap")],
                [
                    new DeleteLabelOp(D1, Direct, "L83"),
                    Item(SV, 84, folder: 83, label: "L83"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                ]
            ),
            // #24: 一意な列の「削除→同じ値で挿入」を、アップロードが挿入から送って止まる
            new(
                "24-unique-delete-then-insert",
                [Folder(SV, 90), Item(SV, 91, folder: 90, code: "u1")],
                [new DeleteItemOp(D1, Direct, 91), Item(D1, 92, folder: 90, code: "u1"), Sync(D1)]
            ),
            // 新規（案 N1）: サーバーで一意な列の「削除→同じ値で挿入」が起きると、ダウンロードが挿入を削除伝搬より先に
            // 適用してローカルの一意制約で止まる（#24 のダウンロード側）
            new(
                "n1-download-unique-delete-then-insert",
                [Folder(SV, 110), Item(SV, 111, folder: 110, code: "v1")],
                [
                    new DeleteItemOp(SV, Direct, 111),
                    Item(SV, 112, folder: 110, code: "v1"),
                    Sync(D1),
                    // ダウンロードの前の削除伝搬が 111 を先に消すので、112 は同じランで降りる
                    new ExpectLocalOp(D1, ModelTables.Items, "111", null),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "112",
                        "folder=110 label=null code=v1 title=t112"
                    ),
                ]
            ),
            // N1: 同じ形を削除伝搬なしで。112 は 111 とぶつかる行で打ち切って降りず（例外にしない）、111 は残る
            new(
                "n1-download-unique-without-propagation",
                [Folder(SV, 113), Item(SV, 114, folder: 113, code: "v2")],
                [
                    new DeleteItemOp(SV, Direct, 114),
                    Item(SV, 115, folder: 113, code: "v2"),
                    Sync(D1, propagateDeletes: false),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "114",
                        "folder=113 label=null code=v2 title=t114"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Items, "115", null),
                ]
            ),
            // 新規（案 N2）: ServerWins の読み直しが、ローカルで消した行を戻すとき、同じ一意な値で作り直した未送信の行が
            // 残っていてローカルの一意制約で止まる（#24 の ServerWins 側）
            new(
                "n2-serverwins-unique-delete-then-insert",
                [Folder(SV, 140), Item(SV, 141, folder: 140, code: "w1")],
                [
                    new DeleteItemOp(D1, Direct, 141),
                    Item(D1, 142, folder: 140, code: "w1"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                    // サーバーに無い 142 を先に消してから 141 を戻す（同じランで）
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "141",
                        "folder=140 label=null code=w1 title=t141"
                    ),
                    new ExpectLocalOp(D1, ModelTables.Items, "142", null),
                ]
            ),
            // N2: 同じ形を削除伝搬なしで。142 は残り、141 は 142 とぶつかるので戻さず記録を残す（例外にしない）
            new(
                "n2-serverwins-unique-without-propagation",
                [Folder(SV, 143), Item(SV, 144, folder: 143, code: "w2")],
                [
                    new DeleteItemOp(D1, Direct, 144),
                    Item(D1, 145, folder: 143, code: "w2"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins, propagateDeletes: false),
                    new ExpectLocalOp(D1, ModelTables.Items, "144", null),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "145",
                        "folder=143 label=null code=w2 title=t145"
                    ),
                ]
            ),
            // 新規（案 N3）: 親の表を ExcludedEntityTypes で外した実行のダウンロードが、ローカルに無い親を参照する子を
            // 適用して止まる
            new(
                "n3-download-parent-table-excluded",
                [Folder(SV, 150)],
                [
                    Folder(SV, 151),
                    Item(SV, 152, folder: 151),
                    Sync(D1, excluded: [ModelTables.Folders]),
                ]
            ),
            // N3: 同じ形で、親が版なしの表（Versioned の実行の範囲外）
            new(
                "n3-download-parent-versionless",
                [Folder(SV, 153)],
                [
                    new AddLabelOp(SV, Direct, "L153", 153, "cap"),
                    Item(SV, 154, folder: 153, label: "L153"),
                    Sync(D1),
                ]
            ),
            // 新規（案 N4）: ServerWins の読み直しが戻す行の親が、実行の範囲外の表（版なし）でローカルに削除されていて止まる
            new(
                "n4-serverwins-restore-out-of-scope-parent",
                [
                    Folder(SV, 155),
                    new AddLabelOp(SV, Direct, "L155", 155, "cap"),
                    Item(SV, 156, folder: 155, label: "L155"),
                ],
                [
                    new DeleteItemOp(D1, Direct, 156),
                    new DeleteLabelOp(D1, Direct, "L155"),
                    Sync(D1, policy: SyncConflictPolicy.ServerWins),
                ]
            ),
            // #23 系: ServerWins が挿入の記録を捨てた後、残ったローカルの行を編集して上げると、blob の無い挿入になる
            new(
                "23-insert-after-discarded-blob-record",
                [Folder(SV, 160)],
                [
                    Item(D1, 161, folder: 160),
                    new ModelSyncOp(
                        D1,
                        ModelSyncKind.Upload,
                        SyncMode.Versioned,
                        SyncConflictPolicy.ServerWins,
                        [],
                        true,
                        500
                    ),
                    new UpdateItemOp(D1, Direct, 161, 160, null, "c-161", "edited"),
                    Sync(D1),
                ]
            ),
            // #25: 日時の主キー（Kind=Utc）のオフライン挿入が、キー文字列の食い違いで捨てられる
            new(
                "25-datetime-key-utc",
                [Folder(SV, 95), Item(SV, 96, folder: 95)],
                [
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234567),
                        96,
                        "utc"
                    ),
                    Sync(D1),
                ]
            ),
            // #25: 以前の版が Kind つきで書いたジャーナルのキー（ローカルのオフセット）が残ったローカル DB でも、ランの前に
            // 書き直して照合でき、オフラインの挿入が上がる
            new(
                "25-legacy-journal-key-text",
                [Folder(SV, 430), Item(SV, 431, folder: 430)],
                [
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 7, 1, 8, 0, 0).AddTicks(9),
                        431,
                        "legacy"
                    ),
                    new LegacyKeyTextsOp(D1, ModelTables.Events, "+09:00"),
                    Sync(D1),
                    new ExpectLocalOp(
                        SV,
                        ModelTables.Events,
                        "2026-07-01 08:00:00.0000009",
                        "item=431 memo=legacy"
                    ),
                ]
            ),
            // #25: 同じく受理記録のキー（Z つき）。書き直さないと、後の編集が受理記録を見つけられず偽の DuplicateOnServer になる
            new(
                "25-legacy-ack-key-text",
                [Folder(SV, 432), Item(SV, 433, folder: 432)],
                [
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 7, 2, 8, 0, 0).AddTicks(9),
                        433,
                        "legacy"
                    ),
                    new ModelSyncOp(
                        D1,
                        ModelSyncKind.Upload,
                        SyncMode.Versioned,
                        SyncConflictPolicy.Collect,
                        [],
                        true,
                        500
                    ),
                    new LegacyKeyTextsOp(D1, ModelTables.Events, "Z"),
                    new UpdateEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 7, 2, 8, 0, 0).AddTicks(9),
                        433,
                        "edited"
                    ),
                    Sync(D1),
                    new ExpectLocalOp(
                        SV,
                        ModelTables.Events,
                        "2026-07-02 08:00:00.0000009",
                        "item=433 memo=edited"
                    ),
                ]
            ),
            // #25: 同じ形で Kind=Local
            new(
                "25-datetime-key-local",
                [Folder(SV, 97), Item(SV, 98, folder: 97)],
                [
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Local).AddTicks(7654321),
                        98,
                        "local"
                    ),
                    Sync(D1),
                ]
            ),
            // #22: 強制上書き（後勝ち）の後、サーバーで別の書き手が書いた blob が降りず食い違ったまま
            new(
                "22-forced-overwrite-hides-server-blob",
                [Folder(SV, 120), Item(SV, 121, folder: 120)],
                [
                    new WritePayloadOp(SV, 121, "0C0D0E0F"),
                    new UpdateItemOp(D1, Direct, 121, 120, null, "c-121", "edited"),
                    Sync(D1, mode: SyncMode.LastWriteWins),
                ]
            ),
            // #14: コピー待ちの blob の元の行がサーバーで消えていると、NOT NULL の除外列へ NULL を書いて毎回止まる
            // （ローカルの未送信のイベントが品目を参照して残す＝ダウンロードの前の削除伝搬で行が先に消えないように）
            new(
                "14-owed-blob-source-gone",
                [Folder(SV, 170), Item(SV, 171, folder: 170)],
                [
                    new WritePayloadOp(SV, 171, "0A0A0A0A"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    Sync(D1),
                    new DeleteItemOp(SV, Direct, 171),
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 4, 1, 9, 0, 0).AddTicks(1),
                        171,
                        "keep"
                    ),
                    Sync(D1),
                ]
            ),
            // #16: 失敗した Write{列}Async の記録が残り、blob を持たない端末（コピーに失敗して空のまま）がサーバーの
            // blob を空で上書きする
            new(
                "16-failed-write-record-overwrites-blob",
                [Folder(SV, 175)],
                [
                    Item(SV, 176, folder: 175),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    Sync(D1),
                    new WritePayloadOp(D1, 176, "0B0B0B0B") { ExpectFailure = true },
                    Sync(D1),
                ]
            ),
            // #16: 失敗した更新（After フックの例外）の記録が残り、LocalWins の再生が古い内容でサーバーの他者の更新を戻す
            new(
                "16-failed-update-record-reverts-row",
                [Folder(SV, 210, name: "base")],
                [
                    new ArmFaultOp(D1, ModelFaultKind.LocalAfterSave),
                    new UpdateFolderOp(D1, ModelWriteVia.Graph, 210, null, "d1"),
                    new UpdateFolderOp(SV, Direct, 210, null, "sv"),
                    Sync(D1, policy: SyncConflictPolicy.LocalWins),
                ]
            ),
            // #16: 失敗した Write{列}Async の記録が残り、LocalWins の再生が古い blob でサーバーの他者の blob を戻す
            new(
                "16-failed-blob-write-record-reverts-blob",
                [Folder(SV, 212), Item(SV, 213, folder: 212)],
                [
                    new WritePayloadOp(D1, 213, "0D0D0D0D") { ExpectFailure = true },
                    new WritePayloadOp(SV, 213, "0E0E0E0E"),
                    Sync(D1, policy: SyncConflictPolicy.LocalWins),
                ]
            ),
            // #16: 失敗した Write{列}Async の記録が中身の変わらない書き込みとして上がり、版を進めて別の端末に偽の競合を出す
            new(
                "16-failed-blob-write-record-spurious-conflict",
                [Folder(SV, 214), Item(SV, 215, folder: 214)],
                [
                    new WritePayloadOp(D1, 215, "0F0F0F0F") { ExpectFailure = true },
                    Sync(D1),
                    new UpdateItemOp(D2, Direct, 215, 214, null, "c-215", "d2"),
                    Sync(D2),
                ]
            ),
            // 新規（案 N5）: 版なしの表の削除が DeleteRefusedOnServer になると、どのポリシーでも決着しない（ServerWins は
            // Versioned の実行で版なしの表を範囲外にし、後勝ちの実行はポリシーを見ない）
            new(
                "n5-versionless-delete-refused-never-settles",
                [Folder(SV, 216), new AddLabelOp(SV, Direct, "L216", 216, "cap")],
                [
                    new DeleteLabelOp(D1, Direct, "L216"),
                    Item(SV, 217, folder: 216, label: "L216"),
                    Sync(D1, mode: SyncMode.LastWriteWins),
                ]
            ),
            // N5: 同じ形で、静止化の ServerWins を通る前に決着しない形（キーだけのスタブの削除〔#27〕でサーバーに残った行が
            // ラベルを参照し続ける。種 1157 の操作の列を縮めたもの）
            new(
                "n5-versionless-delete-refused-after-stub",
                [
                    Folder(SV, 218),
                    new AddLabelOp(SV, Direct, "L218", 218, "cap"),
                    new AddLabelOp(SV, Direct, "L219", 218, "cap"),
                    Item(SV, 219, folder: 218, label: "L218"),
                    Item(SV, 220, folder: 218, label: "L218"),
                ],
                [
                    new UpdateItemOp(D2, ModelWriteVia.Graph, 219, 218, "L219", "c-219", "d2"),
                    new DeleteStubOp(D1, ModelTables.Items, "220"),
                    new ModelSyncOp(
                        D2,
                        ModelSyncKind.Upload,
                        SyncMode.Versioned,
                        SyncConflictPolicy.Collect,
                        [],
                        true,
                        3
                    ),
                    Sync(D1, excluded: [ModelTables.Folders]),
                    new DeleteLabelOp(D1, Direct, "L218"),
                ]
            ),
            // #23: LocalWins の復活の挿入が記録の列しか送らず、サーバーの行が blob の無いまま作り直される
            new(
                "23-localwins-resurrect-without-blob",
                [Folder(SV, 180), Item(SV, 181, folder: 180)],
                [
                    new UpdateItemOp(D1, Direct, 181, 180, null, "c-181", "edited"),
                    new DeleteItemOp(SV, Direct, 181),
                    Sync(D1, policy: SyncConflictPolicy.LocalWins),
                ]
            ),
            // #26: 外部の ISyncTable 実装には未送信のキーが渡らず、競合を報告した行のローカルの編集が上書きされる
            new(
                "26-external-table-overwrites-pending-edit",
                [Folder(SV, 185), Item(SV, 186, folder: 185)],
                [
                    new UpdateItemOp(D1, Direct, 186, 185, null, "c-186", "d1"),
                    new UpdateItemOp(SV, Direct, 186, 185, null, "c-186", "sv"),
                    Sync(D1),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        "186",
                        "folder=185 label=null code=c-186 title=d1"
                    ),
                ]
            )
            {
                ExternalTables = [D1],
            },
            // #27: キーだけのスタブで消した削除が黙って捨てられ、サーバーに行が残る
            new(
                "27-key-only-stub-delete",
                [Folder(SV, 190), Item(SV, 191, folder: 190)],
                [new DeleteStubOp(D1, ModelTables.Items, "191"), Sync(D1)]
            ),
            // #27: 同期の後もメモリに持っていた実体（挿入したときのもの）で消した削除が黙って捨てられる
            new(
                "27-held-object-delete",
                [Folder(SV, 192)],
                [
                    new AddItemOp(
                        D1,
                        ModelWriteVia.Graph,
                        193,
                        192,
                        null,
                        "c-193",
                        "t193",
                        "0C0C0C0C"
                    ),
                    Sync(D1),
                    new DeleteHeldOp(D1, ModelTables.Items, "193"),
                    Sync(D1),
                ]
            ),
            // #28: BeforeSaveAsync で主キーを決めるフックがあると古いキーで記録され、行が両側から消える
            new(
                "28-hook-assigns-key",
                [Folder(SV, 195)],
                [
                    new ArmFaultOp(D1, ModelFaultKind.HookRekey),
                    new AddFolderOp(D1, ModelWriteVia.Graph, 196, 195, "rekeyed"),
                    Sync(D1),
                ]
            ),
            // 3r rs #6: オフラインで削除して同じキーで作り直すと、偽の DuplicateOnServer になる
            new(
                "rs6-offline-delete-reinsert",
                [Folder(SV, 198)],
                [
                    new DeleteFolderOp(D1, Direct, 198),
                    new AddFolderOp(D1, Direct, 198, null, "again"),
                    Sync(D1),
                ]
            ),
            // 対照（失敗しない）: 失敗の注入の後の同期が取り直して収束する。乱数の種では、取り直しの不具合が既知の失敗と
            // 同じ署名（blob の食い違いなど）に吸い込まれて見えないことがあるので、取り直しそのものを単独で確かめる
            new(
                "retry-blob-download-copy",
                [Folder(SV, 230), Item(SV, 231, folder: 230)],
                [
                    new WritePayloadOp(SV, 231, "10101010"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    Sync(D1),
                    Sync(D1),
                ]
            ),
            new(
                "retry-blob-upload-copy",
                [Folder(SV, 232), Item(SV, 233, folder: 232)],
                [
                    new WritePayloadOp(D1, 233, "11111111"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobUpload),
                    Sync(D1),
                    Sync(D1),
                ]
            ),
            new(
                "retry-server-write",
                [Folder(SV, 234)],
                [
                    new UpdateFolderOp(D1, Direct, 234, null, "d1"),
                    Item(D1, 235, folder: 234),
                    new ArmFaultOp(D1, ModelFaultKind.ServerWrite),
                    Sync(D1),
                    Sync(D1),
                ]
            ),
            new(
                "retry-server-fetch",
                [Folder(SV, 236)],
                [
                    new UpdateFolderOp(SV, Direct, 236, null, "sv"),
                    Item(SV, 237, folder: 236),
                    new ArmFaultOp(D1, ModelFaultKind.ServerFetch),
                    Sync(D1),
                    Sync(D1),
                ]
            ),
            // 対照（失敗しない）: フックが飛ばした書き込みは、記録が残っても何も変えない
            new(
                "hook-skip-changes-nothing",
                [Folder(SV, 238, name: "base")],
                [
                    new ArmFaultOp(D1, ModelFaultKind.HookSkip),
                    new UpdateFolderOp(D1, ModelWriteVia.Graph, 238, null, "skipped"),
                    new UpdateFolderOp(SV, Direct, 238, null, "sv"),
                    Sync(D1),
                ],
                state =>
                    state.Folders[238].Name == "sv"
                        ? null
                        : $"folder 238: {state.Folders[238].Name}"
            ),
            // #19: varchar のキー × SQL_* 照合順序で、キー順のページングが行を黙って飛ばさない（SQL Server でだけ起きていた＝
            // '-' を ORDER BY は文字として並べ、nvarchar で束縛した @afterKey との比較は無視する。@afterKey を列の型へキャストする）
            new(
                "19-varchar-key-paging-skips-rows",
                [Folder(SV, 240)],
                [
                    new AddLabelOp(SV, Direct, "a-c", 240, "first"),
                    new AddLabelOp(SV, Direct, "ab", 240, "second"),
                    Sync(D1, mode: SyncMode.LastWriteWins, batch: 1),
                    // 静止化はバッチ 500＝1 ページで取り切って食い違いを消すので、ページングした直後のローカルを確かめる
                    new ExpectLocalOp(D1, ModelTables.Labels, "ab", "folder=240 caption=second"),
                ]
            ),
            // 対照: 同じ形で Kind=Unspecified（キーの文字列が読み戻しと一致する＝失敗しない）
            new(
                "25-datetime-key-unspecified",
                [Folder(SV, 99), Item(SV, 100, folder: 99)],
                [
                    new AddEventOp(
                        D1,
                        Direct,
                        new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Unspecified).AddTicks(
                            1111111
                        ),
                        100,
                        "plain"
                    ),
                    Sync(D1),
                ]
            ),
        };

        list.AddRange(PolicyScenarios());
        list.AddRange(UniqueValueTakenScenarios());

        return list;
    }

    /// <summary>
    /// 書き手が 2 人のキーの期待値（ポリシー×ぶつかり方）。端末 1 が先に（Collect で）上げ、端末 2 が古い版のまま書いて、
    /// 調べるポリシーで上げる。
    /// </summary>
    /// <remarks>
    /// ServerWins は端末 2 の変更を送らずに捨ててサーバーの行で上書きする＝端末 1 の値が残る。Collect は競合を報告して
    /// 残すだけなので、最後の静止化（Collect → ServerWins）が決着させる＝ServerWins と同じ期待になる。LocalWins は端末 2 の
    /// 変更を強制して送る（削除された行は復活させ、編集された行は消す）。後勝ち（LastWriteWins）はポリシーを見ず、後から
    /// 上げた端末 2 が勝つ。
    /// </remarks>
    private static IEnumerable<SyncModelScenario> PolicyScenarios()
    {
        var cases = new (string Name, SyncMode Mode, SyncConflictPolicy Policy)[]
        {
            ("collect", SyncMode.Versioned, SyncConflictPolicy.Collect),
            ("serverwins", SyncMode.Versioned, SyncConflictPolicy.ServerWins),
            ("localwins", SyncMode.Versioned, SyncConflictPolicy.LocalWins),
            ("lww", SyncMode.LastWriteWins, SyncConflictPolicy.Collect),
        };

        foreach (var (name, mode, policy) in cases)
        {
            var deviceTwoWins =
                policy == SyncConflictPolicy.LocalWins || mode == SyncMode.LastWriteWins;

            // 編集×編集: 端末 1 の編集が先に届き、端末 2 の編集がぶつかる
            yield return new(
                $"policy-{name}-edit-edit",
                [Folder(SV, 200, name: "base")],
                [
                    new UpdateFolderOp(D1, Direct, 200, null, "d1"),
                    Sync(D1, mode),
                    new UpdateFolderOp(D2, Direct, 200, null, "d2"),
                    Sync(D2, mode, policy),
                ],
                state => ExpectFolder(state, 200, deviceTwoWins ? "d2" : "d1")
            );

            // 削除×編集: 端末 1 の削除が先に届き、端末 2 の編集がぶつかる（端末 2 が勝てば行が復活する）
            yield return new(
                $"policy-{name}-delete-edit",
                [Folder(SV, 201, name: "base")],
                [
                    new DeleteFolderOp(D1, Direct, 201),
                    Sync(D1, mode),
                    new UpdateFolderOp(D2, Direct, 201, null, "d2"),
                    Sync(D2, mode, policy),
                ],
                state => ExpectFolder(state, 201, deviceTwoWins ? "d2" : null)
            );

            // 編集×削除: 端末 1 の編集が先に届き、端末 2 の削除がぶつかる（端末 2 が勝てば行が消える）
            yield return new(
                $"policy-{name}-edit-delete",
                [Folder(SV, 202, name: "base")],
                [
                    new UpdateFolderOp(D1, Direct, 202, null, "d1"),
                    Sync(D1, mode),
                    new DeleteFolderOp(D2, Direct, 202),
                    Sync(D2, mode, policy),
                ],
                state => ExpectFolder(state, 202, deviceTwoWins ? null : "d1")
            );
        }
    }

    /// <summary>
    /// N7: 端末 2 が先に取った code を、端末 1 がオフラインで作った行が使う（ポリシー 3 つ × モード 2 つ）。
    /// </summary>
    /// <remarks>
    /// Collect と LocalWins は UniqueValueTakenOnServer で報告して記録を残す（端末 1 の行は残り、サーバーには上がらず、
    /// 端末 2 の行はぶつかるのでそのランでは降りない）。ServerWins はどちらのモードでも決着させる（端末 1 の新しい行は
    /// 捨てられて消え、端末 2 の行が降りる）。
    /// </remarks>
    private static IEnumerable<SyncModelScenario> UniqueValueTakenScenarios()
    {
        var cases = new (string Name, SyncMode Mode, SyncConflictPolicy Policy)[]
        {
            ("collect", SyncMode.Versioned, SyncConflictPolicy.Collect),
            ("serverwins", SyncMode.Versioned, SyncConflictPolicy.ServerWins),
            ("localwins", SyncMode.Versioned, SyncConflictPolicy.LocalWins),
            ("lww-collect", SyncMode.LastWriteWins, SyncConflictPolicy.Collect),
            ("lww-serverwins", SyncMode.LastWriteWins, SyncConflictPolicy.ServerWins),
            ("lww-localwins", SyncMode.LastWriteWins, SyncConflictPolicy.LocalWins),
        };
        var index = 0;

        foreach (var (name, mode, policy) in cases)
        {
            var folder = 350 + (10 * index);
            var taken = folder + 1;
            var mine = folder + 2;
            var code = $"z{index}";
            var serverWins = policy == SyncConflictPolicy.ServerWins;
            index++;

            yield return new(
                $"n7-{name}",
                [Folder(SV, folder)],
                [
                    Item(D2, taken, folder: folder, code: code),
                    Sync(D2),
                    Item(D1, mine, folder: folder, code: code),
                    Sync(D1, mode, policy),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        ModelDbState.Key(mine),
                        serverWins ? null : $"folder={folder} label=null code={code} title=t{mine}"
                    ),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Items,
                        ModelDbState.Key(taken),
                        serverWins ? $"folder={folder} label=null code={code} title=t{taken}" : null
                    ),
                    new ExpectLocalOp(SV, ModelTables.Items, ModelDbState.Key(mine), null),
                ]
            );
        }
    }

    private static string? ExpectFolder(ModelDbState state, int id, string? name)
    {
        var actual = state.Folders.TryGetValue(id, out var row) ? row.Name : null;

        return actual == name
            ? null
            : $"folder {id}: expected {name ?? "(deleted)"}, got {actual ?? "(deleted)"}";
    }
}
