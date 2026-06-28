using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary><see cref="CSharpGenerationDialogViewModel" /> の入力確定・検証・参照・分割・永続化を検証するテストクラス</summary>
public class CSharpGenerationDialogViewModelTests
{
    /// <summary>一時フォルダのストアで ViewModel を生成する（実 %APPDATA% を汚さない）</summary>
    private static CSharpGenerationDialogViewModel CreateViewModel(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return new CSharpGenerationDialogViewModel(new CSharpGenerationSettingsStore(folder));
    }

    /// <summary>OK 実行で namespace・出力先・生成オプションが結果へ反映され閉じることを検証する</summary>
    [Fact(DisplayName = "OK 実行で namespace と出力先が結果へ反映される")]
    public void Ok_SetsResult()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Sample.Domain";
        vm.OutputFilePath = @"C:\temp\Entities.g.cs";
        bool? closed = null;
        vm.CloseAction = result => closed = result;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.NamespaceName.Should().Be("Sample.Domain");
        vm.Result.Options.SplitFilesByCategory.Should().BeFalse();
        vm.Result.OutputDirectory.Should().Be(@"C:\temp");
        vm.Result.Options.GenerateRepositories.Should().BeTrue();
        closed.Should().BeTrue();
    }

    /// <summary>Repository 生成オプションの変更が結果へ反映されることを検証する</summary>
    [Fact(DisplayName = "Repository 生成オプションを変更すると結果へ反映される")]
    public void Ok_WithRepositoryOption_StoresSelection()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Sample.Domain";
        vm.OutputFilePath = @"C:\temp\Entities.g.cs";
        vm.GenerateRepositories = false;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateRepositories.Should().BeFalse();
    }

    /// <summary>不正な名前空間ではエラーメッセージを表示し、確定・クローズしないことを検証する</summary>
    [Fact(DisplayName = "不正な namespace ではエラーメッセージを表示して閉じない")]
    public void Ok_WithInvalidNamespace_ShowsError()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "1Invalid.Namespace";
        bool closed = false;
        vm.CloseAction = _ => closed = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be("namespace の形式が正しくありません。");
        closed.Should().BeFalse();
    }

    /// <summary>参照コマンドで選択したパスが出力先へ反映されることを検証する</summary>
    [Fact(DisplayName = "参照コマンドで出力先ファイルを更新できる")]
    public void BrowseOutputFile_UpdatesPath()
    {
        var vm = CreateViewModel(out _);
        vm.BrowseOutputFileAction = _ => @"C:\work\Generated\Entities.g.cs";

        vm.BrowseOutputFileCommand.Execute(null);

        vm.OutputFilePath.Should().Be(@"C:\work\Generated\Entities.g.cs");
    }

    /// <summary>分割モードでは詳細欄が表示され、プレビューにカテゴリ別ファイルが並ぶことを検証する</summary>
    [Fact(DisplayName = "分割モードで詳細とプレビューが現れる")]
    public void SplitMode_ShowsDetailsAndPreview()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";

        vm.SplitFilesByCategory = true;

        vm.ShowSplitOptions.Should().BeTrue();
        vm.ShowSingleFileOutput.Should().BeFalse();
        vm.ShowRuntimeNamespace.Should().BeTrue();
        vm.PreviewFiles.Should().Contain(line => line.Contains("Entities.g.cs"));
        vm.PreviewFiles.Should().Contain(line => line.Contains("Runtime.g.cs"));
        vm.PreviewFiles.Should().Contain(line => line.Contains("namespace Acme.App.Entities"));
    }

    /// <summary>生成対象を外すと、その名前空間欄が隠れプレビューからも消えることを検証する</summary>
    [Fact(DisplayName = "生成対象を外すと欄とプレビューが連動する")]
    public void GenerateFlag_TogglesFieldAndPreview()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.GenerateMappers = false;

        vm.ShowMapperNamespace.Should().BeFalse();
        vm.PreviewFiles.Should().NotContain(line => line.Contains("Mappers.g.cs"));
    }

    /// <summary>ベース名前空間を変えると、既定のままの子名前空間が追従し、手編集済みは保持されることを検証する</summary>
    [Fact(DisplayName = "ベース変更で既定の子 namespace が追従する")]
    public void BaseNamespaceChange_FollowsDefaultChildren()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        // EditModel は手編集（追従対象外にする）
        vm.EditModelNamespace = "Custom.Edit";

        vm.BaseNamespace = "Contoso.Sales";

        vm.EntityNamespace.Should().Be("Contoso.Sales.Entities");
        vm.RuntimeNamespace.Should().Be("Contoso.Sales.Runtime");
        vm.EditModelNamespace.Should().Be("Custom.Edit");
    }

    /// <summary>分割モードで出力フォルダ未指定なら確定できないことを検証する</summary>
    [Fact(DisplayName = "分割モードで出力フォルダ未指定なら確定できない")]
    public void Ok_Split_WithoutFolder_ShowsError()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.OutputFolderPath = string.Empty;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be("出力先フォルダを指定してください。");
    }

    /// <summary>確定で保存した設定が、次回の ViewModel 構築で復元されることを検証する</summary>
    [Fact(DisplayName = "設定が次回起動時に復元される")]
    public void Settings_ArePersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.BaseNamespace = "Acme.App";
            vm.SplitFilesByCategory = true;
            vm.GenerateValueObjects = true;
            vm.OutputFolderPath = @"C:\out";
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.BaseNamespace.Should().Be("Acme.App");
            restored.SplitFilesByCategory.Should().BeTrue();
            restored.GenerateValueObjects.Should().BeTrue();
            restored.OutputFolderPath.Should().Be(@"C:\out");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>クリアで全設定が工場出荷既定へ戻ることを検証する</summary>
    [Fact(DisplayName = "クリアで工場出荷既定へ戻る")]
    public void Clear_RestoresFactoryDefaults()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.GenerateRepositories = false;
        vm.GenerateValueObjects = true;

        vm.ClearCommand.Execute(null);

        vm.SplitFilesByCategory.Should().BeFalse();
        vm.BaseNamespace.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
        vm.GenerateRepositories.Should().BeTrue();
        vm.GenerateValueObjects.Should().BeFalse();
    }
}
