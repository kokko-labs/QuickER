using System.IO;
using System.Xml.Linq;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Gui.Views;

/// <summary>
/// src の XAML の <c>DataGrid</c> が、利用者の行の追加・削除（<c>CanUserAddRows</c> / <c>CanUserDeleteRows</c>）を
/// 明示で切っていることを全数検査するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// DataGrid は既定で行の追加と削除を受け付け、Delete キーや末尾の新規行が ItemsSource のコレクションを直接書き換える。
/// QuickER の DataGrid は正本の ViewModel のコレクションを ItemsSource にするので、既定のままにすると、
/// 変更が履歴にも未保存の判定にも残らず（外部変更の自動再読込で確認なしに失われる）、関連する制約の後始末も通らない。
/// 削除・追加はコマンドを通す（列一覧の Delete は <c>RemoveColumnCommand</c> へ結ぶ＝<see cref="ColumnGridDeleteKeyTests"/>）。
/// </para>
/// <para>
/// 既定値に頼る書き方を許さないため、属性が無い場合も赤にする（DataGrid を 1 つ足しただけで既定の挙動が戻る）。
/// </para>
/// </remarks>
public class DataGridRowEditGuardTests
{
    /// <summary>src のルート</summary>
    private static readonly string SourceRoot = Path.Combine(
        NeutralResxFiles.FindRepositoryRoot(),
        "src"
    );

    [Fact(DisplayName = "src の XAML の DataGrid は、行の追加と削除を明示で切っている")]
    public void EveryDataGrid_DisablesUserRowAddAndDelete()
    {
        var dataGrids = Directory
            .EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .SelectMany(path =>
                XDocument
                    .Load(path, LoadOptions.SetLineInfo)
                    .Descendants()
                    .Where(element => element.Name.LocalName == "DataGrid")
                    .Select(element => (Path: Path.GetRelativePath(SourceRoot, path), element))
            )
            .ToList();

        dataGrids.Should().NotBeEmpty("走査が DataGrid を 1 つも見つけないのは走査の誤り");

        var offenders = dataGrids
            .Where(item =>
                (string?)item.element.Attribute("CanUserAddRows") != "False"
                || (string?)item.element.Attribute("CanUserDeleteRows") != "False"
            )
            .Select(item =>
                $"{item.Path}:{((System.Xml.IXmlLineInfo)item.element).LineNumber} "
                + $"(x:Name={(string?)item.element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")})"
            )
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "DataGrid は CanUserAddRows=\"False\" と CanUserDeleteRows=\"False\" を明示し、行の追加・削除はコマンドを通すこと"
            );
    }
}
