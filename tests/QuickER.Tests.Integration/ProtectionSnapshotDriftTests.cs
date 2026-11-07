using System.IO;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration の守りの台帳（エンジンの台帳と単体の表の台帳）の写し（<c>protection-registry.json</c>）が、台帳と食い違わないことを固定する
/// </summary>
/// <remarks>
/// QuickER.Tests の影響の地図は、Integration の台帳をこの写しから読む（QuickER.Tests は Integration を参照できない）。
/// 台帳を変えて写しを書き直し忘れると、このテストが赤になる（Integration だけを流す ubuntu のジョブでも捕まる）。
/// 書き直しは生成フィクスチャと同じ環境変数で行い、続けて QuickER.Tests の地図を書き直す。
/// </remarks>
public sealed class ProtectionSnapshotDriftTests
{
    /// <summary>書き直しの手順（Integration の写し → QuickER.Tests の地図の順）</summary>
    private static readonly string RegenerateSteps = RegenerationRequest.Command(
        (
            "tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj",
            "ProtectionSnapshotDrift"
        ),
        ("tests/QuickER.Tests/QuickER.Tests.csproj", "ImpactMapDrift")
    );

    [Fact(
        DisplayName = "[台帳] Integration の守りの台帳の写しは、エンジンの台帳と単体の表の台帳から作り直した内容と同じ"
    )]
    public void Snapshot_MatchesRegistries() =>
        GeneratedTextFile.Verify(
            Path.Combine(EngineRepository.Root(), ProtectionSnapshot.IntegrationPath),
            ProtectionSnapshot.Render(EngineRegistry.All, TableRegistry.All),
            "Integration の守りの台帳の写し",
            RegenerateSteps
        );
}
