using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges.CodeRule;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：名前付きクエリ・双方向同期・重複の事前チェック（QueryGenerationTests・SyncSupportGenerationTests・UniquenessGenerationTests） の、生成テキストの文字列で判定していたテストを、宣言の期待・実行の探針・解析の判定へ言い換えたもの
/// </summary>
/// <remarks>
/// 名前付きクエリの図は <c>CodegenCases.Dialects.cs</c> の <c>Dialects_QueryDiagram</c> など（元の QueryGenerationTests の Order エンティティと同じ）を使う。
/// 同期の行の図・オプションは <see cref="SyncFixtureDefinition"/>（実 <c>quicker generate</c> と同じ経路で、実効方言ごとの型マッパで解決する）。
/// </remarks>
internal static partial class CodegenCases
{
    /// <summary>この主題の行を作る（説明は <paramref name="testClass"/> ＋ "." ＋ 元のメソッド名）</summary>
    private static ScenarioRow<CodegenCase> QueryTextRow(
        string id,
        string testClass,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        IReadOnlyList<CodeRule>? code = null,
        IReadOnlyList<ExecCheck>? exec = null,
        IReadOnlyList<FileRule>? fileRules = null,
        IReadOnlyList<CodegenProbe>? probes = null,
        bool compile = false,
        string typeDialect = DialectNames.SqlServer,
        ReducedReferences? compileWithout = null,
        IReadOnlyList<GeneratedFile>? extraSources = null
    ) =>
        new(
            id,
            null,
            testClass + "." + description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                DiagnosticExpectation.Contains(),
                FileRules: fileRules,
                Compile: compile,
                Probes: probes,
                ExtraSources: extraSources,
                Code: code,
                CompileWithout: compileWithout,
                Exec: exec
            )
        );

    private const string QueryClass = "QueryGenerationTests";
    private const string SyncClass = "SyncSupportGenerationTests";
    private const string UniquenessClass = "UniquenessGenerationTests";

    // ===== 名前付きクエリの図 =====

    /// <summary>QueryGenerationTests.Generate_ProjectionDto_DerivesNullabilityFromColumns の図（生 SQL の射影 5 フィールド）</summary>
    private static ErDiagram QueryText_ProjectionDtoDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetRows",
                        Returns = QueryReturnShape.Projection,
                        ResultTypeName = "OrderRow",
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "SELECT ..." },
                        Fields =
                        {
                            // 列参照 × 非 NULL 列 → 非 NULL の値型
                            new ProjectionField
                            {
                                Name = "CustomerId",
                                SourceColumnId = Dialects_Column(order, "CustomerId").Id,
                            },
                            // 列参照 × NULL 許容列 → NULL 許容の参照型
                            new ProjectionField
                            {
                                Name = "Memo",
                                SourceColumnId = Dialects_Column(order, "Memo").Id,
                            },
                            // 列参照 × 明示 IsNullable=true → 列が非 NULL でも NULL 許容へ上書き
                            new ProjectionField
                            {
                                Name = "OptionalId",
                                SourceColumnId = Dialects_Column(order, "CustomerId").Id,
                                IsNullable = true,
                            },
                            // 自由フィールド → 既定で NULL 許容
                            new ProjectionField { Name = "Total", Type = "decimal(12,2)" },
                            // 自由フィールド × 明示 IsNullable=false → 非 NULL（参照型は null! 初期化）
                            new ProjectionField
                            {
                                Name = "Label",
                                Type = "string(50)",
                                IsNullable = false,
                            },
                        },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_DanglingGuidReferences_WarnAndSkip の図（ダングリング参照 4 本と孤児クエリ）</summary>
    private static ErDiagram QueryText_DanglingDiagram()
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
        diagram.Queries.Add(new QueryDefinition { EntityId = Guid.NewGuid(), Name = "Orphan" });
        return diagram;
    }

    /// <summary>生 SQL の一覧クエリ 1 本だけの図（1 本ごとの違いは名前・パラメータ・SQL）</summary>
    private static ErDiagram QueryText_RawListDiagram(
        string name,
        string sql,
        params QueryParameter[] parameters
    ) =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = name,
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Parameters = [.. parameters],
                        Sql = { ["sqlserver"] = sql },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_RawSqlUndeclaredParameter_WarnsAndSkipsQuery の図（有効な 1 本と、宣言に無い @ghost を使う 1 本）</summary>
    private static ErDiagram QueryText_UndeclaredParameterDiagram() =>
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
                        Sql = { ["sqlserver"] = "SELECT * FROM [Order] WHERE [X] = @ghost" },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_RawSqlWithConditionAndOrder_WarnsIgnored の図</summary>
    private static ErDiagram QueryText_ConditionAndOrderDiagram() =>
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
                            new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
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
        );

    /// <summary>QueryGenerationTests.Generate_ProjectionNotNullOverrideOnNullableValueColumn_SkipsWithWarning の図</summary>
    private static ErDiagram QueryText_ForcedRowsDiagram() =>
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
                    new QueryDefinition { Name = "ListAll", Returns = QueryReturnShape.List },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_SkippedProjectionQuery_ReleasesItsResultTypeName の図</summary>
    private static ErDiagram QueryText_SkippedProjectionDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(withQuantity: true),
            order =>
                [
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
        );

    // ===== 重複の事前チェックの図 =====

    /// <summary>
    /// UniquenessGenerationTests のコンストラクタと CreateDiagram の図（OrderId が主キー・Code が NOT NULL の nvarchar(20)・Memo が NULL 許容の nvarchar(200)）。
    /// <paramref name="constraints"/> は Code・Memo の列から一意制約を作る
    /// </summary>
    private static ErDiagram QueryText_UniquenessDiagram(
        Func<Column, Column, IEnumerable<UniqueConstraint>>? constraints = null,
        bool codeIsKey = false
    )
    {
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = false,
            IsPrimaryKey = codeIsKey,
        };
        var memo = new Column { Name = "Memo", DataType = "nvarchar(200)" };
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
                code,
                memo,
            },
        };

        foreach (var constraint in constraints?.Invoke(code, memo) ?? [])
        {
            order.UniqueConstraints.Add(constraint);
        }

        return new ErDiagram { Entities = { order } };
    }

    /// <summary>UniquenessGenerationTests.CreateOptions：QuickER 版 Repository＋EditModel の標準オプション</summary>
    private static CodeGenerationOptions QueryText_UniquenessOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEditModels = true,
        };

    /// <summary>名前つきの Memo の一意制約と、名前なしの (Code, Memo) の複合一意制約（元の Entity 属性・EditModel の制約テーブルのテスト）</summary>
    private static IEnumerable<UniqueConstraint> QueryText_MemoAndCompositeConstraints(
        Column code,
        Column memo
    ) =>
        [
            new UniqueConstraint { Name = "UQ_Order_Memo", ColumnIds = { memo.Id } },
            new UniqueConstraint { ColumnIds = { code.Id, memo.Id } },
        ];

    // ===== 同期の図 =====

    /// <summary>SyncSupportGenerationTests.SyncTables_CycleDoesNotPullOutsideChildBeforeParent の図（users ⇄ teams が循環し、comments が users を参照する）</summary>
    private static ErDiagram QueryText_SyncCycleDiagram()
    {
        static Entity Table(string name, out Column id)
        {
            id = new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
            return new Entity { TableName = name, Columns = [id] };
        }

        static Column AddForeignKey(Entity entity, string name)
        {
            var column = new Column { Name = name, DataType = "int" };
            entity.Columns.Add(column);
            return column;
        }

        static Relationship Reference(
            Entity parent,
            Column parentKey,
            Entity child,
            Column childKey
        ) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new RelationshipColumnPair(parentKey.Id, childKey.Id)],
            };

        var comments = Table("comments", out _);
        var users = Table("users", out var userId);
        var teams = Table("teams", out var teamId);
        var commentUser = AddForeignKey(comments, "user_id");
        var userTeam = AddForeignKey(users, "team_id");
        var teamLead = AddForeignKey(teams, "lead_user_id");

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = [comments, users, teams],
            Relationships =
            [
                Reference(users, userId, comments, commentUser),
                Reference(teams, teamId, users, userTeam),
                Reference(users, userId, teams, teamLead),
            ],
        };
    }

    /// <summary>SyncSupportGenerationTests.VersionlessEndpointMap_EmittedOnlyWhenAVersionlessTableExists の版なしテーブルを外した図</summary>
    private static ErDiagram QueryText_SyncWithoutVersionlessTableDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();
        var removed = diagram.Entities.Where(e => e.TableName == "sync_notes").ToList();
        diagram.Relationships.RemoveAll(r =>
            removed.Any(e => e.Id == r.SourceEntityId || e.Id == r.TargetEntityId)
        );
        diagram.Entities.RemoveAll(e => e.TableName == "sync_notes");
        return diagram;
    }

    /// <summary>SyncSupportGenerationTests.SyncSupport_AllowsAllVersionlessDiagram の図（rowversion 列を全削除）</summary>
    private static ErDiagram QueryText_SyncAllVersionlessDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();

        foreach (var entity in diagram.Entities)
        {
            entity.Columns.RemoveAll(column => column.DataType == "rowversion");
        }

        return diagram;
    }

    /// <summary>SyncSupportGenerationTests.SyncKeyText_DecimalPrimaryKey_NormalizesScale の図（sync_orders.order_id を decimal(10,2) にする）</summary>
    private static ErDiagram QueryText_SyncDecimalOrderKeyDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram
            .Entities.First(e => e.TableName == "sync_orders")
            .Columns.First(c => c.Name == "order_id")
            .DataType = "decimal(10,2)";
        return diagram;
    }

    /// <summary>
    /// SyncSupportGenerationTests.ParentReferences_CoverSelfReferenceAndSkipWhatCannotBeKeyed の図（自己参照・一意制約の列を参照する外部キー・
    /// 同期対象でない親＝複合主キーへの外部キーを足す）
    /// </summary>
    private static ErDiagram QueryText_SyncParentReferenceDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();
        var order = diagram.Entities.Single(entity => entity.TableName == "sync_orders");
        var note = diagram.Entities.Single(entity => entity.TableName == "sync_notes");
        var orderKey = order.Columns.Single(column => column.IsPrimaryKey);
        var orderName = order.Columns.Single(column => column.Name == "customer_name");

        // 自己参照（NULL 許容）
        var parentOrder = new Column
        {
            Id = Guid.NewGuid(),
            Name = "parent_order_id",
            DataType = "int",
            IsNullable = true,
        };
        order.Columns.Add(parentOrder);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = Guid.NewGuid(),
                SourceEntityId = order.Id,
                TargetEntityId = order.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = { new RelationshipColumnPair(orderKey.Id, parentOrder.Id) },
            }
        );

        // 一意制約の列（主キーでない）を参照する外部キー（同じ親子の 2 本目はナビゲーション名が重なるので別テーブル）
        var tagOwner = new Column
        {
            Id = Guid.NewGuid(),
            Name = "owner_name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var tag = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "sync_tags",
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "tag_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                tagOwner,
            },
        };
        diagram.Entities.Add(tag);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = Guid.NewGuid(),
                SourceEntityId = order.Id,
                TargetEntityId = tag.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = { new RelationshipColumnPair(orderName.Id, tagOwner.Id) },
            }
        );

        // 同期対象でない親（複合主キー＝Repository 契約が無い）への外部キー
        var category = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "categories",
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "region",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "code",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        diagram.Entities.Add(category);
        var noteRegion = new Column
        {
            Id = Guid.NewGuid(),
            Name = "region",
            DataType = "int",
            IsNullable = true,
        };
        var noteCode = new Column
        {
            Id = Guid.NewGuid(),
            Name = "code",
            DataType = "int",
            IsNullable = true,
        };
        note.Columns.Add(noteRegion);
        note.Columns.Add(noteCode);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = Guid.NewGuid(),
                SourceEntityId = category.Id,
                TargetEntityId = note.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                {
                    new RelationshipColumnPair(category.Columns[0].Id, noteRegion.Id),
                    new RelationshipColumnPair(category.Columns[1].Id, noteCode.Id),
                },
            }
        );

        return diagram;
    }

    // ===== 同期の実行の探針の補助（生成物の GeneratedSyncTables の記述子を反射で読む） =====

    /// <summary>生成物の <c>GeneratedSyncTables</c> の記述子 1 つ（プロパティ名は <c>SyncOrder</c> など）</summary>
    private static object QueryText_SyncDescriptor(Assembly assembly, string property) =>
        ProbeKit.GetStatic(ProbeKit.Type(assembly, "GeneratedSyncTables"), property)
        ?? throw new InvalidOperationException($"記述子 {property} が null");

    /// <summary>生成物の <c>GeneratedSyncTables</c> の全部の記述子（プロパティ名と値）</summary>
    private static IReadOnlyList<(string Name, object Descriptor)> QueryText_SyncDescriptors(
        Assembly assembly
    ) =>
        ProbeKit
            .Type(assembly, "GeneratedSyncTables")
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property =>
                property.PropertyType.GetProperty("ReferencingTableNames") is not null
            )
            .Select(property => (property.Name, property.GetValue(null)!))
            .ToList();

    /// <summary>文字列の並びのプロパティを読む</summary>
    private static IReadOnlyList<string> QueryText_Strings(object target, string property) =>
        ((IEnumerable)ProbeKit.Get(target, property)!).Cast<string>().ToList();

    /// <summary>
    /// 記述子の親参照を、<paramref name="entityType"/> の行の <paramref name="property"/> に <paramref name="value"/> を置いて読み、
    /// 親のテーブル名とキーの文字列（親が無ければ null）の組にする
    /// </summary>
    private static IReadOnlyList<(string Parent, string? Key)> QueryText_ParentKeys(
        Assembly assembly,
        object descriptor,
        string entityType,
        string property,
        object? value
    )
    {
        var entity = ProbeKit.Create(ProbeKit.Type(assembly, entityType));
        ProbeKit.Set(entity, property, value);

        return ((IEnumerable)ProbeKit.Get(descriptor, "ParentReferences")!)
            .Cast<object>()
            .Select(reference =>
                (
                    (string)ProbeKit.Get(reference, "ParentTableName")!,
                    (string?)
                        ((Delegate)ProbeKit.Get(reference, "ReadParentKeyText")!).DynamicInvoke(
                            entity
                        )
                )
            )
            .ToList();
    }

    /// <summary>名前が <paramref name="names"/> のどれかを含む型が 1 つも無い（同期の型の不在）</summary>
    private static CodeRule QueryText_NoTypeNamedLike(params string[] names) =>
        NoTypes(
            $"名前に [{string.Join(", ", names)}] のどれかを含む",
            type => names.Any(name => type.Name.Contains(name, StringComparison.Ordinal))
        );

    /// <summary>型の宣言の構文木に、単純名 <paramref name="name"/> の識別子（型・メンバーの参照）が 1 つも現れない</summary>
    private static CodeRule QueryText_NoIdentifierUse(string name) =>
        NoTypes(
            $"{name} を参照する",
            type =>
                type.DeclaringSyntaxReferences.Any(reference =>
                    reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Any(identifier => identifier.Identifier.ValueText == name)
                )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> QueryTextRows() =>
        [
            .. QueryTextDslRows(),
            .. QueryTextUniquenessExecRows(),
            .. QueryTextSyncRows(),
            // ===== QueryGenerationTests =====
            QueryTextRow(
                "code-query-projection-dto-nullability-from-columns",
                QueryClass,
                "Generate_ProjectionDto_DerivesNullabilityFromColumns",
                QueryText_ProjectionDtoDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member(
                        "OrderRow",
                        "CustomerId",
                        "int CustomerId { get; set; }",
                        modifiers: "public"
                    ),
                    Member("OrderRow", "Memo", "string? Memo { get; set; }", modifiers: "public"),
                    Member(
                        "OrderRow",
                        "OptionalId",
                        "int? OptionalId { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderRow",
                        "Total",
                        "decimal? Total { get; set; }",
                        modifiers: "public"
                    ),
                    Member("OrderRow", "Label", "string Label { get; set; }", modifiers: "public"),
                ]
            ),
            QueryTextRow(
                "compile-query-projection-dto-non-null-reference-initializer",
                QueryClass,
                "Generate_ProjectionDto_DerivesNullabilityFromColumns（Label の `= null!` は Nullable 有効の警告ゼロのコンパイルで担保。EF Core × 生 SQL は手動実装＝素のコンパイルが CS0535 のため EF Core を外す）",
                QueryText_ProjectionDtoDiagram,
                Dialects_QueryRepositoryOnlyOptions(),
                compile: true
            ),
            QueryTextRow(
                "code-query-dangling-references-skipped-valid-kept",
                QueryClass,
                "Generate_DanglingGuidReferences_WarnAndSkip",
                QueryText_DanglingDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "ValidQueryAsync"),
                    Text_NoTypeDeclaring("DanglingParameterAsync"),
                    Text_NoTypeDeclaring("DanglingFieldAsync"),
                    Text_NoTypeDeclaring("DanglingOrderByAsync"),
                    Text_NoTypeDeclaring("OrphanAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-blank-sql-contract-only",
                QueryClass,
                "Generate_BlankSql_IsTreatedAsManual",
                () => QueryText_RawListDiagram("GetBlank", "   \r\n  "),
                Dialects_QueryOptions(),
                code:
                [
                    Member(
                        "IOrderRepository",
                        "GetBlankAsync",
                        "Task<IReadOnlyList<OrderEntity>> GetBlankAsync(CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type("OrderRepository", kind: "class"),
                    NoMember("OrderRepository", "GetBlankAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-raw-sql-undeclared-parameter-skips-only-bad",
                QueryClass,
                "Generate_RawSqlUndeclaredParameter_WarnsAndSkipsQuery",
                QueryText_UndeclaredParameterDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "GetGoodAsync"),
                    Text_NoTypeDeclaring("GetBadAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-raw-sql-unused-parameter-still-generated",
                QueryClass,
                "Generate_RawSqlUnusedParameter_WarnsButGenerates",
                () =>
                    QueryText_RawListDiagram(
                        "ListEverything",
                        "SELECT * FROM [Order]",
                        new QueryParameter { Name = "unused", Type = "int32" }
                    ),
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "ListEverythingAsync"),
                    Member("OrderRepository", "ListEverythingAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-raw-sql-multiple-statements-still-generated",
                QueryClass,
                "Generate_RawSqlMultipleStatements_WarnsButGenerates",
                () =>
                    QueryText_RawListDiagram(
                        "GetTwo",
                        "SELECT * FROM [Order]; SELECT * FROM [Order]"
                    ),
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "GetTwoAsync"),
                    Member("OrderRepository", "GetTwoAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-no-queries-contract-has-no-query-members",
                QueryClass,
                "Generate_NoQueries_KeepsContractFreeOfQueryMembers",
                () => Dialects_QueryDiagram(Dialects_QueryOrder(), _ => []),
                Dialects_QueryOptions(),
                code:
                [
                    Type(
                        "IOrderRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<OrderEntity, int>"]
                    ),
                    NoTypes(
                        "IOrderRepository が自分でメンバーを宣言する",
                        type =>
                            type.Name == "IOrderRepository"
                            && type.GetMembers().Any(member => !member.IsImplicitlyDeclared)
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "contract-inherits-check-uniqueness",
                        assembly =>
                        {
                            var contract = ProbeKit.Type(assembly, "IOrderRepository");
                            var found = contract
                                .GetInterfaces()
                                .SelectMany(face => face.GetMethods())
                                .Where(method => method.Name == "CheckUniquenessAsync")
                                .ToList();

                            return found.Count == 1
                                ? []
                                :
                                [
                                    $"継承した CheckUniquenessAsync が {found.Count} 個（期待 1 個）",
                                ];
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-query-raw-sql-with-condition-and-order-still-generated",
                QueryClass,
                "Generate_RawSqlWithConditionAndOrder_WarnsIgnored",
                QueryText_ConditionAndOrderDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "ListBigOrdersAsync"),
                    Member("OrderRepository", "ListBigOrdersAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-not-null-override-on-nullable-value-column-skipped",
                QueryClass,
                "Generate_ProjectionNotNullOverrideOnNullableValueColumn_SkipsWithWarning",
                QueryText_ForcedRowsDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Text_NoTypeDeclaring("GetForcedRowsAsync"),
                    NoType("ForcedRow"),
                    Member("IOrderRepository", "ListAllAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-skipped-projection-releases-result-type-name",
                QueryClass,
                "Generate_SkippedProjectionQuery_ReleasesItsResultTypeName",
                QueryText_SkippedProjectionDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member("IOrderRepository", "ValidAsync"),
                    Text_NoTypeDeclaring("SkippedAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-guidkey-string-match-skips-partial-only",
                QueryClass,
                "Generate_StringMatchOnGuidKeyColumn_SkipsOnlyPartialMatches",
                Dialects_GuidKeyTagDiagram,
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = true,
                    UseGuidKeyForStringPrimaryKey = true,
                },
                code:
                [
                    Member("ITagRepository", "ExactLikeAsync"),
                    Member("ITagRepository", "NotExactLikeAsync"),
                    Member("ITagRepository", "EqualityAsync"),
                    Member("ITagRepository", "OtherColumnStillMatchesAsync"),
                    Text_NoTypeDeclaring("PartialLikeAsync"),
                    Text_NoTypeDeclaring("ContainsMatchAsync"),
                    Text_NoTypeDeclaring("NegatedStartsWithAsync"),
                ]
            ),
            // ===== SyncSupportGenerationTests =====
            QueryTextRow(
                "code-sync-off-emits-no-sync-types",
                SyncClass,
                "SyncSupportOff_EmitsNoSyncTypes",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    GenerateSyncSupport = false,
                },
                code:
                [
                    QueryText_NoTypeNamedLike("SyncEngine", "SyncJournal", "Journaling"),
                    NoType("RemoteSyncOperations"),
                    NoType("HttpSyncServerSource"),
                    Text_NoTypeDeclaring("AddGeneratedSyncSupport"),
                    Text_NoTypeDeclaring("MapSyncEndpoints"),
                    Text_NoTypeDeclaring("RequireSyncSources"),
                    Text_NoTypeDeclaring("AddGeneratedHttpSyncSources"),
                    QueryText_NoIdentifierUse("IServiceProviderIsService"),
                ],
                fileRules:
                [
                    new FileRule(
                        SyncFixtureDefinition.RemoteServerOutputFileName,
                        Count: 1,
                        AnyDirectory: true
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-without-remote-services-has-no-http-transport",
                SyncClass,
                "SyncSupportWithoutRemoteServices_EmitsNoHttpTransport",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    GenerateRemoteServices = false,
                },
                code:
                [
                    Type("SyncEngine", kind: "class", modifiers: "public sealed"),
                    Type(
                        "DirectSyncSource<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Member(
                        "GeneratedSyncServiceCollectionExtensions",
                        "AddGeneratedDirectSyncSources"
                    ),
                    NoType("RemoteSyncOperations"),
                    NoType("RemoteSyncChangesRequest"),
                    NoType("HttpSyncServerSource"),
                    Text_NoTypeDeclaring("AddGeneratedHttpSyncSources"),
                    Text_NoTypeDeclaring("MapSyncEndpoints"),
                ]
            ),
            QueryTextRow(
                "code-sync-on-emits-engine-and-per-entity-code",
                SyncClass,
                "SyncSupportOn_EmitsEngineAndPerEntityCode",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    Type("SyncEngine", kind: "class", modifiers: "public sealed"),
                    Type("SyncJournal", kind: "class", modifiers: "public sealed"),
                    Type("SyncTable<TEntity, TKey>", kind: "class", modifiers: "public sealed"),
                    Type(
                        "DirectSyncSource<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Type("GeneratedSyncTables", kind: "class", modifiers: "public static"),
                    Member(
                        "GeneratedSyncTables",
                        "SyncOrder",
                        "SyncTableDescriptor<SyncOrderEntity, int> SyncOrder { get; }",
                        modifiers: "public static"
                    ),
                    Member(
                        "GeneratedSyncTables",
                        "SyncOrderLine",
                        "SyncTableDescriptor<SyncOrderLineEntity, int> SyncOrderLine { get; }",
                        modifiers: "public static"
                    ),
                    Type(
                        "JournalingSyncOrderRepository",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Type(
                        "JournalingSyncOrderLineRepository",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Member("GeneratedSyncServiceCollectionExtensions", "AddGeneratedSyncSupport"),
                    NoType("SyncOrderSyncTable"),
                    NoType("SyncOrderDirectSyncSource"),
                ]
            ),
            QueryTextRow(
                "probe-sync-key-text-decimal-order-key-scale",
                SyncClass,
                "SyncKeyText_DecimalPrimaryKey_NormalizesScale（sync_orders.order_id を decimal(10,2) にした同じ図で、探針 SyncKeyText が往復・指数表記を使わないこと・NormalizeKeyText が decimal の主キーの表だけにあることを実行で確かめる）",
                QueryText_SyncDecimalOrderKeyDiagram,
                SyncFixtureDefinition.Options,
                probes: [CodegenProbe.SyncKeyText]
            ),
            QueryTextRow(
                "code-sync-cycle-keeps-outside-child-after-parent",
                SyncClass,
                "SyncTables_CycleDoesNotPullOutsideChildBeforeParent",
                QueryText_SyncCycleDiagram,
                SyncFixtureDefinition.Options,
                code:
                [
                    CallOrder(
                        "GeneratedSyncServiceCollectionExtensions",
                        "AddGeneratedSyncEngine",
                        [
                            "AddSyncTable<UserEntity, int, IUserRepository>",
                            "AddSyncTable<CommentEntity, int, ICommentRepository>",
                            "AddSyncTable<TeamEntity, int, ITeamRepository>",
                        ]
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-tables-registered-in-foreign-key-order",
                SyncClass,
                "SyncTables_AreRegisteredInForeignKeyOrder",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    CallOrder(
                        "GeneratedSyncServiceCollectionExtensions",
                        "AddGeneratedSyncEngine",
                        [
                            "AddSyncTable<SyncOrderEntity, int, ISyncOrderRepository>",
                            "AddSyncTable<SyncOrderLineEntity, int, ISyncOrderLineRepository>",
                        ]
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-versionless-endpoint-map-with-versionless-table",
                SyncClass,
                "VersionlessEndpointMap_EmittedOnlyWhenAVersionlessTableExists（版なしテーブルが在る図）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    Member(
                        "GeneratedRemoteEndpoints",
                        "MapVersionlessSyncEndpoints",
                        modifiers: "private static"
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "MapVersionlessSyncEndpoints<SyncNoteEntity, int>",
                        ["group", "\"SyncNote\""]
                    ),
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "MapSyncEndpoints<SyncOrderEntity, int>",
                        count: 1
                    ),
                    Calls(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "MapVersionlessSyncEndpoints<SyncOrderEntity, int>",
                        count: 0
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-versionless-endpoint-map-absent-without-versionless-table",
                SyncClass,
                "VersionlessEndpointMap_EmittedOnlyWhenAVersionlessTableExists（版なしテーブルを外した図）",
                QueryText_SyncWithoutVersionlessTableDiagram,
                SyncFixtureDefinition.Options,
                code:
                [
                    NoMember("GeneratedRemoteEndpoints", "MapVersionlessSyncEndpoints"),
                    Member(
                        "GeneratedRemoteEndpoints",
                        "MapSyncEndpoints",
                        modifiers: "private static"
                    ),
                    CallArguments(
                        "GeneratedRemoteEndpoints",
                        "MapGeneratedRemoteEndpoints",
                        "MapSyncEndpoints<SyncOrderEntity, int>",
                        ["group", "\"SyncOrder\""]
                    ),
                    ReferencesSymbol(
                        "GeneratedRemoteEndpoints",
                        null,
                        "RemoteSyncOperations.Ceiling"
                    ),
                ]
            ),
            QueryTextRow(
                "exec-sync-all-versionless-descriptors",
                SyncClass,
                "SyncSupport_AllowsAllVersionlessDiagram",
                QueryText_SyncAllVersionlessDiagram,
                SyncFixtureDefinition.Options,
                exec:
                [
                    new ExecCheck(
                        "all-descriptors-versionless",
                        assembly =>
                        {
                            var descriptors = QueryText_SyncDescriptors(assembly);
                            var problems = new List<string>();

                            if (descriptors.Count == 0)
                            {
                                problems.Add("記述子が 1 つも無い");
                            }

                            foreach (var (name, descriptor) in descriptors)
                            {
                                if (ProbeKit.Get(descriptor, "IsVersionless") is not true)
                                {
                                    problems.Add($"{name}: IsVersionless が true でない");
                                }

                                foreach (var sql in new[] { "ServerChangesSql", "LocalAnchorSql" })
                                {
                                    if (ProbeKit.Get(descriptor, sql) is not "")
                                    {
                                        problems.Add($"{name}: {sql} が未設定でない");
                                    }
                                }
                            }

                            return problems;
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-decorator-journals-unbounded-binary-writes",
                SyncClass,
                "SyncDecorator_JournalsUnboundedBinaryWrites",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    // 読みは素通し（記録しない）
                    Member(
                        "JournalingSyncOrderRepository",
                        "ReadAttachmentAsync",
                        "Task<bool> ReadAttachmentAsync(int id, Stream destination, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Calls(
                        "JournalingSyncOrderRepository",
                        "ReadAttachmentAsync",
                        "ISyncOrderRemoteRepository.ReadAttachmentAsync",
                        count: 1
                    ),
                    CallArguments(
                        "JournalingSyncOrderRepository",
                        "ReadAttachmentAsync",
                        "ISyncOrderRemoteRepository.ReadAttachmentAsync",
                        ["id", "destination", "cancellationToken"],
                        receiver: "_inner"
                    ),
                    Calls(
                        "JournalingSyncOrderRepository",
                        "ReadAttachmentAsync",
                        "RecordUnboundedBinaryWriteAsync",
                        count: 0
                    ),
                    // 書きは journal-first で包み、その中で実書き込みを呼ぶ
                    Member(
                        "JournalingSyncOrderRepository",
                        "WriteAttachmentAsync",
                        "Task<bool> WriteAttachmentAsync(int id, Stream? source, long? length = null, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Calls(
                        "JournalingSyncOrderRepository",
                        "WriteAttachmentAsync",
                        "RecordUnboundedBinaryWriteAsync",
                        count: 1
                    ),
                    Calls(
                        "JournalingSyncOrderRepository",
                        "WriteAttachmentAsync",
                        "ISyncOrderRemoteRepository.WriteAttachmentAsync",
                        count: 1
                    ),
                    // 実書き込みは記録の呼び出しの引数（ラムダ）の中で、引数をそのまま内側へ渡す
                    CallArguments(
                        "JournalingSyncOrderRepository",
                        "WriteAttachmentAsync",
                        "ISyncOrderRemoteRepository.WriteAttachmentAsync",
                        ["id", "source", "length", "cancellationToken"],
                        receiver: "_inner",
                        within: "RecordUnboundedBinaryWriteAsync"
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-binary-columns-absent-without-excluded-columns",
                SyncClass,
                "SyncBinaryColumns_AreAbsentWithoutExcludedColumns",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options with
                {
                    ExcludeUnboundedBinaryColumns = false,
                },
                code:
                [
                    Type("ISyncBinaryColumns<TKey>", kind: "interface", modifiers: "public"),
                    Text_NoTypeDeclaring("ReadAttachmentAsync"),
                    Text_NoTypeDeclaring("WriteAttachmentAsync"),
                ],
                exec:
                [
                    new ExecCheck(
                        "descriptors-carry-no-unbounded-binary-columns",
                        assembly =>
                            QueryText_SyncDescriptors(assembly)
                                .Where(item =>
                                    QueryText_Strings(
                                        item.Descriptor,
                                        "UnboundedBinaryColumnNames"
                                    ).Count > 0
                                )
                                .Select(item =>
                                    $"{item.Name}: UnboundedBinaryColumnNames が空でない"
                                )
                    ),
                ]
            ),
            QueryTextRow(
                "exec-sync-descriptors-carry-parent-references-and-referencing-tables",
                SyncClass,
                "Descriptors_CarryParentReferencesAndReferencingTables",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                exec:
                [
                    new ExecCheck(
                        "parent-references-and-referencing-tables",
                        assembly =>
                        {
                            var problems = new List<string>();
                            var line = QueryText_SyncDescriptor(assembly, "SyncOrderLine");
                            var order = QueryText_SyncDescriptor(assembly, "SyncOrder");

                            var lineKeys = QueryText_ParentKeys(
                                assembly,
                                line,
                                "SyncOrderLineEntity",
                                "OrderId",
                                5
                            );

                            if (!lineKeys.SequenceEqual([("sync_orders", "5")]))
                            {
                                problems.Add(
                                    $"SyncOrderLine の親参照が [{string.Join(", ", lineKeys)}]（期待 [(sync_orders, 5)]）"
                                );
                            }

                            if (QueryText_Strings(line, "ReferencingTableNames").Count != 0)
                            {
                                problems.Add("SyncOrderLine の ReferencingTableNames が空でない");
                            }

                            if (
                                ((IEnumerable)ProbeKit.Get(order, "ParentReferences")!)
                                    .Cast<object>()
                                    .Any()
                            )
                            {
                                problems.Add("SyncOrder の ParentReferences が空でない");
                            }

                            var referencing = QueryText_Strings(order, "ReferencingTableNames");

                            if (!referencing.SequenceEqual(["sync_order_lines", "sync_notes"]))
                            {
                                problems.Add(
                                    $"SyncOrder の ReferencingTableNames が [{string.Join(", ", referencing)}]"
                                );
                            }

                            if (
                                QueryText_Strings(order, "UnlistedReferencingTableNames").Count != 0
                            )
                            {
                                problems.Add(
                                    "SyncOrder の UnlistedReferencingTableNames が空でない"
                                );
                            }

                            return problems;
                        }
                    ),
                ]
            ),
            // 元の図は主キーが NULL 許容だったが、その形は今の製品では同期つきの生成物がコンパイルできない（製品の候補・別の区切りで直す）。
            // 生成物を動かして確かめるために主キーを NOT NULL にした。表明の中身（自己参照・キーにできないものを飛ばす）は元と同じ
            QueryTextRow(
                "exec-sync-parent-references-self-reference-and-unkeyable-skipped",
                SyncClass,
                "ParentReferences_CoverSelfReferenceAndSkipWhatCannotBeKeyed",
                QueryText_SyncParentReferenceDiagram,
                SyncFixtureDefinition.Options,
                exec:
                [
                    new ExecCheck(
                        "self-reference-and-skipped-references",
                        assembly =>
                        {
                            var problems = new List<string>();
                            var order = QueryText_SyncDescriptor(assembly, "SyncOrder");
                            var note = QueryText_SyncDescriptor(assembly, "SyncNote");
                            var tag = QueryText_SyncDescriptor(assembly, "SyncTag");

                            var selfKeys = QueryText_ParentKeys(
                                assembly,
                                order,
                                "SyncOrderEntity",
                                "ParentOrderId",
                                7
                            );

                            if (!selfKeys.Contains(("sync_orders", "7")))
                            {
                                problems.Add(
                                    $"SyncOrder の親参照に自己参照が無い [{string.Join(", ", selfKeys)}]"
                                );
                            }

                            var noSelf = QueryText_ParentKeys(
                                assembly,
                                order,
                                "SyncOrderEntity",
                                "ParentOrderId",
                                null
                            );

                            if (!noSelf.Contains(("sync_orders", (string?)null)))
                            {
                                problems.Add("ParentOrderId が null の行の親のキーが null でない");
                            }

                            var referencing = QueryText_Strings(order, "ReferencingTableNames");

                            if (
                                !referencing.SequenceEqual([
                                    "sync_order_lines",
                                    "sync_notes",
                                    "sync_orders",
                                    "sync_tags",
                                ])
                            )
                            {
                                problems.Add(
                                    $"SyncOrder の ReferencingTableNames が [{string.Join(", ", referencing)}]"
                                );
                            }

                            var unlisted = QueryText_Strings(
                                order,
                                "UnlistedReferencingTableNames"
                            );

                            if (!unlisted.SequenceEqual(["sync_tags"]))
                            {
                                problems.Add(
                                    $"SyncOrder の UnlistedReferencingTableNames が [{string.Join(", ", unlisted)}]"
                                );
                            }

                            var localTables = ((IEnumerable)ProbeKit.Get(order, "LocalReferences")!)
                                .Cast<object>()
                                .Select(reference => (string)ProbeKit.Get(reference, "TableName")!)
                                .ToList();

                            if (localTables.Contains("sync_tags"))
                            {
                                problems.Add("LocalReferences に sync_tags がある");
                            }

                            var noteKeys = QueryText_ParentKeys(
                                assembly,
                                note,
                                "SyncNoteEntity",
                                "Region",
                                1
                            );

                            if (
                                noteKeys.Count != 1
                                || noteKeys.Any(item => item.Parent == "categories")
                            )
                            {
                                problems.Add(
                                    $"SyncNote の親参照が [{string.Join(", ", noteKeys)}]（主キーを単一列で参照する注文だけの 1 件）"
                                );
                            }

                            if (
                                ((IEnumerable)ProbeKit.Get(tag, "ParentReferences")!)
                                    .Cast<object>()
                                    .Any()
                            )
                            {
                                problems.Add("SyncTag の ParentReferences が空でない");
                            }

                            return problems;
                        }
                    ),
                ]
            ),
            // ===== UniquenessGenerationTests =====
            QueryTextRow(
                "code-uniqueness-no-constraints-keeps-method-and-hook",
                UniquenessClass,
                "Generate_NoConstraints_StillEmitsMethodAndHook",
                () => QueryText_UniquenessDiagram(),
                QueryText_UniquenessOptions(),
                code:
                [
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "CheckUniquenessAsync",
                        "Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Member(
                        "OrderRepository",
                        "CollectCustomUniquenessChecks",
                        modifiers: "partial"
                    ),
                    NoType("OrderUniquenessConstraints"),
                    NoMember("OrderRepository", "UniquenessConstraints"),
                    Calls(
                        "SqlServerRepositoryCore<TEntity, TKey>",
                        "CheckUniquenessAsync",
                        "UniquenessChecker.RunCustomChecksAsync",
                        count: 1
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-contract-inherited-without-remote-contracts",
                UniquenessClass,
                "Generate_WithoutRemoteContracts_InheritsContractFromCommonFace",
                () => QueryText_UniquenessDiagram(),
                QueryText_UniquenessOptions(),
                code:
                [
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "CheckUniquenessAsync",
                        "Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type(
                        "IOrderRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<OrderEntity, int>"]
                    ),
                    NoMember("IOrderRepository", "CheckUniquenessAsync"),
                    NoType("IOrderRemoteRepository"),
                ],
                exec:
                [
                    new ExecCheck(
                        "validate-unique-takes-full-face",
                        assembly => QueryText_ValidateUniqueFace(assembly, "IOrderRepository")
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-contract-stays-on-common-face-with-remote-contracts",
                UniquenessClass,
                "Generate_WithRemoteContracts_KeepsContractOnCommonFace",
                () => QueryText_UniquenessDiagram(),
                QueryText_UniquenessOptions() with
                {
                    GenerateRemoteContracts = true,
                },
                code:
                [
                    Member(
                        "IRemoteRepositoryCore<TEntity, TKey>",
                        "CheckUniquenessAsync",
                        "Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken))"
                    ),
                    Type(
                        "IOrderRemoteRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRemoteRepository<OrderEntity, int>"]
                    ),
                    NoMember("IOrderRemoteRepository", "CheckUniquenessAsync"),
                    NoMember("IOrderRepository", "CheckUniquenessAsync"),
                    Type(
                        "IOrderRepository",
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IOrderRemoteRepository", "IRepository<OrderEntity, int>"]
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "validate-unique-takes-remote-face",
                        assembly => QueryText_ValidateUniqueFace(assembly, "IOrderRemoteRepository")
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-entity-carries-unique-constraint-attributes",
                UniquenessClass,
                "Generate_Entity_EmitsUniqueConstraintAttributes",
                () => QueryText_UniquenessDiagram(QueryText_MemoAndCompositeConstraints),
                QueryText_UniquenessOptions(),
                code:
                [
                    Type("OrderEntity", kind: "class", modifiers: "public partial"),
                    Attribute(
                        "OrderEntity",
                        null,
                        "UniqueConstraint([\"Memo\"], Name = \"UQ_Order_Memo\")"
                    ),
                    Attribute(
                        "OrderEntity",
                        null,
                        "UniqueConstraint([\"Code\", \"Memo\"], Name = \"UQ_Order_Code_Memo\")"
                    ),
                    NoAttribute("OrderEditModel", null, "UniqueConstraint"),
                    XmlDoc(
                        "OrderEditModel",
                        null,
                        "summary",
                        "Edit model for on-screen editing of the Order table."
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "unique-constraint-attributes-in-declaration-order",
                        assembly =>
                        {
                            var actual = ProbeKit
                                .Type(assembly, "OrderEntity")
                                .CustomAttributes.Where(data =>
                                    data.AttributeType.Name == "UniqueConstraintAttribute"
                                )
                                .Select(data =>
                                    string.Join(
                                        ",",
                                        (
                                            (IEnumerable<CustomAttributeTypedArgument>)
                                                data.ConstructorArguments[0].Value!
                                        ).Select(argument => argument.Value)
                                    )
                                    + "|"
                                    + data.NamedArguments.Single(named =>
                                        named.MemberName == "Name"
                                    ).TypedValue.Value
                                )
                                .ToList();
                            string[] expected =
                            [
                                "Memo|UQ_Order_Memo",
                                "Code,Memo|UQ_Order_Code_Memo",
                            ];

                            return actual.SequenceEqual(expected)
                                ? []
                                : [$"属性の並びが [{string.Join("; ", actual)}]"];
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-no-constraints-omits-attribute-definition",
                UniquenessClass,
                "Generate_NoConstraints_OmitsAttributeDefinition",
                () => QueryText_UniquenessDiagram(),
                QueryText_UniquenessOptions(),
                code: [NoType("UniqueConstraintAttribute")]
            ),
            QueryTextRow(
                "code-uniqueness-edit-model-emits-constraints-table",
                UniquenessClass,
                "Generate_EditModel_EmitsUniquenessConstraintsTable",
                () => QueryText_UniquenessDiagram(QueryText_MemoAndCompositeConstraints),
                QueryText_UniquenessOptions(),
                code:
                [
                    Member(
                        "OrderEditModel",
                        "_uniquenessConstraints",
                        "IReadOnlyList<EditModelUniquenessConstraint> _uniquenessConstraints",
                        modifiers: "private static readonly"
                    ),
                    Member(
                        "OrderEditModel",
                        "UniquenessConstraints",
                        "IReadOnlyList<EditModelUniquenessConstraint> UniquenessConstraints { get; }",
                        modifiers: "public override"
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "constraints-table-names-members-and-values",
                        assembly =>
                        {
                            var model = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEditModel"));
                            ProbeKit.Set(model, "Memo", "m");
                            ProbeKit.Set(model, "Code", "c");
                            var constraints = (
                                (IEnumerable)ProbeKit.Get(model, "UniquenessConstraints")!
                            )
                                .Cast<object>()
                                .Select(constraint =>
                                    (
                                        Name: (string)ProbeKit.Get(constraint, "ConstraintName")!,
                                        Members: QueryText_Strings(constraint, "PropertyNames"),
                                        Values: (
                                            (object?[])
                                                (
                                                    (Delegate)ProbeKit.Get(constraint, "GetValues")!
                                                ).DynamicInvoke(model)!
                                        )
                                            .Select(value => (string?)value)
                                            .ToList()
                                    )
                                )
                                .ToList();
                            var actual = constraints.Select(item =>
                                $"{item.Name}:{string.Join(",", item.Members)}:{string.Join(",", item.Values)}"
                            );
                            string[] expected =
                            [
                                "UQ_Order_Memo:Memo:m",
                                "UQ_Order_Code_Memo:Code,Memo:c,m",
                            ];

                            return actual.SequenceEqual(expected)
                                ? []
                                : [$"制約テーブルが [{string.Join("; ", actual)}]"];
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-edit-model-without-constraints-omits-table",
                UniquenessClass,
                "Generate_EditModelWithoutConstraints_OmitsConstraintsTable",
                () => QueryText_UniquenessDiagram(),
                QueryText_UniquenessOptions(),
                code:
                [
                    Type("OrderEditModel", kind: "class"),
                    NoMember("OrderEditModel", "_uniquenessConstraints"),
                    NoMember("OrderEditModel", "UniquenessConstraints"),
                ],
                exec:
                [
                    new ExecCheck(
                        "constraints-table-is-empty",
                        assembly =>
                        {
                            var model = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEditModel"));
                            var count = ((IEnumerable)ProbeKit.Get(model, "UniquenessConstraints")!)
                                .Cast<object>()
                                .Count();

                            return count == 0 ? [] : [$"制約テーブルが {count} 件（期待 0 件）"];
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-without-repository-contract-omits-validate-unique",
                UniquenessClass,
                "Generate_WithoutRepositoryContract_OmitsValidateUniqueAsync",
                () => QueryText_UniquenessDiagram(),
                new CodeGenerationOptions { RootNamespace = "Test.Ns", GenerateEditModels = true },
                code:
                [
                    Type("OrderEditModel", kind: "class"),
                    Text_NoTypeDeclaring("ValidateUniqueAsync"),
                    Type("EditModelUniquenessValidator", kind: "class"),
                ]
            ),
            QueryTextRow(
                "code-uniqueness-composite-key-omits-validate-unique",
                UniquenessClass,
                "Generate_CompositeKey_OmitsValidateUniqueAsync",
                () => QueryText_UniquenessDiagram(codeIsKey: true),
                QueryText_UniquenessOptions(),
                code:
                [
                    Type("OrderEditModel", kind: "class"),
                    Text_NoTypeDeclaring("ValidateUniqueAsync"),
                ]
            ),
        ];

    /// <summary>EditModel の <c>ValidateUniqueAsync</c> の最初の引数が、契約の面 <paramref name="face"/> である</summary>
    private static IEnumerable<string> QueryText_ValidateUniqueFace(Assembly assembly, string face)
    {
        var methods = ProbeKit
            .Type(assembly, "OrderEditModel")
            .GetMethods()
            .Where(method => method.Name == "ValidateUniqueAsync")
            .ToList();

        if (methods.Count != 1)
        {
            return [$"ValidateUniqueAsync が {methods.Count} 個（期待 1 個）"];
        }

        var first = methods[0].GetParameters()[0].ParameterType.Name;
        return first == face ? [] : [$"ValidateUniqueAsync の第 1 引数が {first}（期待 {face}）"];
    }

    // ===== QueryGenerationTests：宣言・SQLite の実行へ移した分（条件の訳し方の文は P3-6〔簡易 DSL〕の領分で、元のメソッドに文のまま残す） =====

    private const string RepositoryContract = "IOrderRepository";

    private const string CancellationParameter =
        "CancellationToken cancellationToken = default(CancellationToken)";

    /// <summary>SQLite 方言の QuickER 版 Repository だけを出す標準オプション（生 SQL の実行の行）</summary>
    private static CodeGenerationOptions QueryText_SqliteOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            RepositoryDialects = [DialectNames.Sqlite],
            IncludeDataAnnotations = true,
        };

    /// <summary>QueryGenerationTests.Generate_DslListQuery_EmitsSharedBody の図</summary>
    private static ErDiagram QueryText_DslListDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetByCustomer",
                        Description = "顧客IDで注文を検索する",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int32" },
                        },
                        Condition = "CustomerId = @customerId",
                        OrderBy =
                        {
                            new QueryOrdering
                            {
                                ColumnId = Dialects_Column(order, "Amount").Id,
                                Descending = true,
                            },
                        },
                        HasPaging = true,
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_DslShapes_EmitBodies の図（単一・件数・文字列一致・IN）</summary>
    private static ErDiagram QueryText_DslShapesDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "FindTop",
                        Returns = QueryReturnShape.Single,
                        OrderBy =
                        {
                            new QueryOrdering
                            {
                                ColumnId = Dialects_Column(order, "Amount").Id,
                                Descending = true,
                            },
                        },
                    },
                    new QueryDefinition
                    {
                        Name = "CountLarge",
                        Returns = QueryReturnShape.Count,
                        Parameters =
                        {
                            new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                        },
                        Condition = "Amount >= @minAmount",
                    },
                    new QueryDefinition
                    {
                        Name = "SearchMemo",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter { Name = "keyword", Type = "string(50)" },
                        },
                        Condition = "Memo LIKE @keyword",
                    },
                    new QueryDefinition
                    {
                        Name = "GetByIds",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter
                            {
                                Name = "ids",
                                Type = "int32",
                                IsList = true,
                            },
                        },
                        Condition = "OrderId IN @ids",
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_DslProjection_EmitsDtoAndSelector の図（CustomerId・Amount の射影）</summary>
    private static ErDiagram QueryText_DslProjectionDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetSummaries",
                        Returns = QueryReturnShape.Projection,
                        ResultTypeName = "OrderSummaryRow",
                        Parameters =
                        {
                            new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                        },
                        Condition = "Amount >= @minAmount",
                        Fields =
                        {
                            new ProjectionField
                            {
                                Name = "CustomerId",
                                Type = "int32",
                                SourceColumnId = Dialects_Column(order, "CustomerId").Id,
                            },
                            new ProjectionField
                            {
                                Name = "Amount",
                                Type = "decimal(12,2)",
                                SourceColumnId = Dialects_Column(order, "Amount").Id,
                            },
                        },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_SqlQuery_AdoOnlyAndEfManual の図（生 SQL の一覧・スカラーと手動実装）</summary>
    private static ErDiagram QueryText_SqlAndManualDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetRecent",
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Sql =
                        {
                            ["sqlserver"] = "SELECT * FROM [Order] WHERE [Amount] > 0",
                            ["sqlite"] = "SELECT * FROM \"Order\" WHERE \"Amount\" > 0",
                        },
                    },
                    new QueryDefinition
                    {
                        Name = "SumAmount",
                        Returns = QueryReturnShape.Scalar,
                        ScalarType = "decimal(12,2)",
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "SELECT SUM([Amount]) FROM [Order]" },
                    },
                    new QueryDefinition
                    {
                        Name = "SpecialLookup",
                        Returns = QueryReturnShape.Single,
                        Implementation = QueryImplementationKind.Manual,
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_MultiTarget_DispatchesByDialect の図（共有の DSL と sqlserver 限定の生 SQL）</summary>
    private static ErDiagram QueryText_MultiTargetDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetByCustomer",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int32" },
                        },
                        Condition = "CustomerId = @customerId",
                    },
                    new QueryDefinition
                    {
                        Name = "ServerOnly",
                        Returns = QueryReturnShape.Count,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "SELECT COUNT(*) FROM [Order]" },
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_ColumnTypedParameter_FollowsColumnType の図（列参照で型付けしたパラメータ）</summary>
    private static ErDiagram QueryText_ColumnTypedDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetByCustomerTyped",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter
                            {
                                Name = "customerId",
                                SourceColumnId = Dialects_Column(order, "CustomerId").Id,
                            },
                        },
                        Condition = "CustomerId = @customerId",
                    },
                ]
        );

    /// <summary>QueryGenerationTests.Generate_InOnNullableValueTypeColumn_LiftsToNullableList の図（NULL 許容の int 列 Quantity への IN）</summary>
    private static ErDiagram QueryText_InOnNullableDiagram() =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(withQuantity: true),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "GetByQuantities",
                        Returns = QueryReturnShape.List,
                        Parameters =
                        {
                            new QueryParameter
                            {
                                Name = "quantities",
                                Type = "int32",
                                IsList = true,
                            },
                        },
                        Condition = "Quantity IN @quantities",
                    },
                ]
        );

    /// <summary>生 SQL（sqlserver と sqlite に同じ文）の 1 つのクエリを持つ図（SQLite の実行の行。元のテストの SQL 文は sqlserver だけに書いていた）</summary>
    private static ErDiagram QueryText_RawSqlDiagram(
        string name,
        QueryReturnShape returns,
        string sql,
        params QueryParameter[] parameters
    ) =>
        Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = name,
                        Returns = returns,
                        Implementation = QueryImplementationKind.Sql,
                        Parameters = [.. parameters],
                        Sql = { ["sqlserver"] = sql, ["sqlite"] = sql },
                    },
                ]
        );

    /// <summary>生成物の Order の行を作る（OrderId・Memo・CustomerId を指定。Amount は 1）</summary>
    private static object QueryText_NewOrder(
        Assembly assembly,
        int orderId,
        string? memo,
        int customerId = 1
    )
    {
        var order = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEntity"));
        ProbeKit.Set(order, "OrderId", orderId);
        ProbeKit.Set(order, "CustomerId", customerId);
        ProbeKit.Set(order, "Amount", 1m);
        ProbeKit.Set(order, "Memo", memo);
        return order;
    }

    /// <summary>DI から引いた Order の Repository へ行を挿入する</summary>
    private static async Task QueryText_InsertOrdersAsync(
        Assembly assembly,
        IServiceProvider provider,
        params object[] orders
    )
    {
        var repository = provider.GetRequiredService(ProbeKit.Type(assembly, RepositoryContract));

        foreach (var order in orders)
        {
            await (Task)ProbeKit.Call(repository, "InsertAsync", order, CancellationToken.None)!;
        }
    }

    /// <summary>生成物の Order の Repository のクエリのメソッド（<paramref name="arguments"/> の後ろに取り消しを渡す）を呼んで待つ</summary>
    private static object? QueryText_CallQuery(
        Assembly assembly,
        IServiceProvider provider,
        string method,
        params object?[] arguments
    ) =>
        ProbeKit.Await(
            ProbeKit.Call(
                provider.GetRequiredService(ProbeKit.Type(assembly, RepositoryContract)),
                method,
                [.. arguments, CancellationToken.None]
            )
        );

    /// <summary>クエリの結果（Order の列）の OrderId を並べる</summary>
    private static IReadOnlyList<int> QueryText_OrderIds(object? result) =>
        ((IEnumerable)result!)
            .Cast<object>()
            .Select(row => (int)ProbeKit.Get(row, "OrderId")!)
            .ToList();

    /// <summary>
    /// 生 SQL のクエリを SQLite で実行し、返った Order の OrderId の並びが <paramref name="expected"/> と一致する
    /// （挿入する行は <paramref name="orders"/> の（OrderId, Memo））
    /// </summary>
    private static ExecCheck QueryText_SqliteRawQuery(
        string name,
        Func<ErDiagram> diagram,
        (int OrderId, string? Memo)[] orders,
        string method,
        object?[] arguments,
        int[] expected
    ) =>
        new(
            name,
            assembly =>
                ProbeKit.WithSqlite(
                    assembly,
                    diagram(),
                    async (provider, _) =>
                    {
                        await QueryText_InsertOrdersAsync(
                            assembly,
                            provider,
                            [
                                .. orders.Select(order =>
                                    QueryText_NewOrder(assembly, order.OrderId, order.Memo)
                                ),
                            ]
                        );
                        var actual = QueryText_OrderIds(
                            QueryText_CallQuery(assembly, provider, method, arguments)
                        );

                        return actual.SequenceEqual(expected)
                            ? []
                            :
                            [
                                $"{method} の結果が [{string.Join(",", actual)}]（期待 [{string.Join(",", expected)}]）",
                            ];
                    }
                )
        );

    /// <summary>改行・行区切り・段落区切り・FF・NEL・CR を含む Memo の値（QueryGenerationTests.Generate_RawSqlWithLineBreaks_KeepsExactValue の SQL の文字列リテラルの中身）</summary>
    private static readonly string QueryText_LineBreakMemo =
        $"a{(char)0x2028}b{(char)0x2029}c\f{(char)0x85}d\r";

    /// <summary>QueryGenerationTests.Generate_RawSqlWithLineBreaks_KeepsExactValue の図（複数行の生 SQL 2 本と 1 行の生 SQL）</summary>
    private static ErDiagram QueryText_LineBreaksDiagram()
    {
        var multiLine =
            "SELECT *\r\nFROM [Order]\nWHERE [Memo] = '" + QueryText_LineBreakMemo + "'\r\n\r\n";
        var singleLine = "SELECT * FROM [Order] WHERE [Memo] = \"q\"";

        // SQLite は二重引用符を識別子として読む（Microsoft.Data.Sqlite は二重引用符の文字列リテラルを受け付けない）ので、SQLite の SQL だけ単一引用符にする
        var sqliteSingleLine = "SELECT * FROM [Order] WHERE [Memo] = 'q'";

        return Dialects_QueryDiagram(
            Dialects_QueryOrder(),
            order =>
                [
                    new QueryDefinition
                    {
                        Name = "FindMultiLine",
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = multiLine, ["sqlite"] = multiLine },
                    },
                    new QueryDefinition
                    {
                        Name = "CountMultiLine",
                        Returns = QueryReturnShape.Count,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "\n" + multiLine, ["sqlite"] = "\n" + multiLine },
                    },
                    new QueryDefinition
                    {
                        Name = "FindSingleLine",
                        Returns = QueryReturnShape.List,
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = singleLine, ["sqlite"] = sqliteSingleLine },
                    },
                ]
        );
    }

    /// <summary>契約・QuickER 版・EF Core 版のそれぞれに載るメンバーの宣言（契約は修飾子を見ず、実装は public）</summary>
    private static IEnumerable<CodeRule> QueryText_OnAll(
        string member,
        string signature,
        params string[] types
    ) =>
        types.Select(type =>
            Member(type, member, signature, modifiers: type == RepositoryContract ? null : "public")
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> QueryTextDslRows()
    {
        const string contract = RepositoryContract;
        const string implementation = "OrderRepository";
        const string efCore = "EfCoreOrderRepository";
        const string listOfOrders = "Task<IReadOnlyList<OrderEntity>>";

        return
        [
            QueryTextRow(
                "code-query-dsl-list-contract-and-both-implementations",
                QueryClass,
                "Generate_DslListQuery_EmitsSharedBody（契約のブロック展開・3 つの宣言・説明の XmlDoc。共有本体の文は P3-6〔簡易 DSL〕の領分で元に残す）",
                QueryText_DslListDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Type(
                        contract,
                        kind: "interface",
                        modifiers: "public partial",
                        interfaces: ["IRepository<OrderEntity, int>"]
                    ),
                    .. QueryText_OnAll(
                        "GetByCustomerAsync",
                        $"{listOfOrders} GetByCustomerAsync(int customerId, int take, int skip = 0, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                    XmlDocOwners(
                        "summary",
                        "顧客IDで注文を検索する",
                        [
                            $"{contract}.GetByCustomerAsync",
                            $"{implementation}.GetByCustomerAsync",
                            $"{efCore}.GetByCustomerAsync",
                        ]
                    ),
                ]
            ),
            QueryTextRow(
                "code-query-dsl-shapes-declared-on-contract-and-implementations",
                QueryClass,
                "Generate_DslShapes_EmitBodies（単一・件数・文字列一致・IN の宣言。本体の文は P3-6〔簡易 DSL〕の領分で元に残す）",
                QueryText_DslShapesDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    .. QueryText_OnAll(
                        "FindTopAsync",
                        $"Task<OrderEntity?> FindTopAsync({CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                    .. QueryText_OnAll(
                        "CountLargeAsync",
                        $"Task<int> CountLargeAsync(decimal minAmount, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                    .. QueryText_OnAll(
                        "SearchMemoAsync",
                        $"{listOfOrders} SearchMemoAsync(string keyword, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                    .. QueryText_OnAll(
                        "GetByIdsAsync",
                        $"{listOfOrders} GetByIdsAsync(IReadOnlyList<int> ids, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                ]
            ),
            QueryTextRow(
                "exec-query-dsl-projection-dto-and-hoisted-selector",
                QueryClass,
                "Generate_DslProjection_EmitsDtoAndSelector",
                QueryText_DslProjectionDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Type("OrderSummaryRow", kind: "class", modifiers: "public sealed partial"),
                    Member(
                        "OrderSummaryRow",
                        "CustomerId",
                        "int CustomerId { get; set; }",
                        modifiers: "public"
                    ),
                    Member(
                        "OrderSummaryRow",
                        "Amount",
                        "decimal Amount { get; set; }",
                        modifiers: "public"
                    ),
                    .. new[] { implementation, efCore }.SelectMany(type =>
                        new CodeRule[]
                        {
                            Member(
                                type,
                                "_getSummariesAsyncSelector",
                                "Expression<Func<OrderEntity, OrderSummaryRow>> _getSummariesAsyncSelector",
                                modifiers: "private static readonly"
                            ),
                            // 射影終端へは巻き上げたフィールドを渡す（選択式の直書きが残らない）
                            CallArguments(
                                type,
                                "GetSummariesAsync",
                                "ToProjectionListAsync",
                                [$"{type}._getSummariesAsyncSelector", "cancellationToken"]
                            ),
                        }
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "projection-selector-copies-the-two-columns",
                        assembly =>
                        {
                            var problems = new List<string>();

                            foreach (var type in new[] { implementation, efCore })
                            {
                                var selector = (System.Linq.Expressions.LambdaExpression)
                                    ProbeKit.GetStaticField(
                                        ProbeKit.Type(assembly, type),
                                        "_getSummariesAsyncSelector"
                                    );
                                var order = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEntity"));
                                ProbeKit.Set(order, "CustomerId", 7);
                                ProbeKit.Set(order, "Amount", 12.5m);
                                var row = selector.Compile().DynamicInvoke(order)!;

                                if (
                                    row.GetType().Name != "OrderSummaryRow"
                                    || !Equals(ProbeKit.Get(row, "CustomerId"), 7)
                                    || !Equals(ProbeKit.Get(row, "Amount"), 12.5m)
                                )
                                {
                                    problems.Add(
                                        $"{type} の選択式が (CustomerId, Amount) を写した OrderSummaryRow を作らない"
                                    );
                                }
                            }

                            return problems;
                        }
                    ),
                ]
            ),
            QueryTextRow(
                "code-query-sql-and-manual-contract-declared-efcore-skips-bodies",
                QueryClass,
                "Generate_SqlQuery_AdoOnlyAndEfManual（3 メソッドの契約・EF Core 版に実装が無いこと・QuickER 版が生 SQL を実行する呼び出し。SQL Server の SQL の断片と manual 注記の英文は元に残す）",
                QueryText_SqlAndManualDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    Member(
                        contract,
                        "GetRecentAsync",
                        $"{listOfOrders} GetRecentAsync({CancellationParameter})"
                    ),
                    Member(
                        contract,
                        "SumAmountAsync",
                        $"Task<decimal?> SumAmountAsync({CancellationParameter})"
                    ),
                    Member(
                        contract,
                        "SpecialLookupAsync",
                        $"Task<OrderEntity?> SpecialLookupAsync({CancellationParameter})"
                    ),
                    Calls(implementation, "GetRecentAsync", "QueryBySqlAsync", count: 1),
                    Calls(
                        implementation,
                        "SumAmountAsync",
                        "ExecuteScalarSqlAsync<decimal?>",
                        count: 1
                    ),
                    NoMember(efCore, "GetRecentAsync"),
                    NoMember(efCore, "SumAmountAsync"),
                    NoMember(efCore, "SpecialLookupAsync"),
                ]
            ),
            QueryTextRow(
                "code-query-multi-target-shared-on-both-sql-only-on-its-dialect",
                QueryClass,
                "Generate_MultiTarget_DispatchesByDialect（契約と方言ごとの Repository の宣言。共有 DSL 本体と SQL Server の SQL の断片は元に残す）",
                QueryText_MultiTargetDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.Ns",
                    GenerateRepositories = true,
                    RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
                    IncludeDataAnnotations = true,
                },
                code:
                [
                    Member(
                        contract,
                        "GetByCustomerAsync",
                        $"{listOfOrders} GetByCustomerAsync(int customerId, {CancellationParameter})"
                    ),
                    Member(
                        contract,
                        "ServerOnlyAsync",
                        $"Task<int> ServerOnlyAsync({CancellationParameter})"
                    ),
                    Member(
                        implementation,
                        "GetByCustomerAsync",
                        modifiers: "public",
                        ns: "Test.Ns.Repositories.SqlServer"
                    ),
                    Member(
                        implementation,
                        "GetByCustomerAsync",
                        modifiers: "public",
                        ns: "Test.Ns.Repositories.Sqlite"
                    ),
                    Member(
                        implementation,
                        "ServerOnlyAsync",
                        modifiers: "public async",
                        ns: "Test.Ns.Repositories.SqlServer"
                    ),
                    NoMember(implementation, "ServerOnlyAsync", ns: "Test.Ns.Repositories.Sqlite"),
                ]
            ),
            QueryTextRow(
                "code-query-column-typed-parameter-follows-value-object",
                QueryClass,
                "Generate_ColumnTypedParameter_FollowsColumnType（VO 有効の図。直接比較の本体の文は P3-6〔簡易 DSL〕の領分で元に残す）",
                QueryText_ColumnTypedDiagram,
                Dialects_QueryOptions() with
                {
                    GenerateValueObjects = true,
                },
                code:
                [
                    .. QueryText_OnAll(
                        "GetByCustomerTypedAsync",
                        $"{listOfOrders} GetByCustomerTypedAsync(CustomerIdValue customerId, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                ]
            ),
            QueryTextRow(
                "code-query-column-typed-parameter-follows-primitive",
                QueryClass,
                "Generate_ColumnTypedParameter_FollowsColumnType（VO なしの図）",
                QueryText_ColumnTypedDiagram,
                Dialects_QueryOptions(),
                code:
                [
                    .. QueryText_OnAll(
                        "GetByCustomerTypedAsync",
                        $"{listOfOrders} GetByCustomerTypedAsync(int customerId, {CancellationParameter})",
                        contract,
                        implementation,
                        efCore
                    ),
                ]
            ),
            QueryTextRow(
                "compile-query-in-on-nullable-value-type-column",
                QueryClass,
                "Generate_InOnNullableValueTypeColumn_LiftsToNullableList（NULL 許容の値型列への IN がコンパイルできる）",
                QueryText_InOnNullableDiagram,
                Dialects_QueryOptions(),
                compile: true
            ),
            QueryTextRow(
                "exec-query-raw-sql-single-with-items-parameter-returns-first-or-null",
                QueryClass,
                "Generate_RawSqlSingleWithItemsParameter_RenamesLocal（SQLite で実行：引数名 items の生 SQL の単一戻り形が、一致があれば先頭の行・0 件なら null。ローカル名の綴りは行 compile-query-raw-sql-single-items-parameter のコンパイルが見る）",
                QueryText_ItemsDiagram,
                QueryText_SqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                exec:
                [
                    new ExecCheck(
                        "single-returns-first-row-or-null",
                        assembly =>
                            ProbeKit.WithSqlite(
                                assembly,
                                QueryText_ItemsDiagram(),
                                async (provider, _) =>
                                {
                                    await QueryText_InsertOrdersAsync(
                                        assembly,
                                        provider,
                                        QueryText_NewOrder(assembly, 1, "a", customerId: 7),
                                        QueryText_NewOrder(assembly, 2, "b", customerId: 7),
                                        QueryText_NewOrder(assembly, 3, "c", customerId: 8)
                                    );
                                    var problems = new List<string>();
                                    var hit = QueryText_CallQuery(
                                        assembly,
                                        provider,
                                        "FindByItemsAsync",
                                        8
                                    );
                                    var two = QueryText_CallQuery(
                                        assembly,
                                        provider,
                                        "FindByItemsAsync",
                                        7
                                    );
                                    var miss = QueryText_CallQuery(
                                        assembly,
                                        provider,
                                        "FindByItemsAsync",
                                        99
                                    );

                                    Text_Expect(
                                        problems,
                                        hit is not null && Equals(ProbeKit.Get(hit, "OrderId"), 3),
                                        "一致 1 件の行が返らない"
                                    );
                                    Text_Expect(
                                        problems,
                                        two is not null && ProbeKit.Get(two, "OrderId") is 1 or 2,
                                        "一致 2 件のとき、その 1 件が返らない"
                                    );
                                    Text_Expect(
                                        problems,
                                        miss is null,
                                        "一致 0 件が null にならない"
                                    );
                                    return problems;
                                }
                            )
                    ),
                ]
            ),
            QueryTextRow(
                "exec-query-raw-sql-blank-lines-inside-literal-are-kept",
                QueryClass,
                "Generate_RawSqlWithBlankLines_PreservesThemInsideVerbatimLiteral（SQLite で実行：改行 3 連を含む SQL の文字列リテラルの値が変わらない＝その値の行だけが一致する。生成テキストに連続空行が無いことは元に残す）",
                QueryText_BlankLinesDiagram,
                QueryText_SqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                exec:
                [
                    QueryText_SqliteRawQuery(
                        "blank-lines-literal-keeps-its-value",
                        QueryText_BlankLinesDiagram,
                        [
                            (1, "first\n\n\nsecond"),
                            (2, "first\n\nsecond"),
                            (3, "first\n\n\n\nsecond"),
                        ],
                        "FindByBlankMemoAsync",
                        [],
                        [1]
                    ),
                ]
            ),
            QueryTextRow(
                "exec-query-raw-sql-line-breaks-and-separators-keep-exact-value",
                QueryClass,
                "Generate_RawSqlWithLineBreaks_KeepsExactValue（SQLite で実行：CRLF・LF・LS・PS・FF・NEL・CR を含む複数行の SQL の文字列リテラルの値が 1 文字も変わらない。1 行の SQL の逐語リテラルの表記は元に残す）",
                QueryText_LineBreaksDiagram,
                QueryText_SqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                exec:
                [
                    new ExecCheck(
                        "line-break-sql-keeps-every-character",
                        assembly =>
                            ProbeKit.WithSqlite(
                                assembly,
                                QueryText_LineBreaksDiagram(),
                                async (provider, _) =>
                                {
                                    await QueryText_InsertOrdersAsync(
                                        assembly,
                                        provider,
                                        QueryText_NewOrder(assembly, 1, QueryText_LineBreakMemo),
                                        QueryText_NewOrder(
                                            assembly,
                                            2,
                                            QueryText_LineBreakMemo[..^1]
                                        ),
                                        QueryText_NewOrder(assembly, 3, "q")
                                    );
                                    var problems = new List<string>();
                                    var multi = QueryText_OrderIds(
                                        QueryText_CallQuery(
                                            assembly,
                                            provider,
                                            "FindMultiLineAsync"
                                        )
                                    );
                                    var count = QueryText_CallQuery(
                                        assembly,
                                        provider,
                                        "CountMultiLineAsync"
                                    );
                                    var single = QueryText_OrderIds(
                                        QueryText_CallQuery(
                                            assembly,
                                            provider,
                                            "FindSingleLineAsync"
                                        )
                                    );

                                    Text_Expect(
                                        problems,
                                        multi.SequenceEqual([1]),
                                        $"複数行の SQL の結果が [{string.Join(",", multi)}]（期待 [1]）"
                                    );
                                    Text_Expect(
                                        problems,
                                        Equals(count, 1),
                                        $"先頭が改行の複数行の SQL の結果が {count}（期待 1＝一致した行の先頭列）"
                                    );
                                    Text_Expect(
                                        problems,
                                        single.SequenceEqual([3]),
                                        $"1 行の SQL の結果が [{string.Join(",", single)}]（期待 [3]）"
                                    );
                                    return problems;
                                }
                            )
                    ),
                ]
            ),
        ];
    }

    /// <summary>QueryGenerationTests.Generate_RawSqlSingleWithItemsParameter_RenamesLocal の図（引数名 items の生 SQL の単一戻り形）</summary>
    private static ErDiagram QueryText_ItemsDiagram() =>
        QueryText_RawSqlDiagram(
            "FindByItems",
            QueryReturnShape.Single,
            "SELECT * FROM [Order] WHERE [CustomerId] = @items",
            new QueryParameter { Name = "items", Type = "int32" }
        );

    /// <summary>QueryGenerationTests.Generate_RawSqlWithBlankLines_PreservesThemInsideVerbatimLiteral の図（改行 3 連を含む文字列リテラルの生 SQL）</summary>
    private static ErDiagram QueryText_BlankLinesDiagram() =>
        QueryText_RawSqlDiagram(
            "FindByBlankMemo",
            QueryReturnShape.List,
            "SELECT * FROM [Order] WHERE [Memo] = 'first\n\n\nsecond'"
        );

    // ===== UniquenessGenerationTests：SQLite で CheckUniquenessAsync・ValidateUniqueAsync を実行して確かめる分 =====

    /// <summary>SQLite 方言の QuickER 版 Repository と EditModel を出す重複の事前チェックのオプション</summary>
    private static CodeGenerationOptions QueryText_UniquenessSqliteOptions() =>
        QueryText_UniquenessOptions() with
        {
            RepositoryDialects = [DialectNames.Sqlite],
        };

    /// <summary>UniquenessGenerationTests の Order に NULL 許容でない値型の列 Qty を足し、Qty と Code に一意制約を付けた図（Generate_ValueTypeConstraintMember_EmitsUnsetGuard）</summary>
    private static ErDiagram QueryText_UniquenessQtyDiagram()
    {
        var diagram = QueryText_UniquenessDiagram();
        var order = diagram.Entities.Single();
        var qty = new Column
        {
            Name = "Qty",
            DataType = "int",
            IsNullable = false,
        };
        order.Columns.Add(qty);
        order.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Order_Qty", ColumnIds = { qty.Id } }
        );
        order.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Order_Code",
                ColumnIds = { order.Columns.Single(column => column.Name == "Code").Id },
            }
        );
        return diagram;
    }

    /// <summary>UniquenessGenerationTests.Generate_NullablePrimaryKey_GuardsSelfExclusion の図（主キーを文字列にし、Memo に一意制約）</summary>
    private static ErDiagram QueryText_UniquenessStringKeyDiagram()
    {
        var diagram = QueryText_UniquenessDiagram(
            (_, memo) => [new UniqueConstraint { Name = "UQ_Order_Memo", ColumnIds = { memo.Id } }]
        );
        diagram.Entities.Single().Columns.Single(column => column.Name == "OrderId").DataType =
            "nvarchar(20)";
        return diagram;
    }

    /// <summary>生成物の Order の実体を作る（<paramref name="values"/> は列名と値の組。null の値も書く）</summary>
    private static object QueryText_NewUniqueOrder(
        Assembly assembly,
        params (string Property, object? Value)[] values
    )
    {
        var order = ProbeKit.Create(ProbeKit.Type(assembly, "OrderEntity"));

        foreach (var (property, value) in values)
        {
            ProbeKit.Set(order, property, value);
        }

        return order;
    }

    /// <summary>Repository の <c>CheckUniquenessAsync</c> が返した違反を <c>制約名:構成列,…</c> の文字列に並べる</summary>
    private static IReadOnlyList<string> QueryText_Violations(
        Assembly assembly,
        IServiceProvider provider,
        object entity
    ) =>
        (
            (IEnumerable)
                ProbeKit.Await(
                    ProbeKit.Call(
                        provider.GetRequiredService(ProbeKit.Type(assembly, RepositoryContract)),
                        "CheckUniquenessAsync",
                        entity,
                        CancellationToken.None
                    )
                )!
        )
            .Cast<object>()
            .Select(violation =>
                $"{ProbeKit.Get(violation, "ConstraintName")}:{string.Join(",", QueryText_Strings(violation, "PropertyNames"))}"
            )
            .ToList();

    /// <summary>
    /// SQLite の Repository の <c>CheckUniquenessAsync</c> で、入れておいた行 <paramref name="seed"/> に対し、各 <paramref name="cases"/> の実体が返す違反が期待と一致する
    /// </summary>
    private static ExecCheck QueryText_SqliteUniqueness(
        string name,
        Func<ErDiagram> diagram,
        Func<Assembly, object[]> seed,
        params (string Case, Func<Assembly, object> Entity, string[] Expected)[] cases
    ) =>
        new(
            name,
            assembly =>
                ProbeKit.WithSqlite(
                    assembly,
                    diagram(),
                    async (provider, _) =>
                    {
                        var repository = provider.GetRequiredService(
                            ProbeKit.Type(assembly, RepositoryContract)
                        );

                        foreach (var row in seed(assembly))
                        {
                            await (Task)
                                ProbeKit.Call(
                                    repository,
                                    "InsertAsync",
                                    row,
                                    CancellationToken.None
                                )!;
                        }

                        return cases
                            .Select(item =>
                            {
                                var actual = QueryText_Violations(
                                    assembly,
                                    provider,
                                    item.Entity(assembly)
                                );

                                return actual.SequenceEqual(item.Expected)
                                    ? null
                                    : $"{item.Case}: 違反が [{string.Join("; ", actual)}]（期待 [{string.Join("; ", item.Expected)}]）";
                            })
                            .OfType<string>()
                            .ToList();
                    }
                )
        );

    /// <summary>型の宣言の構文木に、式 <paramref name="expression"/> への <c>is not null</c> のパターンが 1 つも現れない</summary>
    private static CodeRule QueryText_NoNotNullPatternOn(string expression) =>
        NoTypes(
            $"{expression} への is not null のパターンを含む",
            type =>
                type.DeclaringSyntaxReferences.Any(reference =>
                    reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<IsPatternExpressionSyntax>()
                        .Any(pattern =>
                            pattern.Expression.ToString() == expression
                            && pattern.Pattern
                                is UnaryPatternSyntax { Pattern: ConstantPatternSyntax constant }
                            && constant.Expression.Kind()
                                == Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression
                        )
                )
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> QueryTextUniquenessExecRows() =>
        [
            QueryTextRow(
                "exec-uniqueness-nullable-single-column-skips-null-and-excludes-self",
                UniquenessClass,
                "Generate_NullableSingleColumn_EmitsNullGuard（SQLite で CheckUniquenessAsync を実行：NULL の 2 行は違反にならず、同じ非 NULL の値は実名 UQ_Order_Memo の違反、自分の行は除外）",
                QueryText_UniquenessMemoDiagram,
                QueryText_UniquenessSqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    // 非 NULL の値型の主キーは null になり得ない：自分の除外は無条件で、`is not null` の判定（常に真＝警告）を付けない
                    QueryText_NoNotNullPatternOn("entity.OrderId"),
                ],
                exec:
                [
                    QueryText_SqliteUniqueness(
                        "memo-null-guard-and-self-exclusion",
                        QueryText_UniquenessMemoDiagram,
                        assembly =>
                            [
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 1),
                                    ("Code", "c1"),
                                    ("Memo", null)
                                ),
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 2),
                                    ("Code", "c2"),
                                    ("Memo", null)
                                ),
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 4),
                                    ("Code", "c4"),
                                    ("Memo", "m")
                                ),
                            ],
                        (
                            "Memo が NULL の新しい行（NULL は照合しない）",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 3),
                                    ("Code", "c3"),
                                    ("Memo", null)
                                ),
                            []
                        ),
                        (
                            "同じ非 NULL の Memo の別の行",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 5),
                                    ("Code", "c5"),
                                    ("Memo", "m")
                                ),
                            ["UQ_Order_Memo:Memo"]
                        ),
                        (
                            "自分自身の行（更新）",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 4),
                                    ("Code", "c4"),
                                    ("Memo", "m")
                                ),
                            []
                        )
                    ),
                ]
            ),
            QueryTextRow(
                "exec-uniqueness-nullable-primary-key-excludes-self-only-when-set",
                UniquenessClass,
                "Generate_NullablePrimaryKey_GuardsSelfExclusion（主キーが文字列の図を SQLite で実行：主キー未設定の実体は何も除外せず重複を見逃さない・設定済みなら自分の行を除外）",
                QueryText_UniquenessStringKeyDiagram,
                QueryText_UniquenessSqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Member(
                        "OrderUniquenessConstraints",
                        "ExcludeSelf",
                        "SqlQuery<OrderEntity> ExcludeSelf(SqlQuery<OrderEntity> query, OrderEntity entity)",
                        modifiers: "public static"
                    ),
                ],
                exec:
                [
                    QueryText_SqliteUniqueness(
                        "string-key-self-exclusion-only-when-set",
                        QueryText_UniquenessStringKeyDiagram,
                        assembly =>
                            [
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", "A"),
                                    ("Code", "c1"),
                                    ("Memo", "x")
                                ),
                            ],
                        (
                            "主キー未設定（挿入前）の実体は何も除外せず、同じ Memo を重複として見つける",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", null),
                                    ("Code", "c2"),
                                    ("Memo", "x")
                                ),
                            ["UQ_Order_Memo:Memo"]
                        ),
                        (
                            "主キー設定済みの別の行（挿入）",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", "B"),
                                    ("Code", "c2"),
                                    ("Memo", "x")
                                ),
                            ["UQ_Order_Memo:Memo"]
                        ),
                        (
                            "主キー設定済みの自分の行（更新）は除外される",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", "A"),
                                    ("Code", "c1"),
                                    ("Memo", "x")
                                ),
                            []
                        )
                    ),
                ]
            ),
            QueryTextRow(
                "exec-uniqueness-unnamed-composite-matches-only-when-every-column-matches",
                UniquenessClass,
                "Generate_UnnamedComposite_UsesSynthesizedName（名前なしの複合制約を SQLite で実行：全構成列が一致したときだけ合成名 UQ_Order_Code_Memo の違反。EditModel の構成列は行 code-uniqueness-edit-model-emits-constraints-table が同じ複合制約で見る）",
                QueryText_UniquenessCompositeDiagram,
                QueryText_UniquenessSqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Attribute(
                        "OrderEntity",
                        null,
                        "UniqueConstraint([\"Code\", \"Memo\"], Name = \"UQ_Order_Code_Memo\")"
                    ),
                ],
                exec:
                [
                    QueryText_SqliteUniqueness(
                        "composite-every-column-must-match",
                        QueryText_UniquenessCompositeDiagram,
                        assembly =>
                            [
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 1),
                                    ("Code", "c"),
                                    ("Memo", "m")
                                ),
                            ],
                        (
                            "Code・Memo の両方が一致",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 2),
                                    ("Code", "c"),
                                    ("Memo", "m")
                                ),
                            ["UQ_Order_Code_Memo:Code,Memo"]
                        ),
                        (
                            "Memo だけが違う",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 3),
                                    ("Code", "c"),
                                    ("Memo", "other")
                                ),
                            []
                        ),
                        (
                            "Code だけが違う",
                            assembly =>
                                QueryText_NewUniqueOrder(
                                    assembly,
                                    ("OrderId", 4),
                                    ("Code", "d"),
                                    ("Memo", "m")
                                ),
                            []
                        )
                    ),
                ]
            ),
            QueryTextRow(
                "exec-uniqueness-edit-model-drops-findings-on-unset-value-type-members",
                UniquenessClass,
                "Generate_ValueTypeConstraintMember_EmitsUnsetGuard（SQLite で EditModel の ValidateUniqueAsync を実行：未入力の値型・NOT NULL の文字列の列の所見だけを捨て、ほかの制約は照合する）",
                QueryText_UniquenessQtyDiagram,
                QueryText_UniquenessSqliteOptions(),
                typeDialect: DialectNames.Sqlite,
                code:
                [
                    Calls(
                        "OrderEditModel",
                        "ValidateUniqueAsync",
                        "CheckDatabaseUniquenessAsync",
                        count: 1
                    ),
                    // 未入力で実体へ写せない列（値型の Qty・NOT NULL の文字列の Code）だけが「照合できない列」に載り、NULL 許容の Memo は載らない
                    CallArguments(
                        "OrderEditModel",
                        "ValidateUniqueAsync",
                        "List.Add",
                        ["\"Qty\""],
                        count: 1
                    ),
                    CallArguments(
                        "OrderEditModel",
                        "ValidateUniqueAsync",
                        "List.Add",
                        ["\"Code\""],
                        count: 1
                    ),
                    CallArguments(
                        "OrderEditModel",
                        "ValidateUniqueAsync",
                        "List.Add",
                        ["\"Memo\""],
                        count: 0
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "unset-members-drop-only-their-own-findings",
                        assembly =>
                            ProbeKit.WithSqlite(
                                assembly,
                                QueryText_UniquenessQtyDiagram(),
                                async (provider, _) =>
                                {
                                    var repository = provider.GetRequiredService(
                                        ProbeKit.Type(assembly, RepositoryContract)
                                    );

                                    // Qty の既定値（0）と同じ値の行と、Code が空文字列の行（未入力の Code が実体の既定値で照合されれば当たる）
                                    foreach (
                                        var row in new[]
                                        {
                                            QueryText_NewUniqueOrder(
                                                assembly,
                                                ("OrderId", 1),
                                                ("Code", "a"),
                                                ("Qty", 0)
                                            ),
                                            QueryText_NewUniqueOrder(
                                                assembly,
                                                ("OrderId", 2),
                                                ("Code", ""),
                                                ("Qty", 9)
                                            ),
                                        }
                                    )
                                    {
                                        await (Task)
                                            ProbeKit.Call(
                                                repository,
                                                "InsertAsync",
                                                row,
                                                CancellationToken.None
                                            )!;
                                    }

                                    bool Validate(string? code, int? qty)
                                    {
                                        var model = ProbeKit.Create(
                                            ProbeKit.Type(assembly, "OrderEditModel")
                                        );
                                        ProbeKit.Set(model, "Code", code);
                                        ProbeKit.Set(model, "Qty", qty);
                                        return (bool)
                                            ProbeKit.Await(
                                                ProbeKit.Call(
                                                    model,
                                                    "ValidateUniqueAsync",
                                                    repository,
                                                    CancellationToken.None
                                                )
                                            )!;
                                    }

                                    var problems = new List<string>();
                                    Text_Expect(
                                        problems,
                                        Validate("b", null),
                                        "Qty が未入力（既定値 0 が行と重なる）でも、Qty の所見は捨てられて通る"
                                    );
                                    Text_Expect(
                                        problems,
                                        !Validate("a", null),
                                        "Qty が未入力でも、別の制約 UQ_Order_Code（Code が行と重なる）は照合される"
                                    );
                                    Text_Expect(
                                        problems,
                                        Validate(null, 7),
                                        "Code が未入力（空文字列の行と重なる）でも、Code の所見は捨てられて通る"
                                    );
                                    return problems;
                                }
                            )
                    ),
                ]
            ),
        ];

    /// <summary>UniquenessGenerationTests の Order に、名前つきの Memo の一意制約を付けた図（Generate_NullableSingleColumn_EmitsNullGuard）</summary>
    private static ErDiagram QueryText_UniquenessMemoDiagram() =>
        QueryText_UniquenessDiagram(
            (_, memo) => [new UniqueConstraint { Name = "UQ_Order_Memo", ColumnIds = { memo.Id } }]
        );

    /// <summary>UniquenessGenerationTests の Order に、名前なしの (Code, Memo) の複合一意制約を付けた図（Generate_UnnamedComposite_UsesSynthesizedName）</summary>
    private static ErDiagram QueryText_UniquenessCompositeDiagram() =>
        QueryText_UniquenessDiagram(
            (code, memo) => [new UniqueConstraint { ColumnIds = { code.Id, memo.Id } }]
        );

    // ===== SyncSupportGenerationTests：宣言・呼び出し・生成するオブジェクト・記述子・SQLite での実行へ移した分 =====

    /// <summary>SyncSupportGenerationTests.SyncSql_DottedTableName_QuotesAtFirstDotOnly の図（sync_orders を dbo.sync.orders へ改名）</summary>
    private static ErDiagram QueryText_SyncDottedTableDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram.Entities.First(entity => entity.TableName == "sync_orders").TableName =
            "dbo.sync.orders";
        return diagram;
    }

    /// <summary>名前 <paramref name="typeName"/> の型の宣言の構文木に、単純名 <paramref name="identifier"/> の識別子が 1 つも現れない</summary>
    private static CodeRule QueryText_TypeHasNoIdentifier(string typeName, string identifier) =>
        NoTypes(
            $"{typeName} の中で {identifier} を参照する",
            type =>
                type.Name == typeName
                && type.DeclaringSyntaxReferences.Any(reference =>
                    reference
                        .GetSyntax()
                        .DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Any(name => name.Identifier.ValueText == identifier)
                )
        );

    /// <summary>
    /// 生成物のサーバー側の <c>SyncSourceServices</c>（同期対象テーブルごとの差分ソースの登録を問う表）が、<paramref name="expected"/>
    /// （<c>型引数の名前:報告名</c>）と完全一致する
    /// </summary>
    private static ExecCheck QueryText_SyncSourceServices(params string[] expected) =>
        new(
            "sync-source-services-table",
            assembly =>
            {
                var actual = (
                    (IEnumerable)
                        ProbeKit.GetStaticField(
                            ProbeKit.Type(assembly, "GeneratedRemoteEndpoints"),
                            "SyncSourceServices"
                        )
                )
                    .Cast<System.Runtime.CompilerServices.ITuple>()
                    .Select(entry =>
                        $"{string.Join(",", ((Type)entry[0]!).GetGenericArguments().Select(argument => argument.Name))}:{entry[1]}"
                    )
                    .ToList();

                return actual.SequenceEqual(expected)
                    ? []
                    :
                    [
                        $"SyncSourceServices が [{string.Join("; ", actual)}]（期待 [{string.Join("; ", expected)}]）",
                    ];
            }
        );

    /// <summary>テーブル名 <paramref name="tableName"/> の記述子の、SQLite 側の SQL（<c>Local*Sql</c>）をすべて実際の SQLite で流す（別 DB を dbo として付けた中に表を作る）</summary>
    private static ExecCheck QueryText_SyncLocalSqlOnSqlite(string tableName, string quotedTable) =>
        new(
            "local-sql-runs-on-sqlite",
            assembly =>
            {
                var descriptor = QueryText_SyncDescriptors(assembly)
                    .Single(item =>
                        (string)ProbeKit.Get(item.Descriptor, "TableName")! == tableName
                    )
                    .Descriptor;
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    "Data Source=:memory:"
                );
                connection.Open();

                void Run(string sql)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("@key", 1);
                    command.Parameters.AddWithValue("@keys", 1);
                    command.ExecuteNonQuery();
                }

                Run("ATTACH DATABASE ':memory:' AS dbo");
                Run(
                    $"CREATE TABLE {quotedTable} (order_id INTEGER PRIMARY KEY, customer_name TEXT, row_ver BLOB, summary TEXT, attachment BLOB)"
                );
                var problems = new List<string>();

                foreach (
                    var property in new[]
                    {
                        "LocalAnchorSql",
                        "LocalRowVersionSql",
                        "LocalKeysSql",
                        "LocalExistingKeysSql",
                        "LocalDeleteAllSql",
                    }
                )
                {
                    var sql = (string)ProbeKit.Get(descriptor, property)!;

                    if (!sql.Contains(quotedTable, StringComparison.Ordinal))
                    {
                        problems.Add($"{property} が表 {quotedTable} を指さない: {sql}");
                        continue;
                    }

                    try
                    {
                        Run(sql);
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex)
                    {
                        problems.Add($"{property} が SQLite で実行できない（{ex.Message}）: {sql}");
                    }
                }

                return problems;
            }
        );

    /// <summary>同期の記述子のうち、<paramref name="property"/>（<c>SyncNote</c> など）が版を持たない・持つの宣言を読む</summary>
    private static ExecCheck QueryText_SyncVersionShapes() =>
        new(
            "mixed-diagram-descriptor-version-shapes",
            assembly =>
            {
                var note = QueryText_SyncDescriptor(assembly, "SyncNote");
                var order = QueryText_SyncDescriptor(assembly, "SyncOrder");
                var problems = new List<string>();

                // 記述子の ReadRowVersion に、RowVer 列を持つ行（無ければ何も持たない行）を渡して返る版を見る
                byte[]? ReadRowVersion(Assembly loaded, object descriptor, string entityType)
                {
                    var entity = ProbeKit.Create(ProbeKit.Type(loaded, entityType));

                    if (entity.GetType().GetProperty("RowVer") is { } rowVer)
                    {
                        rowVer.SetValue(entity, new byte[] { 7, 8 });
                    }

                    return (byte[]?)
                        ((Delegate)ProbeKit.Get(descriptor, "ReadRowVersion")!).DynamicInvoke(
                            entity
                        );
                }

                Text_Expect(
                    problems,
                    ProbeKit.Get(note, "IsVersionless") is true,
                    "SyncNote の IsVersionless が true でない"
                );
                Text_Expect(
                    problems,
                    ProbeKit.Get(note, "ServerChangesSql") is "",
                    "版なしの SyncNote に ServerChangesSql がある"
                );
                Text_Expect(
                    problems,
                    ReadRowVersion(assembly, note, "SyncNoteEntity") is null,
                    "版なしの SyncNote の ReadRowVersion が版を返す"
                );
                Text_Expect(
                    problems,
                    ProbeKit.Get(order, "IsVersionless") is false,
                    "SyncOrder の IsVersionless が false でない"
                );
                Text_Expect(
                    problems,
                    ProbeKit.Get(order, "ServerChangesSql") is string { Length: > 0 },
                    "版ありの SyncOrder に ServerChangesSql が無い"
                );
                Text_Expect(
                    problems,
                    ReadRowVersion(assembly, order, "SyncOrderEntity") is [7, 8],
                    "版ありの SyncOrder の ReadRowVersion が行の RowVer を返さない"
                );
                Text_Expect(
                    problems,
                    ProbeKit.Get(order, "ServerPageFirstSql") is "",
                    "版ありの SyncOrder に ServerPageFirstSql がある"
                );
                return problems;
            }
        );

    private static IEnumerable<ScenarioRow<CodegenCase>> QueryTextSyncRows()
    {
        const string endpoints = "GeneratedRemoteEndpoints";
        const string extensions = "GeneratedSyncServiceCollectionExtensions";
        const string journalingBase = "JournalingRepositoryBase<TEntity, TKey>";

        return
        [
            QueryTextRow(
                "code-sync-remote-services-emit-endpoints-and-http-sources",
                SyncClass,
                "SyncSupportWithRemoteServices_EmitsEndpointsAndHttpSources（サーバーの同期エンドポイントと HTTP 差分ソースの登録。経路の文字列の組み立ては元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    // 同期対象テーブルごとに 1 行ずつ（漏れたテーブルは黙って同期不能になる）
                    CallArguments(
                        endpoints,
                        "MapGeneratedRemoteEndpoints",
                        "MapSyncEndpoints<SyncOrderEntity, int>",
                        ["group", "\"SyncOrder\""]
                    ),
                    CallArguments(
                        endpoints,
                        "MapGeneratedRemoteEndpoints",
                        "MapSyncEndpoints<SyncOrderLineEntity, int>",
                        ["group", "\"SyncOrderLine\""]
                    ),
                    // 経路はクライアントと共有の定数から組み立てる
                    ReferencesSymbol(endpoints, "MapSyncEndpoints", "RemoteSyncOperations.Ceiling"),
                    ReferencesSymbol(endpoints, "MapSyncEndpoints", "RemoteSyncOperations.Changes"),
                    ReferencesSymbol(endpoints, "MapSyncEndpoints", "RemoteSyncOperations.Keys"),
                    // エンドポイントは差分ソースの薄い remoting（実装の二重化をしない）
                    Calls(endpoints, "MapSyncEndpoints", "SyncSource<TEntity, TKey>"),
                    Calls(
                        endpoints,
                        "SyncSource",
                        "GetRequiredService<ISyncServerSource<TEntity, TKey>>",
                        count: 1
                    ),
                    Type(
                        "HttpSyncServerSource<TEntity, TKey>",
                        kind: "class",
                        modifiers: "public sealed"
                    ),
                    Calls(
                        extensions,
                        "AddGeneratedHttpSyncSources",
                        "AddHttpSource<SyncOrderEntity, int>",
                        count: 1
                    ),
                    Calls(
                        extensions,
                        "AddGeneratedHttpSyncSources",
                        "AddHttpSource<SyncOrderLineEntity, int>",
                        count: 1
                    ),
                    Member(
                        extensions,
                        "AddGeneratedHttpSyncSources",
                        "IServiceCollection AddGeneratedHttpSyncSources(this IServiceCollection services, string baseAddress)",
                        modifiers: "public static"
                    ),
                    Member(
                        extensions,
                        "AddGeneratedHttpSyncSources",
                        "IServiceCollection AddGeneratedHttpSyncSources(this IServiceCollection services, Func<IServiceProvider, HttpClient> httpClientFactory)",
                        modifiers: "public static"
                    ),
                    // 転送経路の選択は DI 1 行の差でしかない（直結の登録も併存する）
                    Member(
                        extensions,
                        "AddGeneratedDirectSyncSources",
                        "IServiceCollection AddGeneratedDirectSyncSources(this IServiceCollection services, object? serverServiceKey)",
                        modifiers: "public static"
                    ),
                    Member(
                        extensions,
                        "AddGeneratedSyncEngine",
                        "IServiceCollection AddGeneratedSyncEngine(this IServiceCollection services, object? localServiceKey, IReadOnlyCollection<Type>? excludeFromSync = null)",
                        modifiers: "public static"
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-endpoints-check-source-registration-at-mapping-time",
                SyncClass,
                "SyncEndpoints_CheckSourceRegistrationAtMappingTime（マップ時の検査の宣言と、全テーブルぶんの差分ソースの表。例外の文言は元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    // マップ側は 1 行（エンドポイントを張る前に検査する）
                    CallOrder(
                        endpoints,
                        "MapGeneratedRemoteEndpoints",
                        [
                            "RequireSyncSources",
                            "MapSyncEndpoints<SyncOrderEntity, int>",
                            "MapSyncEndpoints<SyncMarkerEntity, int>",
                            "MapSyncEndpoints<SyncOrderLineEntity, int>",
                        ]
                    ),
                    CallArguments(
                        endpoints,
                        "MapGeneratedRemoteEndpoints",
                        "RequireSyncSources",
                        ["ServiceProvider"]
                    ),
                    // 検査は「登録の有無だけを問う面」で行う＝実体を作らずスコープにも触れない。表を 1 周するだけ
                    Calls(
                        endpoints,
                        "RequireSyncSources",
                        "GetService<IServiceProviderIsService>",
                        count: 1
                    ),
                    Calls(
                        endpoints,
                        "RequireSyncSources",
                        "IServiceProviderIsService.IsService",
                        count: 1
                    ),
                    ReferencesSymbol(
                        endpoints,
                        "RequireSyncSources",
                        $"{endpoints}.SyncSourceServices"
                    ),
                ],
                exec:
                [
                    // 同期対象テーブルごとに 1 行（漏れたテーブルは検査から静かに抜ける）。報告名は型から導かず書き下ろす（Int32 ではなく int）
                    QueryText_SyncSourceServices(
                        "SyncOrderEntity,Int32:ISyncServerSource<SyncOrderEntity, int>",
                        "SyncMarkerEntity,Int32:ISyncServerSource<SyncMarkerEntity, int>",
                        "SyncOrderLineEntity,Int32:ISyncServerSource<SyncOrderLineEntity, int>",
                        "SyncNoteEntity,Int32:ISyncServerSource<SyncNoteEntity, int>"
                    ),
                ]
            ),
            QueryTextRow(
                "exec-sync-dotted-table-name-local-sql-quotes-at-first-dot",
                SyncClass,
                "SyncSql_DottedTableName_QuotesAtFirstDotOnly（SQLite 側の SQL を、別 DB を dbo として付けた SQLite で実際に流す：最初のドットだけで分割クォートした表を指す。SQL Server 側の SQL の断片は元に残す）",
                QueryText_SyncDottedTableDiagram,
                SyncFixtureDefinition.Options,
                exec: [QueryText_SyncLocalSqlOnSqlite("dbo.sync.orders", "\"dbo\".\"sync.orders\"")]
            ),
            QueryTextRow(
                "code-sync-journaling-decorator-covers-every-write-entry-point",
                SyncClass,
                "JournalingDecorator_CoversEveryWriteEntryPoint（全書き込み入口の記録・読み取りの素通し・削除前の読み・per-type の委譲・SyncGraphRecorder の走査。RowState の比較の向きは元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    Type(journalingBase, kind: "class", modifiers: "public abstract"),
                    // 書き込み入口の網羅：どの入口も記録と業務書き込みを RecordAndWriteAsync（ローカルの書き込みロックの内側で 1 組）へ渡す
                    Member(
                        journalingBase,
                        "InsertAsync",
                        "Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public async"
                    ),
                    Member(
                        journalingBase,
                        "UpdateAsync",
                        "Task<bool> UpdateAsync(TEntity entity, ConcurrencyMode mode = Optimistic, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Member(
                        journalingBase,
                        "BulkInsertAsync",
                        "Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Member(
                        journalingBase,
                        "DeleteAsync",
                        "Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    Member(journalingBase, "SaveAsync", modifiers: "public", count: 2),
                    Calls(journalingBase, "InsertAsync", "RecordAndWriteAsync", count: 1),
                    Calls(journalingBase, "UpdateAsync", "RecordAndWriteAsync", count: 1),
                    Calls(journalingBase, "BulkInsertAsync", "RecordAndWriteAsync", count: 1),
                    Calls(journalingBase, "DeleteAsync", "RecordAndWriteAsync", count: 1),
                    Calls(journalingBase, "SaveAsync", "RecordAndWriteAsync", count: 2),
                    Calls(
                        journalingBase,
                        "RecordUnboundedBinaryWriteAsync",
                        "RecordAndWriteAsync",
                        count: 1
                    ),
                    Calls(journalingBase, "RecordAndWriteAsync", "WriteExclusiveAsync", count: 1),
                    // グラフ保存はカスケード閉包の記録（SyncGraphRecorder）を先に呼ぶ
                    Calls(
                        journalingBase,
                        "SaveAsync",
                        "SyncGraphRecorder.RecordSaveAsync",
                        count: 1
                    ),
                    Calls(journalingBase, "SaveAsync", "SyncGraphRecorder.CollectSave", count: 1),
                    // 読み取り経路は素通し（記録しない）
                    Calls(journalingBase, "Query", "Query", count: 1),
                    // 削除は「行が消える前に読む」ことでミラー版を捕まえ、版ガード付きで再生できるようにする（版ありサブクラス基底の担当）
                    Calls(
                        "JournalingRepository<TEntity, TKey>",
                        "RecordDeleteAsync",
                        "GetByIdAsync",
                        count: 1
                    ),
                    ReferencesSymbol(
                        "JournalingRepository<TEntity, TKey>",
                        "RecordDeleteAsync",
                        "SyncJournalOperation.Delete"
                    ),
                    // per-type はテーブルの記述子と共有レコーダを基底へ渡すだけ
                    Type(
                        "JournalingSyncOrderRepository",
                        kind: "class",
                        baseType: "JournalingRepository<SyncOrderEntity, int>"
                    ),
                    ReferencesSymbol(
                        "JournalingSyncOrderRepository",
                        ".ctor",
                        "GeneratedSyncTables.SyncOrder"
                    ),
                    ReferencesSymbol(
                        "JournalingSyncOrderRepository",
                        ".ctor",
                        "GeneratedSyncTables.GraphRecorder"
                    ),
                    // テーブル名・キー整形・グラフ記録の委譲は汎用コアが記述子から引く（per-type には出ない）
                    NoMember("JournalingSyncOrderRepository", "TableName"),
                    ReferencesSymbol(
                        "JournalingSyncOrderRepository",
                        null,
                        "SyncGraphRecorder.RecordSaveAsync",
                        present: false
                    ),
                    // 重複事前チェックはランタイム共通面の member なので、素通しは汎用コア側が 1 回だけ持つ
                    NoMember("JournalingSyncOrderRepository", "CheckUniquenessAsync"),
                    Member(
                        journalingBase,
                        "CheckUniquenessAsync",
                        "Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(TEntity entity, CancellationToken cancellationToken = default(CancellationToken))",
                        modifiers: "public"
                    ),
                    // SyncGraphRecorder：走査は保存側と同じ 1 つの列挙（EntityBaseCore.EnumerateCascadeChildren）で、テーブルごとの再帰メソッドではない
                    Calls("SyncGraphRecorder", "CollectSave", "EnumerateCascadeChildren", count: 1),
                    ReferencesSymbol("SyncGraphRecorder", null, "SyncJournalOperation.Delete"),
                    ReferencesSymbol("SyncGraphRecorder", "CollectSave", "RowState.Removed"),
                    ReferencesSymbol("SyncGraphRecorder", "CollectSave", "RowState.Unchanged"),
                    QueryText_TypeHasNoIdentifier("SyncGraphRecorder", "SyncOrderEntity"),
                ]
            ),
            QueryTextRow(
                "code-sync-session-suppresses-journaling-for-engine-writes",
                SyncClass,
                "SyncEngine_SuppressesJournalingForItsOwnWrites（SyncSession の宣言と、エンジンの書き込み経路・ジャーナル記録の双方が参照すること。if / using の文の形は元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    Type("SyncSession", kind: "class", modifiers: "public static"),
                    Member(
                        "SyncSession",
                        "IsSuppressed",
                        "bool IsSuppressed { get; }",
                        modifiers: "public static"
                    ),
                    Member(
                        "SyncSession",
                        "Suppress",
                        "IDisposable Suppress()",
                        modifiers: "public static"
                    ),
                    ReferencesSymbol("SyncTableBase<TEntity, TKey>", null, "SyncSession.Suppress"),
                    ReferencesSymbol(
                        journalingBase,
                        "RecordAndWriteAsync",
                        "SyncSession.IsSuppressed"
                    ),
                ]
            ),
            QueryTextRow(
                "code-sync-mixed-diagram-versionless-shapes",
                SyncClass,
                "MixedDiagram_EmitsVersionlessShapes（版なしテーブルの記述子・登録・ソース・デコレータの形。サーバーのエンドポイントの選別は行 code-sync-versionless-endpoint-map-with-versionless-table〔同じ図〕が見る。SQL Server の SQL の断片は元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    // ① 登録側が記述子の版の有無で固定クラスを選ぶ
                    Creates(
                        extensions,
                        "AddSyncTable",
                        "VersionlessSyncTable<TEntity, TKey>",
                        ["local", "localSqlExecutor", "server", "descriptor", "localSaveHooks"]
                    ),
                    Creates(
                        extensions,
                        "AddSyncTable",
                        "SyncTable<TEntity, TKey>",
                        ["local", "localSqlExecutor", "server", "descriptor", "localSaveHooks"]
                    ),
                    Creates(
                        extensions,
                        "AddDirectSource",
                        "VersionlessDirectSyncSource<TEntity, TKey>",
                        ["serverSqlExecutor", "serverRepository", "descriptor"]
                    ),
                    // ② 走査の形：版なし＝キー順ページングと null の ceiling
                    Member(
                        "VersionlessDirectSyncSource<TEntity, TKey>",
                        "GetFirstPageAsync",
                        modifiers: "public override async"
                    ),
                    Calls(
                        "VersionlessDirectSyncSource<TEntity, TKey>",
                        "GetChangeCeilingAsync",
                        "FromResult<byte[]?>",
                        count: 1
                    ),
                    // ③ 版なしデコレータは版なし基底を継承し、版ありデコレータは版あり基底を継承する
                    Type(
                        "JournalingSyncNoteRepository",
                        kind: "class",
                        baseType: "VersionlessJournalingRepository<SyncNoteEntity, int>"
                    ),
                    NoMember("JournalingSyncNoteRepository", "ReadRowVersion"),
                    Type(
                        "JournalingSyncOrderRepository",
                        kind: "class",
                        baseType: "JournalingRepository<SyncOrderEntity, int>"
                    ),
                ],
                exec: [QueryText_SyncVersionShapes()]
            ),
            QueryTextRow(
                "code-sync-binary-columns-implemented-on-three-surfaces",
                SyncClass,
                "SyncBinaryColumns_AreImplementedOnAllThreeSurfaces（除外列のコピー面：記述子のアクセサ・ローカル・直結・HTTP の 3 実装。HTTP の BinaryColumns の条件式は元に残す）",
                SyncFixtureDefinition.Build,
                SyncFixtureDefinition.Options,
                code:
                [
                    // 列ごとのアクセサは記述子が 1 度だけ運ぶ（具象契約を知るのはこのラムダだけ）
                    CallArguments(
                        "GeneratedSyncTables",
                        "SyncOrder",
                        "ISyncOrderRemoteRepository.ReadAttachmentAsync",
                        ["id", "destination", "cancellationToken"]
                    ),
                    CallArguments(
                        "GeneratedSyncTables",
                        "SyncOrder",
                        "ISyncOrderRemoteRepository.WriteAttachmentAsync",
                        ["id", "source", "length", "cancellationToken"]
                    ),
                    // ローカル（記述子側）：同じ表をローカルリポジトリの上で開く
                    Creates(
                        "SyncTableBase<TEntity, TKey>",
                        ".ctor",
                        "SyncRepositoryBinaryColumns<TEntity, TKey>",
                        ["descriptor", "local"]
                    ),
                    // 直結サーバー：同じ表をサーバーリポジトリ（＝Writer）の上で開く
                    Creates(
                        "DirectSyncSourceBase<TEntity, TKey>",
                        ".ctor",
                        "SyncRepositoryBinaryColumns<TEntity, TKey>",
                        ["descriptor", "writer"]
                    ),
                    // HTTP サーバー：既存のバイナリエンドポイント（基底のヘルパー）へ委譲
                    Calls(
                        "HttpSyncServerSource<TEntity, TKey>",
                        "ReadUnboundedBinaryAsync",
                        "DownloadUnboundedBinaryColumnAsync",
                        count: 1
                    ),
                ],
                exec:
                [
                    new ExecCheck(
                        "descriptor-names-the-excluded-column",
                        assembly =>
                        {
                            var names = QueryText_Strings(
                                QueryText_SyncDescriptor(assembly, "SyncOrder"),
                                "UnboundedBinaryColumnNames"
                            );

                            return names.SequenceEqual(["Attachment"])
                                ? []
                                :
                                [
                                    $"SyncOrder の UnboundedBinaryColumnNames が [{string.Join(", ", names)}]",
                                ];
                        }
                    ),
                ]
            ),
        ];
    }
}
