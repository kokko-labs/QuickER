using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectScaffoldService"/> の、スキャフォールドの表（<c>MockScaffoldTests</c>）の行にしないものを検証するテストクラス
/// （プロジェクト名で断るフォルダの名前と、API キー方式の提出が断るフォルダの名前の 2 つの一覧の一致＝構造のガード）。
/// </summary>
/// <remarks>
/// スキャフォールドの構成物・方言 × ターゲット・名前空間の案内・プロジェクト名の入口・作り直しは <c>MockScaffoldTests</c> の行、
/// プロジェクト名の形の判定は OS との境界の表（<c>PathSegmentEntranceFuzzTests</c> の入口 ProjectName）が見る。
/// </remarks>
public class MockProjectScaffoldServiceTests
{
    /// <summary>
    /// プロジェクト名で断るフォルダの名前は、API キー方式の提出が断る名前と同じ一覧から作る（写しを持たない）。
    /// その名前のプロジェクトフォルダへの提出は必ず断られるので、生成を始める前に断る理由がある
    /// </summary>
    [Fact(
        DisplayName = "プロジェクト名で断るフォルダの名前は、プロジェクトフォルダにすると提出がすべて断られる名前と同じ"
    )]
    public void ProtectedFolderNames_AreExactlyTheNamesThatBlockEverySubmission()
    {
        MockProjectEmitTools
            .ProtectedFolderNames.Should()
            .BeEquivalentTo(
                MockProjectScaffoldService.GeneratedFolderName,
                MockProjectScaffoldService.DesignRootFolderName,
                "obj",
                "bin"
            );

        foreach (var name in MockProjectEmitTools.ProtectedFolderNames)
        {
            MockProjectScaffoldService.IsValidProjectName(name).Should().BeFalse(name);
            MockProjectEmitTools
                .ResolveEmitPath(
                    Path.GetTempPath(),
                    MockProjectTargetProfile.Blazor,
                    $"{name}/Components/Pages/Home.razor"
                )
                .Ok.Should()
                .BeFalse(name);
        }
    }
}
