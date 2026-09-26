using AwesomeAssertions;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.Provider.PostgreSql;

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

    /// <summary>
    /// <c>time with time zone</c>（<c>timetz</c>）は <c>DateTimeOffset</c> へマップされる（TM4）。
    /// </summary>
    /// <remarks>
    /// 従来は <c>time</c> へ畳んで <c>TimeSpan</c> にしており、Npgsql が返す CLR 型と食い違うため
    /// <b>読み取り自体が</b> <c>InvalidCastException</c> になっていた（実 PG で実測。
    /// 行の組み立ては全列をまとめて読むため、その列を持つテーブルが丸ごと使えない）。
    /// </remarks>
    [Theory(DisplayName = "time with time zone は DateTimeOffset へマップされる")]
    [InlineData("time with time zone")]
    [InlineData("timetz")]
    [InlineData("TIME WITH TIME ZONE")]
    [InlineData("time(3) with time zone")]
    [InlineData("timetz(3)")]
    public void Map_TimeWithTimeZone_MapsToDateTimeOffset(string dataType)
    {
        var info = Mapper.Map(dataType);

        info.TypeName.Should().Be("DateTimeOffset");
        info.IsReferenceType.Should().BeFalse();
        info.IsFallbackType.Should().BeFalse();
    }

    [Theory(DisplayName = "time without time zone は従来どおり TimeSpan のまま")]
    [InlineData("time")]
    [InlineData("time(6)")]
    [InlineData("time without time zone")]
    [InlineData("time(6) without time zone")]
    public void Map_TimeWithoutTimeZone_StaysTimeSpan(string dataType)
    {
        Mapper.Map(dataType).TypeName.Should().Be("TimeSpan");
    }

    /// <summary>
    /// 精度つきの <c>timestamp ... with time zone</c> も時間帯を落とさず <c>DateTimeOffset</c> になる。
    /// </summary>
    /// <remarks>
    /// 基本型名の取り出しが「括弧より後ろを切り捨てる」形だったため、PostgreSQL が<b>括弧の後ろへ</b>置く
    /// 時間帯の語ごと落ちて <c>timestamp</c> に見え、別名解決が <c>timestamptz</c> に到達できなかった。
    /// </remarks>
    [Theory(DisplayName = "精度つきでも時間帯の語を落とさない")]
    [InlineData("timestamp(6) with time zone", "DateTimeOffset")]
    [InlineData("timestamptz(6)", "DateTimeOffset")]
    [InlineData("timestamp(6) without time zone", "DateTime")]
    [InlineData("timestamp(6)", "DateTime")]
    public void Map_TimestampWithPrecision_KeepsTimeZoneDistinction(
        string dataType,
        string expected
    )
    {
        Mapper.Map(dataType).TypeName.Should().Be(expected);
    }

    [Theory(DisplayName = "未知の型は IsFallbackType が true になる")]
    [InlineData("inet")]
    [InlineData("point")]
    public void Map_UnknownType_IsFallbackTypeIsTrue(string dataType)
    {
        Mapper.Map(dataType).IsFallbackType.Should().BeTrue();
    }

    [Fact(DisplayName = "解決できる型は IsFallbackType が false になる")]
    public void Map_KnownType_IsFallbackTypeIsFalse()
    {
        Mapper.Map("integer").IsFallbackType.Should().BeFalse();
        Mapper.Map("varchar(50)").IsFallbackType.Should().BeFalse();
    }
}
