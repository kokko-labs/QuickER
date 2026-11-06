using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Model;

/// <summary>名前の同一性の規則の正本 <see cref="SchemaNames"/> のテストクラス</summary>
/// <remarks>
/// 期待値は名前の総当たりの規則（<see cref="NameIdentityRule"/>）と同じ答えになることを、総当たりの名前の組で確かめる
/// （正本と総当たりの期待値が別々にずれない）。
/// </remarks>
public sealed class SchemaNamesTests
{
    /// <remarks>
    /// 残す理由（P3-6）：null と空文字（null と ''・'' と ' '）の公開 API の契約。前後の空白・大文字小文字だけの違い・別の名前の組は、
    /// 総当たりの規則との照合（<see cref="AreSame_AgreesWithFuzzRule"/>＝<see cref="NameFuzz.FixedPairs"/> の同じ形の組と種の組）へ移した
    /// </remarks>
    [Theory(DisplayName = "AreSame は null と空文字を空の名前として比べる")]
    [InlineData("", " ", true)]
    [InlineData(null, "", true)]
    public void AreSame_TrimsAndIgnoresCase(string? first, string? second, bool expected) =>
        SchemaNames.AreSame(first, second).Should().Be(expected);

    [Fact(DisplayName = "AreSame は名前の総当たりの規則と同じ答えになる")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void AreSame_AgreesWithFuzzRule()
    {
        foreach (var seed in NameFuzz.Seeds)
        {
            foreach (var pair in NameFuzz.Pairs(seed, NameFuzzRunner.RandomPairsPerSeed))
            {
                SchemaNames
                    .AreSame(pair.First, pair.Second)
                    .Should()
                    .Be(
                        NameIdentityRule.AreSame(NameKind.Table, pair.First, pair.Second),
                        pair.ToString()
                    );
                SchemaNames
                    .ToStoredForm(pair.Second)
                    .Should()
                    .Be(NameIdentityRule.StoredForm(pair.Second), pair.ToString());
            }
        }
    }

    /// <remarks>
    /// 残す理由（P3-6）：null は空文字（総当たりの規則は null を受けない）。前後の空白だけを除き綴りを変えないことは
    /// <see cref="AreSame_AgreesWithFuzzRule"/> の保存形の照合へ移した
    /// </remarks>
    [Fact(DisplayName = "ToStoredForm は null を空文字にする")]
    public void ToStoredForm_TrimsOnly()
    {
        SchemaNames.ToStoredForm(null).Should().BeEmpty();
    }

    [Fact(DisplayName = "Comparer は AreSame と同じ規則で辞書の鍵を引き当てる")]
    public void Comparer_GroupsLikeAreSame()
    {
        var names = new Dictionary<string, int>(SchemaNames.Comparer) { ["Orders"] = 1 };

        names.ContainsKey(" ORDERS ").Should().BeTrue();
        names.ContainsKey("Order").Should().BeFalse();
        SchemaNames
            .Comparer.GetHashCode(" orders")
            .Should()
            .Be(SchemaNames.Comparer.GetHashCode("ORDERS "));
    }
}
