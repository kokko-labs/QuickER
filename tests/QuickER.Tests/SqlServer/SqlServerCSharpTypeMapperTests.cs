using AwesomeAssertions;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

/// <summary>
/// <see cref="SqlServerCSharpTypeMapper"/> の SQL Server 型 → C# 型情報変換を検証するテストクラス。
/// とくに SQL パラメータ型明示化に使う <c>SqlDbTypeName</c> と宣言長（Size 用）の解決を確認する。
/// </summary>
public class SqlServerCSharpTypeMapperTests
{
    private static readonly SqlServerCSharpTypeMapper Mapper = new();

    [Theory(DisplayName = "SQL Server 型を SqlDbType 列挙名へ解決できる")]
    [InlineData("char(10)", "Char")]
    [InlineData("varchar(50)", "VarChar")]
    [InlineData("nchar(5)", "NChar")]
    [InlineData("nvarchar(100)", "NVarChar")]
    [InlineData("nvarchar(max)", "NVarChar")]
    [InlineData("text", "Text")]
    [InlineData("ntext", "NText")]
    [InlineData("xml", "Xml")]
    [InlineData("decimal(10,2)", "Decimal")]
    [InlineData("numeric(18,4)", "Decimal")]
    [InlineData("money", "Money")]
    [InlineData("smallmoney", "SmallMoney")]
    [InlineData("bit", "Bit")]
    [InlineData("tinyint", "TinyInt")]
    [InlineData("smallint", "SmallInt")]
    [InlineData("int", "Int")]
    [InlineData("bigint", "BigInt")]
    [InlineData("float", "Float")]
    [InlineData("real", "Real")]
    [InlineData("date", "Date")]
    [InlineData("time", "Time")]
    [InlineData("datetime", "DateTime")]
    [InlineData("datetime2", "DateTime2")]
    [InlineData("smalldatetime", "SmallDateTime")]
    [InlineData("datetimeoffset", "DateTimeOffset")]
    [InlineData("uniqueidentifier", "UniqueIdentifier")]
    [InlineData("binary(8)", "Binary")]
    [InlineData("varbinary(max)", "VarBinary")]
    [InlineData("image", "Image")]
    [InlineData("rowversion", "Timestamp")]
    [InlineData("timestamp", "Timestamp")]
    public void Map_ResolvesSqlDbTypeName(string dataType, string expected)
    {
        Mapper.Map(dataType).SqlDbTypeName.Should().Be(expected);
    }

    [Fact(DisplayName = "未知の型は SqlDbTypeName が null になる")]
    public void Map_UnknownType_SqlDbTypeNameIsNull()
    {
        Mapper.Map("geography").SqlDbTypeName.Should().BeNull();
    }

    [Theory(DisplayName = "宣言長は n / max=-1 / 無指定=0 の三値で解決される")]
    [InlineData("varchar(50)", 50)]
    [InlineData("nvarchar(max)", -1)]
    [InlineData("varbinary(max)", -1)]
    [InlineData("binary(16)", 16)]
    [InlineData("text", 0)]
    public void Map_ResolvesSqlDeclaredLength(string dataType, int expected)
    {
        Mapper.Map(dataType).SqlDeclaredLength.Should().Be(expected);
    }

    [Fact(DisplayName = "値型・非文字列は宣言長 0 になる")]
    public void Map_NonStringType_DeclaredLengthIsZero()
    {
        Mapper.Map("int").SqlDeclaredLength.Should().Be(0);
        Mapper.Map("decimal(10,2)").SqlDeclaredLength.Should().Be(0);
    }

    [Fact(DisplayName = "大文字小文字・前後空白を許容して SqlDbTypeName を解決できる")]
    public void Map_IsCaseInsensitiveAndAllowsWhitespace()
    {
        var info = Mapper.Map("  NVARCHAR(100) ");

        info.SqlDbTypeName.Should().Be("NVarChar");
        info.SqlDeclaredLength.Should().Be(100);
    }

    [Theory(
        DisplayName = "無制限バイナリ（varbinary(max) / image）だけ IsUnboundedBinary=true になる"
    )]
    [InlineData("varbinary(max)", true)]
    [InlineData("image", true)]
    [InlineData("varbinary(100)", false)]
    [InlineData("binary(16)", false)]
    [InlineData("rowversion", false)]
    [InlineData("timestamp", false)]
    public void Map_ResolvesUnboundedBinary(string dataType, bool expected)
    {
        Mapper.Map(dataType).IsUnboundedBinary.Should().Be(expected);
    }

    [Fact(DisplayName = "非バイナリ型は IsUnboundedBinary=false になる")]
    public void Map_NonBinaryType_IsUnboundedBinaryIsFalse()
    {
        Mapper.Map("nvarchar(max)").IsUnboundedBinary.Should().BeFalse();
        Mapper.Map("int").IsUnboundedBinary.Should().BeFalse();
    }
}
