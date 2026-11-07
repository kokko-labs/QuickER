using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 影響の地図の照合（<see cref="ImpactMapGuard"/>）を、リポジトリの宣言そのものに当てる：src のすべての単位が守りか理由つきの守りの
/// 無い領域のちょうど一方にあり、決まった形の行の表がどれも地図の守りに登録されている
/// </summary>
/// <remarks>
/// 守りの無い領域の宣言は <see cref="ImpactMapDeclarations"/>。照合の検査そのものの自己検査は <see cref="ImpactMapTests"/>。
/// </remarks>
public sealed class ImpactMapGuardTests
{
    private readonly ITestOutputHelper _output;

    public ImpactMapGuardTests(ITestOutputHelper output) => _output = output;

    [Fact(
        DisplayName = "[地図] src のすべての単位は、守りか理由つきの守りの無い領域のちょうど一方にある"
    )]
    public void EveryUnit_IsProtectedOrDeclaredUnprotected()
    {
        var root = EngineRepository.Root();
        var map = ImpactMapSources.Build(root);

        // 守りの無い領域の手書きのテストの数は、地図に入れない（足すたびに地図が変わる）代わりに出力へ書く
        foreach (var (unit, files) in ImpactMapSources.HandwrittenTestFiles(map, root))
        {
            _output.WriteLine($"{unit}: エンジン・表の外のテスト {files.Count} ファイル");
        }

        ImpactMapGuard.Verify(map, root);
    }

    [Fact(
        DisplayName = "[地図] 決まった形の行の表を使うテストのソースは、エンジンの部分か単体の表に登録されている"
    )]
    public void ScenarioTableSources_AreRegistered()
    {
        var root = EngineRepository.Root();
        var unregistered = ImpactMapSources.UnregisteredScenarioTableSources(
            ImpactMapSources.Protections(root),
            root
        );

        if (unregistered.Count > 0)
        {
            throw new XunitException(
                "決まった形の行の表を使うのに、エンジンの部分のソースにも単体の表の台帳（TableRegistry）にも無い（登録しないと表の守りの範囲が地図から漏れる）:\n  "
                    + string.Join("\n  ", unregistered)
            );
        }

        var sources = ImpactMapSources.ScenarioTableSources(root);

        ImpactMapSources
            .ScenarioTableExemptions.Select(exemption => exemption.Path)
            .Where(path => !sources.Contains(path, StringComparer.Ordinal))
            .Should()
            .BeEmpty(
                "登録しなくてよいソースの一覧の項目は、表を使い続けていること（使わなくなったら一覧から消す）"
            );
    }
}
