using AwesomeAssertions;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// <see cref="OracleCSharpTypeMapper"/> の Oracle 型 → C# 型情報変換を検証するテストクラス。
/// とくに RAW / BLOB / LONG RAW の byte[] マッピングと
/// 無制限バイナリ（<c>IsUnboundedBinary</c>）判定を確認する。
/// </summary>
public class OracleCSharpTypeMapperTests
{
    private static readonly OracleCSharpTypeMapper Mapper = new();

    [Theory(DisplayName = "バイナリ系は byte[]（参照型）へマップされる")]
    [InlineData("blob")]
    [InlineData("long raw")]
    [InlineData("raw(16)")]
    public void Map_BinaryTypes_MapToByteArray(string dataType)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be("byte[]");
        info.IsReferenceType.Should().BeTrue();
    }

    [Theory(DisplayName = "BLOB・LONG RAW は無制限バイナリ、RAW(n) は有界")]
    [InlineData("blob", true)]
    [InlineData("long raw", true)]
    [InlineData("raw(16)", false)]
    public void Map_ResolvesUnboundedBinary(string dataType, bool expected)
    {
        Mapper.Map(dataType).IsUnboundedBinary.Should().Be(expected);
    }

    [Fact(DisplayName = "非バイナリ型は IsUnboundedBinary=false になる")]
    public void Map_NonBinaryType_IsUnboundedBinaryIsFalse()
    {
        Mapper.Map("varchar2(100)").IsUnboundedBinary.Should().BeFalse();
        Mapper.Map("number(10)").IsUnboundedBinary.Should().BeFalse();
    }

    [Theory(DisplayName = "未知の型は IsFallbackType が true になる")]
    [InlineData("SDO_GEOMETRY")]
    [InlineData("BFILE")]
    public void Map_UnknownType_IsFallbackTypeIsTrue(string dataType)
    {
        Mapper.Map(dataType).IsFallbackType.Should().BeTrue();
    }

    /// <summary>
    /// ANSI の INTEGER / SMALLINT は Oracle では NUMBER(*,0) として作られるので、NUMBER(*,0) と同じ decimal へ写す
    /// （フォールバックの string にしない）ことを検証する。
    /// </summary>
    [Theory(DisplayName = "INTEGER・SMALLINT は NUMBER(*,0) と同じ decimal へマップされる")]
    [InlineData("INTEGER")]
    [InlineData("SMALLINT")]
    [InlineData("integer")]
    public void Map_AnsiInteger_IsDecimalLikeNumberStarZero(string dataType)
    {
        var info = Mapper.Map(dataType);
        var number = Mapper.Map("NUMBER(*,0)");

        info.TypeName.Should().Be("decimal");
        info.IsFallbackType.Should().BeFalse();
        info.Precision.Should().Be(number.Precision);
        info.Scale.Should().Be(number.Scale);
    }

    [Fact(DisplayName = "解決できる型は IsFallbackType が false になる")]
    public void Map_KnownType_IsFallbackTypeIsFalse()
    {
        Mapper.Map("number(10)").IsFallbackType.Should().BeFalse();
        Mapper.Map("varchar2(50)").IsFallbackType.Should().BeFalse();
    }

    /// <summary>
    /// 単位語つきの長さ（<c>VARCHAR2(50 CHAR)</c> / <c>CHAR(10 BYTE)</c>）からも最大長を読むことを検証する。
    /// <c>NLS_LENGTH_SEMANTICS=CHAR</c> の DB では取込の全文字列列がこの形になるため、読めないと
    /// [MaxLength]・HasMaxLength・値オブジェクトの長さ検証が全部消える。
    /// </summary>
    [Theory(DisplayName = "単位語つきの長さからも最大長を読む")]
    [InlineData("VARCHAR2(50 CHAR)", 50)]
    [InlineData("VARCHAR2(50 BYTE)", 50)]
    [InlineData("CHAR(10 BYTE)", 10)]
    [InlineData("CHAR(10 CHAR)", 10)]
    [InlineData("VARCHAR2(50)", 50)]
    public void LengthWithUnit_IsRead(string dataType, int expected)
    {
        Mapper.Map(dataType).MaxLength.Should().Be(expected);
    }

    /// <summary>
    /// <c>TIMESTAMP WITH LOCAL TIME ZONE</c> は DateTime へ写すことを検証する（ODP.NET が返す CLR 型。
    /// DateTimeOffset で受けると読み取りが InvalidCastException になる）。WITH TIME ZONE は DateTimeOffset のまま。
    /// </summary>
    [Theory(DisplayName = "WITH LOCAL TIME ZONE は DateTime、WITH TIME ZONE は DateTimeOffset")]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE", "DateTime")]
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE", "DateTime")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", "DateTimeOffset")]
    [InlineData("TIMESTAMP(6)", "DateTime")]
    public void TimestampFamily_MapsToDriverType(string dataType, string expected)
    {
        Mapper.Map(dataType).TypeName.Should().Be(expected);
    }
}
