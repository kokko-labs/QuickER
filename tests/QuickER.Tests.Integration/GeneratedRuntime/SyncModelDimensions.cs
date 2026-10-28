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
                    "種ごとに生成器が決める同期のモード（種の中で固定）",
                    AxisMember.Property(typeof(SyncOptions), nameof(SyncOptions.Mode))
                ),
                new Factor(
                    Policy,
                    Enum.GetValues<SyncConflictPolicy>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "種ごとに生成器が決める競合ポリシー（種の中で固定）",
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
                    ],
                    "操作の種類（write＝行の追加・更新・削除 1 つ／unique-reuse＝一意な値を手放す削除と、同じ値の別のキーの挿入の組／reinsert＝同じキーで消して作り直す組／payload＝blob の書き込み／failing-write＝業務の書き込みが失敗する形／delete-stub・delete-held＝キーだけのスタブ・持っていた実体のグラフ保存での削除／sync・upload・download＝同期の呼び方／fault＝失敗の仕掛け）"
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
            ],
            [new FactorGroup(Mode, Policy, OpFamily)],
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
                CoverageGap.Tuple(
                    DirectScope,
                    "P1-3",
                    "P2",
                    "キーだけのスタブでの削除は Extras の組の端末の書き込みの 25%×1/5 でしか選ばれず、既定の 60 種では LastWriteWins×Collect の種（Mode と Policy は種ごとに 1 つ）にたまたま現れない（3 因子の組を狙って作る生成をしていない）",
                    (Mode, nameof(SyncMode.LastWriteWins)),
                    (Policy, nameof(SyncConflictPolicy.Collect)),
                    (OpFamily, DeleteStub)
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
                }
            );
        }
    }

    /// <summary>
    /// 歩の操作ごとの種類。削除と挿入の 2 つの組は、同じキーなら作り直し、違うキーなら一意な値の付け替えとして 2 つとも数える
    /// （知らない操作の型は型の名前を返し、宣言していない水準として観測が断る）
    /// </summary>
    private static string[] Families(IReadOnlyList<ModelOp> ops)
    {
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

        return true;
    }
}
