using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 出力フォルダの上書き走査 <see cref="MockProjectOverwriteScanner"/> を検証するテストクラス。
/// </summary>
/// <remarks>
/// スキャフォールドはソリューション・csproj・README-QuickER.md・Generated/・design/mock/ を無条件に
/// 上書きする。何が上書きされるかを列挙できないと、確認ダイアログが判断材料にならない。
/// </remarks>
public class MockProjectOverwriteScannerTests
{
    private const string ProjectName = "AcmeMock";

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    /// <summary>空フォルダ・存在しないフォルダでは確認が要らないことを検証する</summary>
    [Fact(DisplayName = "空フォルダ・未作成フォルダでは確認しない")]
    public void Scan_EmptyOrMissing_RequiresNoConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            MockProjectOverwriteScanner
                .Scan(folder, ProjectName)
                .RequiresConfirmation.Should()
                .BeFalse();
            MockProjectOverwriteScanner
                .Scan(Path.Combine(folder, "missing"), ProjectName)
                .RequiresConfirmation.Should()
                .BeFalse();
            MockProjectOverwriteScanner
                .Scan(string.Empty, ProjectName)
                .RequiresConfirmation.Should()
                .BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>同一プロジェクト名での再生成では、上書きされる既存物がすべて列挙されることを検証する</summary>
    [Fact(DisplayName = "同名で再生成すると上書き対象を列挙する")]
    public void Scan_SameProjectName_ListsOverwrittenPaths()
    {
        var folder = NewTempFolder();

        try
        {
            var project = Path.Combine(folder, ProjectName);
            Write(Path.Combine(folder, $"{ProjectName}.sln"));
            Write(Path.Combine(project, $"{ProjectName}.csproj"));
            Write(Path.Combine(project, MockProjectScaffoldService.ReadmeFileName));
            Write(Path.Combine(project, "Generated", "Entities.g.cs"));
            Write(Path.Combine(project, "design", "mock", "mock.json"));
            // 上書き対象ではない手書きコード（列挙しない）
            Write(Path.Combine(project, "Program.cs"));

            var scan = MockProjectOverwriteScanner.Scan(folder, ProjectName);

            scan.RequiresConfirmation.Should().BeTrue();
            scan.ExistingPaths.Should()
                .Equal(
                    $"{ProjectName}.sln",
                    ProjectName + Path.DirectorySeparatorChar,
                    Path.Combine(ProjectName, $"{ProjectName}.csproj"),
                    Path.Combine(ProjectName, MockProjectScaffoldService.ReadmeFileName),
                    Path.Combine(ProjectName, "Generated") + Path.DirectorySeparatorChar,
                    Path.Combine(ProjectName, "design", "mock") + Path.DirectorySeparatorChar
                );
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>出力フォルダ直下の別ソリューション・別プロジェクトも確認の対象になることを検証する</summary>
    [Fact(DisplayName = "直下の別ソリューション・別プロジェクトも列挙する")]
    public void Scan_ForeignSolutionsAtRoot_AreListed()
    {
        var folder = NewTempFolder();

        try
        {
            Write(Path.Combine(folder, "Other.sln"));
            Write(Path.Combine(folder, "Legacy.slnx"));
            Write(Path.Combine(folder, "Library.csproj"));
            // 直下でないものは対象外（同居の判断材料にならない）
            Write(Path.Combine(folder, "nested", "Deep.csproj"));

            var scan = MockProjectOverwriteScanner.Scan(folder, ProjectName);

            scan.RequiresConfirmation.Should().BeTrue();
            scan.ExistingPaths.Should().Equal("Legacy.slnx", "Library.csproj", "Other.sln");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロジェクトフォルダが空でも「そこへ書き込む」ことは確認の対象になることを検証する</summary>
    [Fact(DisplayName = "プロジェクトフォルダが既にあれば中身が空でも確認する")]
    public void Scan_ExistingEmptyProjectFolder_RequiresConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, ProjectName));

            var scan = MockProjectOverwriteScanner.Scan(folder, ProjectName);

            scan.RequiresConfirmation.Should().BeTrue();
            scan.ExistingPaths.Should().Equal(ProjectName + Path.DirectorySeparatorChar);
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
