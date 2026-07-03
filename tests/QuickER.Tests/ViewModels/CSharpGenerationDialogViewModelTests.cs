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
    private static CSharpGenerationDialogViewModel CreateViewModel(
        out string folder,
        string? currentProviderName = null
    )
    {
        folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(folder),
            currentProviderName: currentProviderName
        );
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
        // DB アクセスの既定は「なし」（自作 Repository も EF Core も生成しない）
        vm.Result.Options.GenerateRepositories.Should().BeFalse();
        vm.Result.Options.GenerateEfCore.Should().BeFalse();
        closed.Should().BeTrue();
    }

    /// <summary>DB アクセスラジオが排他で動き、選択が結果オプションへ反映されることを検証する</summary>
    [Fact(DisplayName = "DB アクセスラジオは排他選択で結果へ反映される")]
    public void DbAccessRadios_AreExclusive_AndStoredInResult()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Sample.Domain";
        vm.OutputFilePath = @"C:\temp\Entities.g.cs";

        vm.DbAccessNone.Should().BeTrue("既定は「なし」");

        vm.DbAccessEfCore = true;
        vm.GenerateEfCore.Should().BeTrue();
        vm.GenerateRepositories.Should().BeFalse();

        vm.DbAccessRepository = true;
        vm.GenerateRepositories.Should().BeTrue();
        vm.GenerateEfCore.Should().BeFalse("排他選択のため EF Core は外れる");

        vm.DbAccessEfCore = true;
        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateEfCore.Should().BeTrue();
        vm.Result.Options.GenerateRepositories.Should().BeFalse();
    }

    /// <summary>Entity は保存値に依らず常に生成対象（強制 ON）であることを検証する</summary>
    [Fact(DisplayName = "Entity は保存値に依らず常に生成対象になる")]
    public void EntityGeneration_IsAlwaysForcedOn()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var store = new CSharpGenerationSettingsStore(folder);
        var settings = CSharpGenerationSettings.CreateDefault();
        settings.GenerateEntityClasses = false;
        store.Save(settings);

        try
        {
            var vm = new CSharpGenerationDialogViewModel(store);

            vm.GenerateEntityClasses.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>SQL Server 以外のプロバイダでは自作 Repository が選択不可になり、保存値も「なし」へ倒れることを検証する</summary>
    [Fact(DisplayName = "SQL Server 以外では自作 Repository を選択できない")]
    public void NonSqlServerProvider_DisablesRepositoryOption()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var store = new CSharpGenerationSettingsStore(folder);
        var settings = CSharpGenerationSettings.CreateDefault();
        settings.GenerateRepositories = true;
        store.Save(settings);

        try
        {
            var vm = new CSharpGenerationDialogViewModel(
                store,
                currentProviderName: QuickER.PostgreSql.PostgreSqlProvider.ProviderName
            );

            vm.CanSelectSqlServerRepository.Should().BeFalse();
            vm.ShowRepositoryDisabledNote.Should().BeTrue();
            vm.RepositoryDisabledNote.Should().NotBeEmpty();
            // 保存されていた「自作 Repository」選択は矛盾生成物を防ぐため「なし」へ倒す
            vm.GenerateRepositories.Should().BeFalse();
            vm.DbAccessNone.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>SQL Server プロバイダでは自作 Repository を選択できることを検証する</summary>
    [Fact(DisplayName = "SQL Server プロバイダでは自作 Repository を選択できる")]
    public void SqlServerProvider_AllowsRepositoryOption()
    {
        var vm = CreateViewModel(
            out _,
            currentProviderName: QuickER.SqlServer.SqlServerProvider.ProviderName
        );

        vm.CanSelectSqlServerRepository.Should().BeTrue();
        vm.ShowRepositoryDisabledNote.Should().BeFalse();
    }

    /// <summary>EF Core 選択＋分割モードで EfCore 名前空間欄が現れ、ベース変更へ追従することを検証する</summary>
    [Fact(DisplayName = "EF Core 選択で EfCore 名前空間欄が連動する")]
    public void EfCoreSelection_TogglesNamespaceField()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.ShowEfCoreNamespace.Should().BeFalse("EF Core 未選択では欄を出さない");

        vm.DbAccessEfCore = true;

        vm.ShowEfCoreNamespace.Should().BeTrue();
        vm.EfCoreNamespace.Should().Be("Acme.App.EfCore");

        vm.BaseNamespace = "Contoso.Sales";
        vm.EfCoreNamespace.Should().Be("Contoso.Sales.EfCore");
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
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var files = new StubFileDialogService
        {
            SaveResult = new FileDialogResult(@"C:\work\Generated\Entities.g.cs", 1),
        };
        var vm = new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(folder),
            files
        );

        vm.BrowseOutputFileCommand.Execute(null);

        vm.OutputFilePath.Should().Be(@"C:\work\Generated\Entities.g.cs");
    }

    /// <summary>ファイル選択ダイアログを表示せず、設定済みの結果を返すスタブ</summary>
    private sealed class StubFileDialogService : IFileDialogService
    {
        public FileDialogResult? SaveResult { get; init; }

        public string? FolderResult { get; init; }

        public FileDialogResult? PickOpenFile(string filter) => null;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        ) => SaveResult;

        public string? PickFolder(string title, string? initialDirectory = null) => FolderResult;
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
        vm.DbAccessEfCore = true;
        vm.GenerateValueObjects = true;

        vm.ClearCommand.Execute(null);

        vm.SplitFilesByCategory.Should().BeFalse();
        vm.BaseNamespace.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
        // 工場出荷既定は DB アクセス「なし」・Entity 常時 ON
        vm.DbAccessNone.Should().BeTrue();
        vm.GenerateEntityClasses.Should().BeTrue();
        vm.GenerateValueObjects.Should().BeFalse();
    }

    /// <summary>EF Core の選択と EfCore 名前空間が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "EF Core の選択が次回起動時に復元される")]
    public void EfCoreSelection_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.BaseNamespace = "Acme.App";
            vm.SplitFilesByCategory = true;
            vm.DbAccessEfCore = true;
            vm.EfCoreNamespace = "Acme.App.Persistence";
            vm.OutputFolderPath = @"C:\out";
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.DbAccessEfCore.Should().BeTrue();
            restored.GenerateRepositories.Should().BeFalse();
            restored.EfCoreNamespace.Should().Be("Acme.App.Persistence");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
