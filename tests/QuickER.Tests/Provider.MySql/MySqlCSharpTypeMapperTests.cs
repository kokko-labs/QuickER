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
