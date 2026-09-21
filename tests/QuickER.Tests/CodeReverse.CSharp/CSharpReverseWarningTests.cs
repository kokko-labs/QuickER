using AwesomeAssertions;
using QuickER.CodeReverse.CSharp;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using ReverseStrings = QuickER.CodeReverse.CSharp.Resources.Strings;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// 「情報が黙って消える」経路を警告で可視化する挙動（Phase R）を検証する。
/// </summary>
/// <remarks>
/// 対象は、型メタ無し列だけのテーブル（従来はテーブルごと無警告で消えた）・主キー列のスキップ・
/// 外部キー列の解決失敗によるリレーションの退化・読めない <c>[NavigationReference]</c>・
/// 同名テーブル／同名列の衝突・同一ファイル内の partial 分割による列の脱落。
/// いずれも取込自体は続行し（＝復元できるものは復元し）、失われた／変質した情報だけを名指しする。
/// </remarks>
public class CSharpReverseWarningTests
{
    private static CodeReverseResult Parse(string source) =>
        new CSharpReverseParser().Parse(source, new SqlServerTypeCatalog());

    /// <summary>
    /// 型メタ（<c>[DbColumnMeta]</c>）を持つ列が 1 つも無い <c>[Table]</c> クラスも、列ゼロのテーブルとして
    /// 取り込む（＝存在を保つ）。従来はテーブルごと無警告で消え、関連リレーションまで道連れになっていた。
    /// </summary>
    [Fact(DisplayName = "型メタ無し列だけのテーブルは列ゼロで残り、名指し警告が出る")]
    public void Parse_TableWithOnlyUntypedColumns_IsKeptEmptyWithWarning()
    {
        const string source = """
            namespace Sample;

            [Table("regions")]
            public partial class RegionEntity
            {
                // 型カタログが解析できない verbatim 型（Oracle SDO_GEOMETRY 等）は [DbColumnMeta] が付かない
                [Column("shape")]
                public object Shape { get; set; }
            }
            """;

        var result = Parse(source);

        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be("regions");
        entity.Columns.Should().BeEmpty();
        result
            .Warnings.Should()
            .Contain(string.Format(ReverseStrings.Reverse_TableWithoutColumns, "regions"));
    }

    /// <summary>
    /// 型メタ無しで消えるテーブルを参照していたリレーションも、テーブルが残ることで
    /// 「解決できない端点」として名指しされる（従来は無警告で丸ごと消えた）。
    /// </summary>
    [Fact(DisplayName = "列ゼロのテーブルを端点に持つリレーションは列ペアなしで残り警告が出る")]
    public void Parse_RelationshipToColumnlessTable_IsKeptWithoutPairAndWarns()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        // 両端テーブルが生存するためリレーションは残る（＝線が黙って消えない）
        var relationship = result.Relationships.Should().ContainSingle().Subject;
        relationship.ColumnPairs.Should().BeEmpty();
        result
            .Warnings.Should()
            .Contain(
                string.Format(
                    ReverseStrings.Reverse_RelationshipColumnUnresolved,
                    "regions",
                    "region_id",
                    "regions",
                    "shops"
                )
            );
    }

    /// <summary>型メタ欠落でスキップした列が <c>[Key]</c> なら、主キー喪失を名指しする専用警告になる</summary>
    [Fact(DisplayName = "型メタ無しの主キー列は主キー喪失の専用警告になる")]
    public void Parse_PrimaryKeyColumnWithoutTypeMeta_WarnsAboutLostPrimaryKey()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_PrimaryKeyColumnMissingTypeMeta,
                    "regions",
                    "region_id"
                )
            );
        // 一般の列スキップ警告（主キーであることを伝えない側）は出さない
        result
            .Warnings.Should()
            .NotContain(
                string.Format(ReverseStrings.Reverse_ColumnMissingTypeMeta, "regions", "region_id")
            );
    }

    /// <summary>参照先テーブルが解析結果に無いリレーションは、無視したことを名指しで警告する</summary>
    [Fact(DisplayName = "端点テーブルが存在しないリレーションは名指し警告のうえ無視される")]
    public void Parse_NavigationToUnknownTable_WarnsAndIsIgnored()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Relationships.Should().BeEmpty();
        result
            .Warnings.Should()
            .Contain(
                string.Format(
                    ReverseStrings.Reverse_NavigationTableMissing,
                    "regions",
                    "shops",
                    "regions"
                )
            );
    }

    /// <summary>引数が足りない <c>[NavigationReference]</c> は、無視したことをプロパティ名込みで警告する</summary>
    [Fact(DisplayName = "引数不足の [NavigationReference] は名指し警告のうえ無視される")]
    public void Parse_UnreadableNavigationAttribute_WarnsWithPropertyName()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_NavigationUnreadable, "shops", "Region"));
    }

    /// <summary>
    /// <c>name:</c> 形式（名前付き引数）で端点を並べ替えた <c>[NavigationReference]</c> は、
    /// 位置だけで読むと端点が入れ替わるため、読めなかったものとして警告のうえ無視する
    /// </summary>
    [Fact(DisplayName = "name: 形式で並べ替えた [NavigationReference] は警告のうえ無視される")]
    public void Parse_ReorderedNamedColonArguments_WarnsAndIsIgnored()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Relationships.Should().BeEmpty();
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_NavigationUnreadable, "shops", "Region"));
    }

    /// <summary>並び順どおりの <c>name:</c> 形式は従来どおり読める（警告を出さない）</summary>
    [Fact(DisplayName = "並び順どおりの name: 形式は従来どおり読める")]
    public void Parse_NamedColonArgumentsInDeclarationOrder_AreRead()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Relationships.Should().ContainSingle();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>同じテーブル名を複数クラスが宣言していたら、受け入れたうえで衝突を警告する</summary>
    [Fact(DisplayName = "同名テーブルの衝突を警告する（取込自体は続行）")]
    public void Parse_DuplicateTableName_WarnsAndKeepsBoth()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Entities.Should().HaveCount(2);
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_DuplicateTable, "shops"));
    }

    /// <summary>同じ列名を 1 クラスで複数回宣言していたら、受け入れたうえで衝突を警告する</summary>
    [Fact(DisplayName = "同名列の衝突を警告する（取込自体は続行）")]
    public void Parse_DuplicateColumnName_WarnsAndKeepsBoth()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Entities.Single().Columns.Should().HaveCount(2);
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_DuplicateColumn, "shops", "shop_id"));
    }

    /// <summary>
    /// 同一ファイル内で partial 宣言が分かれると、<c>[Table]</c> を持たない側の列は読まれない。
    /// 取りこぼしを黙らせないよう、クラス名と脱落した列数を名指しで警告する。
    /// </summary>
    [Fact(DisplayName = "同一ファイル内の partial 分割で落ちる列を名指し警告する")]
    public void Parse_PartialClassSplitInSameFile_WarnsAboutSkippedColumns()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Entities.Single().Columns.Select(column => column.Name).Should().Equal("shop_id");
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(ReverseStrings.Reverse_PartialClassSplit, "ShopEntity", 1));
    }

    /// <summary>列を持たない partial 分割は何も失わないため、警告を出さない（ノイズにしない）</summary>
    [Fact(DisplayName = "列を持たない partial 分割は警告しない")]
    public void Parse_PartialClassSplitWithoutColumns_DoesNotWarn()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// 参照アクションのトークンは列挙体「名」で照合する。定義済みの数値（<c>"1"</c>）は
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> を通ってしまうため、名前一致まで確認する
    /// （通してしまうと <c>OnUpdate = "1"</c> が無警告で Cascade に化ける）。
    /// </summary>
    [Fact(DisplayName = "数値の参照アクショントークンは拒否して警告する")]
    public void Parse_NumericReferentialActionToken_IsRejected()
    {
        const string source = """
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
            """;

        var result = Parse(source);

        var relationship = result.Relationships.Should().ContainSingle().Subject;
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
        result.RelationshipMetadata.Should().BeEmpty();
        result
            .Warnings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                string.Format(
                    ReverseStrings.Reverse_ReferentialActionUnknown,
                    "1",
                    "regions",
                    "shops"
                )
            );
    }
}
