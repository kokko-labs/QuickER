using FluentAssertions;
using QuickER.Oracle;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="OracleTypeCatalog"/> のネイティブ型 ⇔ 正規型変換を検証するテストクラス。
/// NUMBER の精度によるスケール／整数型振り分けを重点的に確認する。
/// </summary>
public class OracleTypeCatalogTests
{
    private static readonly OracleTypeCatalog Catalog = new();

    /// <summary>DataTypes が OracleDataTypes.All をそのまま公開することを検証する</summary>
    [Fact(DisplayName = "DataTypes は OracleDataTypes.All を返す")]
    public void DataTypes_ReturnsOracleDataTypesAll()
    {
        Catalog.DataTypes.Should().BeSameAs(OracleDataTypes.All);
    }

    /// <summary>DefaultDataType が NUMBER(10) であることを検証する</summary>
    [Fact(DisplayName = "DefaultDataType は NUMBER(10)")]
    public void DefaultDataType_IsNumber10()
    {
        Catalog.DefaultDataType.Should().Be("NUMBER(10)");
    }

    // ---------------- NUMBER 精度の振り分け（肝） ----------------

    [Theory(DisplayName = "NUMBER(p) は精度により整数型へ振り分けられる")]
    [InlineData("NUMBER(1)", CanonicalTypeKind.Boolean)]
    [InlineData("NUMBER(3)", CanonicalTypeKind.TinyInt)]
    [InlineData("NUMBER(5)", CanonicalTypeKind.SmallInt)]
    [InlineData("NUMBER(10)", CanonicalTypeKind.Int32)]
    [InlineData("NUMBER(19)", CanonicalTypeKind.Int64)]
    public void TryParse_NumberByPrecision_ResolvesIntegerKind(
        string nativeType,
        CanonicalTypeKind expected
    )
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    [Fact(DisplayName = "NUMBER(p,s)（s>0）は Decimal(p,s) として解析される")]
    public void TryParse_NumberWithScale_ResolvesDecimal()
    {
        Catalog.TryParse("NUMBER(10,2)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(10);
        canonical.Scale.Should().Be(2);
    }

    [Fact(DisplayName = "NUMBER(p)（整数型に該当しない精度）は Decimal(p,0) として解析される")]
    public void TryParse_NumberOtherPrecision_ResolvesDecimalWithScaleZero()
    {
        Catalog.TryParse("NUMBER(7)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(7);
        canonical.Scale.Should().Be(0);
    }

    [Fact(DisplayName = "NUMBER（無引数）は精度・スケール null の Decimal として解析される")]
    public void TryParse_NumberNoArgs_ResolvesDecimalWithNulls()
    {
        Catalog.TryParse("NUMBER", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().BeNull();
        canonical.Scale.Should().BeNull();
    }

    // ---------------- 浮動小数点 ----------------

    [Theory(DisplayName = "浮動小数点型を正規型へ解析できる")]
    [InlineData("BINARY_FLOAT", CanonicalTypeKind.Float32)]
    [InlineData("BINARY_DOUBLE", CanonicalTypeKind.Float64)]
    [InlineData("FLOAT", CanonicalTypeKind.Float64)]
    [InlineData("FLOAT(63)", CanonicalTypeKind.Float64)]
    public void TryParse_FloatingTypes_ResolvesExpectedKind(
        string nativeType,
        CanonicalTypeKind expected
    )
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(expected);
    }

    // ---------------- 文字列・バイナリ ----------------

    [Theory(DisplayName = "文字列・LOB・バイナリ型を正規型へ解析できる")]
    [InlineData("NVARCHAR2(50)", CanonicalTypeKind.String, 50)]
    [InlineData("VARCHAR2(50)", CanonicalTypeKind.AnsiString, 50)]
    [InlineData("NCHAR(10)", CanonicalTypeKind.FixedString, 10)]
    [InlineData("CHAR(10)", CanonicalTypeKind.AnsiFixedString, 10)]
    [InlineData("RAW(16)", CanonicalTypeKind.Binary, 16)]
    public void TryParse_LengthTypes_ResolvesKindAndLength(
        string nativeType,
        CanonicalTypeKind expected,
        int length
    )
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(expected);
        canonical.Length.Should().Be(length);
    }

    [Theory(DisplayName = "LOB 型は max（-1）長として解析される")]
    [InlineData("NCLOB", CanonicalTypeKind.String)]
    [InlineData("CLOB", CanonicalTypeKind.AnsiString)]
    [InlineData("BLOB", CanonicalTypeKind.Binary)]
    public void TryParse_LobTypes_ResolvesMaxLength(string nativeType, CanonicalTypeKind expected)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(expected);
        canonical.Length.Should().Be(-1);
    }

    // ---------------- 日付・時刻 ----------------

    [Fact(DisplayName = "DATE は（時刻を含むため）DateTime として解析される")]
    public void TryParse_Date_ResolvesDateTime()
    {
        Catalog.TryParse("DATE", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
    }

    [Fact(DisplayName = "TIMESTAMP(p) は精度付き DateTime として解析される")]
    public void TryParse_TimestampWithPrecision_ResolvesDateTime()
    {
        Catalog.TryParse("TIMESTAMP(6)", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        canonical.Precision.Should().Be(6);
    }

    [Fact(DisplayName = "TIMESTAMP(p) WITH TIME ZONE は精度付き DateTimeOffset として解析される")]
    public void TryParse_TimestampWithTimeZone_ResolvesDateTimeOffset()
    {
        Catalog.TryParse("TIMESTAMP(6) WITH TIME ZONE", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.DateTimeOffset);
        canonical.Precision.Should().Be(6);
    }

    [Fact(
        DisplayName = "TIMESTAMP WITH LOCAL TIME ZONE は DateTimeOffset として解析される（解釈のみ）"
    )]
    public void TryParse_TimestampWithLocalTimeZone_ResolvesDateTimeOffset()
    {
        Catalog.TryParse("TIMESTAMP(6) WITH LOCAL TIME ZONE", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.DateTimeOffset);
    }

    // ---------------- XML ----------------

    [Fact(DisplayName = "XMLTYPE は Xml として解析される")]
    public void TryParse_XmlType_ResolvesXml()
    {
        Catalog.TryParse("XMLTYPE", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.Xml);
    }

    // ---------------- 大文字小文字・空白 ----------------

    [Fact(DisplayName = "大文字小文字・空白を許容して解析できる")]
    public void TryParse_IsCaseInsensitiveAndAllowsWhitespace()
    {
        Catalog.TryParse("  nvarchar2( 100 ) ", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    // ---------------- 変換不能 ----------------

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("LONG")]
    [InlineData("LONG RAW")]
    [InlineData("ROWID")]
    [InlineData("UROWID")]
    [InlineData("BFILE")]
    [InlineData("INTERVAL YEAR TO MONTH")]
    [InlineData("INTERVAL DAY TO SECOND")]
    [InlineData("SDO_GEOMETRY")]
    [InlineData("no_such_type")]
    [InlineData("NUMBER(-5)")] // 負数の型引数は例外ではなく false
    [InlineData("NUMBER(99999999999)")] // int 範囲外の型引数は例外ではなく false
    [InlineData("VARCHAR2(-1)")]
    public void TryParse_UnconvertibleTypes_ReturnsFalse(string nativeType)
    {
        Catalog.TryParse(nativeType, out _).Should().BeFalse();
    }

    // ---------------- TryFormat 整数 ----------------

    [Theory(DisplayName = "整数系の正規型は精度付き NUMBER を生成する")]
    [InlineData(CanonicalTypeKind.Boolean, "NUMBER(1)")]
    [InlineData(CanonicalTypeKind.TinyInt, "NUMBER(3)")]
    [InlineData(CanonicalTypeKind.SmallInt, "NUMBER(5)")]
    [InlineData(CanonicalTypeKind.Int32, "NUMBER(10)")]
    [InlineData(CanonicalTypeKind.Int64, "NUMBER(19)")]
    public void TryFormat_IntegerKinds_ProducesNumber(CanonicalTypeKind kind, string expected)
    {
        Catalog.TryFormat(new CanonicalType(kind), out var nativeType).Should().BeTrue();
        nativeType.Should().Be(expected);
    }

    [Fact(DisplayName = "TryFormat(Decimal(p,s)) は NUMBER(p,s) を生成する")]
    public void TryFormat_Decimal_ProducesNumber()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 12, Scale: 4),
                out var nativeType
            )
            .Should()
            .BeTrue();
        nativeType.Should().Be("NUMBER(12,4)");
    }

    [Fact(DisplayName = "TryFormat(Money) は NUMBER(19,4) を生成する")]
    public void TryFormat_Money_ProducesNumber19_4()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Money), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be("NUMBER(19,4)");
    }

    [Theory(DisplayName = "浮動小数点の正規型は BINARY_FLOAT / BINARY_DOUBLE を生成する")]
    [InlineData(CanonicalTypeKind.Float32, "BINARY_FLOAT")]
    [InlineData(CanonicalTypeKind.Float64, "BINARY_DOUBLE")]
    public void TryFormat_Floating_ProducesBinaryTypes(CanonicalTypeKind kind, string expected)
    {
        Catalog.TryFormat(new CanonicalType(kind), out var nativeType).Should().BeTrue();
        nativeType.Should().Be(expected);
    }

    // ---------------- TryFormat 文字列・バイナリ ----------------

    [Theory(DisplayName = "文字列系の正規型は Oracle の文字列型を生成する")]
    [InlineData(CanonicalTypeKind.String, 50, "NVARCHAR2(50)")]
    [InlineData(CanonicalTypeKind.String, -1, "NCLOB")]
    [InlineData(CanonicalTypeKind.AnsiString, 50, "VARCHAR2(50)")]
    [InlineData(CanonicalTypeKind.AnsiString, -1, "CLOB")]
    [InlineData(CanonicalTypeKind.FixedString, 10, "NCHAR(10)")]
    [InlineData(CanonicalTypeKind.AnsiFixedString, 10, "CHAR(10)")]
    public void TryFormat_StringKinds_ProducesExpected(
        CanonicalTypeKind kind,
        int length,
        string expected
    )
    {
        Catalog
            .TryFormat(new CanonicalType(kind, Length: length), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be(expected);
    }

    [Theory(DisplayName = "バイナリの正規型は RAW(n) / BLOB を生成する")]
    [InlineData(CanonicalTypeKind.Binary, 16, "RAW(16)")]
    [InlineData(CanonicalTypeKind.Binary, -1, "BLOB")]
    [InlineData(CanonicalTypeKind.FixedBinary, 16, "RAW(16)")]
    public void TryFormat_BinaryKinds_ProducesRawOrBlob(
        CanonicalTypeKind kind,
        int length,
        string expected
    )
    {
        Catalog
            .TryFormat(new CanonicalType(kind, Length: length), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be(expected);
    }

    // ---------------- TryFormat 日付・時刻（非対称） ----------------

    [Fact(DisplayName = "TryFormat(Date) は DATE を生成する（DATE の非対称変換）")]
    public void TryFormat_Date_ProducesDate()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Date), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be("DATE");
    }

    [Fact(DisplayName = "TryFormat(DateTime, 精度あり) は TIMESTAMP(p) を生成する")]
    public void TryFormat_DateTimeWithPrecision_ProducesTimestamp()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.DateTime, Precision: 6),
                out var nativeType
            )
            .Should()
            .BeTrue();
        nativeType.Should().Be("TIMESTAMP(6)");
    }

    [Fact(
        DisplayName = "TryFormat(DateTimeOffset, 精度あり) は TIMESTAMP(p) WITH TIME ZONE を生成する"
    )]
    public void TryFormat_DateTimeOffset_ProducesTimestampWithTimeZone()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.DateTimeOffset, Precision: 6),
                out var nativeType
            )
            .Should()
            .BeTrue();
        nativeType.Should().Be("TIMESTAMP(6) WITH TIME ZONE");
    }

    [Fact(DisplayName = "TryFormat(Time) は false（Oracle に TIME 型が無い）")]
    public void TryFormat_Time_ReturnsFalse()
    {
        Catalog.TryFormat(new CanonicalType(CanonicalTypeKind.Time), out _).Should().BeFalse();
    }

    // ---------------- TryFormat その他（format-only） ----------------

    [Fact(DisplayName = "TryFormat(Guid) は RAW(16) を生成する（format-only）")]
    public void TryFormat_Guid_ProducesRaw16()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Guid), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be("RAW(16)");
    }

    [Fact(DisplayName = "TryFormat(Xml) は XMLTYPE を生成する")]
    public void TryFormat_Xml_ProducesXmlType()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Xml), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be("XMLTYPE");
    }

    [Fact(DisplayName = "TryFormat(Json) は CLOB を生成する（19c に JSON 型が無い・format-only）")]
    public void TryFormat_Json_ProducesClob()
    {
        Catalog
            .TryFormat(new CanonicalType(CanonicalTypeKind.Json), out var nativeType)
            .Should()
            .BeTrue();
        nativeType.Should().Be("CLOB");
    }

    // ---------------- ラウンドトリップ ----------------

    [Theory(DisplayName = "パース → フォーマットのラウンドトリップで型が保持される")]
    [InlineData("NUMBER(1)")]
    [InlineData("NUMBER(3)")]
    [InlineData("NUMBER(5)")]
    [InlineData("NUMBER(10)")]
    [InlineData("NUMBER(19)")]
    [InlineData("NUMBER(10,2)")]
    [InlineData("BINARY_FLOAT")]
    [InlineData("BINARY_DOUBLE")]
    [InlineData("NVARCHAR2(50)")]
    [InlineData("VARCHAR2(50)")]
    [InlineData("NCHAR(10)")]
    [InlineData("CHAR(10)")]
    [InlineData("NCLOB")]
    [InlineData("CLOB")]
    [InlineData("RAW(16)")]
    [InlineData("BLOB")]
    [InlineData("TIMESTAMP(6)")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE")]
    [InlineData("XMLTYPE")]
    public void ParseThenFormat_RoundTrips_ProducesSameNativeType(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();
        formatted.Should().Be(nativeType);
    }

    [Fact(DisplayName = "DATE のラウンドトリップは非対称（DATE→DateTime→TIMESTAMP）")]
    public void DateRoundTrip_IsAsymmetric()
    {
        Catalog.TryParse("DATE", out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();
        // DATE は時刻を含むため DateTime へ寄せ、書き戻しは TIMESTAMP になる
        formatted.Should().Be("TIMESTAMP");
    }
}
