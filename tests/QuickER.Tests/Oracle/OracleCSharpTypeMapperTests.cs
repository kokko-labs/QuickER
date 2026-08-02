using AwesomeAssertions;
using QuickER.Oracle;

namespace QuickER.Tests.Oracle;

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
}
