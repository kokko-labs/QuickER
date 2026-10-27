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

    /// <summary>並行性の扱いの引数を渡す操作（グラフ保存の単数・複数と UpdateAsync）</summary>
    private static readonly string[] ModeTakingKinds = ["Save", "SaveManyOrders", "UpdateOrder"];

    /// <summary>リモート面に無い操作（Include の取得・列の条件の一括削除・除外 blob の Stream アクセサ）</summary>
    private static readonly string[] NonRemoteKinds =
    [
        "LoadOrderWithLines",
        "ExecuteDeleteOrders",
        "WriteBlob",
        "ReadBlob",
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
                    ],
                    "操作の記録の型（挿入は行の表で、削除の印は根か子かで分ける）"
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
                            && kind is "LoadOrderWithLines" or "ExecuteDeleteOrders"
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

        foreach (var op in scenario.Ops)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Mode] = mode,
                    [OpKind] = KindOf(op),
                    [Concurrency] = ConcurrencyOf(op),
                }
            );
        }
    }

    /// <summary>操作の種類（知らない型は型の名前を返す＝宣言していない水準として観測が断る）</summary>
    private static string KindOf(ParityOp op) =>
        op switch
        {
            InsertOp { Row: CustomerRow } => "InsertCustomer",
            InsertOp { Row: OrderRow } => "InsertOrder",
            InsertOp { Row: LineRow } => "InsertLine",
            LoadOp => "Load",
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
            SaveOp save => (save.Options ?? new()).Mode.ToString(),
            SaveManyOrdersOp many => (many.Options ?? new()).Mode.ToString(),
            UpdateOrderOp update => update.Mode.ToString(),
            _ => NotApplicable,
        };

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Mode, out var mode);
        assignment.TryGetValue(OpKind, out var kind);
        assignment.TryGetValue(Concurrency, out var concurrency);

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
