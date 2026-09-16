using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="DotnetBuildRunner"/> が最終ビルドへ渡す引数を固定するガードテスト。
/// </summary>
/// <remarks>
/// ここで固定するのは「AI が書いたコードをビルド検証の瞬間に実行させない」ための引数群と、
/// ビルド対象の一意化（自分のソリューションを明示）。いずれも外しても <c>dotnet build</c> は成功するため、
/// ビルドでも型検査でも検出できず静かに回帰する。
/// </remarks>
public class DotnetBuildRunnerArgumentsTests
{
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

    /// <summary>
    /// 出力フォルダに別のソリューションがあっても、ビルド対象が自分のソリューションのパスで明示されることを検証する。
    /// </summary>
    [Fact(DisplayName = "ビルド対象は自分のソリューションのパスを明示する")]
    public void CreateBuildStartInfo_PassesSolutionPathExplicitly()
    {
        var folder = NewTempFolder();

        try
        {
            // 同じフォルダに別のソリューションがある状況（フォルダ指定だと MSB1011 で落ちる）
            File.WriteAllText(Path.Combine(folder, "Other.sln"), string.Empty);
            var solutionPath = Path.Combine(folder, "AcmeMock.sln");
            File.WriteAllText(solutionPath, string.Empty);

            var startInfo = DotnetBuildRunner.CreateBuildStartInfo(solutionPath);

            startInfo.FileName.Should().Be("dotnet");
            startInfo.ArgumentList.Should().HaveCountGreaterThan(1);
            startInfo.ArgumentList[0].Should().Be("build");
            // 対象はビルド動詞の直後に置く（フォルダ推測に委ねない）
            startInfo.ArgumentList[1].Should().Be(solutionPath);
            // 作業フォルダはソリューションの置き場
            startInfo.WorkingDirectory.Should().Be(folder);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// MSBuild の自動応答ファイル・常駐プロセス再利用を無効化する引数が入ることを検証する。
    /// </summary>
    /// <remarks>
    /// <c>-noAutoResponse</c> が無いと、出力フォルダの <c>Directory.Build.rsp</c> に書かれた
    /// <c>-logger:&lt;DLL&gt;</c> が採用され、任意アセンブリをビルドプロセスへ読み込ませられる（実測）。
    /// <c>-nodeReuse:false</c> / <c>-p:UseSharedCompilation=false</c> が無いと、AI が書いたタスク DLL・
    /// アナライザが常駐プロセスに残って次のビルドへ持ち越される。
    /// </remarks>
    [Fact(DisplayName = "自動応答ファイルとプロセス再利用を無効化する引数が入る")]
    public void CreateBuildStartInfo_ContainsIsolationArguments()
    {
        var folder = NewTempFolder();

        try
        {
            var solutionPath = Path.Combine(folder, "AcmeMock.sln");
            File.WriteAllText(solutionPath, string.Empty);

            var arguments = DotnetBuildRunner.CreateBuildStartInfo(solutionPath).ArgumentList;

            arguments.Should().Contain("-noAutoResponse");
            arguments.Should().Contain("-nodeReuse:false");
            arguments.Should().Contain("-p:UseSharedCompilation=false");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 祖先フォルダのビルドカスタマイズを継承しない 5 プロパティが入り続けることを検証する。
    /// </summary>
    [Fact(DisplayName = "自動 import を無効化する 5 プロパティが入る")]
    public void CreateBuildStartInfo_ContainsSelfContainedProperties()
    {
        var folder = NewTempFolder();

        try
        {
            var solutionPath = Path.Combine(folder, "AcmeMock.sln");
            File.WriteAllText(solutionPath, string.Empty);

            var arguments = DotnetBuildRunner.CreateBuildStartInfo(solutionPath).ArgumentList;

            arguments.Should().Contain("-p:ImportDirectoryBuildProps=false");
            arguments.Should().Contain("-p:ImportDirectoryBuildTargets=false");
            arguments.Should().Contain("-p:ImportDirectorySolutionProps=false");
            arguments.Should().Contain("-p:ImportDirectorySolutionTargets=false");
            arguments.Should().Contain("-p:ImportDirectoryPackagesProps=false");
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
