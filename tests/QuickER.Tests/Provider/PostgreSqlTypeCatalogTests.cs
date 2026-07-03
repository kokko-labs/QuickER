using FluentAssertions;
using QuickER.PostgreSql;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="PostgreSqlTypeCatalog"/> のネイティブ型 ⇔ 正規型変換を検証するテストクラス。
/// </summary>
public class PostgreSqlTypeCatalogTests
{
    private static readonly PostgreSqlTypeCatalog Catalog = new();

    /// <summary>DataTypes が PostgreSqlDataTypes.All をそのまま公開することを検証する</summary>
    [Fact(DisplayName = "DataTypes は PostgreSqlDataTypes.All を返す")]
    public void DataTypes_ReturnsPostgreSqlDataTypesAll()
    {
        Catalog.DataTypes.Should().BeSameAs(PostgreSqlDataTypes.All);
    }

    /// <summary>DefaultDataType が integer であることを検証する</summary>
    [Fact(DisplayName = "DefaultDataType は integer")]
    public void DefaultDataType_IsInteger()
    {
        Catalog.DefaultDataType.Should().Be("integer");
    }

    [Theory(DisplayName = "主要型を正規型へ解析できる")]
    [InlineData("boolean", CanonicalTypeKind.Boolean)]
    [InlineData("smallint", CanonicalTypeKind.SmallInt)]
    [InlineData("integer", CanonicalTypeKind.Int32)]
    [InlineData("bigint", CanonicalTypeKind.Int64)]
    [InlineData("real", CanonicalTypeKind.Float32)]
    [InlineData("double precision", CanonicalTypeKind.Float64)]
    [InlineData("money", CanonicalTypeKind.Money)]
    [InlineData("date", CanonicalTypeKind.Date)]
    [InlineData("uuid", CanonicalTypeKind.Guid)]
    [InlineData("xml", CanonicalTypeKind.Xml)]
    [InlineData("bytea", CanonicalTypeKind.Binary)]
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
    [InlineData("character varying", CanonicalTypeKind.String)]
    [InlineData("character", CanonicalTypeKind.FixedString)]
    [InlineData("bpchar", CanonicalTypeKind.FixedString)]
    [InlineData("int", CanonicalTypeKind.Int32)]
    [InlineData("int4", CanonicalTypeKind.Int32)]
    [InlineData("int2", CanonicalTypeKind.SmallInt)]
    [InlineData("int8", CanonicalTypeKind.Int64)]
    [InlineData("bool", CanonicalTypeKind.Boolean)]
    [InlineData("float4", CanonicalTypeKind.Float32)]
    [InlineData("float8", CanonicalTypeKind.Float64)]
    [InlineData("decimal", CanonicalTypeKind.Decimal)]
    public void TryParse_Aliases_ResolvesExpectedKind(string nativeType, CanonicalTypeKind expected)
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Theory(DisplayName = "複数語の日付時刻型名を正規化して解析できる")]
    [InlineData("timestamp without time zone", CanonicalTypeKind.DateTime)]
    [InlineData("timestamp with time zone", CanonicalTypeKind.DateTimeOffset)]
    [InlineData("timestamptz", CanonicalTypeKind.DateTimeOffset)]
    [InlineData("time without time zone", CanonicalTypeKind.Time)]
    [InlineData("time with time zone", CanonicalTypeKind.Time)]
    [InlineData("timetz", CanonicalTypeKind.Time)]
    public void TryParse_MultiWordDateTimeTypes_ResolvesExpectedKind(
        string nativeType,
        CanonicalTypeKind expected
    )
    {
        var ok = Catalog.TryParse(nativeType, out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Fact(DisplayName = "大文字小文字・空白を許容して解析できる")]
    public void TryParse_IsCaseInsensitiveAndAllowsWhitespace()
    {
        var ok = Catalog.TryParse("  VARCHAR( 100 ) ", out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "複数語型名の空白ゆれ（DOUBLE  PRECISION）を許容する")]
    public void TryParse_DoublePrecision_AllowsExtraWhitespace()
    {
        var ok = Catalog.TryParse("DOUBLE   PRECISION", out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.Float64);
    }

    [Fact(DisplayName = "varchar(n) は String(n) として解析される")]
    public void TryParse_VarcharWithLength_ResolvesStringWithLength()
    {
        Catalog.TryParse("varchar(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(50);
    }

    [Fact(DisplayName = "text は String(-1) として解析される")]
    public void TryParse_Text_ResolvesStringMax()
    {
        Catalog.TryParse("text", out var canonical).Should().BeTrue();

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

    [Fact(DisplayName = "numeric(p,s) は Decimal(Precision,Scale) として解析される")]
    public void TryParse_NumericWithPrecisionScale_ResolvesDecimal()
    {
        Catalog.TryParse("numeric(10,2)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(10);
        canonical.Scale.Should().Be(2);
    }

    [Fact(DisplayName = "numeric（引数省略）は Precision/Scale が null で解析される")]
    public void TryParse_NumericWithoutArgs_ResolvesDecimalWithNullPrecisionScale()
    {
        Catalog.TryParse("numeric", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().BeNull();
        canonical.Scale.Should().BeNull();
    }

    [Fact(DisplayName = "timestamp(p) は精度付き DateTime として解析される")]
    public void TryParse_TimestampWithPrecision_ResolvesDateTimeWithPrecision()
    {
        Catalog.TryParse("timestamp(3)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        canonical.Precision.Should().Be(3);
    }

    [Fact(DisplayName = "timestamptz(p) は精度付き DateTimeOffset として解析される")]
    public void TryParse_TimestamptzWithPrecision_ResolvesDateTimeOffsetWithPrecision()
    {
        Catalog.TryParse("timestamptz(6)", out var canonical).Should().BeTrue();

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

    [Fact(DisplayName = "json は parse-only で Json として解析される")]
    public void TryParse_Json_ResolvesJson()
    {
        Catalog.TryParse("json", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Json);
    }

    [Fact(DisplayName = "jsonb は Json として解析される")]
    public void TryParse_Jsonb_ResolvesJson()
    {
        Catalog.TryParse("jsonb", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Json);
    }

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("serial")]
    [InlineData("bigserial")]
    [InlineData("smallserial")]
    [InlineData("integer[]")]
    [InlineData("text[]")]
    [InlineData("inet")]
    [InlineData("cidr")]
    [InlineData("macaddr")]
    [InlineData("interval")]
    [InlineData("point")]
    [InlineData("tsvector")]
    [InlineData("no_such_type")]
    [InlineData("varchar(-5)")] // 負数の型引数は例外ではなく false
    [InlineData("varchar(99999999999)")] // int 範囲外の型引数は例外ではなく false
    [InlineData("numeric(99999999999,2)")]
    public void TryParse_UnconvertibleTypes_ReturnsFalse(string nativeType)
    {
        Catalog.TryParse(nativeType, out _).Should().BeFalse();
    }

    [Theory(DisplayName = "TryFormat が主要な正規型からネイティブ型文字列を生成する")]
    [InlineData(CanonicalTypeKind.Boolean, "boolean")]
    [InlineData(CanonicalTypeKind.SmallInt, "smallint")]
    [InlineData(CanonicalTypeKind.Int32, "integer")]
    [InlineData(CanonicalTypeKind.Int64, "bigint")]
    [InlineData(CanonicalTypeKind.Float32, "real")]
    [InlineData(CanonicalTypeKind.Float64, "double precision")]
    [InlineData(CanonicalTypeKind.Money, "money")]
    [InlineData(CanonicalTypeKind.Date, "date")]
    [InlineData(CanonicalTypeKind.Guid, "uuid")]
    [InlineData(CanonicalTypeKind.Xml, "xml")]
    public void TryFormat_SimpleTypes_ProducesExpectedNativeType(
        CanonicalTypeKind kind,
        string expected
    )
    {
        var ok = Catalog.TryFormat(new CanonicalType(kind), out var nativeType);

        ok.Should().BeTrue();
        nativeType.Should().Be(expected);
    }

    [Fact(DisplayName = "TryFormat(TinyInt) は smallint を生成する（PG に tinyint は無い）")]
    public void TryFormat_TinyInt_ProducesSmallint()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.TinyInt), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("smallint");
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

    [Fact(DisplayName = "TryFormat(String(-1)) は text を生成する")]
    public void TryFormat_StringMax_ProducesText()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.String, Length: -1), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("text");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(n)) は varchar(n) を生成する")]
    public void TryFormat_AnsiStringWithLength_ProducesVarchar()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiString, Length: 50),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("varchar(50)");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(-1)) は text を生成する")]
    public void TryFormat_AnsiStringMax_ProducesText()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiString, Length: -1),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("text");
    }

    [Fact(DisplayName = "TryFormat(FixedString(n)) は char(n) を生成する")]
    public void TryFormat_FixedStringWithLength_ProducesChar()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.FixedString, Length: 10),
                out var nativeType
            )
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

    [Theory(DisplayName = "TryFormat の Binary / FixedBinary は長さを問わず bytea を生成する")]
    [InlineData(CanonicalTypeKind.Binary, 50)]
    [InlineData(CanonicalTypeKind.Binary, -1)]
    [InlineData(CanonicalTypeKind.FixedBinary, 16)]
    public void TryFormat_Binary_ProducesBytea(CanonicalTypeKind kind, int length)
    {
        Catalog
            .TryFormat(new CanonicalType(kind, Length: length), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("bytea");
    }

    [Fact(DisplayName = "TryFormat(Decimal) は numeric(p,s) を生成する")]
    public void TryFormat_Decimal_ProducesNumericWithPrecisionScale()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 2),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("numeric(10,2)");
    }

    [Fact(DisplayName = "TryFormat(DateTime, Precision指定あり) は timestamp(p) を生成する")]
    public void TryFormat_DateTimeWithPrecision_ProducesTimestampWithPrecision()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.DateTime, Precision: 3),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("timestamp(3)");
    }

    [Fact(DisplayName = "TryFormat(DateTimeOffset) は timestamptz を生成する")]
    public void TryFormat_DateTimeOffset_ProducesTimestamptz()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.DateTimeOffset), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("timestamptz");
    }

    [Fact(DisplayName = "TryFormat(Json) は jsonb を生成する")]
    public void TryFormat_Json_ProducesJsonb()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Json), out var nativeType)
            .Should()
            .BeTrue();

        nativeType.Should().Be("jsonb");
    }

    [Theory(DisplayName = "パース → フォーマットのラウンドトリップで型が保持される")]
    [InlineData("boolean")]
    [InlineData("smallint")]
    [InlineData("integer")]
    [InlineData("bigint")]
    [InlineData("numeric(10,2)")]
    [InlineData("real")]
    [InlineData("double precision")]
    [InlineData("money")]
    [InlineData("varchar(100)")]
    [InlineData("text")]
    [InlineData("char(10)")]
    [InlineData("bytea")]
    [InlineData("date")]
    [InlineData("time(6)")]
    [InlineData("timestamp(3)")]
    [InlineData("timestamptz(6)")]
    [InlineData("uuid")]
    [InlineData("xml")]
    [InlineData("jsonb")]
    public void ParseThenFormat_RoundTrips_ProducesSameNativeType(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();

        formatted.Should().Be(nativeType);
    }
}
