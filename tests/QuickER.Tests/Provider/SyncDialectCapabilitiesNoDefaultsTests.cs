using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の能力の宣言（<see cref="SyncDialectCapabilities"/>）が既定値を持たないことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 既定値があると、方言が宣言を書き忘れた項目は既定値のまま黙って動く（新しい項目を足したとき、既存の方言が
/// 何も言わずに既定値の側へ倒れる）。全プロパティを <c>required</c> にして、書き忘れをビルドで止める。
/// </para>
/// <para>
/// 見るのは 2 つ。(1) 公開のインスタンスプロパティがすべて <c>required</c>（<see cref="RequiredMemberAttribute"/>）。
/// (2) ソースのプロパティ宣言が初期化子を持たない（<c>required</c> と初期化子は両立し、既定値が残っても
/// ビルドは通るため、構文木で確かめる）。
/// </para>
/// <para>(2) はソースの規則の表へ移した（SourceRuleTests の規則 <c>sync-capabilities-initializers</c>）。このクラスは (1) の反射だけを持つ。</para>
/// </remarks>
public sealed class SyncDialectCapabilitiesNoDefaultsTests
{
    [Fact(DisplayName = "能力の宣言の公開プロパティはすべて required")]
    public void AllProperties_AreRequired()
    {
        var notRequired = typeof(SyncDialectCapabilities)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<RequiredMemberAttribute>() is null)
            .Select(property => property.Name)
            .ToList();

        notRequired.Should().BeEmpty("方言の宣言に既定値を持たせない（規則 2）");
    }
}
