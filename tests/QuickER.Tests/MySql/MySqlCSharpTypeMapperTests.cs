using FluentAssertions;
using QuickER.MySql;

namespace QuickER.Tests.MySql;

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
}
