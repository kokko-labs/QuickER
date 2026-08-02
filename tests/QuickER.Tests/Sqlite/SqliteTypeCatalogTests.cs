using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Sqlite;

namespace QuickER.Tests.Sqlite;

/// <summary>
/// <see cref="SqliteTypeCatalog"/> のネイティブ（宣言型）⇔ 正規型変換を検証するテストクラス。
/// </summary>
public class SqliteTypeCatalogTests
{
    private static readonly SqliteTypeCatalog Catalog = new();

    /// <summary>DataTypes が SqliteDataTypes.All をそのまま公開することを検証する</summary>
    [Fact(DisplayName = "DataTypes は SqliteDataTypes.All を返す")]
    public void DataTypes_ReturnsSqliteDataTypesAll()
    {
        Catalog.DataTypes.Should().BeSameAs(SqliteDataTypes.All);
    }

    /// <summary>DefaultDataType が INT であることを検証する</summary>
    [Fact(DisplayName = "DefaultDataType は INT")]
    public void DefaultDataType_IsInt()
    {
        Catalog.DefaultDataType.Should().Be("INT");
    }

    [Theory(DisplayName = "主要型を正規型へ解析できる")]
    [InlineData("BIT", CanonicalTypeKind.Boolean)]
    [InlineData("TINYINT", CanonicalTypeKind.TinyInt)]
    [InlineData("SMALLINT", CanonicalTypeKind.SmallInt)]
    [InlineData("INT", CanonicalTypeKind.Int32)]
    [InlineData("BIGINT", CanonicalTypeKind.Int64)]
    [InlineData("REAL", CanonicalTypeKind.Float32)]
    [InlineData("FLOAT", CanonicalTypeKind.Float64)]
    [InlineData("MONEY", CanonicalTypeKind.Money)]
    [InlineData("DATE", CanonicalTypeKind.Date)]
    [InlineData("UNIQUEIDENTIFIER", CanonicalTypeKind.Guid)]
    [InlineData("XML", CanonicalTypeKind.Xml)]
    [InlineData("JSON", CanonicalTypeKind.Json)]
    public void TryParse_SimpleTypes_ResolvesExpectedKind(
        string nativeType,
        CanonicalTypeKind expected
    )
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Theory(DisplayName = "別名・SQLite 親和性キーワードを正規化して解析できる")]
    // SQLite の INTEGER は最大 8 バイト格納のため Int64 として扱う
    [InlineData("INTEGER", CanonicalTypeKind.Int64)]
    [InlineData("BOOLEAN", CanonicalTypeKind.Boolean)]
    [InlineData("NUMERIC", CanonicalTypeKind.Decimal)]
    [InlineData("DOUBLE", CanonicalTypeKind.Float64)]
    [InlineData("TEXT", CanonicalTypeKind.String)]
    [InlineData("BLOB", CanonicalTypeKind.Binary)]
    [InlineData("DATETIME", CanonicalTypeKind.DateTime)]
    [InlineData("TIMESTAMP", CanonicalTypeKind.DateTime)]
    public void TryParse_Aliases_ResolvesExpectedKind(string nativeType, CanonicalTypeKind expected)
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Fact(DisplayName = "大文字小文字・空白を許容して解析できる")]
    public void TryParse_IsCaseInsensitiveAndAllowsWhitespace()
    {
        var ok = Catalog.TryParse("  nvarchar( 100 ) ", out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "NVARCHAR(n) は String(n) として解析される")]
    public void TryParse_NVarcharWithLength_ResolvesStringWithLength()
    {
        Catalog.TryParse("NVARCHAR(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(50);
    }

    [Fact(DisplayName = "NVARCHAR(MAX) は String(-1) として解析される")]
    public void TryParse_NVarcharMax_ResolvesStringMax()
    {
        Catalog.TryParse("NVARCHAR(MAX)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "VARCHAR(n) は AnsiString(n) として解析される")]
    public void TryParse_VarcharWithLength_ResolvesAnsiStringWithLength()
    {
        Catalog.TryParse("VARCHAR(100)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.AnsiString);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "NCHAR(n) は FixedString(n) として解析される")]
    public void TryParse_NCharWithLength_ResolvesFixedStringWithLength()
    {
        Catalog.TryParse("NCHAR(10)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.FixedString);
        canonical.Length.Should().Be(10);
    }

    [Fact(DisplayName = "CHAR(n) は AnsiFixedString(n) として解析される")]
    public void TryParse_CharWithLength_ResolvesAnsiFixedStringWithLength()
    {
        Catalog.TryParse("CHAR(5)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.AnsiFixedString);
        canonical.Length.Should().Be(5);
    }

    [Fact(DisplayName = "VARBINARY(n) は Binary(n) として解析される")]
    public void TryParse_VarbinaryWithLength_ResolvesBinary()
    {
        Catalog.TryParse("VARBINARY(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Binary);
        canonical.Length.Should().Be(50);
    }

    [Fact(DisplayName = "BINARY(n) は FixedBinary(n) として解析される")]
    public void TryParse_BinaryWithLength_ResolvesFixedBinary()
    {
        Catalog.TryParse("BINARY(16)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.FixedBinary);
        canonical.Length.Should().Be(16);
    }

    [Fact(DisplayName = "DECIMAL(p,s) は Decimal(Precision,Scale) として解析される")]
    public void TryParse_DecimalWithPrecisionScale_ResolvesDecimal()
    {
        Catalog.TryParse("DECIMAL(18,2)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(18);
        canonical.Scale.Should().Be(2);
    }

    [Fact(DisplayName = "DATETIME2(p) は精度付き DateTime として解析される")]
    public void TryParse_DateTime2WithPrecision_ResolvesDateTimeWithPrecision()
    {
        Catalog.TryParse("DATETIME2(3)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        canonical.Precision.Should().Be(3);
    }

    [Fact(DisplayName = "TIME(p) は精度付き Time として解析される")]
    public void TryParse_TimeWithPrecision_ResolvesTimeWithPrecision()
    {
        Catalog.TryParse("TIME(6)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Time);
        canonical.Precision.Should().Be(6);
    }

    [Fact(DisplayName = "DATETIMEOFFSET(p) は精度付き DateTimeOffset として解析される")]
    public void TryParse_DateTimeOffsetWithPrecision_ResolvesDateTimeOffset()
    {
        Catalog.TryParse("DATETIMEOFFSET(7)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTimeOffset);
        canonical.Precision.Should().Be(7);
    }

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("no_such_type")]
    [InlineData("NVARCHAR(-5)")] // 負数の型引数は例外ではなく false
    [InlineData("NVARCHAR(99999999999)")] // int 範囲外の型引数は例外ではなく false
    [InlineData("DECIMAL(99999999999,2)")]
    [InlineData("INT[]")] // 配列表記は SQLite に無く弾かれる
    public void TryParse_UnconvertibleTypes_ReturnsFalse(string nativeType)
    {
        Catalog.TryParse(nativeType, out _).Should().BeFalse();
    }

    [Theory(DisplayName = "TryFormat が主要な正規型からネイティブ型文字列を生成する")]
    [InlineData(CanonicalTypeKind.Boolean, "BIT")]
    [InlineData(CanonicalTypeKind.TinyInt, "TINYINT")]
    [InlineData(CanonicalTypeKind.SmallInt, "SMALLINT")]
    [InlineData(CanonicalTypeKind.Int32, "INT")]
    [InlineData(CanonicalTypeKind.Int64, "BIGINT")]
    [InlineData(CanonicalTypeKind.Float32, "REAL")]
    [InlineData(CanonicalTypeKind.Float64, "FLOAT")]
    [InlineData(CanonicalTypeKind.Money, "MONEY")]
    [InlineData(CanonicalTypeKind.Date, "DATE")]
    [InlineData(CanonicalTypeKind.Guid, "UNIQUEIDENTIFIER")]
    [InlineData(CanonicalTypeKind.Xml, "XML")]
    [InlineData(CanonicalTypeKind.Json, "JSON")]
    [InlineData(CanonicalTypeKind.DateTimeOffset, "DATETIMEOFFSET")]
    public void TryFormat_SimpleTypes_ProducesExpectedNativeType(
        CanonicalTypeKind kind,
        string expected
    )
    {
        var ok = Catalog.TryFormat(new CanonicalType(kind), out var nativeType);

        ok.Should().BeTrue();
        nativeType.Should().Be(expected);
    }

    [Fact(DisplayName = "TryFormat(String(n)) は NVARCHAR(n) を生成する")]
    public void TryFormat_StringWithLength_ProducesNVarchar()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.String, Length: 100), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("NVARCHAR(100)");
    }

    [Fact(DisplayName = "TryFormat(String(-1)) は NVARCHAR(MAX) を生成する")]
    public void TryFormat_StringMax_ProducesNVarcharMax()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.String, Length: -1), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("NVARCHAR(MAX)");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(n)) は VARCHAR(n) を生成する")]
    public void TryFormat_AnsiStringWithLength_ProducesVarchar()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiString, Length: 50),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("VARCHAR(50)");
    }

    [Fact(DisplayName = "TryFormat(Binary(n)) は VARBINARY(n) を生成する")]
    public void TryFormat_BinaryWithLength_ProducesVarbinary()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Binary, Length: 50), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("VARBINARY(50)");
    }

    [Fact(DisplayName = "TryFormat(FixedBinary(n)) は BINARY(n) を生成する")]
    public void TryFormat_FixedBinaryWithLength_ProducesBinary()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.FixedBinary, Length: 16),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("BINARY(16)");
    }

    [Fact(DisplayName = "TryFormat(Decimal) は DECIMAL(p,s) を生成する")]
    public void TryFormat_Decimal_ProducesDecimalWithPrecisionScale()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 18, Scale: 2),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("DECIMAL(18,2)");
    }

    [Fact(DisplayName = "TryFormat(DateTime, Precision指定あり) は DATETIME2(p) を生成する")]
    public void TryFormat_DateTimeWithPrecision_ProducesDateTime2WithPrecision()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.DateTime, Precision: 3),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("DATETIME2(3)");
    }

    [Theory(
        DisplayName = "パース → フォーマットのラウンドトリップで全 CanonicalTypeKind の代表宣言型が保持される"
    )]
    [InlineData("BIT")]
    [InlineData("TINYINT")]
    [InlineData("SMALLINT")]
    [InlineData("INT")]
    [InlineData("BIGINT")]
    [InlineData("DECIMAL(18,2)")]
    [InlineData("REAL")]
    [InlineData("FLOAT")]
    [InlineData("MONEY")]
    [InlineData("NVARCHAR(100)")]
    [InlineData("NVARCHAR(MAX)")]
    [InlineData("VARCHAR(50)")]
    [InlineData("NCHAR(10)")]
    [InlineData("CHAR(5)")]
    [InlineData("VARBINARY(50)")]
    [InlineData("BINARY(16)")]
    [InlineData("DATE")]
    [InlineData("TIME(6)")]
    [InlineData("DATETIME2(3)")]
    [InlineData("DATETIMEOFFSET(7)")]
    [InlineData("UNIQUEIDENTIFIER")]
    [InlineData("XML")]
    [InlineData("JSON")]
    public void ParseThenFormat_RoundTrips_ProducesSameNativeType(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();

        formatted.Should().Be(nativeType);
    }
}
