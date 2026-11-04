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
/// <para>
/// 等値・ハッシュの一致・Dictionary キーの決まった形の表明は <see cref="QuickER.Tests.GeneratedFixture.ValueObjectScenarioTests"/>
/// の vo-equality の表にある。ここに残すのは、ハッシュの分散を統計で見るテストだけ。
/// </para>
/// </remarks>
public sealed class ValueObjectBinaryEqualityTests
{
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
}
