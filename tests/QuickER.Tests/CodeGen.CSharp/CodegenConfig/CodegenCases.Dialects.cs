using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedMultiTargetFixture;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

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
        string typeDialect = DialectNames.SqlServer,
        IReadOnlyList<CodeRule>? code = null
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
                ExtraSources: extraSources,
                Code: code
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
                "規則の検証エラー（スカラー×DSL・重複メソッド名）は Error の診断になりファイルを出さない（QueryGenerationTests.Generate_InvalidQueries_ProduceDiagnostics。条件の検証の失敗はそのクエリだけ飛ばす警告で、行 diag-query-condition-invalid-warns-and-skips-only-that-query が見る）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
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
            // 生成側の診断（保存前の規則 QueryDefinitionRules.Validate の戻りは公開 API の契約として元のテストに残した）
            DialectRow(
                "diag-query-reserved-parameter-name-selector-field",
                "簡易 DSL の射影の選択式のフィールドと同じ名前のパラメータは、生成器で断られる（QueryReservedParameterNameTests.SelectorFieldNamedParameter_IsRejected の生成側）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_ReservedNameOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "Rows",
                                    Returns = QueryReturnShape.Projection,
                                    Implementation = QueryImplementationKind.Dsl,
                                    Condition = "Qty = @_rowsAsyncSelector",
                                    ResultTypeName = "RowsRow",
                                    Parameters =
                                    {
                                        new QueryParameter
                                        {
                                            Name = "_rowsAsyncSelector",
                                            Type = "int32",
                                        },
                                    },
                                    Fields =
                                    {
                                        new ProjectionField
                                        {
                                            Name = "Qty",
                                            SourceColumnId = Dialects_Column(order, "Qty").Id,
                                        },
                                    },
                                },
                            ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_ReservedParameterName),
                        "_rowsAsyncSelector"
                    )
                )
            ),
            DialectRow(
                "diag-query-reserved-parameter-name-await-raw-sql-single",
                "await という名前のパラメータは、生 SQL の単一戻り（async メソッド）で生成時エラーになる（QueryReservedParameterNameTests.AwaitParameter_IsRejected の生成側）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_ReservedNameOrder(),
                        _ =>
                            [
                                new QueryDefinition
                                {
                                    Name = "FindOne",
                                    Returns = QueryReturnShape.Single,
                                    Implementation = QueryImplementationKind.Sql,
                                    Sql =
                                    {
                                        [DialectNames.SqlServer] =
                                            "SELECT * FROM [Order] WHERE [Qty] = @await",
                                    },
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "await", Type = "int32" },
                                    },
                                },
                            ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_ReservedParameterName),
                        "await"
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

    // ===== 名前付きクエリの簡易 DSL：診断・型の検査・コンパイル（P3-6 で単発のテストから移した行） =====

    /// <summary>
    /// 簡易 DSL と生 SQL のクエリの診断・型の検査・コンパイルの行（元は <c>QueryCodegenDiagnosticsTests</c>・
    /// <c>QueryConditionTypeCheckTests</c>・<c>QueryConditionCSharpEmitterTests</c>・<c>QueryInNullableUnknownValueTypeTests</c>）。
    /// 条件の意味（NULL の行・NOT・文字列一致・IN）は簡易 DSL の総当たり（<c>DslConditionFuzzTests</c>）が見る
    /// </summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> DialectDslRows()
    {
        const string contract = RepositoryContract;
        const string implementation = "OrderRepository";
        const string diagnosticsClass = "QueryCodegenDiagnosticsTests.";

        return
        [
            // ===== 生 SQL の未宣言パラメータが、使われない SQL でクエリを止めない =====
            DialectRow(
                "code-query-raw-sql-undeclared-parameter-in-unused-dialect-keeps-query",
                diagnosticsClass
                    + "UndeclaredParameterInUnsupportedDialect_DoesNotSkipQuery（mysql の SQL の誤りで、使われる sqlserver の実装まで止めない）",
                () =>
                    Dialects_OrdersDiagram(
                        new QueryDefinition
                        {
                            Name = "FindByStatus",
                            Implementation = QueryImplementationKind.Sql,
                            Sql =
                            {
                                ["sqlserver"] = "SELECT * FROM Orders WHERE status = @status",
                                ["mysql"] = "SELECT * FROM Orders WHERE status = @statsu",
                            },
                            Parameters =
                            {
                                new QueryParameter { Name = "status", Type = "string(20)" },
                            },
                        }
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(),
                code:
                [
                    Member(contract, "FindByStatusAsync"),
                    Member(implementation, "FindByStatusAsync"),
                ]
            ),
            DialectRow(
                "code-query-raw-sql-undeclared-parameter-in-unselected-dialect-keeps-query",
                diagnosticsClass
                    + "UndeclaredParameterInUnselectedDialect_DoesNotSkipQuery（sqlite だけの生成。選んでいない sqlserver の SQL の誤りは警告で知らせ、sqlite の実装は生 SQL を実行する）",
                () =>
                    Dialects_OrdersDiagram(
                        DialectNames.Sqlite,
                        new QueryDefinition
                        {
                            Name = "FindByStatus",
                            Implementation = QueryImplementationKind.Sql,
                            Sql =
                            {
                                ["sqlserver"] = "SELECT * FROM Orders WHERE status = @statsu",
                                ["sqlite"] = "SELECT * FROM Orders WHERE status = @status",
                            },
                            Parameters =
                            {
                                new QueryParameter { Name = "status", Type = "string(20)" },
                            },
                        }
                    ),
                Dialects_QueryRepositoryOnlyOptions() with
                {
                    RepositoryDialects = [DialectNames.Sqlite],
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_RawSqlIssue),
                        "@statsu"
                    )
                ),
                typeDialect: DialectNames.Sqlite,
                code: [Calls(implementation, "FindByStatusAsync", "QueryBySqlAsync")]
            ),
            DialectRow(
                "code-query-raw-sql-undeclared-parameter-without-repositories-keeps-contract",
                diagnosticsClass
                    + "UndeclaredParameterWithoutRepositories_KeepsContract（QuickER 版 Repository を生成しない構成では、生 SQL の誤りでクエリを飛ばさず契約を出す）",
                () =>
                    Dialects_OrdersDiagram(
                        new QueryDefinition
                        {
                            Name = "FindByStatus",
                            Implementation = QueryImplementationKind.Sql,
                            Sql = { ["sqlserver"] = "SELECT * FROM Orders WHERE status = @statsu" },
                            Parameters =
                            {
                                new QueryParameter { Name = "status", Type = "string(20)" },
                            },
                        }
                    ),
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateEfCoreRepositories = true,
                    IncludeDataAnnotations = true,
                },
                DiagnosticExpectation.Contains(),
                code: [Member(contract, "FindByStatusAsync")]
            ),
            DialectRow(
                "diag-query-unresolved-parameter-type-not-reported-as-undeclared",
                diagnosticsClass
                    + "UnresolvedParameterType_IsNotAlsoReportedAsUndeclared（型を解決できないパラメータ strng だけを報告し、@status を未宣言として報告しない）",
                () =>
                    Dialects_OrdersDiagram(
                        new QueryDefinition
                        {
                            Name = "FindByStatus",
                            Implementation = QueryImplementationKind.Sql,
                            Sql = { ["sqlserver"] = "SELECT * FROM Orders WHERE status = @status" },
                            Parameters =
                            {
                                new QueryParameter { Name = "status", Type = "strng" },
                            },
                        }
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.ErrorMentioning(
                        nameof(Strings.CodeGen_Query_UnresolvedTypeToken),
                        "strng"
                    ),
                ])
            ),
            DialectRow(
                "diag-query-projection-not-null-override-names-database-column",
                diagnosticsClass
                    + "ProjectionNotNullOverride_NamesDatabaseColumn（フィールド Customer の NULL 許容の診断は DB の列名 customer_id を名指しする）",
                () =>
                    Dialects_OrdersDiagram(order => new QueryDefinition
                    {
                        Name = "Customers",
                        Returns = QueryReturnShape.Projection,
                        Implementation = QueryImplementationKind.Dsl,
                        ResultTypeName = "CustomerRow",
                        Fields =
                        {
                            new ProjectionField
                            {
                                Name = "Customer",
                                SourceColumnId = Dialects_Column(order, "customer_id").Id,
                                IsNullable = false,
                            },
                        },
                    }),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ProjectionFieldNotNullOverride),
                        "Customers",
                        "customer_id"
                    )
                )
            ),
            .. new[] { ("param", "seen_at LIKE @p"), ("literal", "seen_at LIKE '%1'") }.Select(
                pair =>
                    DialectRow(
                        $"diag-query-like-violation-reported-as-like-{pair.Item1}",
                        diagnosticsClass
                            + $"StringMatchViolation_UsesLikeSpelling（{pair.Item2}：日時の列への LIKE の診断は LIKE の名前で報告し、書いていない CONTAINS・ENDSWITH で報告しない）",
                        () =>
                            Dialects_OrdersDiagram(
                                new QueryDefinition
                                {
                                    Name = "BySeen",
                                    Implementation = QueryImplementationKind.Dsl,
                                    Condition = pair.Item2,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "p", Type = "string(10)" },
                                    },
                                }
                            ),
                        Dialects_QueryRepositoryOnlyOptions(),
                        DiagnosticExpectation
                            .Exact([
                                ExpectedDiagnostic.WarningMentioning(
                                    nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                                    "BySeen",
                                    "LIKE"
                                ),
                            ])
                            .Without(
                                ExpectedDiagnostic.WarningMentioning(
                                    nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                                    "CONTAINS"
                                ),
                                ExpectedDiagnostic.WarningMentioning(
                                    nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                                    "ENDSWITH"
                                )
                            )
                    )
            ),
            DialectRow(
                "diag-query-long-literal-truncation-keeps-surrogate-pairs",
                diagnosticsClass
                    + "TruncatedLiteral_DoesNotSplitSurrogatePair（19 文字の後に絵文字を置いた長い文字列リテラルの型の診断。対になっていないサロゲートが無いことは全行に掛かる CodegenRunner.LoneSurrogateProblems）",
                () =>
                    Dialects_OrdersDiagram(
                        new QueryDefinition
                        {
                            Name = "ByQty",
                            Implementation = QueryImplementationKind.Dsl,
                            Condition =
                                $"OrderId = '{new string('a', 19)}{char.ConvertFromUtf32(0x1F600)}tail'",
                        }
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionOperandTypeMismatch),
                        "ByQty"
                    )
                )
            ),
            DialectRow(
                "diag-query-string-match-on-non-string-value-object-skips-query",
                diagnosticsClass
                    + "StringMatchOnNonStringValueObject_IsRejected（PostgreSQL の cidr の値オブジェクトへの CONTAINS は生成前に断る）",
                () =>
                    Dialects_HostsDiagram(
                        "net",
                        nullable: false,
                        "InPrivateRange",
                        "net CONTAINS '10.'"
                    ),
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateEfCoreRepositories = true,
                    IncludeDataAnnotations = true,
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported),
                        "InPrivateRange",
                        "net"
                    )
                ),
                typeDialect: DialectNames.PostgreSql,
                code: [Text_NoTypeDeclaring("InPrivateRangeAsync")]
            ),
            DialectRow(
                "diag-query-in-on-nullable-unknown-value-type-mismatched-list-skips-query",
                "QueryInNullableUnknownValueTypeTests.Generate_InOnNullableUnknownValueType_WithMismatchedList_SkipsQuery（型ファミリが未知の NULL 許容の値型の列〔PostgreSQL の cidr〕への IN は、型の違うリストを警告して飛ばす）",
                () =>
                    Dialects_HostsDiagram(
                        "backup_net",
                        nullable: true,
                        "FindByNets",
                        "backup_net IN @nets",
                        new QueryParameter
                        {
                            Name = "nets",
                            Type = "string",
                            IsList = true,
                        }
                    ),
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateRepositories = false,
                    GenerateEfCoreRepositories = true,
                    GenerateInMemoryRepositories = true,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                },
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionOperandTypeMismatch),
                        "FindByNets",
                        "backup_net"
                    )
                ),
                typeDialect: DialectNames.PostgreSql,
                code: [Text_NoTypeDeclaring("FindByNetsAsync")]
            ),
            // ===== エミッタ：NULL 非許容列の IS NULL・持ち上げ変数とラムダ変数の名前の衝突 =====
            DialectRow(
                "diag-query-null-check-on-non-nullable-column-warns-and-skips",
                "QueryConditionCSharpEmitterTests.Validate_NullCheckOnNonNullableColumn_Fails（NULL 非許容の列 CustomerId への IS NULL は、クエリと列を名指しする警告 1 件でそのクエリだけ飛ばす。列を後から NOT NULL にした図で起きる。対照のクエリは生成されてコンパイルできる）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "NullCustomer",
                                    Returns = QueryReturnShape.List,
                                    Condition = "CustomerId IS NULL",
                                },
                                // 対照：正当なクエリは巻き添えにしない
                                Dialects_ListQuery(
                                    "GetByCustomer",
                                    "CustomerId = @customerId",
                                    [new QueryParameter { Name = "customerId", Type = "int32" }]
                                ),
                            ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Exact([
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "NullCustomer",
                        "CustomerId"
                    ),
                ]),
                compile: true,
                code:
                [
                    Text_NoTypeDeclaring("NullCustomerAsync"),
                    Member(implementation, "GetByCustomerAsync"),
                ]
            ),
            // ===== 条件の検証の失敗：生成全体を止めず、そのクエリだけ警告して飛ばす（未知の列・構文・未宣言のパラメータ・射影） =====
            DialectRow(
                "diag-query-condition-invalid-warns-and-skips-only-that-query",
                "条件の検証に失敗したクエリ（削除した列の名前・構文エラー・未宣言のパラメータ・条件の壊れた射影）は、それぞれを名指しする警告でそのクエリだけ飛ばし、Error は出さず、ほかのクエリは生成されてコンパイルできる。飛ばした射影の DTO の型名 QtyRow は後の正当な射影 ValidRows が使える（型名を返さないと DTO 名の重複の Error で全体が止まる）。飛ばしたクエリが条件なしの全件取得へ化けないことは、メソッドが無いことで見る",
                () =>
                    Dialects_TypeCheckDiagram(order =>
                        [
                            Dialects_ListQuery("ByRemovedColumn", "Nope = 1", []),
                            Dialects_ListQuery("BySyntaxError", "Qty = ", []),
                            Dialects_ListQuery("ByUndeclaredParameter", "Qty = @missing", []),
                            new QueryDefinition
                            {
                                Name = "BrokenRows",
                                Returns = QueryReturnShape.Projection,
                                ResultTypeName = "QtyRow",
                                Condition = "Nope > 1",
                                Fields =
                                {
                                    new ProjectionField
                                    {
                                        Name = "Qty",
                                        SourceColumnId = order
                                            .Columns.Single(column => column.Name == "Qty")
                                            .Id,
                                    },
                                },
                            },
                            // 対照：正当なクエリは巻き添えにしない
                            Dialects_ListQuery(
                                "GetByQty",
                                "Qty = @qty",
                                [new QueryParameter { Name = "qty", Type = "int32" }]
                            ),
                            // 飛ばした射影の型名 QtyRow を後の正当な射影が使える（返さないと DTO 名の重複の Error で全体が止まる）
                            new QueryDefinition
                            {
                                Name = "ValidRows",
                                Returns = QueryReturnShape.Projection,
                                ResultTypeName = "QtyRow",
                                Fields =
                                {
                                    new ProjectionField
                                    {
                                        Name = "Qty",
                                        SourceColumnId = order
                                            .Columns.Single(column => column.Name == "Qty")
                                            .Id,
                                    },
                                },
                            },
                        ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "ByRemovedColumn",
                        "Nope"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "BySyntaxError"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "ByUndeclaredParameter",
                        "missing"
                    ),
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Query_ConditionInvalid),
                        "BrokenRows"
                    )
                ),
                compile: true,
                code:
                [
                    Text_NoTypeDeclaring("ByRemovedColumnAsync"),
                    Text_NoTypeDeclaring("BySyntaxErrorAsync"),
                    Text_NoTypeDeclaring("ByUndeclaredParameterAsync"),
                    Text_NoTypeDeclaring("BrokenRowsAsync"),
                    Member(implementation, "GetByQtyAsync"),
                    Member(implementation, "ValidRowsAsync"),
                ]
            ),
            DialectRow(
                "compile-query-in-lift-variable-renamed-value-object",
                "QueryConditionCSharpEmitterTests.Emit_ValueObjectIn_AvoidsParameterNameCollision（値オブジェクトの列の IN の持ち上げ変数が引数 idsValues と衝突しない＝CS0136 にならずコンパイルできる）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetByIdSet",
                                    Returns = QueryReturnShape.List,
                                    Parameters =
                                    {
                                        new QueryParameter
                                        {
                                            Name = "ids",
                                            Type = "int32",
                                            IsList = true,
                                        },
                                        new QueryParameter { Name = "idsValues", Type = "int32" },
                                    },
                                    Condition = "CustomerId IN @ids OR CustomerId = @idsValues",
                                },
                            ]
                    ),
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                code: [Member(implementation, "GetByIdSetAsync")]
            ),
            DialectRow(
                "compile-query-lambda-variable-avoids-parameter-name",
                "QueryConditionCSharpEmitterTests.Emit_LambdaVariableAvoidsCollision（ラムダの変数が引数 e と衝突しない＝CS0136 にならずコンパイルできる）",
                () =>
                    Dialects_QueryDiagram(
                        Dialects_QueryOrder(),
                        order =>
                            [
                                new QueryDefinition
                                {
                                    Name = "GetByE",
                                    Returns = QueryReturnShape.List,
                                    Parameters =
                                    {
                                        new QueryParameter { Name = "e", Type = "int32" },
                                    },
                                    Condition = "CustomerId = @e",
                                },
                            ]
                    ),
                Dialects_QueryOptions(),
                DiagnosticExpectation.Contains(),
                compile: true,
                code: [Member(implementation, "GetByEAsync")]
            ),
            // ===== 型の検査：コンパイルできない型の組み合わせは警告してそのクエリだけ飛ばす（1 行 1 入力） =====
            .. Dialects_TypeCheckRejections()
                .Select(rejection =>
                    DialectRow(
                        $"diag-query-type-check-rejects-{rejection.Id}",
                        (
                            rejection.ValueObjects
                                ? "QueryConditionTypeCheckTests.Generate_ValueObjectIncompatibleCondition_IsRejected"
                                : "QueryConditionTypeCheckTests.Generate_IncompatibleCondition_IsRejectedAndOnlyThatQuerySkipped"
                        ) + $"（{rejection.Name}: {rejection.Condition}）",
                        () =>
                            Dialects_TypeCheckDiagram(order =>
                                rejection.ValueObjects
                                    ?
                                    [
                                        Dialects_ListQuery(
                                            rejection.Name,
                                            rejection.Condition,
                                            rejection.Parameters(order)
                                        ),
                                    ]
                                    :
                                    [
                                        Dialects_ListQuery(
                                            rejection.Name,
                                            rejection.Condition,
                                            rejection.Parameters(order)
                                        ),
                                        // 対照：正当なクエリは巻き添えにしない
                                        Dialects_ListQuery(
                                            "GetByQty",
                                            "Qty = @qty",
                                            [new QueryParameter { Name = "qty", Type = "int32" }]
                                        ),
                                    ]
                            ),
                        Dialects_QueryRepositoryOnlyOptions() with
                        {
                            GenerateValueObjects = rejection.ValueObjects,
                        },
                        DiagnosticExpectation.Contains(
                            ExpectedDiagnostic.WarningMentioning(rejection.Key, rejection.Name)
                        ),
                        compile: !rejection.ValueObjects,
                        code: rejection.ValueObjects
                            ? [Text_NoTypeDeclaring(rejection.Name + "Async")]
                            :
                            [
                                Text_NoTypeDeclaring(rejection.Name + "Async"),
                                Member(implementation, "GetByQtyAsync"),
                            ]
                    )
                ),
            DialectRow(
                "compile-query-type-check-accepts-widening-forms",
                "QueryConditionTypeCheckTests.Generate_CompatibleConditions_AreAcceptedAndCompile（受理の 3 形：整数列×小数リテラル〔int・long〕・bool の列×boolean の引数・NOT NULL の int 列×int64 のリストの IN。整数列×小数リテラルは総当たりも実行の結果まで見る。残りの 7 形は総当たりが警告 0 のコンパイルと実行で見る）",
                () =>
                    Dialects_TypeCheckDiagram(order =>
                        [
                            Dialects_ListQuery("FracOnInt", "Qty > 1.5", []),
                            Dialects_ListQuery("FracOnLong", "Big > 1.5", []),
                            Dialects_ListQuery(
                                "FlagParam",
                                "Flag = @flag",
                                [new QueryParameter { Name = "flag", Type = "boolean" }]
                            ),
                            Dialects_ListQuery(
                                "WideIn",
                                "Qty IN @bigs",
                                [
                                    new QueryParameter
                                    {
                                        Name = "bigs",
                                        Type = "int64",
                                        IsList = true,
                                    },
                                ]
                            ),
                        ]
                    ),
                Dialects_QueryRepositoryOnlyOptions(),
                DiagnosticExpectation.Contains(),
                compile: true,
                code:
                [
                    Member(implementation, "FracOnIntAsync"),
                    Member(implementation, "FracOnLongAsync"),
                    Member(implementation, "FlagParamAsync"),
                    Member(implementation, "WideInAsync"),
                ]
            ),
            DialectRow(
                "compile-query-in-same-list-on-two-value-object-columns",
                "QueryConditionTypeCheckTests.Generate_SameListOnTwoValueObjectColumns_LiftsTwoVariables（同じリストを値オブジェクトの型が違う 2 列の IN に使うと、持ち上げ変数を別名で宣言する＝CS0128 にならずコンパイルできる）",
                () =>
                    Dialects_TypeCheckDiagram(order =>
                        [
                            Dialects_ListQuery(
                                "SharedList",
                                "Qty IN @ids OR Reorder IN @ids",
                                [
                                    new QueryParameter
                                    {
                                        Name = "ids",
                                        Type = "int32",
                                        IsList = true,
                                    },
                                ]
                            ),
                        ]
                    ),
                Dialects_QueryRepositoryOnlyOptions() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                code: [Member(implementation, "SharedListAsync")]
            ),
            DialectRow(
                "compile-query-in-value-object-widening-list",
                "QueryConditionTypeCheckTests.Generate_ValueObjectInWithWideningList_Compiles（要素型が内包型と違うリスト〔int32 のリスト × bigint の値オブジェクトの列〕の IN と、同じ型のリストの IN がコンパイルできる＝CS0123 にならない）",
                () =>
                    Dialects_TypeCheckDiagram(order =>
                        [
                            Dialects_ListQuery(
                                "WidenedList",
                                "Big IN @ids",
                                [
                                    new QueryParameter
                                    {
                                        Name = "ids",
                                        Type = "int32",
                                        IsList = true,
                                    },
                                ]
                            ),
                            Dialects_ListQuery(
                                "ExactList",
                                "Big IN @bigs",
                                [
                                    new QueryParameter
                                    {
                                        Name = "bigs",
                                        Type = "int64",
                                        IsList = true,
                                    },
                                ]
                            ),
                        ]
                    ),
                Dialects_QueryRepositoryOnlyOptions() with
                {
                    GenerateValueObjects = true,
                },
                DiagnosticExpectation.Contains(),
                compile: true,
                code:
                [
                    Member(implementation, "WidenedListAsync"),
                    Member(implementation, "ExactListAsync"),
                ]
            ),
        ];
    }

    /// <summary>型の検査が断る条件 1 つ（行の Id の後半・クエリ名・条件・パラメータ・値オブジェクトの有無・警告の種類）</summary>
    private sealed record Dialects_TypeCheckRejection(
        string Id,
        string Name,
        string Condition,
        Func<Entity, QueryParameter[]> Parameters,
        bool ValueObjects,
        string Key
    );

    /// <summary>
    /// QueryConditionTypeCheckTests の拒否の 18 行（値オブジェクトなし）と値オブジェクトの拒否の 5 行に、DateTime と DateTimeOffset の
    /// 混在を断る 6 行（値オブジェクトなし 4・あり 2）を足したもの。総当たりの型の検査が断るリテラル・パラメータと重なる行も含めて全部を
    /// 行にする（総当たりは「そのクエリのメソッドが生成物に無い」を見ない）
    /// </summary>
    private static IEnumerable<Dialects_TypeCheckRejection> Dialects_TypeCheckRejections()
    {
        const string mismatch = nameof(Strings.CodeGen_Query_ConditionOperandTypeMismatch);
        const string timeZone = nameof(Strings.CodeGen_Query_ConditionTimeZoneDependentOperand);
        static QueryParameter[] None(Entity _) => [];
        static QueryParameter[] Int32(Entity _) =>
            [new QueryParameter { Name = "num", Type = "int32" }];

        return
        [
            // string 列への順序比較（e.Name > "M" は CS0019）
            new(
                "bad-order",
                "BadOrder",
                "Name > 'M'",
                None,
                false,
                nameof(Strings.CodeGen_Query_ConditionOrderedOperatorUnsupported)
            ),
            // 数値列への文字列一致（int に Contains は無い＝CS1061）
            new(
                "bad-contains",
                "BadContains",
                "Qty CONTAINS '1'",
                None,
                false,
                nameof(Strings.CodeGen_Query_ConditionStringMatchUnsupported)
            ),
            // 日時列への文字列リテラル比較（DateTime == string は CS0019）
            new("bad-date", "BadDate", "SeenAt > '2026-01-01'", None, false, mismatch),
            // bool 列への数値リテラル比較（bool == int は CS0019）
            new("bad-flag", "BadFlag", "Flag = 1", None, false, mismatch),
            // string 列の CONTAINS へ int トークン引数（string.Contains(int) は CS1503）
            new("bad-match-param", "BadMatchParam", "Name CONTAINS @num", Int32, false, mismatch),
            // string 列の IN へ int リスト（Contains の型推論が一致しない）
            new(
                "bad-in",
                "BadIn",
                "Name IN @ids",
                _ =>
                    [
                        new QueryParameter
                        {
                            Name = "ids",
                            Type = "int32",
                            IsList = true,
                        },
                    ],
                false,
                mismatch
            ),
            // NULL 許容 int 列の IN へ long リスト（Cast<int?> の持ち上げが実行時 InvalidCastException）
            new(
                "bad-nullable-in",
                "BadNullableIn",
                "Quantity IN @bigs",
                _ =>
                    [
                        new QueryParameter
                        {
                            Name = "bigs",
                            Type = "int64",
                            IsList = true,
                        },
                    ],
                false,
                mismatch
            ),
            // 日時列 × int トークン引数（DateTime == int は CS0019）
            new("bad-date-param", "BadDateParam", "SeenAt = @num", Int32, false, mismatch),
            // ulong を超える整数リテラル（"…L" も素のリテラルも CS1021）
            new(
                "bad-int-overflow",
                "BadIntOverflow",
                "Big = 99999999999999999999",
                None,
                false,
                mismatch
            ),
            // long を超える整数リテラル（ulong のリテラル）× int 列（int と ulong は比べられない＝CS0034）
            new(
                "bad-ulong-on-int",
                "BadULongOnInt",
                "Qty = 10000000000000000000",
                None,
                false,
                mismatch
            ),
            // long を超える整数リテラル × long 列（"…L" が ulong になり long と比べられない）
            new(
                "bad-ulong-on-long",
                "BadULongOnLong",
                "Big = 9223372036854775808",
                None,
                false,
                mismatch
            ),
            // 列の型の範囲の外の整数リテラル（範囲の両端のすぐ外・符号なしの列の負の値も）
            new("bad-tiny-above", "BadTinyAbove", "Tiny = 256", None, false, mismatch),
            new("bad-tiny-below", "BadTinyBelow", "Tiny <> -1", None, false, mismatch),
            new("bad-int-above", "BadIntAbove", "Qty < 3000000000", None, false, mismatch),
            new("bad-int-below", "BadIntBelow", "Qty >= -2147483649", None, false, mismatch),
            new(
                "bad-long-below",
                "BadLongBelow",
                "Big = -9223372036854775809",
                None,
                false,
                mismatch
            ),
            // decimal の範囲を超えるリテラル（"…m" は CS0594）
            new(
                "bad-decimal-range",
                "BadDecimalRange",
                "Amount = 99999999999999999999999999999999",
                None,
                false,
                mismatch
            ),
            // 値オブジェクトあり：整数内包の値オブジェクトへの小数リテラル・decimal 内包の別の値オブジェクトで型付けした引数・内包型の範囲外
            new("bad-vo-fraction", "BadVoFraction", "Qty = 1.5", None, true, mismatch),
            new(
                "bad-vo-param",
                "BadVoParam",
                "Qty = @amount",
                order =>
                    [
                        new QueryParameter
                        {
                            Name = "amount",
                            SourceColumnId = Dialects_Column(order, "Amount").Id,
                        },
                    ],
                true,
                mismatch
            ),
            new("bad-vo-tiny-range", "BadVoTinyRange", "Tiny < 300", None, true, mismatch),
            new("bad-vo-tiny-negative", "BadVoTinyNegative", "Tiny <> -1", None, true, mismatch),
            new("bad-vo-int-range", "BadVoIntRange", "Qty = 3000000000", None, true, mismatch),
            // DateTime と DateTimeOffset の混在（コンパイルは通るが、C# の暗黙変換が DateTime を実行する機械の時間帯の時差で
            // DateTimeOffset にするので、答えが機械で変わる）。比較の両向き・列で型付けしたパラメータ・IN・値オブジェクト
            new(
                "bad-offset-datetime-param",
                "BadOffsetDateTimeParam",
                "OffsetAt = @at",
                _ => [new QueryParameter { Name = "at", Type = "datetime" }],
                false,
                timeZone
            ),
            new(
                "bad-datetime-offset-param",
                "BadDateTimeOffsetParam",
                "SeenAt < @o",
                _ => [new QueryParameter { Name = "o", Type = "datetimeoffset" }],
                false,
                timeZone
            ),
            new(
                "bad-offset-column-typed-param",
                "BadOffsetColumnTypedParam",
                "OffsetAt >= @seen",
                order =>
                    [
                        new QueryParameter
                        {
                            Name = "seen",
                            SourceColumnId = Dialects_Column(order, "SeenAt").Id,
                        },
                    ],
                false,
                timeZone
            ),
            new(
                "bad-datetime-in-offset-list",
                "BadDateTimeInOffsetList",
                "SeenAt IN @os",
                _ =>
                    [
                        new QueryParameter
                        {
                            Name = "os",
                            Type = "datetimeoffset",
                            IsList = true,
                        },
                    ],
                false,
                timeZone
            ),
            new(
                "bad-vo-offset-datetime-param",
                "BadVoOffsetDateTimeParam",
                "OffsetAt <> @at",
                _ => [new QueryParameter { Name = "at", Type = "datetime" }],
                true,
                timeZone
            ),
            new(
                "bad-vo-offset-in-datetime-list",
                "BadVoOffsetInDateTimeList",
                "OffsetAt IN @ats",
                _ =>
                    [
                        new QueryParameter
                        {
                            Name = "ats",
                            Type = "datetime",
                            IsList = true,
                        },
                    ],
                true,
                timeZone
            ),
        ];
    }

    /// <summary>一覧のクエリ（QueryConditionTypeCheckTests.ListQuery）</summary>
    private static QueryDefinition Dialects_ListQuery(
        string name,
        string condition,
        IEnumerable<QueryParameter> parameters
    ) =>
        new()
        {
            Name = name,
            Returns = QueryReturnShape.List,
            Condition = condition,
            Parameters = [.. parameters],
        };

    /// <summary>QueryConditionTypeCheckTests の型ファミリを網羅する Order（Name・Qty・Big・Amount・SeenAt・OffsetAt・Flag・Quantity〔NULL 許容〕・Tiny・Reorder）の図</summary>
    private static ErDiagram Dialects_TypeCheckDiagram(
        Func<Entity, IEnumerable<QueryDefinition>> queries
    )
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

        foreach (
            var (name, type, nullable) in new[]
            {
                ("Name", "nvarchar(50)", false),
                ("Qty", "int", false),
                ("Big", "bigint", false),
                ("Amount", "decimal(12,2)", false),
                ("SeenAt", "datetime2", false),
                ("OffsetAt", "datetimeoffset", false),
                ("Flag", "bit", false),
                ("Quantity", "int", true),
                ("Tiny", "tinyint", false),
                ("Reorder", "int", false),
            }
        )
        {
            order.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = type,
                    IsNullable = nullable,
                }
            );
        }

        return Dialects_QueryDiagram(order, queries);
    }

    /// <summary>QueryCodegenDiagnosticsTests の Orders（OrderId・status・customer_id〔NULL 許容〕・memo〔NULL 許容〕・seen_at）の図</summary>
    private static ErDiagram Dialects_OrdersDiagram(params QueryDefinition[] queries) =>
        Dialects_OrdersDiagram(null, queries);

    /// <summary>Orders の図（図の方言を指定する）</summary>
    private static ErDiagram Dialects_OrdersDiagram(
        string? targetDbms,
        params QueryDefinition[] queries
    ) => Dialects_OrdersDiagram(targetDbms, _ => queries);

    /// <summary>Orders の図（クエリが列を参照する）</summary>
    private static ErDiagram Dialects_OrdersDiagram(Func<Entity, QueryDefinition> query) =>
        Dialects_OrdersDiagram(null, order => [query(order)]);

    private static ErDiagram Dialects_OrdersDiagram(
        string? targetDbms,
        Func<Entity, IEnumerable<QueryDefinition>> queries
    )
    {
        var order = new Entity { TableName = "Orders" };

        foreach (
            var (name, type, key, nullable) in new[]
            {
                ("OrderId", "int", true, false),
                ("status", "nvarchar(20)", false, false),
                ("customer_id", "int", false, true),
                ("memo", "nvarchar(100)", false, true),
                ("seen_at", "datetime2", false, false),
            }
        )
        {
            order.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = type,
                    IsPrimaryKey = key,
                    IsNullable = nullable,
                }
            );
        }

        var diagram = Dialects_QueryDiagram(order, queries);

        if (targetDbms is not null)
        {
            diagram.TargetDbms = targetDbms;
        }

        return diagram;
    }

    /// <summary>PostgreSQL の hosts（id integer の主キー・cidr の列 1 本）とクエリ 1 つの図</summary>
    private static ErDiagram Dialects_HostsDiagram(
        string column,
        bool nullable,
        string queryName,
        string condition,
        params QueryParameter[] parameters
    )
    {
        var hosts = new Entity { TableName = "hosts" };
        hosts.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        hosts.Columns.Add(
            new Column
            {
                Name = column,
                DataType = "cidr",
                IsNullable = nullable,
            }
        );
        var diagram = Dialects_QueryDiagram(
            hosts,
            _ =>
                [
                    new QueryDefinition
                    {
                        Name = queryName,
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Dsl,
                        Condition = condition,
                        Parameters = [.. parameters],
                    },
                ]
        );
        diagram.TargetDbms = DialectNames.PostgreSql;
        return diagram;
    }

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

    /// <summary>QueryReservedParameterNameTests.CreateDiagram の表：Order（OrderId・Qty〔int〕・NULL 許容の Quantity〔int〕）</summary>
    private static Entity Dialects_ReservedNameOrder()
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
                Name = "Qty",
                DataType = "int",
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );

        return order;
    }

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

                // 主キーにした列は NOT NULL にする（NULL 許容の主キーの読み替えの経路を意図せず通さない。読み替えは図の形 int-nullable の行が見る）
                if (column.IsPrimaryKey)
                {
                    column.IsNullable = false;
                }
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
