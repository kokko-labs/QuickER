using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 因子の棚卸しの枠組み（<see cref="InventoryAxis"/>・<see cref="InventoryExclusion"/>・<see cref="FactorInventoryGuard"/>・
/// <see cref="FactorInventoryAssignment"/>）を、テスト用の列挙と小さな次元表で固定する
/// </summary>
public sealed class FactorInventoryTests
{
    private const string Reason = "テスト用の次元表にまだ水準として載せていない";

    /// <summary>軸にする列挙</summary>
    private enum Color
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>値を 1 つ足した列挙（製品の列挙に値を足した場合の代わり）</summary>
    private enum ColorPlusOne
    {
        Red,
        Green,
        Blue,
        Violet,
    }

    /// <summary>名前が同じで別の型の列挙（生成フィクスチャの写しの代わり）</summary>
    private enum ColorCopy
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>プロパティの軸にする型</summary>
    private sealed class Options
    {
        public bool Enabled { get; init; }

        public bool Verbose { get; init; }

        public string Name { get; init; } = "";

        public bool IsDerived => Enabled && Verbose;
    }

    /// <summary>Red と Green を水準の Source に、Options.Enabled を因子の Source にした台帳</summary>
    private static IReadOnlyList<EngineDescriptor> Registry<TEnum>(params TEnum[] sourced)
        where TEnum : struct, Enum
    {
        var table = new DimensionTable(
            EngineIds.Guard,
            [
                new Factor(
                    "Color",
                    sourced
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList()
                ),
                new Factor(
                    "Flag",
                    [new Level("On"), new Level("Off")],
                    Source: AxisMember.Property(typeof(Options), nameof(Options.Enabled))
                ),
            ]
        );
        return
        [
            new EngineDescriptor(
                EngineIds.Guard,
                "Inventory",
                "棚卸しのテスト",
                table,
                0,
                ["tests/QuickER.Tests/TestSupport/FactorInventoryTests.cs"],
                []
            ),
        ];
    }

    private static IReadOnlyList<InventoryAxis> Axes<TEnum>()
        where TEnum : struct, Enum =>
        [
            InventoryAxis.Enum<TEnum>("Color"),
            InventoryAxis.Properties(
                "Options",
                typeof(Options),
                property => property.PropertyType == typeof(bool) && property.CanWrite
            ),
        ];

    private static InventoryExclusion Exclude(string axis, string member) =>
        new(axis, member, EngineIds.Guard, "P2", Reason);

    /// <summary>Blue と Options.Verbose を除外した、整った除外の一覧</summary>
    private static List<InventoryExclusion> CleanExclusions() =>
        [Exclude("Color", nameof(Color.Blue)), Exclude("Options", nameof(Options.Verbose))];

    private static IReadOnlyList<string> Problems(
        IReadOnlyList<InventoryAxis> axes,
        IReadOnlyList<EngineDescriptor> registry,
        IReadOnlyList<InventoryExclusion> exclusions
    ) => FactorInventoryGuard.Problems(axes, registry, exclusions);

    [Fact(
        DisplayName = "軸の項目は列挙の全値と、フィルタに当たるインスタンスのプロパティ（宣言の順）"
    )]
    public void Axes_ReflectMembers()
    {
        var axes = Axes<Color>();

        axes[0].Members.Select(member => member.Member).Should().Equal("Red", "Green", "Blue");
        axes[0].Members.Should().OnlyContain(member => member.DeclaringType == typeof(Color));
        axes[1].Members.Select(member => member.Member).Should().Equal("Enabled", "Verbose");
        InventoryAxis
            .Properties("All", typeof(Options))
            .Members.Select(member => member.Member)
            .Should()
            .Equal("Enabled", "Verbose", "Name", "IsDerived");
    }

    [Fact(DisplayName = "すべての項目が Source か除外のちょうど一方なら問題は無い")]
    public void Clean_HasNoProblems()
    {
        Problems(Axes<Color>(), Registry(Color.Red, Color.Green), CleanExclusions())
            .Should()
            .BeEmpty();
        FluentActions
            .Invoking(() =>
                FactorInventoryGuard.Verify(
                    Axes<Color>(),
                    Registry(Color.Red, Color.Green),
                    CleanExclusions()
                )
            )
            .Should()
            .NotThrow();
    }

    [Fact(DisplayName = "除外の行を 1 つ消すと、載っても除外されてもいない項目として赤になる")]
    public void RemovedExclusion_IsRed()
    {
        var exclusions = CleanExclusions().Where(e => e.Member != nameof(Color.Blue)).ToList();

        Problems(Axes<Color>(), Registry(Color.Red, Color.Green), exclusions)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("Color.Blue: どのエンジンの次元表の Source にも無く");
        FluentActions
            .Invoking(() =>
                FactorInventoryGuard.Verify(
                    Axes<Color>(),
                    Registry(Color.Red, Color.Green),
                    exclusions
                )
            )
            .Should()
            .Throw<XunitException>()
            .WithMessage("*因子の棚卸しが整っていない（1 件）*Color.Blue*");
    }

    [Fact(DisplayName = "列挙に値を足すと、足した値が載っても除外されてもいない項目として赤になる")]
    public void AddedEnumMember_IsRed()
    {
        Problems(
                Axes<ColorPlusOne>(),
                Registry(ColorPlusOne.Red, ColorPlusOne.Green),
                CleanExclusions()
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("Color.Violet: どのエンジンの次元表の Source にも無く");
    }

    [Fact(
        DisplayName = "プロパティを足すと、足したプロパティが赤になる（フィルタに当たらないものは対象外）"
    )]
    public void AddedProperty_IsRed()
    {
        var exclusions = CleanExclusions().Where(e => e.Axis != "Options").ToList();

        Problems(Axes<Color>(), Registry(Color.Red, Color.Green), exclusions)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("Options.Verbose: どのエンジンの次元表の Source にも無く");
    }

    [Fact(DisplayName = "Source に載った項目が除外にも残っていると赤になる")]
    public void SourcedAndExcluded_IsRed()
    {
        var exclusions = CleanExclusions();
        exclusions.Add(Exclude("Color", nameof(Color.Red)));
        exclusions.Add(Exclude("Options", nameof(Options.Enabled)));

        Problems(Axes<Color>(), Registry(Color.Red, Color.Green), exclusions)
            .Should()
            .BeEquivalentTo(
                "Color.Red: エンジンの次元表の Source に載ったので除外の一覧から消す",
                "Options.Enabled: エンジンの次元表の Source に載ったので除外の一覧から消す"
            );
    }

    [Fact(DisplayName = "名前が同じでも別の型の Source は載ったとみなさない")]
    public void SourceOfOtherType_DoesNotCount()
    {
        Problems(Axes<Color>(), Registry(ColorCopy.Red, ColorCopy.Green), CleanExclusions())
            .Should()
            .BeEquivalentTo(
                "Color.Red: どのエンジンの次元表の Source にも無く、除外の一覧にも無い（次元表の水準・因子の Source に載せるか、受け持つエンジン・区切り・理由つきで除外する）",
                "Color.Green: どのエンジンの次元表の Source にも無く、除外の一覧にも無い（次元表の水準・因子の Source に載せるか、受け持つエンジン・区切り・理由つきで除外する）"
            );
    }

    [Fact(DisplayName = "知らない軸・軸に無い項目・重複を指す除外は赤になる")]
    public void UnknownOrDuplicateExclusion_IsRed()
    {
        var exclusions = CleanExclusions();
        exclusions.Add(Exclude("Shape", "Circle"));
        exclusions.Add(Exclude("Color", "Purple"));
        exclusions.Add(Exclude("Color", nameof(Color.Blue)));

        var problems = Problems(Axes<Color>(), Registry(Color.Red, Color.Green), exclusions);

        problems.Should().HaveCount(3);
        problems.Should().Contain("除外 Shape.Circle: 知らない軸 Shape");
        problems.Should().Contain("除外 Color.Purple: 軸 Color に項目 Purple が無い");
        problems.Should().Contain("除外 Color.Blue: 同じ項目の除外が重複している");
    }

    [Theory(
        DisplayName = "整っていない除外（知らないエンジン・空や知らない区切り・空や仮置きの理由）は赤になる"
    )]
    [InlineData("Unknown", "P2", Reason, "知らないエンジンの ID Unknown")]
    [InlineData(EngineIds.Guard, "", Reason, "区切りが空")]
    [InlineData(EngineIds.Guard, "P9", Reason, "知らない区切り P9")]
    [InlineData(EngineIds.Guard, "P2", " ", "理由が空")]
    [InlineData(EngineIds.Guard, "P2", "TODO", "理由「TODO」が具体的でない")]
    [InlineData(EngineIds.Guard, "P2", "後で", "理由「後で」が具体的でない")]
    [InlineData(EngineIds.Guard, "P2", "TBD", "理由「TBD」が具体的でない")]
    [InlineData(EngineIds.Guard, "P2", "まだ載せない", "理由「まだ載せない」が具体的でない")]
    public void MalformedExclusion_IsRed(string owner, string stage, string reason, string expected)
    {
        var exclusions = CleanExclusions().Where(e => e.Member != nameof(Color.Blue)).ToList();
        exclusions.Add(new InventoryExclusion("Color", nameof(Color.Blue), owner, stage, reason));

        Problems(Axes<Color>(), Registry(Color.Red, Color.Green), exclusions)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith($"除外 Color.Blue: {expected}");
    }

    [Fact(DisplayName = "同じ名前の軸の重複と、軸の型と違う型の項目は赤になる")]
    public void MalformedAxes_AreRed()
    {
        IReadOnlyList<InventoryAxis> axes =
        [
            .. Axes<Color>(),
            InventoryAxis.Enum<ColorCopy>("Color"),
            new InventoryAxis("Mixed", typeof(Color), [new AxisMember(typeof(ColorCopy), "Red")]),
        ];

        var problems = Problems(axes, Registry(Color.Red, Color.Green), CleanExclusions());

        problems.Should().Contain("軸 Color が重複している");
        problems
            .Should()
            .Contain(problem =>
                problem.StartsWith("Mixed.Red: 項目の型", StringComparison.Ordinal)
            );
    }

    [Fact(
        DisplayName = "軸の割り当ては設計 2.9 の 13 軸を 1 回ずつ、2 つのアセンブリのどちらかへ割り当てる"
    )]
    public void Assignment_CoversThirteenAxesOnce()
    {
        var names = FactorInventoryAssignment.Assignments.Select(pair => pair.Axis).ToList();

        names
            .Should()
            .BeEquivalentTo(
                "SyncDialectCapabilities",
                "SyncConflictReason",
                "SyncMode",
                "SyncConflictPolicy",
                "SyncLocalRowChangeReason",
                "SyncOptions",
                "ConcurrencyMode",
                "SaveOperation",
                "ForeignKeyReferentialAction",
                "RelationshipType",
                "CodeGenerationOptions",
                "CanonicalTypeKind",
                "SchemaImportWarningKind"
            );
        names.Should().OnlyHaveUniqueItems();
        FactorInventoryAssignment
            .Assignments.Select(pair => pair.Assembly)
            .Distinct()
            .Should()
            .BeEquivalentTo(FactorInventoryAssignment.Tests, FactorInventoryAssignment.Integration);
        FactorInventoryAssignment
            .AxesFor(FactorInventoryAssignment.Tests)
            .Concat(FactorInventoryAssignment.AxesFor(FactorInventoryAssignment.Integration))
            .Should()
            .BeEquivalentTo(names);
        FluentActions
            .Invoking(() => FactorInventoryAssignment.AxesFor("QuickER.Gui"))
            .Should()
            .Throw<ArgumentException>();
    }
}
