using QuickER.CodeGen.CSharp;
using QuickER.CodeReverse.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>C# リバースの取込の規則の行（手書きの C# のソース・生成物を書き換えたソースを取り込む）</summary>
/// <remarks>
/// 警告が無いことは取込の結果の検査（<see cref="NoWarnings"/>）で見る（警告の期待に <c>Empty()</c> を積むと、規則の種類が警告になる）。
/// 生成物から作る入力は表を組むときに 1 回だけ生成する（規則の行の文字列の入力は文字列を受けるため）。
/// </remarks>
internal static partial class FormatRuleRows
{
    /// <summary>
    /// 型カタログで解析できない型は C# の生成が [DbColumnMeta] を省き、リバースは警告つきで列をスキップするので、
    /// 取り込んだ図を出力して取り込み直すと列が消える（受け入れた仕様。FormatRoundTripKnownFailures の UnparsableTypeReason と同じ根拠）
    /// </summary>
    private const string UnparsableTypeFixedPoint =
        "C# リバースの仕様（2026-11-05 ユーザーの判断）：型カタログで解析できない型は生成が [DbColumnMeta] を省き"
        + "（src/QuickER.CodeGen.CSharp/CSharpGenerationModels.cs）、リバースは [DbColumnMeta] の無い列を警告つきでスキップする"
        + "（docs/import-export.ja.md「C# コード（.cs）」）";

    /// <summary>
    /// 同名のテーブル・列を持つ図は C# の生成が生成時エラーで止めるので、取り込んだ図を出力できない（受け入れた仕様。
    /// FormatRoundTripKnownFailures の GenerationCollisionReason と同じ根拠）
    /// </summary>
    private const string DuplicateNameFixedPoint =
        "C# の生成の仕様：テーブル名・列名が重複する図（大文字小文字と前後の空白を区別しない）は生成時エラーで止める"
        + "（src/QuickER.CodeGen.CSharp/CLAUDE.md「型名のぶつかりを生成時に止める」「エンティティのメンバー名の大文字小文字」）";

    private static IEnumerable<ScenarioRow<FormatRule>> CSharpReverse() =>
        [
            .. CSharpParserRules(),
            .. CSharpWarningRules(),
            .. CSharpPrimaryKeyOrderRules(),
            .. CSharpVerbatimRules(),
            .. CSharpUnnamedForeignKeyRules(),
            .. CSharpTypeTokenRules(),
        ];

    /// <summary>CSharpReverseParserTests</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpParserRules() =>
        [
            CSharpRule(
                    "csharp-rule-ignores-non-target-classes",
                    "対象外クラス（[Table] なし）は無視される"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Single("Structure table customers")
                        .Count("Structure column *", 1)
                        .Single("Structure column customers.customer_id")
                ),
            CSharpRule(
                    "csharp-rule-no-target-classes-throws",
                    "対象クラス 0 件は CodeReverseException（案内メッセージ）"
                )
                .Text(
                    """
                    namespace Sample;

                    public class PlainPoco
                    {
                        public int Id { get; set; }
                    }
                    """
                )
                .Rejects(ReverseFailure(ReverseStrings.Reverse_NoTargetClasses)),
            // 途中で切れた .g.cs は、列が黙って欠落した図を作らないよう中断し、位置（行番号）と診断 ID を案内する
            // （20 行目の途中で切れたソース。Roslyn はエラー回復して部分木を返すため、検査しないと name 列が黙って欠落する）
            CSharpRule(
                    "csharp-rule-syntax-error-throws-with-location",
                    "構文エラーのあるソースは CodeReverseException（行・診断 ID 付き）"
                )
                .Text(
                    """
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
                    """
                )
                // 文言はロケール依存のため、調査に必要な情報（発生行 20・診断 ID の CS）が載ることで見る（両言語とも行が診断 ID より前）
                .Rejects(ReverseFailure("*20*CS*")),
            // いずれかの端で IsCollection=true のため 1 対多。参照先（customers）が起点、FK 保有（orders）が終点
            CSharpRule(
                    "csharp-rule-deduplicates-bidirectional-navigations",
                    "双方向ナビゲーションは 1 本のリレーションへ一意化される"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType customers->orders#0 = OneToMany")
                        .Single("ForeignKeyMarker orders.customer_id")
                ),
            CSharpRule(
                    "csharp-rule-single-ended-navigation-is-one-to-one",
                    "両端が単一のナビゲーションは 1 対 1 になる"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType * = OneToOne")
                ),
            CSharpRule(
                    "csharp-rule-unresolvable-type-token-falls-back-with-warning",
                    "展開不能な型トークンは verbatim 採用＋警告"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("things")]
                    public partial class ThingEntity
                    {
                        [Column("weird")]
                        [DbColumnMeta("bogustype")]
                        public object Weird { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("Structure table *", 1)
                        .Count("Structure column *", 1)
                        .Single("Type things.weird = bogustype");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_TypeTokenUnresolved,
                            "bogustype",
                            "things",
                            "weird"
                        )
                    );
                })
                .NoFixedPoint(UnparsableTypeFixedPoint),
            // NULL 許容はプロパティ型の ?（型構文）で復元する（[Required]/[Key] には依存しない）
            CSharpRule(
                    "csharp-rule-maps-nullability-from-property-type-syntax",
                    "プロパティ型構文（? の有無）で NULL 許容を復元する"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    // [Required] が付かない値型 NOT NULL 非 PK 列でも、型構文（? 無し）から非 NULL に復元される
                    e.Imported.Single("PrimaryKey mixed.id")
                        .Single("Nullability mixed.id = notnull")
                        .Single("Nullability mixed.name = notnull")
                        .Single("Nullability mixed.note = null")
                        .Single("Nullability mixed.amount = notnull")
                        .Single("Nullability mixed.quantity = null")
                ),
            // 主キーの列は NULL を持てない（DDL・C# 生成・ほかの取込と同じ ColumnNullability.AllowsNull）。
            // 生成コードは主キーに ? を付けないので、? の付いた主キーは手書きのコードだけが持つ
            CSharpRule(
                    "csharp-rule-nullable-key-property-is-not-null",
                    "? の付いた主キーのプロパティ（値型・参照型）は NOT NULL で読み、主キーでない列は ? のとおり読む"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("tenant_codes")]
                    public partial class TenantCodeEntity
                    {
                        [Key]
                        [Column("tenant_id")]
                        [DbColumnMeta("int32")]
                        public int? TenantId { get; set; }

                        [Key]
                        [Column("code")]
                        [DbColumnMeta("string(20)")]
                        public string? Code { get; set; }

                        [Column("quantity")]
                        [DbColumnMeta("int32")]
                        public int? Quantity { get; set; }
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Single("PrimaryKey tenant_codes.tenant_id")
                        .Single("PrimaryKey tenant_codes.code")
                        .Single("Nullability tenant_codes.tenant_id = notnull")
                        .Single("Nullability tenant_codes.code = notnull")
                        .Single("Nullability tenant_codes.quantity = null")
                ),
            // クラスレベルの [UniqueConstraint] は、構成プロパティ名 → 列 Id の逆写像で復元される
            CSharpRule(
                    "csharp-rule-restores-unique-constraints",
                    "[UniqueConstraint] から UNIQUE 制約を復元する（単一・複合・名前有無）"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    // 1 件目は実名付きの単一列制約、2 件目は名前なし（＝生成時に合成）の複合制約で構成列は宣言順を保つ
                    e.Imported.Count("UniqueSet *", 2)
                        .That(
                            "一意制約が (email)＝UQ_users_email・(tenant_id, login_name)＝名前なしの順",
                            lines =>
                                FormatShapeRows.InOrder(
                                    "UniqueOrder ",
                                    "UniqueOrder users(email) = email",
                                    "UniqueOrder users(login_name,tenant_id) = tenant_id,login_name"
                                )(lines)
                                && FormatShapeRows.InOrder(
                                    "UniqueName ",
                                    "UniqueName users(email) = UQ_users_email",
                                    "UniqueName users(login_name,tenant_id) = -"
                                )(lines)
                        )
                ),
            // UNIQUE 制約属性が無いコードは「制約なし」として復元される（コードが正本＝温存しない）
            CSharpRule(
                    "csharp-rule-without-unique-constraint-attributes-restores-none",
                    "[UniqueConstraint] が無いコードは制約なしで復元する"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("users")]
                    public partial class UserEntity
                    {
                        [Key]
                        [Column("user_id")]
                        [DbColumnMeta("int32")]
                        public int UserId { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.None("UniqueSet *");
                    e.ImportedResult.Add(NoWarnings());
                }),
            // 解決できた 2 件目だけが残る（1 件目は email だけへ縮めずに丸ごと捨てる）
            CSharpRule(
                    "csharp-rule-unique-constraint-with-unresolvable-member-skipped",
                    "解決できないプロパティを含む UNIQUE 制約は制約ごとスキップ＋警告"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("UniqueSet *", 1)
                        .Single("UniqueName users(email) = UQ_users_email");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_UniqueConstraintMemberUnresolved,
                            "users",
                            "Missing"
                        )
                    );
                }),
            CSharpRule(
                    "csharp-rule-unique-constraint-without-members-skipped",
                    "構成 0 件の UNIQUE 制約はスキップ＋警告"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                {
                    e.Imported.None("UniqueSet *");
                    e.Warnings.Exact(
                        string.Format(ReverseStrings.Reverse_UniqueConstraintEmpty, "users")
                    );
                }),
            // 両側に同値が刻まれるため 1 本のリレーションへ畳まれ、「コードが指定していた」ことも索引で伝える（GUI マージの温存判断に使う）
            CSharpRule(
                    "csharp-rule-restores-foreign-key-metadata",
                    "[NavigationReference] の名前付き引数から FK 制約名・参照アクションを復元する"
                )
                .Text(
                    CustomersOrdersSource(
                        ", ConstraintName = \"FK_orders_customers\", OnDelete = \"Cascade\", OnUpdate = \"SetNull\""
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ConstraintName customers->orders#0 = FK_orders_customers")
                        .Single("ReferentialAction customers->orders#0 = del=Cascade upd=SetNull");
                    e.ImportedResult.Add(
                        (
                            "メタデータの索引がリレーションの制約名・参照アクションをコードの指定として持つ",
                            imported =>
                                Reversed(imported).RelationshipMetadata.Values.SingleOrDefault()
                                    is { } metadata
                                && metadata.ConstraintName == "FK_orders_customers"
                                && metadata.OnDelete == ForeignKeyReferentialAction.Cascade
                                && metadata.OnUpdate == ForeignKeyReferentialAction.SetNull
                                && Reversed(imported)
                                    .RelationshipMetadata.ContainsKey(
                                        imported.Diagram.Relationships.Single().Id
                                    )
                        )
                    );
                    e.ImportedResult.Add(NoWarnings());
                }),
            // 旧バージョンで生成したコードは既定値で復元し、「未指定」としてメタデータ索引に載せない（GUI マージが現在図から補完できる）
            CSharpRule(
                    "csharp-rule-legacy-navigation-without-metadata-leaves-fields-unspecified",
                    "旧形式（名前付き引数なし）は既定値＋未指定として復元する"
                )
                .Text(CustomersOrdersSource(string.Empty))
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ConstraintName customers->orders#0 = -")
                        .Single(
                            "ReferentialAction customers->orders#0 = del=NoAction upd=NoAction"
                        );
                    e.ImportedResult.Add(MetadataIsEmpty());
                    e.ImportedResult.Add(NoWarnings());
                }),
            // 制約名は指定として残り、解釈できなかった参照アクションだけ未指定になる
            CSharpRule(
                    "csharp-rule-unknown-referential-action-token-warns-and-leaves-unspecified",
                    "未知の参照アクショントークンは警告＋未指定扱い"
                )
                .Text(
                    CustomersOrdersSource(
                        ", ConstraintName = \"FK_orders_customers\", OnDelete = \"RESTRICT\""
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ReferentialAction customers->orders#0 = del=NoAction upd=*");
                    e.ImportedResult.Add(
                        (
                            "メタデータの索引は制約名だけを指定として持ち、削除時の参照アクションは未指定",
                            imported =>
                                Reversed(imported)
                                    .RelationshipMetadata.TryGetValue(
                                        imported.Diagram.Relationships.Single().Id,
                                        out var metadata
                                    )
                                && metadata.ConstraintName == "FK_orders_customers"
                                && metadata.OnDelete is null
                        )
                    );
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_ReferentialActionUnknown,
                            "RESTRICT",
                            "customers",
                            "orders"
                        )
                    );
                }),
            CSharpRule(
                    "csharp-rule-column-without-type-meta-skipped-with-warning",
                    "[DbColumnMeta] なしの列は警告してスキップ"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Single("ColumnOrder partial = id");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_ColumnMissingTypeMeta,
                            "partial",
                            "mystery"
                        )
                    );
                }),
        ];

    /// <summary>
    /// CSharpReverseWarningTests（情報が黙って消える経路を警告で可視化する。取込自体は続行し、失われた／変質した情報だけを名指しする）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpWarningRules() =>
        [
            // 型メタを持つ列が 1 つも無い [Table] クラスも列ゼロのテーブルとして取り込む（＝存在を保つ）
            CSharpRule(
                    "csharp-rule-table-with-only-untyped-columns-is-kept-empty",
                    "型メタ無し列だけのテーブルは列ゼロで残り、名指し警告が出る"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("regions")]
                    public partial class RegionEntity
                    {
                        // 型カタログが解析できない verbatim 型（Oracle SDO_GEOMETRY 等）は [DbColumnMeta] が付かない
                        [Column("shape")]
                        public object Shape { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("Structure table *", 1)
                        .Single("Structure table regions")
                        .None("Structure column *");
                    e.Warnings.Contains(
                        string.Format(ReverseStrings.Reverse_TableWithoutColumns, "regions")
                    );
                }),
            // 両端テーブルが生存するためリレーションは残る（＝線が黙って消えない）
            CSharpRule(
                    "csharp-rule-relationship-to-columnless-table-is-kept-without-pair",
                    "列ゼロのテーブルを端点に持つリレーションは列ペアなしで残り警告が出る"
                )
                .Text(
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    [Table("regions")]
                    public partial class RegionEntity
                    {
                        [Column("region_id")]
                        public object RegionId { get; set; }

                        [NavigationReference("regions", "region_id", "shops", "region_id", true, true, false)]
                        public ICollection<ShopEntity> Shops { get; set; }
                    }

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [Column("region_id")]
                        [DbColumnMeta("int32")]
                        public int RegionId { get; set; }

                        [NavigationReference("regions", "region_id", "shops", "region_id", false, false, true)]
                        public RegionEntity Region { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ColumnPairs regions->shops#0");
                    e.Warnings.Contains(
                        string.Format(
                            ReverseStrings.Reverse_RelationshipColumnUnresolved,
                            "regions",
                            "region_id",
                            "regions",
                            "shops"
                        )
                    );
                }),
            // 一般の列スキップ警告（主キーであることを伝えない側）は出さない＝警告はちょうどこの 1 つ
            CSharpRule(
                    "csharp-rule-primary-key-column-without-type-meta-warns-about-lost-primary-key",
                    "型メタ無しの主キー列は主キー喪失の専用警告になる"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("regions")]
                    public partial class RegionEntity
                    {
                        [Key]
                        [Column("region_id")]
                        public object RegionId { get; set; }

                        [Column("name")]
                        [DbColumnMeta("string(50)")]
                        public string Name { get; set; }
                    }
                    """
                )
                .Expect(e =>
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_PrimaryKeyColumnMissingTypeMeta,
                            "regions",
                            "region_id"
                        )
                    )
                ),
            CSharpRule(
                    "csharp-rule-navigation-to-unknown-table-warns-and-is-ignored",
                    "端点テーブルが存在しないリレーションは名指し警告のうえ無視される"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [NavigationReference("regions", "region_id", "shops", "region_id", false, false, true)]
                        public object Region { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.None("RelationshipType *");
                    e.Warnings.Contains(
                        string.Format(
                            ReverseStrings.Reverse_NavigationTableMissing,
                            "regions",
                            "shops",
                            "regions"
                        )
                    );
                }),
            CSharpRule(
                    "csharp-rule-unreadable-navigation-attribute-warns-with-property-name",
                    "引数不足の [NavigationReference] は名指し警告のうえ無視される"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [NavigationReference("regions", "region_id")]
                        public object Region { get; set; }
                    }
                    """
                )
                .Expect(e =>
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_NavigationUnreadable,
                            "shops",
                            "Region"
                        )
                    )
                ),
            // 位置だけで読むと端点が入れ替わるため、読めなかったものとして警告のうえ無視する
            CSharpRule(
                    "csharp-rule-reordered-named-colon-arguments-warn-and-are-ignored",
                    "name: 形式で並べ替えた [NavigationReference] は警告のうえ無視される"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [NavigationReference(dependentTable: "shops", dependentColumn: "region_id", principalTable: "regions", principalColumn: "region_id", isCollection: false, cascade: false, isParentReference: true)]
                        public object Region { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.None("RelationshipType *");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_NavigationUnreadable,
                            "shops",
                            "Region"
                        )
                    );
                }),
            CSharpRule(
                    "csharp-rule-named-colon-arguments-in-declaration-order-are-read",
                    "並び順どおりの name: 形式は従来どおり読める"
                )
                .Text(
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    [Table("regions")]
                    public partial class RegionEntity
                    {
                        [Key]
                        [Column("region_id")]
                        [DbColumnMeta("int32")]
                        public int RegionId { get; set; }

                        [NavigationReference(principalTable: "regions", principalColumn: "region_id", dependentTable: "shops", dependentColumn: "region_id", isCollection: true, cascade: true, isParentReference: false)]
                        public ICollection<ShopEntity> Shops { get; set; }
                    }

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [Column("region_id")]
                        [DbColumnMeta("int32")]
                        public int RegionId { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1);
                    e.ImportedResult.Add(NoWarnings());
                }),
            CSharpRule(
                    "csharp-rule-duplicate-table-name-warns-and-keeps-both",
                    "同名テーブルの衝突を警告する（取込自体は続行）"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }
                    }

                    [Table("shops")]
                    public partial class ShopMirrorEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("Structure table *", 2);
                    e.Warnings.Exact(string.Format(ReverseStrings.Reverse_DuplicateTable, "shops"));
                })
                .NoFixedPoint(DuplicateNameFixedPoint),
            CSharpRule(
                    "csharp-rule-duplicate-column-name-warns-and-keeps-both",
                    "同名列の衝突を警告する（取込自体は続行）"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [Column("shop_id")]
                        [DbColumnMeta("string(50)")]
                        public string ShopIdText { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("Structure column *", 2);
                    e.Warnings.Exact(
                        string.Format(ReverseStrings.Reverse_DuplicateColumn, "shops", "shop_id")
                    );
                })
                .NoFixedPoint(DuplicateNameFixedPoint),
            // 同一ファイル内で partial 宣言が分かれると、[Table] を持たない側の列は読まれない
            CSharpRule(
                    "csharp-rule-partial-class-split-in-same-file-warns-about-skipped-columns",
                    "同一ファイル内の partial 分割で落ちる列を名指し警告する"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }
                    }

                    // 手書き partial（[Column] 付きプロパティを足す公式の運用）が同じファイルにあるケース
                    public partial class ShopEntity
                    {
                        [Column("nickname")]
                        [DbColumnMeta("string(50)")]
                        public string Nickname { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Single("ColumnOrder shops = shop_id");
                    e.Warnings.Exact(
                        string.Format(ReverseStrings.Reverse_PartialClassSplit, "ShopEntity", 1)
                    );
                }),
            // 列を持たない partial 分割は何も失わないため、警告を出さない（ノイズにしない）
            CSharpRule(
                    "csharp-rule-partial-class-split-without-columns-does-not-warn",
                    "列を持たない partial 分割は警告しない"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }
                    }

                    public partial class ShopEntity
                    {
                        public string Display => ShopId.ToString();
                    }
                    """
                )
                .Expect(e => e.ImportedResult.Add(NoWarnings())),
            // 定義済みの数値（"1"）は Enum.TryParse を通ってしまうため名前一致まで確認する（通すと OnUpdate = "1" が無警告で Cascade に化ける）
            CSharpRule(
                    "csharp-rule-numeric-referential-action-token-is-rejected",
                    "数値の参照アクショントークンは拒否して警告する"
                )
                .Text(
                    """
                    using System.Collections.Generic;

                    namespace Sample;

                    [Table("regions")]
                    public partial class RegionEntity
                    {
                        [Key]
                        [Column("region_id")]
                        [DbColumnMeta("int32")]
                        public int RegionId { get; set; }

                        [NavigationReference("regions", "region_id", "shops", "region_id", true, true, false, OnUpdate = "1")]
                        public ICollection<ShopEntity> Shops { get; set; }
                    }

                    [Table("shops")]
                    public partial class ShopEntity
                    {
                        [Key]
                        [Column("shop_id")]
                        [DbColumnMeta("int32")]
                        public int ShopId { get; set; }

                        [Column("region_id")]
                        [DbColumnMeta("int32")]
                        public int RegionId { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ReferentialAction regions->shops#0 = del=* upd=NoAction");
                    e.ImportedResult.Add(MetadataIsEmpty());
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_ReferentialActionUnknown,
                            "1",
                            "regions",
                            "shops"
                        )
                    );
                }),
        ];

    /// <summary>
    /// CSharpReversePrimaryKeyOrderTests の読み取りの規則（生成物の <c>[DbTableMeta(PrimaryKeyOrder = …)]</c> の配列式を書き換えたソース）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpPrimaryKeyOrderRules()
    {
        var source = PrimaryKeyOrderSource();

        return
        [
            // 配列の書き方（型付き・コレクション式）のどれでも読める（暗黙型 new[] は形の行 csharp-reordered-primary-key が見る）
            .. new (string Slug, string Array)[]
            {
                ("typed", "new string[] { \"OrderNo\", \"TenantId\" }"),
                ("collection-expression", "[\"OrderNo\", \"TenantId\"]"),
            }.Select(item =>
                CSharpRule(
                        $"csharp-rule-primary-key-order-array-form-{item.Slug}",
                        $"主キーの順序は配列の書き方に依らず読める（{item.Array}）"
                    )
                    .Text(
                        source.Replace(
                            GeneratedPrimaryKeyOrderArray,
                            item.Array,
                            StringComparison.Ordinal
                        )
                    )
                    .Expect(e =>
                    {
                        e.Imported.Single("PrimaryKeyOrder order_lines = order_no,tenant_id");
                        e.ImportedResult.Add(NoWarnings());
                    })
                    .Row()
            ),
            // 未知の名前・主キーでない列・重複・過不足・リテラルでない要素。不整合でも「コードが指定した」ことは伝える
            // （GUI マージで現在図の順序を持ち込まない）
            .. new (string Slug, string Array)[]
            {
                ("unknown", "new[] { \"OrderNo\", \"Unknown\" }"),
                ("non-key", "new[] { \"OrderNo\", \"Memo\" }"),
                ("duplicate", "new[] { \"OrderNo\", \"OrderNo\" }"),
                ("missing", "new[] { \"OrderNo\" }"),
                ("extra", "new[] { \"OrderNo\", \"TenantId\", \"Memo\" }"),
                ("nameof", "new[] { nameof(OrderNo), \"TenantId\" }"),
            }.Select(item =>
                CSharpRule(
                        $"csharp-rule-primary-key-order-mismatch-{item.Slug}",
                        $"主キー列と一致しない順序は採らずに警告し、宣言順へ戻す（{item.Array}）"
                    )
                    .Text(
                        source.Replace(
                            GeneratedPrimaryKeyOrderArray,
                            item.Array,
                            StringComparison.Ordinal
                        )
                    )
                    .Expect(e =>
                    {
                        e.Imported.Single("PrimaryKeyOrder order_lines = tenant_id,order_no");
                        e.ImportedDiagram.Add(
                            (
                                "order_lines の主キーの順序を明示しない（PrimaryKeyColumnIds が空）",
                                diagram => diagram.Entities.Single().PrimaryKeyColumnIds.Count == 0
                            )
                        );
                        e.Warnings.Exact(
                            string.Format(
                                ReverseStrings.Reverse_PrimaryKeyOrderMismatch,
                                "order_lines",
                                item.Array
                            )
                        );
                        e.ImportedResult.Add(
                            (
                                "リバースの TablesWithPrimaryKeyOrder が order_lines を含む",
                                imported =>
                                    Reversed(imported)
                                        .TablesWithPrimaryKeyOrder.Contains("order_lines")
                            )
                        );
                    })
                    .Row()
            ),
        ];
    }

    /// <summary>CSharpReverseVerbatimTypeTests の verbatim の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpVerbatimRules() =>
        [
            // 手編集などで verbatim とトークンの意味が食い違ったら、トークン側を採って警告する
            CSharpRule(
                    "csharp-rule-verbatim-contradicts-token-prefers-token-with-warning",
                    "verbatim とトークンが食い違うときはトークンを採り警告する"
                )
                .Text(
                    """
                    namespace Sample;

                    [Table("samples")]
                    public partial class SampleEntity
                    {
                        [Key]
                        [Column("sample_id")]
                        [DbColumnMeta("int32", NativeType = "nvarchar(10)")]
                        public int SampleId { get; set; }
                    }
                    """
                )
                .Expect(e =>
                {
                    e.Imported.Single("Type samples.sample_id = int");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_VerbatimTypeMismatch,
                            "nvarchar(10)",
                            "samples",
                            "sample_id",
                            "int32"
                        )
                    );
                }),
            // SQL Server の 'datetime' を PostgreSQL 方言でリバースする（PostgreSQL に datetime は無い）。
            // 別方言の図から生成したコードでも、トークン側へ倒して警告する＝方言中立トークンによる可搬性は保たれる
            CSharpRule(
                    "csharp-rule-verbatim-unknown-to-target-dialect-prefers-token-with-warning",
                    "対象方言が解析できない verbatim はトークンへ倒して警告する"
                )
                .Text(
                    GeneratedSource(
                        FormatShapeRows.CSharpVerbatimDiagram("datetime"),
                        new CodeGenerationOptions
                        {
                            RootNamespace = "QuickER.Tests.ReverseVerbatim",
                            GenerateValueObjects = false,
                            SplitFilesByCategory = false,
                        },
                        DialectNames.SqlServer
                    )
                )
                .TargetDbms(DialectNames.PostgreSql)
                .Expect(e =>
                {
                    e.Imported.Single("Type samples.probe = timestamp");
                    e.Warnings.Exact(
                        string.Format(
                            ReverseStrings.Reverse_VerbatimTypeMismatch,
                            "datetime",
                            "samples",
                            "probe",
                            "datetime"
                        )
                    );
                }),
        ];

    /// <summary>
    /// CSharpReverseUnnamedForeignKeyTests の解析だけ（CLI）の規則（同じ組の名前の無い 2 本は DDL で別の名前になる）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpUnnamedForeignKeyRules() =>
        [
            // 先に来た 1 本は既定名のまま（名前なし）、ふさがった 2 本目にだけ名前が付く。付けた名前は「コードが指定した名前」ではない
            CSharpRule(
                    "csharp-rule-two-unnamed-references-get-distinct-names-in-ddl",
                    "解析だけ（CLI）でも、同じ組の名前の無い 2 本は DDL で別の名前になる"
                )
                .Text(ShipBillSource())
                .Expect(e =>
                {
                    e.Imported.Count("ConstraintName * = -", 1);
                    e.ImportedDiagram.Add(DistinctForeignKeyNamesInDdl(2));
                    e.ImportedResult.Add(MetadataIsEmpty());
                }),
            CSharpRule(
                    "csharp-rule-single-unnamed-reference-stays-unnamed",
                    "名前の無い参照が 1 本だけなら名前は付けない"
                )
                .Text(
                    """
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
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1).Single("ConstraintName * = -")
                ),
            CSharpRule("csharp-rule-explicit-names-are-kept", "コードが指定した名前は変えない")
                .Text(
                    ShipBillSource(
                        shipArguments: ", ConstraintName = \"FK_Ship\"",
                        billArguments: ", ConstraintName = \"FK_Bill\""
                    )
                )
                .Expect(e =>
                    e.ImportedDiagram.Add(
                        (
                            "子の列 ship_customer_id のリレーションが FK_Ship、bill_customer_id のリレーションが FK_Bill",
                            diagram =>
                                ConstraintNameOfChild(diagram, "ship_customer_id") == "FK_Ship"
                                && ConstraintNameOfChild(diagram, "bill_customer_id") == "FK_Bill"
                        )
                    )
                ),
        ];

    /// <summary>
    /// 型トークンの方言ごとの規則（CSharpReverseLengthlessTokenTests・CSharpReversePrecisionlessDecimalTests）
    /// </summary>
    private static IEnumerable<ScenarioRow<FormatRule>> CSharpTypeTokenRules() =>
        [
            // 長さを要する方言では、長さ無しトークンはそのまま列の型になり警告が出る
            .. new[] { DialectNames.SqlServer, DialectNames.MySql, DialectNames.Oracle }
                .SelectMany(dialect =>
                    new[] { "string", "binary" }.Select(token => (dialect, token))
                )
                .Select(item =>
                {
                    var rule = CSharpRule(
                            $"csharp-rule-lengthless-token-{item.dialect}-{item.token}",
                            "長さ無しトークンは SQL Server / MySQL / Oracle ではトークン文字列のまま採用され警告が出る"
                                + $"（{item.dialect}・{item.token}）"
                        )
                        .Text(LengthlessTokenSource(item.token))
                        .TargetDbms(item.dialect)
                        .Expect(e =>
                        {
                            e.Imported.Count("Structure table *", 1)
                                .Count("Structure column *", 1)
                                .Single($"Type docs.memo = {item.token}");
                            e.Warnings.Exact(
                                string.Format(
                                    ReverseStrings.Reverse_TypeTokenUnresolved,
                                    item.token,
                                    "docs",
                                    "memo"
                                )
                            );
                        });

                    // 型の表記 string は 3 方言、binary は Oracle の型カタログで解析できない（SQL Server・MySQL の binary は書き戻せる）
                    return item.token == "string" || item.dialect == DialectNames.Oracle
                        ? rule.NoFixedPoint(UnparsableTypeFixedPoint).Row()
                        : rule.Row();
                }),
            // 長さ無しが正当な方言（PostgreSQL）では従来どおりネイティブ型へ展開され、警告も出ない
            .. new (string Token, string Expected)[]
            {
                ("string", "varchar"),
                ("binary", "bytea"),
            }.Select(item =>
                CSharpRule(
                        $"csharp-rule-lengthless-token-postgresql-{item.Token}",
                        $"PostgreSQL では長さ無しトークンも従来どおりネイティブ型へ展開される（{item.Token}）"
                    )
                    .Text(LengthlessTokenSource(item.Token))
                    .TargetDbms(DialectNames.PostgreSql)
                    .Expect(e =>
                    {
                        e.Imported.Single($"Type docs.memo = {item.Expected}");
                        e.ImportedResult.Add(NoWarnings());
                    })
                    .Row()
            ),
            // 長さ付きトークンは 3 方言とも従来どおり展開される（長さ無しだけが落ちていることの対照）
            .. new (string Dialect, string Expected)[]
            {
                (DialectNames.SqlServer, "nvarchar(50)"),
                (DialectNames.MySql, "varchar(50)"),
                (DialectNames.Oracle, "NVARCHAR2(50)"),
            }.Select(item =>
                CSharpRule(
                        $"csharp-rule-token-with-length-{item.Dialect}",
                        $"長さ付きトークンは 3 方言とも従来どおり展開される（{item.Dialect}）"
                    )
                    .Text(LengthlessTokenSource("string(50)"))
                    .TargetDbms(item.Dialect)
                    .Expect(e =>
                    {
                        e.Imported.Single($"Type docs.memo = {item.Expected}");
                        e.ImportedResult.Add(NoWarnings());
                    })
                    .Row()
            ),
            // 精度なし・スケールありを書き出せない方言では、トークン文字列のまま列の型に採り警告する
            // （素の decimal / numeric へ黙って落とすと小数部の丸めや桁の宣言が変わる）
            .. new[] { DialectNames.SqlServer, DialectNames.MySql, DialectNames.PostgreSql }.Select(
                dialect =>
                    CSharpRule(
                            $"csharp-rule-precisionless-scaled-token-{dialect}",
                            $"decimal(*,s) は Oracle 以外ではトークン文字列のまま採用され警告が出る（{dialect}）"
                        )
                        .Text(
                            """
                            namespace Sample;

                            [Table("docs")]
                            public partial class DocEntity
                            {
                                [Column("amount")]
                                [DbColumnMeta("decimal(*,2)")]
                                public decimal? Amount { get; set; }
                            }
                            """
                        )
                        .TargetDbms(dialect)
                        .Expect(e =>
                        {
                            e.Imported.Count("Structure table *", 1)
                                .Count("Structure column *", 1)
                                .Single("Type docs.amount = decimal(*,2)");
                            e.Warnings.Exact(
                                string.Format(
                                    ReverseStrings.Reverse_TypeTokenUnresolved,
                                    "decimal(*,2)",
                                    "docs",
                                    "amount"
                                )
                            );
                        })
                        .NoFixedPoint(UnparsableTypeFixedPoint)
                        .Row()
            ),
        ];

    /// <summary>C# リバースの規則の行を始める</summary>
    private static FormatRuleBuilder CSharpRule(string id, string description) =>
        new(CSharpReverseAdapter.Value, id, description);

    /// <summary>リバースが断る例外の「型名: メッセージ」のパターン</summary>
    private static string ReverseFailure(string message) =>
        nameof(CodeReverseException) + ": " + message;

    /// <summary>取込の付帯情報（C# リバースの結果）</summary>
    private static CodeReverseResult Reversed(FormatImport imported) =>
        (CodeReverseResult)imported.Extra!;

    /// <summary>取込の警告が無い</summary>
    private static (string, Func<FormatImport, bool>) NoWarnings() =>
        ("取込の警告が無い", imported => imported.Warnings.Count == 0);

    /// <summary>リバースのメタデータの索引（コードが指定した外部キーの情報）が空</summary>
    private static (string, Func<FormatImport, bool>) MetadataIsEmpty() =>
        (
            "リバースの RelationshipMetadata が空",
            imported => Reversed(imported).RelationshipMetadata.Count == 0
        );

    /// <summary>SQL Server の DDL に現れる外部キーの制約名がちょうど n 個で、すべて違う</summary>
    internal static (string, Func<ErDiagram, bool>) DistinctForeignKeyNamesInDdl(int count) =>
        (
            $"SQL Server の DDL の外部キーの制約名がちょうど {count} 個で、すべて違う",
            diagram =>
            {
                var names = System
                    .Text.RegularExpressions.Regex.Matches(
                        new SqlServerDdlGenerator().Build(diagram),
                        @"CONSTRAINT \[([^\]]+)\] FOREIGN KEY"
                    )
                    .Select(match => match.Groups[1].Value)
                    .ToList();

                return names.Count == count
                    && names.Distinct(StringComparer.Ordinal).Count() == count;
            }
        );

    /// <summary>子の列名からリレーションを引いて制約名を返す（引けなければ null）</summary>
    internal static string? ConstraintNameOfChild(ErDiagram diagram, string childColumnName)
    {
        var columnId = diagram
            .Entities.SelectMany(entity => entity.Columns)
            .Single(column => column.Name == childColumnName)
            .Id;

        return diagram
            .Relationships.Single(relationship =>
                relationship.ColumnPairs.Any(pair => pair.TargetColumnId == columnId)
            )
            .ConstraintName;
    }

    /// <summary>図から C# を生成したソース（表を組むときに生成する）</summary>
    internal static string GeneratedSource(
        ErDiagram diagram,
        CodeGenerationOptions options,
        string dialect
    )
    {
        using var artifact = FormatAxis
            .Axis.Get(CSharpReverseAdapter.Value)
            .Export(diagram, new FormatSettings(options, dialect));
        return artifact.Text;
    }

    /// <summary>生成物が主キーの順序に書く配列式（order_no → tenant_id）</summary>
    internal const string GeneratedPrimaryKeyOrderArray = "new[] { \"OrderNo\", \"TenantId\" }";

    /// <summary>CSharpReversePrimaryKeyOrderTests の生成のオプション</summary>
    internal static CodeGenerationOptions PrimaryKeyOrderOptions =>
        new()
        {
            RootNamespace = "QuickER.Tests.ReversePrimaryKeyOrder",
            GenerateValueObjects = false,
            SplitFilesByCategory = false,
        };

    /// <summary>主キーの順序を order_no → tenant_id と明示した order_lines の生成物（SQL Server）</summary>
    private static string PrimaryKeyOrderSource()
    {
        var source = GeneratedSource(
            FormatShapeRows.CSharpPrimaryKeyOrderDiagram(["order_no", "tenant_id"]),
            PrimaryKeyOrderOptions,
            DialectNames.SqlServer
        );

        return source.Contains(GeneratedPrimaryKeyOrderArray, StringComparison.Ordinal)
            ? source
            : throw new InvalidOperationException("生成物に主キーの順序の配列式が無い");
    }

    /// <summary>customers 1 対多 orders のソース（両側のナビゲーションへ同じ追加引数を刻む。CSharpReverseParserTests.BuildRelationshipSource）</summary>
    private static string CustomersOrdersSource(string extraArguments) =>
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

    /// <summary>orders が customers を 2 本（出荷先・請求先）参照するコード（CSharpReverseUnnamedForeignKeyTests.Source）</summary>
    internal static string ShipBillSource(string shipArguments = "", string billArguments = "") =>
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

    /// <summary>長さ無しトークンを 1 列だけ持つ生成コード相当のソース（CSharpReverseLengthlessTokenTests.Source）</summary>
    private static string LengthlessTokenSource(string token) =>
        $$"""
            namespace Sample;

            [Table("docs")]
            public partial class DocEntity
            {
                [Column("memo")]
                [DbColumnMeta("{{token}}")]
                public string? Memo { get; set; }
            }
            """;
}
