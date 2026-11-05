using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：方言の構成（名前付きクエリのコンパイル・双方向同期の診断・マルチターゲットの診断／ファイル構成／コンパイル・
/// rowversion のマルチターゲット統一）で判定していたテスト
/// </summary>
/// <remarks>
/// 元は <c>QueryGenerationTests</c> / <c>SyncSupportGenerationTests</c> / <c>MultiTargetRepositoryGenerationTests</c> /
/// <c>MultiDialectTypeReconcilerTests</c> / <c>MultiTargetRowVersionGenerationTests</c>。図の方言（型解決）はすべて SQL Server
/// （元のテストの主辞書が SQL Server のマッパだったもの）で、SQLite の図だったもの（<c>RowVersion_SqliteOnly</c> など）だけ SQLite。
/// </remarks>
internal static partial class CodegenCases
{
    private static ScenarioRow<CodegenCase> DialectRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        DiagnosticExpectation diagnostics,
        IReadOnlyList<FileRule>? fileRules = null,
        bool compile = false,
        IReadOnlyList<GeneratedFile>? extraSources = null,
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
                Compile: compile,
                ExtraSources: extraSources
            )
        );

    /// <summary>生成が成り立ち、.g.cs が 1 つ以上ある（成功の期待の足場。エラーなら段の食い違いで赤になる）</summary>
    private static IReadOnlyList<FileRule> DialectsGenerated =>
        [new FileRule(".g.cs", NonEmpty: true)];

    private static IEnumerable<ScenarioRow<CodegenCase>> DialectRows() =>
        [
            // ===== 名前付きクエリ：コンパイルの成否 =====
            DialectRow(
                "compile-query-lowercase-parameter-name",
                "小文字の query パラメータ名は従来どおり使える（QueryGenerationTests.Generate_LowercaseQueryParameterName_IsAccepted）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "FindByQueryWord",
                                    Returns = QueryReturnShape.List,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "query", Type = "string(50)" },
                                    },
                                    Condition = "Memo LIKE @query",
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-query-in-lift-variable-renamed",
                "IN の持ち上げ変数はパラメータ名との衝突を _ 後置で回避する（QueryGenerationTests.Generate_InLiftVariableCollidingWithParameter_IsRenamed のコンパイル）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(withQuantity: true),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetByQuantitySet",
                                    Returns = QueryReturnShape.List,
                                    Parameters =
                                    {
                                        new QueryParameter
                                        {
                                            Name = "quantities",
                                            Type = "int32",
                                            IsList = true,
                                        },
                                        new QueryParameter
                                        {
                                            Name = "quantitiesValues",
                                            Type = "int32",
                                        },
                                    },
                                    Condition =
                                        "Quantity IN @quantities AND CustomerId = @quantitiesValues",
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-query-raw-sql-single-items-parameter",
                "生 SQL 単一戻り形のローカルは引数名 items との衝突を回避する（QueryGenerationTests.Generate_RawSqlSingleWithItemsParameter_RenamesLocal のコンパイル。EF Core は外す）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "FindByItems",
                                    Returns = QueryReturnShape.Single,
                                    Implementation = QueryImplementationKind.Sql,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "items", Type = "int32" },
                                    },
                                    Sql =
                                    {
                                        ["sqlserver"] =
                                            "SELECT * FROM [Order] WHERE [CustomerId] = @items",
                                    },
                                },
                            ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-query-skipped-projection-releases-result-type-name",
                "警告で飛ばされた射影クエリの結果型名は、後続の有効なクエリが使える（QueryGenerationTests.Generate_SkippedProjectionQuery_ReleasesItsResultTypeName のコンパイル）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(withQuantity: true),
                        order =>
                            [
                                // NULL 許容の値型列を非 NULL のフィールドへ写す定義＝警告でスキップされる
                                new QueryDefinition
                                {
                                    Name = "Skipped",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "SharedRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Quantity",
                                            SourceColumnId = Dialects_Column(order, "Quantity").Id,
                                            IsNullable = false,
                                        },
                                    },
                                },
                                new QueryDefinition
                                {
                                    Name = "Valid",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "SharedRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Amount",
                                            SourceColumnId = Dialects_Column(order, "Amount").Id,
                                        },
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-query-not-null-override-on-nullable-value-column-skips",
                "DSL 射影の非 NULL 上書き × NULL 許容値型列は警告＋クエリスキップ（QueryGenerationTests.Generate_ProjectionNotNullOverrideOnNullableValueColumn_SkipsWithWarning のコンパイル。警告・メソッド有無の表明は元のテストに残る）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(withQuantity: true),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetForcedRows",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "ForcedRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Quantity",
                                            SourceColumnId = Dialects_Column(order, "Quantity").Id,
                                            IsNullable = false,
                                        },
                                    },
                                },
                                new QueryDefinition
                                {
                                    Name = "ListAll",
                                    Returns = QueryReturnShape.List,
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ProjectionFieldNotNullOverride),
                        "GetForcedRows",
                        "Quantity"
                    )
                ),
                compile: true
            ),
            DialectRow(
                "compile-query-not-null-override-on-nullable-reference-column",
                "DSL 射影の非 NULL 上書き × NULL 許容の参照型列は null 抑止つきで警告なしに生成される（QueryGenerationTests.Generate_ProjectionNotNullOverrideOnNullableReferenceColumn_SuppressesWarning(false)）",
                () => Dialects_MemoRowsDiagram(),
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = false,
                },
                DiagnosticExpectation.Exact([]),
                compile: true
            ),
            DialectRow(
                "compile-query-not-null-override-on-nullable-reference-column-vo",
                "DSL 射影の非 NULL 上書き × NULL 許容の参照型列は null 抑止つきで警告なしに生成される（QueryGenerationTests.Generate_ProjectionNotNullOverrideOnNullableReferenceColumn_SuppressesWarning(true)）",
                () => Dialects_MemoRowsDiagram(),
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Exact([]),
                compile: true
            ),
            DialectRow(
                "compile-query-guidkey-string-match-skips-partial-matches",
                "GuidKey の列への部分一致は警告でそのクエリだけ飛ばされ、完全一致は生成される（QueryGenerationTests.Generate_StringMatchOnGuidKeyColumn_SkipsOnlyPartialMatches のコンパイル。警告・メソッド有無の表明は元のテストに残る）",
                Dialects_GuidKeyTagDiagram,
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = true,
                    UseGuidKeyForStringPrimaryKey = true,
                },
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "PartialLike",
                        "Code"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "ContainsMatch",
                        "Code"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "NegatedStartsWith",
                        "Code"
                    ),
                ]),
                compile: true
            ),
            DialectRow(
                "compile-query-result-type-name-not-clashing-is-accepted",
                "衝突しない結果型名の射影は従来どおり生成される（QueryGenerationTests.Generate_ProjectionResultTypeNameCollidingWithGeneratedType_IsRejected の後半＝衝突しない名前）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "Rows",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "OrderAmountRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Amount",
                                            SourceColumnId = Dialects_Column(order, "Amount").Id,
                                        },
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-query-raw-sql-line-breaks-and-single-line",
                "改行を含む生 SQL は値を変えずに埋め込まれ、1 行の SQL は逐語リテラルのまま（QueryGenerationTests.Generate_RawSqlWithLineBreaks_KeepsExactValue のコンパイル。値の評価・リテラル形の表明は元のテストに残る）",
                Dialects_RawSqlLineBreaksDiagram,
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            // ===== 名前付きクエリ：診断 =====
            DialectRow(
                "diag-query-invalid-queries-are-errors",
                "検証エラー（未知の列・スカラー×DSL・重複メソッド名）は診断になりファイルを出さない（QueryGenerationTests.Generate_InvalidQueries_ProduceDiagnostics）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "BadColumn",
                                    Returns = QueryReturnShape.List,
                                    Condition = "Nope = 1",
                                },
                                new QueryDefinition
                                {
                                    Name = "BadScalar",
                                    Returns = QueryReturnShape.Scalar,
                                    ScalarType = "int32",
                                    Implementation = QueryImplementationKind.Dsl,
                                },
                                new QueryDefinition
                                {
                                    Name = "Dup",
                                    Returns = QueryReturnShape.List,
                                },
                                new QueryDefinition
                                {
                                    Name = "Dup",
                                    Returns = QueryReturnShape.Count,
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "BadColumn",
                        "Nope"
                    ),
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_ScalarDslUnsupported),
                        "BadScalar"
                    ),
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_DuplicateMethodName),
                        "Dup"
                    )
                )
            ),
            .. new[]
            {
                "SaveMany",
                "Ping",
                "SyncCeiling",
                "SyncChanges",
                "SyncKeys",
                "SyncPage",
                "savemany",
            }.Select(queryName =>
                DialectRow(
                    "diag-query-named-after-remote-operation-"
                        + queryName.ToLowerInvariant()
                        + (queryName == "savemany" ? "-lowercase" : string.Empty),
                    $"リモート操作名と衝突するクエリ名は予約名として拒否される（QueryGenerationTests.Generate_QueryNamedAfterRemoteOperation_IsRejected(\"{queryName}\")）",
                    () =>
                        Dialects_QueryDiagram(
                            Dialects_QueryOrder(),
                            order =>
                                [
                                    new QueryDefinition
                                    {
                                        Name = queryName,
                                        Returns = QueryReturnShape.List,
                                    },
                                ]
                        ),
                    Dialects_QueryOptions(),
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Query_ReservedMethodName),
                            queryName
                        )
                    )
                )
            ),
            DialectRow(
                "diag-query-differing-only-in-case-second-is-rejected",
                "大小だけ違うクエリ名は重複として拒否される（QueryGenerationTests.Generate_QueriesDifferingOnlyInCase_SecondIsRejected）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "FindStuff",
                                    Returns = QueryReturnShape.List,
                                },
                                new QueryDefinition
                                {
                                    Name = "findstuff",
                                    Returns = QueryReturnShape.List,
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_DuplicateMethodName),
                        "findstuff"
                    )
                )
            ),
            .. new[] { "class", "int", "ToString" }.Select(parameterName =>
                DialectRow(
                    "diag-query-reserved-parameter-name-" + parameterName.ToLowerInvariant(),
                    $"予約語・リモートのリクエストの record のメンバー名のパラメータ名は診断エラーで拒否される（QueryGenerationTests.Generate_ReservedParameterName_IsRejected(\"{parameterName}\")）",
                    () =>
                        Dialects_QueryDiagram(
                            Dialects_QueryOrder(),
                            order =>
                                [
                                    new QueryDefinition
                                    {
                                        Name = "FindReserved",
                                        Returns = QueryReturnShape.List,
                                        Parameters =
                                        {
                                            new QueryParameter
                                            {
                                                Name = parameterName,
                                                Type = "int32",
                                            },
                                        },
                                        Condition = $"CustomerId = @{parameterName}",
                                    },
                                ]
                        ),
                    Dialects_QueryOptions(),
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.ErrorMentioning(
                            parameterName == "ToString"
                                ? nameof(Strings.CodeGen_Query_ReservedParameterName)
                                : nameof(Strings.CodeGen_Query_InvalidParameterName),
                            parameterName
                        )
                    )
                )
            ),
            DialectRow(
                "diag-query-projection-field-named-after-result-type",
                "結果型名と同名の射影フィールドは診断エラーで拒否される（QueryGenerationTests.Generate_ProjectionFieldNamedAfterResultType_IsRejected）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetClashRows",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "ClashRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "ClashRow",
                                            Type = "decimal(12,2)",
                                            SourceColumnId = Dialects_Column(order, "Amount").Id,
                                        },
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_FieldNameEqualsResultType),
                        "ClashRow"
                    )
                )
            ),
            .. new[]
            {
                "Task",
                "SqlQuery",
                "OrderEntity",
                "IOrderRepository",
                "AmountValue",
            }.Select(typeName =>
                DialectRow(
                    "diag-query-result-type-name-clashes-with-generated-type-"
                        + typeName.ToLowerInvariant(),
                    $"射影 DTO の型名が生成コードの型名と衝突する定義は名指しして断られる（QueryGenerationTests.Generate_ProjectionResultTypeNameCollidingWithGeneratedType_IsRejected(\"{typeName}\")）",
                    () =>
                        Dialects_QueryDiagram(
                            Dialects_QueryOrder(),
                            order =>
                                [
                                    new QueryDefinition
                                    {
                                        Name = "Rows",
                                        Returns = QueryReturnShape.Projection,
                                        ResultTypeName = typeName,
                                        Fields =
                                        {
                                            new ProjectionField
                                            {
                                                Name = "Amount",
                                                SourceColumnId = Dialects_Column(
                                                    order,
                                                    "Amount"
                                                ).Id,
                                            },
                                        },
                                    },
                                ]
                        ),
                    Dialects_QueryOptions(),
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Query_ReservedResultTypeName),
                            "Rows",
                            typeName
                        )
                    )
                )
            ),
            DialectRow(
                "diag-query-dangling-references-warn-and-skip",
                "ダングリング Guid 参照のクエリは警告でスキップし生成は継続する（QueryGenerationTests.Generate_DanglingGuidReferences_WarnAndSkip の診断・ファイル）",
                () =>
                {
                    var missingColumnId = Guid.NewGuid();
                    var diagram = Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "ValidQuery",
                                    Returns = QueryReturnShape.Count,
                                    Condition = "Amount > 0",
                                },
                                new QueryDefinition
                                {
                                    Name = "DanglingParameter",
                                    Parameters =
                                    {
                                        new QueryParameter
                                        {
                                            Name = "typedId",
                                            SourceColumnId = missingColumnId,
                                        },
                                    },
                                },
                                new QueryDefinition
                                {
                                    Name = "DanglingField",
                                    Returns = QueryReturnShape.Projection,
                                    ResultTypeName = "DanglingRow",
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Ghost",
                                            SourceColumnId = missingColumnId,
                                        },
                                    },
                                },
                                new QueryDefinition
                                {
                                    Name = "DanglingOrderBy",
                                    OrderBy = { new QueryOrdering { ColumnId = missingColumnId } },
                                },
                            ]
                    );

                    // 存在しないエンティティを参照するクエリ（削除済みエンティティの残骸）
                    diagram.Queries.Add(
                        new QueryDefinition { EntityId = Guid.NewGuid(), Name = "Orphan" }
                    );
                    return diagram;
                },
                Dialects_QueryOptions(),
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_DanglingParameterColumn),
                        "typedId"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_DanglingFieldColumn),
                        "Ghost"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_DanglingOrderByColumn),
                        "DanglingOrderBy"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_UnknownEntity),
                        "Orphan"
                    ),
                ]),
                DialectsGenerated
            ),
            DialectRow(
                "diag-query-raw-sql-undeclared-parameter-warns-and-skips",
                "生 SQL 未宣言パラメータ: 警告＋該当クエリのみスキップ（QueryGenerationTests.Generate_RawSqlUndeclaredParameter_WarnsAndSkipsQuery の診断）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetGood",
                                    Returns = QueryReturnShape.List,
                                    Implementation = QueryImplementationKind.Sql,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "customerId", Type = "int32" },
                                    },
                                    Sql =
                                    {
                                        ["sqlserver"] =
                                            "SELECT * FROM [Order] WHERE [CustomerId] = @customerId",
                                    },
                                },
                                new QueryDefinition
                                {
                                    Name = "GetBad",
                                    Returns = QueryReturnShape.List,
                                    Implementation = QueryImplementationKind.Sql,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "customerId", Type = "int32" },
                                    },
                                    // @ghost は宣言に無い → 実行時に必ず失敗するため該当クエリをスキップ
                                    Sql =
                                    {
                                        ["sqlserver"] = "SELECT * FROM [Order] WHERE [X] = @ghost",
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_RawSqlIssue),
                        "GetBad",
                        "ghost"
                    )
                ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-query-raw-sql-unused-parameter-warns",
                "生 SQL 未使用パラメータ: 警告のみで生成継続（QueryGenerationTests.Generate_RawSqlUnusedParameter_WarnsButGenerates の診断）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "ListEverything",
                                    Returns = QueryReturnShape.List,
                                    Implementation = QueryImplementationKind.Sql,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "unused", Type = "int32" },
                                    },
                                    Sql = { ["sqlserver"] = "SELECT * FROM [Order]" },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_RawSqlIssue),
                        "unused"
                    )
                ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-query-raw-sql-multiple-statements-warns",
                "生 SQL 複文: 警告のみで生成継続（QueryGenerationTests.Generate_RawSqlMultipleStatements_WarnsButGenerates の診断）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetTwo",
                                    Returns = QueryReturnShape.List,
                                    Implementation = QueryImplementationKind.Sql,
                                    Sql =
                                    {
                                        ["sqlserver"] =
                                            "SELECT * FROM [Order]; SELECT * FROM [Order]",
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_RawSqlIssue),
                        "GetTwo"
                    )
                ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-query-raw-sql-with-condition-and-order-warns-ignored",
                "生 SQL 実装に残った条件・並び順は警告で告げられる（QueryGenerationTests.Generate_RawSqlWithConditionAndOrder_WarnsIgnored の診断）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "ListBigOrders",
                                    Returns = QueryReturnShape.List,
                                    Implementation = QueryImplementationKind.Sql,
                                    Parameters =
                                    {
                                        new QueryParameter
                                        {
                                            Name = "minAmount",
                                            Type = "decimal(12,2)",
                                        },
                                    },
                                    Condition = "Amount >= @minAmount",
                                    OrderBy =
                                    {
                                        new QueryOrdering
                                        {
                                            ColumnId = Dialects_Column(order, "Amount").Id,
                                            Descending = true,
                                        },
                                    },
                                    Sql =
                                    {
                                        ["sqlserver"] =
                                            "SELECT * FROM [Order] WHERE [Amount] >= @minAmount ORDER BY [Amount] DESC",
                                    },
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionIgnoredForImplementation),
                        "ListBigOrders"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_OrderByIgnoredForImplementation),
                        "ListBigOrders"
                    ),
                ]),
                DialectsGenerated
            ),
            // ===== 双方向同期：診断 =====
            DialectRow(
                "diag-sync-requires-both-dialects-sqlite",
                "同期支援は sqlserver + sqlite の 2 方言でなければ生成時エラー（SyncSupportGenerationTests.SyncSupport_RequiresBothDialects(\"sqlite\")）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresDialects),
                        DialectNames.Sqlite
                    )
                )
            ),
            DialectRow(
                "diag-sync-requires-repository-implementations",
                "同期支援は QuickER 版 Repository の実装生成を前提とする（未生成はエラー）（SyncSupportGenerationTests.SyncSupport_RequiresRepositoryImplementations）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    GenerateRepositories = false,
                    GenerateInMemoryRepositories = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresRepositories)
                    )
                )
            ),
            DialectRow(
                "diag-sync-requires-an-eligible-table",
                "同期可能なテーブルが 1 つも無ければ同期支援は生成時エラー（SyncSupportGenerationTests.SyncSupport_RequiresAtLeastOneEligibleTable）",
                Dialects_SyncNoEligibleTableDiagram,
                SyncFixtureDefinition.Options,
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_SyncSupportRequiresEligibleTables)
                    ),
                    1
                )
            ),
            DialectRow(
                "diag-sync-off-allows-no-eligible-table",
                "同期支援 OFF なら同期可能テーブル 0 件でもエラーにならない（SyncSupportGenerationTests.SyncSupportOff_AllowsDiagramWithoutEligibleTables）",
                Dialects_SyncNoEligibleTableDiagram,
                SyncFixtureDefinition.Options with
                {
                    GenerateSyncSupport = false,
                },
                // この図は全列の複合主キー化で計算列・無制限バイナリ列も主キーになり、同期とは別の理由でエラーになる（それでも同期のエラーは出ない）
                DiagnosticExpectation
                    .Contains(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_PrimaryKeyUnboundedBinaryColumn),
                            "sync_orders",
                            "attachment"
                        ),
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_PrimaryKeyComputedColumn),
                            "sync_orders",
                            "summary"
                        )
                    )
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_SyncSupportRequiresEligibleTables)
                        )
                    )
            ),
            DialectRow(
                "diag-sync-target-tables-info-in-foreign-key-order",
                "同期対象テーブルの一覧が Info 診断で通知される（FK 順のまま）（SyncSupportGenerationTests.SyncSupport_ReportsTargetTablesAsInfo）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioningInOrder(
                        nameof(Strings.CodeGen_Info_SyncSupportTables),
                        "sync_orders",
                        "sync_order_lines"
                    ),
                    1
                )
            ),
            DialectRow(
                "diag-sync-unbounded-binary-exclusion-reports-info-instead-of-error",
                "同期支援と無制限バイナリ列の除外は併用でき、運ばれない列が Info 診断に出る（SyncSupportGenerationTests.SyncSupport_WithUnboundedBinaryExclusion_ReportsInfoInsteadOfError）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                DiagnosticExpectation
                    .CountOf(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns),
                            "sync_orders",
                            "Attachment"
                        ),
                        1
                    )
                    .Without(
                        ExpectedDiagnostic.InfoMentioning(
                            nameof(Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns),
                            "sync_order_lines"
                        )
                    ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-sync-no-excluded-column-in-sync-tables-no-binary-info",
                "同期対象テーブルに除外列が無ければ同期の除外列 Info は出ない（SyncSupportGenerationTests.SyncSupport_WithoutExcludedColumnsInSyncTables_EmitsNoBinaryInfo）",
                () =>
                {
                    var diagram = SyncFixtureDefinition.Build();
                    diagram
                        .Entities.Single(entity => entity.TableName == "sync_orders")
                        .Columns.RemoveAll(column => column.Name == "attachment");
                    return diagram;
                },
                SyncFixtureDefinition.Options,
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Info(
                            nameof(Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns)
                        )
                    ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-sync-mixed-diagram-names-versionless-table-in-info",
                "混在図の版なしテーブルは後勝ち専用として Info 診断に名指しされる（SyncSupportGenerationTests.MixedDiagram_EmitsVersionlessShapes の診断）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_SyncSupportTables),
                        "sync_notes"
                    )
                ),
                DialectsGenerated
            ),
            // ===== 双方向同期：ファイル構成 =====
            DialectRow(
                "files-sync-without-remote-services-single-file",
                "同期支援 ON × リモートサービス OFF ではサーバーファイルが出ず、ファイルは 1 つだけ（SyncSupportGenerationTests.SyncSupportWithoutRemoteServices_EmitsNoHttpTransport のファイル構成）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    GenerateRemoteServices = false,
                },
                DiagnosticExpectation.Contains(),
                [
                    new FileRule(
                        "",
                        Count: 1,
                        Names: [SyncFixtureDefinition.OutputFileName],
                        AnyDirectory: true
                    ),
                ]
            ),
            // ===== マルチターゲット =====
            DialectRow(
                "diag-multitarget-matching-types-no-mismatch-error",
                "方言間で C# 型が一致する（可搬型）なら診断エラーは出ない（MultiTargetRepositoryGenerationTests.Generate_MatchingTypesAcrossDialects_NoMismatchError）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions(),
                DiagnosticExpectation.Contains(),
                DialectsGenerated
            ),
            DialectRow(
                "diag-multitarget-efcore-is-exclusive",
                "マルチ方言＋EF Core は診断エラー（QuickER マルチターゲットと EF Core は排他）（MultiTargetRepositoryGenerationTests.MultiDialect_WithEfCore_ReturnsErrorDiagnostic）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    GenerateEfCoreRepositories = true,
                },
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.Error(
                        nameof(Strings.CodeGen_Error_MultiTargetEfCoreExclusive)
                    ),
                    1
                )
            ),
            DialectRow(
                "diag-multitarget-efcore-without-repositories-is-not-exclusive",
                "Repository 非生成なら複数方言指定＋EF Core は排他ガードに掛からない（MultiTargetRepositoryGenerationTests.MultiDialect_WithEfCore_WithoutRepositories_ReturnsNoExclusiveError）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_MultiTargetEfCoreExclusive)
                        )
                    ),
                DialectsGenerated
            ),
            DialectRow(
                "compile-multitarget-nonsplit-repository-namespace-outside-root",
                "マルチ方言（非分割）は Repository 名前空間がルートの入れ子でなくても連結ファイルがコンパイルできる（MultiTargetRepositoryGenerationTests.MultiDialect_NonSplit_RepositoryNamespaceOutsideRoot_Compiles(false)）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    RootNamespace = "MyApp",
                    RepositoryNamespace = "Data.Repos",
                    GenerateValueObjects = false,
                },
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "compile-multitarget-nonsplit-repository-namespace-outside-root-vo",
                "マルチ方言（非分割）は Repository 名前空間がルートの入れ子でなくても連結ファイルがコンパイルできる（MultiTargetRepositoryGenerationTests.MultiDialect_NonSplit_RepositoryNamespaceOutsideRoot_Compiles(true)）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    RootNamespace = "MyApp",
                    RepositoryNamespace = "Data.Repos",
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true
            ),
            DialectRow(
                "files-multitarget-split-per-dialect-files",
                "マルチ方言（分割）は契約ファイルと方言別ファイルを出す（MultiTargetRepositoryGenerationTests.MultiDialect_Split_AdoUsingsPlacedPerDialectFile のファイル構成）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                [
                    .. new[]
                    {
                        "Repositories.g.cs",
                        "Repositories.SqlServer.g.cs",
                        "Repositories.Sqlite.g.cs",
                        "Runtime.g.cs",
                        "Runtime.SqlServer.g.cs",
                        "Runtime.Sqlite.g.cs",
                    }.Select(name => new FileRule(
                        name,
                        Count: 1,
                        Names: [name],
                        AnyDirectory: true
                    )),
                ]
            ),
            DialectRow(
                "compile-multitarget-nonsplit-keyed-resolution",
                "マルチ方言出力（両方言同梱・keyed 利用）がエラー・警告 0 でコンパイルできる（MultiTargetRepositoryGenerationTests.MultiDialect_CompilesWithKeyedResolution(false)。利用側の断片を同梱）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions(),
                DiagnosticExpectation.Contains(),
                compile: true,
                extraSources: [Dialects_KeyedUsageSource()]
            ),
            DialectRow(
                "compile-multitarget-split-keyed-resolution",
                "マルチ方言出力（両方言同梱・keyed 利用）がエラー・警告 0 でコンパイルできる（MultiTargetRepositoryGenerationTests.MultiDialect_CompilesWithKeyedResolution(true)。利用側の断片を同梱）",
                CustomerOrderDiagram,
                Dialects_MultiTargetOptions() with
                {
                    SplitFilesByCategory = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                extraSources: [Dialects_KeyedUsageSource()]
            ),
            // ===== rowversion × マルチターゲット =====
            DialectRow(
                "diag-rowversion-multitarget-info-names-column-and-owner",
                "rowversion 列のマルチターゲット統一は Info 診断で通知される（MultiTargetRowVersionGenerationTests.Generate_RowVersionMultiTarget_ReportsInfoDiagnostic）",
                MultiTargetRowVersionFixtureDefinition.Build,
                MultiTargetRowVersionFixtureDefinition.Options,
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns),
                        "sync_items.row_ver",
                        DialectNames.SqlServer
                    )
                ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-rowversion-multitarget-no-info-without-rowversion-column",
                "行バージョン列のない図ではマルチターゲットでも rowversion の Info 診断は出ない（MultiTargetRowVersionGenerationTests.Generate_NoRowVersionColumn_NoInfoDiagnostic）",
                MultiTargetPortableFixtureDefinition.Build,
                MultiTargetPortableFixtureDefinition.Options,
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Info(
                            nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns)
                        )
                    ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-rowversion-timestamp-multitarget-info-via-facade",
                "実生成経路（DiagramCodeGenerator）でも timestamp × マルチターゲットがエラーなく通り Info 診断が出る（MultiTargetRowVersionGenerationTests.DiagramCodeGenerator_TimestampMultiTarget_SucceedsWithInfo の診断）",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Hybrid",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns),
                        "sync_items.row_ver"
                    )
                ),
                DialectsGenerated
            ),
            DialectRow(
                "diag-rowversion-sqlite-only-timestamp-no-info",
                "単一方言 sqlite 生成では timestamp は日時で、統一していないので通知もしない（MultiTargetRowVersionGenerationTests.Generate_SqliteOnly_TimestampStaysDateTime の診断）",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.LocalOnly",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Info(
                            nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns)
                        )
                    ),
                DialectsGenerated,
                typeDialect: DialectNames.Sqlite
            ),
            DialectRow(
                "diag-rowversion-efcore-only-hidden-dialects-no-info",
                "Repository 非生成では方言辞書が無視され、統一の Info 診断も出ない（MultiTargetRowVersionGenerationTests.Generate_EfCoreOnlyWithHiddenDialects_IgnoresDialectDictionaries の診断）",
                Dialects_TimestampSpellingDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.EfOnly",
                    GenerateEfCoreRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                },
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.Info(
                            nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns)
                        )
                    ),
                DialectsGenerated,
                typeDialect: DialectNames.Sqlite
            ),
            DialectRow(
                "diag-rowversion-single-owner-reports-one-info-with-all-columns",
                "owner が 1 方言だけなら Info 診断は 1 件で、その方言の全列がまとまる（MultiDialectTypeReconcilerTests.ReconcileRowVersionTypes_SingleOwner_ReportsOneInfoWithAllColumns）",
                Dialects_RowVersionPairDiagram,
                Dialects_MultiTargetOptions(),
                DiagnosticExpectation.CountOf(
                    ExpectedDiagnostic.InfoMentioning(
                        nameof(Strings.CodeGen_Info_MultiTargetRowVersionColumns),
                        DialectNames.SqlServer,
                        "orders.order_ver",
                        "shipments.shipment_ver"
                    ),
                    1
                ),
                DialectsGenerated
            ),
        ];

    // ---- 名前付きクエリの図（QueryGenerationTests の Order エンティティと CreateDiagram） ----

    /// <summary>QueryGenerationTests のコンストラクタが作る Order エンティティ（Quantity は NULL 許容 int を足したいテストだけ）</summary>
    private static Entity Dialects_QueryOrder(bool withQuantity = false)
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
        order.Columns.Add(
            new Column
            {
                Name = "Amount",
                DataType = "decimal(12,2)",
                IsNullable = false,
            }
        );
        order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });

        if (withQuantity)
        {
            order.Columns.Add(
                new Column
                {
                    Name = "Quantity",
                    DataType = "int",
                    IsNullable = true,
                }
            );
        }

        return order;
    }

    private static Column Dialects_Column(Entity entity, string name) =>
        entity.Columns.First(column => column.Name == name);

    /// <summary>QueryGenerationTests.CreateDiagram：クエリをエンティティに結びつけて図に足す</summary>
    private static ErDiagram Dialects_QueryDiagram(
        Entity order,
        Func<Entity, IEnumerable<QueryDefinition>> queries
    )
    {
        var diagram = new ErDiagram { Entities = { order } };

        foreach (var query in queries(order))
        {
            query.EntityId = order.Id;
            diagram.Queries.Add(query);
        }

        return diagram;
    }

    /// <summary>QueryGenerationTests.CreateOptions：QuickER 版 Repository＋EF Core 併存の標準オプション</summary>
    private static CodeGenerationOptions Dialects_QueryOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>EF Core を外した標準オプション（EF Core × 生 SQL は手動実装＝契約宣言のみで、素のコンパイルが CS0535 になるため）</summary>
    private static CodeGenerationOptions Dialects_QueryRepositoryOnlyOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>NULL 許容の参照型の列 Memo を非 NULL 指定のフィールドへ写す射影クエリ（Generate_ProjectionNotNullOverrideOnNullableReferenceColumn_SuppressesWarning）</summary>
    private static ErDiagram Dialects_MemoRowsDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetMemoRows",
                        Returns = QueryReturnShape.Projection,
                        ResultTypeName = "MemoRow",
                        Condition = "Memo IS NOT NULL",
                        Fields =
                        {
                            new ProjectionField
                            {
                                Name = "Memo",
                                SourceColumnId = Dialects_Column(order, "Memo").Id,
                                IsNullable = false,
                            },
                        },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_StringMatchOnGuidKeyColumn_SkipsOnlyPartialMatches の図（文字列主キー Code の Tag 表と 7 本のクエリ）</summary>
    private static ErDiagram Dialects_GuidKeyTagDiagram()
    {
        var tag = new Entity { TableName = "Tag" };
        tag.Columns.Add(
            new Column
            {
                Name = "Code",
                DataType = "nvarchar(36)",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        tag.Columns.Add(new Column { Name = "Label", DataType = "nvarchar(50)" });
        var diagram = new ErDiagram { Entities = { tag } };

        foreach (
            var (name, condition) in new[]
            {
                ("PartialLike", "Code LIKE 'ab%'"),
                ("ContainsMatch", "Code CONTAINS 'ab'"),
                ("NegatedStartsWith", "NOT Code STARTSWITH 'ab'"),
                ("ExactLike", "Code LIKE 'ab'"),
                ("NotExactLike", "Code NOT LIKE 'ab'"),
                ("Equality", "Code = 'ab'"),
                ("OtherColumnStillMatches", "Label CONTAINS 'x'"),
            }
        )
        {
            diagram.Queries.Add(
                new QueryDefinition
                {
                    EntityId = tag.Id,
                    Name = name,
                    Returns = QueryReturnShape.List,
                    Condition = condition,
                }
            );
        }

        return diagram;
    }

    /// <summary>QueryGenerationTests.Generate_RawSqlWithLineBreaks_KeepsExactValue の図（改行・LS/PS/FF/NEL を含む生 SQL 3 本。実文字はエスケープ表記で組む）</summary>
    private static ErDiagram Dialects_RawSqlLineBreaksDiagram()
    {
        var separators = $"{(char)0x2028}b{(char)0x2029}c\f{(char)0x85}d";
        var multiLine = "SELECT *\r\nFROM [Order]\nWHERE [Memo] = 'a" + separators + "\r'\r\n\r\n";
        var singleLine = "SELECT * FROM [Order] WHERE [Memo] = \"q\"";

        return Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "FindMultiLine",
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = multiLine },
                    },
                    new QueryDefinition
                    {
                        Name = "CountMultiLine",
                        Returns = QueryReturnShape.Count,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "\n" + multiLine },
                    },
                    new QueryDefinition
                    {
                        Name = "FindSingleLine",
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = singleLine },
                    },
                ]
        );
    }

    // ---- 双方向同期の図 ----

    /// <summary>SyncSupportGenerationTests.NoEligibleTableDiagram：全テーブルを複合主キー化した同期可能テーブル 0 件の図</summary>
    private static ErDiagram Dialects_SyncNoEligibleTableDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                column.IsPrimaryKey = column.DataType != "rowversion";
            }
        }

        return diagram;
    }

    // ---- マルチターゲットの図・オプション ----

    /// <summary>MultiTargetRepositoryGenerationTests.GenerateMultiTarget のオプション（sqlserver / sqlite・非分割・EF Core なし）</summary>
    private static CodeGenerationOptions Dialects_MultiTargetOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
        };

    /// <summary>MultiTargetRepositoryGenerationTests.MultiDialect_CompilesWithKeyedResolution の利用側のコード断片（keyed DI で 2 接続を登録して解決する）</summary>
    private static GeneratedFile Dialects_KeyedUsageSource() =>
        new()
        {
            FileName = "MultiTargetUsage.g.cs",
            Content = """
                // <auto-generated />
                #nullable enable
                using Microsoft.Extensions.DependencyInjection;
                using Sample.Domain.Repositories;
                using Sample.Domain.Repositories.SqlServer;
                using Sample.Domain.Repositories.Sqlite;

                namespace Sample.Domain.Usage;

                // internal: 公開型だと XmlDoc 診断（DocumentationMode.Diagnose）の CS1591 が
                // このテスト自作の利用例に出る（生成コードの XmlDoc 検証とは無関係のノイズ）
                internal static class MultiTargetUsage
                {
                    public static ICustomerRepository Wire(IServiceCollection services)
                    {
                        // 非 keyed の単独登録（方言別名）
                        services.AddGeneratedSqlServerRepositories("Server=.;Database=a;Trusted_Connection=True;");

                        // keyed 登録（同一契約 ICustomerRepository を方言別に登録）
                        services.AddGeneratedSqlServerRepositories("server", "Server=.;Database=b;Trusted_Connection=True;");
                        services.AddGeneratedSqliteRepositories("local", "Data Source=local.db");

                        var provider = services.BuildServiceProvider();
                        var server = provider.GetRequiredKeyedService<ICustomerRepository>("server");
                        var local = provider.GetRequiredKeyedService<ICustomerRepository>("local");
                        return server;
                    }

                    // [FromKeyedServices] で方言別の同一契約を注入解決できる
                    public sealed class Consumer(
                        [FromKeyedServices("server")] ICustomerRepository server,
                        [FromKeyedServices("local")] ICustomerRepository local
                    )
                    {
                        public ICustomerRepository Server { get; } = server;
                        public ICustomerRepository Local { get; } = local;
                    }
                }
                """,
        };

    // ---- rowversion の図 ----

    /// <summary>MultiTargetRowVersionGenerationTests.BuildTimestampSpellingDiagram：版列の綴りを非推奨別名の timestamp にした図</summary>
    private static ErDiagram Dialects_TimestampSpellingDiagram()
    {
        var diagram = MultiTargetRowVersionFixtureDefinition.Build();
        diagram.Entities[0].Columns[2].DataType = "timestamp";

        return diagram;
    }

    /// <summary>MultiDialectTypeReconcilerTests.BuildRowVersionDiagram：行バージョン列を 1 本ずつ持つ 2 エンティティの図</summary>
    private static ErDiagram Dialects_RowVersionPairDiagram() =>
        new()
        {
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
                            Name = "order_ver",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "shipments",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "shipment_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "shipment_ver",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
}
