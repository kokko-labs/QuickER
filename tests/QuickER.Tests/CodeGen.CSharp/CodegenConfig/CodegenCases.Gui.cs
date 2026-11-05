using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：生成ダイアログ（入口 gui）。画面に入れたオプションの写し・画面に出ていないオプションの落とし方と表示の条件・
/// 確定の結果・保存と復元（元は <c>CSharpGenerationDialogViewModelTests</c>）
/// </summary>
/// <remarks>
/// 表明は GUI の入口（<see cref="GuiEntrance"/>）が全部受け持つ（<c>ToOptions()</c> と規則の照合・表示の条件・確定の後に保持する値・
/// 確定の結果の出力先フォルダとダイアログを閉じること・復元した設定）。生成の結果には元のテストと同じく何も表明しない
/// （元のテストは生成を通らない）。図は 1 表の <see cref="SingleEntityDiagram"/>。
/// 対象 DB は行ごとに明示する（GUI は対象 DB のチェックをそのまま <c>RepositoryDialects</c> へ渡すため、省くと期待と食い違う）。
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>生成ダイアログの行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> GuiRows() =>
        [
            // ===== 画面に出ていないオプション（隠したら結果・設定から落とし、ViewModel では保持する） =====
            GuiRow(
                "gui-gate-sync-dropped-single-dialect",
                "同期支援は非表示構成では結果・設定へ渡さない（値自体は保持する）（CSharpGenerationDialogViewModelTests.SyncSupport_IsDroppedWhenHidden_ButKeptInViewModel。両方言で入れてから SQL Server だけへ戻す）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateSyncSupport = true,
                }
            ),
            GuiRow(
                "gui-gate-exclude-binary-dropped-no-db-access",
                "無制限バイナリ列の除外は非表示構成では結果・設定へ渡さない（値自体は保持する）（CSharpGenerationDialogViewModelTests.ExcludeUnboundedBinary_IsDroppedWhenHidden_ButKeptInViewModel。QuickER 版 Repository で入れてから「なし」へ戻す）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    ExcludeUnboundedBinaryColumns = true,
                }
            ),
            GuiRow(
                "gui-gate-remote-dropped-no-db-access",
                "リモート対応は非表示構成では結果オプションへ渡さない（値自体は保持する）（CSharpGenerationDialogViewModelTests.RemoteOptions_AreDroppedWhenHidden_ButKeptInViewModel。QuickER 版 Repository で HTTP 実装を入れてから「なし」へ戻す）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                }
            ),
            // ===== 表示の条件（DB アクセス・対象 DB の選択に追従する） =====
            GuiRow(
                "gui-no-db-access-single-file",
                "DB アクセス「なし」：リモート対応・無制限バイナリ列の除外・同期支援の行は出ない。OK 実行で namespace と出力先が結果へ反映され閉じる（CSharpGenerationDialogViewModelTests.Ok_SetsResult・ShowRemoteContracts_TracksDbAccessSelection・ShowExcludeUnboundedBinary_TracksRepositorySelection の「なし」）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                }
            ),
            GuiRow(
                "gui-visibility-repository-single-dialect",
                "QuickER 版 Repository・SQL Server だけ：リモート対応と無制限バイナリ列の除外の行は出て、同期支援の行は出ない（CSharpGenerationDialogViewModelTests.ShowSyncSupport_FollowsRepositoryAndBothDialects の片方言・ShowRemoteContracts_TracksDbAccessSelection・ShowExcludeUnboundedBinary_TracksRepositorySelection の QuickER 版 Repository）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                }
            ),
            GuiRow(
                "gui-visibility-efcore",
                "EF Core：リモート対応の行は出て、無制限バイナリ列の除外・同期支援の行は出ない。QuickER 版 Repository から EF Core へ選び直した選択が結果へ渡る（CSharpGenerationDialogViewModelTests.ShowRemoteContracts_TracksDbAccessSelection・ShowExcludeUnboundedBinary_TracksRepositorySelection の EF Core・DbAccessRadios_AreExclusive_AndStoredInResult の結果）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateEfCoreRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                }
            ),
            GuiRow(
                "gui-visibility-repository-both-dialects",
                "QuickER 版 Repository・SQL Server と SQLite：同期支援の行が出る（CSharpGenerationDialogViewModelTests.ShowSyncSupport_FollowsRepositoryAndBothDialects の両方言）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                }
            ),
            GuiRow(
                "gui-visibility-no-db-access-both-dialects",
                "DB アクセス「なし」・SQL Server と SQLite：同期支援の行は出ない（CSharpGenerationDialogViewModelTests.ShowSyncSupport_FollowsRepositoryAndBothDialects の QuickER 版 Repository を外した形）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                }
            ),
            // ===== 画面のオプションの写しと保存・復元 =====
            GuiRow(
                "gui-repository-dialects-fixed-order-sqlite-diagram",
                "ToOptions はチェックした対象 DB を固定順で RepositoryDialects に設定する・対象 DB チェックは保存され、次回は保存値が図の方言より優先して復元される（CSharpGenerationDialogViewModelTests.ToOptions_SetsRepositoryDialects_InFixedOrder・TargetDialectChecks_ArePersisted_AndRestoredOverProviderDefault。SQLite の図）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                DialectNames.Sqlite
            ),
            GuiRow(
                "gui-runtime-packages-with-efcore",
                "EF Core 選択中もパッケージ参照モードは操作可能・併用が結果へ反映される（CSharpGenerationDialogViewModelTests.UseRuntimePackages_StaysEnabled_AndCoexistsWithEfCore。チェックを入れてから EF Core を選ぶ）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateEfCoreRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    UseRuntimePackages = true,
                }
            ),
            GuiRow(
                "gui-runtime-packages-with-repository",
                "QuickER 版 Repository 選択ではパッケージ参照モードが結果へ反映される（CSharpGenerationDialogViewModelTests.UseRuntimePackages_IsReflectedInResult_WhenRepositorySelected）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    UseRuntimePackages = true,
                }
            ),
            GuiRow(
                "gui-api-docs-on",
                "API リファレンス出力チェックの ON が結果へ反映され、次回起動時に復元される（CSharpGenerationDialogViewModelTests.GenerateApiDocs_DefaultsOff_AndReflectedInResult の ON・GenerateApiDocs_IsPersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateApiDocs = true,
                }
            ),
            GuiRow(
                "gui-api-docs-japanese",
                "API リファレンスの言語（日本語）が結果へ渡り、設定復元時に復元される（CSharpGenerationDialogViewModelTests.ApiDocsLanguage_RestoresSavedValue(Japanese, Japanese)）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Japanese,
                }
            ),
            GuiRow(
                "gui-api-docs-both",
                "API リファレンスの言語（両方）が結果へ渡り、設定復元時に復元される（CSharpGenerationDialogViewModelTests.ApiDocsLanguage_RestoresSavedValue(Both, Both)・ApiDocsLanguageRadios_SyncWithLanguage の最後の ToOptions）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                }
            ),
            GuiRow(
                "gui-exclude-binary-repository",
                "無制限バイナリ列の除外チェックの ON が ExcludeUnboundedBinaryColumns へ反映され、次回起動時に復元される（CSharpGenerationDialogViewModelTests.ExcludeUnboundedBinary_IsReflectedInResultOptions の ON・ExcludeUnboundedBinary_IsPersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    ExcludeUnboundedBinaryColumns = true,
                }
            ),
            GuiRow(
                "gui-remote-contracts-repository",
                "リモート対応 ON で GenerateRemoteContracts=true が結果へ反映され、次回起動時に復元される（CSharpGenerationDialogViewModelTests.GenerateRemoteContracts_IsReflectedInResultOptions の ON・GenerateRemoteContracts_IsPersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateRemoteContracts = true,
                }
            ),
            GuiRow(
                "gui-remote-services-repository",
                "HTTP クライアント／サーバー実装チェックが次回起動時に復元される（含意によりリモート対応も ON のまま）（CSharpGenerationDialogViewModelTests.GenerateRemoteServices_IsPersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                }
            ),
            GuiRow(
                "gui-layered-directories-remote-services",
                "層別出力と層フォルダが生成オプションへ写像される（層別は分割を含意する）（CSharpGenerationDialogViewModelTests.LayeredOutput_AndDirectories_AreMappedToOptions の ON）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateRemoteContracts = true,
                    GenerateRemoteServices = true,
                    LayeredOutput = true,
                    DomainLayerDirectory = "Acme.Domain/Generated",
                    InfrastructureLayerDirectory = "Acme.Infrastructure",
                    PresentationLayerDirectory = "Acme.App/Generated",
                    ServerLayerDirectory = "Acme.Api",
                }
            ),
            GuiRow(
                "gui-layered-directories-persisted",
                "層別出力と層フォルダが次回起動時に復元される（含意により分割出力も ON）（CSharpGenerationDialogViewModelTests.LayeredOutput_AndDirectories_ArePersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    RepositoryDialects = [DialectNames.SqlServer],
                    LayeredOutput = true,
                    DomainLayerDirectory = "Acme.Domain",
                    InfrastructureLayerDirectory = "Acme.Infrastructure",
                    PresentationLayerDirectory = "Acme.App",
                    ServerLayerDirectory = "Acme.Api",
                }
            ),
            GuiRow(
                "gui-layered-explicit-entity-namespace",
                "層別出力時は手編集欄のみ明示指定で渡り既定欄は導出（null）に任せる（CSharpGenerationDialogViewModelTests.LayeredOutput_NamespaceFields_ArePassedThroughToOptions のオプション）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    RepositoryDialects = [DialectNames.SqlServer],
                    LayeredOutput = true,
                    DomainLayerDirectory = "MyApp.Domain",
                    EntityNamespace = "Custom.Entities",
                }
            ),
            GuiRow(
                "gui-layered-explicit-mapper-namespace",
                "既定のままの名前空間は導出（null）・手編集値は明示指定として生成へ渡る（CSharpGenerationDialogViewModelTests.DefaultNamespacesAndLayerFolders_PersistAsBlank の結果オプション）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    RepositoryDialects = [DialectNames.SqlServer],
                    LayeredOutput = true,
                    MapperNamespace = "Custom.Mappers",
                }
            ),
            GuiRow(
                "gui-split-value-objects-persisted",
                "設定が次回起動時に復元される（ルート名前空間・分割・値オブジェクト・出力先）（CSharpGenerationDialogViewModelTests.Settings_ArePersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    RepositoryDialects = [DialectNames.SqlServer],
                    SplitFilesByCategory = true,
                    GenerateValueObjects = true,
                }
            ),
            GuiRow(
                "gui-efcore-repository-namespace-persisted",
                "EF Core の選択と Repository 契約名前空間が次回起動時に復元される（CSharpGenerationDialogViewModelTests.EfCoreSelection_IsPersistedAndRestored）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    GenerateEfCoreRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer],
                    SplitFilesByCategory = true,
                    RepositoryNamespace = "Acme.App.Persistence",
                }
            ),
            GuiRow(
                "gui-inmemory",
                "インメモリ実装チェックの ON が結果へ反映され復元される（CSharpGenerationDialogViewModelTests.GenerateInMemory_IsReflectedInResult_AndPersisted の ON）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateInMemoryRepositories = true,
                }
            ),
            GuiRow(
                "gui-hidden-attributes-off",
                "UI 非表示の属性系は読込→保存で保持され ToOptions へ反映される（CSharpGenerationDialogViewModelTests.HiddenAttributes_ArePreservedAcrossLoadSave_AndReflectedInOptions。読込は構築時の設定・保存は確定の保存）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.Loaded",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    IncludeDataAnnotations = false,
                    IncludeJsonIgnoreOnParentNavigation = false,
                }
            ),
            GuiRow(
                "gui-code-subdirectory-layered",
                "CodeSubdirectory が前後の空白を除いて生成オプションへ渡り保存・復元される（CSharpGenerationDialogViewModelTests.CodeSubdirectory_MapsToOptions_AndDoesNotMoveNamespaces の写しと復元）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    RepositoryDialects = [DialectNames.SqlServer],
                    LayeredOutput = true,
                    CodeSubdirectory = "Generated",
                }
            ),
            GuiRow(
                "gui-api-docs-subdirectory",
                "ApiDocsSubdirectory が前後の空白を除いて生成オプションへ渡り保存・復元される（CSharpGenerationDialogViewModelTests.ApiDocsSubdirectory_MapsToOptions_AndPersists の写しと復元）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateApiDocs = true,
                    ApiDocsSubdirectory = "docs",
                }
            ),
            GuiRow(
                "gui-api-docs-file-name",
                "ApiDocsFileName が前後の空白を除いて生成オプションへ渡り保存・復元される（CSharpGenerationDialogViewModelTests.ApiDocsFileName_MapsToOptions_AndPersists の写しと復元）",
                new CodeGenerationOptions
                {
                    RootNamespace = "Acme.App",
                    OutputFileName = "Entities.g.cs",
                    RepositoryDialects = [DialectNames.SqlServer],
                    GenerateApiDocs = true,
                    ApiDocsFileName = "Reference",
                }
            ),
        ];

    /// <summary>生成ダイアログの行（図は 1 表・期待は GUI の入口の表明だけ）</summary>
    private static ScenarioRow<CodegenCase> GuiRow(
        string id,
        string description,
        CodeGenerationOptions options,
        string typeDialect = DialectNames.SqlServer
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                SingleEntityDiagram,
                options,
                typeDialect,
                DiagnosticExpectation.Contains(),
                CodegenEntrance.Gui
            )
        );
}
