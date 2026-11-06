using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;

namespace QuickER.Tests.CodeGen.UI;

/// <summary><see cref="CSharpGenerationDialogViewModel" /> の入力確定・検証・参照・分割・永続化を検証するテストクラス</summary>
public sealed class CSharpGenerationDialogViewModelTests : IDisposable
{
    /// <summary>テストごとの一時フォルダ（設定の保存先・一時プロジェクトはこの下に作り、破棄で中身ごと消す）</summary>
    private readonly TestTempFolder temp = new();

    /// <inheritdoc />
    public void Dispose() => temp.Dispose();

    /// <summary>一時フォルダの下に、まだ無い一意のフォルダのパスを返す（作らない）</summary>
    private string NewFolder() => Path.Combine(temp.Path, Guid.NewGuid().ToString("N"));

    /// <summary>一時フォルダのストアで ViewModel を生成する（実 %LOCALAPPDATA% を汚さない）</summary>
    private CSharpGenerationDialogViewModel CreateViewModel(
        out string folder,
        QuickER.Provider.IDatabaseProvider? currentProvider = null
    )
    {
        folder = NewFolder();
        return new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(folder),
            currentProvider: currentProvider
        );
    }

    /// <summary>
    /// DB アクセスを操作せずに確定すると、QuickER 版 Repository も EF Core も生成しない（既定は「なし」）ことを検証する
    /// </summary>
    /// <remarks>
    /// 確定の結果（namespace・出力先・閉じること）は <c>CodegenCaseTests</c> の行 <c>gui-no-db-access-single-file</c>（GUI の入口）が見る。
    /// 入口は全部のプロパティを画面へ入れるので、操作しないときの既定はここで見る。
    /// </remarks>
    [Fact(
        DisplayName = "DB アクセス未操作の確定では QuickER 版 Repository も EF Core も生成しない"
    )]
    public void Ok_Untouched_GeneratesNoDbAccess()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        // DB アクセスの既定は「なし」（QuickER 版 Repository も EF Core も生成しない）
        vm.Result!.Options.GenerateRepositories.Should().BeFalse();
        vm.Result.Options.GenerateEfCoreRepositories.Should().BeFalse();
    }

    /// <summary>DB アクセスラジオが排他で動くことを検証する（選択が結果へ渡ることは GUI の入口の行が見る）</summary>
    [Fact(DisplayName = "DB アクセスラジオは排他選択で動く")]
    public void DbAccessRadios_AreExclusive()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.DbAccessNone.Should().BeTrue("既定は「なし」");

        vm.DbAccessEfCore = true;
        vm.GenerateEfCoreRepositories.Should().BeTrue();
        vm.GenerateRepositories.Should().BeFalse();

        vm.DbAccessRepository = true;
        vm.GenerateRepositories.Should().BeTrue();
        vm.GenerateEfCoreRepositories.Should().BeFalse("排他選択のため EF Core は外れる");
    }

    /// <summary>未対応方言のプロバイダでもQuickER 版 Repository ラジオは常時選択可であり、対象 DB チェックは両方 OFF から始まることを検証する</summary>
    [Theory(
        DisplayName = "未対応方言でもQuickER 版 Repository ラジオは選択可・対象 DB チェックは両方 OFF から始まる"
    )]
    [InlineData(typeof(QuickER.Provider.PostgreSql.PostgreSqlProvider))]
    [InlineData(typeof(QuickER.Provider.MySql.MySqlProvider))]
    [InlineData(typeof(QuickER.Provider.Oracle.OracleProvider))]
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
    [InlineData(typeof(QuickER.Provider.SqlServer.SqlServerProvider))]
    [InlineData(typeof(QuickER.Provider.Sqlite.SqliteProvider))]
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

    /// <summary>対象 DB を 1 つもチェックしないままQuickER 版 Repository を確定しようとすると拒否されることを検証する</summary>
    [Fact(DisplayName = "対象 DB 0 個では確定できない")]
    public void Ok_Repository_WithNoTargetDialects_ShowsError()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.PostgreSql.PostgreSqlProvider()
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

    /// <summary>API リファレンス出力チェックの既定は OFF であることを検証する（ON が結果へ渡ることは GUI の入口の行が見る）</summary>
    [Fact(DisplayName = "API リファレンス出力チェックは既定 OFF")]
    public void GenerateApiDocs_DefaultsOff()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.GenerateApiDocs.Should().BeFalse("既定は OFF");
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

    /// <summary>
    /// 言語ラジオ 3 つが ApiDocsLanguage と相互に同期し、外れた側の false 通知では言語が変わらないことを検証する
    /// </summary>
    [Fact(DisplayName = "API リファレンスの言語ラジオは ApiDocsLanguage と同期する")]
    public void ApiDocsLanguageRadios_SyncWithLanguage()
    {
        var vm = CreateViewModel(out _);

        vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.English, "既定は英語");
        vm.ApiDocsEnglish.Should().BeTrue();

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ApiDocsJapanese = true;
        vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.Japanese);
        vm.ApiDocsEnglish.Should().BeFalse();
        vm.ApiDocsBoth.Should().BeFalse();
        changed
            .Should()
            .Contain([
                nameof(vm.ApiDocsEnglish),
                nameof(vm.ApiDocsJapanese),
                nameof(vm.ApiDocsBoth),
                nameof(vm.ApiDocsFileNameHint),
            ]);

        // WPF のラジオは外れた側へ false を書き戻す。それで言語が変わってはいけない
        vm.ApiDocsEnglish = false;
        vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.Japanese);

        vm.ApiDocsBoth = true;
        vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.Both);
    }

    /// <summary>
    /// API リファレンス出力を OFF にしても言語の選択は保持され、再び ON にしたとき元の言語で出ることを検証する
    /// （隠れている間の値は GenerateApiDocs=false のため生成に効かない）
    /// </summary>
    [Fact(DisplayName = "API リファレンス出力 OFF でも言語の選択は保持される")]
    public void GenerateApiDocs_Off_KeepsLanguage()
    {
        var vm = CreateViewModel(out _);

        vm.GenerateApiDocs = true;
        vm.ApiDocsJapanese = true;

        vm.GenerateApiDocs = false;
        vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.Japanese);

        vm.GenerateApiDocs = true;
        vm.ApiDocsJapanese.Should().BeTrue();
    }

    /// <summary>
    /// 外部編集で未定義の数値が入っていた言語を復元すると、どのラジオも選ばれない表示にならず既定（英語）へ戻ることを検証する
    /// （保存した言語の復元は GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "設定復元時に未定義の言語の値は英語へ戻す")]
    public void ApiDocsLanguage_UndefinedSavedValue_FallsBackToEnglish()
    {
        var folder = NewFolder();

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings
                {
                    GenerateApiDocs = true,
                    ApiDocsLanguage = (ApiDocsLanguage)99,
                }
            );

            var vm = new CSharpGenerationDialogViewModel(store);

            vm.ApiDocsLanguage.Should().Be(ApiDocsLanguage.English);
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
    /// 無制限バイナリ列の除外チェックの既定は OFF で、結果オプションの ExcludeUnboundedBinaryColumns も false であることを検証する
    /// （ON が結果へ渡ることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "無制限バイナリ列の除外は既定 OFF・ExcludeUnboundedBinaryColumns も false")]
    public void ExcludeUnboundedBinary_DefaultsOff()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        vm.ExcludeUnboundedBinaryColumns.Should().BeFalse("既定は OFF");
        vm.ToOptions().ExcludeUnboundedBinaryColumns.Should().BeFalse();
    }

    /// <summary>
    /// リモート対応チェックの既定は OFF で、結果オプションの GenerateRemoteContracts も false であることを検証する
    /// （ON が結果へ渡ることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "リモート対応は既定 OFF・GenerateRemoteContracts も false")]
    public void GenerateRemoteContracts_DefaultsOff()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
        vm.RootNamespace = "Sample.Domain";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.DbAccessRepository = true;

        vm.GenerateRemoteContracts.Should().BeFalse("既定は OFF");
        vm.ToOptions().GenerateRemoteContracts.Should().BeFalse();
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
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
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
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
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
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
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

    /// <summary>
    /// 層別出力を ON にすると分割出力が強制 ON になること、OFF に戻しても分割の値は維持されることを検証する
    /// </summary>
    [Fact(DisplayName = "層別出力 ON で分割出力が強制 ON になる")]
    public void LayeredOutput_On_ForcesSplit()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";

        vm.LayeredOutput.Should().BeFalse("既定は OFF");
        vm.SplitFilesByCategory.Should().BeFalse("既定は 1 ファイルにまとめる");

        vm.LayeredOutput = true;

        vm.SplitFilesByCategory.Should().BeTrue("層別出力は分割出力を含意する");
        vm.MergeIntoSingleFile.Should().BeFalse();
        vm.ShowLayerDirectories.Should().BeTrue("層フォルダの入力欄が現れる");

        vm.LayeredOutput = false;

        vm.SplitFilesByCategory.Should().BeTrue("分割出力の値そのものは維持する");
        vm.ShowLayerDirectories.Should().BeFalse("層フォルダの入力欄は隠れる");
    }

    /// <summary>
    /// 層別出力 ON の状態で「1 ファイルにまとめる」を選べること、選ぶと層別出力のチェックが外れ、
    /// 層フォルダ由来だった名前空間の既定がルート由来へ戻ることを検証する（含意の逆向き）
    /// </summary>
    [Fact(DisplayName = "1 ファイルにまとめるを選ぶと層別出力が OFF になる")]
    public void MergeIntoSingleFile_TurnsOffLayeredOutput()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.LayeredOutput = true;

        vm.EntityNamespace.Should()
            .Be("Domain.Entities", "層別出力 ON の間は層フォルダ由来の既定になる");

        // ラジオは層別出力 ON でも操作できる（XAML の IsEnabled バインドを撤去した経路）
        vm.MergeIntoSingleFile = true;

        vm.SplitFilesByCategory.Should().BeFalse("1 ファイルにまとめるが選べる");
        vm.LayeredOutput.Should().BeFalse("単一ファイルは層へ割れないためチェックが外れる");
        vm.ShowLayerDirectories.Should().BeFalse("層フォルダの入力欄は隠れる");
        vm.ShowSingleFileOutput.Should().BeTrue("単一ファイルの出力ファイル欄が現れる");
        vm.EntityNamespace.Should().Be("Acme.App.Entities", "名前空間の既定はルート由来へ戻る");
    }

    /// <summary>層フォルダの入力欄が planner の既定フォルダ名でプリフィルされることを検証する</summary>
    [Fact(DisplayName = "層フォルダ欄は既定フォルダ名でプリフィルされる")]
    public void LayerDirectories_ArePrefilledWithDefaults()
    {
        var vm = CreateViewModel(out _);

        vm.DomainLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Domain));
        vm.PresentationLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Presentation));
        vm.InfrastructureLayerDirectory.Should()
            .Be(GeneratedFilePlanner.DefaultLayerDirectory(GeneratedLayer.Infrastructure));
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
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
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

    /// <summary>層別出力の既定は OFF で、生成オプションへも層別出力を要求しないことを検証する（ON の写像は GUI の入口の行が見る）</summary>
    [Fact(DisplayName = "層別出力は既定 OFF で生成オプションへも渡らない")]
    public void LayeredOutput_DefaultsOff_InOptions()
    {
        var vm = CreateViewModel(
            out _,
            currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
        );
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\out";
        vm.DbAccessRepository = true;
        vm.GenerateRemoteServices = true;

        // 既定（OFF）では層別出力を要求しない
        vm.ToOptions().LayeredOutput.Should().BeFalse();
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
    /// （層別でないときはファイル名のみ）
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

    /// <summary>
    /// 外部編集された「層別 ON＋分割 OFF」の設定ファイルでも、復元時に UI 不変条件
    /// （層別出力は分割を含意する）へ揃うことを検証する
    /// </summary>
    [Fact(DisplayName = "層別 ON＋分割 OFF の保存値は復元時に分割 ON へ揃う")]
    public void LayeredOutput_Restore_ForcesSplitOn()
    {
        var folder = NewFolder();

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
        var folder = NewFolder();

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
        var folder = NewFolder();

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
    /// 層別出力時、既定のままの名前空間欄を導出（null）に任せても欄の表示と同じ名前空間へ解決されること、
    /// プレビューが層フォルダ・層由来名前空間の両方を反映することを検証する
    /// （手編集欄だけが明示指定で渡り既定欄は null で渡ることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "層別出力時の既定欄の導出は欄の表示と一致しプレビューへ反映される")]
    public void LayeredOutput_NamespaceFields_ResolveToDisplayedDefaults()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\out";
        vm.LayeredOutput = true;
        vm.DomainLayerDirectory = "MyApp.Domain";
        // Entity だけ手編集（明示指定が既定より優先されること）
        vm.EntityNamespace = "Custom.Entities";

        var options = vm.ToOptions();

        // 既定のままの欄は導出に任せる。導出結果は欄の表示と一致する
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

    /// <summary>出力に使われるカテゴリ別名前空間が不正なら、従来どおり項目名を添えて止めることを検証する</summary>
    [Fact(DisplayName = "使われるカテゴリ別名前空間が不正なら項目名を添えて止める")]
    public void Ok_Split_InvalidUsedChildNamespace_ShowsError()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.OutputPath = @"C:\temp\Generated";
        vm.EntityNamespace = "1Invalid";

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should()
            .Be(
                string.Format(
                    CodeGenStrings.CodeGen_Status_NamespaceInvalidFormat,
                    CodeGenStrings.CodeGen_NamespaceLabel_Entity
                )
            );
    }

    /// <summary>
    /// 出力に使われない名前空間（パッケージ参照モードの Runtime 名前空間）は、欄が見えていても検証しないことを検証する
    /// </summary>
    /// <remarks>
    /// パッケージ参照モードでは固定コード（Runtime 系ファイル）を出さないので、Runtime 名前空間はどこにも使われない。
    /// 生成前の検証（コア）と同じく、計画が記録した出所（<c>UsedNamespaceSources</c>）で判定する。
    /// </remarks>
    [Fact(DisplayName = "パッケージ参照モードの Runtime 名前空間は不正でも止めない")]
    public void Ok_RuntimePackages_IgnoresUnusedRuntimeNamespace()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.SplitFilesByCategory = true;
        vm.OutputPath = @"C:\temp\Generated";
        vm.UseRuntimePackages = true;
        vm.RuntimeNamespace = "1Invalid";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull("Runtime 名前空間は出力に使われない");
        vm.StatusMessage.Should().BeEmpty();
    }

    /// <summary>
    /// 層別出力ではルート名前空間が使われないので、空でも不正でも止めず、空のときもプレビューを出すことを検証する
    /// </summary>
    /// <remarks>層別出力の名前空間の既定は層フォルダから導出され、ルート名前空間はどこにも使われない</remarks>
    [Theory(DisplayName = "層別出力ではルート名前空間が空でも不正でも止めずプレビューを出す")]
    [InlineData("")]
    [InlineData("1Invalid")]
    public void Ok_LayeredOutput_IgnoresUnusedRootNamespace(string rootNamespace)
    {
        var vm = CreateViewModel(out _);
        vm.LayeredOutput = true;
        vm.OutputPath = @"C:\temp\Generated";
        vm.RootNamespace = rootNamespace;

        vm.PreviewFiles.Should()
            .NotBeEmpty("ルート名前空間が使われない構成ではプレビューを止めない");

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull("ルート名前空間は出力に使われない");
    }

    /// <summary>ルート名前空間が使われる構成で空なら、プレビューを出さず OK も必須エラーで止めることを検証する</summary>
    [Fact(DisplayName = "ルート名前空間が使われる構成で空ならプレビューを出さず必須エラーで止める")]
    public void Ok_UsedRootNamespaceEmpty_ShowsRequiredError()
    {
        var vm = CreateViewModel(out _);
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.RootNamespace = string.Empty;

        vm.PreviewFiles.Should().BeEmpty();

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(CodeGenStrings.CodeGen_Status_NamespaceRequired);
    }

    /// <summary>参照コマンドで選択したパスが出力先へ反映されることを検証する</summary>
    [Fact(DisplayName = "参照コマンドで出力先ファイルを更新できる")]
    public void BrowseOutputFile_UpdatesPath()
    {
        var folder = NewFolder();
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
    private string CreateProjectFolder(string csprojFileName, string rootNamespace)
    {
        var projectDir = NewFolder();
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
        var settingsFolder = NewFolder();
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
        var settingsFolder = NewFolder();
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
        var settingsFolder = NewFolder();
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

    /// <summary>
    /// OK で設定を保存できなくても、原因を添えたエラーで知らせたうえで生成は続ける（クラッシュ処理へ入らない）ことを検証する
    /// </summary>
    /// <remarks>
    /// 保存先フォルダの位置に同名のファイルがあると、保存は IOException になる。捕まえないと RelayCommand から
    /// 例外が抜け、生成も実行されずにアプリがクラッシュ時の処理へ入っていた。設定の保存は生成に付随する処理なので、
    /// 知らせたうえで生成は続ける。
    /// </remarks>
    [Fact(DisplayName = "OK で設定を保存できなくてもエラーで知らせて生成は続ける")]
    public void Ok_SettingsSaveFails_ShowsErrorAndContinues()
    {
        var root = NewFolder();
        Directory.CreateDirectory(root);
        var blockedFolder = Path.Combine(root, "settings");
        File.WriteAllText(blockedFolder, "not a folder");
        var store = new CSharpGenerationSettingsStore(blockedFolder);
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, dialogs: dialogs);
        var closed = false;
        vm.CloseAction = _ => closed = true;

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";

            var act = () => vm.OkCommand.Execute(null);

            act.Should().NotThrow();
            vm.Result.Should().NotBeNull("設定を保存できなくても生成は続ける");
            closed.Should().BeTrue();
            dialogs
                .ErrorMessages.Should()
                .ContainSingle()
                .Which.Should()
                .StartWith(
                    string.Format(
                        CodeGenStrings.CodeGen_SettingsAutoSaveFailedMessage,
                        store.SettingsPath
                    )
                );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 既存の設定ファイルを読めないために保存を見送ったときも、その旨をエラーで知らせて生成は続けることを検証する
    /// </summary>
    /// <remarks>
    /// 設定ストアは、読めないファイルを既定値で塗り潰さないよう何も書かずに戻る（例外にならない）。
    /// 戻り値を見ないと、保存されていないのに何も知らせない形になる。
    /// </remarks>
    [Fact(
        DisplayName = "OK で設定ファイルを読めず保存を見送ったときもエラーで知らせて生成は続ける"
    )]
    public void Ok_SettingsFileUnreadable_ShowsErrorAndContinues()
    {
        var folder = NewFolder();
        var store = new CSharpGenerationSettingsStore(folder);
        store.Save(new CSharpGenerationSettings());
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(store, dialogs: dialogs);

        try
        {
            vm.RootNamespace = "Acme.App";
            vm.OutputPath = @"C:\temp\Entities.g.cs";

            using (
                new FileStream(store.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None)
            )
            {
                vm.OkCommand.Execute(null);
            }

            vm.Result.Should().NotBeNull("設定を保存できなくても生成は続ける");
            dialogs
                .ErrorMessages.Should()
                .ContainSingle()
                .Which.Should()
                .Be(
                    string.Format(
                        CodeGenStrings.CodeGen_SettingsAutoSaveFailedMessage,
                        store.SettingsPath
                    )
                        + Environment.NewLine
                        + CodeGenStrings.CodeGen_SettingsFileUnreadable
                );
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// 単一ファイル⇄分割の切替で、出力先がファイルの親フォルダ⇔フォルダ＋直前のファイル名へ変換されることを検証する
    /// </summary>
    /// <remarks>
    /// 出力先の欄は 1 つで、単一ではファイルパス・分割ではフォルダパスを表す。変換しないと、単一の
    /// <c>C:\out\Foo.g.cs</c> が分割で <c>Foo.g.cs</c> という名前のフォルダになり、分割のフォルダが単一で
    /// フォルダの隣のファイルになる。層別出力（分割を含意する）の ON も同じ変換を通る。
    /// </remarks>
    [Theory(DisplayName = "出力モードの切替で出力先がファイル⇔フォルダへ変換され、往復で元に戻る")]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputModeToggle_ConvertsOutputPath(bool viaLayeredOutput)
    {
        var vm = CreateViewModel(out _);
        vm.OutputPath = @"C:\out\Foo.g.cs";

        if (viaLayeredOutput)
        {
            vm.LayeredOutput = true;
        }
        else
        {
            vm.SplitFilesByCategory = true;
        }

        vm.OutputPath.Should().Be(@"C:\out");

        vm.MergeIntoSingleFile = true;

        vm.OutputPath.Should().Be(@"C:\out\Foo.g.cs", "直前に単一で使っていたファイル名へ戻る");
    }

    /// <summary>分割で開いたフォルダから単一へ切り替えると、既定のファイル名がフォルダの中に付くことを検証する</summary>
    [Fact(
        DisplayName = "単一で使ったファイル名が無ければ、分割のフォルダに既定のファイル名を付ける"
    )]
    public void OutputModeToggle_FromSplitWithoutRememberedName_UsesDefaultFileName()
    {
        var folder = NewFolder();
        var store = new CSharpGenerationSettingsStore(folder);
        store.Save(
            new CSharpGenerationSettings
            {
                SplitFilesByCategory = true,
                OutputPath = @"C:\App\Generated",
            }
        );

        try
        {
            var vm = new CSharpGenerationDialogViewModel(store);

            vm.OutputPath.Should().Be(@"C:\App\Generated", "設定の適用では変換しない");

            vm.MergeIntoSingleFile = true;

            vm.OutputPath.Should()
                .Be(
                    Path.Combine(
                        @"C:\App\Generated",
                        CSharpGenerationSettings.DefaultOutputFilePath
                    )
                );
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>空の出力先は、どちらへ切り替えても空のままであることを検証する</summary>
    [Fact(DisplayName = "空の出力先は出力モードを切り替えても空のまま")]
    public void OutputModeToggle_EmptyPath_StaysEmpty()
    {
        var vm = CreateViewModel(out _);
        vm.OutputPath = string.Empty;

        vm.SplitFilesByCategory = true;
        vm.OutputPath.Should().BeEmpty();

        vm.MergeIntoSingleFile = true;
        vm.OutputPath.Should().BeEmpty();
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

        public bool ConfirmWarningDetails(string message, string details, string title) => false;

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

    /// <summary>「名前を付けて保存」で現在の設定が任意ファイルへ書き出され、TryLoadFrom で同内容に読み戻せることを検証する</summary>
    [Fact(DisplayName = "名前を付けて保存で設定ファイルが生成され読み戻せる")]
    public void SaveSettingsAs_WritesFile_ThatCanBeLoadedBack()
    {
        var folder = NewFolder();
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
            loaded.GenerateEfCoreRepositories.Should().BeTrue();
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
        var folder = NewFolder();
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
        var folder = NewFolder();
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
                GenerateEfCoreRepositories = true,
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
            vm.GenerateEfCoreRepositories.Should().BeFalse();

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

    /// <summary>
    /// CLI の設定ファイル（PascalCase のキー）を「設定読込」で読むと、その内容が画面へ反映されることを検証する
    /// </summary>
    /// <remarks>
    /// 中身は samples/ec-order/quicker.json と同じ形。CLI は大文字小文字を区別せずに読むが、GUI は区別していたため
    /// キーが全部捨てられ、通知もなく画面が既定値へ戻っていた。
    /// </remarks>
    [Fact(DisplayName = "設定読込は CLI の quicker.json（PascalCase）を読む")]
    public void LoadSettingsFrom_CliQuickerJson_IsApplied()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        var configPath = Path.Combine(folder, "quicker.json");
        File.WriteAllText(
            configPath,
            """
            {
              "RootNamespace": "EcOrderSample.Generated",
              "GenerateRepositories": true,
              "OutputPath": "EcOrder.g.cs"
            }
            """
        );
        var files = new StubFileDialogService { OpenResult = new FileDialogResult(configPath, 1) };
        var dialogs = new RecordingDialogService();
        var vm = new CSharpGenerationDialogViewModel(
            new CSharpGenerationSettingsStore(folder),
            files,
            dialogs: dialogs
        );

        try
        {
            vm.LoadSettingsFromCommand.Execute(null);

            vm.RootNamespace.Should().Be("EcOrderSample.Generated");
            vm.GenerateRepositories.Should().BeTrue();
            vm.OutputPath.Should().Be("EcOrder.g.cs");
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

    /// <summary>
    /// 設定ファイルの参照型のキーがすべて明示 null でも、ダイアログが開け、生成を確定できることを検証する
    /// </summary>
    /// <remarks>
    /// 手で編集した <c>"repositoryDialects": null</c> はコンストラクタ（ApplySettings）で、
    /// <c>"codeSubdirectory": null</c> は OK（ToSettings）で NullReferenceException になり、ファイルが残る限り
    /// 毎回ダイアログが開けなかった。null は「指定なし」として既定値で読む。キーの一覧は設定型から
    /// 反射で作るので、参照型の設定を足したときもこのテストが自動で対象にする。
    /// </remarks>
    [Fact(DisplayName = "設定の参照型のキーがすべて明示 null でも開けて確定できる")]
    public void ExplicitNullSettings_OpenAndConfirm()
    {
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        var nulls = new System.Text.Json.Nodes.JsonObject();

        foreach (var property in typeof(CSharpGenerationSettings).GetProperties())
        {
            if (!property.PropertyType.IsValueType)
            {
                nulls[System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name)] =
                    null;
            }
        }

        nulls.Should().ContainKeys("repositoryDialects", "codeSubdirectory", "rootNamespace");
        File.WriteAllText(
            Path.Combine(folder, CSharpGenerationSettingsStore.DefaultFileName),
            nulls.ToJsonString()
        );

        try
        {
            var vm = new CSharpGenerationDialogViewModel(new CSharpGenerationSettingsStore(folder));

            vm.RootNamespace.Should().Be(CSharpGenerationSettings.DefaultRootNamespace);
            vm.OutputPath.Should().Be(CSharpGenerationSettings.DefaultOutputFilePath);

            vm.OkCommand.Execute(null);

            vm.Result.Should().NotBeNull();
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
        var folder = NewFolder();
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
        var folder = NewFolder();
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
                currentProvider: new QuickER.Provider.SqlServer.SqlServerProvider()
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
                currentProvider: new QuickER.Provider.Sqlite.SqliteProvider()
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

    /// <summary>インメモリ実装チェックの既定は OFF で、ToOptions も false であることを検証する（ON の反映と復元は GUI の入口の行が見る）</summary>
    [Fact(DisplayName = "インメモリ実装チェックは既定 OFF")]
    public void GenerateInMemory_DefaultsOff()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";

        vm.GenerateInMemoryRepositories.Should().BeFalse("既定は OFF");
        vm.ToOptions().GenerateInMemoryRepositories.Should().BeFalse();
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
    /// 旧ビルドが実体化して保存した既定名前空間（{root}.{接尾辞} 形）は読込時に未編集として回復され、
    /// 層フォルダの変更へ追従することを検証する（保存済みの既定値が手編集扱いになり追従が止まっていた不具合の再現）
    /// </summary>
    [Fact(DisplayName = "保存済みの実体化既定は読込時に回復され層フォルダ変更へ追従する")]
    public void LegacyMaterializedNamespaces_AreHealed_AndFollowLayerFolders()
    {
        var folder = NewFolder();

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
    /// 既定と一致する名前空間欄・層フォルダ欄は空で永続化される（既定を明示値として持ち越さない）ことを検証する
    /// （生成オプションへ導出〔null〕として渡ることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "既定のままの名前空間・層フォルダは空で保存される")]
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

            vm.Result.Should().NotBeNull();

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

    /// <summary>
    /// 生成コードのサブフォルダを入れても名前空間の既定を一切動かさないこと、空欄は null として生成へ渡ることを検証する
    /// （入れた値が前後の空白を除いて生成オプションへ渡り保存・復元されることは GUI の入口の行が見る）
    /// </summary>
    /// <remarks>
    /// 層フォルダ欄は名前空間の既定を追従させる（<c>ApplyLayerDirectoryChange</c>）が、サブフォルダ欄は
    /// 配置しか変えないため追従させない。ここを取り違えると、サブフォルダを入力しただけで名前空間欄が
    /// 書き換わる（画面上は静かに起きる）。
    /// </remarks>
    [Fact(DisplayName = "CodeSubdirectory は名前空間を動かさず、空欄は null として渡る")]
    public void CodeSubdirectory_DoesNotMoveNamespaces_AndBlankIsNull()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.LayeredOutput = true;
        var namespacesBefore = (vm.EntityNamespace, vm.RepositoryNamespace, vm.MapperNamespace);

        vm.CodeSubdirectory = " Generated ";

        // 名前空間の既定は層フォルダ由来のまま動かない
        (vm.EntityNamespace, vm.RepositoryNamespace, vm.MapperNamespace)
            .Should()
            .Be(namespacesBefore);

        // 空欄は null（既定＝サブフォルダなし）として生成へ渡る
        vm.CodeSubdirectory = string.Empty;
        vm.ToOptions().CodeSubdirectory.Should().BeNull();
    }

    /// <summary>
    /// API リファレンスの出力先サブフォルダの空欄は null（既定＝出力フォルダ直下）として生成へ渡ることを検証する
    /// （入れた値が生成オプションへ渡り保存・復元されることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "ApiDocsSubdirectory の空欄は null として渡る")]
    public void ApiDocsSubdirectory_Blank_IsNull()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.GenerateApiDocs = true;
        vm.ApiDocsSubdirectory = " docs ";

        vm.ApiDocsSubdirectory = string.Empty;
        vm.ToOptions().ApiDocsSubdirectory.Should().BeNull();
    }

    /// <summary>
    /// API リファレンスの出力ファイル名の空欄は null（既定＝導出名）として生成へ渡ることを検証する
    /// （入れた値が生成オプションへ渡り保存・復元されることは GUI の入口の行が見る）
    /// </summary>
    [Fact(DisplayName = "ApiDocsFileName の空欄は null として渡る")]
    public void ApiDocsFileName_Blank_IsNull()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\Entities.g.cs";
        vm.GenerateApiDocs = true;
        vm.ApiDocsFileName = " Reference ";

        vm.ApiDocsFileName = string.Empty;
        vm.ToOptions().ApiDocsFileName.Should().BeNull();
    }

    /// <summary>
    /// 出力ファイル名欄が空のときだけプレースホルダを出し、その内容が出力ファイル名・出力モードへ
    /// 追従することを検証する（表示と実出力がずれないことの担保）
    /// </summary>
    [Fact(DisplayName = "ApiDocsFileName のプレースホルダは空欄時のみ出て導出名に追従する")]
    public void ApiDocsFileNameHint_FollowsDerivedName()
    {
        var vm = CreateViewModel(out _);
        vm.RootNamespace = "Acme.App";
        vm.OutputPath = @"C:\temp\EcOrder.g.cs";

        vm.ShowApiDocsFileNameHint.Should().BeTrue("既定は空欄＝導出名を使う");
        vm.ApiDocsFileNameHint.Should().Be("EcOrder.g.md");

        // 出力ファイル名の変更に追従する
        vm.OutputPath = @"C:\temp\Other.g.cs";
        vm.ApiDocsFileNameHint.Should().Be("Other.g.md");

        // 言語に追従する（日本語だけなら .ja.g.md・両方なら 2 つを「 / 」区切り）
        vm.ApiDocsLanguage = ApiDocsLanguage.Japanese;
        vm.ApiDocsFileNameHint.Should().Be("Other.ja.g.md");
        vm.ApiDocsLanguage = ApiDocsLanguage.Both;
        vm.ApiDocsFileNameHint.Should().Be("Other.g.md / Other.ja.g.md");

        // 分割出力では固定名になる
        vm.SplitFilesByCategory = true;
        vm.ApiDocsFileNameHint.Should().Be("ApiDocs.g.md / ApiDocs.ja.g.md");

        // 明示指定があるとプレースホルダは出さない
        vm.ApiDocsFileName = "Reference";
        vm.ShowApiDocsFileNameHint.Should().BeFalse();
    }
}
