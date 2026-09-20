using System.IO;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// WPF テストの足場（XAML ロードの直列化）が迂回されていないことを、テストソース上で全数検査する。
/// </summary>
/// <remarks>
/// <para>
/// <c>MainWindow</c> の生成は BAML ロードを伴い、複数の STA テストクラスが同時に読むと
/// <c>XamlParseException: The given key '…' was not present in the dictionary.</c> で散発的に落ちる
/// （2026-09-20 実測＝対象 5 クラスを <c>--filter</c> で絞ると 6 回中 3〜4 回失敗）。
/// 直列化は <see cref="WpfApplicationTestSupport.CreateMainWindow"/>（内部で
/// <see cref="WpfApplicationTestSupport.LoadXamlComponent{T}(System.Func{T})"/>）が担う。
/// </para>
/// <para>
/// この種の規約は破っても全件実行では緑のままで、絞って回した開発者だけが理由のない赤を見る。
/// ソース上で検査しない限り、テストを 1 本足した瞬間に静かに戻る。
/// </para>
/// </remarks>
public class WpfTestScaffoldGuardTests
{
    /// <summary>テストプロジェクトのルート</summary>
    private static readonly string TestsRoot = Path.Combine(
        NeutralResxFiles.FindRepositoryRoot(),
        "tests",
        "QuickER.Tests"
    );

    /// <summary>直接生成が許されている唯一のファイル（直列化の窓口そのもの）</summary>
    private const string GateFileName = "WpfApplicationTestSupport.cs";

    [Fact(DisplayName = "テストの MainWindow 生成は必ず共有の窓口を通る")]
    public void MainWindowCreation_GoesThroughSharedGate()
    {
        var offenders = Directory
            .EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && Path.GetFileName(path) != GateFileName
            )
            // 検索語を分割するのは、このガード自身のソースが検出対象に写り込まないようにするため
            .Where(path => File.ReadAllText(path).Contains("new MainWindow" + "("))
            .Select(path => Path.GetRelativePath(TestsRoot, path))
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "MainWindow の生成は WpfApplicationTestSupport.CreateMainWindow / RunInIsolatedWindow "
                    + "を通すこと（BAML ロードの直列化を外すと --filter 実行で散発的に落ちる）"
            );
    }
}
