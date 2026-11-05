using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：列の扱い（計算列・採番列・配列列・ネットワーク型・負のスケール・フォールバック型・値オブジェクトの外部キー統一・
/// 同期の固定長キー・複合キー・重複した外部キー・IncludeGraph・表示名の衝突・リモートサービスと拡張シムと分割ランタイムのファイル構成）で
/// 判定していたテスト
/// </summary>
/// <remarks>
/// 元は <c>ComputedColumnGenerationTests</c> / <c>IdentityColumnGenerationTests</c> / <c>PostgreSqlArrayColumnGenerationTests</c> /
/// <c>PostgreSqlNetworkTypeGenerationTests</c> / <c>NegativeScaleColumnGenerationTests</c> / <c>FallbackTypeGenerationTests</c> /
/// <c>ValueObjectForeignKeyUnificationTests</c> / <c>SyncFixedLengthKeyWarningTests</c> / <c>RepositorySingleKeyWarningTests</c> /
/// <c>NavigationNameCollisionTests</c> / <c>IncludeGraphGenerationTests</c> / <c>UniquenessGenerationTests</c> /
/// <c>DisplayNameGenerationTests</c> / <c>RemoteServiceGenerationTests</c> / <c>ExtensionShimGenerationTests</c> /
/// <c>SplitRuntimeSymmetryTests</c>。図の方言（型解決）は、元のテストが SQL Server のマッパを使っていたものは SQL Server、
/// PostgreSQL のマッパの辞書を渡していたものは PostgreSQL（direct の入口が図の方言のプロバイダで同じ辞書を作る）。
/// </remarks>
internal static partial class CodegenCases
{
    private static ScenarioRow<CodegenCase> ColumnsRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        DiagnosticExpectation diagnostics,
        IReadOnlyList<FileRule>? fileRules = null,
        bool compile = false,
        string typeDialect = DialectNames.SqlServer
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                diagnostics,
                FileRules: fileRules,
                Compile: compile
            )
        );

    /// <summary>生成が成り立ち、.g.cs が 1 つ以上ある（成功の期待の足場。エラーなら段の食い違いで赤になる）</summary>
    private static IReadOnlyList<FileRule> ColumnsGenerated =>
        [new FileRule(".g.cs", NonEmpty: true, AnyDirectory: true)];

    /// <summary>出力の全ファイルの名前（出力の順）と数を見る（接尾辞なし＝全ファイルが対象）</summary>
    private static IReadOnlyList<FileRule> ColumnsAllFiles(params string[] names) =>
        [new FileRule(string.Empty, Count: names.Length, Names: names, Ordered: true)];

    private static IEnumerable<ScenarioRow<CodegenCase>> ColumnRows() =>
        [
            // ===== 計算列 =====
            ColumnsRow(
                "diag-computed-column-info-names-excluded-column",
                "計算列: 除外した列を Info 診断が名指しする。主キーでない計算列は生成時エラーにならない（ComputedColumnGenerationTests.Generate_ComputedColumn_ReportsInfoDiagnostic・Generate_NonPrimaryKeyComputedColumn_Succeeds）",
                () => Columns_ComputedDiagram(computed: true),
                Columns_RepositoryOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_ComputedColumns),
                        "items.total"
                    ),
                    1
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-computed-column-none-no-info",
                "計算列なし: 除外の Info 診断は出ない（ComputedColumnGenerationTests.Generate_NoComputedColumn_DoesNotReportInfoDiagnostic）",
                () => Columns_ComputedDiagram(computed: false),
                Columns_RepositoryOptions(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_ComputedColumns),
                            "items.total"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-computed-not-null-multi-target-warning",
                "マルチターゲット: NOT NULL の計算列を Warning が名指しする（ComputedColumnGenerationTests.Generate_MultiTarget_NotNullComputedColumn_Warns）",
                Columns_NotNullComputedDiagram,
                Columns_DialectOptions([DialectNames.SqlServer, DialectNames.Sqlite]),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_NotNullComputedColumns),
                        "temporal_items.period_start"
                    )
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-computed-not-null-sync-support-warning",
                "同期支援: NOT NULL の計算列を Warning が名指しする（ComputedColumnGenerationTests.Generate_SyncSupport_NotNullComputedColumn_Warns）",
                Columns_NotNullComputedDiagram,
                Columns_DialectOptions(
                    [DialectNames.SqlServer, DialectNames.Sqlite],
                    syncSupport: true
                ),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_NotNullComputedColumns),
                        "temporal_items.period_start"
                    )
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-computed-not-null-single-dialect-no-warning",
                "単一方言: NOT NULL の計算列でも Warning は出ない（ComputedColumnGenerationTests.Generate_SingleDialect_NotNullComputedColumn_DoesNotWarn）",
                Columns_NotNullComputedDiagram,
                Columns_DialectOptions([DialectNames.SqlServer]),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_NotNullComputedColumns),
                            "temporal_items.period_start"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-computed-nullable-multi-target-no-warning",
                "マルチターゲット: NULL 許容の計算列だけなら Warning は出ない（ComputedColumnGenerationTests.Generate_MultiTarget_NullableComputedColumn_DoesNotWarn）",
                () => Columns_ComputedDiagram(computed: true),
                Columns_DialectOptions([DialectNames.SqlServer, DialectNames.Sqlite]),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_NotNullComputedColumns),
                            "items.total"
                        )
                    ),
                ColumnsGenerated
            ),
            // ===== 採番列 =====
            ColumnsRow(
                "diag-identity-column-info-names-excluded-column",
                "採番列: 除外した列を Info 診断が名指しし、主キーが採番列でなければ Warning は出ない（IdentityColumnGenerationTests.Generate_IdentityColumn_ReportsInfoDiagnostic・Generate_NonIdentityPrimaryKey_DoesNotWarn）",
                () => Columns_IdentityDiagram(identity: true),
                Columns_RepositoryOptions(),
                DiagnosticExpectation
                    .CountOf(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_IdentityColumns),
                            "items.seq"
                        ),
                        1
                    )
                    .Without(
                        ExpectedDiagnostic.Warning(
                            nameof(Strings.CodeGen_Warning_IdentityPrimaryKeyColumn),
                            "items",
                            "item_id"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-column-none-no-info",
                "採番列なし: 除外の Info 診断は出ない（IdentityColumnGenerationTests.Generate_NoIdentityColumn_DoesNotReportInfoDiagnostic）",
                () => Columns_IdentityDiagram(identity: false),
                Columns_RepositoryOptions(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_IdentityColumns),
                            "items.seq"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-primary-key-warns-without-excluding",
                "主キーの採番列は印を付けず、生成を止めずに Warning で名指しし、除外の Info には載せない（IdentityColumnGenerationTests.Generate_IdentityPrimaryKey_WarnsWithoutMarking の構造の表明）",
                () => Columns_IdentityDiagram(identity: false, identityPrimaryKey: true),
                Columns_RepositoryOptions(),
                DiagnosticExpectation
                    .Contains(
                        ExpectedDiagnostic.Warning(
                            nameof(Strings.CodeGen_Warning_IdentityPrimaryKeyColumn),
                            "items",
                            "item_id"
                        )
                    )
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_IdentityColumns),
                            "items.item_id"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-primary-key-without-repository-contract-no-warning",
                "Repository 契約を生成しない構成では、主キーの採番列の Warning は出ない（IdentityColumnGenerationTests.Generate_IdentityPrimaryKey_WithoutRepositoryContract_DoesNotWarn）",
                () => Columns_IdentityDiagram(identity: false, identityPrimaryKey: true),
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Warning(
                            nameof(Strings.CodeGen_Warning_IdentityPrimaryKeyColumn),
                            "items",
                            "item_id"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-not-null-multi-target-sqlite-warning",
                "採番列を持てない方言（SQLite）を含むマルチターゲットでは、NOT NULL の採番列を Warning で名指しする（IdentityColumnGenerationTests.Generate_MultiTargetWithSqlite_NotNullIdentityColumn_Warns）",
                () => Columns_IdentityDiagram(identity: true),
                Columns_DialectOptions(
                    [DialectNames.SqlServer, DialectNames.Sqlite],
                    rootNamespace: "Sample.Domain"
                ),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_NotNullIdentityColumns),
                        "(items.seq)"
                    )
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-not-null-single-sqlite-warning",
                "単一方言の SQLite でも、NOT NULL の採番列を Warning で名指しする（IdentityColumnGenerationTests.Generate_SingleSqlite_NotNullIdentityColumn_Warns）",
                () => Columns_IdentityDiagram(identity: true),
                Columns_DialectOptions([DialectNames.Sqlite]),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_NotNullIdentityColumns),
                        "(items.seq)"
                    )
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-mirror-warning-absent-sqlserver-only",
                "持てる方言だけなら、ミラーの Warning は出ない（IdentityColumnGenerationTests.Generate_MirrorWarning_IsNotEmittedWithoutMirrorOrForNullableColumn の前半）",
                () => Columns_IdentityDiagram(identity: true),
                Columns_DialectOptions([DialectNames.SqlServer]),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_NotNullIdentityColumns),
                            "(items.seq)"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-identity-mirror-warning-absent-nullable-column",
                "NULL 許容の採番列では、マルチターゲットでもミラーの Warning は出ない（IdentityColumnGenerationTests.Generate_MirrorWarning_IsNotEmittedWithoutMirrorOrForNullableColumn の後半）",
                () => Columns_IdentityDiagram(identity: true, nullableSeq: true),
                Columns_DialectOptions([DialectNames.SqlServer, DialectNames.Sqlite]),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_NotNullIdentityColumns),
                            "(items.seq)"
                        )
                    ),
                ColumnsGenerated
            ),
            // ===== PostgreSQL の配列列 =====
            ColumnsRow(
                "diag-postgresql-array-columns-info-names-columns",
                "EditModel か VO を生成するとき、外した配列列を Info で名指しする（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_AreReportedAsInfo）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_ArrayColumnsNotEditable),
                        "articles.tags",
                        "articles.labels (varchar(20)[])",
                        "articles.codes (bigint[])"
                    ),
                    1
                ),
                ColumnsGenerated,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "diag-postgresql-array-columns-silent-without-editmodels-and-vo",
                "EditModel も VO も生成しないなら配列列の Info は出ない（PostgreSqlArrayColumnGenerationTests.Generate_WithoutEditModelsOrValueObjects_DoesNotReportArrayColumns）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: false),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_ArrayColumnsNotEditable),
                            "articles.tags"
                        )
                    ),
                ColumnsGenerated,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-columns-plain",
                "配列列を含む図は EditModel・VO なしでもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_Compiles(false, false)）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: false),
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-columns-editmodels",
                "配列列を含む図は EditModel ありでもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_Compiles(true, false)）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false),
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-columns-vo",
                "配列列を含む図は VO ありでもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumns_Compiles(false, true)）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: true),
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-columns-inmemory",
                "配列列はインメモリ Repository と併せてもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnsWithInMemory_Compiles(false)。元のメソッドのコンパイルの表明を行へ移し、本文の表明は Generate_ArrayColumnsWithInMemory_CopiesAndSeedsArrays に残した）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false) with
                {
                    GenerateInMemoryRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-columns-inmemory-vo",
                "配列列はインメモリ Repository と VO を併せてもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnsWithInMemory_Compiles(true)。元のメソッドのコンパイルの表明を行へ移し、本文の表明は Generate_ArrayColumnsWithInMemory_CopiesAndSeedsArrays に残した）",
                PostgreSqlArrayDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: true) with
                {
                    GenerateInMemoryRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-column-unique-constraint",
                "配列列を含む UNIQUE 制約があってもコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnInUniqueConstraint_Compiles(false)）",
                Columns_PostgreSqlArrayUniqueDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false) with
                {
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = false,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "compile-postgresql-array-column-unique-constraint-vo",
                "配列列を含む UNIQUE 制約があっても VO ありでコンパイルできる（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnInUniqueConstraint_Compiles(true)）",
                Columns_PostgreSqlArrayUniqueDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: true) with
                {
                    GenerateRepositories = true,
                    GenerateEfCoreRepositories = false,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "diag-postgresql-array-column-unique-constraint-info",
                "配列列を含む UNIQUE 制約は Info で名指しされ、名前の無い制約は合成名で出る（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnInUniqueConstraint_ReportsInfoDiagnostic）",
                Columns_PostgreSqlArrayUniqueDiagram,
                Columns_PostgreSqlArrayOptions(editModels: true, valueObjects: false),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_ArrayUniqueConstraintsNotValidated),
                        "articles.uq_articles_tags (tags)",
                        "(name, tags)"
                    ),
                    1
                ),
                ColumnsGenerated,
                typeDialect: DialectNames.PostgreSql
            ),
            ColumnsRow(
                "diag-postgresql-array-column-unique-constraint-silent-without-editmodels",
                "EditModel を生成しない構成では UNIQUE 制約を名指ししない（PostgreSqlArrayColumnGenerationTests.Generate_ArrayColumnInUniqueConstraint_IsSilentWithoutEditModels）",
                Columns_PostgreSqlArrayUniqueDiagram,
                Columns_PostgreSqlArrayOptions(editModels: false, valueObjects: false),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_ArrayUniqueConstraintsNotValidated),
                            "uq_articles_tags"
                        )
                    ),
                ColumnsGenerated,
                typeDialect: DialectNames.PostgreSql
            ),
            // ===== PostgreSQL のネットワーク型 =====
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-plain",
                editModels: false,
                valueObjects: false,
                inMemory: false
            ),
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-editmodels",
                editModels: true,
                valueObjects: false,
                inMemory: false
            ),
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-vo",
                editModels: false,
                valueObjects: true,
                inMemory: false
            ),
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-editmodels-vo",
                editModels: true,
                valueObjects: true,
                inMemory: false
            ),
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-editmodels-inmemory",
                editModels: true,
                valueObjects: false,
                inMemory: true
            ),
            Columns_NetworkCompileRow(
                "compile-postgresql-network-types-editmodels-vo-inmemory",
                editModels: true,
                valueObjects: true,
                inMemory: true
            ),
            ColumnsRow(
                "diag-postgresql-inet-columns-info",
                "EditModel に載らない inet の列を、配列列とは別の Info で名指しする（cidr・interval は載せない）（PostgreSqlNetworkTypeGenerationTests.Generate_InetColumns_ReportedAsInfo）",
                Columns_NetworkDiagram,
                Columns_NetworkOptions(editModels: true, valueObjects: false, inMemory: false),
                DiagnosticExpectation
                    .CountOf(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_TextInputUnsupportedColumns),
                            "hosts.addr (inet)",
                            "hosts.backup_addr (inet)"
                        ),
                        1
                    )
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_TextInputUnsupportedColumns),
                            "hosts.net"
                        ),
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_TextInputUnsupportedColumns),
                            "hosts.lease"
                        )
                    ),
                ColumnsGenerated,
                typeDialect: DialectNames.PostgreSql
            ),
            // ===== 負のスケール =====
            Columns_NegativeScaleRow(
                "diag-negative-scale-info-with-value-objects",
                "負のスケール: Info 診断が「テーブル.列 (元の型表記)」で名指しする（NegativeScaleColumnGenerationTests.Generate_NegativeScaleColumn_ReportsInfoDiagnostic）",
                "numeric(10,-2)",
                valueObjects: true,
                reported: true
            ),
            Columns_NegativeScaleRow(
                "diag-negative-scale-info-without-value-objects",
                "負のスケール: 値オブジェクトを生成しない構成でも名指しする（NegativeScaleColumnGenerationTests.Generate_NegativeScaleColumn_ReportsEvenWithoutValueObjects）",
                "numeric(10,-2)",
                valueObjects: false,
                reported: true
            ),
            Columns_NegativeScaleRow(
                "diag-negative-scale-positive-scale-not-reported",
                "負のスケールでない列（numeric(10,2)）は名指ししない（NegativeScaleColumnGenerationTests.Generate_NonNegativeScaleColumn_DoesNotReport(\"numeric(10,2)\")）",
                "numeric(10,2)",
                valueObjects: true,
                reported: false
            ),
            Columns_NegativeScaleRow(
                "diag-negative-scale-zero-scale-not-reported",
                "負のスケールでない列（numeric(10)）は名指ししない（NegativeScaleColumnGenerationTests.Generate_NonNegativeScaleColumn_DoesNotReport(\"numeric(10)\")）",
                "numeric(10)",
                valueObjects: true,
                reported: false
            ),
            Columns_NegativeScaleRow(
                "diag-negative-scale-int-not-reported",
                "負のスケールでない列（int）は名指ししない（NegativeScaleColumnGenerationTests.Generate_NonNegativeScaleColumn_DoesNotReport(\"int\")）",
                "int",
                valueObjects: true,
                reported: false
            ),
            // ===== フォールバック型 =====
            ColumnsRow(
                "diag-fallback-type-info-names-column",
                "フォールバック型: Info 診断が「テーブル.列 (元の型表記)」で名指しする（FallbackTypeGenerationTests.Generate_FallbackTypeColumn_ReportsInfoDiagnosticWithTableColumnAndDataType）",
                () => Columns_FallbackDiagram(includeUnknownType: true),
                Columns_FallbackOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_FallbackTypeColumns),
                        "items.location (geometry)"
                    ),
                    1
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-fallback-type-none-no-info",
                "フォールバック型なし: Info 診断は出ない（FallbackTypeGenerationTests.Generate_NoFallbackTypeColumn_DoesNotReportInfoDiagnostic）",
                () => Columns_FallbackDiagram(includeUnknownType: false),
                Columns_FallbackOptions(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_FallbackTypeColumns),
                            "items.location"
                        )
                    ),
                ColumnsGenerated
            ),
            // ===== 値オブジェクトの外部キー統一 =====
            ColumnsRow(
                "diag-vo-foreign-key-unified-info",
                "統一が起きた列は Info 診断で通知される（ValueObjectForeignKeyUnificationTests.統一はInfo診断で通知される）",
                Columns_VoMismatchedNameDiagram,
                Columns_VoOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_ValueObjectForeignKeyUnified),
                        "orders.ship_customer_id",
                        "CustomerIdValue"
                    ),
                    1
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-vo-foreign-key-matching-names-no-info",
                "列名が一致する図では統一の Info 診断を出さない（ValueObjectForeignKeyUnificationTests.統一が起きない図では診断を出さない）",
                Columns_VoMatchingNameDiagram,
                Columns_VoOptions(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_ValueObjectForeignKeyUnified),
                            "customer_id"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-vo-foreign-key-value-objects-off-no-info",
                "VO 無効の図では統一が働かず、診断も出ない（ValueObjectForeignKeyUnificationTests.VO無効では統一しない の診断の表明）",
                Columns_VoMismatchedNameDiagram,
                Columns_VoOptions() with
                {
                    GenerateValueObjects = false,
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_ValueObjectForeignKeyUnified),
                            "ship_customer_id"
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-vo-foreign-key-conflicting-parents-error",
                "退化ケース: 型の違う親を複数参照する子列は生成時エラー（ValueObjectForeignKeyUnificationTests.型の違う親を複数参照する子列はエラー）",
                Columns_VoConflictingParentsDiagram,
                Columns_VoOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Error_ValueObjectForeignKeyTypeConflict),
                        "items.owner_id"
                    )
                )
            ),
            // ===== 同期の固定長の文字列キー =====
            Columns_SyncFixedLengthRow(
                "diag-sync-fixed-length-key-warning-nchar",
                "同期の対象の表の主キーが固定長の文字列（nchar(10)）なら、生成時の Warning で表と列を名指しする（SyncFixedLengthKeyWarningTests.FixedLengthStringPrimaryKey_IsWarned(\"nchar(10)\")）",
                () => Columns_SyncMarkerKeyTyped("nchar(10)"),
                SyncFixtureDefinition.Options,
                warned: true
            ),
            Columns_SyncFixedLengthRow(
                "diag-sync-fixed-length-key-warning-char",
                "同期の対象の表の主キーが固定長の文字列（char(10)）なら、生成時の Warning で表と列を名指しする（SyncFixedLengthKeyWarningTests.FixedLengthStringPrimaryKey_IsWarned(\"char(10)\")）",
                () => Columns_SyncMarkerKeyTyped("char(10)"),
                SyncFixtureDefinition.Options,
                warned: true
            ),
            Columns_SyncFixedLengthRow(
                "diag-sync-fixed-length-key-variable-nvarchar-no-warning",
                "可変長の文字列の主キー（nvarchar(10)）では、固定長の主キーの Warning を出さない（SyncFixedLengthKeyWarningTests.VariableLengthStringPrimaryKey_IsNotWarned(\"nvarchar(10)\")）",
                () => Columns_SyncMarkerKeyTyped("nvarchar(10)"),
                SyncFixtureDefinition.Options,
                warned: false
            ),
            Columns_SyncFixedLengthRow(
                "diag-sync-fixed-length-key-variable-varchar-no-warning",
                "可変長の文字列の主キー（varchar(10)）では、固定長の主キーの Warning を出さない（SyncFixedLengthKeyWarningTests.VariableLengthStringPrimaryKey_IsNotWarned(\"varchar(10)\")）",
                () => Columns_SyncMarkerKeyTyped("varchar(10)"),
                SyncFixtureDefinition.Options,
                warned: false
            ),
            Columns_SyncFixedLengthRow(
                "diag-sync-fixed-length-key-without-sync-support-no-warning",
                "同期を生成しない構成では、固定長の文字列の主キーでも Warning を出さない（SyncFixedLengthKeyWarningTests.WithoutSyncSupport_IsNotWarned）",
                () => Columns_SyncMarkerKeyTyped("nchar(10)"),
                SyncFixtureDefinition.Options with
                {
                    GenerateSyncSupport = false,
                    GenerateRemoteServices = false,
                },
                warned: false
            ),
            ColumnsRow(
                "diag-sync-fixed-length-non-key-column-no-warning",
                "主キーでない固定長の文字列の列では、Warning を出さない（SyncFixedLengthKeyWarningTests.FixedLengthNonKeyColumn_IsNotWarned）",
                () =>
                {
                    var diagram = SyncFixtureDefinition.Build();
                    diagram
                        .Entities.Single(entity => entity.TableName == "sync_markers")
                        .Columns.Single(column => column.Name == "code")
                        .DataType = "nchar(10)";
                    return diagram;
                },
                SyncFixtureDefinition.Options,
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.WarningMentioning(
                            nameof(Strings.CodeGen_Warning_SyncFixedLengthStringPrimaryKeys),
                            "sync_markers.code"
                        )
                    ),
                ColumnsGenerated
            ),
            // ===== 複合主キー・主キーなしの表 =====
            ColumnsRow(
                "diag-repository-single-key-composite-warning",
                "複合主キーの表は、Repository を作らずに警告で名指しする（RepositorySingleKeyWarningTests.Generate_NonSingleKeyTable_SkipsRepositoryWithWarning(true)）",
                () =>
                    Columns_SingleKeyDiagram(
                        Columns_SingleKeyTable("order_lines", ("order_id", true), ("line_no", true))
                    ),
                Columns_RepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RepositorySingleKeyOnly),
                        "order_lines"
                    )
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-repository-single-key-keyless-warning",
                "主キーなしの表は、Repository を作らずに警告で名指しする（RepositorySingleKeyWarningTests.Generate_NonSingleKeyTable_SkipsRepositoryWithWarning(false)）",
                () =>
                    Columns_SingleKeyDiagram(
                        Columns_SingleKeyTable("audit_logs", ("message", false))
                    ),
                Columns_RepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Warning(
                        nameof(Strings.CodeGen_Warning_RepositorySingleKeyOnly),
                        "audit_logs"
                    )
                ),
                ColumnsGenerated
            ),
            // ===== 同じ列ペアの外部キーの重複 =====
            Columns_DuplicateForeignKeyRow(
                "diag-duplicate-foreign-key-explicit-names",
                "同じ親・子・列ペアの外部キーが 2 本ある図は、両方の制約名と列を名指しする専用のエラーで止まる（NavigationNameCollisionTests.DuplicateForeignKeys_AreRefusedNamingBoth の制約名を指定した行）",
                "FK_first",
                "FK_second",
                "FK_first",
                "FK_second"
            ),
            Columns_DuplicateForeignKeyRow(
                "diag-duplicate-foreign-key-default-names",
                "同じ親・子・列ペアの外部キーが 2 本ある図は、既定の制約名でも両方を名指しする専用のエラーで止まる（NavigationNameCollisionTests.DuplicateForeignKeys_AreRefusedNamingBoth の制約名を省略した行）",
                null,
                null,
                "FK_profiles_customers",
                "FK_profiles_customers_customer_id"
            ),
            // ===== IncludeGraph =====
            ColumnsRow(
                "diag-include-graph-self-reference-info",
                "自己参照ナビは展開せず Info 診断で列挙する（IncludeGraphGenerationTests.SelfReference_IsSkippedAndReported の診断の表明）",
                Columns_IncludeGraphSelfReferenceDiagram,
                Columns_IncludeGraphOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_IncludeGraphSkippedNavigations),
                        "NodeEntity.Nodes"
                    ),
                    1
                ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-include-graph-no-skipped-edges-no-info",
                "スキップが無い図では IncludeGraph の Info 診断を出さない（IncludeGraphGenerationTests.NoSkippedEdges_EmitsNoInfo）",
                Columns_IncludeGraphChainDiagram,
                Columns_IncludeGraphOptions(),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Info(
                            nameof(Strings.CodeGen_Info_IncludeGraphSkippedNavigations)
                        )
                    ),
                ColumnsGenerated
            ),
            ColumnsRow(
                "diag-include-graph-mutual-reference-info",
                "相互参照は再訪する辺だけを落とし、落とした辺は両方向ぶん Info 診断へ載る（IncludeGraphGenerationTests.MutualReference_SkipsOnlyTheRevisitingEdge の診断の表明）",
                Columns_IncludeGraphMutualReferenceDiagram,
                Columns_IncludeGraphOptions(),
                // 元は「Info 全体で 1 件」なので、種類で絞った件数ではなく診断全体の完全一致で書く（元より強い＝警告・エラーも無いこと）
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_IncludeGraphSkippedNavigations),
                        "BetaEntity.Alphas",
                        "AlphaEntity.Betas"
                    ),
                ]),
                ColumnsGenerated
            ),
            // ===== 重複事前チェック =====
            ColumnsRow(
                "diag-query-named-check-uniqueness-reserved-method-name",
                "名前付きクエリの CheckUniqueness は予約名として拒否される（UniquenessGenerationTests.Generate_QueryNamedCheckUniqueness_IsRejected）",
                Columns_UniquenessOrderWithCheckUniquenessQuery,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    GenerateEditModels = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_ReservedMethodName),
                        "CheckUniquenessAsync"
                    )
                )
            ),
            // ===== 表示名の衝突 =====
            ColumnsRow(
                "diag-entity-display-name-collision-warning",
                "display_name 列を持つエンティティは DefaultDisplayName の override を省略して Warning で名指しし、生成は完走する（DisplayNameGenerationTests.Generate_EntityWithDisplayNameColumn_OmitsMembersWarnsAndKeepsOtherEntities の診断の表明）",
                Columns_DisplayNameCollisionDiagram,
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_EntityDisplayNameCollision),
                        "LabelEntity"
                    )
                ),
                ColumnsGenerated
            ),
            // ===== リモートサービスのファイル構成 =====
            Columns_RemoteOutputNameRow(
                "files-remote-service-output-name-shop-cs",
                "出力ファイル名 Shop.cs（.g.cs で終わらない）の正規化は、サーバー実装名と API リファレンスの名前にも効く（RemoteServiceGenerationTests.Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"Shop.cs\", false)）",
                "Shop.cs",
                "Shop.g.cs",
                multiDialect: false
            ),
            Columns_RemoteOutputNameRow(
                "files-remote-service-output-name-shop",
                "出力ファイル名 Shop（拡張子なし）の正規化は、サーバー実装名と API リファレンスの名前にも効く（RemoteServiceGenerationTests.Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"Shop\", false)）",
                "Shop",
                "Shop.g.cs",
                multiDialect: false
            ),
            Columns_RemoteOutputNameRow(
                "files-remote-service-output-name-padded-shop-cs",
                "出力ファイル名 前後に空白のある Shop.cs の正規化は、サーバー実装名と API リファレンスの名前にも効く（RemoteServiceGenerationTests.Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"  Shop.cs  \", false)）",
                "  Shop.cs  ",
                "Shop.g.cs",
                multiDialect: false
            ),
            Columns_RemoteOutputNameRow(
                "files-remote-service-output-name-upper-case-suffix",
                "出力ファイル名 Shop.G.CS（大文字小文字違いの .g.cs）は書き換えず、サーバー実装名と API リファレンスの名前にも効く（RemoteServiceGenerationTests.Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"Shop.G.CS\", false)）",
                "Shop.G.CS",
                "Shop.G.CS",
                multiDialect: false
            ),
            Columns_RemoteOutputNameRow(
                "files-remote-service-output-name-shop-cs-multi-dialect",
                "出力ファイル名 Shop.cs の正規化は、マルチ方言のレイアウトでもサーバー実装名と API リファレンスの名前に効く（RemoteServiceGenerationTests.Generate_NonGeneratedSuffixOutputFileName_ServerFileNameFollowsNormalizedMain(\"Shop.cs\", true)）",
                "Shop.cs",
                "Shop.g.cs",
                multiDialect: true
            ),
            ColumnsRow(
                "files-remote-service-main-and-server-files",
                "ON: 本体＋{ベース名}.RemoteServer.g.cs の 2 ファイルが出力される（RemoteServiceGenerationTests.Generate_RemoteServices_EmitsClientInMainAndServerInSeparateFile のファイル名の表明）",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    OutputFileName = "Shop.g.cs",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(),
                ColumnsAllFiles("Shop.g.cs", "Shop.RemoteServer.g.cs")
            ),
            ColumnsRow(
                "files-remote-service-off-single-file",
                "OFF（既定）: 出力が 1 ファイルのまま（RemoteServiceGenerationTests.Generate_Default_DoesNotEmitRemoteServices のファイル数の表明）",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(string.Empty, Count: 1)]
            ),
            ColumnsRow(
                "files-remote-service-efcore-only-two-files",
                "EF Core 単独でもクライアント／サーバーの 2 ファイルが出力される（RemoteServiceGenerationTests.Generate_RemoteServicesWithEfCoreOnly_Works のファイル数の表明）",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(string.Empty, Count: 2)]
            ),
            ColumnsRow(
                "files-remote-service-non-split-two-files",
                "非分割時は HTTP クライアントが本体ファイルへ同居し、出力は本体とサーバーの 2 ファイル（RemoteServiceGenerationTests.Generate_NonSplit_KeepsHttpClientInMainFile のファイル数の表明）",
                Columns_RemoteServiceDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                },
                DiagnosticExpectation.Contains(),
                [new FileRule(string.Empty, Count: 2)]
            ),
            // ===== 拡張シムのファイル構成 =====
            ColumnsRow(
                "files-extension-shim-package-mode-no-core-runtime-file",
                "パッケージ参照モード: 固定 infra のファイル（Runtime.g.cs）は出ない（ExtensionShimGenerationTests.PackageMode_ShouldStillEmitExtensionSurfaces のファイル名の表明）",
                Columns_ExtensionShimDiagram,
                Columns_ExtensionShimOptions() with
                {
                    SplitFilesByCategory = true,
                    UseRuntimePackages = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Runtime.g.cs", Count: 0),
                    new FileRule("Entities.g.cs", Count: 1),
                    new FileRule("EditModels.g.cs", Count: 1),
                    new FileRule("ValueObjects.g.cs", Count: 1),
                ]
            ),
            ColumnsRow(
                "files-extension-shim-layered-entity-valueobject-editmodel-layers",
                "層別出力: Entity/VO はドメイン層・EditModel はプレゼンテーション層へ出る（ExtensionShimGenerationTests.Layered_ShouldPlaceExtensionSurfacesInDomainAndPresentation の置き場の表明）",
                Columns_ExtensionShimDiagram,
                Columns_ExtensionShimOptions() with
                {
                    LayeredOutput = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Entities.g.cs", Count: 1, Directory: "Domain"),
                    new FileRule("ValueObjects.g.cs", Count: 1, Directory: "Domain"),
                    new FileRule("EditModels.g.cs", Count: 1, Directory: "Presentation"),
                ]
            ),
            ColumnsRow(
                "files-extension-shim-package-mode-repository-no-core-runtime-file",
                "パッケージ参照モード: Repository / Mapper の拡張面も出て、固定 infra のファイル（Runtime.g.cs）は出ない（ExtensionShimGenerationTests.PackageMode_ShouldStillEmitRepositoryAndMapperShims のファイル名の表明）",
                Columns_ExtensionShimDiagram,
                Columns_ExtensionShimBackendOptions() with
                {
                    SplitFilesByCategory = true,
                    UseRuntimePackages = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Runtime.g.cs", Count: 0),
                    new FileRule("Repositories.g.cs", Count: 1),
                    new FileRule("Repositories.SqlServer.g.cs", Count: 1),
                    new FileRule("Repositories.EntityFrameworkCore.g.cs", Count: 1),
                    new FileRule("Repositories.InMemory.g.cs", Count: 1),
                    new FileRule("Repositories.Http.g.cs", Count: 1),
                    new FileRule("Mappers.g.cs", Count: 1),
                ]
            ),
            ColumnsRow(
                "files-extension-shim-layered-repository-mapper-layers",
                "層別出力: 契約シムはドメイン層・実装シムはインフラ層・Mapper シムはプレゼンテーション層（ExtensionShimGenerationTests.Layered_ShouldPlaceRepositoryAndMapperShimsByLayer の置き場の表明）",
                Columns_ExtensionShimDiagram,
                Columns_ExtensionShimBackendOptions() with
                {
                    LayeredOutput = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Repositories.g.cs", Count: 1, Directory: "Domain"),
                    new FileRule(
                        "Repositories.SqlServer.g.cs",
                        Count: 1,
                        Directory: "Infrastructure"
                    ),
                    new FileRule("Repositories.Http.g.cs", Count: 1, Directory: "Infrastructure"),
                    new FileRule("Mappers.g.cs", Count: 1, Directory: "Presentation"),
                ]
            ),
            // ===== 分割ランタイムのファイル構成 =====
            ColumnsRow(
                "files-split-sync-fixed-engine-and-per-entity-files",
                "同期支援の分割生成でも、固定 infra（Runtime.Sync.g.cs）とスキーマ依存物（Repositories.Sync.g.cs）が別ファイルで出る（SplitRuntimeSymmetryTests.SplitSyncFiles_ShouldSeparateFixedEngineFromPerEntityCode のファイル名の表明）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule("Runtime.Sync.g.cs", Count: 1, AnyDirectory: true),
                    new FileRule("Repositories.Sync.g.cs", Count: 1, AnyDirectory: true),
                ]
            ),
            ColumnsRow(
                "files-split-runtime-packages-no-fixed-runtime-file",
                "パッケージ参照モードの分割生成では固定 infra ファイル（Runtime.*）が 1 本も出力されず、スキーマ依存の Repositories 系だけが出る（SplitRuntimeSymmetryTests.SplitUseRuntimePackages_ShouldEmitNoFixedRuntimeFile）",
                Columns_SplitRuntimeDiagram,
                Columns_SplitRuntimeMultiDialectOptions() with
                {
                    UseRuntimePackages = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(".g.cs", Prefix: "Runtime.", Count: 0, AnyDirectory: true),
                    new FileRule("Repositories.g.cs", Count: 1, AnyDirectory: true),
                    new FileRule("Repositories.SqlServer.g.cs", Count: 1, AnyDirectory: true),
                ]
            ),
        ];

    // ---- 行の組み立て ----

    /// <summary>PostgreSQL のネットワーク型の図のコンパイルの行</summary>
    private static ScenarioRow<CodegenCase> Columns_NetworkCompileRow(
        string id,
        bool editModels,
        bool valueObjects,
        bool inMemory
    ) =>
        ColumnsRow(
            id,
            $"inet・cidr・interval を含む図は EditModels={editModels}, ValueObjects={valueObjects}, InMemory={inMemory} でもコンパイルできる（PostgreSqlNetworkTypeGenerationTests.Generate_NetworkTypes_Compiles({editModels}, {valueObjects}, {inMemory})）",
            Columns_NetworkDiagram,
            Columns_NetworkOptions(editModels, valueObjects, inMemory),
            DiagnosticExpectation.Contains(),
            compile: true,
            typeDialect: DialectNames.PostgreSql
        );

    /// <summary>負のスケールの列の Info の行（<paramref name="reported"/> が false なら出ないことを見る）</summary>
    private static ScenarioRow<CodegenCase> Columns_NegativeScaleRow(
        string id,
        string description,
        string amountType,
        bool valueObjects,
        bool reported
    )
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            GenerateValueObjects = valueObjects,
        };
        var info = ExpectedDiagnostic.InfoMentioning(
            nameof(Strings.CodeGen_Info_NegativeScaleColumns),
            reported ? "orders.amount (numeric(10,-2))" : "orders.amount"
        );

        return ColumnsRow(
            id,
            description,
            () => Columns_NegativeScaleDiagram(amountType),
            options,
            reported
                ? DiagnosticExpectation.CountOf(info, 1)
                : DiagnosticExpectation.Contains().Without(info),
            ColumnsGenerated
        );
    }

    /// <summary>同期の固定長の文字列キーの Warning の行（<paramref name="warned"/> が false なら出ないことを見る）</summary>
    private static ScenarioRow<CodegenCase> Columns_SyncFixedLengthRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        bool warned
    )
    {
        var warning = ExpectedDiagnostic.WarningMentioning(
            nameof(Strings.CodeGen_Warning_SyncFixedLengthStringPrimaryKeys),
            "sync_markers.marker_id"
        );

        return ColumnsRow(
            id,
            description,
            diagram,
            options,
            warned
                ? DiagnosticExpectation.CountOf(warning, 1)
                : DiagnosticExpectation.Contains().Without(warning),
            ColumnsGenerated
        );
    }

    /// <summary>同じ列ペアの外部キーの重複エラーの行（両方の制約名と列を名指しする）</summary>
    private static ScenarioRow<CodegenCase> Columns_DuplicateForeignKeyRow(
        string id,
        string description,
        string? firstName,
        string? secondName,
        string firstExpected,
        string secondExpected
    ) =>
        ColumnsRow(
            id,
            description,
            () =>
            {
                var (customers, profiles) = Columns_NavigationTables();
                var diagram = Columns_NavigationDiagram(customers, profiles);
                diagram.Relationships.Add(
                    Columns_NavigationReference(
                        customers,
                        profiles,
                        "customer_id",
                        RelationshipType.OneToMany,
                        firstName
                    )
                );
                diagram.Relationships.Add(
                    Columns_NavigationReference(
                        customers,
                        profiles,
                        "customer_id",
                        RelationshipType.OneToMany,
                        secondName
                    )
                );
                return diagram;
            },
            Columns_NavigationOptions(),
            DiagnosticExpectation.Exact([
                ExpectedDiagnostic.ErrorMentioning(
                    nameof(Strings.CodeGen_Error_DuplicateForeignKey),
                    $"'{firstExpected}'",
                    $"'{secondExpected}'",
                    "'profiles'.'customer_id'",
                    "'customers'.'id'"
                ),
            ])
        );

    /// <summary>出力ファイル名の正規化の行（本体・サーバー実装・API リファレンスの名前が出力の順に並ぶ）</summary>
    private static ScenarioRow<CodegenCase> Columns_RemoteOutputNameRow(
        string id,
        string description,
        string outputFileName,
        string mainName,
        bool multiDialect
    )
    {
        var baseName = "Shop";

        return ColumnsRow(
            id,
            description,
            Columns_RemoteServiceDiagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                OutputFileName = outputFileName,
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                RepositoryDialects = multiDialect
                    ? [DialectNames.SqlServer, DialectNames.Sqlite]
                    : [DialectNames.SqlServer],
                GenerateApiDocs = true,
            },
            DiagnosticExpectation.Contains(),
            ColumnsAllFiles(mainName, baseName + ".RemoteServer.g.cs", baseName + ".g.md")
        );
    }

    // ---- オプション ----

    /// <summary>Repository だけを生成する標準オプション（計算列・採番列の元のテストの <c>Generate</c>）</summary>
    private static CodeGenerationOptions Columns_RepositoryOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = false,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>方言（と同期支援）だけを差し替える Repository のオプション（元のテストの <c>GenerateFor</c> / <c>GenerateRepositories</c>）</summary>
    private static CodeGenerationOptions Columns_DialectOptions(
        string[] dialects,
        bool syncSupport = false,
        string rootNamespace = "Sample.Domain"
    ) =>
        new()
        {
            RootNamespace = rootNamespace,
            GenerateRepositories = true,
            RepositoryDialects = dialects,
            GenerateSyncSupport = syncSupport,
        };

    /// <summary>PostgreSQL の配列列の元のテストの <c>BaseOptions</c></summary>
    private static CodeGenerationOptions Columns_PostgreSqlArrayOptions(
        bool editModels,
        bool valueObjects
    ) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = editModels,
            GenerateMappers = editModels,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
        };

    /// <summary>PostgreSQL のネットワーク型の元のテストの <c>Options</c></summary>
    private static CodeGenerationOptions Columns_NetworkOptions(
        bool editModels,
        bool valueObjects,
        bool inMemory
    ) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = editModels,
            GenerateMappers = editModels,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = inMemory,
        };

    /// <summary>フォールバック型の元のテストの <c>Generate</c></summary>
    private static CodeGenerationOptions Columns_FallbackOptions() =>
        new() { RootNamespace = "Sample.Domain", GenerateRepositories = true };

    /// <summary>VO 有効・全カテゴリ生成の標準オプション（ValueObjectForeignKeyUnificationTests.VoOptions）</summary>
    private static CodeGenerationOptions Columns_VoOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateValueObjects = true,
            GenerateRepositories = true,
            GenerateEditModels = true,
            GenerateMappers = true,
        };

    /// <summary>RepositorySingleKeyWarningTests の生成オプション</summary>
    private static CodeGenerationOptions Columns_RepositoryOnlyOptions() =>
        new() { RootNamespace = "Sample.Domain", GenerateRepositories = true };

    /// <summary>NavigationNameCollisionTests.Generate のオプション</summary>
    private static CodeGenerationOptions Columns_NavigationOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer],
            GenerateInMemoryRepositories = true,
            GenerateEfCoreRepositories = true,
            GenerateRemoteServices = true,
        };

    /// <summary>IncludeGraphGenerationTests.RepositoryOptions</summary>
    private static CodeGenerationOptions Columns_IncludeGraphOptions() =>
        new() { RootNamespace = "Test.Ns", GenerateRepositories = true };

    /// <summary>ExtensionShimGenerationTests.Options（Entity・EditModel・Mapper・VO がすべて出る最小構成）</summary>
    private static CodeGenerationOptions Columns_ExtensionShimOptions() =>
        new()
        {
            RootNamespace = "Shim.Sample",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateValueObjects = true,
        };

    /// <summary>ExtensionShimGenerationTests.BackendOptions（バックエンド 4 枝と Mapper がすべて出る構成）</summary>
    private static CodeGenerationOptions Columns_ExtensionShimBackendOptions() =>
        Columns_ExtensionShimOptions() with
        {
            GenerateValueObjects = false,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer],
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = true,
            GenerateRemoteServices = true,
        };

    /// <summary>SplitRuntimeSymmetryTests.MultiDialectOptions（全機能 ON のマルチ方言分割）</summary>
    private static CodeGenerationOptions Columns_SplitRuntimeMultiDialectOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateRemoteServices = true,
            GenerateValueObjects = true,
            IncludeDataAnnotations = true,
            IncludeJsonIgnoreOnParentNavigation = true,
            ExcludeUnboundedBinaryColumns = true,
        };

    // ---- 元のテストの図（そのまま写す） ----

    /// <summary>ComputedColumnGenerationTests.Diagram（<c>total</c> を計算列にするかを選ぶ。主キーは <c>item_id</c>）</summary>
    private static ErDiagram Columns_ComputedDiagram(bool computed) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "total",
                            DataType = "decimal(21,2)",
                            IsNullable = true,
                            IsComputed = computed,
                            IsPrimaryKey = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>ComputedColumnGenerationTests.NotNullComputedDiagram（NOT NULL の計算列 1 本・テンポラルテーブルの期間列の形）</summary>
    private static ErDiagram Columns_NotNullComputedDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "temporal_items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "period_start",
                            DataType = "datetime2",
                            IsNullable = false,
                            IsComputed = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>IdentityColumnGenerationTests.Diagram（<paramref name="nullableSeq"/> は元のテストが後から <c>seq</c> を NULL 許容へ書き換えた形）</summary>
    private static ErDiagram Columns_IdentityDiagram(
        bool identity,
        bool identityPrimaryKey = false,
        bool nullableSeq = false
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            IsIdentity = identityPrimaryKey,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "qty",
                            DataType = "int",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "seq",
                            DataType = "bigint",
                            IsNullable = nullableSeq,
                            IsIdentity = identity,
                        },
                    ],
                },
            ],
        };

    /// <summary>PostgreSqlArrayColumnGenerationTests.BuildDiagramWithArrayUniqueConstraints（単独列と複合の UNIQUE 制約）</summary>
    private static ErDiagram Columns_PostgreSqlArrayUniqueDiagram()
    {
        var diagram = PostgreSqlArrayDiagram();
        var entity = diagram.Entities[0];
        var tags = entity.Columns.Single(column => column.Name == "tags");
        var name = entity.Columns.Single(column => column.Name == "name");

        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "uq_articles_tags", ColumnIds = [tags.Id] }
        );

        // 名前を持たない複合制約（名指しでは合成名が出る）
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [name.Id, tags.Id] });

        return diagram;
    }

    /// <summary>PostgreSqlNetworkTypeGenerationTests.BuildDiagram（inet / cidr / interval の列）</summary>
    private static ErDiagram Columns_NetworkDiagram() =>
        new()
        {
            TargetDbms = "postgresql",
            Entities =
            {
                new Entity
                {
                    TableName = "hosts",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "integer",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "addr",
                            DataType = "inet",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "backup_addr",
                            DataType = "inet",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "net",
                            DataType = "cidr",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "backup_net",
                            DataType = "cidr",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "lease",
                            DataType = "interval",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "grace",
                            DataType = "interval day to second(3)",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>NegativeScaleColumnGenerationTests.Diagram（金額列 1 本）</summary>
    private static ErDiagram Columns_NegativeScaleDiagram(string amountType) =>
        new()
        {
            TargetDbms = "postgresql",
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = amountType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>FallbackTypeGenerationTests.Diagram（型カタログが解析できない型の列を持つか選ぶ）</summary>
    private static ErDiagram Columns_FallbackDiagram(bool includeUnknownType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "location",
                            DataType = includeUnknownType ? "geometry" : "nvarchar(50)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

    /// <summary>ValueObjectForeignKeyUnificationTests.Key / IncludeGraphGenerationTests.Key（主キー列・int）</summary>
    private static Column Columns_Key(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>ValueObjectForeignKeyUnificationTests.Fk / IncludeGraphGenerationTests.Fk（外部キー列・int・NULL 許容）</summary>
    private static Column Columns_Fk(string name, bool isForeignKey) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsForeignKey = isForeignKey,
            IsNullable = true,
        };

    /// <summary>1 対多のリレーション（親 principal → 子 dependent）</summary>
    private static Relationship Columns_OneToMany(
        Entity principal,
        Column principalColumn,
        Entity dependent,
        Column dependentColumn
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = RelationshipType.OneToMany,
            SourceEntityId = principal.Id,
            TargetEntityId = dependent.Id,
            ColumnPairs = { new(principalColumn.Id, dependentColumn.Id) },
        };

    /// <summary>ValueObjectForeignKeyUnificationTests.MismatchedNameDiagram（customers.customer_id ← orders.ship_customer_id）</summary>
    private static ErDiagram Columns_VoMismatchedNameDiagram()
    {
        var customerId = Columns_Key("customer_id");
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Columns_Key("order_id");
        var shipCustomerId = Columns_Fk("ship_customer_id", isForeignKey: true);
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { Columns_OneToMany(customers, customerId, orders, shipCustomerId) },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.統一が起きない図では診断を出さない の図（列名が一致する）</summary>
    private static ErDiagram Columns_VoMatchingNameDiagram()
    {
        var customerId = Columns_Key("customer_id");
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Columns_Key("order_id");
        var orderCustomerId = Columns_Fk("customer_id", isForeignKey: true);
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerId } };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { Columns_OneToMany(customers, customerId, orders, orderCustomerId) },
        };
    }

    /// <summary>ValueObjectForeignKeyUnificationTests.型の違う親を複数参照する子列はエラー の図</summary>
    private static ErDiagram Columns_VoConflictingParentsDiagram()
    {
        var personId = Columns_Key("person_id");
        var people = new Entity { TableName = "people", Columns = { personId } };

        var companyId = Columns_Key("company_id");
        var companies = new Entity { TableName = "companies", Columns = { companyId } };

        var itemId = Columns_Key("item_id");
        var ownerId = Columns_Fk("owner_id", isForeignKey: true);
        var items = new Entity { TableName = "items", Columns = { itemId, ownerId } };

        return new ErDiagram
        {
            Entities = { people, companies, items },
            Relationships =
            {
                Columns_OneToMany(people, personId, items, ownerId),
                Columns_OneToMany(companies, companyId, items, ownerId),
            },
        };
    }

    /// <summary>SyncFixedLengthKeyWarningTests.MarkerKeyTyped（同期のフィクスチャの図で、マーカーの主キーの型だけを差し替える）</summary>
    private static ErDiagram Columns_SyncMarkerKeyTyped(string dataType)
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram
            .Entities.Single(entity => entity.TableName == "sync_markers")
            .Columns.Single(column => column.IsPrimaryKey)
            .DataType = dataType;
        return diagram;
    }

    /// <summary>RepositorySingleKeyWarningTests.Table</summary>
    private static Entity Columns_SingleKeyTable(
        string name,
        params (string Name, bool Key)[] columns
    )
    {
        var entity = new Entity { TableName = name };

        foreach (var (column, key) in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = "int",
                    IsPrimaryKey = key,
                    IsNullable = !key,
                }
            );
        }

        return entity;
    }

    /// <summary>RepositorySingleKeyWarningTests の図（単一主キーの customers と、複合主キーまたは主キーなしの表）</summary>
    private static ErDiagram Columns_SingleKeyDiagram(Entity table) =>
        new() { Entities = { Columns_SingleKeyTable("customers", ("id", true)), table } };

    /// <summary>NavigationNameCollisionTests.Tables（表 customers（列 profile_id）と profiles（列 customer_id・backup_customer_id））</summary>
    private static (Entity Customers, Entity Profiles) Columns_NavigationTables() =>
        (
            Columns_NavigationTable("customers", "profile_id"),
            Columns_NavigationTable("profiles", "customer_id", "backup_customer_id")
        );

    /// <summary>NavigationNameCollisionTests.Diagram</summary>
    private static ErDiagram Columns_NavigationDiagram(params Entity[] entities)
    {
        var diagram = new ErDiagram { TargetDbms = "sqlserver" };
        diagram.Entities.AddRange(entities);
        return diagram;
    }

    /// <summary>NavigationNameCollisionTests.Table（主キー <c>id</c> と、指定した int の列を持つ表）</summary>
    private static Entity Columns_NavigationTable(string name, params string[] columns)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        foreach (var column in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                }
            );
        }

        return entity;
    }

    /// <summary>NavigationNameCollisionTests.Reference（子の列から親の主キーへのリレーション）</summary>
    private static Relationship Columns_NavigationReference(
        Entity parent,
        Entity child,
        string childColumn,
        RelationshipType type,
        string? constraintName = null
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = type,
            ConstraintName = constraintName,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    parent.Columns.Single(column => column.IsPrimaryKey).Id,
                    child.Columns.Single(column => column.Name == childColumn).Id
                ),
            ],
        };

    /// <summary>IncludeGraphGenerationTests.ChainDiagram（root → mid → leaf の 3 階層チェーン）</summary>
    private static ErDiagram Columns_IncludeGraphChainDiagram()
    {
        var rootId = Columns_Key("root_id");
        var root = new Entity { TableName = "root", Columns = { rootId } };

        var midId = Columns_Key("mid_id");
        var midRootId = Columns_Fk("root_id", isForeignKey: false);
        var mid = new Entity { TableName = "mid", Columns = { midId, midRootId } };

        var leafId = Columns_Key("leaf_id");
        var leafMidId = Columns_Fk("mid_id", isForeignKey: false);
        var leaf = new Entity { TableName = "leaf", Columns = { leafId, leafMidId } };

        return new ErDiagram
        {
            Entities = { root, mid, leaf },
            Relationships =
            {
                Columns_OneToMany(root, rootId, mid, midRootId),
                Columns_OneToMany(mid, midId, leaf, leafMidId),
            },
        };
    }

    /// <summary>IncludeGraphGenerationTests.SelfReference_IsSkippedAndReported の図（自己参照）</summary>
    private static ErDiagram Columns_IncludeGraphSelfReferenceDiagram()
    {
        var nodeId = Columns_Key("node_id");
        var parentId = Columns_Fk("parent_id", isForeignKey: false);
        var node = new Entity { TableName = "node", Columns = { nodeId, parentId } };

        return new ErDiagram
        {
            Entities = { node },
            Relationships = { Columns_OneToMany(node, nodeId, node, parentId) },
        };
    }

    /// <summary>IncludeGraphGenerationTests.MutualReference_SkipsOnlyTheRevisitingEdge の図（A → B → A）</summary>
    private static ErDiagram Columns_IncludeGraphMutualReferenceDiagram()
    {
        var alphaId = Columns_Key("alpha_id");
        var alphaBetaId = Columns_Fk("beta_id", isForeignKey: false);
        var alpha = new Entity { TableName = "alpha", Columns = { alphaId, alphaBetaId } };

        var betaId = Columns_Key("beta_id");
        var betaAlphaId = Columns_Fk("alpha_id", isForeignKey: false);
        var beta = new Entity { TableName = "beta", Columns = { betaId, betaAlphaId } };

        return new ErDiagram
        {
            Entities = { alpha, beta },
            Relationships =
            {
                Columns_OneToMany(alpha, alphaId, beta, betaAlphaId),
                Columns_OneToMany(beta, betaId, alpha, alphaBetaId),
            },
        };
    }

    /// <summary>UniquenessGenerationTests の図（Order エンティティ＋予約名 CheckUniqueness の名前付きクエリ。UNIQUE 制約なし）</summary>
    private static ErDiagram Columns_UniquenessOrderWithCheckUniquenessQuery()
    {
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            {
                new Column
                {
                    Name = "OrderId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
                new Column { Name = "Memo", DataType = "nvarchar(200)" },
            },
        };
        var diagram = new ErDiagram { Entities = { order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "CheckUniqueness",
                Returns = QueryReturnShape.List,
            }
        );

        return diagram;
    }

    /// <summary>
    /// DisplayNameGenerationTests.Generate_EntityWithDisplayNameColumn_OmitsMembersWarnsAndKeepsOtherEntities の図
    /// （display_name 列を持つ labels と、衝突しない customers）
    /// </summary>
    private static ErDiagram Columns_DisplayNameCollisionDiagram() =>
        new()
        {
            Entities =
            [
                // display_name 列 → プロパティ名 DisplayName が静的メンバーと衝突する
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "labels",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "label_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "display_name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                // 衝突しない別エンティティは通常どおり DisplayName を持つ
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>RemoteServiceGenerationTests.CreateDiagram（Order エンティティ＋名前付きクエリ 2 件〔DSL 一覧・パラメータなし単一〕）</summary>
    private static ErDiagram Columns_RemoteServiceDiagram()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        var diagram = new ErDiagram { Entities = { order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
                HasPaging = true,
            }
        );
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "FindTop",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Manual,
            }
        );

        return diagram;
    }

    /// <summary>ExtensionShimGenerationTests.Diagram（string / bool の VO が出る 1 エンティティ）</summary>
    private static ErDiagram Columns_ExtensionShimDiagram()
    {
        var customer = new Entity { TableName = "customers" };
        customer.Columns.Add(
            new Column
            {
                Name = "customer_id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "nvarchar(50)",
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "is_active",
                DataType = "bit",
                IsNullable = false,
            }
        );

        return new ErDiagram { Entities = { customer } };
    }

    /// <summary>
    /// SplitRuntimeSymmetryTests.Diagram（説明・UNIQUE 制約・無制限バイナリ列を含む図。属性型の出力条件をすべて満たして
    /// パッケージ書き出しと同じ固定 infra 集合になる）
    /// </summary>
    private static ErDiagram Columns_SplitRuntimeDiagram()
    {
        var customer = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var customerCode = Guid.NewGuid();
        var order = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    // [DbTableMeta] / [DbColumnMeta] の出力条件（説明が 1 つでもある）を満たす
                    Description = "Customer master",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        // 文字列列は Unicode で統一（Ansi/Unicode 差で canonical トークンが割れるため）
                        new Column
                        {
                            Id = customerCode,
                            Name = "code",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                            Description = "Business code",
                        },
                        // 無制限バイナリ列（除外オプションの経路と [UnboundedBinaryColumn] を有効化する）
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    ],
                    // [UniqueConstraint] 属性型の出力条件を満たす
                    UniqueConstraints =
                    [
                        new UniqueConstraint
                        {
                            Id = Guid.NewGuid(),
                            Name = "UQ_customers_code",
                            ColumnIds = [customerCode],
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = orderPk,
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    ColumnPairs = [new(customerPk, orderFk)],
                },
            ],
        };
    }
}
