using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;
using static QuickER.Tests.TestSupport.DimensionTableTests;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// エンジンの台帳の整合の検査（<see cref="EngineRegistryChecks"/>）と、エンジンの ID の正本（<see cref="EngineIds"/>）を固定する
/// </summary>
public sealed class EngineRegistryChecksTests
{
    private const string ExistingFile = "tests/QuickER.Tests/TestSupport/Engines/KnownFailures.cs";
    private const string ExistingFolder = "tests/QuickER.Tests/TestSupport/Engines";

    private static readonly string Root = EngineRepository.Root();

    private static DimensionTable Dimensions(string engineId) =>
        new(engineId, [F("A", "a1"), F("B", "b1")]);

    private static EngineDescriptor Descriptor(
        string id = EngineIds.Dsl,
        string part = "main",
        IReadOnlyList<string>? sources = null,
        IReadOnlyList<string>? covers = null,
        string? dimensionsId = null
    ) =>
        new(
            id,
            part,
            "表示の名前",
            Dimensions(dimensionsId ?? (string.IsNullOrWhiteSpace(id) ? "X" : id)),
            3,
            sources ?? [ExistingFile],
            covers ?? [ExistingFolder]
        );

    [Fact(DisplayName = "エンジンの ID は 13 個で重複しない")]
    public void EngineIds_AreThirteenDistinct()
    {
        EngineIds.All.Should().HaveCount(13).And.OnlyHaveUniqueItems();
        EngineIds.All[0].Should().Be(EngineIds.SyncModel);
        EngineIds.All[^1].Should().Be(EngineIds.Guard);
    }

    [Fact(DisplayName = "リポジトリの根はソリューションのファイルを持つ")]
    public void Root_ContainsSolution()
    {
        File.Exists(Path.Combine(Root, EngineRepository.SolutionFileName)).Should().BeTrue();
    }

    [Fact(DisplayName = "整った台帳には問題が無い")]
    public void ValidRegistry_HasNoProblems()
    {
        var descriptors = new[]
        {
            Descriptor(),
            Descriptor(part: "other"),
            Descriptor(EngineIds.Guard),
        };

        EngineRegistryChecks.Problems(descriptors, Root).Should().BeEmpty();
        FluentActions
            .Invoking(() => EngineRegistryChecks.Verify(descriptors, Root))
            .Should()
            .NotThrow();
    }

    [Fact(DisplayName = "知らない ID・ID と部分の重複・次元表の ID の食い違いを名指しする")]
    public void IdProblems_AreNamed()
    {
        var problems = EngineRegistryChecks.Problems(
            [
                Descriptor("Unknown"),
                Descriptor(),
                Descriptor(),
                Descriptor(EngineIds.Ai, dimensionsId: EngineIds.Dsl),
            ],
            Root
        );

        string.Join("\n", problems)
            .Should()
            .ContainAll(
                "Unknown/main: 知らないエンジンの ID Unknown",
                "Dsl/main: エンジンの ID と部分の組が重複している",
                "Ai/main: 次元表のエンジンの ID Dsl が台帳の ID と違う"
            );
        FluentActions
            .Invoking(() => EngineRegistryChecks.Verify([Descriptor("Unknown")], Root))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*知らないエンジンの ID Unknown*");
    }

    [Theory(
        DisplayName = "パスは相対・区切りが /・.. を含まない・存在する、の 4 つを満たさなければ名指しする"
    )]
    [InlineData("tests/QuickER.Tests/NoSuchFile.cs", "が無い")]
    [InlineData("C:/QuickER/CLAUDE.md", "が相対パスでない")]
    [InlineData("/CLAUDE.md", "が相対パスでない")]
    [InlineData("tests\\QuickER.Tests", "の区切りが / でない")]
    [InlineData("tests/../CLAUDE.md", "に .. がある")]
    public void PathProblems_AreNamed(string path, string expected)
    {
        EngineRegistryChecks
            .Problems([Descriptor(sources: [ExistingFile, path])], Root)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(path)
            .And.Contain(expected);
        EngineRegistryChecks
            .Problems([Descriptor(covers: [path])], Root)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("対象の範囲");
    }

    [Fact(DisplayName = "テストのソースが無い行と、同じパスの重複を名指しする")]
    public void EmptySourcesAndDuplicates_AreNamed()
    {
        string.Join(
                "\n",
                EngineRegistryChecks.Problems(
                    [
                        Descriptor(sources: []),
                        Descriptor(part: "dup", covers: [ExistingFolder, ExistingFolder]),
                    ],
                    Root
                )
            )
            .Should()
            .ContainAll(
                "Dsl/main: テストのソースが無い",
                $"Dsl/dup: 対象の範囲の {ExistingFolder} が重複している"
            );
    }
}
