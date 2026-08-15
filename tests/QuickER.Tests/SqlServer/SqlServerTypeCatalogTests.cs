using AwesomeAssertions;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

/// <summary>
/// <see cref="SqlServerTypeCatalog"/> のネイティブ型 ⇔ 正規型変換を検証するテストクラス。
/// </summary>
public class SqlServerTypeCatalogTests
{
    private static readonly SqlServerTypeCatalog Catalog = new();

    /// <summary>DataTypes が SqlServerDataTypes.All をそのまま公開することを検証する</summary>
    [Fact(DisplayName = "DataTypes は SqlServerDataTypes.All を返す")]
    public void DataTypes_ReturnsSqlServerDataTypesAll()
    {
        Catalog.DataTypes.Should().BeSameAs(SqlServerDataTypes.All);
    }

    [Theory(DisplayName = "主要型を正規型へ解析できる")]
    [InlineData("bit", CanonicalTypeKind.Boolean)]
    [InlineData("tinyint", CanonicalTypeKind.TinyInt)]
    [InlineData("smallint", CanonicalTypeKind.SmallInt)]
    [InlineData("int", CanonicalTypeKind.Int32)]
    [InlineData("bigint", CanonicalTypeKind.Int64)]
    [InlineData("real", CanonicalTypeKind.Float32)]
    [InlineData("float", CanonicalTypeKind.Float64)]
    [InlineData("money", CanonicalTypeKind.Money)]
    [InlineData("date", CanonicalTypeKind.Date)]
    [InlineData("uniqueidentifier", CanonicalTypeKind.Guid)]
    [InlineData("xml", CanonicalTypeKind.Xml)]
    public void TryParse_SimpleTypes_ResolvesExpectedKind(
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
        var ok = Catalog.TryParse("  NVARCHAR( 100 ) ", out var canonical);

        ok.Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "nvarchar(n) は String(n) として解析される")]
    public void TryParse_NVarcharWithLength_ResolvesStringWithLength()
    {
        Catalog.TryParse("nvarchar(100)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(100);
    }

    [Fact(DisplayName = "nvarchar(max) は String(-1) として解析される")]
    public void TryParse_NVarcharMax_ResolvesStringWithMaxLength()
    {
        Catalog.TryParse("nvarchar(max)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "varchar(n) は AnsiString(n) として解析される")]
    public void TryParse_VarcharWithLength_ResolvesAnsiStringWithLength()
    {
        Catalog.TryParse("varchar(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.AnsiString);
        canonical.Length.Should().Be(50);
    }

    [Fact(DisplayName = "decimal(p,s) は Decimal(Precision,Scale) として解析される")]
    public void TryParse_DecimalWithPrecisionScale_ResolvesDecimal()
    {
        Catalog.TryParse("decimal(10,2)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(10);
        canonical.Scale.Should().Be(2);
    }

    [Fact(DisplayName = "decimal（引数省略）は Precision/Scale が null で解析される")]
    public void TryParse_DecimalWithoutArgs_ResolvesDecimalWithNullPrecisionScale()
    {
        Catalog.TryParse("decimal", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().BeNull();
        canonical.Scale.Should().BeNull();
    }

    [Fact(DisplayName = "numeric(p,s) は parse-only で Decimal として解析される")]
    public void TryParse_Numeric_ResolvesDecimal()
    {
        Catalog.TryParse("numeric(18,0)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Decimal);
        canonical.Precision.Should().Be(18);
        canonical.Scale.Should().Be(0);
    }

    [Fact(DisplayName = "ntext は parse-only で String(-1) として解析される")]
    public void TryParse_NText_ResolvesStringMax()
    {
        Catalog.TryParse("ntext", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "text は parse-only で AnsiString(-1) として解析される")]
    public void TryParse_Text_ResolvesAnsiStringMax()
    {
        Catalog.TryParse("text", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.AnsiString);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "image は parse-only で Binary(-1) として解析される")]
    public void TryParse_Image_ResolvesBinaryMax()
    {
        Catalog.TryParse("image", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Binary);
        canonical.Length.Should().Be(-1);
    }

    [Fact(DisplayName = "datetime は parse-only で DateTime として解析される")]
    public void TryParse_DateTime_ResolvesDateTime()
    {
        Catalog.TryParse("datetime", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
    }

    [Fact(DisplayName = "smalldatetime は parse-only で DateTime として解析される")]
    public void TryParse_SmallDateTime_ResolvesDateTime()
    {
        Catalog.TryParse("smalldatetime", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
    }

    [Fact(DisplayName = "smallmoney は parse-only で Money として解析される")]
    public void TryParse_SmallMoney_ResolvesMoney()
    {
        Catalog.TryParse("smallmoney", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Money);
    }

    [Fact(DisplayName = "datetime2(p) は精度付き DateTime として解析される")]
    public void TryParse_DateTime2WithPrecision_ResolvesDateTimeWithPrecision()
    {
        Catalog.TryParse("datetime2(3)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTime);
        canonical.Precision.Should().Be(3);
    }

    [Fact(DisplayName = "time(p) は精度付き Time として解析される")]
    public void TryParse_TimeWithPrecision_ResolvesTimeWithPrecision()
    {
        Catalog.TryParse("time(7)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.Time);
        canonical.Precision.Should().Be(7);
    }

    [Fact(DisplayName = "datetimeoffset(p) は精度付き DateTimeOffset として解析される")]
    public void TryParse_DateTimeOffsetWithPrecision_ResolvesDateTimeOffsetWithPrecision()
    {
        Catalog.TryParse("datetimeoffset(7)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.DateTimeOffset);
        canonical.Precision.Should().Be(7);
    }

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("hierarchyid")]
    [InlineData("geography")]
    [InlineData("geometry")]
    [InlineData("sql_variant")]
    [InlineData("no_such_type")]
    [InlineData("nvarchar(-5)")] // 負数の型引数は例外ではなく false
    [InlineData("nvarchar(99999999999)")] // int 範囲外の型引数は例外ではなく false
    [InlineData("decimal(99999999999,2)")]
    public void TryParse_UnconvertibleTypes_ReturnsFalse(string nativeType)
    {
        Catalog.TryParse(nativeType, out _).Should().BeFalse();
    }

    /// <summary>
    /// 行バージョン型（<c>rowversion</c> と非推奨別名 <c>timestamp</c>）が
    /// <see cref="CanonicalTypeKind.RowVersion"/> として解析され、代表表記へ整形されることを検証する。
    /// </summary>
    /// <remarks>
    /// 他方言へ持ち出すために正規型を持たせている（SQLite は <c>BLOB</c>＝ミラー列へ落とす）。
    /// <c>timestamp</c> は同義の非推奨別名のため、整形（<see cref="ITypeCatalog.TryFormat"/>）では
    /// 代表表記 <c>rowversion</c> の 1 つに寄せる。
    /// </remarks>
    [Theory(DisplayName = "rowversion / timestamp は行バージョンの正規型として解析される")]
    [InlineData("rowversion")]
    [InlineData("timestamp")]
    [InlineData("TIMESTAMP")]
    public void TryParse_RowVersionTypes_ResolvesRowVersionKind(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(CanonicalTypeKind.RowVersion);

        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();
        formatted.Should().Be("rowversion", "timestamp は非推奨別名のため代表表記へ寄せる");
    }

    [Theory(DisplayName = "TryFormat が主要な正規型からネイティブ型文字列を生成する")]
    [InlineData(CanonicalTypeKind.Boolean, "bit")]
    [InlineData(CanonicalTypeKind.TinyInt, "tinyint")]
    [InlineData(CanonicalTypeKind.SmallInt, "smallint")]
    [InlineData(CanonicalTypeKind.Int32, "int")]
    [InlineData(CanonicalTypeKind.Int64, "bigint")]
    [InlineData(CanonicalTypeKind.Float32, "real")]
    [InlineData(CanonicalTypeKind.Float64, "float")]
    [InlineData(CanonicalTypeKind.Money, "money")]
    [InlineData(CanonicalTypeKind.Date, "date")]
    [InlineData(CanonicalTypeKind.Guid, "uniqueidentifier")]
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

    [Fact(DisplayName = "TryFormat(String(n)) は nvarchar(n) を生成する")]
    public void TryFormat_StringWithLength_ProducesNVarchar()
    {
        var ok = Catalog.TryFormat(
            new CanonicalType(CanonicalTypeKind.String, Length: 100),
            out var nativeType
        );

        ok.Should().BeTrue();
        nativeType.Should().Be("nvarchar(100)");
    }

    [Fact(DisplayName = "TryFormat(String(-1)) は nvarchar(max) を生成する")]
    public void TryFormat_StringMax_ProducesNVarcharMax()
    {
        var ok = Catalog.TryFormat(
            new CanonicalType(CanonicalTypeKind.String, Length: -1),
            out var nativeType
        );

        ok.Should().BeTrue();
        nativeType.Should().Be("nvarchar(max)");
    }

    [Fact(DisplayName = "TryFormat(Decimal) は decimal(p,s) を生成する")]
    public void TryFormat_Decimal_ProducesDecimalWithPrecisionScale()
    {
        var ok = Catalog.TryFormat(
            new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 2),
            out var nativeType
        );

        ok.Should().BeTrue();
        nativeType.Should().Be("decimal(10,2)");
    }

    [Fact(DisplayName = "TryFormat(DateTime, Precision指定あり) は datetime2(p) を生成する")]
    public void TryFormat_DateTimeWithPrecision_ProducesDateTime2WithPrecision()
    {
        var ok = Catalog.TryFormat(
            new CanonicalType(CanonicalTypeKind.DateTime, Precision: 3),
            out var nativeType
        );

        ok.Should().BeTrue();
        nativeType.Should().Be("datetime2(3)");
    }

    [Fact(DisplayName = "TryFormat(DateTime, Precisionなし) は datetime2 を生成する")]
    public void TryFormat_DateTimeWithoutPrecision_ProducesDateTime2()
    {
        var ok = Catalog.TryFormat(
            new CanonicalType(CanonicalTypeKind.DateTime),
            out var nativeType
        );

        ok.Should().BeTrue();
        nativeType.Should().Be("datetime2");
    }

    [Fact(DisplayName = "TryFormat(Json) は nvarchar(max) を生成する（json 型が候補に無いため）")]
    public void TryFormat_Json_ProducesNVarcharMax()
    {
        var ok = Catalog.TryFormat(new CanonicalType(CanonicalTypeKind.Json), out var nativeType);

        ok.Should().BeTrue();
        nativeType.Should().Be("nvarchar(max)");
    }

    [Theory(DisplayName = "パース → フォーマットのラウンドトリップで型が保持される")]
    [InlineData("bit")]
    [InlineData("tinyint")]
    [InlineData("smallint")]
    [InlineData("int")]
    [InlineData("bigint")]
    [InlineData("decimal(10,2)")]
    [InlineData("real")]
    [InlineData("float")]
    [InlineData("money")]
    [InlineData("nvarchar(100)")]
    [InlineData("nvarchar(max)")]
    [InlineData("varchar(50)")]
    [InlineData("varchar(max)")]
    [InlineData("nchar(10)")]
    [InlineData("char(10)")]
    [InlineData("varbinary(50)")]
    [InlineData("varbinary(max)")]
    [InlineData("binary(50)")]
    [InlineData("date")]
    [InlineData("time(7)")]
    [InlineData("datetime2(3)")]
    [InlineData("datetimeoffset(7)")]
    [InlineData("uniqueidentifier")]
    [InlineData("xml")]
    public void ParseThenFormat_RoundTrips_ProducesSameNativeType(string nativeType)
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        Catalog.TryFormat(canonical, out var formatted).Should().BeTrue();

        formatted.Should().Be(nativeType);
    }
}
