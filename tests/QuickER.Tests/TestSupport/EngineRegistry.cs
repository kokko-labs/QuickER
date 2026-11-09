using QuickER.Tests.Gui.Services;
using QuickER.Tests.Gui.UndoRedo;
using QuickER.Tests.Mcp.Tools;
using QuickER.Tests.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>QuickER.Tests にあるテストのエンジンの台帳（エンジンの部分ごとの次元表・シナリオの数・テストのソース・対象の範囲）</summary>
/// <remarks>
/// Integration にも同じ名前の台帳（<c>QuickER.Tests.Integration.EngineRegistry</c>）がある。このファイルは Integration の
/// Compile リンクの glob（<c>NameFuzz*.cs</c>・<c>DslFuzz*.cs</c>・<c>Engines/**</c>）に当たらない名前にしておく。
/// </remarks>
internal static class EngineRegistry
{
    /// <summary>台帳の行（エンジンの ID と部分の組は一意）</summary>
    public static IReadOnlyList<EngineDescriptor> All { get; } =
    [
        DslFuzzDimensions.SqliteMySqlDescriptor,
        NameFuzzDimensions.Descriptor,
        ToolArgumentFuzzDimensions.Descriptor,
        RelationshipTypeChangeDimensions.Descriptor,
        DialectTypeDimensions.Descriptor,
        DialectIdentifierDimensions.Descriptor,
        DialectDdlDimensions.Descriptor,
        DialectImportWarningDimensions.Descriptor,
        CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions.ErrorsDescriptor,
        CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions.SuccessDescriptor,
        CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions.CasesDescriptor,
        ToolScenarioDimensions.Descriptor,
        UndoOperationDimensions.Descriptor,
        SelectionInvariantDimensions.Descriptor,
        AutoLayoutDimensions.Descriptor,
        Gui.Behaviors.CanvasInputDimensions.Descriptor,
        Gui.ViewModels.DocumentFileDimensions.ScenariosDescriptor,
        Gui.ViewModels.DocumentFileDimensions.RandomDescriptor,
        Document.DocumentFormatDimensions.Descriptor,
        Settings.SettingsStoreDimensions.Descriptor,
        Gui.Services.FormatRoundTrip.FormatRoundTripDimensions.RoundTripDescriptor,
        Gui.Services.FormatRoundTrip.FormatRoundTripDimensions.OmissionDescriptor,
        Gui.Services.FormatRoundTrip.FormatRoundTripDimensions.RuleDescriptor,
        Gui.Services.FormatRoundTrip.FormatRoundTripDimensions.MergeDescriptor,
        AI.ChatEngines.ChatEngineDimensions.Descriptor,
        AI.ChatEngines.ChatDriverDimensions.Descriptor,
        AI.UI.AttachmentListDimensions.Descriptor,
        AI.UI.ConnectionSettingsDimensions.Descriptor,
        AI.UI.ApiKeyStoreDimensions.Descriptor,
        AI.UI.ChatMarkdownDimensions.Descriptor,
        AI.Chat.ChatDialogDimensions.Descriptor,
        Platform.OsBoundary.ProcessLaunchDimensions.Descriptor,
        Platform.OsBoundary.ShellPermissionDimensions.Descriptor,
        Platform.OsBoundary.PathEntranceDimensions.Descriptor,
        Platform.OsBoundary.AtomicFileDimensions.Descriptor,
        Platform.OsBoundary.PathContainmentDimensions.Descriptor,
        Platform.OsBoundary.PathSegmentsDimensions.Descriptor,
        Gui.Services.Lifecycle.LifecycleDimensions.Descriptor,
        Gui.Services.Lifecycle.PostExitExhaustiveDimensions.Descriptor,
        Gui.Services.Lifecycle.ShutdownChainExhaustiveDimensions.Descriptor,
        Extensibility.FeatureModuleContractDimensions.Descriptor,
        Db.UI.ProfileStoreDimensions.Descriptor,
        Guards.GuardDimensions.Descriptor,
        Guards.GuardTextDimensions.ParityDescriptor,
        Guards.GuardTextDimensions.PhraseDescriptor,
        AI.Mock.Agents.MockAgentDimensions.Descriptor,
        AI.Mock.Runner.MockRunnerDimensions.Descriptor,
        AI.Mock.BuildBoundary.MockBuildBoundaryDimensions.Descriptor,
        AI.Mock.Scaffold.MockScaffoldDimensions.Descriptor,
        AI.Mock.Folder.MockFolderDimensions.Descriptor,
        AI.Mock.Outputs.MockOutputsDimensions.Descriptor,
        AI.Mock.Dialog.MockDialogDimensions.Descriptor,
        Db.UI.ConnectionDialog.ConnectionDialogDimensions.Descriptor,
        Db.UI.ImportSync.ImportSyncDimensions.Descriptor,
    ];
}
