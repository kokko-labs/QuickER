using FluentAssertions;
using QuickER.Sqlite;

namespace QuickER.Tests.Sqlite;

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
}
