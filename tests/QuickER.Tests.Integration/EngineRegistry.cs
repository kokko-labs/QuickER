using QuickER.Tests.Integration.Dialects;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>Integration にあるテストのエンジンの台帳（エンジンの部分ごとの次元表・シナリオの数・テストのソース・対象の範囲）</summary>
internal static class EngineRegistry
{
    /// <summary>台帳の行（エンジンの ID と部分の組は一意）</summary>
    public static IReadOnlyList<EngineDescriptor> All { get; } =
    [
        SyncModelDimensions.Descriptor,
        SyncModelOutcomeDimensions.Descriptor,
        SyncConfigDimensions.Descriptor,
        SyncPlanFuzzDimensions.Descriptor,
        BackendParityDimensions.Descriptor,
        DslFuzzDimensions.SqlServerDescriptor,
        NameFuzzDdlRoundTripDimensions.Descriptor,
    ];
}
