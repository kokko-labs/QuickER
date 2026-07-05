using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="CanonicalTypeToken"/> の Format / TryParse（方言中立トークン ⇔ 正規型）を検証するテストクラス。
/// </summary>
/// <remarks>
/// DB 定義メタ属性（<c>[DbColumnMeta]</c>）へ刻む中立トークンが、全 <see cref="CanonicalTypeKind"/> を
/// 双方向に無損失で往復できること（正書法の固定・引数規則の網羅）を守る。
/// </remarks>
public class CanonicalTypeTokenTests
{
    /// <summary>引数を持たない全種別の代表値（Format→TryParse で Kind が一致し、トークン表記も固定であること）</summary>
    [Theory(DisplayName = "引数なし種別のトークン正書法と往復")]
    [InlineData(CanonicalTypeKind.Boolean, "boolean")]
    [InlineData(CanonicalTypeKind.TinyInt, "tinyint")]
    [InlineData(CanonicalTypeKind.SmallInt, "smallint")]
    [InlineData(CanonicalTypeKind.Int32, "int32")]
    [InlineData(CanonicalTypeKind.Int64, "int64")]
    [InlineData(CanonicalTypeKind.Float32, "float32")]
    [InlineData(CanonicalTypeKind.Float64, "float64")]
    [InlineData(CanonicalTypeKind.Money, "money")]
    [InlineData(CanonicalTypeKind.Date, "date")]
    [InlineData(CanonicalTypeKind.Guid, "guid")]
    [InlineData(CanonicalTypeKind.Xml, "xml")]
    [InlineData(CanonicalTypeKind.Json, "json")]
    public void SimpleKinds_FormatAndRoundTrip(CanonicalTypeKind kind, string expectedToken)
    {
        var canonical = new CanonicalType(kind);

        var token = CanonicalTypeToken.Format(canonical);
        token.Should().Be(expectedToken);

        CanonicalTypeToken.TryParse(token, out var parsed).Should().BeTrue();
        parsed.Should().Be(canonical);
    }

    /// <summary>長さ引数を持つ文字列・バイナリ系種別の正書法（n / max / 無指定）と往復</summary>
    [Theory(DisplayName = "長さ引数種別のトークン正書法と往復")]
    [InlineData(CanonicalTypeKind.String, 50, "string(50)")]
    [InlineData(CanonicalTypeKind.String, -1, "string(max)")]
    [InlineData(CanonicalTypeKind.String, null, "string")]
    [InlineData(CanonicalTypeKind.AnsiString, 100, "ansistring(100)")]
    [InlineData(CanonicalTypeKind.AnsiString, -1, "ansistring(max)")]
    [InlineData(CanonicalTypeKind.FixedString, 10, "fixedstring(10)")]
    [InlineData(CanonicalTypeKind.AnsiFixedString, 5, "ansifixedstring(5)")]
    [InlineData(CanonicalTypeKind.Binary, 256, "binary(256)")]
    [InlineData(CanonicalTypeKind.Binary, -1, "binary(max)")]
    [InlineData(CanonicalTypeKind.FixedBinary, 16, "fixedbinary(16)")]
    public void LengthKinds_FormatAndRoundTrip(
        CanonicalTypeKind kind,
        int? length,
        string expectedToken
    )
    {
        var canonical = new CanonicalType(kind, Length: length);

        CanonicalTypeToken.Format(canonical).Should().Be(expectedToken);

        CanonicalTypeToken.TryParse(expectedToken, out var parsed).Should().BeTrue();
        parsed.Should().Be(canonical);
    }

    /// <summary>decimal の精度・スケール（p,s / p / 無指定）の正書法と往復</summary>
    [Theory(DisplayName = "decimal の精度スケールのトークン正書法と往復")]
    [InlineData(10, 2, "decimal(10,2)")]
    [InlineData(18, 0, "decimal(18,0)")]
    [InlineData(10, null, "decimal(10)")]
    [InlineData(null, null, "decimal")]
    public void Decimal_FormatAndRoundTrip(int? precision, int? scale, string expectedToken)
    {
        var canonical = new CanonicalType(
            CanonicalTypeKind.Decimal,
            Precision: precision,
            Scale: scale
        );

        CanonicalTypeToken.Format(canonical).Should().Be(expectedToken);

        CanonicalTypeToken.TryParse(expectedToken, out var parsed).Should().BeTrue();
        parsed.Should().Be(canonical);
    }

    /// <summary>時刻・日時系（小数秒桁 Precision のみ）の正書法と往復</summary>
    [Theory(DisplayName = "時刻・日時系のトークン正書法と往復")]
    [InlineData(CanonicalTypeKind.Time, 7, "time(7)")]
    [InlineData(CanonicalTypeKind.Time, null, "time")]
    [InlineData(CanonicalTypeKind.DateTime, 7, "datetime(7)")]
    [InlineData(CanonicalTypeKind.DateTime, 0, "datetime(0)")]
    [InlineData(CanonicalTypeKind.DateTime, null, "datetime")]
    [InlineData(CanonicalTypeKind.DateTimeOffset, 7, "datetimeoffset(7)")]
    [InlineData(CanonicalTypeKind.DateTimeOffset, null, "datetimeoffset")]
    public void PrecisionOnlyKinds_FormatAndRoundTrip(
        CanonicalTypeKind kind,
        int? precision,
        string expectedToken
    )
    {
        var canonical = new CanonicalType(kind, Precision: precision);

        CanonicalTypeToken.Format(canonical).Should().Be(expectedToken);

        CanonicalTypeToken.TryParse(expectedToken, out var parsed).Should().BeTrue();
        parsed.Should().Be(canonical);
    }

    /// <summary>全 <see cref="CanonicalTypeKind"/> の代表値が Format→TryParse で完全一致すること（種別の網羅漏れ防止）</summary>
    [Fact(DisplayName = "全 CanonicalTypeKind が Format→TryParse で往復する")]
    public void AllKinds_RoundTrip()
    {
        // 各種別の代表的な引数を与え、全種別が一度は往復することを保証する
        var samples = Enum.GetValues<CanonicalTypeKind>().Select(RepresentativeSample).ToList();

        samples.Should().HaveCount(Enum.GetValues<CanonicalTypeKind>().Length);

        foreach (var canonical in samples)
        {
            var token = CanonicalTypeToken.Format(canonical);

            CanonicalTypeToken
                .TryParse(token, out var parsed)
                .Should()
                .BeTrue($"'{token}'（{canonical.Kind}）は解析できるべき");
            parsed.Should().Be(canonical, $"'{token}' の往復で正規型が一致するべき");
        }
    }

    /// <summary>不正・未知のトークンは TryParse が false を返す</summary>
    [Theory(DisplayName = "不正トークンは解析に失敗する")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknownkind")]
    [InlineData("int32(5)")] // 引数を取らない種別に括弧
    [InlineData("string(1,2)")] // 文字列に 2 引数
    [InlineData("time(1,2)")] // 時刻に 2 引数
    [InlineData("decimal(max)")] // decimal に max は不可
    [InlineData("string()")] // 空括弧
    [InlineData("string(-5)")] // 長さに負数（max 以外の -1 も TryParse の None で弾かれる）
    public void TryParse_InvalidTokens_ReturnsFalse(string token)
    {
        CanonicalTypeToken.TryParse(token, out _).Should().BeFalse();
    }

    /// <summary>各種別の代表サンプルを返す（引数規則に沿った値を与える）</summary>
    private static CanonicalType RepresentativeSample(CanonicalTypeKind kind) =>
        kind switch
        {
            CanonicalTypeKind.String
            or CanonicalTypeKind.AnsiString
            or CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString
            or CanonicalTypeKind.Binary
            or CanonicalTypeKind.FixedBinary => new CanonicalType(kind, Length: 42),
            CanonicalTypeKind.Decimal => new CanonicalType(kind, Precision: 12, Scale: 3),
            CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => new CanonicalType(kind, Precision: 6),
            _ => new CanonicalType(kind),
        };
}
