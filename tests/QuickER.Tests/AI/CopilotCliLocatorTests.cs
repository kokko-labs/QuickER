using System.IO;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.AI;

/// <summary>
/// copilot CLI の PATH 走査（<see cref="PathExecutableResolver"/> の非 Windows の分岐）を検証するテストクラス。
/// 実 PATH を書き換えるとプロセス全体（＝並列テスト）へ影響するため、走査対象の PATH 値と
/// ファイル存在判定を注入する internal オーバーロードで検証する。
/// </summary>
/// <remarks>
/// 残したテストは非 Windows の分岐（<c>OperatingSystem.IsWindows()</c> に差し込み点が無い）。
/// ほかは <c>Platform/OsBoundary/ProcessLaunchRows*.cs</c> へ移した。
/// </remarks>
public class CopilotCliLocatorTests
{
    /// <summary>走査対象に使う架空のディレクトリ（実在は不要＝存在判定は注入する）</summary>
    private static string FakeBinDirectory =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", "copilot-bin");

    /// <summary>指定した実在パス集合に対する存在判定を作る（Windows に合わせて大文字小文字は無視する）</summary>
    private static Func<string, bool> Existing(params string[] paths) =>
        candidate => paths.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Windows 以外では拡張子なしの名前が唯一の候補で、引用符は名前の一部として扱うことを検証する
    /// （Unix の PATH に引用の規則は無い）。
    /// </summary>
    [Fact(DisplayName = "Windows 以外では拡張子なしの名前を候補にし引用符を外さない")]
    public void PathExecutableResolver_NonWindows_UsesBareNameAndKeepsQuotes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var expected = Path.Combine(FakeBinDirectory, "copilot");

        PathExecutableResolver
            .Resolve("copilot", FakeBinDirectory, Existing(expected))
            .Should()
            .Be(expected);
        PathExecutableResolver
            .Resolve("copilot", "\"" + FakeBinDirectory + "\"", Existing(expected))
            .Should()
            .BeNull();
    }
}
