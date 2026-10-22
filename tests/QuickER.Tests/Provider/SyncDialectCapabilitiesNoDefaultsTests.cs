using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Provider;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

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

    [Fact(DisplayName = "能力の宣言のプロパティは初期化子（既定値）を持たない")]
    public void Properties_HaveNoInitializers()
    {
        var path = Path.Combine(
            NeutralResxFiles.FindRepositoryRoot(),
            "src",
            "QuickER.Provider",
            "SyncDialectCapabilities.cs"
        );
        var variants = ConditionalSourceParse.Parse(
            File.ReadAllText(path),
            cancellationToken: TestContext.Current.CancellationToken
        );
        var withInitializer = ConditionalSourceParse
            .DescendantNodes(variants, TestContext.Current.CancellationToken)
            .OfType<PropertyDeclarationSyntax>()
            .Where(property =>
                property.Parent is RecordDeclarationSyntax record
                && record.Identifier.ValueText == nameof(SyncDialectCapabilities)
            )
            .Where(property => property.Initializer is not null)
            .Select(property => property.Identifier.ValueText)
            .ToList();

        withInitializer.Should().BeEmpty("方言の宣言に既定値を持たせない（規則 2）");
    }
}
