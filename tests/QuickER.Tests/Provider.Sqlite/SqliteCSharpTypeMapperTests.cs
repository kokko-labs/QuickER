using AwesomeAssertions;
using QuickER.Provider.Sqlite;

namespace QuickER.Tests.Provider.Sqlite;

/// <summary>
/// <see cref="SqliteCSharpTypeMapper"/> の SQLite 宣言型 → C# 型情報変換を検証するテストクラス。
/// とくに byte[] へのマッピングと無制限バイナリ（<c>IsUnboundedBinary</c>）判定を確認する。
/// </summary>
public class SqliteCSharpTypeMapperTests
{
    private static readonly SqliteCSharpTypeMapper Mapper = new();

    [Theory(DisplayName = "バイナリ宣言型は byte[]（参照型）へマップされる")]
    [InlineData("blob")]
    [InlineData("blob(1000)")]
    [InlineData("binary")]
    [InlineData("varbinary")]
    [InlineData("varbinary(100)")]
    [InlineData("varbinary(max)")]
    public void Map_BinaryTypes_MapToByteArray(string dataType)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be("byte[]");
        info.IsReferenceType.Should().BeTrue();
    }

    [Theory(DisplayName = "長さ宣言なし・(MAX) は無制限バイナリ、長さ付きは有界と判定される")]
    [InlineData("blob", true)]
    [InlineData("varbinary(max)", true)]
    [InlineData("binary", true)]
    [InlineData("blob(1000)", false)]
    [InlineData("varbinary(100)", false)]
    public void Map_ResolvesUnboundedBinary(string dataType, bool expected)
    {
        Mapper.Map(dataType).IsUnboundedBinary.Should().Be(expected);
    }

    [Fact(DisplayName = "非バイナリ型は IsUnboundedBinary=false になる")]
    public void Map_NonBinaryType_IsUnboundedBinaryIsFalse()
    {
        Mapper.Map("nvarchar(100)").IsUnboundedBinary.Should().BeFalse();
        Mapper.Map("int").IsUnboundedBinary.Should().BeFalse();
    }

    [Theory(DisplayName = "未知の宣言型は IsFallbackType が true になる")]
    [InlineData("geometry")]
    [InlineData("weird_type")]
    public void Map_UnknownType_IsFallbackTypeIsTrue(string dataType)
    {
        Mapper.Map(dataType).IsFallbackType.Should().BeTrue();
    }

    [Fact(DisplayName = "解決できる型は IsFallbackType が false になる")]
    public void Map_KnownType_IsFallbackTypeIsFalse()
    {
        Mapper.Map("int").IsFallbackType.Should().BeFalse();
        Mapper.Map("nvarchar(50)").IsFallbackType.Should().BeFalse();
    }

    /// <summary>
    /// REAL アフィニティの宣言型（REAL・FLOAT・DOUBLE 等）はすべて倍精度の <c>double</c> へ写すことを検証する。
    /// SQLite の REAL は 8 バイトの倍精度で、単精度の <c>float</c> で受けると読み書きのたびに精度を落とす。
    /// </summary>
    [Theory(DisplayName = "REAL アフィニティの宣言型は double へ写す")]
    [InlineData("REAL")]
    [InlineData("real")]
    [InlineData("FLOAT")]
    [InlineData("DOUBLE")]
    [InlineData("DOUBLE PRECISION")]
    [InlineData("FLOAT8")]
    [InlineData("REAL(10)")]
    public void Map_RealAffinity_IsDouble(string dataType)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be("double");
        info.IsFallbackType.Should().BeFalse();
    }

    /// <summary>
    /// アフィニティの判定順（INT → TEXT 系 → BLOB → REAL）どおり、先の規則に当たる名前は REAL として扱わないことを検証する。
    /// </summary>
    [Theory(DisplayName = "先のアフィニティ規則に当たる名前は double にしない")]
    [InlineData("FLOATINT")]
    [InlineData("REALCHAR")]
    [InlineData("DOUBLEBLOB")]
    public void Map_EarlierAffinityWins(string dataType)
    {
        Mapper.Map(dataType).TypeName.Should().NotBe("double");
    }
}
