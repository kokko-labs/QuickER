using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の乱数の操作の列の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、種 1 つのシナリオ（<see cref="BackendParityGenerator.Create"/> が返したもの）の操作 1 つ。観測はシナリオを作った後、
/// 流す前に、シナリオの名前と操作の記録を読むだけで、生成器の乱数を引かない（種の指紋は変わらない）。
/// </para>
/// <para>
/// 網羅しない組の照合は、既定の種の数で、すべての種を流し終えたときだけ行う（違いのあった種が 3 つになると流すのを
/// 打ち切る＝そのときはテストがすでに赤）。範囲は <see cref="RandomScope"/>。
/// </para>
/// </remarks>
internal static class BackendParityDimensions
{
    /// <summary>網羅しない組の照合の範囲（乱数の操作の列）</summary>
    public const string RandomScope = "random";

    /// <summary>操作の範囲（どの実装先で流すか）</summary>
    public const string Mode = "Mode";

    /// <summary>操作の種類</summary>
    public const string OpKind = "OpKind";

    /// <summary>保存・更新の並行性の扱い</summary>
    public const string Concurrency = "Concurrency";

    /// <summary>並行性の扱いの引数を持たない操作</summary>
    private const string NotApplicable = "NotApplicable";

    /// <summary>因子：受け入れた手の差を持つ手か</summary>
    public const string Deviation = "Deviation";

    /// <summary>受け入れた手の差を持たない手の水準</summary>
    private const string NoDeviation = "None";

    /// <summary>因子：連絡先の手が扱う関係（連絡先を持つコレクション・付け替える外部キー）</summary>
    public const string ContactRelation = "ContactRelation";

    /// <summary>因子：その保存に当たる Save フックの規則の振る舞い</summary>
    public const string HookRule = "HookRule";

    /// <summary>規則が当たらない保存・連絡先の関係を持たない手の水準</summary>
    private const string NoneLevel = "None";

    /// <summary>並行性の扱いの引数を渡す操作（グラフ保存の単数・複数と UpdateAsync）</summary>
    private static readonly string[] ModeTakingKinds =
    [
        "Save",
        "SaveManyOrders",
        "UpdateOrder",
        "SaveCustomer",
        "UpdateCustomer",
    ];

    /// <summary>リモート面に無い操作（Include の取得・列の条件の一括削除・除外 blob の Stream アクセサ）</summary>
    private static readonly string[] NonRemoteKinds =
    [
        "LoadOrderWithLines",
        "ExecuteDeleteOrders",
        "WriteBlob",
        "ReadBlob",
        "LoadCustomerWithContacts",
    ];

    /// <summary>連絡先の関係を持つ手</summary>
    private static readonly string[] ContactRelationKinds =
    [
        "AttachContact",
        "RetargetContact",
        "ClearContactKey",
    ];

    /// <summary>顧客のグラフ保存（Save フックの規則が当たり得る手）</summary>
    private static readonly string[] CustomerSaveKinds = ["SaveCustomer", "SaveManyCustomers"];

    /// <summary>顧客のスロット（それ以外は注文のスロット）</summary>
    private static readonly string[] CustomerSlots =
    [
        "C",
        "D",
        "TC",
        "TD",
        BackendParityGenerator.HookSlot,
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(
                    Mode,
                    Enum.GetValues<ParityGeneratorMode>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "種を 3 で割った余りで決まる操作の範囲（Common＝リモート面の操作だけ・Full＝全機能面の操作も・Blob＝除外 blob の Stream アクセサも）"
                ),
                new Factor(
                    OpKind,
                    [
                        new Level("InsertCustomer"),
                        new Level("InsertOrder"),
                        new Level("InsertLine"),
                        new Level("Load"),
                        new Level("LoadOrderWithLines"),
                        new Level("AttachLine"),
                        new Level("AddLine"),
                        new Level("EditMemo"),
                        new Level("EditQuantity"),
                        new Level("MarkRemovedLine"),
                        new Level("MarkRemovedRoot"),
                        new Level("Save"),
                        new Level("SaveManyOrders"),
                        new Level("UpdateOrder"),
                        new Level("Delete"),
                        new Level("OutsideDelete"),
                        new Level("OutsideEditMemo"),
                        new Level("ExecuteDeleteOrders"),
                        new Level("WriteBlob"),
                        new Level("ReadBlob"),
                        new Level("InsertContact"),
                        new Level("LoadCustomer"),
                        new Level("LoadCustomerWithContacts"),
                        new Level("AttachContact"),
                        new Level("AttachReferral"),
                        new Level("EditCustomerNote"),
                        new Level("EditCustomerCode"),
                        new Level("SetReferrer"),
                        new Level("ClearReferrer"),
                        new Level("EditContactLabel"),
                        new Level("RetargetContact"),
                        new Level("ClearContactKey"),
                        new Level("MarkRemovedContact"),
                        new Level("MarkRemovedCustomer"),
                        new Level("SaveCustomer"),
                        new Level("SaveManyCustomers"),
                        new Level("UpdateCustomer"),
                        new Level("NewCustomerRoot"),
                    ],
                    "操作の記録の型（挿入は行の表で、削除の印は行の表で、顧客の編集は列で、連絡先の編集は付け替えか外すかで分ける。グラフ保存は根の表で分ける）"
                ),
                new Factor(
                    ContactRelation,
                    [
                        new Level(NotApplicable),
                        .. Enum.GetValues<ParityContactRelation>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "連絡先を持つコレクション（AttachContact）・付け替えるか外す外部キー（RetargetContact・ClearContactKey）。参照アクションごとの外部キー（必須×NO ACTION・任意×NO ACTION・SET NULL・SET DEFAULT・一意制約の参照）"
                ),
                new Factor(
                    HookRule,
                    [
                        new Level(NoneLevel),
                        .. Enum.GetValues<ParityHookBehavior>()
                            .Where(value => value != ParityHookBehavior.Record)
                            .Select(value => new Level(value.ToString())),
                    ],
                    "その顧客のグラフ保存に当たる Save フックの規則の振る舞い（列の末尾にだけ置く）"
                ),
                new Factor(
                    Concurrency,
                    [
                        new Level(
                            nameof(ConcurrencyMode.Optimistic),
                            AxisMember.Of(ConcurrencyMode.Optimistic)
                        ),
                        new Level(
                            nameof(ConcurrencyMode.ForceOverwrite),
                            AxisMember.Of(ConcurrencyMode.ForceOverwrite)
                        ),
                        new Level(NotApplicable),
                    ],
                    "保存・更新に渡す並行性の扱い（引数を持たない操作は NotApplicable）"
                ),
                new Factor(
                    Deviation,
                    [
                        new Level(NoDeviation),
                        .. Enum.GetValues<ParityDeviation>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "受け入れた手の差を持つ手か（BackendParityKnownDifferences.AcceptedDeviations・列の末尾にだけ置く。宣言した実装先はその手から後を比べない）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧（範囲は <see cref="RandomScope"/> だけ）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.GeneratedRuntime,
            [
                CoverageGap.Matching(
                    RandomScope,
                    "P1-3",
                    "P2",
                    "生成器は範囲ごとに候補の操作を分け、Full には Include の取得と一括削除だけを、Blob には除外 blob の Stream アクセサだけを足す（混ぜると EF Core もリモートも流せず、比べる相手がインメモリだけになる）",
                    Mode,
                    OpKind,
                    (mode, kind) =>
                        (
                            mode == nameof(ParityGeneratorMode.Full)
                            && kind is "WriteBlob" or "ReadBlob"
                        )
                        || (
                            mode == nameof(ParityGeneratorMode.Blob)
                            && kind
                                is "LoadOrderWithLines"
                                    or "ExecuteDeleteOrders"
                                    or "LoadCustomerWithContacts"
                        )
                ),
                .. new[]
                {
                    (
                        Mode,
                        nameof(ParityGeneratorMode.Blob),
                        ContactRelation,
                        nameof(ParityContactRelation.CustomerCode)
                    ),
                    (
                        Mode,
                        nameof(ParityGeneratorMode.Common),
                        Deviation,
                        nameof(ParityDeviation.ReferencedKeyUpdate)
                    ),
                    (
                        Mode,
                        nameof(ParityGeneratorMode.Full),
                        HookRule,
                        nameof(ParityHookBehavior.ThrowBefore)
                    ),
                }.Select(tuple =>
                    CoverageGap.Tuple(
                        RandomScope,
                        "P2-5",
                        "P2-末",
                        "普段の 200 種では、末尾の手（全体の約 3 分の 2 の種に 1 つ）と範囲の組み合わせが足りない（夜間の種の数では埋まる）",
                        (tuple.Item1, tuple.Item2),
                        (tuple.Item3, tuple.Item4)
                    )
                ),
                CoverageGap.Tuple(
                    RandomScope,
                    "P1-3",
                    "P2",
                    "生成器の SaveMany は保存の引数を渡さず（SaveManyOrdersOp の Options が null＝既定の Optimistic）、ForceOverwrite の複数形のグラフ保存を作らない",
                    (OpKind, "SaveManyOrders"),
                    (Concurrency, nameof(ConcurrencyMode.ForceOverwrite))
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            "Parity",
            "実装先どうしの一致（乱数の操作の列）",
            Table,
            BackendParityScenarios.All.Count,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityBackend.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.Customers.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityKnownDifferences.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityOperations.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRunner.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityScenarios.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerRuntimeTests.cs",
            ],
            [
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.Sqlite",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.InMemory",
                "src/QuickER.Runtime.AspNetCore",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime",
            ]
        );

    /// <summary>シナリオの操作を、流す前にケースとして数える（シナリオの名前と操作の記録を読むだけで乱数を引かない）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="scenario">種から作ったシナリオ（名前の末尾が操作の範囲）</param>
    public static void Observe(CoverageCounter counter, ParityScenario scenario)
    {
        var mode = scenario.Name.Split('-')[^1];
        var rule = scenario.Options?.HookRules?.SingleOrDefault();

        foreach (var op in scenario.Ops)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Mode] = mode,
                    [OpKind] = KindOf(op),
                    [Concurrency] = ConcurrencyOf(op),
                    [Deviation] = op is DeviatingOp deviating
                        ? deviating.Deviation.ToString()
                        : NoDeviation,
                    [ContactRelation] = ContactRelationOf(op),
                    [HookRule] =
                        rule is not null && HoldsHookSlot(op)
                            ? rule.Behavior.ToString()
                            : NoneLevel,
                }
            );
        }
    }

    /// <summary>操作の種類（知らない型は型の名前を返す＝宣言していない水準として観測が断る）</summary>
    private static string KindOf(ParityOp op) =>
        op switch
        {
            DeviatingOp deviating => KindOf(deviating.Inner),
            InsertOp { Row: CustomerRow } => "InsertCustomer",
            InsertOp { Row: OrderRow } => "InsertOrder",
            InsertOp { Row: LineRow } => "InsertLine",
            InsertOp { Row: ContactRow } => "InsertContact",
            LoadOp { Table: "customers" } => "LoadCustomer",
            LoadOp => "Load",
            LoadCustomerWithContactsOp => "LoadCustomerWithContacts",
            AttachContactOp => "AttachContact",
            AttachChildCustomerOp => "AttachReferral",
            EditCustomerOp { Code: not null } => "EditCustomerCode",
            EditCustomerOp { Referrer: not null } => "SetReferrer",
            EditCustomerOp { ClearReferrer: true } => "ClearReferrer",
            EditCustomerOp => "EditCustomerNote",
            EditContactOp { Target: null } => "EditContactLabel",
            EditContactOp { Target: { Customer: null, Code: null } } => "ClearContactKey",
            EditContactOp => "RetargetContact",
            MarkRemovedOp removed
                when removed.Key.StartsWith("contacts/", StringComparison.Ordinal) =>
                "MarkRemovedContact",
            MarkRemovedOp removed
                when removed.Key.StartsWith("customers/", StringComparison.Ordinal) =>
                "MarkRemovedCustomer",
            SaveOp save when CustomerSlots.Contains(save.Slot) => "SaveCustomer",
            SaveManyCustomersOp => "SaveManyCustomers",
            UpdateCustomerOp => "UpdateCustomer",
            NewRootOp { Row: CustomerRow } => "NewCustomerRoot",
            LoadOrderWithLinesOp => "LoadOrderWithLines",
            AttachLineOp => "AttachLine",
            AddLineOp => "AddLine",
            EditMemoOp => "EditMemo",
            EditQuantityOp => "EditQuantity",
            MarkRemovedOp removed
                when removed.Key.StartsWith("order_lines/", StringComparison.Ordinal) =>
                "MarkRemovedLine",
            MarkRemovedOp removed
                when removed.Key.StartsWith("orders/", StringComparison.Ordinal) =>
                "MarkRemovedRoot",
            SaveOp => "Save",
            SaveManyOrdersOp => "SaveManyOrders",
            UpdateOrderOp => "UpdateOrder",
            DeleteOp => "Delete",
            OutsideDeleteOp => "OutsideDelete",
            OutsideEditMemoOp => "OutsideEditMemo",
            ExecuteDeleteOrdersOp => "ExecuteDeleteOrders",
            WriteBlobOp => "WriteBlob",
            ReadBlobOp => "ReadBlob",
            _ => op.GetType().Name,
        };

    /// <summary>操作が渡す並行性の扱い（省略した引数は既定の Optimistic）</summary>
    private static string ConcurrencyOf(ParityOp op) =>
        op switch
        {
            DeviatingOp deviating => ConcurrencyOf(deviating.Inner),
            SaveOp save => (save.Options ?? new()).Mode.ToString(),
            SaveManyOrdersOp many => (many.Options ?? new()).Mode.ToString(),
            UpdateOrderOp update => update.Mode.ToString(),
            UpdateCustomerOp update => update.Mode.ToString(),
            _ => NotApplicable,
        };

    /// <summary>連絡先の手が扱う関係</summary>
    private static string ContactRelationOf(ParityOp op) =>
        op switch
        {
            DeviatingOp deviating => ContactRelationOf(deviating.Inner),
            AttachContactOp attach => attach.Relation.ToString(),
            EditContactOp { Target: { } target } => target.Relation.ToString(),
            _ => NotApplicable,
        };

    /// <summary>Save フックの規則の末尾の手のスロットを保存する手か</summary>
    private static bool HoldsHookSlot(ParityOp op) =>
        op switch
        {
            DeviatingOp deviating => HoldsHookSlot(deviating.Inner),
            SaveOp save => save.Slot == BackendParityGenerator.HookSlot,
            SaveManyCustomersOp many => many.SlotNames.Contains(BackendParityGenerator.HookSlot),
            _ => false,
        };

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Mode, out var mode);
        assignment.TryGetValue(OpKind, out var kind);
        assignment.TryGetValue(Concurrency, out var concurrency);
        assignment.TryGetValue(Deviation, out var deviation);
        assignment.TryGetValue(ContactRelation, out var relation);
        assignment.TryGetValue(HookRule, out var hook);

        // 手の差の印は、その差を持つ手の種類にだけ付く（生成器が末尾に置く手と同じ対応）
        if (deviation is not null && deviation != NoDeviation && kind is not null)
        {
            string[] kinds = deviation switch
            {
                nameof(ParityDeviation.ParentDeleteWithChildren) => ["Delete", "SaveManyCustomers"],
                nameof(ParityDeviation.OrphanInsert) => ["InsertOrder", "InsertLine"],
                nameof(ParityDeviation.ReferencedKeyUpdate)
                or nameof(ParityDeviation.AlternateKeyUpdate) => ["SaveCustomer", "UpdateCustomer"],
                nameof(ParityDeviation.DependentHeldParentDelete) => ["SaveManyCustomers"],
                _ => CustomerSaveKinds,
            };

            if (!kinds.Contains(kind))
            {
                return false;
            }
        }

        // 手の差の印と並行性の扱い：削除・挿入・複数形の顧客の保存は引数を持たず、代替キーの更新は UpdateAsync か単数の
        // グラフ保存（引数を持つ）
        if (deviation is not null && concurrency is not null)
        {
            if (
                deviation
                    is nameof(ParityDeviation.ParentDeleteWithChildren)
                        or nameof(ParityDeviation.OrphanInsert)
                        or nameof(ParityDeviation.DependentHeldParentDelete)
                && concurrency != NotApplicable
            )
            {
                return false;
            }

            if (
                deviation
                    is nameof(ParityDeviation.ReferencedKeyUpdate)
                        or nameof(ParityDeviation.AlternateKeyUpdate)
                && concurrency == NotApplicable
            )
            {
                return false;
            }
        }

        // 連絡先の関係を持つ手は、保存でも手の差の印でもない（並行性の扱い・規則・印を持たない）
        if (relation is not null && relation != NotApplicable)
        {
            if (
                (concurrency is not null && concurrency != NotApplicable)
                || (hook is not null && hook != NoneLevel)
                || (deviation is not null && deviation != NoDeviation)
            )
            {
                return false;
            }
        }

        // 連絡先の関係は連絡先の手だけが持ち、必須の owner は外さない
        if (kind is not null && relation is not null)
        {
            if (ContactRelationKinds.Contains(kind) != (relation != NotApplicable))
            {
                return false;
            }
        }

        if (kind == "ClearContactKey" && relation == nameof(ParityContactRelation.Owner))
        {
            return false;
        }

        // Save フックの規則は顧客のグラフ保存にだけ当たり、規則とその手の差の印は対で決まる（書き換えてから飛ばす＝
        // HookEditsSkippedRow・書き換えて続ける削除＝HookEditsDeletedRow・ほかの規則は印なし）
        if (
            hook is not null
            && hook != NoneLevel
            && kind is not null
            && !CustomerSaveKinds.Contains(kind)
        )
        {
            return false;
        }

        if (hook is not null && deviation is not null)
        {
            var hookDeviation =
                deviation
                is nameof(ParityDeviation.HookEditsSkippedRow)
                    or nameof(ParityDeviation.HookEditsDeletedRow);

            if (hook == NoneLevel ? hookDeviation : (deviation != NoDeviation && !hookDeviation))
            {
                return false;
            }

            if (
                (hook == nameof(ParityHookBehavior.EditThenSkip))
                    != (deviation == nameof(ParityDeviation.HookEditsSkippedRow))
                || (
                    deviation == nameof(ParityDeviation.HookEditsDeletedRow)
                    && hook != nameof(ParityHookBehavior.EditThenContinue)
                )
            )
            {
                return false;
            }
        }

        // Common はリモート面にある操作だけの範囲として定義した（4 つの実装先すべてで流す）ので、リモート面に無い操作は含まない
        if (
            mode == nameof(ParityGeneratorMode.Common)
            && kind is not null
            && NonRemoteKinds.Contains(kind)
        )
        {
            return false;
        }

        if (kind is not null && concurrency is not null)
        {
            // グラフ保存と UpdateAsync は並行性の扱いを必ず渡す（省略しても既定の Optimistic）
            if (ModeTakingKinds.Contains(kind) && concurrency == NotApplicable)
            {
                return false;
            }

            // ほかの操作は並行性の扱いの引数を持たない
            if (!ModeTakingKinds.Contains(kind) && concurrency != NotApplicable)
            {
                return false;
            }
        }

        return true;
    }
}
