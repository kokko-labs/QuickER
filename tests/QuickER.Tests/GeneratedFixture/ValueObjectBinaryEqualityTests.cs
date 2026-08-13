using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// byte[] を内包する VO（<c>ValueObjectBinaryBase</c>）の等値・ハッシュ契約を、コミット済みフィクスチャ
/// （<c>ConcurrencyFixture.g.cs</c> の <c>RowVerValue</c>）の実型に対して検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 等値はスパンの <c>SequenceEqual</c>（ベクトル化）で、ハッシュは <c>HashCode.AddBytes</c> による全量ベース。
/// 以前は配列の構造比較器（<c>StructuralComparisons.StructuralEqualityComparer</c>）を使っており、
/// そのハッシュは<b>末尾 8 要素しか混ぜない</b>ため、先頭だけが異なる長い blob が全て同一バケットへ潰れていた。
/// 本テストはその回帰を「先頭 1 バイトだけ異なる 64 本のハッシュが分散すること」で固定する。
/// </para>
/// <para>
/// あわせて <c>IEquatable&lt;TSelf&gt;</c> 実装（<c>EqualityComparer&lt;T&gt;.Default</c> が object ベースの
/// フォールバックへ落ちないこと）と、Dictionary キーとしての値ベース一致も固定する。
/// </para>
/// </remarks>
public sealed class ValueObjectBinaryEqualityTests
{
    [Fact(DisplayName = "バイナリ VO は同内容の別配列を等値とみなし、ハッシュも一致する")]
    public void バイナリVOは同内容で等値()
    {
        var left = RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        var right = RowVerValue.Create([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        (left == right).Should().BeTrue();
        // Equals(object?) は型付きオーバーロードへ委譲する
        left.Equals((object)right).Should().BeTrue();
        // 防御コピーも等値（配列は複製されるが値は同じ）
        left.CopyValue().Equals(left).Should().BeTrue();
    }

    [Fact(DisplayName = "バイナリ VO は長さ違い・null 相手・異型を等しくないと判定する")]
    public void バイナリVOの非等値()
    {
        var left = RowVerValue.Create([1, 2, 3]);

        left.Equals(RowVerValue.Create([1, 2, 3, 0])).Should().BeFalse();
        left.Equals((object)"1,2,3").Should().BeFalse();

        // null 相手は毎回新しいレシーバーで書く（IEquatable<T> へ Equals(null) を渡すと C# の null 解析が
        // レシーバーを maybe-null へ落とし、以降の同一変数の使用が CS8602 になるため。BCL の string も同じ）
        RowVerValue.Create([1, 2, 3]).Equals((RowVerValue?)null).Should().BeFalse();
        RowVerValue.Create([1, 2, 3]).Equals((object?)null).Should().BeFalse();
    }

    [Fact(
        DisplayName = "バイナリ VO は先頭 1 バイトの差を等値で検出する（末尾一致に引きずられない）"
    )]
    public void バイナリVOは先頭の差を等値で検出する()
    {
        var a = new byte[1024];
        var b = new byte[1024];
        b[0] = 1;

        var left = RowVerValue.Create(a);
        var right = RowVerValue.Create(b);

        left.Equals(right).Should().BeFalse();
        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact(
        DisplayName = "バイナリ VO のハッシュは全バイトを反映する（先頭のみ異なる 64 本が分散する）"
    )]
    public void バイナリVOのハッシュは全量ベース()
    {
        // 末尾 8 要素しか混ぜない構造比較器のハッシュでは、この 64 本は「1 種類」へ潰れていた。
        var distinct = Enumerable
            .Range(0, 64)
            .Select(i =>
            {
                var bytes = new byte[1024];
                bytes[0] = (byte)i;
                return RowVerValue.Create(bytes).GetHashCode();
            })
            .Distinct()
            .Count();

        distinct
            .Should()
            .BeGreaterThan(32, "全バイトを反映するハッシュなら 64 本はほぼ全て相異なる");
    }

    [Fact(
        DisplayName = "バイナリ VO は IEquatable<TSelf> 実装により Dictionary キーとして値ベースで一致する"
    )]
    public void バイナリVOは辞書キーとして値ベースで一致する()
    {
        typeof(RowVerValue).Should().BeAssignableTo<IEquatable<RowVerValue>>();
        EqualityComparer<RowVerValue>
            .Default.GetType()
            .Name.Should()
            .NotStartWith(
                "ObjectEqualityComparer",
                "IEquatable<TSelf> があれば型付きの比較器が選ばれる"
            );

        var map = new Dictionary<RowVerValue, string> { [RowVerValue.Create([9, 9])] = "v" };

        map.TryGetValue(RowVerValue.Create([9, 9]), out var found).Should().BeTrue();
        found.Should().Be("v");
        map.ContainsKey(RowVerValue.Create([9, 8])).Should().BeFalse();
    }
}
