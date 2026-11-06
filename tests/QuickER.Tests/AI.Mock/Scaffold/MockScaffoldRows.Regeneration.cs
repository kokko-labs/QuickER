using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Scaffold;

/// <summary>スキャフォールドの表：プロジェクト名の入口と、同じ出力フォルダへの作り直し</summary>
internal static partial class MockScaffoldRows
{
    /// <summary>
    /// スキャフォールドの入口で断るプロジェクト名（行の名前・名前）。名前の形の判定そのものは OS との境界の表
    /// （PathSegmentEntranceFuzzTests の入口 ProjectName）が見る。ここでは、スキャフォールドが ArgumentException で断り、
    /// 出力フォルダを作らない（検証は副作用の前で完結する）ことを見る
    /// </summary>
    private static readonly (string Name, string? Value)[] RejectedProjectNames =
    [
        ("parent-traversal-backslash", "..\\outside"),
        ("parent-traversal-slash", "../outside"),
        ("dot-dot", ".."),
        ("dot", "."),
        ("absolute-drive", "C:\\Windows"),
        ("separator-slash", "sub/dir"),
        ("separator-backslash", "sub\\dir"),
        ("colon", "bad:name"),
        ("asterisk", "bad*name"),
        // 生成するプロジェクトが自分用に使うフォルダの名前（API キー方式の提出がすべて断られる）
        ("reserved-design", "Design"),
        ("reserved-obj", "obj"),
        // 空の名前（IsValidProjectName が空文字・null を断る）
        ("empty", ""),
        ("null", null),
    ];

    /// <summary>プロジェクト名の入口</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> ProjectNames() =>
        RejectedProjectNames.Select(name =>
            Row(
                $"project-name-rejected-before-output-is-created-{name.Name}",
                "不正なプロジェクト名（トラバーサル・絶対パス・区切り・使えない文字・予約のフォルダ名・空）はスキャフォールドの入口で ArgumentException になり、出力フォルダを作らない（出力フォルダの外への書き込みを防ぐ）",
                Wpf,
                SqlServer,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var folder = c.NewPath("out");
                    var act = () =>
                        c.Service.Scaffold(
                            MockScaffoldContext.Diagram(s.Dialect, withNameColumn: true),
                            folder,
                            name.Value!,
                            // 検証は Scaffold の入口で完結する（モックフォルダの実在は要らない）
                            "unused-mock-folder",
                            s.Target.Profile
                        );

                    act.Should().Throw<ArgumentException>();
                    Directory.Exists(folder).Should().BeFalse();
                }
            )
        );

    /// <summary>同じ出力フォルダへの作り直し（前回の Generated/・design/mock/ を空にしてから書く・コピー元と重なるときは何も消さずに止める）</summary>
    private static IEnumerable<ScenarioRow<MockScaffoldScenario>> Regeneration() =>
        [
            Row(
                "rescaffold-clears-previous-generated-and-design",
                "作り直しは前回の生成物の残骸（別の方言の生成ファイル・削除した画面の HTML・読み取り専用のファイル）を消してから書く（残ると型が二重定義になり、AI が無い画面まで再現する）",
                Blazor,
                Sqlite,
                ScaffoldExisting.PreviousOutput,
                (c, s) =>
                {
                    var output = c.NewPath("out");
                    var first = c.Scaffold(
                        s,
                        output,
                        c.SeedMock("mock1", "CustomerList.html", "OldScreen.html"),
                        dialect: SqlServer
                    );
                    var staleGenerated = Path.Combine(first.GeneratedDirectory, "Stale.g.cs");
                    File.WriteAllText(staleGenerated, "// stale");
                    File.SetAttributes(staleGenerated, FileAttributes.ReadOnly);
                    File.Exists(Path.Combine(first.DesignFolderPath, "OldScreen.html"))
                        .Should()
                        .BeTrue();
                    var sqlServerFiles = Directory
                        .EnumerateFiles(
                            first.GeneratedDirectory,
                            "*SqlServer*",
                            SearchOption.AllDirectories
                        )
                        .ToList();
                    sqlServerFiles
                        .Should()
                        .NotBeEmpty("前提: 1 回目は SQL Server の方言のファイルを出す");

                    var second = c.Scaffold(s, output, c.SeedMock("mock2", "CustomerList.html"));

                    File.Exists(staleGenerated).Should().BeFalse();
                    sqlServerFiles
                        .Should()
                        .AllSatisfy(path => File.Exists(path).Should().BeFalse());
                    File.Exists(Path.Combine(second.DesignFolderPath, "OldScreen.html"))
                        .Should()
                        .BeFalse();
                    File.Exists(Path.Combine(second.DesignFolderPath, "CustomerList.html"))
                        .Should()
                        .BeTrue();
                    Directory
                        .EnumerateFiles(
                            second.GeneratedDirectory,
                            "*.cs",
                            SearchOption.AllDirectories
                        )
                        .Should()
                        .NotBeEmpty();
                }
            ),
            Row(
                "scaffold-skips-atomic-write-leftovers-in-mock-folder",
                "モックフォルダに残った原子的な書き込みの一時ファイル（書き込みの途中で落ちた残骸）は design/mock/ へ写さない",
                Blazor,
                Sqlite,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var mock = c.SeedMock("mock", "CustomerList.html");
                    var leftover = Path.GetFileName(
                        AtomicFile.TemporaryPathFor(Path.Combine(mock, "mock.json"))
                    );
                    File.WriteAllText(Path.Combine(mock, leftover), "{ half");

                    var result = c.Scaffold(s, c.NewPath("out"), mock);

                    File.Exists(Path.Combine(result.DesignFolderPath, leftover)).Should().BeFalse();
                    File.Exists(Path.Combine(result.DesignFolderPath, "CustomerList.html"))
                        .Should()
                        .BeTrue();
                }
            ),
            Row(
                "rescaffold-generated-junction-keeps-link-target",
                "Generated/ がジャンクションのとき、リンクだけを外してリンク先の中身には触れない",
                Blazor,
                Sqlite,
                ScaffoldExisting.GeneratedJunction,
                (c, s) =>
                {
                    var output = c.NewPath("out");
                    var outside = c.NewFolder("outside");
                    var mock = c.SeedMock("mock", "CustomerList.html");
                    var precious = Path.Combine(outside, "precious.txt");
                    File.WriteAllText(precious, "keep me");
                    ReparsePointTestHelper.CreateJunction(
                        Path.Combine(
                            output,
                            MockScaffoldContext.ProjectName,
                            MockProjectScaffoldService.GeneratedFolderName
                        ),
                        outside
                    );

                    var result = c.Scaffold(s, output, mock);

                    File.ReadAllText(precious).Should().Be("keep me");
                    MockProjectFiles
                        .IsLink(new DirectoryInfo(result.GeneratedDirectory))
                        .Should()
                        .BeFalse();
                    Directory.EnumerateFiles(outside).Should().ContainSingle();
                }
            ),
            Row(
                "rescaffold-mock-folder-is-output-design-throws-without-deleting",
                "前回の出力の design/mock/ をそのままモックフォルダに指定すると（大文字小文字だけ違う・区切りの付いた指定でも）、何も消さずに英語の例外で止まる（先に消すとコピー元＝利用者の作業の正本が失われる）",
                Blazor,
                Sqlite,
                ScaffoldExisting.MockOverlapsOutput,
                (c, s) =>
                {
                    var output = c.NewPath("out");
                    var first = c.Scaffold(s, output, c.SeedMock("mock", "CustomerList.html"));
                    var generatedFiles = Directory
                        .EnumerateFiles(first.GeneratedDirectory, "*", SearchOption.AllDirectories)
                        .ToList();
                    var designFiles = Directory.EnumerateFiles(first.DesignFolderPath).ToList();
                    var reused =
                        first.DesignFolderPath.ToUpperInvariant() + Path.DirectorySeparatorChar;

                    var act = () => c.Scaffold(s, output, reused);

                    act.Should()
                        .Throw<InvalidOperationException>()
                        .Which.Message.Should()
                        .MatchRegex(@"^[\x20-\x7E]+$", "英語固定の文言");
                    generatedFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
                    designFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
                }
            ),
            Row(
                "rescaffold-mock-folder-nested-in-generated-throws",
                "モックフォルダが空にするフォルダ（Generated/）の中にあるときも、何も消さずに止まる",
                Blazor,
                Sqlite,
                ScaffoldExisting.MockOverlapsOutput,
                (c, s) => MockFolderNestedWithOwnedFolderThrows(c, s, "Generated/mock")
            ),
            Row(
                "rescaffold-mock-folder-is-project-folder-throws",
                "モックフォルダが空にするフォルダを含む位置（プロジェクトフォルダ）にあるときも、何も消さずに止まる",
                Blazor,
                Sqlite,
                ScaffoldExisting.MockOverlapsOutput,
                (c, s) => MockFolderNestedWithOwnedFolderThrows(c, s, "")
            ),
            Row(
                "rescaffold-mock-folder-via-junction-throws",
                "ジャンクションを挟んで出力先の design/mock/ を指したモックフォルダも、実体まで解決して重なりとみなし、何も消さずに止まる",
                Blazor,
                Sqlite,
                ScaffoldExisting.MockOverlapsOutput,
                (c, s) =>
                {
                    var output = c.NewPath("out");
                    var first = c.Scaffold(s, output, c.SeedMock("mock", "CustomerList.html"));
                    var designFiles = Directory.EnumerateFiles(first.DesignFolderPath).ToList();
                    var alias = Path.Combine(c.NewFolder("alias-root"), "alias");
                    ReparsePointTestHelper.CreateJunction(alias, first.DesignFolderPath);

                    var act = () => c.Scaffold(s, output, alias);

                    act.Should().Throw<InvalidOperationException>();
                    designFiles.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
                }
            ),
            Row(
                "validation-messages-are-english",
                "スキャフォールドとターゲットの解決の入力検証の例外文は英語固定（印字可能な ASCII だけ）",
                Blazor,
                Sqlite,
                ScaffoldExisting.None,
                (c, s) =>
                {
                    var invalidName = () =>
                        c.Service.Scaffold(
                            MockScaffoldContext.Diagram(s.Dialect),
                            c.NewPath("out"),
                            "bad:name",
                            "unused",
                            s.Target.Profile
                        );
                    var unknownTarget = () =>
                        MockProjectTargetProfile.Resolve(new MockProjectTarget("x", "X"));

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
            ),
        ];

    /// <summary>モックフォルダが空にするフォルダと入れ子なら、何も消さずに止まる</summary>
    /// <param name="c">文脈</param>
    /// <param name="s">行の入力</param>
    /// <param name="relativeUnderProject">モックフォルダを置く、プロジェクトフォルダからの相対パス（空ならプロジェクトフォルダそのもの）</param>
    private static void MockFolderNestedWithOwnedFolderThrows(
        MockScaffoldContext c,
        MockScaffoldScenario s,
        string relativeUnderProject
    )
    {
        var output = c.NewPath("out");
        var mock = c.SeedMock("mock", "CustomerList.html");
        c.Scaffold(s, output, mock);
        var nested = Path.Combine(output, MockScaffoldContext.ProjectName, relativeUnderProject);
        Directory.CreateDirectory(nested);

        foreach (var file in Directory.EnumerateFiles(mock))
        {
            File.Copy(file, Path.Combine(nested, Path.GetFileName(file)), overwrite: true);
        }

        var before = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToList();

        var act = () => c.Scaffold(s, output, nested);

        act.Should().Throw<InvalidOperationException>();
        before.Should().AllSatisfy(path => File.Exists(path).Should().BeTrue());
    }
}
