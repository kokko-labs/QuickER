using FluentAssertions;
using QuickER.PostgreSql;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="PostgreSqlCSharpTypeMapper"/> の PostgreSQL 型 → C# 型情報変換を検証するテストクラス。
/// とくに bytea の byte[] マッピングと無制限バイナリ（<c>IsUnboundedBinary</c>）判定を確認する。
/// </summary>
public class PostgreSqlCSharpTypeMapperTests
{
    private static readonly PostgreSqlCSharpTypeMapper Mapper = new();

    [Fact(DisplayName = "bytea は byte[]（参照型）へマップされる")]
    public void Map_Bytea_MapsToByteArray()
    {
        var info = Mapper.Map("bytea");

        info.TypeName.Should().Be("byte[]");
        info.IsReferenceType.Should().BeTrue();
    }

    [Fact(DisplayName = "bytea は無制限バイナリと判定される")]
    public void Map_Bytea_IsUnboundedBinary()
    {
        Mapper.Map("bytea").IsUnboundedBinary.Should().BeTrue();
    }

    [Fact(DisplayName = "非バイナリ型は IsUnboundedBinary=false になる")]
    public void Map_NonBinaryType_IsUnboundedBinaryIsFalse()
    {
        Mapper.Map("varchar(100)").IsUnboundedBinary.Should().BeFalse();
        Mapper.Map("integer").IsUnboundedBinary.Should().BeFalse();
    }
}
