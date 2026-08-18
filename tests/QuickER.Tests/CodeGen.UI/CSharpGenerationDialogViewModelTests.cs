using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
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
    /// Repositories.EntityFrameworkCore.g.cs・{RepositoryNamespace}.EntityFrameworkCore へ導出される
    /// （専用の名前空間欄は持たない）ことを検証する
    /// </summary>
    [Fact(
        DisplayName = "EF Core 選択で EF Core 実装が {Repository}.EntityFrameworkCore へ導出される"
    )]
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
            .Contain(
                "Repositories.EntityFrameworkCore.g.cs  →  namespace Acme.App.Repositories.EntityFrameworkCore"
            );

        // ルート変更に伴い契約（Repositories）が追従すれば EF Core も自動的に追従する
        vm.RootNamespace = "Contoso.Sales";
        vm.PreviewFiles.Should()
            .Contain(
                "Repositories.EntityFrameworkCore.g.cs  →  namespace Contoso.Sales.Repositories.EntityFrameworkCore"
            );
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
    /// API リファレンス出力（GenerateApiDocs）を OFF にすると、下位の日本語版併産
    /// （IncludeJapaneseApiDocs）も OFF に連動することを検証する（無効化＋チェック残りの見かけ矛盾を防ぐ）
    /// </summary>
    [Fact(DisplayName = "API リファレンス出力 OFF で日本語版併産も OFF に連動する")]
    public void GenerateApiDocs_Off_TurnsOffJapaneseApiDocs()
    {
        var vm = CreateViewModel(out _);

        vm.GenerateApiDocs = true;
        vm.IncludeJapaneseApiDocs = true;

        // 親を OFF にすると子（日本語版併産）も OFF に戻る
        vm.GenerateApiDocs = false;
        vm.IncludeJapaneseApiDocs.Should().BeFalse("親 OFF で子も OFF に連動する");
    }

    /// <summary>
    /// 外部編集された設定ファイルが「親 OFF＋子 ON」の組み合わせでも、復元時に
    /// 「親 OFF なら子も OFF」の UI 不変条件へクランプされることを検証する
    /// </summary>
    [Fact(DisplayName = "設定復元時は親 OFF なら日本語版併産もクランプして OFF になる")]
    public void IncludeJapaneseApiDocs_RestoreClampsToGenerateApiDocs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings
                {
                    GenerateApiDocs = false,
                    IncludeJapaneseApiDocs = true,
                }
            );

            var vm = new CSharpGenerationDialogViewModel(store);

            vm.GenerateApiDocs.Should().BeFalse();
            vm.IncludeJapaneseApiDocs.Should().BeFalse("親 OFF の保存値は子もクランプして復元する");
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

    /// <summary>
    /// 層別出力を ON にすると分割出力が強制 ON になり、出力モードのラジオが操作不可になること、
    /// OFF に戻すと（分割の値はそのままに）操作可能へ戻ることを検証する（含意の可視化）
    /// </summary>
    [Fact(DisplayName = "層別出力 ON で分割出力が強制 ON＋出力モードが操作不可になる")]
    public void LayeredOutput_On_ForcesSplit_AndLocksOutputMode()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";

        vm.LayeredOutput.Should().BeFalse("既定は OFF");
        vm.SplitFilesByCategory.Should().BeFalse("既定は 1 ファイルにまとめる");
        vm.CanEditSplitFilesByCategory.Should().BeTrue("層別出力 OFF では出力モードを選べる");

        vm.LayeredOutput = true;

        vm.SplitFilesByCategory.Should().BeTrue("層別出力は分割出力を含意する");
        vm.MergeIntoSingleFile.Should().BeFalse();
        vm.CanEditSplitFilesByCategory.Should().BeFalse("含意の可視化として操作不可にする");
        vm.ShowLayerDirectories.Should().BeTrue("層フォルダの入力欄が現れる");

        vm.LayeredOutput = false;

        vm.CanEditSplitFilesByCategory.Should().BeTrue("OFF に戻すと出力モードを再び選べる");
        vm.SplitFilesByCategory.Should().BeTrue("分割出力の値そのものは維持する");
        vm.ShowLayerDirectories.Should().BeFalse("層フォルダの入力欄は隠れる");
    }

    /// <summary>層フォルダの入力欄が planner の既定フォルダ名でプリフィルされることを検証する</summary>
    [Fact(DisplayName = "層フォルダ欄は既定フォルダ名でプリフィルされる")]
    public void LayerDirectories_ArePrefilledWithDefaults()
    {
        var vm = CreateViewModel(out _);

        vm.DomainLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Domain));
        vm.InfrastructureLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Infrastructure));
        vm.PresentationLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Presentation));
        vm.ServerLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Server));
    }

    /// <summary>
    /// サーバー層フォルダの欄は「層別出力 ON かつリモートサービス生成 ON」のときだけ表示されることを検証する
    /// （サーバー層へ出るのはリモートサーバー実装だけのため）
    /// </summary>
    [Fact(DisplayName = "サーバー層フォルダ欄はリモートサービス生成 ON のときだけ表示される")]
    public void ShowServerLayerDirectory_TracksRemoteServices()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Acme.App";
        vm.DbAccessRepository = true;

        vm.ShowServerLayerDirectory.Should().BeFalse("層別出力 OFF では表示しない");

        vm.LayeredOutput = true;
        vm.ShowServerLayerDirectory.Should()
            .BeFalse("層別出力だけではサーバー層のファイルが出ないため表示しない");

        vm.GenerateRemoteServices = true;
        vm.ShowServerLayerDirectory.Should().BeTrue("リモートサービス生成 ON で表示する");

        vm.GenerateRemoteServices = false;
        vm.ShowServerLayerDirectory.Should().BeFalse("OFF に戻すと隠れる");

        vm.GenerateRemoteServices = true;
        vm.LayeredOutput = false;
        vm.ShowServerLayerDirectory.Should().BeFalse("層別出力 OFF でも隠れる");
    }

    /// <summary>層別出力と 4 つの層フォルダが結果オプション（CodeGenerationOptions）へ写像されることを検証する</summary>
    [Fact(DisplayName = "層別出力と層フォルダが生成オプションへ写像される")]
    public void LayeredOutput_AndDirectories_AreMappedToOptions()
    {
        var vm = CreateViewModel(out _, currentProvider: new QuickER.SqlServer.SqlServerProvider());
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\out";
        vm.DbAccessRepository = true;
        vm.GenerateRemoteServices = true;

        // 既定（OFF）では層別出力を要求せず、層フォルダも生成オプションへ影響しない
        vm.ToOptions().LayeredOutput.Should().BeFalse();

        vm.LayeredOutput = true;
        vm.DomainLayerDirectory = "Acme.Domain/Generated";
        vm.InfrastructureLayerDirectory = "Acme.Infrastructure";
        vm.PresentationLayerDirectory = "Acme.App/Generated";
        vm.ServerLayerDirectory = "Acme.Api";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        var options = vm.Result!.Options;
        options.LayeredOutput.Should().BeTrue();
        // 層別出力は分割を含意するため、分割フラグも ON のまま渡る（コア側は EffectiveSplitFilesByCategory で解釈）
        options.SplitFilesByCategory.Should().BeTrue();
        options.EffectiveSplitFilesByCategory.Should().BeTrue();
        options.DomainLayerDirectory.Should().Be("Acme.Domain/Generated");
        options.InfrastructureLayerDirectory.Should().Be("Acme.Infrastructure");
        options.PresentationLayerDirectory.Should().Be("Acme.App/Generated");
        options.ServerLayerDirectory.Should().Be("Acme.Api");
    }

    /// <summary>
    /// 層フォルダを空にすると生成オプションでは null へ畳まれ、planner の既定フォルダ名へフォールバックする
    /// ことを検証する（既存の名前空間欄と同じ流儀）
    /// </summary>
    [Fact(DisplayName = "空の層フォルダは null へ畳まれ既定フォルダ名へフォールバックする")]
    public void EmptyLayerDirectory_FallsBackToDefault()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\out";
        vm.LayeredOutput = true;
        vm.DomainLayerDirectory = "   ";

        var options = vm.ToOptions();

        options.DomainLayerDirectory.Should().BeNull();
        GeneratedFilePlanner
            .ResolveLayerDirectory(options, GeneratedLayer.Domain)
            .Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Domain));
    }

    /// <summary>
    /// 層別出力の切替で、生成ファイルのプレビュー表示に層フォルダが現れ・消えることを検証する
    /// （層別でないときは従来どおりファイル名のみ）
    /// </summary>
    [Fact(DisplayName = "層別出力の切替でプレビューに層フォルダが連動する")]
    public void LayeredOutput_TogglesLayerFolderInPreview()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;

        vm.PreviewFiles.Should()
            .Contain(line => line.StartsWith("Entities.g.cs", StringComparison.Ordinal));

        vm.LayeredOutput = true;

        vm.PreviewFiles.Should()
            .Contain(line => line.StartsWith("Domain/Entities.g.cs", StringComparison.Ordinal));
        vm.PreviewFiles.Should()
            .Contain(line =>
                line.StartsWith("Presentation/EditModels.g.cs", StringComparison.Ordinal)
            );

        // 層フォルダを変えるとプレビューも即座に追従する
        vm.DomainLayerDirectory = "Acme.Domain";
        vm.PreviewFiles.Should()
            .Contain(line =>
                line.StartsWith("Acme.Domain/Entities.g.cs", StringComparison.Ordinal)
            );

        vm.LayeredOutput = false;

        vm.PreviewFiles.Should()
            .Contain(line => line.StartsWith("Entities.g.cs", StringComparison.Ordinal));
    }

    /// <summary>層別出力と層フォルダの設定が保存され、次回起動時に復元されることを検証する</summary>
    [Fact(DisplayName = "層別出力と層フォルダが次回起動時に復元される")]
    public void LayeredOutput_AndDirectories_ArePersistedAndRestored()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\out";
            vm.LayeredOutput = true;
            vm.DomainLayerDirectory = "Acme.Domain";
            vm.InfrastructureLayerDirectory = "Acme.Infrastructure";
            vm.PresentationLayerDirectory = "Acme.App";
            vm.ServerLayerDirectory = "Acme.Api";
            vm.OkCommand.Execute(null);

            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );

            restored.LayeredOutput.Should().BeTrue();
            restored
                .SplitFilesByCategory.Should()
                .BeTrue("含意により分割出力も ON のまま復元される");
            restored.CanEditSplitFilesByCategory.Should().BeFalse();
            restored.DomainLayerDirectory.Should().Be("Acme.Domain");
            restored.InfrastructureLayerDirectory.Should().Be("Acme.Infrastructure");
            restored.PresentationLayerDirectory.Should().Be("Acme.App");
            restored.ServerLayerDirectory.Should().Be("Acme.Api");
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
    /// 外部編集された「層別 ON＋分割 OFF」の設定ファイルでも、復元時に UI 不変条件
    /// （層別出力は分割を含意する）へ揃うことを検証する
    /// </summary>
    [Fact(DisplayName = "層別 ON＋分割 OFF の保存値は復元時に分割 ON へ揃う")]
    public void LayeredOutput_Restore_ForcesSplitOn()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings { LayeredOutput = true, SplitFilesByCategory = false }
            );

            var vm = new CSharpGenerationDialogViewModel(store);

            vm.LayeredOutput.Should().BeTrue();
            vm.SplitFilesByCategory.Should().BeTrue("含意により分割出力へ強制される");
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
    /// 「層別 ON＋名前空間欄が空」の設定を開くと、名前空間欄が層フォルダ由来の既定
    /// （<c>Domain.Entities</c> など）でプリフィルされることを検証する
    /// </summary>
    [Fact(DisplayName = "層別 ON の設定を開くと名前空間が層フォルダ由来でプリフィルされる")]
    public void ApplySettings_LayeredOutput_PrefillsNamespacesFromLayerFolders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            // 名前空間は 6 欄とも空（＝未指定）で保存する
            store.Save(
                new CSharpGenerationSettings { RootNamespace = "Acme.App", LayeredOutput = true }
            );

            var vm = new CSharpGenerationDialogViewModel(store);

            vm.EntityNamespace.Should().Be("Domain.Entities");
            vm.ValueObjectNamespace.Should().Be("Domain.ValueObjects");
            vm.RepositoryNamespace.Should().Be("Domain.Repositories");
            vm.RuntimeNamespace.Should().Be("Domain.Runtime");
            vm.EditModelNamespace.Should().Be("Presentation.EditModels");
            vm.MapperNamespace.Should().Be("Presentation.Mappers");
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
    /// 層フォルダが複数階層（<c>MyApp.Domain/Generated</c>）でも、名前空間の既定が区切りをドットへ
    /// 変換した形（<c>MyApp.Domain.Generated.Entities</c>）でプリフィルされることを検証する
    /// </summary>
    [Fact(DisplayName = "複数階層の層フォルダはドット区切りの名前空間既定になる")]
    public void ApplySettings_NestedLayerFolder_PrefillsDottedNamespace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings
                {
                    RootNamespace = "Acme.App",
                    LayeredOutput = true,
                    DomainLayerDirectory = "MyApp.Domain/Generated",
                }
            );

            var vm = new CSharpGenerationDialogViewModel(store);

            vm.EntityNamespace.Should().Be("MyApp.Domain.Generated.Entities");
            // 明示しなかった層は既定フォルダ由来のまま
            vm.EditModelNamespace.Should().Be("Presentation.EditModels");
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
    /// 層別出力の ON/OFF 切替で、既定のままの名前空間欄が新しい既定へ追従し、
    /// 手編集済みの欄は保持されることを検証する
    /// </summary>
    [Fact(DisplayName = "層別出力の切替で既定のままの名前空間だけが追従する")]
    public void LayeredOutput_Toggle_FollowsDefaultNamespacesOnly()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        // EditModel だけ手編集（追従対象外にする）
        vm.EditModelNamespace = "Custom.Edit";

        vm.EntityNamespace.Should().Be("Acme.App.Entities", "層別 OFF の既定はルート由来");

        vm.LayeredOutput = true;

        vm.EntityNamespace.Should().Be("Domain.Entities", "層別 ON の既定は層フォルダ由来");
        vm.MapperNamespace.Should().Be("Presentation.Mappers");
        vm.EditModelNamespace.Should().Be("Custom.Edit", "手編集済みの欄は触らない");

        vm.LayeredOutput = false;

        vm.EntityNamespace.Should().Be("Acme.App.Entities", "OFF に戻すとルート由来の既定へ戻る");
        vm.MapperNamespace.Should().Be("Acme.App.Mappers");
        vm.EditModelNamespace.Should().Be("Custom.Edit", "手編集済みの欄は戻すときも触らない");
    }

    /// <summary>
    /// 層フォルダを編集すると、その層に属するバケットの名前空間欄だけが追従し、
    /// 他の層の欄と手編集済みの欄は変わらないことを検証する
    /// </summary>
    [Fact(DisplayName = "層フォルダの編集はその層の名前空間だけを追従させる")]
    public void LayerDirectoryEdit_FollowsOnlyThatLayersNamespaces()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.LayeredOutput = true;

        vm.DomainLayerDirectory = "MyApp.Domain";

        // ドメイン層のバケット（Entity / ValueObject / Repository 契約 / Runtime コア）が追従する
        vm.EntityNamespace.Should().Be("MyApp.Domain.Entities");
        vm.ValueObjectNamespace.Should().Be("MyApp.Domain.ValueObjects");
        vm.RepositoryNamespace.Should().Be("MyApp.Domain.Repositories");
        vm.RuntimeNamespace.Should().Be("MyApp.Domain.Runtime");
        // プレゼンテーション層は無関係
        vm.EditModelNamespace.Should().Be("Presentation.EditModels");
        vm.MapperNamespace.Should().Be("Presentation.Mappers");

        // 手編集した欄は、その層のフォルダを変えても保持する
        vm.MapperNamespace = "Custom.Mapping";
        vm.PresentationLayerDirectory = "MyApp.App/Ui";

        vm.EditModelNamespace.Should().Be("MyApp.App.Ui.EditModels");
        vm.MapperNamespace.Should().Be("Custom.Mapping");
        vm.EntityNamespace.Should().Be("MyApp.Domain.Entities", "ドメイン層は影響を受けない");
    }

    /// <summary>
    /// 層別出力時、手編集した名前空間欄だけが明示指定として生成オプションへ渡り、既定のままの欄は
    /// 導出（null）に任せても同じ名前空間へ解決されること、プレビューが層フォルダ・層由来名前空間の
    /// 両方を反映することを検証する
    /// </summary>
    [Fact(DisplayName = "層別出力時は手編集欄のみ明示指定で渡り既定欄は導出に任せる")]
    public void LayeredOutput_NamespaceFields_ArePassedThroughToOptions()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\out";
        vm.LayeredOutput = true;
        vm.DomainLayerDirectory = "MyApp.Domain";
        // Entity だけ手編集（明示指定が既定より優先されること）
        vm.EntityNamespace = "Custom.Entities";

        var options = vm.ToOptions();

        options.EntityNamespace.Should().Be("Custom.Entities");
        // 既定のままの欄は明示値として渡さず導出（null）に任せる（既定を明示値化すると
        // 設定へ持ち越されたとき層フォルダ変更への追従が止まるため）。導出結果は欄の表示と一致する
        options.MapperNamespace.Should().BeNull();
        options.RuntimeNamespace.Should().BeNull();
        GeneratedFilePlanner
            .ResolveNamespace(options, GenerationBucket.Mapper)
            .Should()
            .Be("Presentation.Mappers");
        GeneratedFilePlanner
            .ResolveNamespace(options, GenerationBucket.Runtime)
            .Should()
            .Be("MyApp.Domain.Runtime");

        // プレビューは層フォルダ配置と名前空間の両方に追従する（Plan 経由のため自動）
        vm.PreviewFiles.Should()
            .Contain("MyApp.Domain/Entities.g.cs  →  namespace Custom.Entities");
        vm.PreviewFiles.Should()
            .Contain("Presentation/Mappers.g.cs  →  namespace Presentation.Mappers");
    }

    /// <summary>クリアで層別出力と層フォルダが工場出荷既定（OFF・既定フォルダ名）へ戻ることを検証する</summary>
    [Fact(DisplayName = "クリアで層別出力が OFF・層フォルダが既定名へ戻る")]
    public void Clear_RestoresLayeredOutputDefaults()
    {
        var vm = CreateViewModel(out _);
        vm.LayeredOutput = true;
        vm.DomainLayerDirectory = "Acme.Domain";

        vm.ClearCommand.Execute(null);

        vm.LayeredOutput.Should().BeFalse();
        vm.CanEditSplitFilesByCategory.Should().BeTrue();
        vm.DomainLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Domain));
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
    /// インメモリ実装とパッケージ参照モードの併用が確定できることを検証する
    /// （インメモリ基盤は QuickER.Runtime.InMemory パッケージが担うため、旧・併用不可チェックは撤去済み）
    /// </summary>
    [Fact(DisplayName = "インメモリ実装＋パッケージ参照モードの併用は確定できる")]
    public void Ok_InMemoryWithRuntimePackages_Succeeds()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.GenerateInMemoryRepositories = true;
        vm.UseRuntimePackages = true;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.Options.GenerateInMemoryRepositories.Should().BeTrue();
        vm.Result.Options.UseRuntimePackages.Should().BeTrue();
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

    /// <summary>
    /// 旧ビルドが実体化して保存した既定名前空間（{root}.{接尾辞} 形）は読込時に未編集として回復され、
    /// 層フォルダの変更へ追従することを検証する（保存済みの既定値が手編集扱いになり追従が止まっていた不具合の再現）
    /// </summary>
    [Fact(DisplayName = "保存済みの実体化既定は読込時に回復され層フォルダ変更へ追従する")]
    public void LegacyMaterializedNamespaces_AreHealed_AndFollowLayerFolders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            // 旧ビルド相当の保存値: 層別 ON なのに名前空間は {root}.{接尾辞} 形の実体化値・Mapper だけ真の手編集値
            new CSharpGenerationSettingsStore(folder).Save(
                new CSharpGenerationSettings
                {
                    SplitFilesByCategory = true,
                    LayeredOutput = true,
                    RootNamespace = "Acme.App",
                    RuntimeNamespace = "Acme.App.Runtime",
                    EntityNamespace = "Acme.App.Entities",
                    EditModelNamespace = "Acme.App.EditModels",
                    MapperNamespace = "Custom.Mappers",
                    RepositoryNamespace = "Acme.App.Repositories",
                    ValueObjectNamespace = "Acme.App.ValueObjects",
                }
            );

            var vm = new CSharpGenerationDialogViewModel(new CSharpGenerationSettingsStore(folder));

            // 実体化された旧既定は現在のモードの既定（層フォルダ由来）へ回復し、手編集値だけが残る
            vm.EntityNamespace.Should().Be("Domain.Entities");
            vm.RuntimeNamespace.Should().Be("Domain.Runtime");
            vm.RepositoryNamespace.Should().Be("Domain.Repositories");
            vm.EditModelNamespace.Should().Be("Presentation.EditModels");
            vm.MapperNamespace.Should().Be("Custom.Mappers");

            // 回復後は層フォルダの変更に追従する（報告された症状の end-to-end）
            vm.DomainLayerDirectory = "MyApp.Domain";

            vm.EntityNamespace.Should().Be("MyApp.Domain.Entities");
            vm.RuntimeNamespace.Should().Be("MyApp.Domain.Runtime");
            vm.MapperNamespace.Should().Be("Custom.Mappers", "手編集値は追従で上書きしない");
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
    /// 既定と一致する名前空間欄・層フォルダ欄は空で永続化され（既定を明示値として持ち越さない）、
    /// 生成オプションへも導出（null）として渡ることを検証する
    /// </summary>
    [Fact(DisplayName = "既定のままの名前空間・層フォルダは空で保存され導出として生成へ渡る")]
    public void DefaultNamespacesAndLayerFolders_PersistAsBlank()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.LayeredOutput = true;
            vm.OutputPath = @"C:\temp";
            vm.MapperNamespace = "Custom.Mappers";
            vm.CloseAction = _ => { };

            vm.OkCommand.Execute(null);

            // 生成オプション: 既定のままの欄は導出（null）・手編集値は明示指定
            vm.Result.Should().NotBeNull();
            vm.Result!.Options.LayeredOutput.Should().BeTrue();
            vm.Result.Options.EntityNamespace.Should().BeNull("既定のままの欄は導出に任せる");
            vm.Result.Options.MapperNamespace.Should().Be("Custom.Mappers");

            // 永続化: 既定のままの欄・既定フォルダは空で保存される
            var saved = new CSharpGenerationSettingsStore(folder).Load();
            saved.LayeredOutput.Should().BeTrue();
            saved.EntityNamespace.Should().BeEmpty();
            saved.RuntimeNamespace.Should().BeEmpty();
            saved.DomainLayerDirectory.Should().BeEmpty();
            saved.MapperNamespace.Should().Be("Custom.Mappers");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>API リファレンスの出力先サブフォルダが生成オプションへ渡り、保存・復元されることを検証する</summary>
    [Fact(DisplayName = "ApiDocsDirectory が生成オプションへ渡り保存・復元される")]
    public void ApiDocsDirectory_MapsToOptions_AndPersists()
    {
        var vm = CreateViewModel(out var folder);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";
            vm.GenerateApiDocs = true;
            vm.ApiDocsDirectory = " docs ";
            vm.CloseAction = _ => { };

            // 前後空白は除去して明示指定として渡る
            vm.ToOptions().ApiDocsDirectory.Should().Be("docs");

            vm.OkCommand.Execute(null);
            var restored = new CSharpGenerationDialogViewModel(
                new CSharpGenerationSettingsStore(folder)
            );
            restored.GenerateApiDocs.Should().BeTrue();
            restored.ApiDocsDirectory.Should().Be("docs");

            // 空欄は null（既定＝出力フォルダ直下）として生成へ渡る
            vm.ApiDocsDirectory = string.Empty;
            vm.ToOptions().ApiDocsDirectory.Should().BeNull();
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
