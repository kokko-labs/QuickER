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
        QuickER.Provider.IDatabaseProvider? currentProvider = null
    )
    {
        folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(folder),
            currentProvider: currentProvider
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

    /// <summary>未対応方言のプロバイダでも自作 Repository ラジオは常時選択可であり、対象 DB チェックは両方 OFF から始まることを検証する</summary>
    [Theory(
        DisplayName = "未対応方言でも自作 Repository ラジオは選択可・対象 DB チェックは両方 OFF から始まる"
    )]
    [InlineData(typeof(QuickER.PostgreSql.PostgreSqlProvider))]
    [InlineData(typeof(QuickER.MySql.MySqlProvider))]
    [InlineData(typeof(QuickER.Oracle.OracleProvider))]
    public void UnsupportedDialectProvider_StillAllowsRepositoryRadio_ButNoDialectPreselected(
        Type providerType
    )
    {
        var provider = (QuickER.Provider.IDatabaseProvider)Activator.CreateInstance(providerType)!;
        var vm = CreateViewModel(out _, currentProvider: provider);

        vm.TargetSqlServer.Should().BeFalse("未対応方言では対象 DB を予選択しない");
        vm.TargetSqlite.Should().BeFalse("未対応方言では対象 DB を予選択しない");

        vm.DbAccessRepository = true;

        vm.GenerateRepositories.Should().BeTrue("Repository ラジオは常時選択可");
    }

    /// <summary>対応方言（SQL Server / SQLite）のプロバイダでは、その方言のみ対象 DB チェックが初期 ON になることを検証する</summary>
    [Theory(
        DisplayName = "対応方言（SQL Server / SQLite）ではその方言のみ対象 DB チェックが初期 ON になる"
    )]
    [InlineData(typeof(QuickER.SqlServer.SqlServerProvider))]
    [InlineData(typeof(QuickER.Sqlite.SqliteProvider))]
    public void SupportedDialectProvider_PreselectsItsDialect(Type providerType)
    {
        var provider = (QuickER.Provider.IDatabaseProvider)Activator.CreateInstance(providerType)!;
        var vm = CreateViewModel(out _, currentProvider: provider);

        vm.QuickErRepositoryToolTip.Should()
            .Be(
                "EF 非依存の軽量 Repository を生成します（対象 DB をチェックで選択: SQL Server / SQLite）"
            );

        if (provider.Name == "sqlserver")
        {
            vm.TargetSqlServer.Should().BeTrue();
            vm.TargetSqlite.Should().BeFalse();
        }
        else
        {
            vm.TargetSqlServer.Should().BeFalse();
            vm.TargetSqlite.Should().BeTrue();
        }
    }

    /// <summary>ToOptions がチェックした対象 DB を固定順（sqlserver, sqlite）で RepositoryDialects へ設定することを検証する</summary>
    [Fact(DisplayName = "ToOptions はチェックした対象 DB を固定順で RepositoryDialects に設定する")]
    public void ToOptions_SetsRepositoryDialects_InFixedOrder()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.Sqlite.SqliteProvider());
        vm.BaseNamespace = "Sample.Domain";
        vm.OutputFilePath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;
        vm.TargetSqlServer = true;
        vm.TargetSqlite = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.RepositoryDialects.Should().Equal("sqlserver", "sqlite");
    }

    /// <summary>対象 DB を 1 つもチェックしないまま自作 Repository を確定しようとすると拒否されることを検証する</summary>
    [Fact(DisplayName = "対象 DB 0 個では確定できない")]
    public void Ok_Repository_WithNoTargetDialects_ShowsError()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.PostgreSql.PostgreSqlProvider()
        );
        vm.BaseNamespace = "Sample.Domain";
        vm.OutputFilePath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be("対象 DB を 1 つ以上選択してください。");
    }

    /// <summary>対象 DB チェック群は Repository (QuickER) 選択時のみ表示されることを検証する</summary>
    [Fact(DisplayName = "対象 DB チェック群は Repository (QuickER) 選択時のみ表示される")]
    public void ShowRepositoryDialectTargets_TracksRepositorySelection()
    {
        var vm = CreateViewModel(out _);

        vm.ShowRepositoryDialectTargets.Should().BeFalse("既定は DB アクセス「なし」");

        vm.DbAccessRepository = true;
        vm.ShowRepositoryDialectTargets.Should().BeTrue();

        vm.DbAccessNone = true;
        vm.ShowRepositoryDialectTargets.Should().BeFalse();
    }

    /// <summary>対象 DB チェックは設定として永続化されず、次回起動時は図の方言から再導出されることを検証する</summary>
    [Fact(DisplayName = "対象 DB チェックは保存されず図の方言から毎回導出される")]
    public void TargetDialectChecks_AreNotPersisted()
    {
        var vm = CreateViewModel(
            out var folder,
            currentProvider: new QuickER.Sqlite.SqliteProvider()
        );

        try
        {
            vm.BaseNamespace = "Acme.App";
            vm.OutputFilePath = @"C:\temp\Entities.g.cs";
            vm.DbAccessRepository = true;
            vm.TargetSqlServer = true;
            vm.TargetSqlite = true;
            vm.OkCommand.Execute(null);

            // 次回はプロバイダを変えて再構築しても、保存されたチェック内容ではなく現在のプロバイダから導出される
            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder),
                currentProvider: new QuickER.SqlServer.SqlServerProvider()
            );

            restored.TargetSqlServer.Should().BeTrue();
            restored.TargetSqlite.Should().BeFalse("SQLite のチェックは保存されず引き継がれない");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>EF Core 選択＋分割モードで EfCore 名前空間欄が現れ、ベース変更へ追従することを検証する</summary>
    [Fact(DisplayName = "EF Core 選択で EfCore 名前空間欄が連動する")]
    public void EfCoreSelection_TogglesNamespaceField()
    {
        var vm = CreateViewModel(out _);
        vm.BaseNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.ShowEfCoreNamespace.Should().BeFalse("EF Core 未選択では欄を出さない");
        vm.ShowRepositoryNamespace.Should().BeFalse("DB アクセス「なし」では契約も出力されない");

        vm.DbAccessEfCore = true;

        vm.ShowEfCoreNamespace.Should().BeTrue();
        vm.ShowRepositoryNamespace.Should()
            .BeTrue("EF Core でも契約が Repository バケットに出力される");
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
