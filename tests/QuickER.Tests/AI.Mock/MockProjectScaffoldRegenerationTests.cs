using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Platform;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 同じ出力フォルダへスキャフォールドし直すときの振る舞い（前回の <c>Generated/</c>・<c>design/mock/</c> を
/// 空にしてから書く・コピー元のモックフォルダと重なるときは何も消さずに止める）を検証するテストクラス。
/// </summary>
public class MockProjectScaffoldRegenerationTests
{
    private const string ProjectName = "AcmeMock";

    private const string ScreenHtml =
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + "<body><h1>Customers</h1></body></html>";

    private static DatabaseProviderRegistry BuildRegistry() =>
        new([new SqlServerProvider(), new SqliteProvider()]);

    private static string NewTempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>指定した画面ファイルを持つモックフォルダを作って返す</summary>
    private static string SeedMockFolder(params string[] screenFiles)
    {
        var folder = NewTempFolder();
        Directory.CreateDirectory(folder);
        var store = MockFolderStore.CreateNew(folder, ProjectName, "# schema");
        store.SaveStylesheet("body { font-family: sans-serif; }", "first");

        foreach (var file in screenFiles)
        {
            store.SaveScreen(
                file,
                file,
                "screen",
                ScreenHtml,
                Array.Empty<MockTransition>(),
                "first"
            );
        }

        return folder;
    }

    /// <summary>顧客テーブル 1 つの図を、指定方言で作る</summary>
    private static ErDiagram BuildDiagram(string targetDbms) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    private static MockProjectScaffoldResult Scaffold(
        string output,
        string mockFolder,
        string dialect = SqliteProvider.ProviderName
    ) =>
        new MockProjectScaffoldService(BuildRegistry()).Scaffold(
            BuildDiagram(dialect),
            output,
            ProjectName,
            mockFolder,
            MockProjectTargetProfile.Blazor
        );

    /// <summary>
    /// 前回の生成物の残骸（別方言の生成ファイル・削除した画面の HTML・読み取り専用のファイル）が、
    /// 再スキャフォールドで消えることを検証する（残ると型が二重定義になり、AI が無い画面まで再現する）。
    /// </summary>
    [Fact(DisplayName = "再スキャフォールドは前回の Generated/ と design/mock/ を空にしてから書く")]
    public void Rescaffold_ClearsPreviousGeneratedAndDesignFolders()
    {
        var output = NewTempFolder();
        var firstMock = SeedMockFolder("CustomerList.html", "OldScreen.html");
        var secondMock = SeedMockFolder("CustomerList.html");

        try
        {
            var first = Scaffold(output, firstMock, SqlServerProvider.ProviderName);
            var staleGenerated = Path.Combine(first.GeneratedDirectory, "Stale.g.cs");
            File.WriteAllText(staleGenerated, "// stale");
            File.SetAttributes(staleGenerated, FileAttributes.ReadOnly);
            File.Exists(Path.Combine(first.DesignFolderPath, "OldScreen.html")).Should().BeTrue();
            var sqlServerFiles = Directory
                .EnumerateFiles(
                    first.GeneratedDirectory,
                    "*SqlServer*",
                    SearchOption.AllDirectories
                )
                .ToList();
            sqlServerFiles.Should().NotBeEmpty("前提: 1 回目は SQL Server 方言のファイルを出す");

            var second = Scaffold(output, secondMock, SqliteProvider.ProviderName);

            File.Exists(staleGenerated).Should().BeFalse();
            sqlServerFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeFalse());
            File.Exists(Path.Combine(second.DesignFolderPath, "OldScreen.html")).Should().BeFalse();
            File.Exists(Path.Combine(second.DesignFolderPath, "CustomerList.html"))
                .Should()
                .BeTrue();
            Directory
                .EnumerateFiles(second.GeneratedDirectory, "*.cs", SearchOption.AllDirectories)
                .Should()
                .NotBeEmpty();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(firstMock);
            ReparsePointTestHelper.DeleteTree(secondMock);
        }
    }

    /// <summary>
    /// モックフォルダに残った原子的な書き込みの一時ファイル（書き込みの途中で落ちた残骸）は、<c>design/mock/</c> へ写さない
    /// </summary>
    [Fact(DisplayName = "モックフォルダに残った一時ファイルの残骸は design/mock/ へ写さない")]
    public void Scaffold_SkipsAtomicWriteLeftoversInMockFolder()
    {
        var output = NewTempFolder();
        var mock = SeedMockFolder("CustomerList.html");

        try
        {
            var leftover = Path.GetFileName(
                AtomicFile.TemporaryPathFor(Path.Combine(mock, "mock.json"))
            );
            File.WriteAllText(Path.Combine(mock, leftover), "{ half");

            var result = Scaffold(output, mock);

            File.Exists(Path.Combine(result.DesignFolderPath, leftover)).Should().BeFalse();
            File.Exists(Path.Combine(result.DesignFolderPath, "CustomerList.html"))
                .Should()
                .BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>
    /// <c>Generated/</c> がジャンクションのとき、リンクだけを外してリンク先の中身には触れないことを検証する。
    /// </summary>
    [Fact(DisplayName = "Generated/ がジャンクションでもリンク先の中身は消さない")]
    public void Rescaffold_GeneratedIsJunction_KeepsLinkTargetContents()
    {
        var output = NewTempFolder();
        var outside = NewTempFolder();
        var mock = SeedMockFolder("CustomerList.html");

        try
        {
            Directory.CreateDirectory(outside);
            var precious = Path.Combine(outside, "precious.txt");
            File.WriteAllText(precious, "keep me");
            ReparsePointTestHelper.CreateJunction(
                Path.Combine(output, ProjectName, MockProjectScaffoldService.GeneratedFolderName),
                outside
            );

            var result = Scaffold(output, mock);

            File.ReadAllText(precious).Should().Be("keep me");
            MockProjectFiles
                .IsLink(new DirectoryInfo(result.GeneratedDirectory))
                .Should()
                .BeFalse();
            Directory.EnumerateFiles(outside).Should().ContainSingle();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(outside);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>
    /// 前回の出力の <c>design/mock/</c> をそのままモックフォルダに指定すると、何も消さずに英語の例外で止まることを
    /// 検証する（先に消すと、コピー元＝利用者の作業の正本そのものが失われる）。
    /// </summary>
    [Fact(DisplayName = "出力先の design/mock/ をモックフォルダに指定すると、何も消さずに失敗する")]
    public void Rescaffold_MockFolderIsOutputDesignFolder_ThrowsWithoutDeleting()
    {
        var output = NewTempFolder();
        var mock = SeedMockFolder("CustomerList.html");

        try
        {
            var first = Scaffold(output, mock);
            var generatedFiles = Directory
                .EnumerateFiles(first.GeneratedDirectory, "*", SearchOption.AllDirectories)
                .ToList();
            var designFiles = Directory.EnumerateFiles(first.DesignFolderPath).ToList();

            // 大文字小文字だけ違う・区切りの付いた指定でも同じフォルダとして扱う
            var reused = first.DesignFolderPath.ToUpperInvariant() + Path.DirectorySeparatorChar;
            var act = () => Scaffold(output, reused);

            act.Should()
                .Throw<InvalidOperationException>()
                .Which.Message.Should()
                .MatchRegex(@"^[\x20-\x7E]+$", "英語固定の文言");
            generatedFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
            designFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>
    /// モックフォルダが <c>Generated/</c> の中にある、または空にするフォルダを含む位置（プロジェクトフォルダ）にあるときも
    /// 何も消さずに止まることを検証する。
    /// </summary>
    [Theory(DisplayName = "モックフォルダが空にするフォルダと入れ子なら止まる")]
    [InlineData("Generated/mock")]
    [InlineData("")]
    public void Rescaffold_MockFolderNestedWithOwnedFolder_Throws(string relativeUnderProject)
    {
        var output = NewTempFolder();
        var mock = SeedMockFolder("CustomerList.html");

        try
        {
            Scaffold(output, mock);
            var nested = Path.Combine(output, ProjectName, relativeUnderProject);
            Directory.CreateDirectory(nested);
            foreach (var file in Directory.EnumerateFiles(mock))
            {
                File.Copy(file, Path.Combine(nested, Path.GetFileName(file)), overwrite: true);
            }

            var before = Directory
                .EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .ToList();

            var act = () => Scaffold(output, nested);

            act.Should().Throw<InvalidOperationException>();
            before.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>
    /// ジャンクションを挟んで出力先の <c>design/mock/</c> を指したモックフォルダも、実体まで解決して重なりとみなし、
    /// 何も消さずに止まることを検証する。
    /// </summary>
    [Fact(DisplayName = "ジャンクション越しに design/mock/ を指すモックフォルダでも止まる")]
    public void Rescaffold_MockFolderViaJunction_Throws()
    {
        var output = NewTempFolder();
        var mock = SeedMockFolder("CustomerList.html");
        var aliasRoot = NewTempFolder();

        try
        {
            var first = Scaffold(output, mock);
            var designFiles = Directory.EnumerateFiles(first.DesignFolderPath).ToList();

            var alias = Path.Combine(aliasRoot, "alias");
            ReparsePointTestHelper.CreateJunction(alias, first.DesignFolderPath);

            var act = () => Scaffold(output, alias);

            act.Should().Throw<InvalidOperationException>();
            designFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(aliasRoot);
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>重ならない別のフォルダ同士は重なりとみなさないことを検証する（前方一致の取り違えも含めて）</summary>
    [Fact(DisplayName = "名前の前方が一致するだけの別フォルダは重ならない")]
    public void PathsOverlap_SiblingWithCommonPrefix_IsFalse()
    {
        var root = NewTempFolder();

        try
        {
            var design = Path.Combine(root, "design");
            var sibling = Path.Combine(root, "design-copy");
            Directory.CreateDirectory(design);
            Directory.CreateDirectory(sibling);

            MockProjectFiles.PathsOverlap(design, sibling).Should().BeFalse();
            MockProjectFiles.PathsOverlap(design, Path.Combine(design, "mock")).Should().BeTrue();
            MockProjectFiles.PathsOverlap(Path.Combine(design, "mock"), design).Should().BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(root);
        }
    }

    /// <summary>入力検証の例外文が英語固定であることを検証する（警告・検証エラーは英語固定の方針）</summary>
    [Fact(DisplayName = "スキャフォールドとターゲット解決の例外文は英語")]
    public void ValidationMessages_AreEnglish()
    {
        var service = new MockProjectScaffoldService(BuildRegistry());
        var invalidName = () =>
            service.Scaffold(
                BuildDiagram(SqliteProvider.ProviderName),
                NewTempFolder(),
                "bad:name",
                "unused",
                MockProjectTargetProfile.Blazor
            );
        var unknownTarget = () => MockProjectTargetProfile.Resolve(new MockProjectTarget("x", "X"));

        invalidName
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .MatchRegex(@"^[\x20-\x7E]+$");
        unknownTarget
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .MatchRegex(@"^[\x20-\x7E]+$");
    }
}
