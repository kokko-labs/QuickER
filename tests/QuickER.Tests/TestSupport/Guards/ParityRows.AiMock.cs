using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：AiMock（モックプロジェクトの csproj へ埋め込むパッケージの版と Directory.Packages.props）</summary>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> AiMock() =>
        [
            new(
                "mock-project-package-ids",
                "版をアセンブリへ写すパッケージの一覧（MockProjectPackageVersions.PackageIds）は、生成する csproj が参照し得る"
                    + "パッケージ（テスト側で独立に列挙する＝一覧からの削除も検知する）と一致する",
                ParityCompare.Multiset,
                () => MockProjectPackageVersionsTests.ExpectedPackageIds,
                () => MockProjectPackageVersions.PackageIds
            ),
            new(
                "mock-project-package-versions",
                "生成する csproj が参照し得る各パッケージの版はアセンブリへ写されていて、版の正本 Directory.Packages.props と一致する"
                    + "（写しが無い・props から消えた・名前を間違えた、のいずれでも赤。項目はパッケージ=版）",
                ParityCompare.Sequence,
                () =>
                {
                    var central = MockProjectPackageVersionsTests.ReadCentralPackageVersions();

                    return
                    [
                        .. MockProjectPackageVersionsTests.ExpectedPackageIds.Select(id =>
                            central.TryGetValue(id, out var version)
                                ? $"{id}={version}"
                                : throw new InvalidOperationException(
                                    $"版の正本は Directory.Packages.props だが {id} の PackageVersion が無い"
                                )
                        ),
                    ];
                },
                () =>
                    [
                        .. MockProjectPackageVersionsTests.ExpectedPackageIds.Select(id =>
                            $"{id}={MockProjectPackageVersions.Get(id)}"
                        ),
                    ]
            ),
        ];
}
