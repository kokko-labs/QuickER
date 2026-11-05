using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>保存と読込の往復・JSON の正準形（null のキーを省略する・スキーマのみ文書は layout を省く）</summary>
    private static IEnumerable<ScenarioRow<FormatCase>> RoundTripRows() =>
        [
            Row(
                "roundtrip-entities-and-relationships-restored",
                "Save → Load でエンティティとリレーションが復元される",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = BuildViewModelDocument,
                    Expect = o =>
                    {
                        // 組んだ図（Document の工場が返したもの）から、元のテストが持っていた Id を引き直す
                        var a = o.Written!.Schema.Entities.First(e => e.TableName == "Customer");
                        var b = o.Written.Schema.Entities.First(e => e.Id != a.Id);

                        File.Exists(o.Path).Should().BeTrue();

                        var loaded = o.Loaded!;
                        loaded.Schema.Entities.Should().HaveCount(2);
                        loaded.Schema.Relationships.Should().HaveCount(1);

                        // 意味情報は schema、視覚情報は layout サイドカーへ分離して往復する
                        var ea = loaded.Schema.Entities.First(e => e.Id == a.Id);
                        ea.TableName.Should().Be("Customer");

                        var la = loaded.Layout!.Should().ContainKey(a.Id).WhoseValue;
                        la.X.Should().Be(100);
                        la.Y.Should().Be(50);
                        la.TitleBackgroundColor.Should().Be("#FFF0BF");

                        loaded.Schema.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
                        loaded
                            .Schema.Relationships[0]
                            .ColumnPairs[0]
                            .SourceColumnId.Should()
                            .Be(a.Columns[0].Id);
                        loaded
                            .Schema.Relationships[0]
                            .ColumnPairs[0]
                            .TargetColumnId.Should()
                            .Be(b.Columns[1].Id);
                        loaded
                            .Schema.Relationships[0]
                            .ConstraintName.Should()
                            .Be("FK_Order_Customer");
                        loaded
                            .Schema.Entities.First(e => e.Id == b.Id)
                            .Columns[1]
                            .IsNullable.Should()
                            .BeFalse();
                    },
                }
            ),
            Row(
                "roundtrip-query-definition-all-fields-restored",
                "Save → Load で名前付きクエリ定義が全フィールド復元される",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = BuildQueryDocument,
                    Expect = o =>
                    {
                        var entity = o.Written!.Schema.Entities[0];
                        var amountColumn = entity.Columns[0];
                        var query = o.Written.Schema.Queries[0];

                        var loaded = o.Loaded!.Schema.Queries.Should().ContainSingle().Which;

                        loaded.Id.Should().Be(query.Id);
                        loaded.EntityId.Should().Be(entity.Id);
                        loaded.Name.Should().Be("GetByCustomer");
                        loaded.Description.Should().Be("顧客IDで注文を検索する");
                        loaded.Returns.Should().Be(QueryReturnShape.Projection);
                        loaded.ScalarType.Should().BeNull();
                        loaded.Parameters.Should().HaveCount(2);
                        loaded.Parameters[0].SourceColumnId.Should().Be(amountColumn.Id);
                        loaded.Parameters[1].Name.Should().Be("statuses");
                        loaded.Parameters[1].IsList.Should().BeTrue();
                        loaded.Parameters[1].SourceColumnId.Should().BeNull();
                        loaded
                            .Condition.Should()
                            .Be("CustomerId = @customerId AND Status IN @statuses");
                        loaded.OrderBy.Should().ContainSingle();
                        loaded.OrderBy[0].ColumnId.Should().Be(amountColumn.Id);
                        loaded.OrderBy[0].Descending.Should().BeTrue();
                        loaded.HasPaging.Should().BeTrue();
                        loaded.Implementation.Should().Be(QueryImplementationKind.Sql);
                        loaded.Sql.Should().HaveCount(2).And.ContainKey("sqlite");
                        loaded.ResultTypeName.Should().Be("OrderSummaryRow");
                        loaded.Fields.Should().ContainSingle();
                        loaded.Fields[0].SourceColumnId.Should().Be(amountColumn.Id);
                    },
                }
            ),
            Row(
                "roundtrip-null-properties-omitted-from-json",
                "Save: null プロパティはキーごと省略される",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = BuildNullPropertiesDocument,
                    Expect = o =>
                    {
                        var json = o.FileText!;

                        json.Should()
                            .NotContain("\"ScalarType\"", "null プロパティはキーごと省略される");
                        json.Should().NotContain("\"Type\": null");
                        json.Should().NotContain("\"ConstraintName\"");
                        json.Should().NotContain("\"IsNullable\": null");

                        // 読み戻しでは省略されたキーが既定値（null）へ戻る
                        var column = o.Written!.Schema.Entities[0].Columns[0];
                        var loaded = o.Loaded!;
                        var query = loaded.Schema.Queries.Should().ContainSingle().Which;
                        query.Parameters[0].Type.Should().BeNull();
                        query.Parameters[0].SourceColumnId.Should().Be(column.Id);
                        query.Fields[0].Type.Should().BeNull();
                        query.Fields[0].IsNullable.Should().BeNull();
                        loaded.Schema.Relationships[0].ConstraintName.Should().BeNull();
                    },
                }
            ),
            Row(
                "saveatomic-schema-only-document-omits-layout-key",
                "Save: スキーマのみ文書（Layout = null）は layout キーを省略する",
                new FormatCase
                {
                    Stage = FormatStage.SaveAtomic,
                    Result = FormatResult.Written,
                    Document = () =>
                    {
                        var entity = new Entity { TableName = "Order" };
                        var document = new DiagramDocument
                        {
                            Schema = new ErDiagram { Entities = { entity } },
                            Layout = null,
                        };
                        document.Schema.Queries.Add(
                            new QueryDefinition { EntityId = entity.Id, Name = "GetAll" }
                        );

                        return document;
                    },
                    Expect = o =>
                    {
                        var json = o.FileText!;

                        json.Should()
                            .NotContain("\"Layout\"", "スキーマのみ文書は layout キーを出力しない");
                        json.Should().Contain("\"Version\"");
                        json.Should().Contain("\"Schema\"");
                        json.Should().Contain("\"Queries\"");
                        json.Should().Contain("GetAll", "クエリ定義は schema 内に出力される");
                    },
                }
            ),
        ];

    /// <summary>MainViewModel で組んだ 2 テーブル・1 リレーションの図（座標・色・列ペア・制約名つき）</summary>
    private static DiagramDocument BuildViewModelDocument()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        var a = vm.Entities[0];
        var b = vm.Entities[1];
        a.TableName = "Customer";
        a.X = 100;
        a.Y = 50;
        a.TitleBackgroundColor = "#FFF0BF";

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);
        b.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsNullable = false,
                }
            )
        );
        vm.Relationships[0]
            .SetColumnPairs([new RelationshipColumnPair(a.Columns[0].Id, b.Columns[1].Id)]);
        vm.Relationships[0].ConstraintName = "FK_Order_Customer";

        return vm.ToDocument();
    }

    /// <summary>全フィールドを埋めた名前付きクエリ 1 件を持つ図</summary>
    private static DiagramDocument BuildQueryDocument()
    {
        var entity = new Entity { TableName = "Order" };
        var amountColumn = new Column { Name = "Amount", DataType = "decimal(12,2)" };
        entity.Columns.Add(amountColumn);

        var query = new QueryDefinition
        {
            EntityId = entity.Id,
            Name = "GetByCustomer",
            Description = "顧客IDで注文を検索する",
            Returns = QueryReturnShape.Projection,
            ScalarType = null,
            Parameters =
            {
                new QueryParameter
                {
                    Name = "customerId",
                    Type = "int32",
                    SourceColumnId = amountColumn.Id,
                },
                new QueryParameter
                {
                    Name = "statuses",
                    Type = "int32",
                    IsList = true,
                },
            },
            Condition = "CustomerId = @customerId AND Status IN @statuses",
            OrderBy =
            {
                new QueryOrdering { ColumnId = amountColumn.Id, Descending = true },
            },
            HasPaging = true,
            Implementation = QueryImplementationKind.Sql,
            Sql = { ["sqlserver"] = "SELECT ...", ["sqlite"] = "SELECT ... LIMIT ..." },
            ResultTypeName = "OrderSummaryRow",
            Fields =
            {
                new ProjectionField
                {
                    Name = "TotalAmount",
                    Type = "decimal(12,2)",
                    SourceColumnId = amountColumn.Id,
                },
            },
        };

        var document = new DiagramDocument();
        document.Schema.Entities.Add(entity);
        document.Schema.Queries.Add(query);

        return document;
    }

    /// <summary>null になり得るプロパティ（列参照パラメータの Type・未指定の ScalarType / ConstraintName 等）を未指定のまま持つ図</summary>
    private static DiagramDocument BuildNullPropertiesDocument()
    {
        var entity = new Entity { TableName = "Order" };
        var column = new Column { Name = "CustomerId", DataType = "int" };
        entity.Columns.Add(column);

        var document = new DiagramDocument();
        document.Schema.Entities.Add(entity);
        document.Schema.Relationships.Add(
            new Relationship
            {
                SourceEntityId = entity.Id,
                TargetEntityId = entity.Id,
                // SourceColumnId / TargetColumnId / ConstraintName は未指定（null）
            }
        );
        document.Schema.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "GetByCustomer",
                Parameters =
                {
                    // 列参照型付け＝Type は保存されない（null）
                    new QueryParameter { Name = "customerId", SourceColumnId = column.Id },
                },
                Fields =
                {
                    new ProjectionField { Name = "CustomerId", SourceColumnId = column.Id },
                },
            }
        );

        return document;
    }
}
