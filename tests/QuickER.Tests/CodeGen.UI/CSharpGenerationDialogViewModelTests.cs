using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;

namespace QuickER.Tests.CodeGen.UI;

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
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        bool? closed = null;
        vm.CloseAction = result => closed = result;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.RootNamespace.Should().Be("Sample.Domain");
        vm.Result.Options.SplitFilesByCategory.Should().BeFalse();
        vm.Result.OutputDirectory.Should().Be(@"C:\temp");
        // DB アクセスの既定は「なし」（QuickER 版 Repository も EF Core も生成しない）
        vm.Result.Options.GenerateRepositories.Should().BeFalse();
        vm.Result.Options.GenerateEfCore.Should().BeFalse();
        closed.Should().BeTrue();
    }

    /// <summary>DB アクセスラジオが排他で動き、選択が結果オプションへ反映されることを検証する</summary>
    [Fact(DisplayName = "DB アクセスラジオは排他選択で結果へ反映される")]
    public void DbAccessRadios_AreExclusive_AndStoredInResult()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

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

    /// <summary>未対応方言のプロバイダでもQuickER 版 Repository ラジオは常時選択可であり、対象 DB チェックは両方 OFF から始まることを検証する</summary>
    [Theory(
        DisplayName = "未対応方言でもQuickER 版 Repository ラジオは選択可・対象 DB チェックは両方 OFF から始まる"
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

        vm.QuickErRepositoryToolTip.Should().Be(CodeGenStrings.CodeGen_QuickErRepositoryToolTip);

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
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;
        vm.TargetSqlServer = true;
        vm.TargetSqlite = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.RepositoryDialects.Should().Equal("sqlserver", "sqlite");
    }

    /// <summary>対象 DB を 1 つもチェックしないままQuickER 版 Repository を確定しようとすると拒否されることを検証する</summary>
    [Fact(DisplayName = "対象 DB 0 個では確定できない")]
    public void Ok_Repository_WithNoTargetDialects_ShowsError()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.PostgreSql.PostgreSqlProvider()
        );
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(CodeGenStrings.CodeGen_Status_TargetDbRequired);
    }

    /// <summary>対象 DB チェック群は QuickER 版 Repository 選択時のみ表示されることを検証する</summary>
    [Fact(DisplayName = "対象 DB チェック群は QuickER 版 Repository 選択時のみ表示される")]
    public void ShowRepositoryDialectTargets_TracksRepositorySelection()
    {
        var vm = CreateViewModel(out _);

        vm.ShowRepositoryDialectTargets.Should().BeFalse("既定は DB アクセス「なし」");

        vm.DbAccessRepository = true;
        vm.ShowRepositoryDialectTargets.Should().BeTrue();

        vm.DbAccessNone = true;
        vm.ShowRepositoryDialectTargets.Should().BeFalse();
    }

    /// <summary>
    /// 対象 DB チェックは %APPDATA% の設定へ永続化され、次回起動時はプロバイダの初期値より
    /// 保存値（非空リスト）が優先して復元されることを検証する
    /// </summary>
    [Fact(DisplayName = "対象 DB チェックは保存され、次回は保存値が図の方言より優先して復元される")]
    public void TargetDialectChecks_ArePersisted_AndRestoredOverProviderDefault()
    {
        var vm = CreateViewModel(
            out var folder,
            currentProvider: new QuickER.Sqlite.SqliteProvider()
        );

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.DbAccessRepository = true;
            vm.TargetSqlServer = true;
            vm.TargetSqlite = true;
            vm.OkCommand.Execute(null);

            // 次回はプロバイダを変えて再構築しても、保存された対象 DB（両方 ON）が優先して復元される
            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder),
                currentProvider: new QuickER.SqlServer.SqlServerProvider()
            );

            restored.TargetSqlServer.Should().BeTrue("保存値どおり SQL Server は ON");
            restored
                .TargetSqlite.Should()
                .BeTrue("保存値（非空リスト）が優先され SQLite も復元される");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// EF Core 選択＋分割モードで Repository 契約名前空間欄が現れ、EF Core 実装ファイルは方言別実装と同じ流儀で
    /// Repositories.EfCore.g.cs・{RepositoryNamespace}.EfCore へ導出される（専用の名前空間欄は持たない）ことを検証する
    /// </summary>
    [Fact(DisplayName = "EF Core 選択で EF Core 実装が {Repository}.EfCore へ導出される")]
    public void EfCoreSelection_DerivesEfCoreNamespaceFromRepository()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.ShowRepositoryNamespace.Should().BeFalse("DB アクセス「なし」では契約も出力されない");

        vm.DbAccessEfCore = true;

        vm.ShowRepositoryNamespace.Should()
            .BeTrue("EF Core でも契約が Repository バケットに出力される");

        // EF Core 実装は Repository 契約名前空間のサブ名前空間へ導出される（専用欄なし）
        vm.PreviewFiles.Should()
            .Contain("Repositories.EfCore.g.cs  →  namespace Acme.App.Repositories.EfCore");

        // ルート変更に伴い契約（Repositories）が追従すれば EF Core も自動的に追従する
        vm.RootNamespace = "Contoso.Sales";
        vm.PreviewFiles.Should()
            .Contain("Repositories.EfCore.g.cs  →  namespace Contoso.Sales.Repositories.EfCore");
    }

    /// <summary>
    /// EF Core 選択中もパッケージ参照モードのチェックボックスは操作可能で、
    /// チェック済みのまま EF Core を選んでも解除されず、結果に両方が反映されることを検証する（併用解禁）
    /// </summary>
    [Fact(DisplayName = "EF Core 選択中もパッケージ参照モードは操作可能・併用が結果へ反映される")]
    public void UseRuntimePackages_StaysEnabled_AndCoexistsWithEfCore()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.UseRuntimePackages = true;
        vm.UseRuntimePackages.Should().BeTrue();

        vm.DbAccessEfCore = true;

        vm.UseRuntimePackages.Should().BeTrue("EF Core 選択でもチェックは解除されない");

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateEfCore.Should().BeTrue();
        vm.Result.Options.UseRuntimePackages.Should()
            .BeTrue("EF Core とパッケージ参照モードの併用が結果へ反映される");
    }

    /// <summary>
    /// QuickER 版 Repository 選択（EF Core ではない）ではパッケージ参照モードが操作可能で、
    /// チェックした場合に結果オプションへ反映されることを検証する
    /// </summary>
    [Fact(DisplayName = "QuickER 版 Repository 選択ではパッケージ参照モードが結果へ反映される")]
    public void UseRuntimePackages_IsReflectedInResult_WhenRepositorySelected()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        vm.UseRuntimePackages = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.UseRuntimePackages.Should().BeTrue();
        vm.Result.Options.GenerateRepositories.Should().BeTrue();
    }

    /// <summary>API リファレンス出力チェックの既定は OFF で、ON にすると結果オプションへ反映されることを検証する</summary>
    [Fact(DisplayName = "API リファレンス出力チェックは既定 OFF・ON で結果へ反映される")]
    public void GenerateApiDocs_DefaultsOff_AndReflectedInResult()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.GenerateApiDocs.Should().BeFalse("既定は OFF");

        vm.GenerateApiDocs = true;
        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateApiDocs.Should().BeTrue();
    }

    /// <summary>API リファレンス出力チェックの既定 OFF が結果オプションにも反映されることを検証する</summary>
    [Fact(DisplayName = "API リファレンス出力チェック未操作では GenerateApiDocs=false")]
    public void GenerateApiDocs_WhenUntouched_ResultIsFalse()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateApiDocs.Should().BeFalse();
    }

    /// <summary>API リファレンス出力チェックの状態が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "API リファレンス出力チェックが次回起動時に復元される")]
    public void GenerateApiDocs_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.GenerateApiDocs = true;
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.GenerateApiDocs.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 無制限バイナリ列の除外チェックを ON にすると、結果オプションの ExcludeUnboundedBinaryColumns が true になり、
    /// OFF（既定）では false になることを検証する
    /// </summary>
    [Fact(
        DisplayName = "無制限バイナリ列の除外は既定 OFF・ON で ExcludeUnboundedBinaryColumns へ反映される"
    )]
    public void ExcludeUnboundedBinary_IsReflectedInResultOptions()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        // 既定（OFF）は false
        vm.ExcludeUnboundedBinaryColumns.Should().BeFalse("既定は OFF");
        vm.ToOptions().ExcludeUnboundedBinaryColumns.Should().BeFalse();

        vm.ExcludeUnboundedBinaryColumns = true;
        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.ExcludeUnboundedBinaryColumns.Should().BeTrue();
    }

    /// <summary>
    /// 無制限バイナリ列の除外行の表示フラグはQuickER 版 Repository 生成に追従し、「なし」/ EF Core では非表示・
    /// QuickER 版 Repository 選択時のみ表示になることを検証する
    /// </summary>
    [Fact(DisplayName = "無制限バイナリ列の除外行はQuickER 版 Repository 選択時のみ表示")]
    public void ShowExcludeUnboundedBinary_TracksRepositorySelection()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";

        vm.ShowExcludeUnboundedBinary.Should().BeFalse("既定は DB アクセス「なし」のため非表示");

        vm.DbAccessRepository = true;
        vm.ShowExcludeUnboundedBinary.Should().BeTrue("QuickER 版 Repository 選択で表示");

        vm.DbAccessEfCore = true;
        vm.ShowExcludeUnboundedBinary.Should()
            .BeFalse("EF Core 選択では非表示（QuickER 版 Repository 専用）");

        vm.DbAccessNone = true;
        vm.ShowExcludeUnboundedBinary.Should().BeFalse("DB アクセス「なし」でも非表示");
    }

    /// <summary>無制限バイナリ列の除外チェックの状態が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "無制限バイナリ列の除外チェックが次回起動時に復元される")]
    public void ExcludeUnboundedBinary_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.DbAccessRepository = true;
            vm.ExcludeUnboundedBinaryColumns = true;
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.ExcludeUnboundedBinaryColumns.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// リモート対応のチェックを ON にすると、結果オプションの GenerateRemoteContracts が true になり、
    /// OFF（既定）では false になることを検証する
    /// </summary>
    [Fact(
        DisplayName = "リモート対応 ON で GenerateRemoteContracts=true・OFF で false が結果へ反映される"
    )]
    public void GenerateRemoteContracts_IsReflectedInResultOptions()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        // 既定（OFF）は false
        vm.GenerateRemoteContracts.Should().BeFalse("既定は OFF");
        vm.ToOptions().GenerateRemoteContracts.Should().BeFalse();

        vm.GenerateRemoteContracts = true;
        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateRemoteContracts.Should().BeTrue();
    }

    /// <summary>
    /// リモート対応行の表示フラグは DB アクセス選択に連動し、「なし」では非表示・
    /// QuickER 版 Repository / EF Core 選択で表示になることを検証する
    /// </summary>
    [Fact(DisplayName = "リモート対応行は DB アクセス「なし」で非表示・Repository/EF Core で表示")]
    public void ShowRemoteContracts_TracksDbAccessSelection()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";

        vm.ShowRemoteContracts.Should().BeFalse("既定は DB アクセス「なし」のため非表示");

        vm.DbAccessRepository = true;
        vm.ShowRemoteContracts.Should().BeTrue("QuickER 版 Repository 選択で表示");

        vm.DbAccessEfCore = true;
        vm.ShowRemoteContracts.Should().BeTrue("EF Core 選択でも表示");

        vm.DbAccessNone = true;
        vm.ShowRemoteContracts.Should().BeFalse("DB アクセス「なし」に戻すと非表示");
    }

    /// <summary>リモート対応チェックの状態が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "リモート対応チェックが次回起動時に復元される")]
    public void GenerateRemoteContracts_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.DbAccessRepository = true;
            vm.GenerateRemoteContracts = true;
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.GenerateRemoteContracts.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// HTTP クライアント／サーバー実装のチェックを ON にすると、結果オプションの GenerateRemoteServices が true になり、
    /// かつ含意により GenerateRemoteContracts（リモート面インターフェイス）も true に連動することを検証する
    /// </summary>
    [Fact(
        DisplayName = "HTTP 実装 ON で GenerateRemoteServices=true・GenerateRemoteContracts も連動して true"
    )]
    public void GenerateRemoteServices_On_ImpliesRemoteContracts()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        // 既定（OFF）は両方 false
        vm.GenerateRemoteServices.Should().BeFalse("既定は OFF");
        vm.ToOptions().GenerateRemoteServices.Should().BeFalse();

        vm.GenerateRemoteServices = true;

        // ON にすると含意でリモート面インターフェイスも ON になる
        vm.GenerateRemoteContracts.Should()
            .BeTrue("HTTP 実装 ON はリモート面インターフェイスを含意する");

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateRemoteServices.Should().BeTrue();
        vm.Result!.Options.GenerateRemoteContracts.Should().BeTrue();
    }

    /// <summary>
    /// リモート面インターフェイス（GenerateRemoteContracts）を OFF にすると、それに依存する
    /// HTTP クライアント／サーバー実装（GenerateRemoteServices）も OFF に連動することを検証する
    /// </summary>
    [Fact(DisplayName = "リモート面インターフェイス OFF で HTTP 実装も OFF に連動する")]
    public void GenerateRemoteContracts_Off_TurnsOffRemoteServices()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.DbAccessRepository = true;

        // HTTP 実装を ON にすると親（リモート面）も ON
        vm.GenerateRemoteServices = true;
        vm.GenerateRemoteContracts.Should().BeTrue();

        // 親を OFF にすると子（HTTP 実装）も OFF に戻る
        vm.GenerateRemoteContracts = false;
        vm.GenerateRemoteServices.Should().BeFalse("親 OFF で子も OFF に連動する");
    }

    /// <summary>
    /// HTTP クライアント／サーバー実装のチェック切り替えで、出力ファイルのプレビューへ
    /// サーバーファイル（{ベース名}.RemoteServer.g.cs）が即時に現れ・消えることを検証する
    /// </summary>
    [Fact(DisplayName = "HTTP 実装の切替でプレビューに RemoteServer ファイルが連動する")]
    public void GenerateRemoteServices_TogglesRemoteServerFileInPreview()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Shop.g.cs";
        vm.DbAccessRepository = true;

        vm.PreviewFiles.Should()
            .NotContain(
                line => line.Contains("RemoteServer.g.cs"),
                "OFF（既定）ではサーバーファイルを出力しない"
            );

        vm.GenerateRemoteServices = true;

        vm.PreviewFiles.Should()
            .Contain(
                line => line.Contains("Shop.RemoteServer.g.cs"),
                "ON にした直後にプレビューへサーバーファイルが現れる"
            );

        vm.GenerateRemoteServices = false;

        vm.PreviewFiles.Should()
            .NotContain(
                line => line.Contains("RemoteServer.g.cs"),
                "OFF に戻した直後にプレビューからサーバーファイルが消える"
            );
    }

    /// <summary>HTTP クライアント／サーバー実装チェックの状態が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "HTTP クライアント／サーバー実装チェックが次回起動時に復元される")]
    public void GenerateRemoteServices_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.DbAccessRepository = true;
            vm.GenerateRemoteServices = true;
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.GenerateRemoteServices.Should().BeTrue();
            // 含意により復元後もリモート面インターフェイスは ON のまま
            restored.GenerateRemoteContracts.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>不正な名前空間ではエラーメッセージを表示し、確定・クローズしないことを検証する</summary>
    [Fact(DisplayName = "不正な namespace ではエラーメッセージを表示して閉じない")]
    public void Ok_WithInvalidNamespace_ShowsError()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "1Invalid.Namespace";
        bool closed = false;
        vm.CloseAction = _ => closed = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(CodeGenStrings.CodeGen_Status_NamespaceInvalid);
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

        vm.BrowseOutputCommand.Execute(null);

        vm.OutputPath.Should().Be(@"C:\work\Generated\Entities.g.cs");
    }

    /// <summary>一時プロジェクトフォルダに csproj を書き出し、そのプロジェクトディレクトリを返す</summary>
    private static string CreateProjectFolder(string csprojFileName, string rootNamespace)
    {
        var projectDir = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            "NsBrowse",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(projectDir, csprojFileName),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + $"    <RootNamespace>{rootNamespace}</RootNamespace>\n"
                + "  </PropertyGroup>\n"
                + "</Project>\n"
        );
        return projectDir;
    }

    /// <summary>
    /// 分割モードでフォルダを選び確認を承諾すると、フォルダから導出した namespace へベースが書き換わり、
    /// 既定パターンのままの子カテゴリ別 namespace も追従することを検証する
    /// </summary>
    [Fact(
        DisplayName = "分割モードのフォルダ選択で承諾すると namespace が導出値へ書き換わり子 namespace が追従する"
    )]
    public void BrowseOutputFolder_ConfirmTrue_DerivesNamespace_AndFollowsChildren()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var projectDir = CreateProjectFolder("MyProject.csproj", "Contoso.Sales");
        var target = Path.Combine(projectDir, "Data");
        Directory.CreateDirectory(target);

        var files = new StubFileDialogService { FolderResult = target };
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var vm = new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(settingsFolder),
            files,
            dialogs: dialogs
        );

        try
        {
            vm.SplitFilesByCategory = true;

            vm.BrowseOutputCommand.Execute(null);

            // フォルダパスは反映され、確認は導出候補で 1 回だけ行われる
            vm.OutputPath.Should().Be(target);
            dialogs
                .ConfirmMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(
                    string.Format(
                        CodeGenStrings.CodeGen_ConfirmNamespaceFromFolder,
                        "Contoso.Sales.Data"
                    )
                );
            // 承諾したのでベースが書き換わり、既定のままの子 namespace（Entity）も追従する
            vm.RootNamespace.Should().Be("Contoso.Sales.Data");
            vm.EntityNamespace.Should().Be("Contoso.Sales.Data.Entities");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);

            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 分割モードでフォルダを選んでも確認をキャンセルすると、namespace は一切変わらず
    /// フォルダパスの反映だけが行われることを検証する
    /// </summary>
    [Fact(
        DisplayName = "分割モードのフォルダ選択で確認をキャンセルすると namespace は不変・フォルダパスのみ反映"
    )]
    public void BrowseOutputFolder_ConfirmFalse_LeavesNamespaceUnchanged()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        var projectDir = CreateProjectFolder("MyProject.csproj", "Contoso.Sales");
        var target = Path.Combine(projectDir, "Data");
        Directory.CreateDirectory(target);

        var files = new StubFileDialogService { FolderResult = target };
        var dialogs = new RecordingDialogService { ConfirmResult = false };
        var vm = new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(settingsFolder),
            files,
            dialogs: dialogs
        );

        try
        {
            vm.SplitFilesByCategory = true;

            vm.BrowseOutputCommand.Execute(null);

            // 確認は行われたが、キャンセルのため namespace は既定のまま・フォルダパスだけ反映される
            dialogs.ConfirmMessages.Should().ContainSingle();
            vm.OutputPath.Should().Be(target);
            vm.RootNamespace.Should().Be(CSharpGenerationSettings.DefaultRootNamespace);
            vm.EntityNamespace.Should()
                .Be($"{CSharpGenerationSettings.DefaultRootNamespace}.Entities");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);

            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 導出した候補が現在のベース名前空間と同一なら、確認ダイアログを出さず namespace も触らないことを検証する
    /// </summary>
    [Fact(DisplayName = "導出候補が現在の namespace と同一なら確認しない")]
    public void BrowseOutputFolder_SuggestionEqualsCurrent_DoesNotConfirm()
    {
        var settingsFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests",
            Guid.NewGuid().ToString("N")
        );
        // プロジェクト直下（相対階層なし）を選ぶと導出候補はベース "Acme.App" のみになる
        var projectDir = CreateProjectFolder("MyProject.csproj", "Acme.App");

        var files = new StubFileDialogService { FolderResult = projectDir };
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var vm = new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(settingsFolder),
            files,
            dialogs: dialogs
        );

        try
        {
            vm.SplitFilesByCategory = true;
            vm.RootNamespace = "Acme.App";

            vm.BrowseOutputCommand.Execute(null);

            // 候補が現在値と同一なので確認は呼ばれず、namespace も変わらない（フォルダパスは反映される）
            dialogs.ConfirmMessages.Should().BeEmpty();
            vm.RootNamespace.Should().Be("Acme.App");
            vm.OutputPath.Should().Be(projectDir);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);

            if (Directory.Exists(settingsFolder))
            {
                Directory.Delete(settingsFolder, recursive: true);
            }
        }
    }

    /// <summary>メッセージダイアログを表示せず、呼び出し（確認／情報／エラー）を記録するスタブ</summary>
    private sealed class RecordingDialogService : IDialogService
    {
        /// <summary>ShowInformation に渡されたメッセージの記録</summary>
        public List<string> InformationMessages { get; } = new();

        /// <summary>ShowError に渡されたメッセージの記録</summary>
        public List<string> ErrorMessages { get; } = new();

        /// <summary>Confirm に渡されたメッセージの記録</summary>
        public List<string> ConfirmMessages { get; } = new();

        /// <summary>Confirm の返り値（既定は false＝キャンセル扱い。既存挙動を壊さない）</summary>
        public bool ConfirmResult { get; init; }

        public bool Confirm(string message, string title)
        {
            ConfirmMessages.Add(message);
            return ConfirmResult;
        }

        public bool ConfirmWarning(string message, string title) => false;

        public void ShowInformation(string message, string title) =>
            InformationMessages.Add(message);

        public void ShowError(string message, string title) => ErrorMessages.Add(message);

        public void ShowInformationDetails(string message, string details, string title) { }

        public void ShowErrorDetails(string message, string details, string title) { }
    }

    /// <summary>分割モードでは詳細欄が表示され、プレビューにカテゴリ別ファイルが並ぶことを検証する</summary>
    [Fact(DisplayName = "分割モードで詳細とプレビューが現れる")]
    public void SplitMode_ShowsDetailsAndPreview()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";

        vm.SplitFilesByCategory = true;

        vm.ShowSplitOptions.Should().BeTrue();
        vm.ShowSingleFileOutput.Should().BeFalse();
        vm.ShowRuntimeNamespace.Should().BeTrue();
        vm.PreviewFiles.Should().Contain(line => line.Contains("Entities.g.cs"));
        vm.PreviewFiles.Should().Contain(line => line.Contains("Runtime.g.cs"));
        vm.PreviewFiles.Should().Contain(line => line.Contains("namespace Acme.App.Entities"));
    }

    /// <summary>パッケージ参照モードの切替でプレビューが追従し、Runtime ファイルの有無が連動することを検証する</summary>
    [Fact(DisplayName = "パッケージ参照モード切替でプレビューから Runtime が消える")]
    public void UseRuntimePackages_TogglesRuntimeFileInPreview()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.UseRuntimePackages = true;

        vm.PreviewFiles.Should()
            .NotContain(
                line => line.Contains("Runtime.g.cs"),
                "パッケージ参照モードではランタイムを出力しないため、プレビューにも Runtime ファイルが現れてはならない"
            );

        vm.UseRuntimePackages = false;

        vm.PreviewFiles.Should().Contain(line => line.Contains("Runtime.g.cs"));
    }

    /// <summary>生成対象を外すと、その名前空間欄が隠れプレビューからも消えることを検証する</summary>
    [Fact(DisplayName = "生成対象を外すと欄とプレビューが連動する")]
    public void GenerateFlag_TogglesFieldAndPreview()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.GenerateMappers = false;

        vm.ShowMapperNamespace.Should().BeFalse();
        vm.PreviewFiles.Should().NotContain(line => line.Contains("Mappers.g.cs"));
    }

    /// <summary>ベース名前空間を変えると、既定のままの子名前空間が追従し、手編集済みは保持されることを検証する</summary>
    [Fact(DisplayName = "ベース変更で既定の子 namespace が追従する")]
    public void RootNamespaceChange_FollowsDefaultChildren()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        // EditModel は手編集（追従対象外にする）
        vm.EditModelNamespace = "Custom.Edit";

        vm.RootNamespace = "Contoso.Sales";

        vm.EntityNamespace.Should().Be("Contoso.Sales.Entities");
        vm.RuntimeNamespace.Should().Be("Contoso.Sales.Runtime");
        vm.EditModelNamespace.Should().Be("Custom.Edit");
    }

    /// <summary>分割モードで出力フォルダ未指定なら確定できないことを検証する</summary>
    [Fact(DisplayName = "分割モードで出力フォルダ未指定なら確定できない")]
    public void Ok_Split_WithoutFolder_ShowsError()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.OutputPath = string.Empty;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(CodeGenStrings.CodeGen_Status_OutputFolderRequired);
    }

    /// <summary>確定で保存した設定が、次回の ViewModel 構築で復元されることを検証する</summary>
    [Fact(DisplayName = "設定が次回起動時に復元される")]
    public void Settings_ArePersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.SplitFilesByCategory = true;
            vm.GenerateValueObjects = true;
            vm.OutputPath = @"C:\out";
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.RootNamespace.Should().Be("Acme.App");
            restored.SplitFilesByCategory.Should().BeTrue();
            restored.GenerateValueObjects.Should().BeTrue();
            restored.OutputPath.Should().Be(@"C:\out");
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
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.DbAccessEfCore = true;
        vm.GenerateValueObjects = true;

        vm.ClearCommand.Execute(null);

        vm.SplitFilesByCategory.Should().BeFalse();
        vm.RootNamespace.Should().Be(CSharpGenerationSettings.DefaultRootNamespace);
        // 工場出荷既定は DB アクセス「なし」
        vm.DbAccessNone.Should().BeTrue();
        vm.GenerateValueObjects.Should().BeFalse();
    }

    /// <summary>EF Core の選択と Repository 契約名前空間が保存・復元されることを検証する</summary>
    [Fact(DisplayName = "EF Core の選択が次回起動時に復元される")]
    public void EfCoreSelection_IsPersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.SplitFilesByCategory = true;
            vm.DbAccessEfCore = true;
            // EF Core 実装は Repository 契約名前空間のサブ名前空間へ導出されるため、契約名前空間を保存・復元する
            vm.RepositoryNamespace = "Acme.App.Persistence";
            vm.OutputPath = @"C:\out";
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.DbAccessEfCore.Should().BeTrue();
            restored.GenerateRepositories.Should().BeFalse();
            restored.RepositoryNamespace.Should().Be("Acme.App.Persistence");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>「名前を付けて保存」で現在の設定が任意ファイルへ書き出され、TryLoadFrom で同内容に読み戻せることを検証する</summary>
    [Fact(DisplayName = "名前を付けて保存で設定ファイルが生成され読み戻せる")]
    public void SaveSettingsAs_WritesFile_ThatCanBeLoadedBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var presetPath = Path.Combine(folder, "preset", "codegen-settings.json");
        var store = new CSharpGenerationSettingsStore(folder);
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(presetPath, 1) };
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, files, dialogs: dialogs);

        try
        {
            vm.RootNamespace = "Acme.Preset";
            vm.SplitFilesByCategory = true;
            vm.DbAccessEfCore = true;
            vm.GenerateValueObjects = true;

            vm.SaveSettingsAsCommand.Execute(null);

            File.Exists(presetPath).Should().BeTrue();
            // 保存成功は情報ダイアログで 1 回だけ通知される（エラーは出ない）
            dialogs
                .InformationMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(
                    string.Format(
                        CodeGenStrings.CodeGen_SettingsSavedMessage,
                        Path.GetFileName(presetPath)
                    )
                );
            dialogs.ErrorMessages.Should().BeEmpty();

            // 保存されたファイルを読み戻すと、ToSettings 相当の代表値が一致する
            var loaded = store.TryLoadFrom(presetPath);
            loaded.Should().NotBeNull();
            loaded!.RootNamespace.Should().Be("Acme.Preset");
            loaded.SplitFilesByCategory.Should().BeTrue();
            loaded.GenerateEfCore.Should().BeTrue();
            loaded.GenerateValueObjects.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>「名前を付けて保存」をキャンセル（null）した場合、ファイルは書かれずダイアログも出ないことを検証する</summary>
    [Fact(DisplayName = "名前を付けて保存キャンセルではファイル書き込みもダイアログ表示もない")]
    public void SaveSettingsAs_WhenCancelled_DoesNotWriteOrShowDialog()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var store = new CSharpGenerationSettingsStore(folder);
        // SaveResult 未設定＝キャンセル扱い（PickSaveFile が null を返す）
        var files = new StubFileDialogService();
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, files, dialogs: dialogs);

        try
        {
            vm.RootNamespace = "Acme.Preset";

            vm.SaveSettingsAsCommand.Execute(null);

            // キャンセルでは情報・エラーいずれのダイアログも出さない
            dialogs.InformationMessages.Should().BeEmpty();
            dialogs.ErrorMessages.Should().BeEmpty();
            // 既定保存先へも書かれていない（生成確定時のみ書く）
            File.Exists(store.SettingsPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 「読み込み」で保存済み設定ファイルの内容が表示状態へ反映され、既存 ApplySettings の排他規則
    /// （QuickER 版 Repository 優先・Entity 常時 ON）が効くことを検証する
    /// </summary>
    [Fact(DisplayName = "読み込みで設定ファイルの内容が表示状態へ反映される")]
    public void LoadSettingsFrom_AppliesFileToViewModel()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var presetPath = Path.Combine(folder, "codegen-settings.json");
        var store = new CSharpGenerationSettingsStore(folder);

        // 事前に SaveTo で設定ファイルを用意する（Repository と EF Core を両方 true＝手編集相当）
        store.SaveTo(
            presetPath,
            new CSharpGenerationSettings
            {
                SplitFilesByCategory = true,
                RootNamespace = "Contoso.Loaded",
                RepositoryNamespace = "Contoso.Loaded.Persistence",
                GenerateRepositories = true,
                GenerateEfCore = true,
                GenerateValueObjects = true,
            }
        );

        var files = new StubFileDialogService { OpenResult = new FileDialogResult(presetPath, 1) };
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, files, dialogs: dialogs);

        try
        {
            vm.LoadSettingsFromCommand.Execute(null);

            vm.RootNamespace.Should().Be("Contoso.Loaded");
            vm.SplitFilesByCategory.Should().BeTrue();
            vm.RepositoryNamespace.Should().Be("Contoso.Loaded.Persistence");
            vm.GenerateValueObjects.Should().BeTrue();
            // 排他規則: 両方 true の保存値は QuickER 版 Repository を優先し EF Core は外れる
            vm.GenerateRepositories.Should().BeTrue();
            vm.GenerateEfCore.Should().BeFalse();

            // 読み込み成功は無通知（情報・エラーいずれのダイアログも出さない）
            dialogs.InformationMessages.Should().BeEmpty();
            dialogs.ErrorMessages.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>壊れた JSON を読み込もうとした場合、ステータスにエラーを表示し表示状態は変更しないことを検証する</summary>
    [Fact(DisplayName = "壊れた JSON の読み込みはエラー表示・表示状態は不変")]
    public void LoadSettingsFrom_WhenCorrupt_ShowsError_AndLeavesStateUnchanged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var brokenPath = Path.Combine(folder, "broken.json");
        File.WriteAllText(brokenPath, "{ this is not valid json");

        var store = new CSharpGenerationSettingsStore(folder);
        var files = new StubFileDialogService { OpenResult = new FileDialogResult(brokenPath, 1) };
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, files, dialogs: dialogs);

        try
        {
            // 読み込み前の表示状態を作っておく
            vm.RootNamespace = "Acme.Untouched";
            vm.SplitFilesByCategory = true;
            vm.GenerateValueObjects = true;

            vm.LoadSettingsFromCommand.Execute(null);

            // 失敗はエラーダイアログで 1 回だけ通知される（情報ダイアログは出ない）
            dialogs
                .ErrorMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(
                    string.Format(
                        CodeGenStrings.CodeGen_SettingsLoadFailedMessage,
                        Path.GetFileName(brokenPath)
                    )
                );
            dialogs.InformationMessages.Should().BeEmpty();
            // 表示状態は変更されない
            vm.RootNamespace.Should().Be("Acme.Untouched");
            vm.SplitFilesByCategory.Should().BeTrue();
            vm.GenerateValueObjects.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// 対象 DB チェック（RepositoryDialects）と namespace が「保存 → 別 VM で読込」で往復し、
    /// 読み込んだ VM の対象 DB チェックがファイル内容どおりに復元されることを検証する
    /// </summary>
    [Fact(DisplayName = "対象 DB チェックと namespace が名前を付けて保存→読み込みで往復する")]
    public void SaveThenLoad_RoundTripsTargetDialectsAndNamespace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var presetPath = Path.Combine(folder, "preset", "codegen-settings.json");
        var store = new CSharpGenerationSettingsStore(folder);

        try
        {
            // 保存側: SQL Server 図でQuickER 版 Repository・対象 DB は SQLite のみを選ぶ
            var saveFiles = new StubFileDialogService
            {
                SaveResult = new FileDialogResult(presetPath, 1),
            };
            var saveVm = new CSharpGenerationDialogViewModel(
                store,
                saveFiles,
                currentProvider: new QuickER.SqlServer.SqlServerProvider()
            );
            saveVm.RootNamespace = "Acme.Roundtrip";
            saveVm.DbAccessRepository = true;
            saveVm.TargetSqlServer = false;
            saveVm.TargetSqlite = true;

            saveVm.SaveSettingsAsCommand.Execute(null);

            // 読込側: SQLite 図（対象 DB の初期値は SQLite のみ ON）へ、上記ファイルを読み込む
            var loadFiles = new StubFileDialogService
            {
                OpenResult = new FileDialogResult(presetPath, 1),
            };
            var loadVm = new CSharpGenerationDialogViewModel(
                store,
                loadFiles,
                currentProvider: new QuickER.Sqlite.SqliteProvider()
            );

            loadVm.LoadSettingsFromCommand.Execute(null);

            // namespace と対象 DB チェックがファイル内容どおりに復元される
            loadVm.RootNamespace.Should().Be("Acme.Roundtrip");
            loadVm.GenerateRepositories.Should().BeTrue();
            loadVm.TargetSqlServer.Should().BeFalse("保存値どおり SQL Server は OFF");
            loadVm.TargetSqlite.Should().BeTrue("保存値どおり SQLite は ON");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// インメモリ実装チェックは既定 OFF で、ON にすると ToOptions・結果オプションへ反映され、次回起動時に復元されることを検証する
    /// </summary>
    [Fact(DisplayName = "インメモリ実装チェックは既定 OFF・ON で結果へ反映され復元される")]
    public void GenerateInMemory_IsReflectedInResult_AndPersisted()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";

            vm.GenerateInMemoryRepositories.Should().BeFalse("既定は OFF");
            vm.ToOptions().GenerateInMemoryRepositories.Should().BeFalse();

            vm.GenerateInMemoryRepositories = true;
            vm.OkCommand.Execute(null);

            vm.Result.Should().NotBeNull();
            vm.Result!.Options.GenerateInMemoryRepositories.Should().BeTrue();

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.GenerateInMemoryRepositories.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// インメモリ実装とパッケージ参照モードの併用は Ok で拒否され、専用のエラーメッセージが表示されることを検証する
    /// </summary>
    [Fact(DisplayName = "インメモリ実装＋パッケージ参照モードの併用は確定できない")]
    public void Ok_InMemoryWithRuntimePackages_ShowsConflictError()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.GenerateInMemoryRepositories = true;
        vm.UseRuntimePackages = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(CodeGenStrings.CodeGen_Status_InMemoryRuntimePackagesConflict);
    }

    /// <summary>
    /// UI 非表示の属性系（IncludeDataAnnotations / IncludeJsonIgnoreOnParentNavigation）が、
    /// 読み込んだ設定の値を保持したまま保存で書き戻され、生成オプション（ToOptions）へも反映されることを検証する
    /// </summary>
    [Fact(DisplayName = "UI 非表示の属性系は読込→保存で保持され ToOptions へ反映される")]
    public void HiddenAttributes_ArePreservedAcrossLoadSave_AndReflectedInOptions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var presetPath = Path.Combine(folder, "preset.json");
        var savedPath = Path.Combine(folder, "saved.json");
        var store = new CSharpGenerationSettingsStore(folder);

        // 既定 true の属性系を false に倒した設定ファイルを用意する（UI には出ないが値は保持されるべき）
        store.SaveTo(
            presetPath,
            new CSharpGenerationSettings
            {
                RootNamespace = "Acme.Loaded",
                IncludeDataAnnotations = false,
                IncludeJsonIgnoreOnParentNavigation = false,
            }
        );

        // 読込は presetPath・保存は savedPath を返すスタブ
        var files = new StubFileDialogService
        {
            OpenResult = new FileDialogResult(presetPath, 1),
            SaveResult = new FileDialogResult(savedPath, 1),
        };
        var vm = new CSharpGenerationDialogViewModel(store, files);

        try
        {
            vm.LoadSettingsFromCommand.Execute(null);

            // 読み込んだ属性値が生成オプションへ反映される
            var options = vm.ToOptions();
            options.IncludeDataAnnotations.Should().BeFalse();
            options.IncludeJsonIgnoreOnParentNavigation.Should().BeFalse();

            // 現在の表示状態を「名前を付けて保存」で書き出しても、属性値が失われず書き戻される
            vm.SaveSettingsAsCommand.Execute(null);

            var reloaded = store.TryLoadFrom(savedPath);
            reloaded.Should().NotBeNull();
            reloaded!.IncludeDataAnnotations.Should().BeFalse();
            reloaded.IncludeJsonIgnoreOnParentNavigation.Should().BeFalse();
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
