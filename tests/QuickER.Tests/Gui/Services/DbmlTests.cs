using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>DbmlExporter / DbmlImporter による DBML 形式の入出力をテストするクラス</summary>
public class DbmlTests
{
    /// <summary>PK・FK 列とリレーションを持つ図から Table ブロックと制約名付き Ref 行が生成されることを検証する</summary>
    [Fact(DisplayName = "DBML 出力で Table と Ref を生成できる")]
    public void Export_BuildsDbmlText()
    {
        var vm = new MainViewModel();
        var customer = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "CustomerName",
                        DataType = "nvarchar(100)",
                        IsNullable = false,
                    },
                },
            }
        );
        var order = new EntityViewModel(
            new Entity
            {
                TableName = "Orders",
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
                        Name = "CustomerId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(customer);
        vm.Entities.Add(order);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(customer.Columns[0].Id, order.Columns[1].Id),
                    ],
                    ConstraintName = "FK_Orders_Customer",
                },
                customer,
                order
            )
        );

        var dbml = DbmlExporter.Build(vm.ToDiagramModel());

        dbml.Should().Contain("Table Customer {");
        dbml.Should().Contain("CustomerId int [pk, not null]");
        dbml.Should().Contain("CustomerId int [ref, not null]");
        dbml.Should()
            .Contain("Ref: [note: 'FK_Orders_Customer'] Customer.CustomerId < Orders.CustomerId");
    }

    /// <summary>DBML テキストのパースで pk/ref 属性と Ref 行の制約名・1対多種別が復元されることを検証する</summary>
    [Fact(DisplayName = "DBML 読込でエンティティとリレーションを復元できる")]
    public void Import_ParsesEntitiesAndRelationships()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk, not null]",
                "  CustomerName nvarchar(100) [not null]",
                "}",
                string.Empty,
                "Table Orders {",
                "  OrderId int [pk, not null]",
                "  CustomerId int [ref, not null]",
                "}",
                string.Empty,
                "Ref: [note: 'FK_Orders_Customer'] Customer.CustomerId < Orders.CustomerId",
            ]
        );

        var diagram = DbmlImporter.Parse(text);

        diagram.Entities.Should().HaveCount(2);
        diagram.Relationships.Should().ContainSingle();
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Customer"
                && entity.Columns.Any(column => column.Name == "CustomerId" && column.IsPrimaryKey)
            );
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Orders"
                && entity.Columns.Any(column => column.Name == "CustomerId" && column.IsForeignKey)
            );
        diagram.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        diagram.Relationships[0].ConstraintName.Should().Be("FK_Orders_Customer");
    }

    /// <summary>SaveTo で書き出した DBML ファイルを Load で読み戻し、エンティティとリレーションが往復で保持されることを検証する</summary>
    [Fact(DisplayName = "DBML ファイルの SaveTo と Load を往復できる")]
    public void SaveAndLoad_RoundTrip()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].TableName = "Parent";
        vm.Entities[0].Columns[0].Name = "ParentId";
        vm.Entities[1].TableName = "Child";
        vm.Entities[1].Columns[0].Name = "ChildId";
        vm.Entities[1]
            .Columns.Add(
                new ColumnViewModel(
                    new Column
                    {
                        Name = "ParentId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    }
                )
            );
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.Relationships[0]
            .SetColumnPairs([
                new RelationshipColumnPair(
                    vm.Entities[0].Columns[0].Id,
                    vm.Entities[1].Columns[1].Id
                ),
            ]);
        vm.Relationships[0].ConstraintName = "FK_Child_Parent";

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.dbml");

        try
        {
            DbmlExporter.SaveTo(vm.ToDiagramModel(), path);
            var diagram = DbmlImporter.Load(path);

            diagram.Entities.Should().HaveCount(2);
            diagram.Relationships.Should().ContainSingle();
            diagram.Entities.Should().Contain(entity => entity.TableName == "Parent");
            diagram.Entities.Should().Contain(entity => entity.TableName == "Child");
            diagram.Relationships[0].ConstraintName.Should().Be("FK_Child_Parent");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>複合外部キーが DBML 標準の複合 Ref 構文で書き出され、そのまま往復することを検証する</summary>
    [Fact(DisplayName = "DBML は複合外部キーを複合 Ref 構文で往復できる")]
    public void CompositeForeignKey_RoundTripsThroughCompositeRefSyntax()
    {
        var diagram = BuildCompositeDiagram();

        var dbml = DbmlExporter.Build(diagram);

        dbml.Should()
            .Contain(
                "Ref: [note: 'FK_TenantUser_TenantRegion'] TenantRegion.(TenantId, RegionCode) < TenantUser.(TenantRef, RegionRef)"
            );

        var restored = DbmlImporter.Parse(dbml);
        var parent = restored.Entities.Single(entity => entity.TableName == "TenantRegion");
        var child = restored.Entities.Single(entity => entity.TableName == "TenantUser");
        var relationship = restored.Relationships.Single();

        relationship.ColumnPairs.Should().HaveCount(2);
        relationship
            .ColumnPairs.Select(pair =>
                (
                    parent.Columns.Single(column => column.Id == pair.SourceColumnId).Name,
                    child.Columns.Single(column => column.Id == pair.TargetColumnId).Name
                )
            )
            .Should()
            .Equal(("TenantId", "TenantRef"), ("RegionCode", "RegionRef"));

        // 構成列はすべて FK 化される
        child.Columns.Single(column => column.Name == "TenantRef").IsForeignKey.Should().BeTrue();
        child.Columns.Single(column => column.Name == "RegionRef").IsForeignKey.Should().BeTrue();
    }

    /// <summary>単一列の Ref 行は行に書かれた列名がそのまま端点になる（推論しない）ことを検証する</summary>
    [Fact(DisplayName = "DBML 取込は Ref 行の列名をそのまま端点に使う")]
    public void Import_UsesRefLineColumnsAsWritten()
    {
        // 命名規則（ParentId）とは違う列を指す Ref 行。旧実装は推論で ParentId を選んでいた
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Parent {",
                "  ParentId int [pk, not null]",
                "}",
                string.Empty,
                "Table Child {",
                "  ChildId int [pk, not null]",
                "  ParentId int [null]",
                "  OwnerId int [null]",
                "}",
                string.Empty,
                "Ref: Parent.ParentId < Child.OwnerId",
            ]
        );

        var diagram = DbmlImporter.Parse(text);
        var child = diagram.Entities.Single(entity => entity.TableName == "Child");
        var pair = diagram.Relationships.Single().ColumnPairs.Should().ContainSingle().Subject;

        pair.TargetColumnId.Should()
            .Be(child.Columns.Single(column => column.Name == "OwnerId").Id);
        child.Columns.Single(column => column.Name == "ParentId").IsForeignKey.Should().BeFalse();
    }

    /// <summary>単一列・複合が混在する図でも、それぞれの表記で往復することを検証する</summary>
    [Fact(DisplayName = "DBML は単一列と複合外部キーの混在を往復できる")]
    public void MixedForeignKeys_RoundTrip()
    {
        var diagram = BuildCompositeDiagram();

        // 単一列の外部キーを 1 本足す（TenantUser → TenantAudit）
        var child = diagram.Entities.Single(entity => entity.TableName == "TenantUser");
        var audit = new Entity
        {
            TableName = "TenantAudit",
            Columns =
            {
                new Column
                {
                    Name = "TenantAuditId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantUserId",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        diagram.Entities.Add(audit);
        diagram.Relationships.Add(
            new Relationship
            {
                SourceEntityId = child.Id,
                TargetEntityId = audit.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs =
                [
                    new RelationshipColumnPair(
                        child.Columns.Single(column => column.Name == "TenantUserId").Id,
                        audit.Columns.Single(column => column.Name == "TenantUserId").Id
                    ),
                ],
                ConstraintName = "FK_TenantAudit_TenantUser",
            }
        );

        var dbml = DbmlExporter.Build(diagram);

        // 単一列は従来どおりの表記のまま
        dbml.Should()
            .Contain(
                "Ref: [note: 'FK_TenantAudit_TenantUser'] TenantUser.TenantUserId < TenantAudit.TenantUserId"
            );

        var restored = DbmlImporter.Parse(dbml);

        restored
            .Relationships.Single(relationship =>
                relationship.ConstraintName == "FK_TenantUser_TenantRegion"
            )
            .ColumnPairs.Should()
            .HaveCount(2);
        restored
            .Relationships.Single(relationship =>
                relationship.ConstraintName == "FK_TenantAudit_TenantUser"
            )
            .ColumnPairs.Should()
            .ContainSingle();
    }

    /// <summary>複合 PK (TenantId, RegionCode) の親と、複合外部キーを持つ子からなる図を作る</summary>
    private static ErDiagram BuildCompositeDiagram()
    {
        var parent = new Entity
        {
            TableName = "TenantRegion",
            Columns =
            {
                new Column
                {
                    Name = "TenantId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "RegionCode",
                    DataType = "nvarchar(10)",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "TenantUser",
            Columns =
            {
                new Column
                {
                    Name = "TenantUserId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantRef",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "RegionRef",
                    DataType = "nvarchar(10)",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id),
                        new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[2].Id),
                    ],
                    ConstraintName = "FK_TenantUser_TenantRegion",
                },
            ],
        };
    }

    /// <summary>行に紐づく解析エラーへ、その行の行番号が前置されることを検証する</summary>
    [Fact(DisplayName = "DBML 取込の解析エラーに行番号が付く")]
    public void Import_ParseError_PrefixesLineNumber()
    {
        // 3 行目のカラム定義が名前と型の 2 トークンに満たず解析できない
        var text = string.Join(
            Environment.NewLine,
            ["Table Customer {", "  CustomerId int [pk]", "  broken", "}"]
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    3,
                    string.Format(Strings.Dbml_ColumnParseError, "Customer", "broken")
                )
            );
    }

    /// <summary>行ループを抜けてから解決する Indexes ブロックの診断が、索引定義行を指すことを検証する</summary>
    [Fact(DisplayName = "DBML 取込の索引の未定義列は索引定義行を指す")]
    public void Import_IndexColumnNotFound_PointsToIndexLine()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk]",
                "",
                "  Indexes {",
                "    (Missing) [unique]",
                "  }",
                "}",
            ]
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    5,
                    string.Format(Strings.Dbml_IndexColumnNotFound, "Customer", "Missing")
                )
            );
    }

    /// <summary>未閉じブロックの診断が、エラーの判明位置ではなくブロック開始行を指すことを検証する</summary>
    [Fact(DisplayName = "DBML 取込の未閉じブロックはブロック開始行を指す")]
    public void Import_MissingClosingBrace_PointsToBlockStartLine()
    {
        var text = string.Join(
            Environment.NewLine,
            ["// comment", "Table Customer {", "  CustomerId int [pk]"]
        );

        var act = () => DbmlImporter.Parse(text);

        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    Strings.Import_LineDiagnostic,
                    2,
                    string.Format(Strings.Dbml_MissingClosingBrace, "Customer")
                )
            );
    }

    /// <summary>ファイル全体に紐づく診断には（指すべき行が無いため）行番号を付けないことを検証する</summary>
    [Fact(DisplayName = "DBML 取込のファイル全体の診断には行番号を付けない")]
    public void Import_WholeFileDiagnostic_HasNoLineNumber()
    {
        var act = () => DbmlImporter.Parse("// only a comment");

        act.Should().Throw<InvalidDataException>().WithMessage(Strings.Dbml_NoEntities);
    }

    /// <summary>テーブルの説明が DBML 標準の <c>Note:</c> 行として往復することを検証する</summary>
    [Fact(DisplayName = "DBML 往復: テーブルの説明を Note 行で保持する")]
    public void Export_TableDescription_RoundTrips()
    {
        var diagram = BuildOneToManyDiagram(
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        diagram.Entities[0].Description = "顧客マスタ";

        var text = DbmlExporter.Build(diagram);

        text.Should().Contain("Note: '顧客マスタ'");
        DbmlImporter
            .Parse(text)
            .Entities.Single(entity => entity.TableName == "Customer")
            .Description.Should()
            .Be("顧客マスタ");
    }

    /// <summary>他ツールが書いた <c>Note:</c> 行をカラム定義として誤って取り込まないことを検証する</summary>
    [Fact(DisplayName = "DBML 取込: Note 行はカラムとして取り込まない")]
    public void Import_TableNote_IsNotTreatedAsColumn()
    {
        var text = string.Join(
            Environment.NewLine,
            [
                "Table Customer {",
                "  CustomerId int [pk, not null]",
                "  Note: 'Stores customers'",
                "}",
            ]
        );

        var entity = DbmlImporter.Parse(text).Entities.Single();

        entity.Columns.Should().ContainSingle().Which.Name.Should().Be("CustomerId");
        entity.Description.Should().Be("Stores customers");
    }

    /// <summary>参照アクションが Ref 行の設定ブロックとして往復することを検証する</summary>
    [Fact(DisplayName = "DBML 往復: 参照アクションを設定ブロックで保持する")]
    public void Export_ReferentialActions_RoundTrip()
    {
        var diagram = BuildOneToManyDiagram(
            ForeignKeyReferentialAction.Cascade,
            ForeignKeyReferentialAction.SetNull
        );

        var text = DbmlExporter.Build(diagram);

        text.Should().Contain("delete: cascade");
        text.Should().Contain("update: set null");

        var restored = DbmlImporter.Parse(text).Relationships.Single();
        restored.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        restored.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        restored.ConstraintName.Should().Be("FK_Orders_Customer");
    }

    /// <summary>既定の参照アクション（NO ACTION）は設定ブロックへ書き出さないことを検証する</summary>
    [Fact(DisplayName = "DBML 出力: 既定の参照アクションは書かない")]
    public void Export_DefaultReferentialActions_AreOmitted()
    {
        var diagram = BuildOneToManyDiagram(
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var text = DbmlExporter.Build(diagram);

        text.Should().NotContain("delete:");
        text.Should().NotContain("update:");
        text.Should().Contain("[note: 'FK_Orders_Customer']");
    }

    /// <summary>DBML で表現できないのはテーブルのメモと名前付きクエリだけであることを検証する</summary>
    [Fact(DisplayName = "DBML の欠落はメモと名前付きクエリだけ")]
    public void DetectOmissions_ReturnsMemoAndNamedQueryOnly()
    {
        var diagram = BuildOneToManyDiagram(
            ForeignKeyReferentialAction.Cascade,
            ForeignKeyReferentialAction.SetNull
        );
        var customer = diagram.Entities[0];
        customer.Description = "顧客マスタ";
        customer.Memo = "打ち合わせメモ";
        customer.Columns[0].Description = "顧客 ID";
        customer.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Customer", ColumnIds = [customer.Columns[0].Id] }
        );
        diagram.Queries.Add(new QueryDefinition { Name = "ById", EntityId = customer.Id });

        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .Equal(ExportOmissionKind.TableMemo, ExportOmissionKind.NamedQuery);
    }

    /// <summary>説明・メモ・クエリを持たない図では欠落を挙げないことを検証する</summary>
    [Fact(DisplayName = "DBML の欠落は中身が無ければ挙げない")]
    public void DetectOmissions_IgnoresEmptyContent()
    {
        var diagram = BuildOneToManyDiagram(
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        DbmlExporter.DetectOmissions(diagram).Should().BeEmpty();
    }

    /// <summary>親 Customer(CustomerId PK) → 子 Orders(OrderId PK / CustomerId FK) の 1 対多図を作る</summary>
    private static ErDiagram BuildOneToManyDiagram(
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    )
    {
        var parent = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = "Orders",
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
                    Name = "CustomerId",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id),
                    ],
                    ConstraintName = "FK_Orders_Customer",
                    OnDelete = onDelete,
                    OnUpdate = onUpdate,
                },
            ],
        };
    }
}
