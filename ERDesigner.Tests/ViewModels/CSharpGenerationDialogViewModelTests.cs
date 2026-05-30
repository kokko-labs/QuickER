using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="CSharpGenerationDialogViewModel" /> の入力確定と検証を確認します。
/// </summary>
public class CSharpGenerationDialogViewModelTests
{
    [Fact(DisplayName = "OK 実行で namespace と出力先が結果へ反映される")]
    public void Ok_SetsResult()
    {
        var vm = new CSharpGenerationDialogViewModel("Sample.Domain", @"C:\temp\Entities.g.cs");
        bool? closed = null;
        vm.CloseAction = result => closed = result;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.NamespaceName.Should().Be("Sample.Domain");
        vm.Result.OutputFilePath.Should().Be(@"C:\temp\Entities.g.cs");
        closed.Should().BeTrue();
    }

    [Fact(DisplayName = "不正な namespace ではエラーメッセージを表示して閉じない")]
    public void Ok_WithInvalidNamespace_ShowsError()
    {
        var vm = new CSharpGenerationDialogViewModel("1Invalid.Namespace", @"C:\temp\Entities.g.cs");
        bool closed = false;
        vm.CloseAction = _ => closed = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be("namespace の形式が正しくありません。");
        closed.Should().BeFalse();
    }

    [Fact(DisplayName = "参照コマンドで出力先ファイルを更新できる")]
    public void BrowseOutputFile_UpdatesPath()
    {
        var vm = new CSharpGenerationDialogViewModel("Sample.Domain", "ErDesignerEntities.g.cs") { BrowseOutputFileAction = _ => @"C:\work\Generated\Entities.g.cs" };

        vm.BrowseOutputFileCommand.Execute(null);

        vm.OutputFilePath.Should().Be(@"C:\work\Generated\Entities.g.cs");
    }
}
