using System;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>IncludeKeys.Normalize</c> が、主キーと外部キーの CLR の型が違っても同じ値を同じキーにすることを固定する単体テスト。
/// </summary>
/// <remarks>
/// SQLite の Include ローダーとインメモリの Include・カスケード削除が、このキーで突き合わせる。
/// 実 DB での関連の組み立ては <c>TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests</c> が固定する。
/// </remarks>
public sealed class IncludeKeysTests
{
    [Fact(DisplayName = "long に収まる整数はすべて long にそろえ、同じ値は型が違っても等しくなる")]
    public void Integers_BecomeLong()
    {
        IncludeKeys.Normalize(1).Should().Be(1L);
        IncludeKeys.Normalize((short)1).Should().Be(1L);
        IncludeKeys.Normalize((byte)1).Should().Be(1L);
        IncludeKeys.Normalize(1u).Should().Be(1L);
        IncludeKeys.Normalize(1UL).Should().Be(1L);
        IncludeKeys.Normalize(1L).Should().Be(1L);

        Equals(IncludeKeys.Normalize(1), IncludeKeys.Normalize(1L)).Should().BeTrue();
    }

    [Fact(DisplayName = "long に収まらない ulong と整数以外の値はそのまま返す")]
    public void OtherValues_AreReturnedAsIs()
    {
        IncludeKeys.Normalize(ulong.MaxValue).Should().Be(ulong.MaxValue);
        IncludeKeys.Normalize("1").Should().Be("1");

        var id = Guid.NewGuid();
        IncludeKeys.Normalize(id).Should().Be(id);
        IncludeKeys.Normalize(null).Should().BeNull();
    }

    [Fact(DisplayName = "バイト列は中身の 16 進文字列にし、同じ中身の別の配列が等しくなる")]
    public void ByteArrays_CompareByContent()
    {
        IncludeKeys.Normalize(new byte[] { 1, 2, 0xAB }).Should().Be("0102AB");

        Equals(
                IncludeKeys.Normalize(new byte[] { 1, 2 }),
                IncludeKeys.Normalize(new byte[] { 1, 2 })
            )
            .Should()
            .BeTrue();
    }

    [Fact(DisplayName = "値オブジェクトは中の値を取り出してからそろえる")]
    public void ValueObjects_AreOpened()
    {
        IncludeKeys.Normalize(CustomerIdValue.Create(7)).Should().Be(7L);
    }
}
