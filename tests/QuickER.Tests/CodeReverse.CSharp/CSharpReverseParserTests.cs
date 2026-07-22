using FluentAssertions;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.SqlServer;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// <see cref="CSharpReverseParser"/> の単体解析（対象外クラス無視・0 件エラー・重複一意化・
/// トークン展開不能時のフォールバック・NULL 判定）を検証する。
/// </summary>
public class CSharpReverseParserTests
{
    private static CodeReverseResult Parse(string source) =>
        new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());

    /// <summary>[Table] を持たないインフラクラスは無視され、対象クラスのみ復元される</summary>
    [Fact(DisplayName = "対象外クラス（[Table] なし）は無視される")]
    public void Parse_IgnoresNonTargetClasses()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace Sample;

            public abstract class EntityBase { }

            [Table("customers")]
            public partial class CustomerEntity : EntityBase
            {
                [Key]
                [Column("customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }
            }

            public sealed class CustomerRepository
            {
                [Column("not_a_real_column")]
                public int Ignored { get; set; }
            }
            """;

        var result = Parse(source);

        result.Entities.Should().ContainSingle().Which.TableName.Should().Be("customers");
        result.Entities[0].Columns.Should().ContainSingle().Which.Name.Should().Be("customer_id");
    }

    /// <summary>解析対象クラスが 1 件も無いと、案内メッセージ付きの例外になる</summary>
    [Fact(DisplayName = "対象クラス 0 件は CodeReverseException（案内メッセージ）")]
    public void Parse_NoTargetClasses_Throws()
    {
        const string source = """
            namespace Sample;

            public class PlainPoco
            {
                public int Id { get; set; }
            }
            """;

        var act = () => Parse(source);

        act.Should()
            .Throw<CodeReverseException>()
            .WithMessage(ReverseStrings.Reverse_NoTargetClasses);
    }

    /// <summary>双方向の [NavigationReference] は端点 4 つ組で 1 本のリレーションへ一意化される</summary>
    [Fact(DisplayName = "双方向ナビゲーションは 1 本のリレーションへ一意化される")]
    public void Parse_DeduplicatesBidirectionalNavigations()
    {
        const string source = """
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

                [NavigationReference("customers", "customer_id", "orders", "customer_id", true, true, false)]
                public ICollection<OrderEntity> Orders { get; set; }
            }

            [Table("orders")]
            public partial class OrderEntity
            {
                [Key]
                [Column("order_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int OrderId { get; set; }

                [Column("customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "customer_id", false, false, true)]
                public CustomerEntity Customer { get; set; }
            }
            """;

        var result = Parse(source);

        var relationship = result.Relationships.Should().ContainSingle().Subject;
        // いずれかの端で IsCollection=true のため 1 対多。参照先（customers）が起点、FK 保有（orders）が終点。
        relationship.Type.Should().Be(RelationshipType.OneToMany);

        var entityById = result.Entities.ToDictionary(entity => entity.Id);
        entityById[relationship.SourceEntityId].TableName.Should().Be("customers");
        entityById[relationship.TargetEntityId].TableName.Should().Be("orders");

        // FK 保有側（orders.customer_id）に FK フラグが立つ
        result
            .Entities.Single(entity => entity.TableName == "orders")
            .Columns.Single(column => column.Name == "customer_id")
            .IsForeignKey.Should()
            .BeTrue();
    }

    /// <summary>両端とも単一（非コレクション）のナビゲーションは 1 対 1 になる</summary>
    [Fact(DisplayName = "両端が単一のナビゲーションは 1 対 1 になる")]
    public void Parse_SingleEndedNavigation_IsOneToOne()
    {
        const string source = """
            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Key]
                [Column("customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "customer_profiles", "customer_id", false, true, false)]
                public CustomerProfileEntity CustomerProfile { get; set; }
            }

            [Table("customer_profiles")]
            public partial class CustomerProfileEntity
            {
                [Key]
                [Column("profile_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int ProfileId { get; set; }

                [Column("customer_id")]
                [Required]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "customer_profiles", "customer_id", false, false, true)]
                public CustomerEntity Customer { get; set; }
            }
            """;

        var result = Parse(source);

        result
            .Relationships.Should()
            .ContainSingle()
            .Which.Type.Should()
            .Be(RelationshipType.OneToOne);
    }

    /// <summary>展開できない型トークンは、トークン文字列をそのまま採用し警告を出す</summary>
    [Fact(DisplayName = "展開不能な型トークンは verbatim 採用＋警告")]
    public void Parse_UnresolvableTypeToken_FallsBackWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("things")]
            public partial class ThingEntity
            {
                [Column("weird")]
                [DbColumnMeta("bogustype")]
                public object Weird { get; set; }
            }
            """;

        var result = Parse(source);

        var column = result.Entities.Single().Columns.Single();
        column.DataType.Should().Be("bogustype");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_TypeTokenUnresolved,
                    "bogustype",
                    "things",
                    "weird"
                )
            );
    }

    /// <summary>NULL 許容はプロパティ型の <c>?</c>（型構文）で復元する（[Required]/[Key] には依存しない）</summary>
    [Fact(DisplayName = "プロパティ型構文（? の有無）で NULL 許容を復元する")]
    public void Parse_MapsNullabilityFromPropertyTypeSyntax()
    {
        const string source = """
            namespace Sample;

            [Table("mixed")]
            public partial class MixedEntity
            {
                [Key]
                [Column("id")]
                [Required]
                [DbColumnMeta("int32")]
                public int Id { get; set; }

                [Column("name")]
                [Required]
                [DbColumnMeta("string(50)")]
                public string Name { get; set; }

                [Column("note")]
                [DbColumnMeta("string(50)")]
                public string? Note { get; set; }

                // VO 無し・値型 NOT NULL 非 PK 列（[Required] は付かない）。型が int（? 無し）＝非 NULL。
                [Column("amount")]
                [DbColumnMeta("int32")]
                public int Amount { get; set; }

                // 値型 NULL 許容列。型が int?（NullableTypeSyntax）＝NULL 許容。
                [Column("quantity")]
                [DbColumnMeta("int32")]
                public int? Quantity { get; set; }
            }
            """;

        var columns = Parse(source).Entities.Single().Columns.ToDictionary(column => column.Name);

        columns["id"].IsPrimaryKey.Should().BeTrue();
        columns["id"].IsNullable.Should().BeFalse();
        columns["name"].IsNullable.Should().BeFalse();
        columns["note"].IsNullable.Should().BeTrue();
        // [Required] が付かない値型 NOT NULL 非 PK 列でも、型構文（? 無し）から非 NULL に復元される
        // （旧 [Required] ベース判定ではここが NULL 許容と誤判定される）
        columns["amount"].IsNullable.Should().BeFalse();
        // 値型 NULL 許容列は int? の型構文から NULL 許容に復元される
        columns["quantity"].IsNullable.Should().BeTrue();
    }

    /// <summary>[Column] はあるが [DbColumnMeta] が無い列は、警告のうえスキップされる</summary>
    [Fact(DisplayName = "[DbColumnMeta] なしの列は警告してスキップ")]
    public void Parse_ColumnWithoutTypeMeta_SkippedWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("partial")]
            public partial class PartialEntity
            {
                [Key]
                [Column("id")]
                [Required]
                [DbColumnMeta("int32")]
                public int Id { get; set; }

                [Column("mystery")]
                public object Mystery { get; set; }
            }
            """;

        var result = Parse(source);

        result.Entities.Single().Columns.Select(column => column.Name).Should().Equal("id");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_ColumnMissingTypeMeta, "partial", "mystery"));
    }
}
