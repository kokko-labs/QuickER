using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>網羅の行の入力（生成器の代わりに宣言するモード・ポリシー・種の組と、初期データ・歩の列）</summary>
/// <param name="Mode">同期のモード（行のどの同期もこのモード）</param>
/// <param name="Policy">競合ポリシー（行のどの同期もこのポリシー）</param>
/// <param name="Extras">Extras の操作を含む組か（<see cref="SyncModelDimensions"/> の Group）</param>
/// <param name="Faults">失敗の仕掛けを含む組か</param>
/// <param name="Setup">初期データ（サーバーの他者の書き込み。<see cref="SyncModelScenarios.Tail"/> を後ろに足す）</param>
/// <param name="Steps">歩の列（1 歩＝生成器の 1 回の <see cref="SyncModelGenerator.Next"/> と同じ単位。作り直しは削除と挿入を 1 歩にする）</param>
/// <param name="ExpectedTuples">
/// 行が出すはずの組（因子 → 水準。どれか 1 歩の操作の割り当てがその組をすべて含む）。出さなくなったら
/// <see cref="SyncModelCoverageRows.MissingTupleProblems"/> が行を名指しして赤にする
/// </param>
internal sealed record SyncModelCoverageInput(
    SyncMode Mode,
    SyncConflictPolicy Policy,
    bool Extras,
    bool Faults,
    IReadOnlyList<ModelWriteOp> Setup,
    IReadOnlyList<IReadOnlyList<ModelOp>> Steps,
    IReadOnlyList<IReadOnlyDictionary<string, string>> ExpectedTuples
)
{
    /// <summary>世界の端末（既定は端末 1・2。端末 3 の組を埋める行は端末 1〜3）</summary>
    public IReadOnlyList<ModelActor> Devices { get; init; } = SyncModelWorldShape.TwoDevices;

    /// <summary>端末ごとの最初の形（既定は空。最初の形の組を埋める行だけが持つ。無い端末は空から始める）</summary>
    public IReadOnlyList<SyncModelPreload> Preloads { get; init; } = [];

    /// <summary>世界の形（端末と端末ごとの最初の形）</summary>
    public SyncModelWorldShape Shape =>
        new(
            Devices,
            [
                .. Devices.Select(device =>
                    Preloads.SingleOrDefault(p => p.Device == device)?.Kind ?? ModelPreload.None
                ),
            ]
        );
}

/// <summary>
/// 同期のモデルの網羅の行（網羅しない組の一覧の Pending を埋めるために、直結の乱数のテストが種の前に必ず流す行）
/// </summary>
/// <remarks>
/// <para>
/// <b>決定的なシナリオ（<see cref="SyncModelScenarios"/>）との違い</b>：シナリオは指摘ごとの回帰の網で、別の Theory の行として流れ、
/// 網羅の計数に届かない（期待の操作 ExpectLocalOp なども混ざり、種の組やモードを宣言しない）。網羅の行は、乱数の種で作れない・
/// 欠ける組（サーバーの他者の Extras の操作・グラフ保存の失敗する書き込み・表を除外したダウンロード）を実際に
/// 流すための行で、操作を 1 歩ずつ次元表（<see cref="SyncModelDimensions"/>）で、同期の結果を結果の表
/// （<see cref="SyncModelOutcomeDimensions"/>）で数える。行は期待の操作を持たない（次元表に無い操作の型は宣言の誤りになる）。
/// 違反は既知の失敗の一覧で外さない＝どれも新しい失敗として赤にする。行が狙った組（<see cref="SyncModelCoverageInput.ExpectedTuples"/>）を
/// 出さなくなったら、<see cref="MissingTupleProblems"/> が行を名指しして赤にする（網羅の照合だけだと、一覧に無い欠けとして行を名指しせずに赤になる）。
/// Id は <see cref="IdPrefix"/> で始める。
/// </para>
/// </remarks>
internal static class SyncModelCoverageRows
{
    /// <summary>網羅の行の Id の接頭辞</summary>
    public const string IdPrefix = "coverage-";

    private const ModelActor D1 = ModelActor.Device1;
    private const ModelActor D2 = ModelActor.Device2;
    private const ModelActor D3 = ModelActor.Device3;
    private const ModelActor SV = ModelActor.Server;
    private const ModelWriteVia Direct = ModelWriteVia.Direct;
    private const ModelWriteVia Graph = ModelWriteVia.Graph;

    /// <summary>行の表</summary>
    public static ScenarioTable<SyncModelCoverageInput> Table { get; } =
        new(EngineIds.SyncModel, Build().Select(Checked));

    /// <summary>行を流し、操作と同期の結果を数え、違反と操作の記録を返す</summary>
    /// <param name="row">行</param>
    /// <param name="coverage">操作を数える先</param>
    /// <param name="outcomes">同期の結果を数える先</param>
    /// <param name="ct">取り消し</param>
    /// <param name="options">世界の組み方（既定は直結・SQLite のサーバー役。端末は行が決める）</param>
    public static async Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> RunAsync(
        ScenarioRow<SyncModelCoverageInput> row,
        CoverageCounter coverage,
        CoverageCounter outcomes,
        CancellationToken ct,
        SyncModelWorldOptions? options = null
    )
    {
        var input = row.Input;
        await using var run = await SyncModelRun.StartAsync(
            [.. input.Setup, .. SyncModelScenarios.Tail],
            ct,
            (options ?? SyncModelWorldOptions.Direct) with
            {
                Devices = input.Devices,
            },
            input.Preloads
        );

        foreach (var step in input.Steps)
        {
            SyncModelDimensions.Observe(
                coverage,
                input.Mode,
                input.Policy,
                input.Extras,
                input.Faults,
                input.Shape,
                step
            );

            foreach (var op in step)
            {
                if (!await run.ExecuteAsync(op))
                {
                    break;
                }
            }
        }

        await run.FinishAsync();
        SyncModelOutcomeDimensions.Observe(outcomes, run.Outcomes);
        return (run.Failures, run.Log);
    }

    /// <summary>
    /// 行が出すはずの組を出さなかった問題（行を名指しする）。歩ごとの割り当ては網羅の計数と同じ
    /// <see cref="SyncModelDimensions.Assignments"/> で作るので、流さずに判定できる
    /// </summary>
    /// <param name="row">行</param>
    public static IEnumerable<string> MissingTupleProblems(ScenarioRow<SyncModelCoverageInput> row)
    {
        var input = row.Input;
        var assignments = input
            .Steps.SelectMany(step =>
                SyncModelDimensions.Assignments(
                    input.Mode,
                    input.Policy,
                    input.Extras,
                    input.Faults,
                    input.Shape,
                    step
                )
            )
            .ToList();

        if (input.ExpectedTuples.Count == 0)
        {
            yield return $"coverage row {row.Id}: declares no expected tuple";
        }

        foreach (var expected in input.ExpectedTuples)
        {
            if (
                !assignments.Any(assignment =>
                    expected.All(pair =>
                        assignment.TryGetValue(pair.Key, out var level) && level == pair.Value
                    )
                )
            )
            {
                yield return $"coverage row {row.Id}: expected a step with "
                    + string.Join(", ", expected.Select(pair => $"{pair.Key}={pair.Value}"))
                    + " (the row's steps no longer produce the tuple it was written to fill)";
            }
        }
    }

    /// <summary>行の同期が宣言したモード・ポリシーと違えば断る（数えるモード・ポリシーは行の宣言で決める）</summary>
    private static ScenarioRow<SyncModelCoverageInput> Checked(
        ScenarioRow<SyncModelCoverageInput> row
    )
    {
        foreach (var sync in row.Input.Steps.SelectMany(step => step).OfType<ModelSyncOp>())
        {
            if (sync.Mode != row.Input.Mode || sync.Policy != row.Input.Policy)
            {
                throw new ArgumentException(
                    $"coverage row {row.Id}: the sync {sync.Describe()} differs from the declared {row.Input.Mode} / {row.Input.Policy}"
                );
            }
        }

        return row;
    }

    private static IEnumerable<ScenarioRow<SyncModelCoverageInput>> Build()
    {
        // ---- サーバーの他者の Extras の操作（Actor=Server × OpFamily=Extras・OpFamily=Extras × Via=None・3 因子の組
        // Server × Extras × UniqueTable=none）。生成器の TryExtra は端末の書き手だけを選ぶ（乱数の列を変えずに組を作る） ----
        yield return Row(
            "server-reinsert",
            "サーバーの他者が品目を同じキーで消して作り直す（reinsert）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: true,
            faults: false,
            [Folder(SV, 500), Item(SV, 501, 500, "cr1")],
            [
                [new DeleteItemOp(SV, Direct, 501), Item(SV, 501, 500, "cr1b")],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
                [Sync(D2, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [ServerExtra(SyncModelDimensions.Reinsert)]
        );
        yield return Row(
            "server-failing-write",
            "サーバーの他者の一意な code の重複する挿入が失敗する（failing-write）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: true,
            faults: false,
            [Folder(SV, 510), Item(SV, 511, 510, "cf1")],
            [
                [Item(SV, 512, 510, "cf1") with { ExpectFailure = true }],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [ServerExtra(SyncModelDimensions.FailingWrite)]
        );
        yield return Row(
            "server-delete-stub",
            "サーバーの他者がキーだけのスタブのグラフ保存でラベルを消す（delete-stub）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: true,
            faults: false,
            [Folder(SV, 520), new AddLabelOp(SV, Direct, "cl1", 520, "l1")],
            [
                [new DeleteStubOp(SV, ModelTables.Labels, "cl1")],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [ServerExtra(SyncModelDimensions.DeleteStub)]
        );
        yield return Row(
            "server-delete-held",
            "サーバーの他者が挿入したときの実体のグラフ保存でラベルを消す（delete-held）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: true,
            faults: false,
            [Folder(SV, 530)],
            [
                [new AddLabelOp(SV, Direct, "ch1", 530, "l1")],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
                [new DeleteHeldOp(SV, ModelTables.Labels, "ch1")],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [ServerExtra(SyncModelDimensions.DeleteHeld)]
        );

        // ---- 失敗する書き込み × グラフ保存（生成器は直接の書き込みだけで作る）。端末のグラフ保存の挿入が一意な code の重複で
        // 失敗し、記録が取り消される（⑲のグラフ保存の経路） ----
        yield return Row(
            "graph-failing-write",
            "端末のグラフ保存の挿入が一意な code の重複で失敗する（failing-write × Graph）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: true,
            faults: false,
            [Folder(SV, 540), Item(SV, 541, 540, "cg1")],
            [
                [Item(D1, 542, 540, "cg1", Graph) with { ExpectFailure = true }],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Actor, nameof(ModelActor.Device1)),
                    (SyncModelDimensions.OpFamily, SyncModelDimensions.FailingWrite),
                    (SyncModelDimensions.Via, nameof(ModelWriteVia.Graph))
                ),
            ]
        );

        // ---- 表を除外したダウンロード（Download × Excluded=Folders / Events）。ダウンロードは同期の 10%・除外はその 15% で、
        // 既定の 60 種ではこの 2 つがたまたま出ない ----
        yield return Row(
            "download-excluding-folders-and-events",
            "フォルダー・イベントをそれぞれ除外したダウンロード",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [Folder(SV, 550), Item(SV, 551, 550, "cx1")],
            [
                [new UpdateItemOp(SV, Direct, 551, 550, null, "cx1", "changed")],
                [
                    Sync(
                        D1,
                        SyncMode.Versioned,
                        SyncConflictPolicy.Collect,
                        ModelSyncKind.Download,
                        [ModelTables.Folders]
                    ),
                ],
                [
                    Sync(
                        D2,
                        SyncMode.Versioned,
                        SyncConflictPolicy.Collect,
                        ModelSyncKind.Download,
                        [ModelTables.Events]
                    ),
                ],
            ],
            [
                Tuple(
                    (SyncModelDimensions.OpFamily, SyncModelDimensions.Download),
                    (SyncModelDimensions.Excluded, ModelTables.Folders)
                ),
                Tuple(
                    (SyncModelDimensions.OpFamily, SyncModelDimensions.Download),
                    (SyncModelDimensions.Excluded, ModelTables.Events)
                ),
            ]
        );

        // ---- 端末 3 の一意な値の形（Actor=Device3 × delete-take-reparent × model_folders）。子のフォルダーを 2 つ持つ親は
        // 乱数の列でまれで、広い世界の既定の種ではこの組がたまたま出ない（端末 1・2 は既存の範囲の種が出す） ----
        yield return Row(
            "device3-delete-take-reparent-folders",
            "端末 3 が子のフォルダーを消し、もう一方の子をその名前で別の親へ付け替え、葉になった親を消す（delete-take-reparent）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [Folder(SV, 590), Folder(SV, 591, 590), Folder(SV, 592, 590), Folder(SV, 593)],
            [
                new ModelShapeStep(
                    SyncModelGenerator.DeleteTakeReparentShape,
                    ModelTables.Folders,
                    [
                        new DeleteFolderOp(D3, Direct, 591),
                        new UpdateFolderOp(D3, Direct, 592, 593, "f591"),
                        new DeleteFolderOp(D3, Direct, 590),
                    ]
                ),
                [Sync(D3, SyncMode.Versioned, SyncConflictPolicy.Collect)],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Actor, nameof(ModelActor.Device3)),
                    (SyncModelDimensions.OpFamily, SyncModelGenerator.DeleteTakeReparentShape),
                    (SyncModelDimensions.UniqueTable, ModelTables.Folders)
                ),
            ],
            SyncModelWorldShape.ThreeDevices
        );

        // ---- 一意な値の形のうち、release-take の形を閉じる 4 本目の乱数（SyncModelGenerator.UniqueCycleRate）が半分を入れ替え・
        // 回転へ回すため、既定の種でたまたま欠ける組（サーバーの他者・端末 3 の書き手、端末の最初の形 Journaled との組） ----
        yield return Row(
            "server-release-take-chain-items",
            "サーバーの他者が品目の code を 3 行の連鎖で付け替える（release-take-chain）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [
                Folder(SV, 600),
                Item(SV, 601, 600, "rc1"),
                Item(SV, 602, 600, "rc2"),
                Item(SV, 603, 600, "rc3"),
            ],
            [
                new ModelShapeStep(
                    SyncModelGenerator.ReleaseTakeChainShape,
                    ModelTables.Items,
                    [
                        new UpdateItemOp(SV, Direct, 601, 600, null, "rc1x", "t601b"),
                        new UpdateItemOp(SV, Direct, 602, 600, null, "rc1", "t602b"),
                        new UpdateItemOp(SV, Direct, 603, 600, null, "rc2", "t603b"),
                    ]
                ),
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Actor, nameof(ModelActor.Server)),
                    (SyncModelDimensions.OpFamily, SyncModelGenerator.ReleaseTakeChainShape),
                    (SyncModelDimensions.UniqueTable, ModelTables.Items)
                ),
            ]
        );
        yield return Row(
            "server-exact-swap-folders",
            "サーバーの他者が 2 つのフォルダーの名前を仮の名前を挟んで入れ替える（exact-swap）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [Folder(SV, 610), Folder(SV, 611)],
            [
                new ModelShapeStep(
                    SyncModelGenerator.ExactSwapShape,
                    ModelTables.Folders,
                    [
                        new UpdateFolderOp(SV, Direct, 610, null, "f610tmp"),
                        new UpdateFolderOp(SV, Direct, 611, null, "f610"),
                        new UpdateFolderOp(SV, Direct, 610, null, "f611"),
                    ]
                ),
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Actor, nameof(ModelActor.Server)),
                    (SyncModelDimensions.OpFamily, SyncModelGenerator.ExactSwapShape),
                    (SyncModelDimensions.UniqueTable, ModelTables.Folders)
                ),
            ]
        );
        yield return Row(
            "device3-release-take-chain-items",
            "端末 3 が品目の code を 3 行の連鎖で付け替えて上げる（release-take-chain）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [
                Folder(SV, 620),
                Item(SV, 621, 620, "rd1"),
                Item(SV, 622, 620, "rd2"),
                Item(SV, 623, 620, "rd3"),
            ],
            [
                new ModelShapeStep(
                    SyncModelGenerator.ReleaseTakeChainShape,
                    ModelTables.Items,
                    [
                        new UpdateItemOp(D3, Direct, 621, 620, null, "rd1x", "t621b"),
                        new UpdateItemOp(D3, Direct, 622, 620, null, "rd1", "t622b"),
                        new UpdateItemOp(D3, Direct, 623, 620, null, "rd2", "t623b"),
                    ]
                ),
                [Sync(D3, SyncMode.Versioned, SyncConflictPolicy.Collect)],
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Actor, nameof(ModelActor.Device3)),
                    (SyncModelDimensions.OpFamily, SyncModelGenerator.ReleaseTakeChainShape),
                    (SyncModelDimensions.UniqueTable, ModelTables.Items)
                ),
            ],
            SyncModelWorldShape.ThreeDevices
        );
        yield return Row(
            "journaled-device-rotation-folders",
            "最初の同期の前に Repository 越しに行を書いた端末 1 が、3 つのフォルダーの名前を仮の名前を挟んで回して上げる（rotation）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [],
            [
                new ModelShapeStep(
                    SyncModelGenerator.RotationShape,
                    ModelTables.Folders,
                    [
                        new UpdateFolderOp(D1, Direct, 1111, null, "jtmp1111"),
                        new UpdateFolderOp(D1, Direct, 1112, null, "jf1111"),
                        new UpdateFolderOp(D1, Direct, 1113, null, "jf1112"),
                        new UpdateFolderOp(D1, Direct, 1111, null, "jf1113"),
                    ]
                ),
                [Sync(D1, SyncMode.Versioned, SyncConflictPolicy.Collect)],
                [Sync(D2, SyncMode.Versioned, SyncConflictPolicy.Collect)],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Preload, nameof(ModelPreload.Journaled)),
                    (SyncModelDimensions.OpFamily, SyncModelGenerator.RotationShape)
                ),
            ],
            SyncModelWorldShape.ThreeDevices,
            [
                new SyncModelPreload(
                    D1,
                    ModelPreload.Journaled,
                    [
                        new AddFolderOp(D1, Direct, 1111, null, "jf1111"),
                        new AddFolderOp(D1, Direct, 1112, null, "jf1112"),
                        new AddFolderOp(D1, Direct, 1113, null, "jf1113"),
                    ]
                ),
            ]
        );

        // ---- 端末の最初の形 × 除外（Preload=Untracked × Excluded=model_labels）。表を 1 つだけ除外する同期は乱数の列で 4 表に
        // 分かれ、広い世界の既定の種ではこの組がたまたま出ない ----
        yield return Row(
            "untracked-device-sync-excluding-labels",
            "Repository を通らずに行を入れた端末 1 が、ラベルを除外して最初の同期をする（追跡しない行は上がらず、削除伝搬で消える）",
            SyncMode.Versioned,
            SyncConflictPolicy.Collect,
            extras: false,
            faults: false,
            [],
            [
                [
                    Sync(
                        D1,
                        SyncMode.Versioned,
                        SyncConflictPolicy.Collect,
                        excluded: [ModelTables.Labels]
                    ),
                ],
            ],
            [
                Tuple(
                    (SyncModelDimensions.Preload, nameof(ModelPreload.Untracked)),
                    (SyncModelDimensions.OpFamily, SyncModelDimensions.Sync),
                    (SyncModelDimensions.Excluded, ModelTables.Labels)
                ),
            ],
            SyncModelWorldShape.ThreeDevices,
            [
                new SyncModelPreload(
                    D1,
                    ModelPreload.Untracked,
                    [new AddFolderOp(D1, Direct, 1801, null, "uf1801")]
                ),
            ]
        );
    }

    private static ScenarioRow<SyncModelCoverageInput> Row(
        string id,
        string description,
        SyncMode mode,
        SyncConflictPolicy policy,
        bool extras,
        bool faults,
        IReadOnlyList<ModelWriteOp> setup,
        IReadOnlyList<IReadOnlyList<ModelOp>> steps,
        IReadOnlyList<IReadOnlyDictionary<string, string>> expected,
        IReadOnlyList<ModelActor>? devices = null,
        IReadOnlyList<SyncModelPreload>? preloads = null
    ) =>
        new(
            IdPrefix + id,
            null,
            description,
            new SyncModelCoverageInput(mode, policy, extras, faults, setup, steps, expected)
            {
                Devices = devices ?? SyncModelWorldShape.TwoDevices,
                Preloads = preloads ?? [],
            }
        );

    /// <summary>期待の組（因子 → 水準）</summary>
    private static IReadOnlyDictionary<string, string> Tuple(
        params (string Factor, string Level)[] pairs
    ) => pairs.ToDictionary(pair => pair.Factor, pair => pair.Level, StringComparer.Ordinal);

    /// <summary>サーバーの他者の Extras の操作の行が埋める組（Server × 操作 × 経路なし × 一意な値の表なし）</summary>
    private static IReadOnlyDictionary<string, string> ServerExtra(string family) =>
        Tuple(
            (SyncModelDimensions.Actor, nameof(ModelActor.Server)),
            (SyncModelDimensions.OpFamily, family),
            (SyncModelDimensions.Via, SyncModelDimensions.NoVia),
            (SyncModelDimensions.UniqueTable, SyncModelDimensions.NoUniqueTable)
        );

    private static ModelSyncOp Sync(
        ModelActor device,
        SyncMode mode,
        SyncConflictPolicy policy,
        ModelSyncKind kind = ModelSyncKind.Sync,
        IReadOnlyList<string>? excluded = null
    ) => new(device, kind, mode, policy, excluded ?? [], true, 500);

    private static AddFolderOp Folder(ModelActor actor, int id, int? parent = null) =>
        new(actor, Direct, id, parent, $"f{id}");

    private static AddItemOp Item(
        ModelActor actor,
        int id,
        int folder,
        string code,
        ModelWriteVia via = Direct
    ) => new(actor, via, id, folder, null, code, $"t{id}", $"0A0B{id:X4}");
}
