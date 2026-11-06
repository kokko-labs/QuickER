using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Chat;
using QuickER.AI.Mock;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.Extensibility;

/// <summary>
/// 機能モジュールの契約の行列の軸の値ごとのアダプタ（値による違いはこの抽象メンバーだけで表す）。
/// resx の期待値は厳密型アクセサ経由で取得して比較する（グローバルカルチャは変更しない）。
/// ランチャーの Open()（実 Window 生成）は呼ばない
/// </summary>
internal interface IFeatureModuleContractAdapter : IMatrixAdapter
{
    /// <summary>期待するモジュール Id（軸の値と同じ）</summary>
    string ExpectedId { get; }

    /// <summary>モジュールを作る</summary>
    IFeatureModule CreateModule();

    /// <summary>モジュールが DI へ登録するランチャーの型</summary>
    Type LauncherType { get; }

    /// <summary>ツールバーのアイコンの期待</summary>
    string ToolbarIcon { get; }

    /// <summary>ツールバーの Label の期待（resx）</summary>
    string ToolbarLabel { get; }

    /// <summary>ツールバーの Tooltip の期待（resx）</summary>
    string ToolbarTooltip { get; }
}

/// <summary>AI チャットのモジュール（QuickER.AI.Chat）</summary>
internal sealed class AiChatContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "ai-chat";

    public IFeatureModule CreateModule() => new AiChatFeatureModule();

    public Type LauncherType => typeof(IAiChatLauncher);

    // U+1F916（ロボットの顔）
    public string ToolbarIcon => "\U0001F916";

    public string ToolbarLabel => ChatStrings.Toolbar_OpenAiChat;

    public string ToolbarTooltip => ChatStrings.Toolbar_OpenAiChatTooltip;
}

/// <summary>モック生成のモジュール（QuickER.AI.Mock）</summary>
internal sealed class MockGenerationContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "mock-generation";

    public IFeatureModule CreateModule() => new MockGenerationFeatureModule();

    public Type LauncherType => typeof(IMockGenerationLauncher);

    // U+1F5BC（額縁の絵）
    public string ToolbarIcon => "\U0001F5BC";

    public string ToolbarLabel => MockStrings.Toolbar_MockGeneration;

    public string ToolbarTooltip => MockStrings.Toolbar_MockGenerationTooltip;
}

/// <summary>機能モジュールの軸（正本＝製品の <c>FeatureModuleCatalog.CreateModules()</c> の各モジュールの Id）</summary>
internal static class FeatureModuleAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "FeatureModule";

    /// <summary>正本の値（カタログのモジュールの Id の並び）</summary>
    public static IReadOnlyList<string> Names { get; } =
        QuickER.FeatureModuleCatalog.CreateModules().Select(module => module.Id).ToList();

    /// <summary>アダプタを持たない値と理由</summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code-generation"] =
                "同じ形のテスト CodeGenerationFeatureModuleTests は P3-10 の受け持ちの外で、記録先が必須の契約のテストも持たない。足すのは P4",
            ["db-tools"] =
                "同じ形のテスト DbToolsFeatureModuleTests は P3-10 の受け持ちの外で、記録先が必須の契約のテストも持たない。足すのは P4",
        };

    /// <summary>軸（値ごとにちょうど 1 つのアダプタか理由つきの除外）</summary>
    public static MatrixAxis<IFeatureModuleContractAdapter> Axis { get; } =
        new(
            Name,
            Names,
            [new AiChatContractAdapter(), new MockGenerationContractAdapter()],
            Excluded
        );
}

/// <summary>機能モジュールの契約（行の種類）</summary>
internal enum FeatureModuleContract
{
    /// <summary>ConfigureServices の後にランチャーを解決できる</summary>
    ResolvableLauncher,

    /// <summary>Id が期待どおり</summary>
    ExpectedId,

    /// <summary>ツールバーの寄与が resx と一致する 1 件</summary>
    LocalizedToolbarItem,

    /// <summary>ダイアログ未生成でも OnMainWindowClosing が例外なく完了する</summary>
    ClosingWithoutDialog,

    /// <summary>終了失敗の記録先が未登録ならランチャーを解決できない</summary>
    ReporterRequired,
}

/// <summary>機能モジュールの契約の表の 1 行の入力</summary>
internal sealed record FeatureModuleContractCase(
    IFeatureModuleContractAdapter Adapter,
    FeatureModuleContract Contract
);

/// <summary>
/// 機能モジュールの契約の表（QuickER.AI.Chat の <c>AiChatFeatureModule</c>・QuickER.AI.Mock の
/// <c>MockGenerationFeatureModule</c>。契約は QuickER.Extensibility の <c>IFeatureModule</c>）：軸の値 × 契約の全組
/// </summary>
internal static class FeatureModuleContractRows
{
    /// <summary>表（軸のアダプタごとに全契約）</summary>
    public static ScenarioTable<FeatureModuleContractCase> Table =>
        new(
            EngineIds.OsBoundary,
            from adapter in FeatureModuleAxis.Axis.Adapters
            from contract in Enum.GetValues<FeatureModuleContract>()
            select new ScenarioRow<FeatureModuleContractCase>(
                $"module-{adapter.AxisValue}-{Slug(contract)}",
                null,
                $"{adapter.AxisValue}: {Describe(contract)}",
                new FeatureModuleContractCase(adapter, contract)
            )
        );

    private static string Slug(FeatureModuleContract contract) =>
        contract switch
        {
            FeatureModuleContract.ResolvableLauncher =>
                "launcher-is-resolvable-after-configure-services",
            FeatureModuleContract.ExpectedId => "id-is-expected",
            FeatureModuleContract.LocalizedToolbarItem => "toolbar-returns-single-localized-item",
            FeatureModuleContract.ClosingWithoutDialog => "closing-without-dialog-does-not-throw",
            FeatureModuleContract.ReporterRequired => "launcher-needs-shutdown-failure-reporter",
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, null),
        };

    private static string Describe(FeatureModuleContract contract) =>
        contract switch
        {
            FeatureModuleContract.ResolvableLauncher =>
                "ConfigureServices 後にランチャーが解決できる",
            FeatureModuleContract.ExpectedId => "Id が期待どおり",
            FeatureModuleContract.LocalizedToolbarItem =>
                "CreateToolbarItems は resx 一致の 1 件を返す",
            FeatureModuleContract.ClosingWithoutDialog =>
                "OnMainWindowClosing は未生成状態でも例外なく完了する",
            FeatureModuleContract.ReporterRequired =>
                "終了失敗の記録先が未登録ならランチャーを解決できない",
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, null),
        };

    /// <summary>スタブのホストを登録した DI（記録先の登録は引数で決める）に ConfigureServices を通す</summary>
    private static ServiceCollection Configure(IFeatureModule module, bool withReporter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(new StubErDiagramHost());

        if (withReporter)
        {
            services.AddSingleton<IShutdownFailureReporter>(new RecordingShutdownFailureReporter());
        }

        module.ConfigureServices(services);
        return services;
    }

    /// <summary>1 行を流す。常に true</summary>
    public static bool Run(FeatureModuleContractCase c)
    {
        var adapter = c.Adapter;
        var module = adapter.CreateModule();

        switch (c.Contract)
        {
            case FeatureModuleContract.ResolvableLauncher:
            {
                using var provider = Configure(module, withReporter: true).BuildServiceProvider();

                provider.GetService(adapter.LauncherType).Should().NotBeNull();
                break;
            }

            case FeatureModuleContract.ExpectedId:
                module.Id.Should().Be(adapter.ExpectedId);
                break;

            case FeatureModuleContract.LocalizedToolbarItem:
            {
                using var provider = Configure(module, withReporter: true).BuildServiceProvider();
                var items = module.CreateToolbarItems(provider);

                items.Should().ContainSingle();
                var item = items[0];
                item.Icon.Should().Be(adapter.ToolbarIcon);
                item.Label.Should().Be(adapter.ToolbarLabel);
                item.Tooltip.Should().Be(adapter.ToolbarTooltip);
                item.Command.Should().NotBeNull();
                break;
            }

            case FeatureModuleContract.ClosingWithoutDialog:
            {
                using var provider = Configure(module, withReporter: true).BuildServiceProvider();

                var act = () => module.OnMainWindowClosing(provider);
                act.Should().NotThrow();
                break;
            }

            case FeatureModuleContract.ReporterRequired:
            {
                using var provider = Configure(module, withReporter: false).BuildServiceProvider();

                var act = () => provider.GetService(adapter.LauncherType);
                act.Should().Throw<InvalidOperationException>();
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(c), c.Contract, null);
        }

        return true;
    }
}
