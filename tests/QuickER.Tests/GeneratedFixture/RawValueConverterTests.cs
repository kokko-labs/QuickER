using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 固定 infra の値変換 <see cref="RawValueConverter"/> と、生 SQL の単一値・射影（<see cref="RawSqlMapper"/>）が、
/// <c>Convert.ChangeType</c> の届かない列挙型と <see cref="DateOnly"/> / <see cref="TimeOnly"/> を扱えることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 列挙型は <c>ChangeType</c> では作れず、どの値でも <see cref="InvalidCastException"/> になっていた。さらに
/// <c>Type.GetTypeCode</c> が列挙型を基底の整数型として報告するため、ConvertInput の数値の解析より前で分岐しないと
/// 列挙型でなく int が返る。
/// </para>
/// <para>
/// <see cref="DateOnly"/> / <see cref="TimeOnly"/> は単一値として扱われず DTO 扱いで失敗し、ドライバが返す
/// <see cref="DateTime"/> / <see cref="TimeSpan"/> からの変換も無かった（SQL Server は date / time 列をそう返す）。
/// 時刻を持つ DateTime と 1 日の範囲外の TimeSpan は、値を黙って変えないよう失敗させる。
/// </para>
/// </remarks>
public sealed class RawValueConverterTests
{
    /// <summary>検証用の列挙型（基底は int）</summary>
    public enum Status
    {
        Draft = 1,
        Active = 2,
    }

    /// <summary>射影の受け皿（列挙型と日付・時刻のプロパティを持つ DTO）</summary>
    public sealed class Row
    {
        public Status State { get; set; }

        public DateOnly Day { get; set; }

        public TimeOnly At { get; set; }
    }

    [Theory(DisplayName = "列挙型へは整数・decimal・名前・数字の文字列から変換できる")]
    [InlineData(2L)]
    [InlineData(2)]
    [InlineData("Active")]
    [InlineData("2")]
    public void ConvertRaw_ToEnum_AcceptsNumbersAndNames(object raw)
    {
        RawValueConverter.ConvertRaw(raw, typeof(Status)).Should().Be(Status.Active);
        RawValueConverter.ConvertRaw(raw, typeof(Status?)).Should().Be(Status.Active);
    }

    [Fact(DisplayName = "列挙型へは decimal（Oracle の NUMBER 相当）からも変換できる")]
    public void ConvertRaw_ToEnum_AcceptsDecimal() =>
        RawValueConverter.ConvertRaw(1m, typeof(Status)).Should().Be(Status.Draft);

    [Fact(DisplayName = "メンバーにない数値は C# のキャストと同じく受け入れる")]
    public void ConvertRaw_ToEnum_AcceptsUndefinedNumber() =>
        RawValueConverter.ConvertRaw(7L, typeof(Status)).Should().Be((Status)7);

    [Fact(DisplayName = "名前でも数字でもない文字列は FormatException になる")]
    public void ConvertRaw_ToEnum_RejectsUnknownName()
    {
        var act = () => RawValueConverter.ConvertRaw("Archived", typeof(Status));

        act.Should().Throw<FormatException>();
    }

    [Fact(DisplayName = "ConvertInput の数字の文字列は int でなく列挙型を返す")]
    public void ConvertInput_ToEnum_ReturnsEnumNotUnderlyingInteger()
    {
        var converted = RawValueConverter.ConvertInput(
            "2",
            typeof(Status),
            CultureInfo.InvariantCulture
        );

        converted.Should().BeOfType<Status>().Which.Should().Be(Status.Active);
    }

    [Fact(DisplayName = "時刻が 0 の DateTime は DateOnly へ変換できる")]
    public void ConvertRaw_MidnightDateTime_ToDateOnly() =>
        RawValueConverter
            .ConvertRaw(new DateTime(2026, 10, 6), typeof(DateOnly?))
            .Should()
            .Be(new DateOnly(2026, 10, 6));

    [Fact(
        DisplayName = "時刻を持つ DateTime は DateOnly へ黙って切り捨てず InvalidCastException になる"
    )]
    public void ConvertRaw_DateTimeWithTime_ToDateOnly_Throws()
    {
        var act = () =>
            RawValueConverter.ConvertRaw(new DateTime(2026, 10, 6, 9, 30, 0), typeof(DateOnly));

        act.Should().Throw<InvalidCastException>();
    }

    [Fact(DisplayName = "1 日の範囲の TimeSpan は TimeOnly へ変換できる")]
    public void ConvertRaw_TimeSpan_ToTimeOnly() =>
        RawValueConverter
            .ConvertRaw(new TimeSpan(0, 10, 30, 45, 123), typeof(TimeOnly))
            .Should()
            .Be(new TimeOnly(10, 30, 45, 123));

    [Theory(DisplayName = "負の TimeSpan と 1 日以上の TimeSpan は OverflowException になる")]
    [InlineData(-1L)]
    [InlineData(TimeSpan.TicksPerDay)]
    public void ConvertRaw_TimeSpanOutsideDay_ToTimeOnly_Throws(long ticks)
    {
        var act = () => RawValueConverter.ConvertRaw(new TimeSpan(ticks), typeof(TimeOnly));

        act.Should().Throw<OverflowException>();
    }

    [Fact(DisplayName = "生 SQL のスカラーは列挙型を返せる")]
    public void ConvertSingleValue_ToEnum() =>
        RawSqlMapper.ConvertSingleValue<Status>(2L).Should().Be(Status.Active);

    [Fact(DisplayName = "単一値の射影は DateOnly / TimeOnly の一覧を読める")]
    public async Task ReadProjectionRows_DateOnlyAndTimeOnly_AreSingleValues()
    {
        var days = await ReadAsync<DateOnly>(
            typeof(DateTime),
            new DateTime(2026, 10, 5),
            new DateTime(2026, 10, 6)
        );
        var times = await ReadAsync<TimeOnly?>(typeof(TimeSpan), new TimeSpan(9, 0, 0), null);

        days.Should().Equal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6));
        times.Should().Equal(new TimeOnly(9, 0), null);
    }

    [Fact(DisplayName = "DTO の射影は列挙型・DateOnly・TimeOnly のプロパティへ書ける")]
    public async Task ReadProjectionRows_Dto_WithEnumAndDateTimeParts()
    {
        using var table = new DataTable();
        table.Columns.Add("state", typeof(long));
        table.Columns.Add("day", typeof(DateTime));
        table.Columns.Add("at", typeof(TimeSpan));
        table.Rows.Add(1L, new DateTime(2026, 10, 6), new TimeSpan(18, 15, 0));

        using var reader = table.CreateDataReader();
        var rows = await RawSqlMapper.ReadProjectionRowsAsync<Row>(
            reader,
            TestContext.Current.CancellationToken
        );

        var row = rows.Should().ContainSingle().Which;
        row.State.Should().Be(Status.Draft);
        row.Day.Should().Be(new DateOnly(2026, 10, 6));
        row.At.Should().Be(new TimeOnly(18, 15));
    }

    /// <summary>1 列の結果セットを組み、単一値モードで読む</summary>
    private static async Task<TResult[]> ReadAsync<TResult>(
        Type columnType,
        params object?[] values
    )
    {
        using var table = new DataTable();
        table.Columns.Add("value", columnType);

        foreach (var value in values)
        {
            table.Rows.Add(value ?? DBNull.Value);
        }

        using var reader = table.CreateDataReader();
        var rows = await RawSqlMapper.ReadProjectionRowsAsync<TResult>(
            reader,
            TestContext.Current.CancellationToken
        );

        return rows.ToArray();
    }
}
