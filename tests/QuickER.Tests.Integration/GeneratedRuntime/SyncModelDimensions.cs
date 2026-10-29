using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの乱数の操作の列の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、乱数の種の 1 歩で <see cref="SyncModelGenerator.Next"/> が返した操作 1 つ。観測は生成器が返した値
/// （操作の記録と、生成器がコンストラクタで決めた <see cref="SyncModelGenerator.Mode"/> などのプロパティ）を読むだけで、
/// 乱数を引かない（種の指紋は変わらない）。操作は実行の前に観測する（同じ歩の前の操作が失敗して実行されなかった操作も数える）。
/// </para>
/// <para>
/// 網羅しない組の照合は、直結のクラスの乱数の種を既定の数で流したときだけ行う（範囲 <see cref="DirectScope"/>）。
/// HTTP・SQL Server の派生は種の数が少なく（既定 10）、SQL Server は失敗の注入の組を流さないので、観測と要約だけにする
/// （転送経路は、後の区切りで行列の軸として守る）。
/// </para>
/// </remarks>
internal static class SyncModelDimensions
{
    /// <summary>網羅しない組の照合の範囲（直結の乱数の種）</summary>
    public const string DirectScope = "direct";

    /// <summary>同期のモード</summary>
    public const string Mode = "Mode";

    /// <summary>競合ポリシー</summary>
    public const string Policy = "Policy";

    /// <summary>種の組（種を 4 で割った余り）</summary>
    public const string Group = "Group";

    /// <summary>操作の書き手</summary>
    public const string Actor = "Actor";

    /// <summary>操作の種類</summary>
    public const string OpFamily = "OpFamily";

    /// <summary>端末の書き込みの経路</summary>
    public const string Via = "Via";

    /// <summary>一意な値を付け替える表（一意な値の形の操作だけ）</summary>
    public const string UniqueTable = "UniqueTable";

    /// <summary>同期の操作の削除伝搬</summary>
    public const string PropagateDeletes = "PropagateDeletes";

    /// <summary>同期の操作で除外した表</summary>
    public const string Excluded = "Excluded";

    /// <summary>同期の操作のダウンロードのバッチの大きさ</summary>
    public const string Batch = "Batch";

    /// <summary>同期の操作で blob を運ぶか</summary>
    public const string Blobs = "Blobs";

    /// <summary>仕掛けた失敗の種類</summary>
    public const string FaultKind = "FaultKind";

    // 失敗の仕掛けでない操作の、失敗の種類の水準
    private const string NotFault = "not-fault";

    // 同期の操作でない操作の、同期の設定の因子の水準
    private const string NotSync = "not-sync";

    // 同期の操作の、除外する表が無い水準
    private const string NoExclusion = "none";

    // 一意な値の形でない操作の、表の水準
    private const string NoUniqueTable = "none";

    // 操作の種類の水準
    private const string Write = "write";
    private const string UniqueReuse = "unique-reuse";
    private const string Reinsert = "reinsert";
    private const string Payload = "payload";
    private const string FailingWrite = "failing-write";
    private const string DeleteStub = "delete-stub";
    private const string DeleteHeld = "delete-held";
    private const string Sync = "sync";
    private const string Upload = "upload";
    private const string Download = "download";
    private const string Fault = "fault";
    private const string ReleaseTake = SyncModelGenerator.ReleaseTakeShape;
    private const string ReleaseTakeChain = SyncModelGenerator.ReleaseTakeChainShape;
    private const string DeleteTake = SyncModelGenerator.DeleteTakeShape;
    private const string DeleteTakeReparent = SyncModelGenerator.DeleteTakeReparentShape;

    /// <summary>一意な値の付け替えの形（<see cref="SyncModelGenerator"/> の TryUniqueShape）</summary>
    private static readonly string[] UniqueShapeFamilies =
    [
        ReleaseTake,
        ReleaseTakeChain,
        DeleteTake,
        DeleteTakeReparent,
    ];

    /// <summary>同期の呼び出しの操作の種類</summary>
    private static readonly string[] SyncFamilies = [Sync, Upload, Download];

    // 経路の水準（書き込みの経路を持たない操作と、経路を使わないサーバーの他者の書き込み）
    private const string NoVia = "None";

    // 種の組の水準
    private const string Plain = "plain";
    private const string Extras = "extras";
    private const string Faults = "faults";
    private const string ExtrasFaults = "extras-faults";

    /// <summary>Extras の組だけが作る操作の種類（<see cref="SyncModelGenerator"/> の TryExtra）</summary>
    private static readonly string[] ExtraFamilies =
    [
        Reinsert,
        DeleteStub,
        DeleteHeld,
        FailingWrite,
    ];

    /// <summary>書き込みの経路を持たない操作の種類（同期の呼び出しと失敗の仕掛け）</summary>
    private static readonly string[] NonWriteFamilies = [Sync, Upload, Download, Fault];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncModel,
            [
                new Factor(
                    Mode,
                    Enum.GetValues<SyncMode>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "種の番号から決める同期のモード（種の中で固定。種を 4 で割った商をモード×ポリシーの 6 組へ順に配る）",
                    AxisMember.Property(typeof(SyncOptions), nameof(SyncOptions.Mode))
                ),
                new Factor(
                    Policy,
                    Enum.GetValues<SyncConflictPolicy>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "種の番号から決める競合ポリシー（種の中で固定。モードと同じ配り方）",
                    AxisMember.Property(typeof(SyncOptions), nameof(SyncOptions.ConflictPolicy))
                ),
                new Factor(
                    Group,
                    [
                        new Level(Plain),
                        new Level(Extras),
                        new Level(Faults),
                        new Level(ExtrasFaults),
                    ],
                    "種を 4 で割った余り（0＝足さない・1＝Extras〔キーだけのスタブ・持っていた実体での削除・同じキーでの作り直し・失敗する書き込み〕・2＝Faults〔失敗の注入〕・3＝両方）"
                ),
                new Factor(
                    Actor,
                    Enum.GetValues<ModelActor>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "操作の書き手（同期・失敗の仕掛けは対象の端末）"
                ),
                new Factor(
                    OpFamily,
                    [
                        new Level(Write),
                        new Level(UniqueReuse),
                        new Level(Reinsert),
                        new Level(Payload),
                        new Level(FailingWrite),
                        new Level(DeleteStub),
                        new Level(DeleteHeld),
                        new Level(Sync),
                        new Level(Upload),
                        new Level(Download),
                        new Level(Fault),
                        new Level(ReleaseTake),
                        new Level(ReleaseTakeChain),
                        new Level(DeleteTake),
                        new Level(DeleteTakeReparent),
                    ],
                    "操作の種類（write＝行の追加・更新・削除 1 つ／unique-reuse＝一意な値を手放す削除と、同じ値の別のキーの挿入の組／reinsert＝同じキーで消して作り直す組／payload＝blob の書き込み／failing-write＝業務の書き込みが失敗する形／delete-stub・delete-held＝キーだけのスタブ・持っていた実体のグラフ保存での削除／sync・upload・download＝同期の呼び方／fault＝失敗の仕掛け／release-take＝更新で一意な値を空け、別の行がその値を更新で取る（4m-D3）／release-take-chain＝その 3 行の連鎖／delete-take＝行を消して値を空け、別の行がその値を更新で取る（4m-B3 の材料）／delete-take-reparent＝消す行の値を別の行が取って別の親へ付け替え、元の親を消す（4m-D4））"
                ),
                new Factor(
                    Via,
                    [
                        new Level(nameof(ModelWriteVia.Direct)),
                        new Level(nameof(ModelWriteVia.Graph)),
                        new Level(NoVia),
                    ],
                    "端末の書き込みの経路（直接の CRUD かグラフ保存か。経路を持たない操作とサーバーの他者の書き込みは None）"
                ),
                new Factor(
                    UniqueTable,
                    [
                        new Level(ModelTables.Items),
                        new Level(ModelTables.Folders),
                        new Level(NoUniqueTable),
                    ],
                    "一意な値の形の操作が付け替える表（品目の code・フォルダーの name。unique-reuse は品目。それ以外の操作は none）"
                ),
                new Factor(
                    PropagateDeletes,
                    [new Level("True"), new Level("False"), new Level(NotSync)],
                    "同期の操作の削除伝搬（同期の操作ごとに乱数で選ぶ。false は SyncModelGenerator.PropagateDeletesOffRate の割合。同期でない操作は not-sync）",
                    AxisMember.Property(typeof(SyncOptions), nameof(SyncOptions.PropagateDeletes))
                ),
                new Factor(
                    Excluded,
                    [
                        new Level(NoExclusion),
                        .. ModelTables.All.Select(table => new Level(table)),
                        new Level(NotSync),
                    ],
                    "同期の操作で実行の範囲から外した表（同期の 15% で 1 つ。同期でない操作は not-sync）",
                    AxisMember.Property(
                        typeof(SyncOptions),
                        nameof(SyncOptions.ExcludedEntityTypes)
                    )
                ),
                new Factor(
                    Batch,
                    [
                        .. SyncModelGenerator.Batches.Select(size => new Level(
                            size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        )),
                        new Level(NotSync),
                    ],
                    "同期の操作のダウンロードのバッチの大きさ（SyncModelGenerator.Batches から選ぶ。同期でない操作は not-sync）",
                    AxisMember.Property(typeof(SyncOptions), nameof(SyncOptions.DownloadBatchSize))
                ),
                new Factor(
                    Blobs,
                    [new Level("True"), new Level("False"), new Level(NotSync)],
                    "同期の操作で除外した blob を運ぶか（乱数の同期は常に運ぶ。運ばない水準は網羅しない組に受け入れる。同期でない操作は not-sync）",
                    AxisMember.Property(
                        typeof(SyncOptions),
                        nameof(SyncOptions.IncludeUnboundedBinary)
                    )
                ),
                new Factor(
                    FaultKind,
                    [
                        .. Enum.GetValues<ModelFaultKind>()
                            .Where(kind => kind != ModelFaultKind.ApplySkip)
                            .Select(kind => new Level(kind.ToString())),
                        new Level(NotFault),
                    ],
                    "仕掛けた失敗の種類（ApplySkip は乱数に混ぜない＝決定的なシナリオだけが使うので水準にしない。失敗の仕掛けでない操作は not-fault）"
                ),
            ],
            [
                new FactorGroup(Mode, Policy, OpFamily),
                new FactorGroup(Policy, OpFamily, PropagateDeletes),
                new FactorGroup(Actor, OpFamily, UniqueTable),
            ],
            IsValid
        );

    /// <summary>網羅しない組の一覧（範囲は <see cref="DirectScope"/> だけ）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncModel,
            [
                CoverageGap.Matching(
                    DirectScope,
                    "P1-3",
                    "P2",
                    "Extras の操作（同じキーでの作り直し・キーだけのスタブ・持っていた実体での削除・失敗する書き込み）は、生成器の TryExtra が端末の書き手のときだけ選ぶ（サーバーの他者の書き込みは TryWrite だけ）",
                    Actor,
                    OpFamily,
                    (actor, family) =>
                        actor == nameof(ModelActor.Server) && ExtraFamilies.Contains(family)
                ),
                CoverageGap.Matching(
                    DirectScope,
                    "P1-3",
                    "P2",
                    "経路 None の書き込みはサーバーの他者だけのもので、Extras の操作は生成器の TryExtra が端末の書き手のときだけ選ぶ（上の Actor × OpFamily の欠けと同じ原因）",
                    OpFamily,
                    Via,
                    (family, via) => ExtraFamilies.Contains(family) && via == NoVia
                ),
                CoverageGap.Tuple(
                    DirectScope,
                    "P1-3",
                    "P2",
                    "失敗する書き込みは、生成器が直接の書き込み（一意な値の重複する AddItemOp と、途中で失敗するストリームの WritePayloadOp）だけで作り、グラフ保存の形を作らない",
                    (OpFamily, FailingWrite),
                    (Via, nameof(ModelWriteVia.Graph))
                ),
                .. ExtraFamilies.Select(family =>
                    CoverageGap.Tuple(
                        DirectScope,
                        "P2-3",
                        "P2",
                        "Extras の操作は生成器の TryExtra が端末の書き手のときだけ選ぶ（上の Actor × OpFamily の欠けと同じ原因。3 因子の組 Actor × OpFamily × UniqueTable にも同じ欠けが出る）",
                        (Actor, nameof(ModelActor.Server)),
                        (OpFamily, family),
                        (UniqueTable, NoUniqueTable)
                    )
                ),
                .. new[] { ModelTables.Folders, ModelTables.Events }.Select(table =>
                    CoverageGap.Tuple(
                        DirectScope,
                        "P2-3",
                        "P2",
                        "ダウンロードだけの同期は同期の 10%、表の除外はその 15% で、既定の 60 種ではこの表を除外したダウンロードがたまたま出ない（組を狙って作る生成をしていない）",
                        (OpFamily, Download),
                        (Excluded, table)
                    )
                ),
                .. Table
                    .Factors.Where(factor => factor.Name != Blobs)
                    .Select(factor =>
                        CoverageGap.AcceptedMatching(
                            DirectScope,
                            "P2-3",
                            "blob を運ぶかは端末ごとに一貫させる製品の割り切り（docs/code-generation.md の「Unbounded binary columns」の節の「Choose once per device whether to carry the columns, and keep to it.」）。運ばない実行を挟むと、その間に動いた行の blob は後の運ぶ実行でも動かず、収束の表明が成り立たないので、乱数の同期は常に運ぶ（運ばない実行は決定的なシナリオ n12-* が受け持つ）",
                            factor.Name,
                            Blobs,
                            (_, blobs) => blobs == "False"
                        )
                    ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncModel,
            "Random",
            "同期のモデル（乱数の操作の列）",
            Table,
            SyncModelScenarios.All.Count,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelFaults.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelGenerator.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelKnownFailures.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelOperations.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRun.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRuntimeTests.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelServer.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelTransportRuntimeTests.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelTransports.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelWorld.cs",
            ],
            [
                "src/QuickER.Runtime.Sync",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban",
            ]
        );

    /// <summary>1 歩で生成器が返した操作を、実行の前にケースとして数える（乱数を引かない）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="generator">操作を返した生成器（コンストラクタで決めたプロパティだけを読む）</param>
    /// <param name="ops">その歩の操作</param>
    public static void Observe(
        CoverageCounter counter,
        SyncModelGenerator generator,
        IReadOnlyList<ModelOp> ops
    )
    {
        var families = Families(ops);
        var group = (generator.Extras, generator.Faults) switch
        {
            (false, false) => Plain,
            (true, false) => Extras,
            (false, true) => Faults,
            _ => ExtrasFaults,
        };

        for (var i = 0; i < ops.Count; i++)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Mode] = generator.Mode.ToString(),
                    [Policy] = generator.Policy.ToString(),
                    [Group] = group,
                    [Actor] = ActorOf(ops[i]),
                    [OpFamily] = families[i],
                    [Via] = ViaOf(ops[i]),
                    [UniqueTable] = UniqueTableOf(ops, families[i]),
                    [PropagateDeletes] = ops[i] is ModelSyncOp sync
                        ? sync.PropagateDeletes.ToString()
                        : NotSync,
                    [Excluded] = ops[i] switch
                    {
                        ModelSyncOp { Excluded.Count: 0 } => NoExclusion,
                        ModelSyncOp { Excluded: [var table] } => table,
                        ModelSyncOp other => string.Join(",", other.Excluded),
                        _ => NotSync,
                    },
                    [Batch] = ops[i] is ModelSyncOp batched
                        ? batched.BatchSize.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                        : NotSync,
                    [FaultKind] = ops[i] is ArmFaultOp armed ? armed.Kind.ToString() : NotFault,
                    [Blobs] = ops[i] is ModelSyncOp carried
                        ? carried.IncludeBlobs.ToString()
                        : NotSync,
                }
            );
        }
    }

    /// <summary>一意な値の形の操作が付け替える表（形でない操作は none。unique-reuse は品目の code を使う）</summary>
    private static string UniqueTableOf(IReadOnlyList<ModelOp> ops, string family) =>
        ops is ModelShapeStep step ? step.Table
        : family == UniqueReuse ? ModelTables.Items
        : NoUniqueTable;

    /// <summary>
    /// 歩の操作ごとの種類。削除と挿入の 2 つの組は、同じキーなら作り直し、違うキーなら一意な値の付け替えとして 2 つとも数える
    /// （知らない操作の型は型の名前を返し、宣言していない水準として観測が断る）
    /// </summary>
    private static string[] Families(IReadOnlyList<ModelOp> ops)
    {
        if (ops is ModelShapeStep step)
        {
            return [.. ops.Select(_ => step.Shape)];
        }

        if (
            ops.Count == 2
            && ops[0] is ModelWriteOp first
            && IsDelete(first)
            && ops[1] is ModelWriteOp second
            && IsAdd(second)
            && !first.ExpectFailure
            && !second.ExpectFailure
            && string.Equals(first.Table, second.Table, StringComparison.Ordinal)
        )
        {
            var family = string.Equals(first.KeyText, second.KeyText, StringComparison.Ordinal)
                ? Reinsert
                : UniqueReuse;

            return [family, family];
        }

        return ops.Select(Family).ToArray();
    }

    private static string Family(ModelOp op) =>
        op switch
        {
            ModelSyncOp { Kind: ModelSyncKind.Sync } => Sync,
            ModelSyncOp { Kind: ModelSyncKind.Upload } => Upload,
            ModelSyncOp { Kind: ModelSyncKind.Download } => Download,
            ArmFaultOp => Fault,
            DeleteStubOp => DeleteStub,
            DeleteHeldOp => DeleteHeld,
            ModelWriteOp { ExpectFailure: true } => FailingWrite,
            WritePayloadOp => Payload,
            ModelWriteOp write when IsAdd(write) || IsUpdate(write) || IsDelete(write) => Write,
            _ => op.GetType().Name,
        };

    private static bool IsAdd(ModelWriteOp op) =>
        op is AddFolderOp or AddLabelOp or AddItemOp or AddEventOp;

    private static bool IsUpdate(ModelWriteOp op) =>
        op is UpdateFolderOp or UpdateLabelOp or UpdateItemOp or UpdateEventOp;

    private static bool IsDelete(ModelWriteOp op) =>
        op is DeleteFolderOp or DeleteLabelOp or DeleteItemOp or DeleteEventOp;

    private static string ActorOf(ModelOp op) =>
        op switch
        {
            ModelWriteOp write => write.Actor.ToString(),
            ModelSyncOp sync => sync.Device.ToString(),
            ArmFaultOp fault => fault.Device.ToString(),
            _ => $"(no actor: {op.GetType().Name})",
        };

    private static string ViaOf(ModelOp op) =>
        op is ModelWriteOp write && write.Actor != ModelActor.Server ? write.Via.ToString() : NoVia;

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Group, out var group);
        assignment.TryGetValue(Actor, out var actor);
        assignment.TryGetValue(OpFamily, out var family);
        assignment.TryGetValue(Via, out var via);

        // Group の水準は「Extras の操作を足すか・失敗の仕掛けを足すか」そのものの定義なので、足さない組に
        // その操作が現れることは定義から起こり得ない
        if (family is not null && group is not null)
        {
            if (ExtraFamilies.Contains(family) && group is Plain or Faults)
            {
                return false;
            }

            if (family == Fault && group is Plain or Extras)
            {
                return false;
            }
        }

        // サーバーの他者は端末でない（同期エンジン・ジャーナル・保存のフックを持たない）ので、同期の呼び出しも失敗の仕掛けも
        // 受け取らない
        if (
            family is not null
            && actor == nameof(ModelActor.Server)
            && NonWriteFamilies.Contains(family)
        )
        {
            return false;
        }

        // 同期の呼び出しと失敗の仕掛けは書き込みでないので、書き込みの経路を持たない
        if (
            family is not null
            && via is not null
            && NonWriteFamilies.Contains(family)
            && via != NoVia
        )
        {
            return false;
        }

        // サーバーの他者の書き込みは端末の経路を使わない（サーバー役のリポジトリへ直接書く＝ApplyOnServerAsync は経路を見ない）
        if (actor == nameof(ModelActor.Server) && via is not null && via != NoVia)
        {
            return false;
        }

        // 端末の書き込みは、必ず直接の CRUD かグラフ保存のどちらかを通る
        if (
            family is not null
            && actor is not null
            && via == NoVia
            && actor != nameof(ModelActor.Server)
            && !NonWriteFamilies.Contains(family)
        )
        {
            return false;
        }

        // blob の書き込み（Write{列}Async）にはグラフ保存の形が無い（グラフ保存は除外した blob を書かない）
        if (family == Payload && via == nameof(ModelWriteVia.Graph))
        {
            return false;
        }

        // キーだけのスタブ・持っていた実体での削除は、RowState=Removed を付けた実体のグラフ保存として定義した操作
        if (family is DeleteStub or DeleteHeld && via == nameof(ModelWriteVia.Direct))
        {
            return false;
        }

        // 同期の設定（削除伝搬・除外・バッチ・blob）は同期の操作だけが持つ（同期でない操作は not-sync と定義した水準）。
        // 同期の操作は端末のもので、書き込みの経路も付け替える表も持たないので、それらを持つ割り当ても not-sync だけ
        var settings = new[] { PropagateDeletes, Excluded, Batch, Blobs }
            .Where(assignment.ContainsKey)
            .Select(setting => assignment[setting] == NotSync)
            .Distinct()
            .ToList();

        if (settings.Count > 1)
        {
            return false;
        }

        if (settings.Count == 1)
        {
            var notSync = settings[0];

            if (family is not null && SyncFamilies.Contains(family) == notSync)
            {
                return false;
            }

            if (
                !notSync
                && (
                    actor == nameof(ModelActor.Server)
                    || (via is not null && via != NoVia)
                    || (
                        assignment.TryGetValue(UniqueTable, out var table) && table != NoUniqueTable
                    )
                )
            )
            {
                return false;
            }
        }

        // 失敗の種類は失敗の仕掛けだけが持つ（仕掛けは端末のもので、経路も同期の設定も付け替える表も持たない）
        if (assignment.TryGetValue(FaultKind, out var faultKind) && faultKind != NotFault)
        {
            if (
                (family is not null && family != Fault)
                || actor == nameof(ModelActor.Server)
                || (via is not null && via != NoVia)
                || settings is [false]
                || (
                    assignment.TryGetValue(UniqueTable, out var faultTable)
                    && faultTable != NoUniqueTable
                )
                || (group is Plain or Extras)
            )
            {
                return false;
            }
        }
        else if (faultKind == NotFault && family == Fault)
        {
            return false;
        }

        // 付け替える表は一意な値の形の操作だけが持つ（unique-reuse は品目の code の削除と挿入と定義した操作）
        if (family is not null && assignment.TryGetValue(UniqueTable, out var uniqueTable))
        {
            var shaped = UniqueShapeFamilies.Contains(family);

            if (shaped == (uniqueTable == NoUniqueTable) && family != UniqueReuse)
            {
                return false;
            }

            if (family == UniqueReuse && uniqueTable != ModelTables.Items)
            {
                return false;
            }
        }

        return true;
    }
}
