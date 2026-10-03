using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.CodeGen.UI;
using QuickER.CodeReverse.CSharp;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 同じ親子の組に名前の無い <c>[NavigationReference]</c> が 2 本あるコードを取り込んでも、DDL の外部キー名が
/// 重ならないこと（<see cref="ConstraintNames.NameUnnamedForeignKeys"/> を解析の最後と GUI マージの最後で通すこと）を検証する。
/// </summary>
/// <remarks>
/// CLI の <c>quicker reverse</c> は解析結果をそのまま図にする（<see cref="CSharpReverseParser"/> だけを通る）。
/// GUI のマージ取込は解析の後に <see cref="ReverseMergePostProcessor.Apply"/> が現在図の名前で補完する。
/// </remarks>
public sealed class CSharpReverseUnnamedForeignKeyTests
{
    /// <summary>orders が customers を 2 本（出荷先・請求先）参照するコード。名前の指定は引数で差し込む</summary>
    private static string Source(string shipArguments = "", string billArguments = "") =>
        $$"""
            using System.Collections.Generic;

            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Key]
                [Column("customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "ship_customer_id", true, true, false{{shipArguments}})]
                public ICollection<OrderEntity> ShipOrders { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "bill_customer_id", true, true, false{{billArguments}})]
                public ICollection<OrderEntity> BillOrders { get; set; }
            }

            [Table("orders")]
            public partial class OrderEntity
            {
                [Key]
                [Column("order_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int OrderId { get; set; }

                [Column("ship_customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int ShipCustomerId { get; set; }

                [Column("bill_customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int BillCustomerId { get; set; }
            }
            """;

    /// <summary>参照が 1 本だけのコード（出荷先のみ）</summary>
    private const string SingleReferenceSource = """
        using System.Collections.Generic;

        namespace Sample;

        [Table("customers")]
        public partial class CustomerEntity
        {
            [Key]
            [Column("customer_id")]
            [Required]
            [DbColumnMeta("int32")]
            public int CustomerId { get; set; }

            [NavigationReference("customers", "customer_id", "orders", "ship_customer_id", true, true, false)]
            public ICollection<OrderEntity> ShipOrders { get; set; }
        }

        [Table("orders")]
        public partial class OrderEntity
        {
            [Key]
            [Column("order_id")]
            [Required]
            [DbColumnMeta("int32")]
            public int OrderId { get; set; }

            [Column("ship_customer_id")]
            [Required]
            [DbColumnMeta("int32")]
            public int ShipCustomerId { get; set; }
        }
        """;

    private static CodeReverseResult Parse(string source) =>
        new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());

    /// <summary>SQL Server の DDL に現れる外部キーの制約名を出現順に返す</summary>
    private static List<string> ForeignKeyNamesInDdl(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var ddl = new SqlServerDdlGenerator().Build(
            new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = entities.ToList(),
                Relationships = relationships.ToList(),
            }
        );

        return Regex
            .Matches(ddl, @"CONSTRAINT \[([^\]]+)\] FOREIGN KEY")
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    /// <summary>子の列名からリレーションを引く</summary>
    private static Relationship ByChildColumn(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        string childColumnName
    )
    {
        var columnId = entities
            .SelectMany(entity => entity.Columns)
            .Single(column => column.Name == childColumnName)
            .Id;

        return relationships.Single(relationship =>
            relationship.ColumnPairs.Any(pair => pair.TargetColumnId == columnId)
        );
    }

    [Fact(DisplayName = "解析だけ（CLI）でも、同じ組の名前の無い 2 本は DDL で別の名前になる")]
    public void Parse_TwoUnnamedReferences_GetDistinctNamesInDdl()
    {
        var result = Parse(Source());

        var names = ForeignKeyNamesInDdl(result.Entities, result.Relationships);

        names.Should().HaveCount(2);
        names.Should().OnlyHaveUniqueItems();
        // 先に来た 1 本は既定名のまま（名前なし）、ふさがった 2 本目にだけ名前が付く
        result
            .Relationships.Count(relationship => relationship.ConstraintName is null)
            .Should()
            .Be(1);
        // 付けた名前は「コードが指定した名前」ではない
        result.RelationshipMetadata.Should().BeEmpty();
    }

    [Fact(DisplayName = "名前の無い参照が 1 本だけなら名前は付けない")]
    public void Parse_SingleUnnamedReference_StaysUnnamed()
    {
        var result = Parse(SingleReferenceSource);

        result.Relationships.Should().ContainSingle().Which.ConstraintName.Should().BeNull();
    }

    [Fact(DisplayName = "コードが指定した名前は変えない")]
    public void Parse_ExplicitNames_AreKept()
    {
        var result = Parse(
            Source(
                shipArguments: ", ConstraintName = \"FK_Ship\"",
                billArguments: ", ConstraintName = \"FK_Bill\""
            )
        );

        ByChildColumn(result.Entities, result.Relationships, "ship_customer_id")
            .ConstraintName.Should()
            .Be("FK_Ship");
        ByChildColumn(result.Entities, result.Relationships, "bill_customer_id")
            .ConstraintName.Should()
            .Be("FK_Bill");
    }

    /// <summary>
    /// GUI のマージ取込を通して、現在図へ取り込んだ後の図を返す（取込元のコードは一時ファイルへ書く）
    /// </summary>
    private static ErDiagram MergeIntoCurrent(ErDiagram current, string source)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseUnnamedFk",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "Model.g.cs");
            File.WriteAllText(sourcePath, source);
            var host = new StubErDiagramHost
            {
                DiagramToReturn = current,
                ProvidersToReturn = new DatabaseProviderRegistry(
                    new IDatabaseProvider[] { new SqlServerProvider() }
                ),
            };
            var files = new StubFileDialogService
            {
                OpenResult = new FileDialogResult(sourcePath, 1),
            };
            new CodeReverseCommandService(host, new StubDialogService(), files).Run();

            return host.LastReplacedDiagram!;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 修正前のリバースで作った図と同じ形（同じ組に名前の無い 2 本）の現在図。
    /// 外部キーの名前を引数で差し込める
    /// </summary>
    private static ErDiagram CurrentDiagram(string? shipName = null, string? billName = null)
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var shipCustomerId = new Column
        {
            Name = "ship_customer_id",
            DataType = "int",
            IsNullable = false,
            IsForeignKey = true,
        };
        var billCustomerId = new Column
        {
            Name = "bill_customer_id",
            DataType = "int",
            IsNullable = false,
            IsForeignKey = true,
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                shipCustomerId,
                billCustomerId,
            },
        };

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, shipCustomerId.Id)],
                    ConstraintName = shipName,
                },
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new RelationshipColumnPair(customerId.Id, billCustomerId.Id)],
                    ConstraintName = billName,
                },
            },
        };
    }

    [Fact(DisplayName = "GUI マージで現在図の名前が null に戻っても、DDL の外部キー名は重ならない")]
    public void Merge_CurrentWithDuplicateUnnamedReferences_GetsDistinctNames()
    {
        var merged = MergeIntoCurrent(CurrentDiagram(), Source());

        var names = ForeignKeyNamesInDdl(merged.Entities, merged.Relationships);

        names.Should().HaveCount(2);
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "GUI マージで現在図から補完した名前は変えない")]
    public void Merge_NamesSuppliedByCurrentDiagram_AreKept()
    {
        var merged = MergeIntoCurrent(
            CurrentDiagram(shipName: "FK_CurrentShip", billName: "FK_CurrentBill"),
            Source()
        );

        ByChildColumn(merged.Entities, merged.Relationships, "ship_customer_id")
            .ConstraintName.Should()
            .Be("FK_CurrentShip");
        ByChildColumn(merged.Entities, merged.Relationships, "bill_customer_id")
            .ConstraintName.Should()
            .Be("FK_CurrentBill");
    }
}
