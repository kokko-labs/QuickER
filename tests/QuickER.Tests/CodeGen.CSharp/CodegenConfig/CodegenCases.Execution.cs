using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：実行（探針）。インメモリのシード・EF Core のモデル・同期のキーのテキスト（GuidKey の値オブジェクトは
/// <see cref="SeedRows"/> の行 <c>probe-guidkey-create-generates-unique-guid</c>）
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>実行（探針）の行</summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> ExecutionRows() =>
        [
            // ===== インメモリのシード（InMemorySampleSeedExecutionTests） =====
            ExecutionRow(
                "probe-inmemory-seed-mysql-constraints",
                "InMemorySampleSeedExecutionTests.シード値は制約を守る(valueObjects: false)",
                () => ExecutionSeedConstraintDiagram(),
                ExecutionInMemoryOptions(valueObjects: false),
                DialectNames.MySql,
                [CodegenProbe.InMemorySeed],
                new CodegenProbeFacts(SeedRowCounts: ExecutionSeedConstraintRowCounts())
            ),
            ExecutionRow(
                "probe-inmemory-seed-mysql-constraints-vo",
                "InMemorySampleSeedExecutionTests.シード値は制約を守る(valueObjects: true)",
                () => ExecutionSeedConstraintDiagram(),
                ExecutionInMemoryOptions(valueObjects: true),
                DialectNames.MySql,
                [CodegenProbe.InMemorySeed],
                new CodegenProbeFacts(SeedRowCounts: ExecutionSeedConstraintRowCounts())
            ),
            ExecutionRow(
                "probe-inmemory-seed-row-count-fixed-point-cycles",
                "InMemorySampleSeedExecutionTests.シード行数は循環する参照でも決まる（シーダーのループ上限の文字列の代わりに、シードした行数と制約で観測する）",
                () => ExecutionSeedCycleDiagram(),
                ExecutionInMemoryOptions(valueObjects: false),
                DialectNames.MySql,
                [CodegenProbe.InMemorySeed],
                new CodegenProbeFacts(
                    SeedRowCounts: new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        // a は NOT NULL の bool の一意制約で 2 行・b と c はそれを参照して 2 行・自己参照の nodes は RowsPerEntity（3）行
                        ["AEntity"] = 2,
                        ["BEntity"] = 2,
                        ["CEntity"] = 2,
                        ["NodeEntity"] = 3,
                    }
                )
            ),
            // 元のテストを持たない行：修正 G の受け入れ。図の形の行列では、主の表のキーが複合の行はどの表も主の表へ
            // ぶら下がって全部 0 行になり、主キーが 1 列の表をシードして読めることを示せないので、決まった図で見る
            ExecutionRow(
                "probe-inmemory-seed-composite-key",
                "修正 G の受け入れ（元のテストなし）：主キーが 1 列でない表と、それを直接・間接に参照する表はシードせず、主キーが 1 列の表はシードして読み書きできる",
                () => ExecutionSeedCompositeKeyDiagram(),
                ExecutionInMemoryOptions(valueObjects: false),
                DialectNames.MySql,
                [CodegenProbe.InMemorySeed],
                new CodegenProbeFacts(
                    SeedRowCounts: new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["OrderEntity"] = 3,
                        ["TagEntity"] = 3,
                        // 複合主キーの中間表はシードしない
                        ["OrderTagEntity"] = 0,
                        // 中間表を NOT NULL の複合外部キーで参照する子（参照の連鎖の根は orders / tags）
                        ["OrderTagNoteEntity"] = 0,
                        // 中間表を NULL 許容の複合外部キーで参照する子
                        ["TagMemoEntity"] = 0,
                        // 中間表を間接に参照する孫
                        ["NoteReplyEntity"] = 0,
                        // 主キーの無い表
                        ["AuditLogEntity"] = 0,
                    }
                )
            ),
            // ===== EF Core のモデル =====
            ExecutionRow(
                "probe-efcore-model-vo-foreign-key-name-mismatch",
                "ValueObjectForeignKeyEfCoreModelTests.生成DbContextのモデル検証が通る（FK 列名 ≠ PK 列名・自己参照 × VO × EF Core）",
                ExecutionVoForeignKeyDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.EfCoreModel",
                    GenerateValueObjects = true,
                    GenerateEfCoreRepositories = true,
                    GenerateRepositories = false,
                    GenerateEditModels = false,
                    GenerateMappers = false,
                },
                DialectNames.SqlServer,
                [CodegenProbe.EfCoreModel],
                new CodegenProbeFacts(
                    ModelFacts:
                    [
                        EfCoreModelFact.Present("OrderEntity"),
                        EfCoreModelFact.Present("NodeEntity"),
                    ]
                )
            ),
            ExecutionRow(
                "probe-efcore-model-principal-key-not-primary",
                "EfCorePrincipalKeyModelTests.親列が主キーでないFKはその親列へ結合する",
                ExecutionPrincipalKeyDiagram,
                ExecutionEfCoreOnlyOptions(),
                DialectNames.SqlServer,
                [CodegenProbe.EfCoreModel],
                new CodegenProbeFacts(
                    ModelFacts:
                    [
                        // 1 対多: orders.CustomerCode → customers.Code（主キー Id ではない）
                        EfCoreModelFact.SingleForeignKey(
                            "OrderEntity",
                            ["Code"],
                            principalIsPrimaryKey: false,
                            dependentProperties: ["CustomerCode"]
                        ),
                        // 1 対 1: profiles.CustomerCode → customers.Code
                        EfCoreModelFact.SingleForeignKey(
                            "ProfileEntity",
                            ["Code"],
                            principalIsPrimaryKey: false
                        ),
                        // 複合主キーの一部参照: children.PartARef → parents.PartA（簡易単数形化は children を変換しない）
                        EfCoreModelFact.SingleForeignKey(
                            "ChildrenEntity",
                            ["PartA"],
                            principalIsPrimaryKey: false
                        ),
                    ]
                )
            ),
            ExecutionRow(
                "probe-efcore-model-delete-behavior",
                "EfCorePrincipalKeyModelTests.参照アクションはDeleteBehaviorへ写像される（CASCADE 以外は ClientNoAction）",
                ExecutionDeleteBehaviorDiagram,
                ExecutionEfCoreOnlyOptions(),
                DialectNames.SqlServer,
                [CodegenProbe.EfCoreModel],
                new CodegenProbeFacts(
                    ModelFacts:
                    [
                        EfCoreModelFact.SingleForeignKey(
                            "CascadeChildEntity",
                            deleteBehavior: DeleteBehavior.Cascade
                        ),
                        EfCoreModelFact.SingleForeignKey(
                            "SetnullChildEntity",
                            deleteBehavior: DeleteBehavior.ClientNoAction
                        ),
                        EfCoreModelFact.SingleForeignKey(
                            "SetdefaultChildEntity",
                            deleteBehavior: DeleteBehavior.ClientNoAction
                        ),
                        EfCoreModelFact.SingleForeignKey(
                            "NoactionChildEntity",
                            deleteBehavior: DeleteBehavior.ClientNoAction
                        ),
                    ]
                )
            ),
            ExecutionRow(
                "probe-efcore-model-keyless-table-excluded",
                "EfCoreKeylessTableModelTests.主キーなしテーブルは除外されモデルは使える（警告の名指しとモデルの部分）",
                ExecutionKeylessDiagram,
                new CodeGenerationOptions
                {
                    RootNamespace = "Test.EfCoreKeyless",
                    GenerateEfCoreRepositories = true,
                },
                DialectNames.SqlServer,
                [CodegenProbe.EfCoreModel],
                new CodegenProbeFacts(
                    ModelFacts:
                    [
                        EfCoreModelFact.Absent("LogEntity"),
                        // 親側のコレクションナビゲーション（自動発見の混入経路）もモデルへ紛れ込まない
                        EfCoreModelFact.NoNavigations("CustomerEntity"),
                    ]
                ),
                DiagnosticExpectation.Contains(
                    ExpectedDiagnostic.WarningMentioning(
                        nameof(Strings.CodeGen_Warning_EfCoreKeylessTableExcluded),
                        "logs"
                    )
                )
            ),
            // ===== 同期のキーのテキスト =====
            ExecutionRow(
                "probe-sync-key-text-decimal-key",
                "SyncDecimalKeyExecutionTests（decimal の主キーは指数表記を使わずに書き、以前の指数表記のテキストを書き直して読む・書き直しは decimal の主キーの表だけが持つ）",
                ExecutionSyncDecimalKeyDiagram,
                SyncFixtureDefinition.Options with
                {
                    RootNamespace = "QuickER.Tests.SyncDecimalKeyProbe",
                    GenerateRemoteServices = false,
                },
                DialectNames.SqlServer,
                [CodegenProbe.SyncKeyText]
            ),
            ExecutionRow(
                "probe-sync-key-text-timespan-key",
                "SyncSupportGenerationTests.SyncKeyText_TimeLikePrimaryKeys_RoundTrip(\"TimeSpan\")（型辞書の差し替えの代わりに、time の列で TimeSpan の主キーを作る）",
                ExecutionSyncTimeKeyDiagram,
                SyncFixtureDefinition.Options with
                {
                    GenerateRemoteServices = false,
                },
                DialectNames.SqlServer,
                [CodegenProbe.SyncKeyText]
            ),
        ];

    /// <summary>実行の行を組む（直接の入口・診断はエラーなしの包含が既定）</summary>
    private static ScenarioRow<CodegenCase> ExecutionRow(
        string id,
        string description,
        Func<ErDiagram> diagram,
        CodeGenerationOptions options,
        string typeDialect,
        IReadOnlyList<CodegenProbe> probes,
        CodegenProbeFacts? facts = null,
        DiagnosticExpectation? diagnostics = null
    ) =>
        new(
            id,
            null,
            description,
            new CodegenCase(
                diagram,
                options,
                typeDialect,
                diagnostics ?? DiagnosticExpectation.Contains(),
                Probes: probes,
                ProbeFacts: facts
            )
        );

    // ---- 元のテストのオプション・図（そのまま写す） ----

    /// <summary>InMemorySampleSeedExecutionTests.GenerateInMemory のオプション（インメモリ単独）</summary>
    private static CodeGenerationOptions ExecutionInMemoryOptions(bool valueObjects) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateValueObjects = valueObjects,
            GenerateInMemoryRepositories = true,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

    /// <summary>InMemorySampleSeedExecutionTests.シード値は制約を守る が名指しした行数</summary>
    private static Dictionary<string, int> ExecutionSeedConstraintRowCounts() =>
        new(StringComparer.Ordinal)
        {
            // char(2) の主キーは末尾の行番号を残して 3 行を区別する
            ["CodeEntity"] = 3,
            // 符号なし整数の主キーも行ごとに違う
            ["UnitEntity"] = 3,
            // NOT NULL の bool の一意制約は 2 種類の値しか作れない
            ["FlagEntity"] = 2,
            // 2 行しかない親を参照する子も 2 行に減る
            ["FlagItemEntity"] = 2,
            ["ItemEntity"] = 3,
        };

    /// <summary>InMemorySampleSeedExecutionTests.Column</summary>
    private static Column ExecutionColumn(
        string name,
        string dataType,
        bool key = false,
        bool nullable = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            IsPrimaryKey = key,
            IsNullable = nullable,
        };

    /// <summary>InMemorySampleSeedExecutionTests.Reference</summary>
    private static Relationship ExecutionReference(
        Entity parent,
        Column parentColumn,
        Entity child,
        Column childColumn
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new RelationshipColumnPair(parentColumn.Id, childColumn.Id)],
        };

    /// <summary>InMemorySampleSeedExecutionTests.GenerateConstraintDiagram の図（MySQL）</summary>
    private static ErDiagram ExecutionSeedConstraintDiagram()
    {
        var code = ExecutionColumn("code", "char(2)", key: true);
        var codeLabel = ExecutionColumn("label", "varchar(50)");
        var codes = new Entity { TableName = "codes", Columns = [code, codeLabel] };

        var unitId = ExecutionColumn("unit_id", "int unsigned", key: true);
        var unitShort = ExecutionColumn("short_name", "char(2)");
        var units = new Entity { TableName = "units", Columns = [unitId, unitShort] };
        units.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { unitShort.Id } });

        var flagId = ExecutionColumn("flag_id", "int", key: true);
        var flag = ExecutionColumn("flag", "tinyint(1)");
        var flags = new Entity { TableName = "flags", Columns = [flagId, flag] };
        flags.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { flag.Id } });

        var flagChildId = ExecutionColumn("flag_child_id", "int", key: true);
        var flagRef = ExecutionColumn("flag_id", "int");
        var flagChildren = new Entity
        {
            TableName = "flag_items",
            Columns = [flagChildId, flagRef],
        };

        var refId = ExecutionColumn("ref_id", "int", key: true);
        var refNo = ExecutionColumn("ref_no", "int");
        var refs = new Entity { TableName = "refs", Columns = [refId, refNo] };
        refs.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { refNo.Id } });

        var itemId = ExecutionColumn("item_id", "int", key: true);
        var itemCode = ExecutionColumn("code_value", "char(2)");
        var itemUnit = ExecutionColumn("unit_no", "int unsigned");
        var itemRef = ExecutionColumn("ref_value", "int", nullable: true);
        var items = new Entity
        {
            TableName = "items",
            Columns = [itemId, itemCode, itemUnit, itemRef],
        };

        return new ErDiagram
        {
            TargetDbms = "mysql",
            Entities = [codes, units, flags, flagChildren, refs, items],
            Relationships =
            [
                ExecutionReference(flags, flagId, flagChildren, flagRef),
                ExecutionReference(codes, code, items, itemCode),
                ExecutionReference(units, unitId, items, itemUnit),
                ExecutionReference(refs, refNo, items, itemRef),
            ],
        };
    }

    /// <summary>InMemorySampleSeedExecutionTests.シード行数は循環する参照でも決まる の図（MySQL）</summary>
    /// <remarks>
    /// a は NOT NULL の bool の一意制約で 2 行に減り、a と b は互いを参照し、nodes は自分自身を参照する。
    /// c は b を参照し、図の中で a・b より前に並ぶ＝b の行数が減ってから c へ伝わるには 2 周目が要る。
    /// </remarks>
    private static ErDiagram ExecutionSeedCycleDiagram()
    {
        var aId = ExecutionColumn("a_id", "int", key: true);
        var aFlag = ExecutionColumn("flag", "tinyint(1)");
        var aToB = ExecutionColumn("b_id", "int");
        var bId = ExecutionColumn("b_id", "int", key: true);
        var bToA = ExecutionColumn("a_id", "int");
        var nodeId = ExecutionColumn("node_id", "int", key: true);
        var parentId = ExecutionColumn("parent_id", "int", nullable: true);
        var cId = ExecutionColumn("c_id", "int", key: true);
        var cToB = ExecutionColumn("b_id", "int");

        var a = new Entity { TableName = "a", Columns = [aId, aFlag, aToB] };
        a.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { aFlag.Id } });
        var b = new Entity { TableName = "b", Columns = [bId, bToA] };
        var nodes = new Entity { TableName = "nodes", Columns = [nodeId, parentId] };
        var c = new Entity { TableName = "c", Columns = [cId, cToB] };

        return new ErDiagram
        {
            TargetDbms = "mysql",
            Entities = [c, a, b, nodes],
            Relationships =
            [
                ExecutionReference(b, bId, a, aToB),
                ExecutionReference(a, aId, b, bToA),
                ExecutionReference(nodes, nodeId, nodes, parentId),
                ExecutionReference(b, bId, c, cToB),
            ],
        };
    }

    /// <summary>修正 G の受け入れの図（MySQL）：複合主キーの中間表・それを参照する子と孫・主キーの無い表</summary>
    /// <remarks>
    /// 中間表 order_tags の主キーは orders・tags への外部キーの列でできている。order_tag_notes はそれを複合外部キーで参照するので、
    /// 参照の連鎖の根は orders / tags になる（根だけで行数を抑えると、0 行の中間表を素通りして 3 行入る）。
    /// </remarks>
    private static ErDiagram ExecutionSeedCompositeKeyDiagram()
    {
        var orderId = ExecutionColumn("order_id", "int", key: true);
        var orders = new Entity { TableName = "orders", Columns = [orderId] };
        var tagId = ExecutionColumn("tag_id", "int", key: true);
        var tags = new Entity { TableName = "tags", Columns = [tagId] };

        var linkOrder = ExecutionColumn("order_id", "int", key: true);
        var linkTag = ExecutionColumn("tag_id", "int", key: true);
        var orderTags = new Entity { TableName = "order_tags", Columns = [linkOrder, linkTag] };

        var noteId = ExecutionColumn("note_id", "int", key: true);
        var noteOrder = ExecutionColumn("order_id", "int");
        var noteTag = ExecutionColumn("tag_id", "int");
        var notes = new Entity
        {
            TableName = "order_tag_notes",
            Columns = [noteId, noteOrder, noteTag],
        };

        var memoId = ExecutionColumn("memo_id", "int", key: true);
        var memoOrder = ExecutionColumn("order_id", "int", nullable: true);
        var memoTag = ExecutionColumn("tag_id", "int", nullable: true);
        var memos = new Entity { TableName = "tag_memos", Columns = [memoId, memoOrder, memoTag] };

        var replyId = ExecutionColumn("reply_id", "int", key: true);
        var replyNote = ExecutionColumn("note_id", "int");
        var replies = new Entity { TableName = "note_replies", Columns = [replyId, replyNote] };

        var logMessage = ExecutionColumn("message", "varchar(50)");
        var logs = new Entity { TableName = "audit_logs", Columns = [logMessage] };

        Relationship CompositeReference(Entity child, Column childOrder, Column childTag) =>
            new()
            {
                SourceEntityId = orderTags.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                [
                    new RelationshipColumnPair(linkOrder.Id, childOrder.Id),
                    new RelationshipColumnPair(linkTag.Id, childTag.Id),
                ],
            };

        return new ErDiagram
        {
            TargetDbms = "mysql",
            Entities = [orders, tags, orderTags, notes, memos, replies, logs],
            Relationships =
            [
                ExecutionReference(orders, orderId, orderTags, linkOrder),
                ExecutionReference(tags, tagId, orderTags, linkTag),
                CompositeReference(notes, noteOrder, noteTag),
                CompositeReference(memos, memoOrder, memoTag),
                ExecutionReference(notes, noteId, replies, replyNote),
            ],
        };
    }

    /// <summary>EfCorePrincipalKeyModelTests.CreateOptions（EF Core 単独出力）</summary>
    private static CodeGenerationOptions ExecutionEfCoreOnlyOptions() =>
        new()
        {
            RootNamespace = "Test.EfCorePk",
            GenerateEfCoreRepositories = true,
            GenerateRepositories = false,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    /// <summary>ValueObjectForeignKeyEfCoreModelTests.BuildDiagram（自己参照・FK 列名不一致）</summary>
    private static ErDiagram ExecutionVoForeignKeyDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = new Column
        {
            Name = "order_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var shipCustomerId = new Column
        {
            Name = "ship_customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        var nodeId = new Column
        {
            Name = "node_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentNodeId = new Column
        {
            Name = "parent_node_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentNodeId } };

        return new ErDiagram
        {
            Entities = { customers, orders, nodes },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerId.Id, shipCustomerId.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = nodes.Id,
                    TargetEntityId = nodes.Id,
                    ColumnPairs = { new(nodeId.Id, parentNodeId.Id) },
                },
            },
        };
    }

    /// <summary>
    /// EfCorePrincipalKeyModelTests.BuildDiagram（UNIQUE 列参照の 1 対多・1 対 1 と、複合主キーの一部参照）
    /// </summary>
    private static ErDiagram ExecutionPrincipalKeyDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customerCode = new Column
        {
            Name = "code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var customers = new Entity
        {
            TableName = "customers",
            Columns = { customerId, customerCode },
        };
        customers.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [customerCode.Id] });

        var orderId = new Column
        {
            Name = "order_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerCode = new Column
        {
            Name = "customer_code",
            DataType = "nvarchar(20)",
            IsForeignKey = true,
            IsNullable = true,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerCode } };

        var profileId = new Column
        {
            Name = "profile_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var profileCustomerCode = new Column
        {
            Name = "customer_code",
            DataType = "nvarchar(20)",
            IsForeignKey = true,
            IsNullable = true,
        };
        var profiles = new Entity
        {
            TableName = "profiles",
            Columns = { profileId, profileCustomerCode },
        };

        var partA = new Column
        {
            Name = "part_a",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var partB = new Column
        {
            Name = "part_b",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parents = new Entity { TableName = "parents", Columns = { partA, partB } };

        var childId = new Column
        {
            Name = "child_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childPartA = new Column
        {
            Name = "part_a_ref",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var children = new Entity { TableName = "children", Columns = { childId, childPartA } };

        return new ErDiagram
        {
            Entities = { customers, orders, profiles, parents, children },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerCode.Id, orderCustomerCode.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = customers.Id,
                    TargetEntityId = profiles.Id,
                    ColumnPairs = { new(customerCode.Id, profileCustomerCode.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = parents.Id,
                    TargetEntityId = children.Id,
                    ColumnPairs = { new(partA.Id, childPartA.Id) },
                },
            },
        };
    }

    /// <summary>EfCorePrincipalKeyModelTests.参照アクションはDeleteBehaviorへ写像される の図（hub と参照アクションごとの子）</summary>
    private static ErDiagram ExecutionDeleteBehaviorDiagram()
    {
        var hubId = new Column
        {
            Name = "hub_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var hub = new Entity { TableName = "hub", Columns = { hubId } };
        var diagram = new ErDiagram { Entities = { hub } };

        foreach (
            var (table, action) in new[]
            {
                ("cascade_child", ForeignKeyReferentialAction.Cascade),
                ("setnull_child", ForeignKeyReferentialAction.SetNull),
                ("setdefault_child", ForeignKeyReferentialAction.SetDefault),
                ("noaction_child", ForeignKeyReferentialAction.NoAction),
            }
        )
        {
            var childId = new Column
            {
                Name = "row_id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
            var fkColumn = new Column
            {
                Name = "hub_id",
                DataType = "int",
                IsForeignKey = true,
                IsNullable = true,
            };
            var child = new Entity { TableName = table, Columns = { childId, fkColumn } };
            diagram.Entities.Add(child);
            diagram.Relationships.Add(
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = hub.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = { new(hubId.Id, fkColumn.Id) },
                    OnDelete = action,
                }
            );
        }

        return diagram;
    }

    /// <summary>EfCoreKeylessTableModelTests.BuildDiagram（customers〔PK あり〕→ logs〔PK なし・FK の子〕）</summary>
    private static ErDiagram ExecutionKeylessDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var logCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var logMessage = new Column
        {
            Name = "message",
            DataType = "nvarchar(200)",
            IsNullable = true,
        };
        var logs = new Entity { TableName = "logs", Columns = { logCustomerId, logMessage } };

        return new ErDiagram
        {
            Entities = { customers, logs },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = logs.Id,
                    ColumnPairs = { new(customerId.Id, logCustomerId.Id) },
                },
            },
        };
    }

    /// <summary>SyncDecimalKeyExecutionTests.Tables の図（同期のフィクスチャの図に decimal(38,30) の主キーの表 sync_amounts を足す）</summary>
    private static ErDiagram ExecutionSyncDecimalKeyDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram.Entities.Add(
            new Entity
            {
                TableName = "sync_amounts",
                Columns =
                {
                    new Column
                    {
                        Name = "amount",
                        DataType = "decimal(38,30)",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "memo",
                        DataType = "nvarchar(40)",
                        IsNullable = true,
                    },
                    new Column
                    {
                        Name = "row_ver",
                        DataType = "rowversion",
                        IsNullable = true,
                    },
                },
            }
        );
        return diagram;
    }

    /// <summary>
    /// 同期のフィクスチャの図の注文の主キーと、それを参照する外部キーの列（明細・メモ）を time にした図
    /// （元の SyncKeyText_TimeLikePrimaryKeys_RoundTrip は型辞書の主キーの型を TimeSpan へ差し替えていた。time は SQL Server・SQLite の
    /// どちらの型マッパーも TimeSpan と読むので、差し替えずに同じキーの型を作れる）
    /// </summary>
    private static ErDiagram ExecutionSyncTimeKeyDiagram()
    {
        var diagram = SyncFixtureDefinition.Build();

        foreach (
            var column in diagram
                .Entities.SelectMany(entity => entity.Columns)
                .Where(column => column.Name == "order_id")
        )
        {
            column.DataType = "time";
        }

        return diagram;
    }
}
