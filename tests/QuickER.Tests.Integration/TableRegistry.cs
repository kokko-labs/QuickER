using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>Integration にある単体の表（エンジンの台帳に載らない、決まった形の行の表）の台帳</summary>
/// <remarks>
/// 宣言はこのファイルにまとめ、表のファイルには置かない。台帳を変えたら写し（<c>protection-registry.json</c>）を書き直す
/// （<see cref="ProtectionSnapshotDriftTests"/>）。
/// </remarks>
internal static class TableRegistry
{
    /// <summary>生成コードのランタイムのテストの置き場</summary>
    private const string Runtime = "tests/QuickER.Tests.Integration/GeneratedRuntime/";

    /// <summary>台帳の行（名前は一意）</summary>
    public static IReadOnlyList<TableDescriptor> All { get; } =
    [
        new(
            "TranslatorParityPredicates",
            "式の翻訳の述語の表（SQL Server・SQLite・インメモリと SQL Server の EF Core 版の結果を C# の意味論と突き合わせる）",
            [
                Runtime + "TranslatorParityPredicateRows.cs",
                Runtime + "TranslatorParityRuntimeTestsBase.cs",
                Runtime + "TranslatorParityInMemoryRuntimeTests.cs",
                Runtime + "TranslatorParitySqlServerRuntimeTests.cs",
                Runtime + "TranslatorParitySqliteRuntimeTests.cs",
                Runtime + "TranslatorParityEfCoreSqlServerRuntimeTests.cs",
            ],
            [
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_05_QueryPipeline.scriban",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_08_EfCore.scriban",
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.InMemory",
                "src/QuickER.Runtime.SqlServer",
                "src/QuickER.Runtime.Sqlite",
            ]
        ),
        new(
            "SyncPlanExpectations",
            "同期の差分と計画の期待の行（SyncPlan のエンジン〔SyncPlan/Fuzz・SyncPlan/CapabilityNet〕が流す。方言ごとの期待と実 DB の段の汎用の表明）",
            ["tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanExpectationRows.cs"],
            [
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/SyncPlan.cs",
                "src/QuickER.Provider/SyncPlanner.cs",
            ]
        ),
    ];
}
