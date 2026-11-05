using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>
    /// 計算列・採番列の印の往復と旧形式との互換
    /// </summary>
    /// <remarks>
    /// <see cref="DiagramDocument.Version"/> は据え置き（1 のまま）。旧形式 JSON はプロパティ欠落＝
    /// <c>false</c> として読め、失われるのは「書き込みから外す」指定だけ（列そのものは残る）。
    /// <c>bool</c> なので明示的な <c>null</c> は System.Text.Json が型不一致として拒否する（修復口がない）。
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> ColumnFlagRows() =>
        [
            Row(
                "roundtrip-computed-column-flag-preserved",
                "Save → Load で計算列フラグが往復する",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                        FlagDocument(
                            new Column
                            {
                                Name = "qty",
                                DataType = "int",
                                IsComputed = false,
                            },
                            new Column
                            {
                                Name = "total",
                                DataType = "decimal(21,2)",
                                IsComputed = true,
                            }
                        ),
                    Expect = o =>
                    {
                        var loadedEntity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        loadedEntity.Columns.Select(c => c.IsComputed).Should().Equal(false, true);
                    },
                }
            ),
            Row(
                "load-legacy-json-without-is-computed-yields-false",
                "旧形式 JSON（IsComputed なし）は false として読める",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = LegacyColumnFlagJson(),
                    Expect = o =>
                    {
                        var column = o.Loaded!.Schema.Entities.Should()
                            .ContainSingle()
                            .Which.Columns.Should()
                            .ContainSingle()
                            .Which;
                        // 列そのものは残り、落ちるのは「書き込みから外す」指定だけ
                        column.Name.Should().Be("total");
                        column.IsComputed.Should().BeFalse();
                    },
                }
            ),
            Row(
                "tryload-explicit-null-is-computed-rejected-as-invalid-json",
                "明示的な null の IsComputed は読込を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = NullColumnFlagJson("IsComputed"),
                    Expect = o => o.Error.Should().Be(DocumentLoadError.InvalidJson),
                }
            ),
            Row(
                "roundtrip-identity-column-flag-preserved",
                "Save → Load で DB が採番する列の印が往復する",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                        FlagDocument(
                            new Column
                            {
                                Name = "qty",
                                DataType = "int",
                                IsIdentity = false,
                            },
                            new Column
                            {
                                Name = "total",
                                DataType = "decimal(21,2)",
                                IsIdentity = true,
                            }
                        ),
                    Expect = o =>
                    {
                        var loadedEntity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        loadedEntity.Columns.Select(c => c.IsIdentity).Should().Equal(false, true);
                    },
                }
            ),
            Row(
                "load-legacy-json-without-is-identity-yields-false",
                "旧形式 JSON（IsIdentity なし）は false として読める",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = LegacyColumnFlagJson(),
                    Expect = o =>
                    {
                        var column = o.Loaded!.Schema.Entities.Should()
                            .ContainSingle()
                            .Which.Columns.Should()
                            .ContainSingle()
                            .Which;
                        // 列そのものは残り、落ちるのは「書き込みから外す」指定だけ
                        column.Name.Should().Be("total");
                        column.IsIdentity.Should().BeFalse();
                    },
                }
            ),
            Row(
                "tryload-explicit-null-is-identity-rejected-as-invalid-json",
                "明示的な null の IsIdentity は読込を拒否する",
                new FormatCase
                {
                    Stage = FormatStage.TryLoad,
                    Result = FormatResult.InvalidJson,
                    Json = NullColumnFlagJson("IsIdentity"),
                    Expect = o => o.Error.Should().Be(DocumentLoadError.InvalidJson),
                }
            ),
        ];

    /// <summary>Items テーブルに指定の列を持たせた図</summary>
    private static DiagramDocument FlagDocument(params Column[] columns)
    {
        var entity = new Entity { TableName = "Items" };

        foreach (var column in columns)
        {
            entity.Columns.Add(column);
        }

        return new DiagramDocument { Schema = new ErDiagram { Entities = { entity } } };
    }

    /// <summary>印のプロパティを持たない旧形式の JSON（total 列 1 本）</summary>
    private static string LegacyColumnFlagJson() =>
        """
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "Id": "11111111-1111-1111-1111-111111111111",
                    "TableName": "Items",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "total", "DataType": "decimal(21,2)" }
                    ]
                  }
                ],
                "Relationships": []
              }
            }
            """;

    /// <summary>印のプロパティに明示的な null を書いた JSON（total 列 1 本）</summary>
    private static string NullColumnFlagJson(string property) =>
        $$"""
            {
              "Version": 1,
              "Schema": {
                "TargetDbms": "sqlserver",
                "Entities": [
                  {
                    "Id": "11111111-1111-1111-1111-111111111111",
                    "TableName": "Items",
                    "Columns": [
                      { "Id": "22222222-2222-2222-2222-222222222222", "Name": "total", "DataType": "int", "{{property}}": null }
                    ]
                  }
                ],
                "Relationships": []
              }
            }
            """;

    /// <summary>
    /// 主キーの順序と一意制約の往復と旧形式との互換
    /// </summary>
    /// <remarks>
    /// <see cref="DiagramDocument.Version"/> は据え置き（1 のまま）で、旧形式 JSON は
    /// プロパティ欠落＝空リスト（＝列宣言順）として読める後方互換を保つ。
    /// </remarks>
    private static IEnumerable<ScenarioRow<FormatCase>> KeyOrderAndUniqueRows() =>
        [
            Row(
                "roundtrip-primary-key-order-preserved",
                "Save → Load で主キーの順序が往復する",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                    {
                        var a = new Column
                        {
                            Name = "a",
                            DataType = "int",
                            IsPrimaryKey = true,
                        };
                        var b = new Column
                        {
                            Name = "b",
                            DataType = "int",
                            IsPrimaryKey = true,
                        };
                        var entity = new Entity
                        {
                            TableName = "Pair",
                            Columns = { a, b },
                            // 列宣言順（a → b）とは逆の主キー順
                            PrimaryKeyColumnIds = [b.Id, a.Id],
                        };

                        return new DiagramDocument
                        {
                            Schema = new ErDiagram { Entities = { entity } },
                        };
                    },
                    Expect = o =>
                    {
                        var a = o.Written!.Schema.Entities[0].Columns[0];
                        var b = o.Written.Schema.Entities[0].Columns[1];

                        var loadedEntity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        loadedEntity.PrimaryKeyColumnIds.Should().Equal(b.Id, a.Id);
                        loadedEntity
                            .GetPrimaryKeyColumnsInOrder()
                            .Select(c => c.Name)
                            .Should()
                            .Equal("b", "a");
                    },
                }
            ),
            Row(
                "load-legacy-json-without-primary-key-column-ids-yields-empty-list",
                "旧形式 JSON（PrimaryKeyColumnIds なし）は空リストとして読める",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          {
                            "Id": "11111111-1111-1111-1111-111111111111",
                            "TableName": "Pair",
                            "Columns": [
                              { "Id": "22222222-2222-2222-2222-222222222222", "Name": "a", "DataType": "int", "IsPrimaryKey": true },
                              { "Id": "33333333-3333-3333-3333-333333333333", "Name": "b", "DataType": "int", "IsPrimaryKey": true }
                            ]
                          }
                        ],
                        "Relationships": []
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        var entity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        entity.PrimaryKeyColumnIds.Should().NotBeNull();
                        entity.PrimaryKeyColumnIds.Should().BeEmpty();
                        // 順序指定が無いので列宣言順がそのまま実効順になる
                        entity
                            .GetPrimaryKeyColumnsInOrder()
                            .Select(c => c.Name)
                            .Should()
                            .Equal("a", "b");
                    },
                }
            ),
            Row(
                "load-explicit-null-primary-key-column-ids-normalized",
                "明示的な null の PrimaryKeyColumnIds は空リストへ修復される",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          {
                            "Id": "11111111-1111-1111-1111-111111111111",
                            "TableName": "A",
                            "Columns": [],
                            "PrimaryKeyColumnIds": null
                          }
                        ],
                        "Relationships": []
                      }
                    }
                    """,
                    Expect = o =>
                        o.Loaded!.Schema.Entities.Should()
                            .ContainSingle()
                            .Which.PrimaryKeyColumnIds.Should()
                            .BeEmpty(),
                }
            ),
            Row(
                "roundtrip-unique-constraints-preserved",
                "Save → Load で一意制約（名前・構成列・宣言順）が往復する",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                    {
                        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
                        var region = new Column { Name = "Region", DataType = "nvarchar(10)" };
                        var entity = new Entity { TableName = "Shop", Columns = { code, region } };

                        var named = new UniqueConstraint
                        {
                            Name = "UQ_Shop_Code",
                            ColumnIds = [code.Id],
                        };
                        // 名前なし（SQLite 取込のように自動名しか無いケース）＋複合列
                        var unnamed = new UniqueConstraint
                        {
                            Name = null,
                            ColumnIds = [region.Id, code.Id],
                        };
                        entity.UniqueConstraints.Add(named);
                        entity.UniqueConstraints.Add(unnamed);

                        return new DiagramDocument
                        {
                            Schema = new ErDiagram { Entities = { entity } },
                        };
                    },
                    Expect = o =>
                    {
                        var writtenEntity = o.Written!.Schema.Entities[0];
                        var code = writtenEntity.Columns[0];
                        var region = writtenEntity.Columns[1];
                        var named = writtenEntity.UniqueConstraints[0];

                        var loadedEntity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        loadedEntity.UniqueConstraints.Should().HaveCount(2);

                        var loadedNamed = loadedEntity.UniqueConstraints[0];
                        loadedNamed.Id.Should().Be(named.Id);
                        loadedNamed.Name.Should().Be("UQ_Shop_Code");
                        loadedNamed.ColumnIds.Should().Equal(code.Id);

                        var loadedUnnamed = loadedEntity.UniqueConstraints[1];
                        loadedUnnamed.Name.Should().BeNull();
                        // 宣言順（Region → Code）が維持される
                        loadedUnnamed.ColumnIds.Should().Equal(region.Id, code.Id);
                    },
                }
            ),
            Row(
                "load-legacy-json-without-unique-constraints-yields-empty-list",
                "旧形式 JSON（UniqueConstraints なし）は空リストとして読める",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          {
                            "Id": "11111111-1111-1111-1111-111111111111",
                            "TableName": "Shop",
                            "Columns": [
                              { "Id": "22222222-2222-2222-2222-222222222222", "Name": "Code", "DataType": "int" }
                            ]
                          }
                        ],
                        "Relationships": []
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        var entity = o.Loaded!.Schema.Entities.Should().ContainSingle().Which;
                        entity.UniqueConstraints.Should().NotBeNull();
                        entity.UniqueConstraints.Should().BeEmpty();
                    },
                }
            ),
            Row(
                "load-explicit-nulls-unique-constraints-and-column-ids-normalized",
                "明示的な null の UniqueConstraints / ColumnIds は空リストへ修復される",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "Version": 1,
                      "Schema": {
                        "TargetDbms": "sqlserver",
                        "Entities": [
                          {
                            "Id": "11111111-1111-1111-1111-111111111111",
                            "TableName": "A",
                            "Columns": [],
                            "UniqueConstraints": null
                          },
                          {
                            "Id": "33333333-3333-3333-3333-333333333333",
                            "TableName": "B",
                            "Columns": [],
                            "UniqueConstraints": [
                              { "Id": "44444444-4444-4444-4444-444444444444", "ColumnIds": null }
                            ]
                          }
                        ],
                        "Relationships": []
                      }
                    }
                    """,
                    Expect = o =>
                    {
                        o.Loaded!.Schema.Entities[0].UniqueConstraints.Should().BeEmpty();
                        o.Loaded.Schema.Entities[1]
                            .UniqueConstraints.Should()
                            .ContainSingle()
                            .Which.ColumnIds.Should()
                            .BeEmpty();
                    },
                }
            ),
        ];
}
