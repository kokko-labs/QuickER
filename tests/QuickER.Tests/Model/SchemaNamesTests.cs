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
    [Theory(DisplayName = "AreSame は前後の空白を除き大文字小文字を無視して比べる")]
    [InlineData("Orders", "Orders", true)]
    [InlineData("Orders", "orders", true)]
    [InlineData(" Orders", "Orders ", true)]
    [InlineData("Orders", "Order", false)]
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

    [Fact(DisplayName = "ToStoredForm は前後の空白だけを除き綴りを変えない（null は空文字）")]
    public void ToStoredForm_TrimsOnly()
    {
        SchemaNames.ToStoredForm("  OrDers ").Should().Be("OrDers");
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
