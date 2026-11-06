using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>最終ビルドの境界の表：入口 E＝最終ビルドへ渡す引数</summary>
/// <remarks>
/// 固定するのは「AI が書いたコードをビルド検証の瞬間に実行させない」ための引数群と、ビルド対象の一意化（自分のソリューションを明示）。
/// どれも外しても <c>dotnet build</c> は成功するので、ビルドでも型検査でも気づけない（src/QuickER.AI.Mock/CLAUDE.md「最終ビルド」）。
/// </remarks>
internal static partial class MockBuildBoundaryRows
{
    /// <summary>入口 E：最終ビルドへ渡す引数（<c>DotnetBuildRunner.CreateBuildStartInfo</c>）</summary>
    private static IEnumerable<ScenarioRow<MockBuildBoundaryScenario>> Arguments() =>
        [
            SyncRow(
                "args-build-targets-own-solution-path",
                "出力フォルダに別のソリューションがあっても、ビルド対象は自分のソリューションのパスでビルド動詞の直後に明示し、作業フォルダはその置き場にする（フォルダ指定だと MSB1011 で落ちる）",
                BoundaryEntrance.BuildArguments,
                BoundaryEntryKind.Plain,
                c =>
                {
                    File.WriteAllText(Path.Combine(c.Folder, "Other.sln"), string.Empty);
                    var solutionPath = Path.Combine(c.Folder, $"{ProjectName}.sln");
                    File.WriteAllText(solutionPath, string.Empty);

                    var startInfo = DotnetBuildRunner.CreateBuildStartInfo(solutionPath);

                    startInfo.FileName.Should().Be("dotnet");
                    startInfo.ArgumentList.Should().HaveCountGreaterThan(1);
                    startInfo.ArgumentList[0].Should().Be("build");
                    startInfo.ArgumentList[1].Should().Be(solutionPath);
                    startInfo.WorkingDirectory.Should().Be(c.Folder);
                }
            ),
            SyncRow(
                "args-isolation-arguments-are-passed",
                "自動応答ファイルと常駐プロセスの再利用を無効にする引数が入る（-noAutoResponse が無いと出力フォルダの Directory.Build.rsp の -logger:<DLL> が採用され、"
                    + "-nodeReuse:false・-p:UseSharedCompilation=false が無いと AI が書いたタスク DLL・アナライザが次のビルドへ持ち越される）",
                BoundaryEntrance.BuildArguments,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var arguments = DotnetBuildRunner
                        .CreateBuildStartInfo(SolutionIn(c))
                        .ArgumentList;

                    arguments.Should().Contain("-noAutoResponse");
                    arguments.Should().Contain("-nodeReuse:false");
                    arguments.Should().Contain("-p:UseSharedCompilation=false");
                }
            ),
            SyncRow(
                "args-ancestor-imports-are-disabled",
                "祖先フォルダのビルドのカスタマイズを継承しない 5 つのプロパティが入る",
                BoundaryEntrance.BuildArguments,
                BoundaryEntryKind.Plain,
                c =>
                {
                    var arguments = DotnetBuildRunner
                        .CreateBuildStartInfo(SolutionIn(c))
                        .ArgumentList;

                    arguments.Should().Contain("-p:ImportDirectoryBuildProps=false");
                    arguments.Should().Contain("-p:ImportDirectoryBuildTargets=false");
                    arguments.Should().Contain("-p:ImportDirectorySolutionProps=false");
                    arguments.Should().Contain("-p:ImportDirectorySolutionTargets=false");
                    arguments.Should().Contain("-p:ImportDirectoryPackagesProps=false");
                }
            ),
        ];

    /// <summary>出力フォルダに空のソリューションを置いてそのパスを返す</summary>
    private static string SolutionIn(MockBuildBoundaryContext c)
    {
        var solutionPath = Path.Combine(c.Folder, $"{ProjectName}.sln");
        File.WriteAllText(solutionPath, string.Empty);
        return solutionPath;
    }
}
