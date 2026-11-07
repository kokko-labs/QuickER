using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.Chat;
using QuickER.AI.Mock;
using QuickER.CodeGen.UI;
using QuickER.Db.UI;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;
using DbStrings = QuickER.Db.UI.Resources.Strings;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.Extensibility;

/// <summary>ツールバーの 1 件の期待（並びの位置ごと）</summary>
internal sealed record ExpectedToolbarItem(
    string Icon,
    string Label,
    string? Tooltip,
    bool BeginsGroup
);

/// <summary>
/// 機能モジュールの契約の行列の軸の値ごとのアダプタ（値による違いはこの抽象メンバーだけで表す）。
/// resx の期待値は厳密型アクセサ経由で取得して比較する（グローバルカルチャは変更しない）。
/// ランチャーの Open()（実 Window 生成）とコマンドの Execute（ダイアログ表示）は呼ばない
/// </summary>
internal interface IFeatureModuleContractAdapter : IMatrixAdapter
{
    /// <summary>期待するモジュール Id（軸の値と同じ）</summary>
    string ExpectedId { get; }

    /// <summary>モジュールを作る</summary>
    IFeatureModule CreateModule();

    /// <summary>ConfigureServices の後に解決できるべきサービスの型（ランチャー・提示シーム・コマンドサービスなど）</summary>
    IReadOnlyList<Type> ResolvableServiceTypes { get; }

    /// <summary>ツールバーの寄与の期待（並び順どおり。Label・Tooltip は resx）</summary>
    IReadOnlyList<ExpectedToolbarItem> ExpectedToolbar { get; }

    /// <summary>
    /// 終了失敗の記録先（<see cref="IShutdownFailureReporter"/>）が必須の依存か
    /// （true なら未登録で解決に失敗し、false なら未登録でも解決できる）
    /// </summary>
    bool NeedsShutdownFailureReporter { get; }

    /// <summary>
    /// ホストのイベント（列名の変更・対象 DBMS の切替）を発火した後の期待を表明する
    /// </summary>
    /// <param name="host">イベントを発火したスタブのホスト（図には <see cref="FeatureModuleContractRows.RenamedEntityId"/> のクエリが 1 件ある）</param>
    /// <param name="items">発火の前に作ったツールバーの寄与</param>
    void AssertAfterHostEvents(StubErDiagramHost host, IReadOnlyList<FeatureToolbarItem> items);
}

/// <summary>AI チャットのモジュール（QuickER.AI.Chat）</summary>
internal sealed class AiChatContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "ai-chat";

    public IFeatureModule CreateModule() => new AiChatFeatureModule();

    public IReadOnlyList<Type> ResolvableServiceTypes => [typeof(IAiChatLauncher)];

    // U+1F916（ロボットの顔）。前のグループ（DB 取込・DB 同期など）との区切り
    public IReadOnlyList<ExpectedToolbarItem> ExpectedToolbar =>
        [
            new(
                "\U0001F916",
                ChatStrings.Toolbar_OpenAiChat,
                ChatStrings.Toolbar_OpenAiChatTooltip,
                BeginsGroup: true
            ),
        ];

    public bool NeedsShutdownFailureReporter => true;

    // ホストのイベントを購読しないので、図にも寄与にも触らない
    public void AssertAfterHostEvents(
        StubErDiagramHost host,
        IReadOnlyList<FeatureToolbarItem> items
    ) => FeatureModuleContractRows.AssertHostUntouched(host);
}

/// <summary>モック生成のモジュール（QuickER.AI.Mock）</summary>
internal sealed class MockGenerationContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "mock-generation";

    public IFeatureModule CreateModule() => new MockGenerationFeatureModule();

    public IReadOnlyList<Type> ResolvableServiceTypes => [typeof(IMockGenerationLauncher)];

    // U+1F5BC（額縁の絵）
    public IReadOnlyList<ExpectedToolbarItem> ExpectedToolbar =>
        [
            new(
                "\U0001F5BC",
                MockStrings.Toolbar_MockGeneration,
                MockStrings.Toolbar_MockGenerationTooltip,
                BeginsGroup: false
            ),
        ];

    public bool NeedsShutdownFailureReporter => true;

    // ホストのイベントを購読しないので、図にも寄与にも触らない
    public void AssertAfterHostEvents(
        StubErDiagramHost host,
        IReadOnlyList<FeatureToolbarItem> items
    ) => FeatureModuleContractRows.AssertHostUntouched(host);
}

/// <summary>C# コード生成・クエリ定義のモジュール（QuickER.CodeGen.UI）</summary>
internal sealed class CodeGenerationContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "code-generation";

    public IFeatureModule CreateModule() => new CodeGenerationFeatureModule();

    public IReadOnlyList<Type> ResolvableServiceTypes =>
        [
            typeof(ICSharpGenerationDialogPresenter),
            typeof(IQueryDefinitionDialogPresenter),
            typeof(CSharpGenerationCommandService),
            typeof(QueryDefinitionCommandService),
            typeof(CodeReverseCommandService),
            typeof(QueryConditionRenameFollower),
        ];

    // コード生成（AI グループとの区切り）→ コード取込（コード生成の対として右隣）→ クエリ定義
    public IReadOnlyList<ExpectedToolbarItem> ExpectedToolbar =>
        [
            new(
                "⌘",
                CodeGenStrings.Toolbar_GenerateCSharp,
                CodeGenStrings.Toolbar_GenerateCSharpTooltip,
                BeginsGroup: true
            ),
            new(
                "\U0001F4E5",
                CodeGenStrings.Toolbar_ReverseFromCode,
                CodeGenStrings.Toolbar_ReverseFromCodeTooltip,
                BeginsGroup: false
            ),
            new(
                "\U0001F50E",
                CodeGenStrings.Toolbar_QueryDefinitions,
                CodeGenStrings.Toolbar_QueryDefinitionsTooltip,
                BeginsGroup: false
            ),
        ];

    public bool NeedsShutdownFailureReporter => false;

    // Initialize でフォロワーが列名の変更を購読しているので、クエリの条件の列名を書き換えて書き戻す
    public void AssertAfterHostEvents(
        StubErDiagramHost host,
        IReadOnlyList<FeatureToolbarItem> items
    )
    {
        host.LastReplacedQueries.Should().NotBeNull();
        host.DiagramToReturn.Queries[0]
            .Condition.Should()
            .Be(FeatureModuleContractRows.RenamedCondition);
    }
}

/// <summary>DB 取込・DB 同期のモジュール（QuickER.Db.UI）</summary>
internal sealed class DbToolsContractAdapter : IFeatureModuleContractAdapter
{
    public string AxisValue => ExpectedId;

    public string ExpectedId => "db-tools";

    public IFeatureModule CreateModule() => new DbToolsFeatureModule();

    public IReadOnlyList<Type> ResolvableServiceTypes =>
        [
            typeof(IDbConnectionDialogPresenter),
            typeof(ISchemaSyncDialogPresenter),
            typeof(DbImportCommandService),
            typeof(DbSyncCommandService),
        ];

    // DB 取込（前のグループとの区切り）→ DB 同期（ツールチップは既定の対象 DBMS の通常説明）
    public IReadOnlyList<ExpectedToolbarItem> ExpectedToolbar =>
        [
            new(
                "\U0001F6E2",
                DbStrings.Toolbar_ImportFromDb,
                DbStrings.Toolbar_ImportFromDbTooltip,
                BeginsGroup: true
            ),
            new("⇪", DbStrings.Toolbar_SyncToDb, DbStrings.Db_SyncWriteBack, BeginsGroup: false),
        ];

    public bool NeedsShutdownFailureReporter => false;

    // 方言を SQLite へ切り替えても DB 同期は全方言対応のまま（実行可・通常ツールチップ）。クエリには触らない
    public void AssertAfterHostEvents(
        StubErDiagramHost host,
        IReadOnlyList<FeatureToolbarItem> items
    )
    {
        var sync = items[1];
        sync.Command.CanExecute(null).Should().BeTrue();
        sync.Tooltip.Should().Be(DbStrings.Db_SyncWriteBack);
        host.LastReplacedQueries.Should().BeNull();
    }
}

/// <summary>機能モジュールの軸（正本＝製品の <c>FeatureModuleCatalog.CreateModules()</c> の各モジュールの Id）</summary>
internal static class FeatureModuleAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "FeatureModule";

    /// <summary>正本の値（カタログのモジュールの Id の並び）</summary>
    public static IReadOnlyList<string> Names { get; } =
        QuickER.FeatureModuleCatalog.CreateModules().Select(module => module.Id).ToList();

    /// <summary>軸（値ごとにちょうど 1 つのアダプタ。除外は無い）</summary>
    public static MatrixAxis<IFeatureModuleContractAdapter> Axis { get; } =
        new(
            Name,
            Names,
            [
                new AiChatContractAdapter(),
                new MockGenerationContractAdapter(),
                new CodeGenerationContractAdapter(),
                new DbToolsContractAdapter(),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
        );
}

/// <summary>機能モジュールの契約（行の種類）</summary>
internal enum FeatureModuleContract
{
    /// <summary>ConfigureServices の後に、モジュールが登録するサービス（ランチャー・提示シーム・コマンドサービス）を解決できる</summary>
    ResolvableServices,

    /// <summary>Id が期待どおり</summary>
    ExpectedId,

    /// <summary>ツールバーの寄与が resx と一致する並び（区切り・実行可を含む）</summary>
    LocalizedToolbarItems,

    /// <summary>ダイアログ未生成でも OnMainWindowClosing が例外なく完了する</summary>
    ClosingWithoutDialog,

    /// <summary>終了失敗の記録先への依存がアダプタの宣言どおり（必須なら未登録で解決に失敗し、必須でなければ解決できる）</summary>
    ShutdownReporterDependency,

    /// <summary>ホストの順（ConfigureServices → Build → Initialize → CreateToolbarItems）で組んだ後、ホストのイベントへの追従がアダプタの宣言どおり</summary>
    FollowsHostEvents,
}

/// <summary>機能モジュールの契約の表の 1 行の入力</summary>
internal sealed record FeatureModuleContractCase(
    IFeatureModuleContractAdapter Adapter,
    FeatureModuleContract Contract
);

/// <summary>
/// 機能モジュールの契約の表（QuickER.AI.Chat の <c>AiChatFeatureModule</c>・QuickER.AI.Mock の
/// <c>MockGenerationFeatureModule</c>・QuickER.CodeGen.UI の <c>CodeGenerationFeatureModule</c>・
/// QuickER.Db.UI の <c>DbToolsFeatureModule</c>。契約は QuickER.Extensibility の <c>IFeatureModule</c>）：軸の値 × 契約の全組
/// </summary>
internal static class FeatureModuleContractRows
{
    /// <summary>ホストのイベントの行で、列名を変える表の Id</summary>
    public static readonly Guid RenamedEntityId = new("6f1d3c2a-8b4e-4d7a-9c0f-2e5b7a1d4c38");

    /// <summary>ホストのイベントの行の、変更前のクエリの条件</summary>
    private const string OriginalCondition = "CustomerId = @customerId";

    /// <summary>列名の変更（CustomerId → BuyerId）に追従したときのクエリの条件（パラメータ名は変えない）</summary>
    public const string RenamedCondition = "BuyerId = @customerId";

    /// <summary>表（軸のアダプタごとに全契約）</summary>
    public static ScenarioTable<FeatureModuleContractCase> Table =>
        new(
            EngineIds.OsBoundary,
            from adapter in FeatureModuleAxis.Axis.Adapters
            from contract in Enum.GetValues<FeatureModuleContract>()
            select new ScenarioRow<FeatureModuleContractCase>(
                $"module-{adapter.AxisValue}-{Slug(adapter, contract)}",
                null,
                $"{adapter.AxisValue}: {Describe(adapter, contract)}",
                new FeatureModuleContractCase(adapter, contract)
            )
        );

    private static string Slug(
        IFeatureModuleContractAdapter adapter,
        FeatureModuleContract contract
    ) =>
        contract switch
        {
            FeatureModuleContract.ResolvableServices =>
                "services-are-resolvable-after-configure-services",
            FeatureModuleContract.ExpectedId => "id-is-expected",
            FeatureModuleContract.LocalizedToolbarItems => "toolbar-returns-localized-items",
            FeatureModuleContract.ClosingWithoutDialog => "closing-without-dialog-does-not-throw",
            FeatureModuleContract.ShutdownReporterDependency => adapter.NeedsShutdownFailureReporter
                ? "launcher-needs-shutdown-failure-reporter"
                : "resolves-without-shutdown-failure-reporter",
            FeatureModuleContract.FollowsHostEvents => "follows-host-events-as-declared",
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, null),
        };

    private static string Describe(
        IFeatureModuleContractAdapter adapter,
        FeatureModuleContract contract
    ) =>
        contract switch
        {
            FeatureModuleContract.ResolvableServices =>
                "ConfigureServices 後に登録したサービスが解決できる",
            FeatureModuleContract.ExpectedId => "Id が期待どおり",
            FeatureModuleContract.LocalizedToolbarItems =>
                "CreateToolbarItems は resx 一致の並び（区切り・実行可を含む）を返す",
            FeatureModuleContract.ClosingWithoutDialog =>
                "OnMainWindowClosing は未生成状態でも例外なく完了する",
            FeatureModuleContract.ShutdownReporterDependency => adapter.NeedsShutdownFailureReporter
                ? "終了失敗の記録先が未登録ならランチャーを解決できない"
                : "終了失敗の記録先が未登録でもサービスを解決できる",
            FeatureModuleContract.FollowsHostEvents =>
                "列名の変更・対象 DBMS の切替への追従が宣言どおり",
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, null),
        };

    /// <summary>ホストのイベントを購読しないモジュールの期待：図のクエリを書き戻さず、条件も変えない</summary>
    public static void AssertHostUntouched(StubErDiagramHost host)
    {
        host.LastReplacedQueries.Should().BeNull();
        host.DiagramToReturn.Queries[0].Condition.Should().Be(OriginalCondition);
    }

    /// <summary>列名を変える表のクエリ 1 件を持つスタブのホスト（対象 DBMS は既定の SQL Server）</summary>
    private static StubErDiagramHost CreateHost() =>
        new()
        {
            DiagramToReturn = new ErDiagram
            {
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = RenamedEntityId,
                        Name = "GetByCustomer",
                        Condition = OriginalCondition,
                    },
                },
            },
        };

    /// <summary>
    /// ホストが登録するサービス（ホスト・ダイアログ・ファイル選択・空のプロバイダの登録簿）を登録した DI に
    /// ConfigureServices を通す（終了失敗の記録先の登録だけを引数で決める）
    /// </summary>
    private static ServiceCollection Configure(
        IFeatureModule module,
        StubErDiagramHost host,
        bool withReporter
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<IErDiagramHost>(host);
        services.AddSingleton<IDialogService>(new StubDialogService());
        services.AddSingleton<IFileDialogService>(new NullFileDialogService());
        services.AddSingleton(new DatabaseProviderRegistry(Array.Empty<IDatabaseProvider>()));

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
        var host = CreateHost();

        switch (c.Contract)
        {
            case FeatureModuleContract.ResolvableServices:
            {
                using var provider = Configure(module, host, withReporter: true)
                    .BuildServiceProvider();

                foreach (var type in adapter.ResolvableServiceTypes)
                {
                    provider.GetService(type).Should().NotBeNull(type.Name);
                }

                break;
            }

            case FeatureModuleContract.ExpectedId:
                module.Id.Should().Be(adapter.ExpectedId);
                break;

            case FeatureModuleContract.LocalizedToolbarItems:
            {
                using var provider = Configure(module, host, withReporter: true)
                    .BuildServiceProvider();
                var items = module.CreateToolbarItems(provider);

                items
                    .Select(item => new ExpectedToolbarItem(
                        item.Icon,
                        item.Label,
                        item.Tooltip,
                        item.BeginsGroup
                    ))
                    .Should()
                    .Equal(adapter.ExpectedToolbar);
                items.Should().OnlyContain(item => item.Command != null);
                items.Should().OnlyContain(item => item.Command.CanExecute(null));
                break;
            }

            case FeatureModuleContract.ClosingWithoutDialog:
            {
                using var provider = Configure(module, host, withReporter: true)
                    .BuildServiceProvider();

                var act = () => module.OnMainWindowClosing(provider);
                act.Should().NotThrow();
                break;
            }

            case FeatureModuleContract.ShutdownReporterDependency:
            {
                using var provider = Configure(module, host, withReporter: false)
                    .BuildServiceProvider();

                var act = () =>
                {
                    foreach (var type in adapter.ResolvableServiceTypes)
                    {
                        provider.GetService(type).Should().NotBeNull(type.Name);
                    }
                };

                if (adapter.NeedsShutdownFailureReporter)
                {
                    act.Should().Throw<InvalidOperationException>();
                }
                else
                {
                    act.Should().NotThrow();
                }

                break;
            }

            case FeatureModuleContract.FollowsHostEvents:
            {
                using var provider = Configure(module, host, withReporter: true)
                    .BuildServiceProvider();
                module.Initialize(provider);
                var items = module.CreateToolbarItems(provider);

                host.RaiseColumnRenamed(RenamedEntityId, "CustomerId", "BuyerId");
                host.TargetDbmsToReturn = SqliteProvider.ProviderName;
                host.RaiseTargetDbmsChanged();

                adapter.AssertAfterHostEvents(host, items);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(c), c.Contract, null);
        }

        return true;
    }
}
