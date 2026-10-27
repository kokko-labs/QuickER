using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストのエンジンの種の決め方（<see cref="SeedSource"/>）の解析の規則を固定する
/// </summary>
/// <remarks>
/// 環境変数は辞書で渡す（プロセスの環境変数を書き換えない＝並列のテストに影響しない）。
/// </remarks>
public sealed class SeedSourceTests
{
    private static readonly SeedSettings Settings = new(
        "QUICKER_TEST_SEEDS",
        regularCount: 4,
        nightlyCount: 10,
        firstSeed: 1,
        singleVariable: "QUICKER_TEST_SEED"
    );

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    [Fact(DisplayName = "変数が無ければ普段の既定の種の数を開始番号から流し、既定の数として扱う")]
    public void NoVariables_UseRegularDefault()
    {
        var plan = SeedSource.Resolve(Settings, Env());

        plan.Seeds.Should().Equal(1, 2, 3, 4);
        plan.IsDefaultCount.Should().BeTrue();
        plan.IsSingle.Should().BeFalse();
        plan.Profile.Should().Be(EngineProfile.Regular);
        plan.Warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "開始番号 0 のエンジンは 0 から流す")]
    public void FirstSeedZero_StartsAtZero()
    {
        var settings = new SeedSettings("QUICKER_TEST_SEEDS", 3, 3, firstSeed: 0);

        SeedSource.Resolve(settings, Env()).Seeds.Should().Equal(0, 1, 2);
    }

    [Fact(
        DisplayName = "数の変数を指定すればその数を流し、既定の数ではない（既定より少なくても指定どおり）"
    )]
    public void CountVariable_IsUsedAsGiven()
    {
        var plan = SeedSource.Resolve(Settings, Env(("QUICKER_TEST_SEEDS", "2")));

        plan.Seeds.Should().Equal(1, 2);
        plan.IsDefaultCount.Should().BeFalse();
        plan.Warnings.Should().BeEmpty();
    }

    [Theory(DisplayName = "数の変数が読めない・0 以下なら既定へ倒し、警告を残す")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("1,000")]
    [InlineData("99999999999")]
    public void UnreadableCount_FallsBackToDefaultWithWarning(string text)
    {
        var plan = SeedSource.Resolve(Settings, Env(("QUICKER_TEST_SEEDS", text)));

        plan.Seeds.Should().Equal(1, 2, 3, 4);
        plan.IsDefaultCount.Should().BeTrue();
        plan.Warnings.Should().ContainSingle().Which.Should().Contain("QUICKER_TEST_SEEDS");
    }

    [Fact(DisplayName = "数の変数の全角の数字は読まない（インバリアントの解析は ASCII だけ）")]
    public void FullWidthDigits_AreNotRead()
    {
        var plan = SeedSource.Resolve(Settings, Env(("QUICKER_TEST_SEEDS", "５")));

        plan.Seeds.Should().HaveCount(4);
        plan.Warnings.Should().ContainSingle();
    }

    [Fact(DisplayName = "単独の種の変数があればその種だけを流し、数の変数と夜間より優先する")]
    public void SingleSeed_TakesPrecedence()
    {
        var plan = SeedSource.Resolve(
            Settings,
            Env(
                ("QUICKER_TEST_SEED", "75"),
                ("QUICKER_TEST_SEEDS", "2"),
                (SeedSource.ProfileVariable, "nightly")
            )
        );

        plan.Seeds.Should().Equal(75);
        plan.IsSingle.Should().BeTrue();
        plan.IsDefaultCount.Should().BeFalse();
        plan.Profile.Should().Be(EngineProfile.Nightly);
    }

    [Fact(DisplayName = "単独の種の 0 は有効")]
    public void SingleSeedZero_IsValid()
    {
        var plan = SeedSource.Resolve(Settings, Env(("QUICKER_TEST_SEED", "0")));

        plan.Seeds.Should().Equal(0);
        plan.IsSingle.Should().BeTrue();
    }

    [Fact(DisplayName = "単独の種が読めなければ無視して警告を残し、数の決め方へ進む")]
    public void UnreadableSingleSeed_IsIgnoredWithWarning()
    {
        var plan = SeedSource.Resolve(
            Settings,
            Env(("QUICKER_TEST_SEED", "x"), ("QUICKER_TEST_SEEDS", "3"))
        );

        plan.IsSingle.Should().BeFalse();
        plan.Seeds.Should().Equal(1, 2, 3);
        plan.Warnings.Should().ContainSingle().Which.Should().Contain("QUICKER_TEST_SEED");
    }

    [Fact(DisplayName = "単独の種の変数を持たないエンジンは、その名前の変数を読まない")]
    public void WithoutSingleVariable_SingleIsNeverUsed()
    {
        var settings = new SeedSettings("QUICKER_TEST_SEEDS", 4, 4);
        var plan = SeedSource.Resolve(settings, Env(("QUICKER_TEST_SEED", "75")));

        plan.IsSingle.Should().BeFalse();
        plan.Seeds.Should().HaveCount(4);
    }

    [Theory(DisplayName = "夜間のプロファイルは夜間の既定の種の数を流し、既定の数として扱う")]
    [InlineData("nightly")]
    [InlineData("Nightly")]
    [InlineData(" NIGHTLY ")]
    public void Nightly_UsesNightlyDefault(string value)
    {
        var plan = SeedSource.Resolve(Settings, Env((SeedSource.ProfileVariable, value)));

        plan.Profile.Should().Be(EngineProfile.Nightly);
        plan.Seeds.Should().HaveCount(10);
        plan.IsDefaultCount.Should().BeTrue();
        plan.Warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "夜間の種の列は普段の種の列を先頭に含む（同じ開始番号から連番）")]
    public void NightlySeeds_StartWithRegularSeeds()
    {
        var settings = new SeedSettings("QUICKER_TEST_SEEDS", 3, 7, firstSeed: 5);
        var regular = SeedSource.Resolve(settings, Env());
        var nightly = SeedSource.Resolve(settings, Env((SeedSource.ProfileVariable, "nightly")));

        nightly.Seeds.Take(regular.Seeds.Count).Should().Equal(regular.Seeds);
        nightly.Seeds.Should().Equal(5, 6, 7, 8, 9, 10, 11);
    }

    [Fact(DisplayName = "夜間でも個別の数の変数が優先し、既定の数ではない")]
    public void Nightly_CountVariableWins()
    {
        var plan = SeedSource.Resolve(
            Settings,
            Env((SeedSource.ProfileVariable, "nightly"), ("QUICKER_TEST_SEEDS", "2"))
        );

        plan.Profile.Should().Be(EngineProfile.Nightly);
        plan.Seeds.Should().Equal(1, 2);
        plan.IsDefaultCount.Should().BeFalse();
    }

    [Fact(DisplayName = "夜間で数の変数が読めなければ夜間の既定へ倒す")]
    public void Nightly_UnreadableCount_FallsBackToNightlyDefault()
    {
        var plan = SeedSource.Resolve(
            Settings,
            Env((SeedSource.ProfileVariable, "nightly"), ("QUICKER_TEST_SEEDS", "0"))
        );

        plan.Seeds.Should().HaveCount(10);
        plan.IsDefaultCount.Should().BeTrue();
        plan.Warnings.Should().ContainSingle();
    }

    [Fact(DisplayName = "知らないプロファイルは普段へ倒して警告を残し、空は普段として扱う")]
    public void UnknownProfile_FallsBackToRegularWithWarning()
    {
        var unknown = SeedSource.Resolve(Settings, Env((SeedSource.ProfileVariable, "weekly")));
        var empty = SeedSource.Resolve(Settings, Env((SeedSource.ProfileVariable, "")));

        unknown.Profile.Should().Be(EngineProfile.Regular);
        unknown.Warnings.Should().ContainSingle().Which.Should().Contain("weekly");
        empty.Profile.Should().Be(EngineProfile.Regular);
        empty.Warnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "再現の手順は単独の種の変数があればそれを、無ければ数の変数を名指しする")]
    public void ReproductionFor_NamesVariable()
    {
        SeedSource
            .Resolve(Settings, Env())
            .ReproductionFor(75)
            .Should()
            .Be("`QUICKER_TEST_SEED=75` で 1 種だけ流す");

        var noSingle = new SeedSettings("QUICKER_TEST_SEEDS", 4, 4, firstSeed: 0);
        SeedSource
            .Resolve(noSingle, Env())
            .ReproductionFor(7)
            .Should()
            .Be("`QUICKER_TEST_SEEDS=8` で種 0〜7 を流す");

        var fromOne = new SeedSettings("QUICKER_TEST_SEEDS", 4, 4);
        FluentActions
            .Invoking(() => SeedSource.Resolve(fromOne, Env()).ReproductionFor(0))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "種の決め方の値を検査する（数・開始番号・夜間の数・変数名）")]
    public void Settings_AreValidated()
    {
        FluentActions
            .Invoking(() => new SeedSettings("X", 0, 0))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => new SeedSettings("X", 5, 4))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => new SeedSettings("X", 1, 1, firstSeed: -1))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => new SeedSettings(" ", 1, 1))
            .Should()
            .Throw<ArgumentException>();
        FluentActions
            .Invoking(() => new SeedSettings("X", 1, 1, singleVariable: ""))
            .Should()
            .Throw<ArgumentException>();
    }
}
