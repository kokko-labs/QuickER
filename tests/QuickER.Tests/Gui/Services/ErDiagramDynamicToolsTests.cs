using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Mcp;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="ErDiagramDynamicTools"/> の各ツール実行（エンティティ・カラム操作）を検証するテストクラス</summary>
public class ErDiagramDynamicToolsTests
{
    /// <summary>テスト対象のメイン ViewModel を生成する</summary>
    private static MainViewModel CreateVm() => new MainViewModel();

    /// <summary>引数オブジェクトを JSON 化してツールを実行し、結果と成否を返す</summary>
    private static (string Result, bool Success) Exec(
        MainViewModel vm,
        string toolName,
        object args
    )
    {
        var json = JsonSerializer.Serialize(args);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return ErDiagramDynamicTools.Execute(toolName, element, vm);
    }

    /// <summary>add_entity でテーブルが追加され、列が自動生成されないことを検証する</summary>
    [Fact(DisplayName = "add_entity でテーブルが追加され列は自動生成されない")]
    public void AddEntity_CreatesEntityWithNoColumns()
    {
        var vm = CreateVm();

        var (_, success) = Exec(vm, "add_entity", new { table_name = "Book" });

        success.Should().BeTrue();
        var entity = vm.Entities.Single(e => e.TableName == "Book");
        // 列は AI が add_column で定義する想定のため、ここでは自動生成されない
        entity.Columns.Should().BeEmpty();
    }

    /// <summary>add_column で is_primary_key=true の列が主キーとして追加されることを検証する</summary>
    [Fact(DisplayName = "add_column で is_primary_key=true の列を追加できる")]
    public void AddColumn_WithIsPrimaryKeyTrue_SetsPrimaryKey()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        var (_, success) = Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        success.Should().BeTrue();
        var entity = vm.Entities.Single(e => e.TableName == "Book");
        entity.Columns.Single(c => c.Name == "BookId").IsPrimaryKey.Should().BeTrue();
    }

    /// <summary>add_column を複数回呼ぶと指定どおりの列構成（主キーは 1 列）になることを検証する</summary>
    [Fact(DisplayName = "add_column を複数回呼び出すと AI が設計した列構成になる")]
    public void AddColumn_MultipleColumns_ReflectsAiDesign()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(200)",
                is_primary_key = false,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "AuthorId",
                data_type = "int",
                is_primary_key = false,
                is_nullable = false,
            }
        );

        var entity = vm.Entities.Single(e => e.TableName == "Book");

        // PK は BookId のみ
        entity.Columns.Where(c => c.IsPrimaryKey).Should().ContainSingle(c => c.Name == "BookId");
        entity.Columns.Should().HaveCount(3);
    }

    /// <summary>set_column_property でカラム説明を更新できることを検証する</summary>
    [Fact(DisplayName = "set_column_property でカラムの説明を更新できる")]
    public void SetColumnProperty_UpdatesDescription()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
                description = "",
            }
        );

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                description = "書籍を一意に識別するID",
            }
        );

        success.Should().BeTrue();
        var column = vm
            .Entities.Single(e => e.TableName == "Book")
            .Columns.Single(c => c.Name == "BookId");
        column.Description.Should().Be("書籍を一意に識別するID");
    }

    /// <summary>set_column_property でデータ型と NULL 許容を同時更新できることを検証する</summary>
    [Fact(DisplayName = "set_column_property でデータ型と NULL 許容を更新できる")]
    public void SetColumnProperty_UpdatesDataTypeAndNullable()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(100)",
                is_primary_key = false,
                is_nullable = false,
            }
        );

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(500)",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        var column = vm
            .Entities.Single(e => e.TableName == "Book")
            .Columns.Single(c => c.Name == "Title");
        column.DataType.Should().Be("nvarchar(500)");
        column.IsNullable.Should().BeTrue();
    }

    /// <summary>存在しないカラムを set_column_property で指定すると失敗を返すことを検証する</summary>
    [Fact(DisplayName = "set_column_property で存在しないカラムを指定するとエラーになる")]
    public void SetColumnProperty_UnknownColumn_ReturnsError()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "NoSuchColumn",
                description = "説明",
            }
        );

        success.Should().BeFalse();
    }

    /// <summary>テスト用に親テーブル（PK 付き）と子テーブル（PK + 任意列）を作成する</summary>
    private static void SetupParentAndChild(MainViewModel vm, string childExtraColumn)
    {
        Exec(vm, "add_entity", new { table_name = "Customer" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(vm, "add_entity", new { table_name = "Order" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "OrderId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = childExtraColumn,
                data_type = "int",
                is_primary_key = false,
                is_nullable = true,
            }
        );
    }

    /// <summary>add_relationship で明示指定した source_columns / target_columns が使用されることを検証する</summary>
    [Fact(DisplayName = "add_relationship で明示指定したカラムがそのまま使用される")]
    public void AddRelationship_WithExplicitColumns_UsesSpecifiedColumns()
    {
        var vm = CreateVm();

        // FK 列名が命名規則（CustomerId）と異なる OwnerId のケース
        SetupParentAndChild(vm, childExtraColumn: "OwnerId");

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                source_columns = new[] { "CustomerId" },
                target_table = "Order",
                target_columns = new[] { "OwnerId" },
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        var relationship = vm.Relationships.Single();
        var order = vm.Entities.Single(e => e.TableName == "Order");
        var pair = relationship.ColumnPairs.Should().ContainSingle().Subject;
        pair.SourceColumnId.Should()
            .Be(
                vm.Entities.Single(e => e.TableName == "Customer")
                    .Columns.Single(c => c.Name == "CustomerId")
                    .Id
            );
        pair.TargetColumnId.Should().Be(order.Columns.Single(c => c.Name == "OwnerId").Id);
    }

    /// <summary>add_relationship で存在しないカラムを指定するとエラーになることを検証する</summary>
    [Fact(DisplayName = "add_relationship で存在しない target_columns を指定するとエラーになる")]
    public void AddRelationship_UnknownTargetColumn_ReturnsError()
    {
        var vm = CreateVm();
        SetupParentAndChild(vm, childExtraColumn: "OwnerId");

        var (result, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                source_columns = new[] { "CustomerId" },
                target_table = "Order",
                target_columns = new[] { "NoSuchColumn" },
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("NoSuchColumn");
        vm.Relationships.Should().BeEmpty();
    }

    /// <summary>複合外部キー（並行配列）が宣言順の列ペアとして登録されることを検証する</summary>
    [Fact(DisplayName = "add_relationship は複合外部キーを宣言順の列ペアで登録する")]
    public void AddRelationship_CompositeColumns_RegistersOrderedPairs()
    {
        var vm = CreateVm();
        SetupCompositeParentAndChild(vm);

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId", "RegionCode" },
                target_table = "TenantUser",
                target_columns = new[] { "TenantRef", "RegionRef" },
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();

        var tenant = vm.Entities.Single(e => e.TableName == "TenantRegion");
        var user = vm.Entities.Single(e => e.TableName == "TenantUser");
        var relationship = vm.Relationships.Single();

        relationship.ColumnPairs.Should().HaveCount(2);
        relationship
            .ColumnPairs[0]
            .SourceColumnId.Should()
            .Be(tenant.Columns.Single(c => c.Name == "TenantId").Id);
        relationship
            .ColumnPairs[0]
            .TargetColumnId.Should()
            .Be(user.Columns.Single(c => c.Name == "TenantRef").Id);
        relationship
            .ColumnPairs[1]
            .SourceColumnId.Should()
            .Be(tenant.Columns.Single(c => c.Name == "RegionCode").Id);
        relationship
            .ColumnPairs[1]
            .TargetColumnId.Should()
            .Be(user.Columns.Single(c => c.Name == "RegionRef").Id);

        // 構成列はすべて FK としてロックされる
        user.Columns.Single(c => c.Name == "TenantRef").IsForeignKey.Should().BeTrue();
        user.Columns.Single(c => c.Name == "RegionRef").IsForeignKey.Should().BeTrue();
    }

    /// <summary>並行配列の長さ不一致・片側のみ指定がエラーになることを検証する</summary>
    [Fact(DisplayName = "add_relationship は列配列の長さ不一致と片側のみの指定をエラーにする")]
    public void AddRelationship_InvalidColumnArrays_ReturnError()
    {
        var vm = CreateVm();
        SetupCompositeParentAndChild(vm);

        var mismatch = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId", "RegionCode" },
                target_table = "TenantUser",
                target_columns = new[] { "TenantRef" },
                relationship_type = "OneToMany",
            }
        );
        mismatch.Success.Should().BeFalse();

        var oneSided = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId" },
                target_table = "TenantUser",
                relationship_type = "OneToMany",
            }
        );
        oneSided.Success.Should().BeFalse();

        var duplicated = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId", "TenantId" },
                target_table = "TenantUser",
                target_columns = new[] { "TenantRef", "RegionRef" },
                relationship_type = "OneToMany",
            }
        );
        duplicated.Success.Should().BeFalse();

        vm.Relationships.Should().BeEmpty();
    }

    /// <summary>列省略時に親 PK の全列が自動でペア化されることを検証する</summary>
    [Fact(DisplayName = "add_relationship は列省略時に親 PK 全列を自動ペア化する")]
    public void AddRelationship_OmittedColumns_PairsEveryPrimaryKeyColumn()
    {
        var vm = CreateVm();

        // 親の PK 2 列（TenantId / RegionCode）に対し、子側は命名規則どおりの列を持つ
        SetupCompositeParentAndChild(vm, childColumns: ["TenantId", "RegionCode"]);

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                target_table = "TenantUser",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();

        var user = vm.Entities.Single(e => e.TableName == "TenantUser");
        var relationship = vm.Relationships.Single();

        relationship
            .ColumnPairs.Select(pair => user.Columns.Single(c => c.Id == pair.TargetColumnId).Name)
            .Should()
            .Equal("TenantId", "RegionCode");
    }

    /// <summary>同じテーブル対に複数のリレーションがある場合、remove_relationship は constraint_name を要求する</summary>
    [Fact(DisplayName = "remove_relationship は複数一致時に候補を挙げてエラーにする")]
    public void RemoveRelationship_MultipleMatches_RequiresConstraintName()
    {
        var vm = CreateVm();
        SetupCompositeParentAndChild(vm);

        Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId" },
                target_table = "TenantUser",
                target_columns = new[] { "TenantRef" },
                relationship_type = "OneToMany",
            }
        )
            .Success.Should()
            .BeTrue();

        // 2 本目も同じ向きに張り、制約名だけを変えて区別できるようにする
        Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "RegionCode" },
                target_table = "TenantUser",
                target_columns = new[] { "RegionRef" },
                relationship_type = "OneToMany",
            }
        )
            .Success.Should()
            .BeTrue();
        vm.Relationships[1].ConstraintName = "FK_TenantUser_TenantRegion_Region";

        var ambiguous = Exec(
            vm,
            "remove_relationship",
            new { source_table = "TenantRegion", target_table = "TenantUser" }
        );

        ambiguous.Success.Should().BeFalse();
        ambiguous.Result.Should().Contain("FK_TenantUser_TenantRegion_Region");
        vm.Relationships.Should().HaveCount(2);

        var removed = Exec(
            vm,
            "remove_relationship",
            new
            {
                source_table = "TenantRegion",
                target_table = "TenantUser",
                constraint_name = "FK_TenantUser_TenantRegion_Region",
            }
        );

        removed.Success.Should().BeTrue();
        vm.Relationships.Should().ContainSingle();
        vm.Relationships[0].ConstraintName.Should().Be("FK_TenantUser_TenantRegion");
    }

    /// <summary>get_diagram_summary が複合外部キーの列ペアを表示することを検証する</summary>
    [Fact(DisplayName = "get_diagram_summary は外部キーの列ペアを表示する")]
    public void GetDiagramSummary_ShowsColumnPairs()
    {
        var vm = CreateVm();
        SetupCompositeParentAndChild(vm);

        Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "TenantRegion",
                source_columns = new[] { "TenantId", "RegionCode" },
                target_table = "TenantUser",
                target_columns = new[] { "TenantRef", "RegionRef" },
                relationship_type = "OneToMany",
            }
        );

        var (result, success) = Exec(vm, "get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Should().Contain("FK: (TenantId → TenantRef, RegionCode → RegionRef)");
    }

    /// <summary>複合主キーの親テーブルと、対応する子テーブルを用意する</summary>
    private static void SetupCompositeParentAndChild(
        MainViewModel vm,
        string[]? childColumns = null
    )
    {
        Exec(vm, "add_entity", new { table_name = "TenantRegion" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantRegion",
                column_name = "TenantId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantRegion",
                column_name = "RegionCode",
                data_type = "nvarchar(10)",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(vm, "add_entity", new { table_name = "TenantUser" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantUser",
                column_name = "TenantUserId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        foreach (var columnName in childColumns ?? ["TenantRef", "RegionRef"])
        {
            Exec(
                vm,
                "add_column",
                new
                {
                    table_name = "TenantUser",
                    column_name = columnName,
                    data_type = "nvarchar(10)",
                    is_primary_key = false,
                    is_nullable = false,
                }
            );
        }
    }

    /// <summary>カラム省略時に名前から FK を推測できない場合、無関係な列が FK 化されず未割当となることを検証する</summary>
    [Fact(DisplayName = "add_relationship でカラム省略かつ推測不能な場合は参照先列が未割当となる")]
    public void AddRelationship_NoLikelyColumn_LeavesTargetUnassigned()
    {
        var vm = CreateVm();

        // FK らしい名前の列が無い（従来は先頭の非 PK 列 Quantity がフォールバックで FK 化されていた）
        SetupParentAndChild(vm, childExtraColumn: "Quantity");

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        var relationship = vm.Relationships.Single();
        relationship.ColumnPairs.Should().BeEmpty();

        var quantityColumn = vm
            .Entities.Single(e => e.TableName == "Order")
            .Columns.Single(c => c.Name == "Quantity");
        quantityColumn.IsForeignKey.Should().BeFalse();
        quantityColumn.IsNullable.Should().BeTrue();
    }

    /// <summary>AI ツールで FK 列を削除し Undo すると、列と併せてリレーションの FK 参照も復元されることを検証する</summary>
    [Fact(DisplayName = "remove_column で削除した FK 列は Undo でリレーションの参照も復元される")]
    public void RemoveColumn_UsedAsFk_UndoRestoresRelationshipReference()
    {
        var vm = CreateVm();

        // 親（PK）と子（PK + FK 候補列）を作成し、ツール経由でリレーションを張る
        Exec(vm, "add_entity", new { table_name = "Customer" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(vm, "add_entity", new { table_name = "Order" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "OrderId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = false,
                is_nullable = false,
            }
        );
        Exec(vm, "add_relationship", new { source_table = "Customer", target_table = "Order" });

        var relationship = vm.Relationships.Single();
        var originalTargetColumnId = relationship
            .ColumnPairs.Should()
            .ContainSingle("add_relationship が FK 列を参照先として解決する")
            .Subject.TargetColumnId;

        var (_, success) = Exec(
            vm,
            "remove_column",
            new { table_name = "Order", column_name = "CustomerId" }
        );
        success.Should().BeTrue();

        // 削除後はリレーションの列ペアがクリアされる
        relationship.ColumnPairs.Should().BeEmpty();

        // Undo でカラムが復元され、リレーションの列ペアも復元される
        vm.UndoCommand.Execute(null);
        var order = vm.Entities.Single(e => e.TableName == "Order");
        order.Columns.Should().Contain(c => c.Id == originalTargetColumnId);
        relationship
            .ColumnPairs.Should()
            .ContainSingle()
            .Subject.TargetColumnId.Should()
            .Be(originalTargetColumnId);
    }

    /// <summary>
    /// ツール説明文（英語）に複合主キー禁止と、複合外部キーの指定方法が含まれることを検証する
    /// （AI への指示はツール説明文経由のため）
    /// </summary>
    [Fact(DisplayName = "ツール説明文に複合PK禁止と複合FKの指定方法が含まれる")]
    public void GetDefinitions_DescriptionsContainCompositeKeyProhibition()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();

        definitions
            .Single(d => d.Name == "add_column")
            .Description.Should()
            .Contain("composite primary keys are not allowed");
        definitions
            .Single(d => d.Name == "add_relationship")
            .Description.Should()
            .Contain("two or more entries define a composite foreign key");
        definitions
            .Single(d => d.Name == "add_entity")
            .Description.Should()
            .Contain("exactly one primary key column");
    }

    /// <summary>OpenAI Function Calling 用の ChatTool 変換が、全ツールを名前付きで生成することを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は全ツール定義を ChatTool へ変換する")]
    public void ToOpenAiTools_ConvertsAllDefinitions()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();
        var tools = ChatToolConverter.ToOpenAiTools(definitions);

        tools.Should().HaveCount(definitions.Count);
        tools.Select(t => t.FunctionName).Should().BeEquivalentTo(definitions.Select(d => d.Name));
    }

    /// <summary>Anthropic (Claude) 用の Tool 変換が、全ツールを名前・説明・スキーマ付きで生成することを検証する</summary>
    [Fact(DisplayName = "ToAnthropicTools は全ツール定義を Anthropic Tool へ変換する")]
    public void ToAnthropicTools_ConvertsAllDefinitions()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();
        var tools = ChatToolConverter.ToAnthropicTools(definitions);

        tools.Should().HaveCount(definitions.Count);
        tools.Select(t => t.Name).Should().BeEquivalentTo(definitions.Select(d => d.Name));
        tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Description));

        // input_schema は object 型で、required・properties が元定義どおりに引き継がれること
        var addRelationship = tools.Single(t => t.Name == "add_relationship");
        addRelationship.InputSchema.Type.GetString().Should().Be("object");
        addRelationship
            .InputSchema.Required.Should()
            .BeEquivalentTo(["source_table", "target_table", "relationship_type"]);
        addRelationship.InputSchema.Properties.Should().ContainKey("source_table");
        addRelationship.InputSchema.Properties.Should().ContainKey("relationship_type");
    }
}
