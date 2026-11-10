using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration の生成器の作らない形の宣言（<see cref="NotGeneratedDeclaration"/>）の照合：宣言の構造と、Integration の台帳の種を使うエンジンの
/// 部分がどれも宣言を持つこと
/// </summary>
/// <remarks>
/// 受け持つ守りの照合（影響の地図の名前・テストの名前・行 Id）は QuickER.Tests の <c>NotGeneratedGuardTests</c> が、Compile のリンクで入れた
/// Integration の宣言について行う（照合の関数と地図は QuickER.Tests にしか無い）。
/// </remarks>
public sealed class NotGeneratedGuardTests
{
    /// <summary>このアセンブリの宣言（QuickER.Tests の DslFuzz・NameFuzz の宣言をリンクで含む）</summary>
    private static IReadOnlyList<NotGeneratedDeclaration> Declarations { get; } =
    [
        .. NotGeneratedGuard
            .InAssembly(typeof(NotGeneratedGuardTests).Assembly)
            .Select(found => found.Declaration),
    ];

    [Fact(DisplayName = "[作らない形] Integration の宣言は 1 つ以上あり、構造の問題が無い")]
    public void Declarations_AreWellFormed()
    {
        Declarations.Should().NotBeEmpty("宣言を反射で見つけること");
        NotGeneratedGuard.Verify(
            "作らない形の宣言が整っていない",
            NotGeneratedGuard.Problems(Declarations)
        );
    }

    [Fact(
        DisplayName = "[作らない形] Integration の台帳の種を使うエンジンの部分は、どれも宣言を持つ"
    )]
    public void SeedUsingParts_HaveDeclarations()
    {
        var seedUsing = NotGeneratedGuard.SeedUsingParts(
            EngineRegistry.All,
            NotGeneratedGuard.SourceReader(EngineRepository.Root())
        );

        seedUsing.Should().Contain(ProtectionNames.EnginePart(EngineIds.SyncPlan, "Fuzz"));
        NotGeneratedGuard
            .UndeclaredParts(Declarations, seedUsing)
            .Should()
            .BeEmpty(
                "種を使うエンジンの部分は生成器の作らない形の宣言を持つ（{生成器}NotGenerated.cs を足す）"
            );
    }
}
