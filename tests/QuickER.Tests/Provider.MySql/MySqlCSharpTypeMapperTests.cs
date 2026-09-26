using AwesomeAssertions;
using QuickER.Provider.MySql;

namespace QuickER.Tests.Provider.MySql;

/// <summary>
/// <see cref="MySqlCSharpTypeMapper"/> の MySQL 型 → C# 型情報変換を検証するテストクラス。
/// とくに BLOB 系の byte[] マッピング（tinyblob の取りこぼし回帰含む）と
/// 無制限バイナリ（<c>IsUnboundedBinary</c>）判定を確認する。
/// </summary>
public class MySqlCSharpTypeMapperTests
{
    private static readonly MySqlCSharpTypeMapper Mapper = new();

    [Theory(
        DisplayName = "バイナリ系は byte[]（参照型）へマップされる（tinyblob の取りこぼし回帰含む）"
    )]
    [InlineData("tinyblob")]
    [InlineData("blob")]
    [InlineData("mediumblob")]
    [InlineData("longblob")]
    [InlineData("binary(16)")]
    [InlineData("varbinary(100)")]
    public void Map_BinaryTypes_MapToByteArray(string dataType)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be("byte[]");
        info.IsReferenceType.Should().BeTrue();
    }

    [Theory(
        DisplayName = "blob/mediumblob/longblob は無制限バイナリ、tinyblob/binary(n)/varbinary(n) は有界"
    )]
    [InlineData("blob", true)]
    [InlineData("mediumblob", true)]
    [InlineData("longblob", true)]
    [InlineData("tinyblob", false)]
    [InlineData("binary(16)", false)]
    [InlineData("varbinary(100)", false)]
    public void Map_ResolvesUnboundedBinary(string dataType, bool expected)
    {
        Mapper.Map(dataType).IsUnboundedBinary.Should().Be(expected);
    }

    [Fact(DisplayName = "非バイナリ型は IsUnboundedBinary=false になる")]
    public void Map_NonBinaryType_IsUnboundedBinaryIsFalse()
    {
        Mapper.Map("varchar(255)").IsUnboundedBinary.Should().BeFalse();
        Mapper.Map("int").IsUnboundedBinary.Should().BeFalse();
    }

    /// <summary>
    /// <c>bit(n&gt;1)</c> は <c>ulong</c>、<c>year</c> は <c>int</c>（MySqlConnector が返す CLR 型に合わせる）。
    /// </summary>
    /// <remarks>
    /// 従来はどちらも「未知の型 → string」のフォールバックへ落ちており、生成コードが読み出しで
    /// <c>InvalidCastException</c> になっていた。ドライバの実測値との一致は
    /// <c>MySqlColumnClrTypeIntegrationTests</c>（実 mysql:8.4）が固定する。
    /// </remarks>
    [Theory(DisplayName = "bit(n>1) は ulong、year は int へマップされる")]
    [InlineData("bit(2)", "ulong")]
    [InlineData("bit(8)", "ulong")]
    [InlineData("bit(64)", "ulong")]
    [InlineData("BIT(8)", "ulong")]
    [InlineData("year", "int")]
    [InlineData("YEAR", "int")]
    public void Map_BitFieldAndYear_MapToDriverClrTypes(string dataType, string expected)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be(expected);
        info.IsReferenceType.Should().BeFalse();
    }

    /// <summary>
    /// <c>bit(1)</c>（と引数なしの <c>bit</c>）は従来どおり真偽値慣習で <c>bool</c>。
    /// </summary>
    /// <remarks>
    /// ドライバは <c>bit(1)</c> にも <c>ulong</c> を返すが、1 ビットを真偽値として扱うのは
    /// <c>tinyint(1)</c> と対の既存の慣習で、<c>bit(n&gt;1)</c> の追加では変えない。
    /// </remarks>
    [Theory(DisplayName = "bit(1) / 引数なし bit は従来どおり bool のまま")]
    [InlineData("bit")]
    [InlineData("bit(1)")]
    public void Map_SingleBit_StaysBool(string dataType)
    {
        Mapper.Map(dataType).TypeName.Should().Be("bool");
    }

    /// <summary>
    /// 符号なし整数は符号なしの CLR 型（byte / ushort / uint / ulong）へマップされる。
    /// </summary>
    /// <remarks>
    /// 従来は <c>unsigned</c> 修飾子を落として符号付きの型で解決しており、符号付きの範囲を超える値の
    /// 読み出しが <c>OverflowException</c> になっていた。ドライバ（MySqlConnector）・EF Core
    /// （<c>UseMySQL</c>）の実測値との一致は <c>MySqlColumnClrTypeIntegrationTests</c>（実 mysql:8.4）が固定する。
    /// </remarks>
    [Theory(DisplayName = "符号なし整数は byte / ushort / uint / ulong へマップされる")]
    [InlineData("tinyint unsigned", "byte")]
    [InlineData("smallint unsigned", "ushort")]
    [InlineData("mediumint unsigned", "uint")]
    [InlineData("int unsigned", "uint")]
    [InlineData("integer unsigned", "uint")]
    [InlineData("bigint unsigned", "ulong")]
    [InlineData("INT UNSIGNED", "uint")]
    [InlineData("int unsigned zerofill", "uint")]
    public void Map_UnsignedIntegers_MapToUnsignedClrTypes(string dataType, string expected)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be(expected);
        info.IsReferenceType.Should().BeFalse();
        info.IsFallbackType.Should().BeFalse();
    }

    [Theory(DisplayName = "符号付き整数は従来どおりの CLR 型のまま")]
    [InlineData("tinyint", "sbyte")]
    [InlineData("smallint", "short")]
    [InlineData("int", "int")]
    [InlineData("bigint", "long")]
    [InlineData("int signed", "int")]
    public void Map_SignedIntegers_KeepSignedClrTypes(string dataType, string expected)
    {
        Mapper.Map(dataType).TypeName.Should().Be(expected);
    }

    /// <summary>
    /// <c>mediumint</c> は符号付きが <c>int</c>、符号なしが <c>uint</c>（従来はどちらもフォールバックの string）。
    /// </summary>
    [Theory(DisplayName = "mediumint は int / uint へマップされる（フォールバックに落ちない）")]
    [InlineData("mediumint", "int")]
    [InlineData("mediumint(9)", "int")]
    [InlineData("mediumint unsigned", "uint")]
    public void Map_MediumInt_ResolvesToIntegerTypes(string dataType, string expected)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be(expected);
        info.IsFallbackType.Should().BeFalse();
    }

    /// <summary>
    /// <c>tinyint unsigned</c> には真偽値慣習を当てない（表示幅の有無を問わず <c>byte</c>）。
    /// </summary>
    /// <remarks>
    /// MySQL 8.4 は符号なし tinyint から表示幅を落とすため、取込は <c>tinyint(1) unsigned</c> を
    /// <c>tinyint unsigned</c> として持ち帰る（実測）。ドライバも <c>byte</c> を返し Boolean にはならない。
    /// 型カタログ側（<c>MySqlTypeCatalog.TryParseTinyInt</c>）が Boolean 判定から unsigned を外しているのと同じ線引き。
    /// </remarks>
    [Theory(DisplayName = "tinyint unsigned は真偽値慣習の対象外で byte になる")]
    [InlineData("tinyint unsigned")]
    [InlineData("tinyint(1) unsigned")]
    public void Map_UnsignedTinyInt_IsNotTreatedAsBoolean(string dataType)
    {
        Mapper.Map(dataType).TypeName.Should().Be("byte");
    }

    [Theory(DisplayName = "tinyint(1)（符号付き）は従来どおり bool のまま")]
    [InlineData("tinyint(1)")]
    [InlineData("TINYINT(1)")]
    public void Map_SignedTinyIntOne_StaysBool(string dataType)
    {
        Mapper.Map(dataType).TypeName.Should().Be("bool");
    }

    /// <summary>整数以外の型は <c>unsigned</c> が付いても CLR 型が変わらない。</summary>
    [Theory(DisplayName = "decimal / float / double は unsigned でも CLR 型が変わらない")]
    [InlineData("decimal(10,2) unsigned", "decimal")]
    [InlineData("float unsigned", "float")]
    [InlineData("double unsigned", "double")]
    public void Map_UnsignedNonIntegerTypes_KeepClrType(string dataType, string expected)
    {
        Mapper.Map(dataType).TypeName.Should().Be(expected);
    }

    /// <summary>値リストに <c>unsigned</c> の語を含む <c>enum</c> を符号なしと誤判定しない。</summary>
    [Fact(DisplayName = "enum の値リストの unsigned という語は修飾子として拾わない")]
    public void Map_EnumWithUnsignedValue_IsStillFallback()
    {
        var info = Mapper.Map("enum('signed','unsigned')");

        info.TypeName.Should().Be("string");
        info.IsFallbackType.Should().BeTrue();
    }

    [Theory(DisplayName = "未知の型は IsFallbackType が true になる")]
    [InlineData("geometry")]
    [InlineData("enum")]
    public void Map_UnknownType_IsFallbackTypeIsTrue(string dataType)
    {
        Mapper.Map(dataType).IsFallbackType.Should().BeTrue();
    }

    [Fact(DisplayName = "解決できる型は IsFallbackType が false になる")]
    public void Map_KnownType_IsFallbackTypeIsFalse()
    {
        Mapper.Map("int").IsFallbackType.Should().BeFalse();
        Mapper.Map("varchar(255)").IsFallbackType.Should().BeFalse();
    }
}
