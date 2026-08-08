using AwesomeAssertions;
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

    /// <summary>
    /// 構文エラーのあるソース（途中で切れた .g.cs）は、列が黙って欠落した図を作らないよう
    /// <see cref="CodeReverseException"/> で中断し、位置（行番号）と診断 ID を案内する
    /// </summary>
    [Fact(DisplayName = "構文エラーのあるソースは CodeReverseException（行・診断 ID 付き）")]
    public void Parse_SyntaxError_ThrowsWithLocation()
    {
        // 20 行目の途中で切れたソース（コピペ欠け・コンフリクトマーカー残りの再現）。
        // Roslyn はエラー回復して部分木を返すため、検査しないと name 列が黙って欠落する。
        const string source = """
            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Key]
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId
                {
                    get;
                    set;
                }

                [Column("name")]
                [DbColumnMeta("string(50)")]
                public string Name
                {
                    get;
                    set
            """;

        var act = () => Parse(source);

        var message = act.Should().Throw<CodeReverseException>().Which.Message;
        // 文言はロケール依存のため、調査に必要な情報（診断 ID・発生行）が載ることで検証する
        message.Should().Contain("CS", "Roslyn の診断 ID がメッセージに載る");
        message.Should().Contain("20", "構文エラーの発生行（20 行目）がメッセージに載る");
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

    /// <summary>
    /// クラスレベルの <c>[UniqueConstraint]</c> は、構成プロパティ名 → 列 Id の逆写像で復元される
    /// （単一列・複合列、名前あり・名前なしの 4 通り）
    /// </summary>
    [Fact(DisplayName = "[UniqueConstraint] から UNIQUE 制約を復元する（単一・複合・名前有無）")]
    public void Parse_RestoresUniqueConstraints()
    {
        const string source = """
            namespace Sample;

            [Table("users")]
            [UniqueConstraint("Email", Name = "UQ_users_email")]
            [UniqueConstraint("TenantId", "LoginName")]
            public partial class UserEntity
            {
                [Key]
                [Column("user_id")]
                [DbColumnMeta("int32")]
                public int UserId { get; set; }

                [Column("tenant_id")]
                [DbColumnMeta("int32")]
                public int TenantId { get; set; }

                [Column("email")]
                [Required]
                [DbColumnMeta("string(200)")]
                public string Email { get; set; }

                [Column("login_name")]
                [Required]
                [DbColumnMeta("string(50)")]
                public string LoginName { get; set; }
            }
            """;

        var entity = Parse(source).Entities.Single();
        var columnNameById = entity.Columns.ToDictionary(
            column => column.Id,
            column => column.Name
        );

        entity.UniqueConstraints.Should().HaveCount(2);

        // 1 件目: 実名付きの単一列制約
        entity.UniqueConstraints[0].Name.Should().Be("UQ_users_email");
        entity
            .UniqueConstraints[0]
            .ColumnIds.Select(id => columnNameById[id])
            .Should()
            .Equal("email");

        // 2 件目: 名前なし（＝生成時に合成）の複合制約。構成列は宣言順を保つ
        entity.UniqueConstraints[1].Name.Should().BeNull();
        entity
            .UniqueConstraints[1]
            .ColumnIds.Select(id => columnNameById[id])
            .Should()
            .Equal("tenant_id", "login_name");
    }

    /// <summary>UNIQUE 制約属性が無いコードは「制約なし」として復元される（コードが正本＝温存しない）</summary>
    [Fact(DisplayName = "[UniqueConstraint] が無いコードは制約なしで復元する")]
    public void Parse_WithoutUniqueConstraintAttributes_RestoresNoConstraints()
    {
        const string source = """
            namespace Sample;

            [Table("users")]
            public partial class UserEntity
            {
                [Key]
                [Column("user_id")]
                [DbColumnMeta("int32")]
                public int UserId { get; set; }
            }
            """;

        var result = Parse(source);

        result.Entities.Single().UniqueConstraints.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>構成プロパティを解決できない UNIQUE 制約は、縮めずに制約ごとスキップし警告する</summary>
    [Fact(DisplayName = "解決できないプロパティを含む UNIQUE 制約は制約ごとスキップ＋警告")]
    public void Parse_UniqueConstraintWithUnresolvableMember_SkippedWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("users")]
            [UniqueConstraint("Email", "Missing", Name = "UQ_users_email_missing")]
            [UniqueConstraint("Email", Name = "UQ_users_email")]
            public partial class UserEntity
            {
                [Key]
                [Column("user_id")]
                [DbColumnMeta("int32")]
                public int UserId { get; set; }

                [Column("email")]
                [Required]
                [DbColumnMeta("string(200)")]
                public string Email { get; set; }
            }
            """;

        var result = Parse(source);

        // 解決できた 2 件目だけが残る（1 件目は email だけへ縮めずに丸ごと捨てる）
        result
            .Entities.Single()
            .UniqueConstraints.Select(constraint => constraint.Name)
            .Should()
            .Equal("UQ_users_email");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_UniqueConstraintMemberUnresolved,
                    "users",
                    "Missing"
                )
            );
    }

    /// <summary>構成プロパティを 1 つも持たない UNIQUE 制約はスキップし警告する</summary>
    [Fact(DisplayName = "構成 0 件の UNIQUE 制約はスキップ＋警告")]
    public void Parse_UniqueConstraintWithoutMembers_SkippedWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("users")]
            [UniqueConstraint]
            public partial class UserEntity
            {
                [Key]
                [Column("user_id")]
                [DbColumnMeta("int32")]
                public int UserId { get; set; }
            }
            """;

        var result = Parse(source);

        result.Entities.Single().UniqueConstraints.Should().BeEmpty();
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_UniqueConstraintEmpty, "users"));
    }

    /// <summary>
    /// <c>[NavigationReference]</c> の名前付き引数から、外部キー制約名・参照アクションを復元する
    /// （両側に同値が刻まれるため 1 本のリレーションへ畳まれる）
    /// </summary>
    [Fact(
        DisplayName = "[NavigationReference] の名前付き引数から FK 制約名・参照アクションを復元する"
    )]
    public void Parse_RestoresForeignKeyMetadata()
    {
        var result = Parse(
            BuildRelationshipSource(
                ", ConstraintName = \"FK_orders_customers\", OnDelete = \"Cascade\", OnUpdate = \"SetNull\""
            )
        );

        var relationship = result.Relationships.Should().ContainSingle().Subject;
        relationship.ConstraintName.Should().Be("FK_orders_customers");
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);

        // 「コードが指定していた」ことも索引で伝える（GUI マージの温存判断に使う）
        var metadata = result.RelationshipMetadata[relationship.Id];
        metadata.ConstraintName.Should().Be("FK_orders_customers");
        metadata.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        metadata.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 名前付き引数を持たない旧形式の <c>[NavigationReference]</c>（旧バージョンで生成したコード）は
    /// 既定値で復元し、「未指定」としてメタデータ索引に載せない（GUI マージが現在図から補完できる）
    /// </summary>
    [Fact(DisplayName = "旧形式（名前付き引数なし）は既定値＋未指定として復元する")]
    public void Parse_LegacyNavigationWithoutMetadata_LeavesFieldsUnspecified()
    {
        var result = Parse(BuildRelationshipSource(string.Empty));

        var relationship = result.Relationships.Should().ContainSingle().Subject;
        relationship.ConstraintName.Should().BeNull();
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
        result.RelationshipMetadata.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>解釈できない参照アクションのトークンは、警告のうえ未指定（既定値 NoAction）として扱う</summary>
    [Fact(DisplayName = "未知の参照アクショントークンは警告＋未指定扱い")]
    public void Parse_UnknownReferentialActionToken_WarnsAndLeavesUnspecified()
    {
        var result = Parse(
            BuildRelationshipSource(
                ", ConstraintName = \"FK_orders_customers\", OnDelete = \"RESTRICT\""
            )
        );

        var relationship = result.Relationships.Should().ContainSingle().Subject;
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);
        // 制約名は指定として残り、解釈できなかった参照アクションだけ未指定になる
        result
            .RelationshipMetadata[relationship.Id]
            .ConstraintName.Should()
            .Be("FK_orders_customers");
        result.RelationshipMetadata[relationship.Id].OnDelete.Should().BeNull();
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_ReferentialActionUnknown,
                    "RESTRICT",
                    "customers",
                    "orders"
                )
            );
    }

    /// <summary>customers 1 対多 orders のソースを組み立てる（両側のナビゲーションへ同じ追加引数を刻む）</summary>
    private static string BuildRelationshipSource(string extraArguments) =>
        $$"""
            using System.Collections.Generic;

            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Key]
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "customer_id", true, true, false{{extraArguments}})]
                public ICollection<OrderEntity> Orders { get; set; }
            }

            [Table("orders")]
            public partial class OrderEntity
            {
                [Key]
                [Column("order_id")]
                [DbColumnMeta("int32")]
                public int OrderId { get; set; }

                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [NavigationReference("customers", "customer_id", "orders", "customer_id", false, false, true{{extraArguments}})]
                public CustomerEntity Customer { get; set; }
            }
            """;

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
