# 影響の地図

<!-- 生成物（ImpactMapDriftTests が宣言から作る）。手で直さない。書き直し（リポジトリ直下・PowerShell）：$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj --filter "FullyQualifiedName~ProtectionSnapshotDrift"; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~ImpactMapDrift"; $env:QUICKER_REGEN_FIXTURES=$null -->

src の単位（プロジェクトと、その直下のフォルダ）ごとに、それを守るテストのエンジンの部分・単体の表・範囲を絞った規則を並べる。
出どころはエンジンの台帳（EngineRegistry の Covers）・単体の表の台帳（TableRegistry）・規則の範囲（SourceRules の Scope）・Integration の台帳の写し（protection-registry.json）。

| 名前の形 | 守りの種類 |
|---|---|
| `{エンジンの ID}/{部分}` | エンジンの部分 |
| `table:{名前}` | 単体の表 |
| `rule:{Id}` | ソースの規則 |
| `parity:{Id}` | 照合の表の行 |
| `english:{Id}` | 英語の文言の表の行 |
| `phrase:{Id}` | 必須の文言の表の行 |

## 単位の表

| 単位 | 状態 | 守り |
|---|---|---|
| `src/QuickER.AI` | 全体 | `Ai/Attachments`・`Ai/ChatEngines`・`Ai/Drivers`・`OsBoundary/Containment`・`OsBoundary/ProcessLaunch`・`OsBoundary/ShellPermission`・`rule:chat-engine-tool-invocation-copies`・`rule:chat-engine-tool-invocation-uses` |
| `src/QuickER.AI.Chat` | 一部 | `Ai/ChatDialog`・`DiagramEdit/ToolArgument`・`DiagramEdit/Tools`・`OsBoundary/FeatureModules`・`table:QueryToolFormatter` |
| `src/QuickER.AI.Mock` | 一部 | `Ai/MockAgents`・`Ai/MockBuildBoundary`・`Ai/MockDialog`・`Ai/MockFolder`・`Ai/MockOutputs`・`Ai/MockRunner`・`Ai/MockScaffold`・`OsBoundary/Containment`・`OsBoundary/FeatureModules`・`OsBoundary/PathEntrances`・`OsBoundary/Segments` |
| `src/QuickER.AI.UI` | 一部 | `Ai/ApiKeyStore`・`Ai/Attachments`・`Ai/ConnectionSettings`・`Ai/MarkdownRender`・`OsBoundary/ProcessLaunch`・`table:Converters` |
| `src/QuickER.Cli` | 一部 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`DiagramEdit/ToolArgument` |
| `src/QuickER.CodeGen.CSharp` | 全体 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`Name/Entrances`・`OsBoundary/Segments`・`SyncModel/Config`・`SyncModel/Random`・`table:EditModelScenarios`・`table:TranslatorParityPredicates`・`table:ValueObjectScenarios` |
| `src/QuickER.CodeGen.CSharp/Queries` | 全体 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Dsl/SqlServer`・`Dsl/SqliteMySql`・`Name/Entrances` |
| `src/QuickER.CodeGen.CSharp/Templates` | 全体 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`Name/Entrances`・`SyncModel/Config`・`SyncModel/Random`・`table:EditModelScenarios`・`table:TranslatorParityPredicates`・`table:ValueObjectScenarios` |
| `src/QuickER.CodeGen.UI` | 全体 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Name/Entrances`・`OsBoundary/FeatureModules` |
| `src/QuickER.CodeReverse.CSharp` | 全体 | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Db.UI` | 一部 | `Ai/DbConnectionDialog`・`Ai/DbImportSync`・`OsBoundary/FeatureModules`・`OsBoundary/ProfileStore` |
| `src/QuickER.Dialects` | 全体 | `Dialect/Identifiers`・`Name/DdlRoundTrip`・`rule:dialects-project-references` |
| `src/QuickER.Document` | 全体 | `DocumentFile/Format`・`DocumentFile/Random`・`DocumentFile/Scenarios` |
| `src/QuickER.Extensibility` | 一部 | `OsBoundary/FeatureModules` |
| `src/QuickER.Gui` | 一部 | `DiagramEdit/CanvasInput`・`DiagramEdit/Layout`・`DiagramEdit/Selection`・`DiagramEdit/Tools`・`DiagramEdit/Undo`・`DiagramEdit/UndoOps`・`DocumentFile/Random`・`DocumentFile/Scenarios`・`FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules`・`Name/Entrances`・`OsBoundary/Lifecycle`・`OsBoundary/PostExitExhaustive`・`rule:app-feature-module-shutdown-bypasses`・`rule:app-feature-module-shutdown-uses`・`rule:app-main-window-assignment`・`rule:app-shutdown-mode-declaration`・`rule:app-shutdown-mode-overrides`・`rule:app-ui-crash-handler-bypasses`・`rule:app-ui-crash-handler-uses`・`rule:program-single-instance-bypasses`・`rule:program-single-instance-uses`・`table:Converters` |
| `src/QuickER.Gui.Abstractions` | 一部 | `OsBoundary/Lifecycle`・`OsBoundary/ShutdownChainExhaustive` |
| `src/QuickER.Gui.Common` | 一部 | `rule:dialog-owner-resolve-uses`・`rule:file-dialog-ownerless-shows`・`rule:message-box-owner-resolver-copies` |
| `src/QuickER.Gui/Behaviors` | 一部 | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Converters` | 一部 | `table:Converters` |
| `src/QuickER.Gui/Services` | 全体 | `DiagramEdit/Layout`・`DiagramEdit/Tools`・`DocumentFile/Random`・`DocumentFile/Scenarios`・`FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules`・`Name/Entrances`・`OsBoundary/Lifecycle`・`OsBoundary/PostExitExhaustive` |
| `src/QuickER.Gui/UndoRedo` | 全体 | `DiagramEdit/Undo`・`DiagramEdit/UndoOps` |
| `src/QuickER.Gui/ViewModels` | 全体 | `DiagramEdit/Selection`・`DiagramEdit/Undo`・`DiagramEdit/UndoOps`・`DocumentFile/Random`・`DocumentFile/Scenarios`・`Name/Entrances`・`OsBoundary/Lifecycle` |
| `src/QuickER.Gui/Views` | 守りの無い領域（Accepted） | （なし） |
| `src/QuickER.Mcp` | 一部 | `DiagramEdit/ToolArgument`・`DiagramEdit/Tools`・`rule:tool-argument-parsing-owners` |
| `src/QuickER.Mcp.Tools` | 全体 | `DiagramEdit/ToolArgument`・`DiagramEdit/Tools`・`Name/Entrances`・`rule:mcp-tools-column-nullability-raw-reads`・`table:QueryToolFormatter` |
| `src/QuickER.Model` | 全体 | `Dialect/DdlText`・`Name/Entrances`・`SyncPlan/HistoryRows`・`rule:model-dialect-lookups`・`rule:tool-argument-parsing-owners`・`table:ForeignKeyReferentialActions` |
| `src/QuickER.Platform` | 一部 | `Ai/MarkdownRender`・`OsBoundary/AtomicFile`・`OsBoundary/Containment`・`OsBoundary/PathEntrances`・`OsBoundary/ProcessLaunch`・`OsBoundary/Segments` |
| `src/QuickER.Provider` | 全体 | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Dialect/DdlText`・`Dialect/ImportWarnings`・`Dialect/Types`・`FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules`・`Name/DdlRoundTrip`・`Name/Entrances`・`SyncPlan/CapabilityNet`・`SyncPlan/Fuzz`・`SyncPlan/HistoryRows`・`SyncPlan/ShapeRows`・`rule:provider-column-nullability-raw-reads`・`rule:sql-name-text-name-sources`・`rule:sync-capabilities-initializers`・`rule:sync-name-comparison-tokens`・`rule:sync-selected-foreign-key-names`・`rule:sync-type-deciding-column-copies`・`table:SyncPlanExpectations` |
| `src/QuickER.Provider.MySql` | 全体 | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads`・`rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks` |
| `src/QuickER.Provider.Oracle` | 全体 | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads`・`rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks` |
| `src/QuickER.Provider.PostgreSql` | 全体 | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads`・`rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses` |
| `src/QuickER.Provider.SqlServer` | 全体 | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads`・`rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses` |
| `src/QuickER.Provider.Sqlite` | 全体 | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`Name/Entrances`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads`・`rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses`・`rule:sqlite-file-connection-pooling-off` |
| `src/QuickER.Runtime` | 全体 | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityOutcomes`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues`・`table:EditModelScenarios`・`table:TranslatorParityPredicates`・`table:ValueObjectScenarios` |
| `src/QuickER.Runtime.AspNetCore` | 全体 | `GeneratedRuntime/Parity`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues` |
| `src/QuickER.Runtime.EntityFrameworkCore` | 全体 | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.InMemory` | 全体 | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.SqlServer` | 全体 | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/ParitySqlServerSaves`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.Sqlite` | 全体 | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityOutcomes`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.Sync` | 全体 | `SyncModel/Config`・`SyncModel/Outcomes`・`SyncModel/Random` |
| `src/QuickER.Settings` | 全体 | `DocumentFile/Settings` |

## 範囲の表

守りの範囲（宣言されたパス）ごとの守り。パスの属する範囲は、パスそのものか祖先の行。

| 範囲 | 守り |
|---|---|
| `src/QuickER.AI` | `Ai/ChatEngines` |
| `src/QuickER.AI.Chat/AiChatDialogViewModel.cs` | `Ai/ChatDialog` |
| `src/QuickER.AI.Chat/AiChatFeatureModule.cs` | `OsBoundary/FeatureModules` |
| `src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs` | `DiagramEdit/ToolArgument`・`DiagramEdit/Tools` |
| `src/QuickER.AI.Chat/QueryToolLocalizedFormatter.cs` | `table:QueryToolFormatter` |
| `src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs` | `Ai/MockAgents`・`Ai/MockScaffold`・`OsBoundary/PathEntrances` |
| `src/QuickER.AI.Mock/ClaudeCodeMockProjectAgent.cs` | `Ai/MockAgents` |
| `src/QuickER.AI.Mock/CodexMockProjectAgent.cs` | `Ai/MockAgents` |
| `src/QuickER.AI.Mock/CopilotMockProjectAgent.cs` | `Ai/MockAgents` |
| `src/QuickER.AI.Mock/DotnetBuildRunner.cs` | `Ai/MockBuildBoundary` |
| `src/QuickER.AI.Mock/MockBundleExporter.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.Mock/MockContentValidator.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.Mock/MockDesignDocExporter.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.Mock/MockFolderDesignSession.cs` | `Ai/MockFolder` |
| `src/QuickER.AI.Mock/MockFolderStore.cs` | `Ai/MockFolder`・`Ai/MockOutputs`・`OsBoundary/Segments` |
| `src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs` | `Ai/MockDialog` |
| `src/QuickER.AI.Mock/MockGenerationFeatureModule.cs` | `OsBoundary/FeatureModules` |
| `src/QuickER.AI.Mock/MockManifest.cs` | `Ai/MockFolder` |
| `src/QuickER.AI.Mock/MockPreviewPanel.xaml.cs` | `OsBoundary/Containment`・`OsBoundary/PathEntrances` |
| `src/QuickER.AI.Mock/MockProjectAgentRunner.cs` | `Ai/MockBuildBoundary`・`Ai/MockRunner` |
| `src/QuickER.AI.Mock/MockProjectBuildBoundary.cs` | `Ai/MockBuildBoundary` |
| `src/QuickER.AI.Mock/MockProjectEmitTools.cs` | `OsBoundary/Containment`・`OsBoundary/PathEntrances`・`OsBoundary/Segments` |
| `src/QuickER.AI.Mock/MockProjectFiles.cs` | `Ai/MockBuildBoundary`・`Ai/MockScaffold`・`OsBoundary/Containment` |
| `src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs` | `Ai/MockBuildBoundary` |
| `src/QuickER.AI.Mock/MockProjectScaffoldService.cs` | `Ai/MockScaffold`・`OsBoundary/Segments` |
| `src/QuickER.AI.Mock/MockResumePrompt.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.Mock/MockSchemaMismatch.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.Mock/MockScreenLinks.cs` | `Ai/MockOutputs` |
| `src/QuickER.AI.UI/ApiKeyStore.cs` | `Ai/ApiKeyStore` |
| `src/QuickER.AI.UI/AssistantMessageView.cs` | `Ai/MarkdownRender` |
| `src/QuickER.AI.UI/AttachmentListViewModel.cs` | `Ai/Attachments` |
| `src/QuickER.AI.UI/BackendConnectionChecks.cs` | `Ai/ConnectionSettings` |
| `src/QuickER.AI.UI/BackendTabSelection.cs` | `Ai/ConnectionSettings` |
| `src/QuickER.AI.UI/BoolToVisibilityConverter.cs` | `table:Converters` |
| `src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs` | `Ai/ApiKeyStore`・`Ai/ConnectionSettings` |
| `src/QuickER.AI.UI/ChatMarkdownRenderer.cs` | `Ai/MarkdownRender` |
| `src/QuickER.AI.UI/SavedApiKey.cs` | `Ai/ApiKeyStore` |
| `src/QuickER.AI.UI/ShellChatLinkOpener.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.AI/AnthropicChatTurnDriver.cs` | `Ai/Drivers` |
| `src/QuickER.AI/ChatAttachment.cs` | `Ai/Attachments` |
| `src/QuickER.AI/ChatTurnEngine.cs` | `rule:chat-engine-tool-invocation-copies`・`rule:chat-engine-tool-invocation-uses` |
| `src/QuickER.AI/ClaudeCodeChatEngine.cs` | `rule:chat-engine-tool-invocation-copies`・`rule:chat-engine-tool-invocation-uses` |
| `src/QuickER.AI/ClaudeCodeClient.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.AI/CodexAppServerClient.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.AI/CodexChatEngine.cs` | `rule:chat-engine-tool-invocation-copies`・`rule:chat-engine-tool-invocation-uses` |
| `src/QuickER.AI/CodexCliLocator.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.AI/CopilotChatEngine.cs` | `rule:chat-engine-tool-invocation-copies`・`rule:chat-engine-tool-invocation-uses` |
| `src/QuickER.AI/CopilotCliLocator.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.AI/CopilotWorkspacePermissionPolicy.cs` | `OsBoundary/Containment`・`OsBoundary/ShellPermission` |
| `src/QuickER.AI/OpenAiTurnDriver.cs` | `Ai/Drivers` |
| `src/QuickER.Cli/CodeGenToolSet.cs` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`DiagramEdit/ToolArgument` |
| `src/QuickER.Cli/GenerationConfigLoader.cs` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success` |
| `src/QuickER.Cli/GenerationExecutor.cs` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success` |
| `src/QuickER.CodeGen.CSharp` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Name/Entrances` |
| `src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs` | `OsBoundary/Segments` |
| `src/QuickER.CodeGen.CSharp/GeneratedFileWriter.cs` | `OsBoundary/Segments` |
| `src/QuickER.CodeGen.CSharp/Queries` | `Dsl/SqlServer`・`Dsl/SqliteMySql` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_01_ValueObjects.scriban` | `table:ValueObjectScenarios` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_03_EditModelsAndMappers.scriban` | `table:EditModelScenarios` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_05_QueryPipeline.scriban` | `table:TranslatorParityPredicates` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_08_EfCore.scriban` | `table:TranslatorParityPredicates` |
| `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban` | `SyncModel/Config`・`SyncModel/Random` |
| `src/QuickER.CodeGen.UI` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success`・`Name/Entrances` |
| `src/QuickER.CodeGen.UI/CodeGenerationFeatureModule.cs` | `OsBoundary/FeatureModules` |
| `src/QuickER.CodeReverse.CSharp` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Db.UI/DbConnectionDialogViewModel.cs` | `Ai/DbConnectionDialog` |
| `src/QuickER.Db.UI/DbImportCommandService.cs` | `Ai/DbImportSync` |
| `src/QuickER.Db.UI/DbSyncCommandService.cs` | `Ai/DbImportSync` |
| `src/QuickER.Db.UI/DbToolsFeatureModule.cs` | `OsBoundary/FeatureModules` |
| `src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs` | `Ai/DbImportSync` |
| `src/QuickER.Db.UI/SqlConnectionProfileStore.cs` | `OsBoundary/ProfileStore` |
| `src/QuickER.Dialects` | `Dialect/Identifiers` |
| `src/QuickER.Dialects/IdentifierLengthLimit.cs` | `Name/DdlRoundTrip` |
| `src/QuickER.Dialects/QuickER.Dialects.csproj` | `rule:dialects-project-references` |
| `src/QuickER.Document` | `DocumentFile/Format` |
| `src/QuickER.Document/JsonStorageService.cs` | `DocumentFile/Random`・`DocumentFile/Scenarios` |
| `src/QuickER.Extensibility/IFeatureModule.cs` | `OsBoundary/FeatureModules` |
| `src/QuickER.Gui.Abstractions/ShutdownSteps.cs` | `OsBoundary/Lifecycle`・`OsBoundary/ShutdownChainExhaustive` |
| `src/QuickER.Gui.Common/MessageBoxDialogService.cs` | `rule:dialog-owner-resolve-uses`・`rule:message-box-owner-resolver-copies` |
| `src/QuickER.Gui.Common/WpfFileDialogService.cs` | `rule:dialog-owner-resolve-uses`・`rule:file-dialog-ownerless-shows` |
| `src/QuickER.Gui/App.xaml` | `rule:app-shutdown-mode-declaration` |
| `src/QuickER.Gui/App.xaml.cs` | `rule:app-feature-module-shutdown-bypasses`・`rule:app-feature-module-shutdown-uses`・`rule:app-main-window-assignment`・`rule:app-shutdown-mode-overrides`・`rule:app-ui-crash-handler-bypasses`・`rule:app-ui-crash-handler-uses` |
| `src/QuickER.Gui/Behaviors/BehaviorInput.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Behaviors/CanvasViewportBehavior.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Behaviors/DataGridRowReorderBehavior.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Behaviors/DragBehavior.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Behaviors/RubberBandBehavior.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Behaviors/VisualAncestors.cs` | `DiagramEdit/CanvasInput` |
| `src/QuickER.Gui/Converters/CountToVisibilityConverter.cs` | `table:Converters` |
| `src/QuickER.Gui/Converters/NullToBooleanConverter.cs` | `table:Converters` |
| `src/QuickER.Gui/Converters/NullToVisibilityConverter.cs` | `table:Converters` |
| `src/QuickER.Gui/Program.cs` | `rule:program-single-instance-bypasses`・`rule:program-single-instance-uses` |
| `src/QuickER.Gui/Services` | `Name/Entrances` |
| `src/QuickER.Gui/Services/AutoLayoutService.cs` | `DiagramEdit/Layout` |
| `src/QuickER.Gui/Services/CrashHandlingService.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/Services/CrashLogShutdownFailureReporter.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/Services/DbmlExporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/DbmlImporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/DbmlLiteral.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/DocumentContentHash.cs` | `DocumentFile/Random`・`DocumentFile/Scenarios` |
| `src/QuickER.Gui/Services/ErDiagramDynamicTools.cs` | `DiagramEdit/Tools` |
| `src/QuickER.Gui/Services/ExportTextSanitizer.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/FeatureModuleShutdown.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/Services/LayoutGeometry.cs` | `DiagramEdit/Layout` |
| `src/QuickER.Gui/Services/MermaidExporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/MermaidImporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/PostExitActions.cs` | `OsBoundary/Lifecycle`・`OsBoundary/PostExitExhaustive` |
| `src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Gui/Services/UpdateFeed.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/Services/UpdateService.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/UndoRedo` | `DiagramEdit/Undo`・`DiagramEdit/UndoOps` |
| `src/QuickER.Gui/ViewModels` | `DiagramEdit/Selection`・`DiagramEdit/UndoOps`・`Name/Entrances` |
| `src/QuickER.Gui/ViewModels/LanguageSwitchViewModel.cs` | `OsBoundary/Lifecycle` |
| `src/QuickER.Gui/ViewModels/MainViewModel.ImportExport.cs` | `DocumentFile/Random`・`DocumentFile/Scenarios` |
| `src/QuickER.Gui/ViewModels/RelationshipViewModel.cs` | `DiagramEdit/Undo` |
| `src/QuickER.Mcp.Tools` | `DiagramEdit/ToolArgument`・`DiagramEdit/Tools`・`Name/Entrances`・`rule:mcp-tools-column-nullability-raw-reads` |
| `src/QuickER.Mcp.Tools/QueryToolEnglishFormatter.cs` | `table:QueryToolFormatter` |
| `src/QuickER.Mcp/ErDiagramToolCatalog.cs` | `DiagramEdit/ToolArgument`・`DiagramEdit/Tools` |
| `src/QuickER.Mcp/McpToolSet.cs` | `DiagramEdit/ToolArgument` |
| `src/QuickER.Mcp/ToolArgumentShape.cs` | `DiagramEdit/ToolArgument` |
| `src/QuickER.Mcp/ToolArguments.cs` | `DiagramEdit/ToolArgument`・`rule:tool-argument-parsing-owners` |
| `src/QuickER.Model` | `Dialect/DdlText`・`rule:model-dialect-lookups` |
| `src/QuickER.Model/ConstraintNames.cs` | `SyncPlan/HistoryRows` |
| `src/QuickER.Model/DiagramNameDuplicates.cs` | `Name/Entrances` |
| `src/QuickER.Model/ForeignKeyReferentialAction.cs` | `table:ForeignKeyReferentialActions` |
| `src/QuickER.Model/RelationshipType.cs` | `rule:tool-argument-parsing-owners` |
| `src/QuickER.Model/SchemaNames.cs` | `Name/Entrances` |
| `src/QuickER.Platform/AtomicFile.cs` | `OsBoundary/AtomicFile` |
| `src/QuickER.Platform/BatchShimProcessGuard.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.Platform/ExternalLauncher.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.Platform/PathContainment.cs` | `OsBoundary/Containment`・`OsBoundary/PathEntrances` |
| `src/QuickER.Platform/PathExecutableResolver.cs` | `OsBoundary/ProcessLaunch` |
| `src/QuickER.Platform/PathSegments.cs` | `OsBoundary/PathEntrances`・`OsBoundary/Segments` |
| `src/QuickER.Platform/UrlPolicy.cs` | `Ai/MarkdownRender`・`OsBoundary/ProcessLaunch` |
| `src/QuickER.Provider` | `Dialect/DdlText`・`Dialect/ImportWarnings`・`Dialect/Types`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.MySql` | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.MySql/MySqlSchemaImporter.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses` |
| `src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks` |
| `src/QuickER.Provider.Oracle` | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.Oracle/OracleSchemaImporter.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses` |
| `src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks` |
| `src/QuickER.Provider.PostgreSql` | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.PostgreSql/PostgreSqlSchemaImporter.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses` |
| `src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses` |
| `src/QuickER.Provider.SqlServer` | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses` |
| `src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses` |
| `src/QuickER.Provider.Sqlite` | `Dialect/DdlText`・`Dialect/Identifiers`・`Dialect/Types`・`Dialect/TypesRealDb`・`Name/DdlRoundTrip`・`Name/Entrances`・`SyncPlan/Fuzz`・`rule:provider-column-nullability-raw-reads` |
| `src/QuickER.Provider.Sqlite/SqliteFileConnections.cs` | `rule:sqlite-file-connection-pooling-off` |
| `src/QuickER.Provider.Sqlite/SqliteSchemaImporter.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses` |
| `src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs` | `rule:schema-command-bypasses`・`rule:schema-command-helper-uses`・`rule:schema-sync-raw-rollbacks`・`rule:schema-sync-rollback-helper-uses` |
| `src/QuickER.Provider/DdlGeneratorBase.cs` | `Name/DdlRoundTrip` |
| `src/QuickER.Provider/DiagramCodeGenerator.cs` | `CodegenConfig/Cases`・`CodegenConfig/Errors`・`CodegenConfig/Success` |
| `src/QuickER.Provider/DiagramMergeReconciler.cs` | `FormatRoundTrip/Merge`・`FormatRoundTrip/Omissions`・`FormatRoundTrip/RoundTrip`・`FormatRoundTrip/Rules` |
| `src/QuickER.Provider/NameMatching.cs` | `rule:sync-name-comparison-tokens` |
| `src/QuickER.Provider/SchemaDiffService.cs` | `Name/DdlRoundTrip`・`SyncPlan/CapabilityNet`・`SyncPlan/Fuzz`・`SyncPlan/HistoryRows`・`SyncPlan/ShapeRows`・`rule:sync-name-comparison-tokens`・`table:SyncPlanExpectations` |
| `src/QuickER.Provider/SqlNameDuplicates.cs` | `Name/Entrances` |
| `src/QuickER.Provider/SqlNameText.cs` | `Name/DdlRoundTrip`・`rule:sql-name-text-name-sources` |
| `src/QuickER.Provider/SyncDialectCapabilities.cs` | `SyncPlan/CapabilityNet`・`SyncPlan/Fuzz`・`rule:sync-capabilities-initializers` |
| `src/QuickER.Provider/SyncPlan.cs` | `SyncPlan/CapabilityNet`・`SyncPlan/Fuzz`・`table:SyncPlanExpectations` |
| `src/QuickER.Provider/SyncPlanner.cs` | `SyncPlan/CapabilityNet`・`SyncPlan/Fuzz`・`SyncPlan/ShapeRows`・`rule:sync-name-comparison-tokens`・`rule:sync-selected-foreign-key-names`・`rule:sync-type-deciding-column-copies`・`table:SyncPlanExpectations` |
| `src/QuickER.Provider/SyncScriptBuilderBase.cs` | `SyncPlan/Fuzz` |
| `src/QuickER.Provider/SyncScriptBuilderHelper.cs` | `SyncPlan/Fuzz` |
| `src/QuickER.Provider/UniqueConstraintNaming.cs` | `rule:sync-name-comparison-tokens` |
| `src/QuickER.Runtime` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityOutcomes`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues`・`table:EditModelScenarios`・`table:TranslatorParityPredicates`・`table:ValueObjectScenarios` |
| `src/QuickER.Runtime.AspNetCore` | `GeneratedRuntime/Parity`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues` |
| `src/QuickER.Runtime.EntityFrameworkCore` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParitySqlServerSaves`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.InMemory` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.SqlServer` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/ParitySqlServerSaves`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.Sqlite` | `Dsl/SqlServer`・`Dsl/SqliteMySql`・`GeneratedRuntime/Parity`・`GeneratedRuntime/ParityOutcomes`・`GeneratedRuntime/ParityValues`・`table:TranslatorParityPredicates` |
| `src/QuickER.Runtime.Sync` | `SyncModel/Config`・`SyncModel/Outcomes`・`SyncModel/Random` |
| `src/QuickER.Settings` | `DocumentFile/Settings` |

## 守りの一覧

範囲を持つ守り（エンジンの部分・単体の表・範囲を絞った規則）。

### `Dsl/SqliteMySql`

- 名前：簡易 DSL の条件の総当たり（SQLite の 3 実装先・MySQL のインメモリ）
- テストのソース：`tests/QuickER.Tests/TestSupport/DslFuzz.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzDimensions.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzGenerator.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzKnownFailures.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzUnits.cs`・`tests/QuickER.Tests/CodeGen.CSharp/DslFuzzExecution.cs`・`tests/QuickER.Tests/CodeGen.CSharp/DslFuzzRunner.cs`・`tests/QuickER.Tests/CodeGen.CSharp/DslConditionFuzzTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp/Queries`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime`・`src/QuickER.Runtime`・`src/QuickER.Runtime.Sqlite`・`src/QuickER.Runtime.SqlServer`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.InMemory`

### `Name/Entrances`

- 名前：名前の総当たり（GUI・ツール・取込・クエリ定義・C# 生成・DDL と差分の入口）
- テストのソース：`tests/QuickER.Tests/TestSupport/NameFuzz.cs`・`tests/QuickER.Tests/TestSupport/NameFuzzDimensions.cs`・`tests/QuickER.Tests/TestSupport/NameFuzzKnownFailures.cs`・`tests/QuickER.Tests/CodeGen.CSharp/NameIdentityGenerationFuzzTests.cs`・`tests/QuickER.Tests/CodeGen.UI/NameIdentityQueryDialogFuzzTests.cs`・`tests/QuickER.Tests/Gui/Services/NameIdentityImportFuzzTests.cs`・`tests/QuickER.Tests/Gui/ViewModels/NameIdentityViewModelFuzzTests.cs`・`tests/QuickER.Tests/Mcp.Tools/NameIdentityToolFuzzTests.cs`・`tests/QuickER.Tests/Provider.Sqlite/NameIdentitySqliteImportFuzzTests.cs`・`tests/QuickER.Tests/Provider/NameIdentityDdlFuzzTests.cs`
- 範囲：`src/QuickER.Model/SchemaNames.cs`・`src/QuickER.Model/DiagramNameDuplicates.cs`・`src/QuickER.Provider/SqlNameDuplicates.cs`・`src/QuickER.Mcp.Tools`・`src/QuickER.Gui/ViewModels`・`src/QuickER.Gui/Services`・`src/QuickER.CodeGen.UI`・`src/QuickER.CodeGen.CSharp`・`src/QuickER.Provider.Sqlite`

### `DiagramEdit/ToolArgument`

- 名前：ツールの引数の形の総当たり（MCP・内蔵チャット・コード生成）
- テストのソース：`tests/QuickER.Tests/TestSupport/ToolArgumentFuzz.cs`・`tests/QuickER.Tests/TestSupport/ToolArgumentFuzzDimensions.cs`・`tests/QuickER.Tests/TestSupport/ToolArgumentFuzzSeed.cs`・`tests/QuickER.Tests/TestSupport/ToolArgumentShapeFuzzKnownFailures.cs`・`tests/QuickER.Tests/TestSupport/ToolArgumentShapeFuzzRunner.cs`・`tests/QuickER.Tests/Mcp.Tools/ToolArgumentShapeFuzzTests.cs`・`tests/QuickER.Tests/AI.Chat/ChatToolArgumentShapeFuzzTests.cs`・`tests/QuickER.Tests/Cli/CodeGenToolArgumentShapeFuzzTests.cs`
- 範囲：`src/QuickER.Mcp/ToolArgumentShape.cs`・`src/QuickER.Mcp/ToolArguments.cs`・`src/QuickER.Mcp/McpToolSet.cs`・`src/QuickER.Mcp/ErDiagramToolCatalog.cs`・`src/QuickER.Mcp.Tools`・`src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs`・`src/QuickER.Cli/CodeGenToolSet.cs`

### `DiagramEdit/Undo`

- 名前：リレーションの種別変更の Undo（全組合せ）
- テストのソース：`tests/QuickER.Tests/Gui/UndoRedo/RelationshipTypeChangeDimensions.cs`・`tests/QuickER.Tests/Gui/UndoRedo/TrackedPropertyUndoExhaustiveTests.cs`・`tests/QuickER.Tests/Gui/UndoRedo/UndoKnownFailures.cs`
- 範囲：`src/QuickER.Gui/ViewModels/RelationshipViewModel.cs`・`src/QuickER.Gui/UndoRedo`

### `Dialect/Types`

- 名前：方言の型の表（解析・書き出しの境界・正規化・マッパー・方言切替・型トークン・型の表記の安全性）
- テストのソース：`tests/QuickER.Tests/Provider/DialectTypeAdapters.cs`・`tests/QuickER.Tests/Provider/DialectTypeCase.cs`・`tests/QuickER.Tests/Provider/DialectTypeRunner.cs`・`tests/QuickER.Tests/Provider/DialectTypeRows.cs`・`tests/QuickER.Tests/Provider/DialectTypeDimensions.cs`・`tests/QuickER.Tests/Provider/DialectTypeTableTests.cs`
- 範囲：`src/QuickER.Provider`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.Sqlite`

### `Dialect/Identifiers`

- 名前：方言の識別子の表（クォート・分割・エスケープ・動的 SQL 用のクォート・生成ランタイムの引用・スキーマ修飾名の分け方）
- テストのソース：`tests/QuickER.Tests/Provider/DialectDdlAdapters.cs`・`tests/QuickER.Tests/Provider/DialectIdentifierCase.cs`・`tests/QuickER.Tests/Provider/DialectIdentifierRows.cs`・`tests/QuickER.Tests/Provider/DialectIdentifierDimensions.cs`・`tests/QuickER.Tests/Provider/DialectIdentifierTableTests.cs`
- 範囲：`src/QuickER.Dialects`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.Sqlite`

### `Dialect/DdlText`

- 名前：方言の DDL の表（DDL・同期スクリプトの文の形・識別子・制約名・注記・扱い・固定文の英語・カルチャ）
- テストのソース：`tests/QuickER.Tests/Provider/DialectDdlAdapters.cs`・`tests/QuickER.Tests/Provider/DialectDdlCase.cs`・`tests/QuickER.Tests/Provider/DialectDdlReader.cs`・`tests/QuickER.Tests/Provider/DialectDdlRunner.cs`・`tests/QuickER.Tests/Provider/DialectDdlRows.cs`・`tests/QuickER.Tests/Provider/DialectDdlDimensions.cs`・`tests/QuickER.Tests/Provider/DialectDdlTableTests.cs`
- 範囲：`src/QuickER.Provider`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.Sqlite`・`src/QuickER.Model`

### `Dialect/ImportWarnings`

- 名前：取込警告の表（全種別の文言を英日で整形できる・見出しは件数を含む）
- テストのソース：`tests/QuickER.Tests/Provider/DialectImportWarningRows.cs`・`tests/QuickER.Tests/Provider/DialectImportWarningDimensions.cs`・`tests/QuickER.Tests/Provider/DialectImportWarningTableTests.cs`
- 範囲：`src/QuickER.Provider`

### `CodegenConfig/Errors`

- 名前：生成の構成の網羅の配列（全組。生成時エラーの組を含め、診断の予測を照合する）
- テストのソース：`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenConfigDimensions.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRow.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenSuccessDomain.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/ShapeDiagram.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenEntrances.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRunner.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/Judges`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenMatrixTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp`・`src/QuickER.CodeGen.UI`・`src/QuickER.Provider/DiagramCodeGenerator.cs`・`src/QuickER.Cli/GenerationExecutor.cs`・`src/QuickER.Cli/GenerationConfigLoader.cs`・`src/QuickER.Cli/CodeGenToolSet.cs`

### `CodegenConfig/Success`

- 名前：生成の構成の網羅の配列（生成が通る構成。コンパイル・意味・探針・関係を数える）
- テストのソース：`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenConfigDimensions.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRow.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenSuccessDomain.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/ShapeDiagram.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenEntrances.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRunner.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/Judges`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenMatrixTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp`・`src/QuickER.CodeGen.UI`・`src/QuickER.Provider/DiagramCodeGenerator.cs`・`src/QuickER.Cli/GenerationExecutor.cs`・`src/QuickER.Cli/GenerationConfigLoader.cs`・`src/QuickER.Cli/CodeGenToolSet.cs`

### `CodegenConfig/Cases`

- 名前：生成の構成の決まった行（元のテストの図・オプション・期待）
- テストのソース：`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenConfigDimensions.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRow.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenSuccessDomain.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/ShapeDiagram.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenEntrances.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenRunner.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/Judges`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenCases.cs`・`tests/QuickER.Tests/CodeGen.CSharp/CodegenConfig/CodegenCaseTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp`・`src/QuickER.CodeGen.UI`・`src/QuickER.Provider/DiagramCodeGenerator.cs`・`src/QuickER.Cli/GenerationExecutor.cs`・`src/QuickER.Cli/GenerationConfigLoader.cs`・`src/QuickER.Cli/CodeGenToolSet.cs`

### `DiagramEdit/Tools`

- 名前：ER 図のツールの決定的な行（両ホスト）
- テストのソース：`tests/QuickER.Tests/Mcp.Tools/ToolScenario.cs`・`tests/QuickER.Tests/Mcp.Tools/ToolScenarioDimensions.cs`・`tests/QuickER.Tests/Mcp.Tools/ToolScenarioRows.cs`・`tests/QuickER.Tests/Mcp.Tools/ToolScenarioTests.cs`・`tests/QuickER.Tests/Mcp.Tools/ToolSchemaProjection.cs`・`tests/QuickER.Tests/TestSupport/TwoHostToolHarness.cs`
- 範囲：`src/QuickER.Gui/Services/ErDiagramDynamicTools.cs`・`src/QuickER.Mcp/ErDiagramToolCatalog.cs`・`src/QuickER.Mcp.Tools`・`src/QuickER.AI.Chat/ErDiagramHostChatAdapter.cs`

### `DiagramEdit/UndoOps`

- 名前：VM の操作の往復の行（入口 × 積む履歴の型・並びまで戻る）
- テストのソース：`tests/QuickER.Tests/Gui/UndoRedo/UndoOperation.cs`・`tests/QuickER.Tests/Gui/UndoRedo/UndoOperationDimensions.cs`・`tests/QuickER.Tests/Gui/UndoRedo/UndoOperationRows.cs`・`tests/QuickER.Tests/Gui/UndoRedo/UndoOperationTests.cs`・`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariant.cs`
- 範囲：`src/QuickER.Gui/UndoRedo`・`src/QuickER.Gui/ViewModels`

### `DiagramEdit/Selection`

- 名前：選択の不変条件の行（外れ得るもの × 外れ方）
- テストのソース：`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariant.cs`・`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantDimensions.cs`・`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantRows.cs`・`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantScenario.cs`・`tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantTests.cs`
- 範囲：`src/QuickER.Gui/ViewModels`

### `DiagramEdit/Layout`

- 名前：自動整列の全組の共通の性質と行の指標（図の形 × 整列の種類）
- テストのソース：`tests/QuickER.Tests/Gui/Services/AutoLayoutScenario.cs`・`tests/QuickER.Tests/Gui/Services/AutoLayoutScenarioRows.cs`・`tests/QuickER.Tests/Gui/Services/AutoLayoutScenarioTests.cs`・`tests/QuickER.Tests/Gui/Services/AutoLayoutDimensions.cs`
- 範囲：`src/QuickER.Gui/Services/AutoLayoutService.cs`・`src/QuickER.Gui/Services/LayoutGeometry.cs`

### `DiagramEdit/CanvasInput`

- 名前：キャンバスの入力の列の状態機械（本物の MainWindow へキー・マウスの手の列を流し、各手の後に不変条件を表明する。Space の押下の次元表と左ボタンの押下の次元表）
- テストのソース：`tests/QuickER.Tests/Gui/Behaviors/CanvasInputDimensions.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputGenerator.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputHarness.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputKnownFailures.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputRows.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputSteps.cs`・`tests/QuickER.Tests/Gui/Behaviors/CanvasInputTests.cs`
- 範囲：`src/QuickER.Gui/Behaviors/BehaviorInput.cs`・`src/QuickER.Gui/Behaviors/CanvasViewportBehavior.cs`・`src/QuickER.Gui/Behaviors/DataGridRowReorderBehavior.cs`・`src/QuickER.Gui/Behaviors/DragBehavior.cs`・`src/QuickER.Gui/Behaviors/RubberBandBehavior.cs`・`src/QuickER.Gui/Behaviors/VisualAncestors.cs`

### `DocumentFile/Scenarios`

- 名前：文書の状態機械の決まった形のシナリオ（元の単発のテストの手の列）
- テストのソース：`tests/QuickER.Tests/Gui/ViewModels/DocumentFileDimensions.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileGenerator.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileHarness.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileInvariants.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileModel.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileOps.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileProbe.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileRunner.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.Document.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.ExternalChange.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.HashRace.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.OverwriteGuard.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileTests.cs`
- 範囲：`src/QuickER.Gui/ViewModels/MainViewModel.ImportExport.cs`・`src/QuickER.Gui/Services/DocumentContentHash.cs`・`src/QuickER.Document/JsonStorageService.cs`

### `DocumentFile/Random`

- 名前：文書の状態機械の乱数の手の列（種ごとの手の列を VM と参照モデルで流す）
- テストのソース：`tests/QuickER.Tests/Gui/ViewModels/DocumentFileDimensions.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileGenerator.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileHarness.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileInvariants.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileModel.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileOps.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileProbe.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileRunner.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.Document.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.ExternalChange.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.HashRace.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileScenarios.OverwriteGuard.cs`・`tests/QuickER.Tests/Gui/ViewModels/DocumentFileTests.cs`
- 範囲：`src/QuickER.Gui/ViewModels/MainViewModel.ImportExport.cs`・`src/QuickER.Gui/Services/DocumentContentHash.cs`・`src/QuickER.Document/JsonStorageService.cs`

### `DocumentFile/Format`

- 名前：図ファイルの形式の表（段 × 結果の種類）
- テストのソース：`tests/QuickER.Tests/Document/DocumentFormatDimensions.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.AtomicSave.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.IdIntegrity.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.NullAndOmitted.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.Persistence.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.RequiredText.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.RoundTrip.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.TargetDbms.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.TryLoadClassification.cs`・`tests/QuickER.Tests/Document/DocumentFormatRows.Version.cs`・`tests/QuickER.Tests/Document/DocumentFormatTests.cs`
- 範囲：`src/QuickER.Document`

### `DocumentFile/Settings`

- 名前：設定 JSON ストアの操作列の表（手 × 手の前のファイルの状態）
- テストのソース：`tests/QuickER.Tests/Settings/SettingsStoreDimensions.cs`・`tests/QuickER.Tests/Settings/SettingsStoreRows.cs`・`tests/QuickER.Tests/Settings/SettingsStoreRows.NullAndCase.cs`・`tests/QuickER.Tests/Settings/SettingsStoreRows.Recovery.cs`・`tests/QuickER.Tests/Settings/SettingsStoreRows.Unavailable.cs`・`tests/QuickER.Tests/Settings/SettingsStoreRows.UnknownKeys.cs`・`tests/QuickER.Tests/Settings/SettingsStoreTests.cs`
- 範囲：`src/QuickER.Settings`

### `FormatRoundTrip/RoundTrip`

- 名前：形の行 × 形式の往復・告知・注入・マージの共通の表明とカルチャの代表の形
- テストのソース：`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatAdapters.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatFacet.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatSchemaProjection.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripRunner.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripCaseTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripDimensions.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatShapeRows.cs`
- 範囲：`src/QuickER.Gui/Services/DbmlExporter.cs`・`src/QuickER.Gui/Services/DbmlImporter.cs`・`src/QuickER.Gui/Services/DbmlLiteral.cs`・`src/QuickER.Gui/Services/MermaidExporter.cs`・`src/QuickER.Gui/Services/MermaidImporter.cs`・`src/QuickER.Gui/Services/ExportTextSanitizer.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs`・`src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs`・`src/QuickER.CodeReverse.CSharp`・`src/QuickER.Provider/DiagramMergeReconciler.cs`

### `FormatRoundTrip/Omissions`

- 名前：告知する形式 × 告知の種類（形の行のケースで告知された種類）
- テストのソース：`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatAdapters.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatFacet.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatSchemaProjection.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripRunner.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripCaseTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripDimensions.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatShapeRows.cs`
- 範囲：`src/QuickER.Gui/Services/DbmlExporter.cs`・`src/QuickER.Gui/Services/DbmlImporter.cs`・`src/QuickER.Gui/Services/DbmlLiteral.cs`・`src/QuickER.Gui/Services/MermaidExporter.cs`・`src/QuickER.Gui/Services/MermaidImporter.cs`・`src/QuickER.Gui/Services/ExportTextSanitizer.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs`・`src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs`・`src/QuickER.CodeReverse.CSharp`・`src/QuickER.Provider/DiagramMergeReconciler.cs`

### `FormatRoundTrip/Rules`

- 名前：形式の規則の行（取込の受理・拒否・警告と不動点）
- テストのソース：`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatAdapters.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatFacet.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatSchemaProjection.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripRunner.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripCaseTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripDimensions.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRuleRows.cs`
- 範囲：`src/QuickER.Gui/Services/DbmlExporter.cs`・`src/QuickER.Gui/Services/DbmlImporter.cs`・`src/QuickER.Gui/Services/DbmlLiteral.cs`・`src/QuickER.Gui/Services/MermaidExporter.cs`・`src/QuickER.Gui/Services/MermaidImporter.cs`・`src/QuickER.Gui/Services/ExportTextSanitizer.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs`・`src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs`・`src/QuickER.CodeReverse.CSharp`・`src/QuickER.Provider/DiagramMergeReconciler.cs`

### `FormatRoundTrip/Merge`

- 名前：マージの行（現在の図と取込の図 → マージ後の図・Id の引き継ぎ・クエリ）と、形の行のマージで戻る面の表明
- テストのソース：`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatAdapters.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatFacet.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatSchemaProjection.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripRunner.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripCaseTests.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatRoundTripDimensions.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatMerge.cs`・`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/FormatMergeRows.cs`
- 範囲：`src/QuickER.Gui/Services/DbmlExporter.cs`・`src/QuickER.Gui/Services/DbmlImporter.cs`・`src/QuickER.Gui/Services/DbmlLiteral.cs`・`src/QuickER.Gui/Services/MermaidExporter.cs`・`src/QuickER.Gui/Services/MermaidImporter.cs`・`src/QuickER.Gui/Services/ExportTextSanitizer.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionDocumentImporter.cs`・`src/QuickER.Gui/Services/TableDefinitionHtmlExporter.cs`・`src/QuickER.Gui/Services/TableDefinitionMergePostProcessor.cs`・`src/QuickER.CodeReverse.CSharp`・`src/QuickER.Provider/DiagramMergeReconciler.cs`

### `Ai/ChatEngines`

- 名前：チャットエンジンの契約の網（エンジン 4 種 × 契約の行）
- テストのソース：`tests/QuickER.Tests/AI/ChatEngines/ApiKeyChatEngineAdapter.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineAdapter.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContract.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Attachment.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Cancel.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Conversation.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Dispose.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Failure.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Ready.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Tool.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractRows.Turn.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineContractTests.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineDimensions.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatEngineRig.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatTurnScript.cs`・`tests/QuickER.Tests/AI/ChatEngines/ClaudeCodeChatEngineAdapter.cs`・`tests/QuickER.Tests/AI/ChatEngines/CodexChatEngineAdapter.cs`・`tests/QuickER.Tests/AI/ChatEngines/CopilotChatEngineAdapter.cs`
- 範囲：`src/QuickER.AI`

### `Ai/Drivers`

- 名前：API キー接続のドライバの表（ドライバ 2 つ × 送る形〔発話の添付の組み合わせ・ツールの呼び出しの ID の往復〕）
- テストのソース：`tests/QuickER.Tests/AI/ChatEngines/ChatDriverAdapters.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatDriverDimensions.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatDriverRows.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatDriverTests.cs`・`tests/QuickER.Tests/AI/ChatEngines/ChatDriverToolRoundTrip.cs`
- 範囲：`src/QuickER.AI/AnthropicChatTurnDriver.cs`・`src/QuickER.AI/OpenAiTurnDriver.cs`

### `Ai/Attachments`

- 名前：添付リストの表（受け付ける範囲 × ファイルの種類・操作の列）
- テストのソース：`tests/QuickER.Tests/AI.UI/AttachmentListDimensions.cs`・`tests/QuickER.Tests/AI.UI/AttachmentListRows.cs`・`tests/QuickER.Tests/AI.UI/AttachmentListTests.cs`
- 範囲：`src/QuickER.AI.UI/AttachmentListViewModel.cs`・`src/QuickER.AI/ChatAttachment.cs`

### `Ai/ConnectionSettings`

- 名前：接続設定の操作の列の表（面 × 手の種類・不変条件 4 つ）
- テストのソース：`tests/QuickER.Tests/AI.UI/ConnectionSettingsDimensions.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsHarness.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.ApiKey.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Codex.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Copilot.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Failure.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Persistence.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Tabs.cs`・`tests/QuickER.Tests/AI.UI/ConnectionSettingsTests.cs`
- 範囲：`src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs`・`src/QuickER.AI.UI/BackendTabSelection.cs`・`src/QuickER.AI.UI/BackendConnectionChecks.cs`

### `Ai/ApiKeyStore`

- 名前：保存した API キーの表（入口 × 暗号ファイルの状態。QuickER.AI.UI の ApiKeyStore と接続設定の VM の保存のオン／オフ）
- テストのソース：`tests/QuickER.Tests/AI.UI/ApiKeyStoreDimensions.cs`・`tests/QuickER.Tests/AI.UI/ApiKeyStoreRows.cs`・`tests/QuickER.Tests/AI.UI/ApiKeyStoreTests.cs`
- 範囲：`src/QuickER.AI.UI/ApiKeyStore.cs`・`src/QuickER.AI.UI/SavedApiKey.cs`・`src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs`

### `Ai/MarkdownRender`

- 名前：AI の回答の Markdown 表示の表（要素 × 入力の形。リンクは http / https だけ・画像を読み込まない・HTML を解釈しない・入れ子の上限でテキスト表示へ）
- テストのソース：`tests/QuickER.Tests/AI.UI/ChatMarkdownDimensions.cs`・`tests/QuickER.Tests/AI.UI/ChatMarkdownRows.cs`・`tests/QuickER.Tests/AI.UI/ChatMarkdownRows.Blocks.cs`・`tests/QuickER.Tests/AI.UI/ChatMarkdownRendererTests.cs`
- 範囲：`src/QuickER.AI.UI/ChatMarkdownRenderer.cs`・`src/QuickER.AI.UI/AssistantMessageView.cs`・`src/QuickER.Platform/UrlPolicy.cs`

### `Ai/ChatDialog`

- 名前：チャットのダイアログの操作の列の表（状態 × 手の種類・不変条件 3 つ）
- テストのソース：`tests/QuickER.Tests/AI.Chat/ChatDialogDimensions.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogHarness.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Attachment.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Backend.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Connect.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Persist.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Shutdown.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogRows.Turn.cs`・`tests/QuickER.Tests/AI.Chat/ChatDialogTests.cs`
- 範囲：`src/QuickER.AI.Chat/AiChatDialogViewModel.cs`

### `OsBoundary/ProcessLaunch`

- 名前：起動の表（入口 × 入力の形 → 結果。シムのガード・実行ファイルの解決・外部の起動）
- テストのソース：`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchDimensions.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Shim.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Resolve.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.External.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Filled.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchRows.Gaps.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ProcessLaunchTests.cs`
- 範囲：`src/QuickER.Platform/BatchShimProcessGuard.cs`・`src/QuickER.Platform/PathExecutableResolver.cs`・`src/QuickER.Platform/ExternalLauncher.cs`・`src/QuickER.Platform/UrlPolicy.cs`・`src/QuickER.AI/CopilotCliLocator.cs`・`src/QuickER.AI/CodexCliLocator.cs`・`src/QuickER.AI/ClaudeCodeClient.cs`・`src/QuickER.AI/CodexAppServerClient.cs`・`src/QuickER.AI.UI/ShellChatLinkOpener.cs`

### `OsBoundary/ShellPermission`

- 名前：シェル許可の表（許可要求 → 自動承認してよいか。QuickER.AI の CopilotWorkspacePermissionPolicy）
- テストのソース：`tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionDimensions.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionRows.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionRows.Filled.cs`・`tests/QuickER.Tests/Platform/OsBoundary/ShellPermissionTests.cs`
- 範囲：`src/QuickER.AI/CopilotWorkspacePermissionPolicy.cs`

### `OsBoundary/PathEntrances`

- 名前：パスの入口の表（入口 × 入力の形 → 結果。モックのプレビュー・提出パス・画面のファイル名）
- テストのソース：`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceDimensions.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Preview.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Emit.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceRows.Screen.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathEntranceTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockPreviewPanel.xaml.cs`・`src/QuickER.AI.Mock/MockProjectEmitTools.cs`・`src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs`・`src/QuickER.Platform/PathContainment.cs`・`src/QuickER.Platform/PathSegments.cs`

### `OsBoundary/AtomicFile`

- 名前：原子的な書き込みの表（操作 × 保存先の形 × 失敗 → 結果。QuickER.Platform の AtomicFile）
- テストのソース：`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileDimensions.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Write.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Forms.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Read.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileRows.Names.cs`・`tests/QuickER.Tests/Platform/OsBoundary/AtomicFileTableTests.cs`
- 範囲：`src/QuickER.Platform/AtomicFile.cs`

### `OsBoundary/Containment`

- 名前：包含の判定の表（呼び出し元 × パスの形。PathContainment を直接と呼び出し元の 5 か所から）
- テストのソース：`tests/QuickER.Tests/Platform/PathContainmentTests.cs`・`tests/QuickER.Tests/Platform/PathContainmentCallerFuzzTests.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathTableDimensions.cs`
- 範囲：`src/QuickER.Platform/PathContainment.cs`・`src/QuickER.AI/CopilotWorkspacePermissionPolicy.cs`・`src/QuickER.AI.Mock/MockProjectFiles.cs`・`src/QuickER.AI.Mock/MockPreviewPanel.xaml.cs`・`src/QuickER.AI.Mock/MockProjectEmitTools.cs`

### `OsBoundary/Segments`

- 名前：名前の判定の表（入口 × 当たった規則。PathSegments の判定 2 つと、名前を受け取る 9 つの入口）
- テストのソース：`tests/QuickER.Tests/Platform/PathSegmentsTests.cs`・`tests/QuickER.Tests/Platform/PathSegmentEntranceFuzzTests.cs`・`tests/QuickER.Tests/Platform/PathSegmentForms.cs`・`tests/QuickER.Tests/Platform/OsBoundary/PathTableDimensions.cs`
- 範囲：`src/QuickER.Platform/PathSegments.cs`・`src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs`・`src/QuickER.CodeGen.CSharp/GeneratedFileWriter.cs`・`src/QuickER.AI.Mock/MockFolderStore.cs`・`src/QuickER.AI.Mock/MockProjectScaffoldService.cs`・`src/QuickER.AI.Mock/MockProjectEmitTools.cs`

### `OsBoundary/Lifecycle`

- 名前：アプリの一生の表（入口 × 段の失敗・前提の形。終了の連鎖・クラッシュ・更新・正常終了の最後の処理・言語の切替）
- テストのソース：`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleDimensions.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Shutdown.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Faults.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Crash.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Update.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.PostExit.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleRows.Language.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/LifecycleTests.cs`
- 範囲：`src/QuickER.Gui.Abstractions/ShutdownSteps.cs`・`src/QuickER.Gui/Services/FeatureModuleShutdown.cs`・`src/QuickER.Gui/Services/PostExitActions.cs`・`src/QuickER.Gui/Services/CrashHandlingService.cs`・`src/QuickER.Gui/Services/CrashLogShutdownFailureReporter.cs`・`src/QuickER.Gui/Services/UpdateFeed.cs`・`src/QuickER.Gui/Services/UpdateService.cs`・`src/QuickER.Gui/ViewModels/LanguageSwitchViewModel.cs`

### `OsBoundary/PostExitExhaustive`

- 名前：終了の最後の処理の総当たり（更新 × 再起動の要求 × 起動処理 × 座の手放し・全 60 組）
- テストのソース：`tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveDimensions.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveRows.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/PostExitExhaustiveTests.cs`
- 範囲：`src/QuickER.Gui/Services/PostExitActions.cs`

### `OsBoundary/ShutdownChainExhaustive`

- 名前：終了の連鎖の総当たり（4 段 × 記録先・全 32 組）
- テストのソース：`tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveDimensions.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveRows.cs`・`tests/QuickER.Tests/Gui/Services/Lifecycle/ShutdownChainExhaustiveTests.cs`
- 範囲：`src/QuickER.Gui.Abstractions/ShutdownSteps.cs`

### `OsBoundary/FeatureModules`

- 名前：機能モジュールの契約の表（モジュール × 契約・AI チャット・モック生成・コード生成・DB ツール）
- テストのソース：`tests/QuickER.Tests/Extensibility/FeatureModuleContractDimensions.cs`・`tests/QuickER.Tests/Extensibility/FeatureModuleContractRows.cs`・`tests/QuickER.Tests/Extensibility/FeatureModuleContractTests.cs`
- 範囲：`src/QuickER.Extensibility/IFeatureModule.cs`・`src/QuickER.AI.Chat/AiChatFeatureModule.cs`・`src/QuickER.AI.Mock/MockGenerationFeatureModule.cs`・`src/QuickER.CodeGen.UI/CodeGenerationFeatureModule.cs`・`src/QuickER.Db.UI/DbToolsFeatureModule.cs`

### `OsBoundary/ProfileStore`

- 名前：接続プロファイルの保存の表（入口 × connections.json の状態 × 暗号ファイルの状態 × 暗号フォルダの状態。QuickER.Db.UI の SqlConnectionProfileStore）
- テストのソース：`tests/QuickER.Tests/Db.UI/ProfileStoreDimensions.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Basic.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Conflict.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Legacy.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Tls.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Unreadable.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Pairing.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreRows.Sweep.cs`・`tests/QuickER.Tests/Db.UI/ProfileStoreTests.cs`
- 範囲：`src/QuickER.Db.UI/SqlConnectionProfileStore.cs`

### `Ai/MockAgents`

- 名前：モックプロジェクト生成の実行器の網（実行器 4 種 × 契約の行）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Agents/ApiKeyMockAgentAdapter.cs`・`tests/QuickER.Tests/AI.Mock/Agents/ApiKeyMockMaterials.cs`・`tests/QuickER.Tests/AI.Mock/Agents/ClaudeCodeMockAgentAdapter.cs`・`tests/QuickER.Tests/AI.Mock/Agents/CodexMockAgentAdapter.cs`・`tests/QuickER.Tests/AI.Mock/Agents/CopilotMockAgentAdapter.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentAdapter.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentContract.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentContractRows.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentContractTests.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentRig.cs`・`tests/QuickER.Tests/AI.Mock/Agents/MockAgentScript.cs`・`tests/QuickER.Tests/AI.Mock/Agents/QueueBuildRunner.cs`
- 範囲：`src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs`・`src/QuickER.AI.Mock/ClaudeCodeMockProjectAgent.cs`・`src/QuickER.AI.Mock/CodexMockProjectAgent.cs`・`src/QuickER.AI.Mock/CopilotMockProjectAgent.cs`

### `Ai/MockRunner`

- 名前：モックプロジェクト生成のランナーの流れの表（実行器 → 成果物 → 画面の数 → 最終ビルド → 結果の分類）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Runner/MockRunnerContext.cs`・`tests/QuickER.Tests/AI.Mock/Runner/MockRunnerDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Runner/MockRunnerFakes.cs`・`tests/QuickER.Tests/AI.Mock/Runner/MockRunnerRows.cs`・`tests/QuickER.Tests/AI.Mock/Runner/MockRunnerTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockProjectAgentRunner.cs`

### `Ai/MockBuildBoundary`

- 名前：モックプロジェクトの最終ビルドの境界の表（変更検知・ビルド出力の削除・成果物の探索・上書きの走査・最終ビルドの引数 × 種別）
- テストのソース：`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryContext.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryDimensions.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Arguments.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Clear.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Files.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryRows.Kinds.cs`・`tests/QuickER.Tests/AI.Mock/BuildBoundary/MockBuildBoundaryTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockProjectBuildBoundary.cs`・`src/QuickER.AI.Mock/MockProjectFiles.cs`・`src/QuickER.AI.Mock/MockProjectOverwriteScanner.cs`・`src/QuickER.AI.Mock/MockProjectAgentRunner.cs`・`src/QuickER.AI.Mock/DotnetBuildRunner.cs`

### `Ai/MockScaffold`

- 名前：モックプロジェクトのスキャフォールドの表（ターゲット × 方言 × 出力フォルダの状態。構成物・名前空間の案内・プロジェクト名・作り直し）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldContext.cs`・`tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldRows.cs`・`tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldRows.Regeneration.cs`・`tests/QuickER.Tests/AI.Mock/Scaffold/MockScaffoldTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockProjectScaffoldService.cs`・`src/QuickER.AI.Mock/MockProjectFiles.cs`・`src/QuickER.AI.Mock/ApiKeyMockProjectAgent.cs`

### `Ai/MockFolder`

- 名前：モックフォルダの保存単位の操作の列（ストアとセッションの手・どの手の後にも不変条件を見張る）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Folder/MockFolderDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Folder/MockFolderHarness.cs`・`tests/QuickER.Tests/AI.Mock/Folder/MockFolderRows.cs`・`tests/QuickER.Tests/AI.Mock/Folder/MockFolderRows.Session.cs`・`tests/QuickER.Tests/AI.Mock/Folder/MockFolderScenario.cs`・`tests/QuickER.Tests/AI.Mock/Folder/MockFolderTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockFolderStore.cs`・`src/QuickER.AI.Mock/MockFolderDesignSession.cs`・`src/QuickER.AI.Mock/MockManifest.cs`

### `Ai/MockOutputs`

- 名前：モックフォルダの出力の入力→出力の表（リンクの表記と画面 ID を検証器・単一 HTML・設計書へ流す・再開のプロンプト・スキーマの食い違い・読み込みの検証）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsContext.cs`・`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.cs`・`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.Doc.cs`・`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.Prompt.cs`・`tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockScreenLinks.cs`・`src/QuickER.AI.Mock/MockContentValidator.cs`・`src/QuickER.AI.Mock/MockBundleExporter.cs`・`src/QuickER.AI.Mock/MockDesignDocExporter.cs`・`src/QuickER.AI.Mock/MockResumePrompt.cs`・`src/QuickER.AI.Mock/MockSchemaMismatch.cs`・`src/QuickER.AI.Mock/MockFolderStore.cs`

### `Ai/MockDialog`

- 名前：モック生成のダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 12 個を見張る）
- テストのソース：`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogDimensions.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogFakes.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogHarness.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Backend.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Conversation.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Folder.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Generate.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Screen.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Shutdown.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.StateGaps.cs`・`tests/QuickER.Tests/AI.Mock/Dialog/MockDialogTests.cs`
- 範囲：`src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs`

### `Ai/DbConnectionDialog`

- 名前：接続ダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 14 個を見張る）
- テストのソース：`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogDimensions.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogFakes.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogHarness.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Corrupt.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Failure.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Ok.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Open.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Profile.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Sqlite.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Stale.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Sweep.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.SweepFailure.cs`・`tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogTests.cs`
- 範囲：`src/QuickER.Db.UI/DbConnectionDialogViewModel.cs`

### `Ai/DbImportSync`

- 名前：DB 取込・DB 同期のコマンドと同期のダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 14 個を見張る）
- テストのソース：`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncDimensions.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncFakes.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncHarness.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.Import.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncCommand.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncExecute.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncPreview.cs`・`tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncTests.cs`
- 範囲：`src/QuickER.Db.UI/DbImportCommandService.cs`・`src/QuickER.Db.UI/DbSyncCommandService.cs`・`src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs`

### `SyncModel/Random`

- 名前：同期のモデル（乱数の操作の列）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelCoverageRows.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelExpectationOperations.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelFaults.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelGenerator.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelKnownFailures.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelOperations.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRun.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRun.Expect.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.Blobs.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.Conflicts.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.ForeignKeys.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.PropagationLww.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelScenarios.Engine.Refresh.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelServer.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelTransportRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelTransports.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelWorld.cs`
- 範囲：`src/QuickER.Runtime.Sync`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban`

### `SyncModel/Outcomes`

- 名前：同期のモデル（乱数の種で同期が返した競合・自分から変えた行の理由）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelOutcomeDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRun.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRuntimeTests.cs`
- 範囲：`src/QuickER.Runtime.Sync`

### `SyncModel/Config`

- 名前：同期の構成の行列（DI の登録の形・転送経路・キーの型・キーを決めるフック・書き込みの経路）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigFixtures.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigIntKeyFixture.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigVoKeyFixture.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigMatrixRuntimeTests.cs`
- 範囲：`src/QuickER.Runtime.Sync`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban`

### `SyncPlan/Fuzz`

- 名前：同期計画の総当たり（5 方言の実 DB）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCoverageRows.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDialect.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDimensions.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzKnownFailures.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzRunner.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzTestsBase.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs`
- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/SyncPlanner.cs`・`src/QuickER.Provider/SyncPlan.cs`・`src/QuickER.Provider/SyncDialectCapabilities.cs`・`src/QuickER.Provider/SyncScriptBuilderBase.cs`・`src/QuickER.Provider/SyncScriptBuilderHelper.cs`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.Sqlite`

### `SyncPlan/CapabilityNet`

- 名前：同期計画の能力の組の網（5 方言の能力・DB なし）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCapabilityNet.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCoverageRows.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanStaticRules.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs`
- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/SyncPlanner.cs`・`src/QuickER.Provider/SyncPlan.cs`・`src/QuickER.Provider/SyncDialectCapabilities.cs`

### `SyncPlan/ShapeRows`

- 名前：同期計画の形の行（D4・網羅の配列から作る決定的な行）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanShapeRows.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs`
- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/SyncPlanner.cs`

### `SyncPlan/HistoryRows`

- 名前：同期計画の来歴の行（DB の来歴 × 子の表名の長さ × 2 本目の外部キー）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanHistoryRows.cs`・`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanHistories.cs`
- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Model/ConstraintNames.cs`

### `GeneratedRuntime/Parity`

- 名前：実装先どうしの一致（乱数の操作の列）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityBackend.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityCoverageRows.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.Customers.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.Values.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityKnownDifferences.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityOperations.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRunner.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityScenarios.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerRuntimeTests.cs`
- 範囲：`src/QuickER.Runtime`・`src/QuickER.Runtime.Sqlite`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.InMemory`・`src/QuickER.Runtime.AspNetCore`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime`

### `GeneratedRuntime/ParityOutcomes`

- 名前：実装先どうしの一致（乱数の操作の列で Save フックが受けた段・操作・規則の振る舞い）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityOutcomeDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityBackend.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs`
- 範囲：`src/QuickER.Runtime`・`src/QuickER.Runtime.Sqlite`

### `GeneratedRuntime/ParityValues`

- 名前：実装先どうしの一致（乱数の操作の列が書く値の幅：日時の Kind と端数・decimal の端・blob の長さ・文字列の文字）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityValueDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.Values.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs`
- 範囲：`src/QuickER.Runtime`・`src/QuickER.Runtime.Sqlite`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.InMemory`・`src/QuickER.Runtime.AspNetCore`

### `GeneratedRuntime/ParitySqlServerSaves`

- 名前：実装先どうしの一致（SQL Server の系統の乱数の保存）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveComparison.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.SqlServerSaveComparison.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveDimensions.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerSaveGenerator.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParitySqlServerRuntimeTests.RandomSaves.cs`
- 範囲：`src/QuickER.Runtime`・`src/QuickER.Runtime.SqlServer`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.AspNetCore`

### `Dsl/SqlServer`

- 名前：簡易 DSL の条件の総当たり（SQL Server の QuickER 版 Repository と EF Core）
- テストのソース：`tests/QuickER.Tests/TestSupport/DslFuzz.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzDimensions.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzGenerator.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzKnownFailures.cs`・`tests/QuickER.Tests/TestSupport/DslFuzzUnits.cs`・`tests/QuickER.Tests/CodeGen.CSharp/DslFuzzExecution.cs`・`tests/QuickER.Tests/CodeGen.CSharp/DslFuzzRunner.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/DslConditionFuzzSqlServerRuntimeTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp/Queries`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime`・`src/QuickER.Runtime`・`src/QuickER.Runtime.Sqlite`・`src/QuickER.Runtime.SqlServer`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.InMemory`

### `Name/DdlRoundTrip`

- 名前：名前の総当たりの DDL の往復（5 方言の実 DB）
- テストのソース：`tests/QuickER.Tests/TestSupport/NameFuzz.cs`・`tests/QuickER.Tests/TestSupport/NameFuzzDiagrams.cs`・`tests/QuickER.Tests/TestSupport/NameFuzzKnownFailures.cs`・`tests/QuickER.Tests.Integration/Dialects/NameFuzzDdlRoundTripDimensions.cs`・`tests/QuickER.Tests.Integration/Dialects/NameFuzzDdlRoundTripIntegrationTests.cs`
- 範囲：`src/QuickER.Dialects/IdentifierLengthLimit.cs`・`src/QuickER.Provider/SqlNameText.cs`・`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/DdlGeneratorBase.cs`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.Sqlite`

### `Dialect/TypesRealDb`

- 名前：方言の型の表の実 DB の段（上限の対の DDL の適用と取り込み直し・マッパーとドライバの CLR 型の一致・EF Core の読み書き）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbRows.cs`・`tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbTests.cs`・`tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbDimensions.cs`・`tests/QuickER.Tests.Integration/Dialects/DialectTypeRealDbTableTests.cs`・`tests/QuickER.Tests/Provider/DialectTypeRows.cs`
- 範囲：`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.Sqlite`

### `table:EditModelScenarios`

- 名前：生成した EditModel・Mapper の決まった形のシナリオ（書式・空欄・範囲の削除・Removed・Mapper の null・兄弟・DB のエラー）
- テストのソース：`tests/QuickER.Tests/GeneratedFixture/EditModelScenarioRows.GeneratedFixture.cs`・`tests/QuickER.Tests/GeneratedFixture/EditModelScenarioRows.InMemoryFixture.cs`・`tests/QuickER.Tests/GeneratedFixture/EditModelScenarioRows.QueryFixture.cs`・`tests/QuickER.Tests/GeneratedFixture/EditModelScenarios.GeneratedFixture.cs`・`tests/QuickER.Tests/GeneratedFixture/EditModelScenarios.InMemoryFixture.cs`・`tests/QuickER.Tests/GeneratedFixture/EditModelScenarios.QueryFixture.cs`
- 範囲：`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_03_EditModelsAndMappers.scriban`・`src/QuickER.Runtime`

### `table:ValueObjectScenarios`

- 名前：値オブジェクトの決まった形のシナリオ（呼び出し・参照の同一性・等値・初回使用）
- テストのソース：`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Conversion.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Creation.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Declared.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Defined.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Equality.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Formatting.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarioRows.Members.cs`・`tests/QuickER.Tests/GeneratedFixture/ValueObjectScenarios.cs`
- 範囲：`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_01_ValueObjects.scriban`・`src/QuickER.Runtime`

### `table:ForeignKeyReferentialActions`

- 名前：参照アクションと文字列表現の相互変換の表（SQL 表記の往復・表記ゆれの読み取り）
- テストのソース：`tests/QuickER.Tests/Model/ForeignKeyReferentialActionTests.cs`
- 範囲：`src/QuickER.Model/ForeignKeyReferentialAction.cs`

### `table:Converters`

- 名前：XAML の表示の値のコンバーターの表（反転のパラメーター・件数のしきい値・null の扱い。使われていないアプリ本体の BoolToVisibilityConverter は入れない）
- テストのソース：`tests/QuickER.Tests/Gui/Converters/ConverterRows.cs`・`tests/QuickER.Tests/Gui/Converters/ConverterTests.cs`
- 範囲：`src/QuickER.Gui/Converters/CountToVisibilityConverter.cs`・`src/QuickER.Gui/Converters/NullToBooleanConverter.cs`・`src/QuickER.Gui/Converters/NullToVisibilityConverter.cs`・`src/QuickER.AI.UI/BoolToVisibilityConverter.cs`

### `table:QueryToolFormatter`

- 名前：名前付きクエリのツールの結果の整形の表（MCP 面の英語の整形と、内蔵チャット面の UI の言語〔en・ja〕に追従する整形・改行は LF だけ）
- テストのソース：`tests/QuickER.Tests/Mcp.Tools/QueryToolFormatterRows.cs`・`tests/QuickER.Tests/Mcp.Tools/QueryToolFormatterTests.cs`
- 範囲：`src/QuickER.Mcp.Tools/QueryToolEnglishFormatter.cs`・`src/QuickER.AI.Chat/QueryToolLocalizedFormatter.cs`

### `table:TranslatorParityPredicates`

- 名前：式の翻訳の述語の表（SQL Server・SQLite・インメモリと SQL Server・SQLite の EF Core 版の結果を C# の意味論と突き合わせる）
- テストのソース：`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityPredicateRows.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityRuntimeTestsBase.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityInMemoryRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParitySqlServerRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParitySqliteRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityEfCoreRuntimeTestsBase.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityEfCoreSqlServerRuntimeTests.cs`・`tests/QuickER.Tests.Integration/GeneratedRuntime/TranslatorParityEfCoreSqliteRuntimeTests.cs`
- 範囲：`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_05_QueryPipeline.scriban`・`src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_08_EfCore.scriban`・`src/QuickER.Runtime`・`src/QuickER.Runtime.EntityFrameworkCore`・`src/QuickER.Runtime.InMemory`・`src/QuickER.Runtime.SqlServer`・`src/QuickER.Runtime.Sqlite`

### `table:SyncPlanExpectations`

- 名前：同期の差分と計画の期待の行（SyncPlan のエンジン〔SyncPlan/Fuzz・SyncPlan/CapabilityNet〕が流す。方言ごとの期待と実 DB の段の汎用の表明）
- テストのソース：`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanExpectationRows.cs`
- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/SyncPlan.cs`・`src/QuickER.Provider/SyncPlanner.cs`

### `rule:model-dialect-lookups`

- 範囲：`src/QuickER.Model`

### `rule:sync-capabilities-initializers`

- 範囲：`src/QuickER.Provider/SyncDialectCapabilities.cs`

### `rule:dialects-project-references`

- 範囲：`src/QuickER.Dialects/QuickER.Dialects.csproj`

### `rule:schema-command-bypasses`

- 範囲：`src/QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs`・`src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaImporter.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.MySql/MySqlSchemaImporter.cs`・`src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.Oracle/OracleSchemaImporter.cs`・`src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaImporter.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs`

### `rule:schema-command-helper-uses`

- 範囲：`src/QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs`・`src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaImporter.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.MySql/MySqlSchemaImporter.cs`・`src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.Oracle/OracleSchemaImporter.cs`・`src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaImporter.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs`

### `rule:schema-sync-raw-rollbacks`

- 範囲：`src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.MySql/MySqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.Oracle/OracleSchemaSyncExecutor.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs`

### `rule:schema-sync-rollback-helper-uses`

- 範囲：`src/QuickER.Provider.SqlServer/SqlServerSchemaSyncExecutor.cs`・`src/QuickER.Provider.PostgreSql/PostgreSqlSchemaSyncExecutor.cs`・`src/QuickER.Provider.Sqlite/SqliteSchemaSyncExecutor.cs`

### `rule:sync-name-comparison-tokens`

- 範囲：`src/QuickER.Provider/SchemaDiffService.cs`・`src/QuickER.Provider/SyncPlanner.cs`・`src/QuickER.Provider/NameMatching.cs`・`src/QuickER.Provider/UniqueConstraintNaming.cs`

### `rule:sync-type-deciding-column-copies`

- 範囲：`src/QuickER.Provider/SyncPlanner.cs`

### `rule:sync-selected-foreign-key-names`

- 範囲：`src/QuickER.Provider/SyncPlanner.cs`

### `rule:provider-column-nullability-raw-reads`

- 範囲：`src/QuickER.Provider`・`src/QuickER.Provider.MySql`・`src/QuickER.Provider.Oracle`・`src/QuickER.Provider.PostgreSql`・`src/QuickER.Provider.SqlServer`・`src/QuickER.Provider.Sqlite`

### `rule:sqlite-file-connection-pooling-off`

- 範囲：`src/QuickER.Provider.Sqlite/SqliteFileConnections.cs`

### `rule:sql-name-text-name-sources`

- 範囲：`src/QuickER.Provider/SqlNameText.cs`

### `rule:dialog-owner-resolve-uses`

- 範囲：`src/QuickER.Gui.Common/WpfFileDialogService.cs`・`src/QuickER.Gui.Common/MessageBoxDialogService.cs`

### `rule:file-dialog-ownerless-shows`

- 範囲：`src/QuickER.Gui.Common/WpfFileDialogService.cs`

### `rule:message-box-owner-resolver-copies`

- 範囲：`src/QuickER.Gui.Common/MessageBoxDialogService.cs`

### `rule:app-shutdown-mode-declaration`

- 範囲：`src/QuickER.Gui/App.xaml`

### `rule:app-shutdown-mode-overrides`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:app-main-window-assignment`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:app-feature-module-shutdown-uses`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:app-feature-module-shutdown-bypasses`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:app-ui-crash-handler-uses`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:app-ui-crash-handler-bypasses`

- 範囲：`src/QuickER.Gui/App.xaml.cs`

### `rule:program-single-instance-uses`

- 範囲：`src/QuickER.Gui/Program.cs`

### `rule:program-single-instance-bypasses`

- 範囲：`src/QuickER.Gui/Program.cs`

### `rule:tool-argument-parsing-owners`

- 範囲：`src/QuickER.Mcp/ToolArguments.cs`・`src/QuickER.Model/RelationshipType.cs`

### `rule:mcp-tools-column-nullability-raw-reads`

- 範囲：`src/QuickER.Mcp.Tools`

### `rule:chat-engine-tool-invocation-uses`

- 範囲：`src/QuickER.AI/ChatTurnEngine.cs`・`src/QuickER.AI/ClaudeCodeChatEngine.cs`・`src/QuickER.AI/CodexChatEngine.cs`・`src/QuickER.AI/CopilotChatEngine.cs`

### `rule:chat-engine-tool-invocation-copies`

- 範囲：`src/QuickER.AI/ChatTurnEngine.cs`・`src/QuickER.AI/ClaudeCodeChatEngine.cs`・`src/QuickER.AI/CodexChatEngine.cs`・`src/QuickER.AI/CopilotChatEngine.cs`

## 横断の守り

src 全体か src の外を読む守り。単位の守りには数えない（数えると全単位が守りありになる）。

| 名前 | 読む範囲 |
|---|---|
| `Guard/SourceRules` | src・tests |
| `Guard/Parity` | docs・.github/workflows・src |
| `Guard/Phrases` | src・samples |
| `rule:app-data-path-lookups` | src/**/*.cs（除外 1 件） |
| `rule:path-name-checks` | src/**/*.cs（除外 1 件） |
| `rule:utf8-encoding-creations` | src/**/*.cs（除外 1 件） |
| `rule:path-prefix-comparisons` | src/**/*.cs（除外 1 件） |
| `rule:process-launches` | src/**/*.cs（除外 1 件） |
| `rule:process-launch-create-process-starts` | src/**/*.cs（除外 1 件） |
| `rule:raw-file-writes` | src/**/*.cs（除外 1 件） |
| `rule:constraint-name-safe-rule-copies` | src/**/*.cs（除外 21 件） |
| `rule:constraint-name-default-compositions` | src/**/*.cs（除外 21 件） |
| `rule:unique-constraint-synthesize-name-calls` | src/**/*.cs（除外 20 件） |
| `rule:schema-name-comparisons` | src/**/*.cs（除外 21 件） |
| `rule:dialect-name-literals` | src/**/*.cs（除外 1 件） |
| `rule:schema-qualified-name-dot-splits` | src/**/*.cs（除外 31 件） |
| `rule:column-nullability-copies` | src/**/*.cs（除外 21 件） |
| `rule:numeric-interpolation-culture` | src/**/*.cs（除外 26 件） |
| `rule:sql-comment-unsanitized-holes` | src/**/*.cs（除外 27 件） |
| `rule:schema-import-warning-kind-branches` | src/**/*.cs（除外 1 件） |
| `rule:sqlite-file-connection-creations` | src/**/*.cs（除外 1 件） |
| `rule:sqlite-script-executions` | src/**/*.cs（除外 29 件） |
| `rule:source-invisible-characters-src` | src/**/*.cs |
| `rule:source-invisible-characters-tests` | tests/**/*.cs |
| `rule:cjk-range-copies` | tests/**/*.cs |
| `rule:regex-ignore-case-culture` | src/**/*.cs |
| `rule:regex-digit-classes` | src/**/*.cs |
| `rule:ef-core-provider-cache-limit` | tests/**/*.cs（除外 33 件） |
| `rule:window-activation-collection` | tests/QuickER.Tests/**/*.cs（除外 3 件） |
| `rule:window-activation-markers` | tests/QuickER.Tests/AI.Chat/AiChatDialogApiKeyBoxTests.cs・tests/QuickER.Tests/Gui/Behaviors/CanvasViewportBehaviorSpaceKeyTests.cs・tests/QuickER.Tests/Gui/Views/PropertyPanelMemoEditTests.cs |
| `rule:wpf-main-window-creations` | tests/QuickER.Tests/**/*.cs |
| `rule:direct-roslyn-parses` | tests/**/*.cs |
| `rule:test-host-server-gc` | tests/QuickER.Tests/QuickER.Tests.csproj |
| `rule:test-temp-folder-root-copies` | tests/**/*.cs |
| `rule:datagrid-row-edits` | src/**/*.xaml |
| `rule:behavior-input-reads` | src/**/*.cs（除外 1 件） |
| `rule:behavior-visual-ancestor-copies` | src/**/*.cs（除外 1 件） |
| `rule:tool-argument-parsing-copies` | src/**/*.cs |
| `rule:newer-format-warning-text` | src/**/*.cs |
| `rule:comment-test-names-src` | src/**/*.cs |
| `rule:comment-test-names-templates` | src/**/*.scriban |
| `rule:comment-test-names-workflows` | .github/workflows/**/*.yml |
| `rule:comment-test-names-scripts` | scripts/**/*.ps1 |
| `parity:licensing-mit-row` | 正本と写しの照合 |
| `parity:licensing-ja-mit-row` | 正本と写しの照合 |
| `parity:license-nc-scope-holds-nc-packages` | 正本と写しの照合 |
| `parity:license-nc-scope-lists-existing-non-mit-projects` | 正本と写しの照合 |
| `parity:nightly-actions-pinned-like-other-workflows` | 正本と写しの照合 |
| `parity:nightly-jobs` | 正本と写しの照合 |
| `parity:nightly-job-test-commands` | 正本と写しの照合 |
| `parity:nightly-job-engine-profile` | 正本と写しの照合 |
| `parity:release-vpk-version` | 正本と写しの照合 |
| `parity:release-vpk-pack-runtime` | 正本と写しの照合 |
| `parity:pack-velopack-vpk-pack-runtime` | 正本と写しの照合 |
| `parity:test-projects-run-on-mtp` | 正本と写しの照合 |
| `parity:workflow-test-commands-use-mtp-options` | 正本と写しの照合 |
| `parity:workflow-test-telemetry-opt-out` | 正本と写しの照合 |
| `parity:mutation-actions-pinned-like-other-workflows` | 正本と写しの照合 |
| `parity:mutation-actions-pinned-by-sha` | 正本と写しの照合 |
| `parity:mutation-targets-match-stryker-configs` | 正本と写しの照合 |
| `parity:stryker-tests-only-solution-projects` | 正本と写しの照合 |
| `parity:stryker-mutate-globs-match-files` | 正本と写しの照合 |
| `parity:stryker-configs-report-only` | 正本と写しの照合 |
| `parity:stryker-docker-fixtures-honor-skip` | 正本と写しの照合 |
| `parity:mutation-script-variables-match-test-constants` | 正本と写しの照合 |
| `parity:resx-ja-key-sets` | 正本と写しの照合 |
| `parity:resx-ja-satellites` | 正本と写しの照合 |
| `parity:resx-accessor-properties` | 正本と写しの照合 |
| `parity:config-schema-keys-match-options` | 正本と写しの照合 |
| `parity:config-schema-enum-allowed-values` | 正本と写しの照合 |
| `parity:cli-config-keys-match-options` | 正本と写しの照合 |
| `parity:cli-config-keys-match-schema` | 正本と写しの照合 |
| `parity:generation-settings-keys-match-options` | 正本と写しの照合 |
| `parity:mock-project-package-ids` | 正本と写しの照合 |
| `parity:mock-project-package-versions` | 正本と写しの照合 |
| `english:neutral-resx-values` | 英語の文言の照合 |
| `english:sample-files` | 英語の文言の照合 |
| `english:tool-definitions-er-diagram-catalog` | 英語の文言の照合 |
| `english:tool-definitions-mock-folder-design` | 英語の文言の照合 |
| `english:tool-definitions-mock-emit-file` | 英語の文言の照合 |
| `english:tool-definitions-codegen-toolset` | 英語の文言の照合 |
| `english:mcp-server-instructions` | 英語の文言の照合 |
| `english:query-tool-english-formatter` | 英語の文言の照合 |
| `english:config-schema-catalog` | 英語の文言の照合 |
| `english:query-tool-localized-formatter-english` | 英語の文言の照合 |
| `english:mock-project-agent-prompts-wpf` | 英語の文言の照合 |
| `english:mock-project-api-key-prompts-wpf` | 英語の文言の照合 |
| `english:mock-project-readme-wpf` | 英語の文言の照合 |
| `english:mock-project-agent-prompts-blazor` | 英語の文言の照合 |
| `english:mock-project-api-key-prompts-blazor` | 英語の文言の照合 |
| `english:mock-project-readme-blazor` | 英語の文言の照合 |
| `english:mock-project-continuation-nudge` | 英語の文言の照合 |
| `english:mock-project-schema-description` | 英語の文言の照合 |
| `phrase:er-design-common-principles-ja` | 必須の文言の照合 |
| `phrase:er-design-common-principles-en` | 必須の文言の照合 |
| `phrase:er-design-chat-instructions-template-en` | 必須の文言の照合 |
| `phrase:er-design-chat-instructions-template-ja` | 必須の文言の照合 |
| `phrase:er-design-response-language-rule-ja` | 必須の文言の照合 |
| `phrase:er-design-response-language-rule-en` | 必須の文言の照合 |
| `phrase:design-guidelines-chat-prompt-en` | 必須の文言の照合 |
| `phrase:design-guidelines-chat-prompt-ja` | 必須の文言の照合 |
| `phrase:design-guidelines-mcp-server-instructions` | 必須の文言の照合 |
| `phrase:mock-folder-design-system-prompt-en` | 必須の文言の照合 |
| `phrase:mock-folder-design-system-prompt-ja` | 必須の文言の照合 |
| `phrase:mock-folder-design-codex-instructions-en` | 必須の文言の照合 |
| `phrase:mock-folder-design-response-language-en` | 必須の文言の照合 |
| `phrase:mock-folder-design-response-language-ja` | 必須の文言の照合 |

## 守りの無い領域

どの守りの範囲にも入らない単位と理由。Pending はエンジン・表で埋める候補、Accepted はエンジン・表で守る対象でないと判断したもの。

| 単位 | 分類 | エンジン・表の外のテスト | 理由 |
|---|---|---|---|
| `src/QuickER.Gui/Views` | Accepted | 単体の手書きのテストだけ（`tests/QuickER.Tests/Gui/Views`） | ダイアログ（バージョン情報・印刷の設定）の code-behind とカードの枠の描画。仕様の軸を持つ状態は ViewModel にあり、エンジンはそちらを守る。画面の振る舞いは手書きの画面のテストが守る |
