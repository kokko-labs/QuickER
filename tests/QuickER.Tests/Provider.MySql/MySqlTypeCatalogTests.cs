using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Provider.MySql;

namespace QuickER.Tests.Provider.MySql;

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

    /// <summary>
    /// 整数型は符号の有無で値の範囲が変わるので、範囲を収められる正規型として解析されることを検証する
    /// （正規型の TinyInt は 0〜255。符号なしは 1 段広い型、bigint unsigned は decimal(20,0)）。
    /// </summary>
    [Theory(DisplayName = "整数型は符号の有無に応じて範囲を収められる正規型として解析される")]
    [InlineData("tinyint unsigned", CanonicalTypeKind.TinyInt, null)]
    [InlineData("tinyint(4) unsigned zerofill", CanonicalTypeKind.TinyInt, null)]
    [InlineData("tinyint", CanonicalTypeKind.SmallInt, null)]
    [InlineData("tinyint(4)", CanonicalTypeKind.SmallInt, null)]
    [InlineData("smallint", CanonicalTypeKind.SmallInt, null)]
    [InlineData("smallint unsigned", CanonicalTypeKind.Int32, null)]
    [InlineData("int", CanonicalTypeKind.Int32, null)]
    [InlineData("int unsigned", CanonicalTypeKind.Int64, null)]
    [InlineData("INT(10) UNSIGNED", CanonicalTypeKind.Int64, null)]
    [InlineData("bigint", CanonicalTypeKind.Int64, null)]
    [InlineData("bigint unsigned", CanonicalTypeKind.Decimal, 20)]
    [InlineData("mediumint", CanonicalTypeKind.Int32, null)]
    [InlineData("mediumint(8)", CanonicalTypeKind.Int32, null)]
    [InlineData("mediumint unsigned", CanonicalTypeKind.Int32, null)]
    [InlineData("year", CanonicalTypeKind.Int32, null)]
    public void TryParse_Integer_UsesRangeOfSign(
        string nativeType,
        CanonicalTypeKind kind,
        int? precision
    )
    {
        Catalog.TryParse(nativeType, out var canonical).Should().BeTrue();
        canonical.Kind.Should().Be(kind);
        canonical.Precision.Should().Be(precision);
        canonical.Scale.Should().Be(precision is null ? null : 0);
    }

    [Fact(DisplayName = "varchar(n) は String(n) として解析される")]
    public void TryParse_VarcharWithLength_ResolvesStringWithLength()
    {
        Catalog.TryParse("varchar(50)", out var canonical).Should().BeTrue();

        canonical.Kind.Should().Be(CanonicalTypeKind.String);
        canonical.Length.Should().Be(50);
    }

    [Theory(DisplayName = "tinytext / text / mediumtext / longtext は String(-1) として解析される")]
    [InlineData("tinytext")]
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

    [Theory(DisplayName = "tinyblob / blob / mediumblob / longblob は Binary(-1) として解析される")]
    [InlineData("tinyblob")]
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

    [Theory(DisplayName = "変換不能な型は TryParse が false を返す")]
    [InlineData("enum('a','b')")]
    [InlineData("set('x','y')")]
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
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiString, Length: 50),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("varchar(50)");
    }

    [Fact(DisplayName = "TryFormat(AnsiString(-1)) は longtext を生成する")]
    public void TryFormat_AnsiStringMax_ProducesLongtext()
    {
        Catalog
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.AnsiString, Length: -1),
                out var nativeType
            )
            .Should()
            .BeTrue();

        nativeType.Should().Be("longtext");
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
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.FixedBinary, Length: 16),
                out var nativeType
            )
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
            .TryFormat(
                new CanonicalType(CanonicalTypeKind.DateTime, Precision: 3),
                out var nativeType
            )
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

    /// <summary>
    /// 引数を省いた decimal は MySQL の既定どおり decimal(10,0) として解析される。精度なし（任意の小数）と読むと、
    /// 書き出せない型として C# リバースが偽の警告を出し、他方言への切替も意味の違う型になる。
    /// </summary>
    [Fact(DisplayName = "decimal（引数省略）は既定の decimal(10,0) として解析される")]
    public void TryParse_DecimalWithoutArgs_ResolvesDefaultPrecisionScale()
    {
        Catalog.TryParse("decimal", out var canonical).Should().BeTrue();

        canonical
            .Should()
            .Be(new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 0));
    }

    /// <summary>
    /// MySQL が宣言を拒否する精度・スケール（と精度の無い Decimal）は書き出さず、対象 DBMS 切替の変換不能一覧に
    /// 載せることを検証する。上限ちょうどは書き出せる（境界の対照）。
    /// </summary>
    [Theory(DisplayName = "TryFormat は MySQL の上限を超える値と精度の無い Decimal を書き出さない")]
    [InlineData(CanonicalTypeKind.DateTime, null, 7, null, false)]
    [InlineData(CanonicalTypeKind.DateTime, null, 6, null, true)]
    [InlineData(CanonicalTypeKind.Time, null, 7, null, false)]
    [InlineData(CanonicalTypeKind.DateTimeOffset, null, 7, null, false)]
    [InlineData(CanonicalTypeKind.Decimal, null, 66, 2, false)]
    [InlineData(CanonicalTypeKind.Decimal, null, 65, 30, true)]
    [InlineData(CanonicalTypeKind.Decimal, null, 40, 31, false)]
    [InlineData(CanonicalTypeKind.Decimal, null, 10, 11, false)]
    [InlineData(CanonicalTypeKind.Decimal, null, null, null, false)]
    [InlineData(CanonicalTypeKind.Decimal, null, null, 2, false)]
    [InlineData(CanonicalTypeKind.String, 16383, null, null, true)]
    [InlineData(CanonicalTypeKind.String, 16384, null, null, false)]
    [InlineData(CanonicalTypeKind.AnsiString, 16383, null, null, true)]
    [InlineData(CanonicalTypeKind.AnsiString, 16384, null, null, false)]
    [InlineData(CanonicalTypeKind.String, -1, null, null, true)]
    [InlineData(CanonicalTypeKind.FixedString, 255, null, null, true)]
    [InlineData(CanonicalTypeKind.FixedString, 256, null, null, false)]
    [InlineData(CanonicalTypeKind.AnsiFixedString, 255, null, null, true)]
    [InlineData(CanonicalTypeKind.AnsiFixedString, 256, null, null, false)]
    [InlineData(CanonicalTypeKind.FixedBinary, 255, null, null, true)]
    [InlineData(CanonicalTypeKind.FixedBinary, 256, null, null, false)]
    [InlineData(CanonicalTypeKind.Binary, 65532, null, null, true)]
    [InlineData(CanonicalTypeKind.Binary, 65533, null, null, false)]
    [InlineData(CanonicalTypeKind.Binary, -1, null, null, true)]
    public void TryFormat_RespectsMySqlLimits(
        CanonicalTypeKind kind,
        int? length,
        int? precision,
        int? scale,
        bool expected
    )
    {
        Catalog
            .TryFormat(new CanonicalType(kind, length, precision, scale), out _)
            .Should()
            .Be(expected);
    }

    /// <summary>
    /// 名前付きクエリのパラメータは列を作らないので、列の上限を超える長さ・精度も書き出す（C# 型への写像だけに使う）。
    /// 型そのものを書けない規則（長さ無し・負のスケール・精度の無い Decimal）は列と同じ。
    /// </summary>
    [Theory(
        DisplayName = "TryFormatParameterType は列の上限を当てず、型を書けない規則だけを当てる"
    )]
    [InlineData(CanonicalTypeKind.String, 20000, null, null, "varchar(20000)")]
    [InlineData(CanonicalTypeKind.FixedString, 300, null, null, "char(300)")]
    [InlineData(CanonicalTypeKind.FixedBinary, 300, null, null, "binary(300)")]
    [InlineData(CanonicalTypeKind.Binary, 70000, null, null, "varbinary(70000)")]
    [InlineData(CanonicalTypeKind.DateTime, null, 7, null, "datetime(7)")]
    [InlineData(CanonicalTypeKind.Decimal, null, 66, 2, "decimal(66,2)")]
    [InlineData(CanonicalTypeKind.String, null, null, null, null)]
    [InlineData(CanonicalTypeKind.Decimal, null, 10, -2, null)]
    [InlineData(CanonicalTypeKind.Decimal, null, null, null, null)]
    public void TryFormatParameterType_IgnoresColumnLimits(
        CanonicalTypeKind kind,
        int? length,
        int? precision,
        int? scale,
        string? expected
    )
    {
        var ok = Catalog.TryFormatParameterType(
            new CanonicalType(kind, length, precision, scale),
            out var nativeType
        );

        ok.Should().Be(expected is not null);

        if (expected is not null)
        {
            nativeType.Should().Be(expected);
        }
    }
}
