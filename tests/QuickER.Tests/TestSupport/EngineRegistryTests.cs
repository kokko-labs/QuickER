using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>QuickER.Tests のエンジンの台帳（<see cref="EngineRegistry"/>）の整合を固定する</summary>
public sealed class EngineRegistryTests
{
    [Fact(
        DisplayName = "[台帳] QuickER.Tests のエンジンの台帳は、知っている ID・一意な部分・実在するソースと対象の範囲を持つ"
    )]
    public void Registry_IsWellFormed() =>
        EngineRegistryChecks.Verify(EngineRegistry.All, EngineRepository.Root());
}
