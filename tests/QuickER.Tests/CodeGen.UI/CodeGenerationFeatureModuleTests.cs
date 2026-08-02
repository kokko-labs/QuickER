using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.UI;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="CodeGenerationFeatureModule"/> の DI 登録・初期化（列リネーム購読）・ツールバー寄与を検証するテストクラス。
/// </summary>
/// <remarks>
/// resx の期待値は厳密型アクセサ（<see cref="CodeGenStrings"/>）経由で取得して比較する
/// （グローバルカルチャは変更しない）。
/// </remarks>
public class CodeGenerationFeatureModuleTests
{
    /// <summary>モジュールの依存（ホスト・ダイアログ・ファイル選択）を登録したサービスコレクションを構築する</summary>
    private static ServiceCollection BuildServices(StubErDiagramHost host)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(host);
        services.AddSingleton<IDialogService>(new StubDialogService());
        services.AddSingleton<IFileDialogService>(new NullFileDialogService());
        return services;
    }

    /// <summary>Id が "code-generation" であることを検証する</summary>
    [Fact(DisplayName = "Id は code-generation")]
    public void Id_IsCodeGeneration()
    {
        new CodeGenerationFeatureModule().Id.Should().Be("code-generation");
    }

    /// <summary>ConfigureServices 後にコマンドサービス・提示シーム・フォロワーが解決できることを検証する</summary>
    [Fact(DisplayName = "ConfigureServices 後にサービス群が解決できる")]
    public void ConfigureServices_RegistersResolvableServices()
    {
        var module = new CodeGenerationFeatureModule();
        var services = BuildServices(new StubErDiagramHost());

        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        provider.GetService<ICSharpGenerationDialogPresenter>().Should().NotBeNull();
        provider.GetService<IQueryDefinitionDialogPresenter>().Should().NotBeNull();
        provider.GetService<CSharpGenerationCommandService>().Should().NotBeNull();
        provider.GetService<QueryDefinitionCommandService>().Should().NotBeNull();
        provider.GetService<CodeReverseCommandService>().Should().NotBeNull();
        provider.GetService<QueryConditionRenameFollower>().Should().NotBeNull();
    }

    /// <summary>CreateToolbarItems が resx 一致の 3 件（コード生成・クエリ定義・コード取込）を返すことを検証する</summary>
    [Fact(DisplayName = "CreateToolbarItems は resx 一致の 3 件を返す")]
    public void CreateToolbarItems_ReturnsThreeLocalizedItems()
    {
        var module = new CodeGenerationFeatureModule();
        var services = BuildServices(new StubErDiagramHost());
        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var items = module.CreateToolbarItems(provider);

        items.Should().HaveCount(3);

        // ①コード生成: AI グループとの区切りとして BeginsGroup=true
        var generate = items[0];
        generate.Icon.Should().Be("⌘");
        generate.Label.Should().Be(CodeGenStrings.Toolbar_GenerateCSharp);
        generate.Tooltip.Should().Be(CodeGenStrings.Toolbar_GenerateCSharpTooltip);
        generate.Command.Should().NotBeNull();
        generate.BeginsGroup.Should().BeTrue();

        // ②コード取込: コード生成の対（図→コード / コード→図）として右隣に置く・区切りなし
        var reverse = items[1];
        reverse.Icon.Should().Be("📥");
        reverse.Label.Should().Be(CodeGenStrings.Toolbar_ReverseFromCode);
        reverse.Tooltip.Should().Be(CodeGenStrings.Toolbar_ReverseFromCodeTooltip);
        reverse.Command.Should().NotBeNull();
        reverse.BeginsGroup.Should().BeFalse();

        // ③クエリ定義: 区切りなし
        var query = items[2];
        query.Icon.Should().Be("🔎");
        query.Label.Should().Be(CodeGenStrings.Toolbar_QueryDefinitions);
        query.Tooltip.Should().Be(CodeGenStrings.Toolbar_QueryDefinitionsTooltip);
        query.Command.Should().NotBeNull();
        query.BeginsGroup.Should().BeFalse();
    }

    /// <summary>Initialize がフォロワーをホストの列リネーム通知へ購読させることを検証する</summary>
    [Fact(DisplayName = "Initialize でフォロワーが列リネームを購読する")]
    public void Initialize_SubscribesRenameFollower()
    {
        var entityId = Guid.NewGuid();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = new ErDiagram
            {
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = entityId,
                        Name = "GetByCustomer",
                        Condition = "CustomerId = @customerId",
                    },
                },
            },
        };
        var module = new CodeGenerationFeatureModule();
        var services = BuildServices(host);
        module.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        module.Initialize(provider);

        // 購読済みなら、列リネーム通知でフォロワーがクエリ条件を書き換えて書き戻す
        host.RaiseColumnRenamed(entityId, "CustomerId", "BuyerId");

        host.LastReplacedQueries.Should().NotBeNull();
        host.DiagramToReturn.Queries[0].Condition.Should().Be("BuyerId = @customerId");
    }

    /// <summary>OnMainWindowClosing が例外なく完了することを検証する（後始末不要の空実装）</summary>
    [Fact(DisplayName = "OnMainWindowClosing は例外なく完了する")]
    public void OnMainWindowClosing_DoesNotThrow()
    {
        var module = new CodeGenerationFeatureModule();
        var services = BuildServices(new StubErDiagramHost());
        module.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var act = () => module.OnMainWindowClosing(provider);
        act.Should().NotThrow();
    }
}
