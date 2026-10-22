using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// キーボードフォーカス・流したキー入力・ウィンドウのアクティブ状態に結果が左右されるテストクラスが、
/// <see cref="WindowActivationTestCollection"/> に入っていることを、テストソース上で全数検査する。
/// </summary>
/// <remarks>
/// <para>
/// ほかのテストクラスがウィンドウを表示するとアクティブが移り、キーボードフォーカスが入らない・
/// Space の判断が変わる、といった形で結果が変わる。並列で流す全件実行でだけ時々落ち、単独では合格するので、
/// 落ちたテストを見ても原因に気付けない（実際に 3 クラスで観測した）。
/// </para>
/// <para>
/// 検出の目印は、そうした依存を表す API の名前（<see cref="Markers"/>）。コメントでの言及も数えるが、
/// 言及するクラスは並列から外しても失うのは実行時間だけなので、取りこぼさない側へ倒す。
/// </para>
/// </remarks>
public class WindowActivationCollectionGuardTests
{
    /// <summary>テストプロジェクトのルート</summary>
    private static readonly string TestsRoot = Path.Combine(
        NeutralResxFiles.FindRepositoryRoot(),
        "tests",
        "QuickER.Tests"
    );

    /// <summary>
    /// フォーカス・キー入力・アクティブ状態への依存を表す目印（このガード自身が写り込まないよう分割して書く）
    /// </summary>
    private static readonly string[] Markers =
    [
        "IsKeyboard" + "Focus",
        "Keyboard." + "PrimaryDevice",
        "Keyboard." + "Focus(",
        "Move" + "Focus(",
        "Deac" + "tivated",
    ];

    /// <summary>コレクションへの所属を表す属性</summary>
    private static readonly Regex CollectionAttribute = new(
        @"\[Collection\((TestSupport\.)?WindowActivationTestCollection\.Name\)\]"
    );

    /// <summary>
    /// 目印を持っても検査しないソース。
    /// コレクションの定義そのものと、失敗した瞬間の状態の記録（<see cref="GuiFailureDiagnostics"/>＝
    /// アクティブ・フォーカスのイベントを受け身で購読して記録するだけで、テストを持たず結果も変えない）
    /// </summary>
    private static readonly string[] ExcludedFileNames =
    [
        "WindowActivationTestCollection.cs",
        "GuiFailureDiagnostics.cs",
    ];

    /// <summary>目印を持つテストソース（<see cref="ExcludedFileNames"/> を除く）</summary>
    private static List<(string RelativePath, string Text)> FindDependentSources() =>
        Directory
            .EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !ExcludedFileNames.Contains(Path.GetFileName(path))
            )
            .Select(path => (Path.GetRelativePath(TestsRoot, path), File.ReadAllText(path)))
            .Where(source => Markers.Any(marker => source.Item2.Contains(marker)))
            .ToList();

    [Fact(
        DisplayName = "フォーカス・キー入力・アクティブ状態に頼るテストクラスは WindowActivation コレクションに入っている"
    )]
    public void FocusDependentTests_AreInWindowActivationCollection()
    {
        var offenders = FindDependentSources()
            .Where(source => !CollectionAttribute.IsMatch(source.Text))
            .Select(source => source.RelativePath)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "キーボードフォーカス・流したキー入力・Activated / Deactivated に結果が左右されるテストクラスには "
                    + "[Collection(TestSupport.WindowActivationTestCollection.Name)] を付けること"
                    + "（並列で流すと、ほかのテストのウィンドウにアクティブを奪われて時々落ちる）"
            );
    }

    [Fact(DisplayName = "ガードの目印は既知のフォーカス依存テストを検出できる")]
    public void Markers_DetectKnownDependentTests()
    {
        // 目印の綴りが崩れて何も検出しなくなると、上のガードは黙って緑になる
        FindDependentSources()
            .Select(source => Path.GetFileName(source.RelativePath))
            .Should()
            .Contain([
                "CanvasViewportBehaviorSpaceKeyTests.cs",
                "PropertyPanelMemoEditTests.cs",
                "AiChatDialogApiKeyBoxTests.cs",
            ]);
    }
}
