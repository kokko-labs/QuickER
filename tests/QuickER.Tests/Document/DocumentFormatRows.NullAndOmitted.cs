using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>キー欠落・明示 null・null 要素・layout の欠落を既定値へ吸収する読込</summary>
    private static IEnumerable<ScenarioRow<FormatCase>> NullAndOmittedRows() =>
        [
            Row(
                "load-omitted-keys-fall-back-to-defaults",
                "Load: キー欠落（null 省略形式）は既定値で吸収される",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "Entities": [
                          {
                            "Id": "11111111-0000-0000-0000-000000000001",
                            "TableName": "Customer",
                            "Columns": [
                              { "Id": "11111111-0000-0000-0000-000000000002", "Name": "CustomerId", "DataType": "int" }
                            ]
                          }
                        ],
                        "Relationships": [
                          {
                            "SourceEntityId": "11111111-0000-0000-0000-000000000001",
                            "TargetEntityId": "11111111-0000-0000-0000-000000000001"
                          }
                        ],
                        "Queries": [
                          {
                            "EntityId": "11111111-0000-0000-0000-000000000001",
                            "Name": "GetById",
                            "Parameters": [
                              { "Name": "id", "SourceColumnId": "11111111-0000-0000-0000-000000000002" }
                            ]
                          }
                        ]
                      },
                      "Layout": {
                        "11111111-0000-0000-0000-000000000001": { "X": 10 }
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        var loaded = o.Loaded!;

                        // Entity / Relationship: 省略キーは既定値（Memo / Description は空文字・列ペアは空・制約名は null）
                        var entity = loaded.Schema.Entities.Should().ContainSingle().Which;
                        entity.Memo.Should().BeEmpty();
                        entity.Description.Should().BeEmpty();
                        var relationship = loaded
                            .Schema.Relationships.Should()
                            .ContainSingle()
                            .Which;
                        relationship.Type.Should().Be(RelationshipType.OneToMany);
                        relationship.ColumnPairs.Should().BeEmpty();
                        relationship.ConstraintName.Should().BeNull();

                        // Query: 省略キーは既定値（Type / Condition / ScalarType は null・コレクションは空）
                        var query = loaded.Schema.Queries.Should().ContainSingle().Which;
                        query.Returns.Should().Be(QueryReturnShape.List);
                        query.Parameters[0].Type.Should().BeNull();
                        query.Condition.Should().BeNull();
                        query.ScalarType.Should().BeNull();
                        query.OrderBy.Should().BeEmpty();
                        query.Fields.Should().BeEmpty();

                        // Layout: 省略キーは既定値（Width 200・既定タイトル色）
                        var layout = loaded.Layout!.Should().ContainKey(entity.Id).WhoseValue;
                        layout.X.Should().Be(10);
                        layout.Width.Should().Be(200);
                        layout
                            .TitleBackgroundColor.Should()
                            .Be(EntityLayout.DefaultTitleBackgroundColor);
                    },
                }
            ),
            Row(
                "load-legacy-json-without-queries-yields-empty-queries",
                "Load: queries が無い既存 JSON は空のクエリ一覧になる",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "Entities": [ { "TableName": "Customer" } ],
                        "Relationships": [],
                        "TargetDbms": "sqlserver"
                      },
                      "Layout": {}
                    }
                    """,
                    Expect = o =>
                    {
                        o.Loaded!.Schema.Entities.Should().ContainSingle();
                        o.Loaded.Schema.Queries.Should().BeEmpty();
                    },
                }
            ),
            Row(
                "load-explicit-nulls-normalized-to-defaults",
                "Load: 明示 null のコレクション・TargetDbms は既定値へ正規化される",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": null,
                        "Entities": null,
                        "Relationships": null,
                        "Queries": null
                      },
                      "Layout": null
                    }
                    """,
                    Expect = o =>
                    {
                        var loaded = o.Loaded!;

                        // 図を消費する側（GUI の Count 参照・生成器の列挙）が NRE で落ちないこと
                        loaded.Schema.Entities.Should().NotBeNull().And.BeEmpty();
                        loaded.Schema.Relationships.Should().NotBeNull().And.BeEmpty();
                        loaded.Schema.Queries.Should().NotBeNull().And.BeEmpty();
                        loaded
                            .Schema.TargetDbms.Should()
                            .Be("sqlserver", "方言解決の起点は null を許さない");

                        // layout の null は「スキーマのみ文書」の正当な表現なのでそのまま残す
                        loaded.Layout.Should().BeNull();
                    },
                }
            ),
            Row(
                "load-null-elements-removed",
                "Load: リスト・辞書内の null 要素は取り除かれる",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlite",
                        "Entities": [
                          null,
                          {
                            "Id": "33333333-0000-0000-0000-000000000001",
                            "TableName": "Customer",
                            "Columns": [ null, { "Name": "CustomerId", "DataType": "int" } ]
                          },
                          {
                            "Id": "33333333-0000-0000-0000-000000000002",
                            "TableName": "Order",
                            "Columns": null
                          }
                        ],
                        "Relationships": [ null ],
                        "Queries": [
                          null,
                          {
                            "EntityId": "33333333-0000-0000-0000-000000000001",
                            "Name": "GetAll",
                            "Parameters": [ null ],
                            "OrderBy": null,
                            "Fields": [ null ],
                            "Sql": { "sqlite": null }
                          }
                        ]
                      },
                      "Layout": {
                        "33333333-0000-0000-0000-000000000001": null,
                        "33333333-0000-0000-0000-000000000002": { "X": 10 }
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        var loaded = o.Loaded!;

                        loaded.Schema.Entities.Should().HaveCount(2);
                        loaded.Schema.Entities[0].Columns.Should().ContainSingle();
                        loaded.Schema.Entities[1].Columns.Should().NotBeNull().And.BeEmpty();
                        loaded.Schema.Relationships.Should().BeEmpty();

                        var query = loaded.Schema.Queries.Should().ContainSingle().Which;
                        query.Parameters.Should().BeEmpty();
                        query.OrderBy.Should().NotBeNull().And.BeEmpty();
                        query.Fields.Should().BeEmpty();
                        query
                            .Sql.Should()
                            .NotBeNull()
                            .And.BeEmpty("値が null の方言 SQL は保持しない");

                        // layout は値が null のエントリだけを落とし、正当な配置は残す
                        loaded.Layout.Should().HaveCount(1);
                        loaded
                            .Layout![new Guid("33333333-0000-0000-0000-000000000002")]
                            .X.Should()
                            .Be(10);
                    },
                }
            ),
            Row(
                "load-null-schema-yields-empty-schema",
                "Load: schema が null の文書は空スキーマへ正規化される",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """{ "Version": 1, "Schema": null }""",
                    Expect = o =>
                    {
                        o.Loaded!.Schema.Should().NotBeNull();
                        o.Loaded.Schema.Entities.Should().BeEmpty();
                        o.Loaded.Schema.TargetDbms.Should().Be("sqlserver");
                    },
                }
            ),
            Row(
                "load-missing-layout-key-yields-null-or-empty-layout",
                "Load: layout キーの無い JSON は null または空 layout として読める",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "Entities": [ { "TableName": "Customer" } ],
                        "Relationships": [],
                        "TargetDbms": "sqlserver"
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        o.Loaded!.Schema.Entities.Should().ContainSingle();
                        (o.Loaded.Layout is null or { Count: 0 }).Should().BeTrue();
                    },
                }
            ),
        ];
}
