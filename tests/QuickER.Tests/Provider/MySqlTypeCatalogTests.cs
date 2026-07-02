using FluentAssertions;
using QuickER.MySql;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="MySqlTypeCatalog"/> のネイティブ型 ⇔ 正規型変換を検証するテストクラス。
/// </summary>
public class MySqlTypeCatalogTests
{
    private static readonly MySqlTypeCatalog Catalog = new();

    /// <summary>DataTypes が MySqlDataTypes.All をそのまま公開することを検証する</summary>
    [Fact(DisplayName = "DataTypes は MySqlDataTypes.All を返す")]
    public void DataTypes_ReturnsMySqlDataTypesAll()
    {
        Catalog.DataTypes.Should().BeSameAs(MySqlDataTypes.All);
    }

    /// <summary>DefaultDataType が int であることを検証する</summary>
    [Fact(DisplayName = "DefaultDataType は int")]
    public void DefaultDataType_IsInt()
    {
        Catalog.DefaultDataType.Should().Be("int");
    }

    [Theory(DisplayName = "主要型を正規型へ解析できる")]
    [InlineData("smallint", CanonicalTypeKind.SmallInt)]
    [InlineData("int", CanonicalTypeKind.Int32)]
    [InlineData("bigint", CanonicalTypeKind.Int64)]
    [InlineData("float", CanonicalTypeKind.Float32)]
    [InlineData("double", CanonicalTypeKind.Float64)]
    [InlineData("date", CanonicalTypeKind.Date)]
    [InlineData("json", CanonicalTypeKind.Json)]
    public void TryParse_SimpleTypes_ResolvesExpectedKind(
        string nativeType,
        CanonicalTypeKind expected
    )
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Theory(DisplayName = "別名を正規化して解析できる")]
    [InlineData("integer", CanonicalTypeKind.Int32)]
    [InlineData("numeric", CanonicalTypeKind.Decimal)]
    [InlineData("dec", CanonicalTypeKind.Decimal)]
    [InlineData("double precision", CanonicalTypeKind.Float64)]
    [InlineData("real", CanonicalTypeKind.Float64)]
    [InlineData("bool", CanonicalTypeKind.Boolean)]
    [InlineData("boolean", CanonicalTypeKind.Boolean)]
    public void TryParse_Aliases_ResolvesExpectedKind(string nativeType, CanonicalTypeKind expected)
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Theory(DisplayName = "tinyint(1) / bool / boolean / bit(1) は Boolean として解析される")]
    [InlineData("tinyint(1)")]
    [InlineData("bool")]
    [InlineData("boolean")]
    [InlineData("bit(1)")]
    [InlineData("bit")]
    public void TryParse_BooleanFamily_ResolvesBoolean(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.Boolean);
    }

    [Theory(DisplayName = "tinyint unsigned / tinyint（(1) 以外）は TinyInt として解析される")]
    [InlineData("tinyint unsigned")]
    [InlineData("tinyint")]
    [InlineData("tinyint(4)")]
    public void TryParse_TinyInt_ResolvesTinyInt(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.TinyInt);
    }

    [Fact(DisplayName = "varchar(n) は String(n) として解析される")]
    public void TryParse_VarcharWithLength_ResolvesStringWithLength()
    {
        Catalog.TryParse("varchar(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(50);
    }

    [Theory(DisplayName = "text / mediumtext / longtext は String(-1) として解析される")]
    [InlineData("text")]
    [InlineData("mediumtext")]
    [InlineData("longtext")]
    public void TryParse_TextFamily_ResolvesStringMax(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "char(n) は FixedString(n) として解析される")]
    public void TryParse_CharWithLength_ResolvesFixedStringWithLength()
    {
        Catalog.TryParse("char(10)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.FixedString);
        canonical.Length.Should().Be(10);
    }

    [Fact(DisplayName = "varbinary(n) は Binary(n) として解析される")]
    public void TryParse_VarbinaryWithLength_ResolvesBinaryWithLength()
    {
        Catalog.TryParse("varbinary(255)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Binary);
        canonical.Length.Should().Be(255);
    }

    [Theory(DisplayName = "blob / mediumblob / longblob は Binary(-1) として解析される")]
    [InlineData("blob")]
    [InlineData("mediumblob")]
    [InlineData("longblob")]
    public void TryParse_BlobFamily_ResolvesBinaryMax(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Binary);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "binary(n) は FixedBinary(n) として解析される")]
    public void TryParse_BinaryWithLength_ResolvesFixedBinaryWithLength()
    {
        Catalog.TryParse("binary(16)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.FixedBinary);
        canonical.Length.Should().Be(16);
    }

    [Fact(DisplayName = "decimal(p,s) は Decimal(Precision,Scale) として解析される")]
    public void TryParse_DecimalWithPrecisionScale_ResolvesDecimal()
    {
        Catalog.TryParse("decimal(10,2)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(10);
        canonical.Scale.Should().Be(2);
    }

    [Fact(DisplayName = "datetime(p) は精度付き DateTime として解析される")]
    public void TryParse_DateTimeWithPrecision_ResolvesDateTimeWithPrecision()
    {
        Catalog.TryParse("datetime(3)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        canonical.Precision.Should().Be(3);
    }

    [Fact(DisplayName = "timestamp(p) は精度付き DateTimeOffset として解析される")]
    public void TryParse_TimestampWithPrecision_ResolvesDateTimeOffsetWithPrecision()
    {
        Catalog.TryParse("timestamp(6)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTimeOffset);
        canonical.Precision.Should().Be(6);
    }

    [Fact(DisplayName = "time(p) は精度付き Time として解析される")]
    public void TryParse_TimeWithPrecision_ResolvesTimeWithPrecision()
    {
        Catalog.TryParse("time(6)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Time);
        canonical.Precision.Should().Be(6);
    }

    [Fact(DisplayName = "大文字小文字・空白を許容して解析できる")]
    public void TryParse_IsCaseInsensitiveAndAllowsWhitespace()
    {
        var ok = Catalog.TryParse("  VARCHAR( 100 ) ", out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "int unsigned は末尾修飾子を無視して Int32 として解析される")]
    public void TryParse_IntUnsigned_ResolvesInt32()
    {
        Catalog.TryParse("int unsigned", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.Int32);
    }

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("enum('a','b')")]
    [InlineData("set('x','y')")]
    [InlineData("year")]
    [InlineData("bit(8)")]
    [InlineData("geometry")]
    [InlineData("point")]
    [InlineData("no_such_type")]
    [InlineData("varchar(-5)")] // 負数の型引数は例外ではなく false
    [InlineData("varchar(99999999999)")] // int 範囲外の型引数は例外ではなく false
    [InlineData("decimal(99999999999,2)")]
    public void TryParse_UnconvertibleTypes_ReturnsFalse(string nativeType)
    {
        Catalog.TryParse(nativeType, out _).Should().BeFalse();
    }

    [Theory(DisplayName = "TryFormat が主要な正規型からネイティブ型文字列を生成する")]
    [InlineData(CanonicalTypeKind.Boolean, "tinyint(1)")]
    [InlineData(CanonicalTypeKind.TinyInt, "tinyint unsigned")]
    [InlineData(CanonicalTypeKind.SmallInt, "smallint")]
    [InlineData(CanonicalTypeKind.Int32, "int")]
    [InlineData(CanonicalTypeKind.Int64, "bigint")]
    [InlineData(CanonicalTypeKind.Float32, "float")]
    [InlineData(CanonicalTypeKind.Float64, "double")]
    [InlineData(CanonicalTypeKind.Money, "decimal(19,4)")]
    [InlineData(CanonicalTypeKind.Date, "date")]
    [InlineData(CanonicalTypeKind.Guid, "char(36)")]
    [InlineData(CanonicalTypeKind.Xml, "longtext")]
    [InlineData(CanonicalTypeKind.Json, "json")]
    [InlineData(CanonicalTypeKind.DateTimeOffset, "timestamp")]
    public void TryFormat_SimpleTypes_ProducesExpectedNativeType(
        CanonicalTypeKind kind,
        string expected
    )
    {
        var ok = Catalog.TryFormat(new CanonicalType(kind), out var nativeType);

        ok.Should().BeTrue();
        nativeType.Should().Be(expected);
    }

    [Fact(DisplayName = "TryFormat(String(n)) は varchar(n) を生成する")]
    public void TryFormat_StringWithLength_ProducesVarchar()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.String, Length: 100), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("varchar(100)");
    }

    [Fact(DisplayName = "TryFormat(String(-1)) は longtext を生成する")]
    public void TryFormat_StringMax_ProducesLongtext()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.String, Length: -1), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("longtext");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(n)) は varchar(n) を生成する")]
    public void TryFormat_AnsiStringWithLength_ProducesVarchar()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.AnsiString, Length: 50), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("varchar(50)");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(-1)) は longtext を生成する")]
    public void TryFormat_AnsiStringMax_ProducesLongtext()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.AnsiString, Length: -1), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("longtext");
    }

    [Fact(DisplayName = "TryFormat(FixedString(n)) は char(n) を生成する")]
    public void TryFormat_FixedStringWithLength_ProducesChar()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.FixedString, Length: 10), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("char(10)");
    }

    [Fact(DisplayName = "TryFormat(AnsiFixedString(n)) は char(n) を生成する")]
    public void TryFormat_AnsiFixedStringWithLength_ProducesChar()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiFixedString, Length: 5),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("char(5)");
    }

    [Fact(DisplayName = "TryFormat(Binary(n)) は varbinary(n) を生成する")]
    public void TryFormat_BinaryWithLength_ProducesVarbinary()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Binary, Length: 255), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("varbinary(255)");
    }

    [Fact(DisplayName = "TryFormat(Binary(-1)) は longblob を生成する")]
    public void TryFormat_BinaryMax_ProducesLongblob()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Binary, Length: -1), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("longblob");
    }

    [Fact(DisplayName = "TryFormat(FixedBinary(n)) は binary(n) を生成する")]
    public void TryFormat_FixedBinaryWithLength_ProducesBinary()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.FixedBinary, Length: 16), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("binary(16)");
    }

    [Fact(DisplayName = "TryFormat(Decimal) は decimal(p,s) を生成する")]
    public void TryFormat_Decimal_ProducesDecimalWithPrecisionScale()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 2),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("decimal(10,2)");
    }

    [Fact(DisplayName = "TryFormat(DateTime, Precision指定あり) は datetime(p) を生成する")]
    public void TryFormat_DateTimeWithPrecision_ProducesDatetimeWithPrecision()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.DateTime, Precision: 3), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("datetime(3)");
    }

    [Theory(DisplayName = "パース → フォーマットのラウンドトリップで型が保持される")]
    [InlineData("tinyint(1)")]
    [InlineData("tinyint unsigned")]
    [InlineData("smallint")]
    [InlineData("int")]
    [InlineData("bigint")]
    [InlineData("decimal(10,2)")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("varchar(255)")]
    [InlineData("char(10)")]
    [InlineData("longtext")]
    [InlineData("varbinary(255)")]
    [InlineData("binary(16)")]
    [InlineData("longblob")]
    [InlineData("date")]
    [InlineData("time(6)")]
    [InlineData("datetime(3)")]
    [InlineData("timestamp(6)")]
    [InlineData("json")]
    public void ParseThenFormat_RoundTrips_ProducesSameNativeType(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();

        formatted.Should().Be(nativeType);
    }
}
