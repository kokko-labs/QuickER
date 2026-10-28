using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストのエンジンの種の指紋（<see cref="SeedFingerprint"/> / <see cref="FingerprintFile"/>）を固定する
/// </summary>
/// <remarks>
/// 照合の範囲（普段の既定の種の数＝全数・夜間の既定の種の数＝記録にある種の全部・個別の指定＝重なりだけ）と、
/// 記録の書き直しを普段の既定の種の数に限ることを確かめる。
/// </remarks>
public sealed class SeedFingerprintTests : IDisposable
{
    private static readonly SeedSettings Settings = new(
        "QUICKER_TEST_SEEDS",
        3,
        5,
        singleVariable: "QUICKER_TEST_SEED"
    );

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "QuickER.Tests",
        "fingerprints-" + Guid.NewGuid().ToString("N")
    );

    public SeedFingerprintTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    private static SeedPlan Plan(params (string Name, string Value)[] values) =>
        SeedSource.Resolve(Settings, name => values.FirstOrDefault(v => v.Name == name).Value);

    private static Dictionary<int, string> Prints(IEnumerable<int> seeds, int changed = -1) =>
        seeds.ToDictionary(
            seed => seed,
            seed => SeedFingerprint.Of(seed == changed ? $"changed {seed}" : $"input {seed}")
        );

    [Fact(DisplayName = "同じ要約は同じ指紋になり、改行の違いは指紋を変えず、内容の違いは変える")]
    public void Of_IsStableAndLineEndingInsensitive()
    {
        var lf = SeedFingerprint.Of("a\nb");

        lf.Should().HaveLength(SeedFingerprint.Length).And.MatchRegex("^[0-9a-f]+$");
        SeedFingerprint.Of("a\nb").Should().Be(lf);
        SeedFingerprint.Of("a\r\nb").Should().Be(lf);
        SeedFingerprint.Of("a\rb").Should().Be(lf);
        SeedFingerprint.Of("a\nc").Should().NotBe(lf);
    }

    [Fact(
        DisplayName = "Local の日時は機械の時差を付けずに書き、Utc・Unspecified は往復の書式のまま"
    )]
    public void SummaryOfDateTime_WritesLocalWithoutUtcOffset()
    {
        var ticks = new DateTime(2026, 2, 1, 8, 0, 0).AddTicks(7654321).Ticks;

        SeedSummary
            .Object(new DateTime(ticks, DateTimeKind.Local))
            .Should()
            .Be("DateTime:Local:2026-02-01T08:00:00.7654321");
        SeedSummary
            .Object(new DateTime(ticks, DateTimeKind.Utc))
            .Should()
            .Be("DateTime:2026-02-01T08:00:00.7654321Z");
        SeedSummary
            .Object(new DateTime(ticks, DateTimeKind.Unspecified))
            .Should()
            .Be("DateTime:2026-02-01T08:00:00.7654321");
    }

    [Fact(
        DisplayName = "要約は外側のカルチャに依らずインバリアントの中で作られ、終われば外側のカルチャへ戻る"
    )]
    public void UnderInvariantCulture_PinsCultureAndRestores()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");

            SeedSummary
                .UnderInvariantCulture(() =>
                    $"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}"
                )
                .Should()
                .Be("|");
            CultureInfo.CurrentCulture.Name.Should().Be("ja-JP");
            CultureInfo.CurrentUICulture.Name.Should().Be("ja-JP");
            SeedFingerprint
                .Compute([1], _ => CultureInfo.CurrentUICulture.Name)
                .Should()
                .Equal(new Dictionary<int, string> { [1] = SeedFingerprint.Of("") });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact(
        DisplayName = "普段の既定の種の数は全数を照合する（変わった・記録に無い・計算していない種）"
    )]
    public void RegularDefault_ComparesAll()
    {
        var file = FingerprintFile.Empty().WithEngine("E", Prints([1, 2, 3]));
        var plan = Plan();

        FingerprintFile.CoverageFor(plan).Should().Be(FingerprintCoverage.All);
        file.Compare("E", Prints([1, 2, 3]), FingerprintCoverage.All).IsEmpty.Should().BeTrue();

        var diff = file.Compare("E", Prints([2, 3, 4], changed: 3), FingerprintCoverage.All);
        diff.Changed.Should().Equal(3);
        diff.NotRecorded.Should().Equal(4);
        diff.NotComputed.Should().Equal(1);
    }

    [Fact(
        DisplayName = "夜間の既定の種の数は、記録にある種がすべて計算されて一致すればよく、記録に無い種は見ない"
    )]
    public void NightlyDefault_ComparesRecordedSeeds()
    {
        var file = FingerprintFile.Empty().WithEngine("E", Prints([1, 2, 3]));
        var plan = Plan((SeedSource.ProfileVariable, "nightly"));

        FingerprintFile.CoverageFor(plan).Should().Be(FingerprintCoverage.RecordedSeeds);
        file.Compare("E", Prints(plan.Seeds), FingerprintCoverage.RecordedSeeds)
            .IsEmpty.Should()
            .BeTrue();

        var diff = file.Compare(
            "E",
            Prints([2, 3, 4, 5], changed: 2),
            FingerprintCoverage.RecordedSeeds
        );
        diff.Changed.Should().Equal(2);
        diff.NotRecorded.Should().BeEmpty();
        diff.NotComputed.Should().Equal(1);
    }

    [Fact(DisplayName = "単独の種・個別の数の指定は、計算した種のうち記録にあるものだけを照合する")]
    public void PartialRuns_CompareIntersection()
    {
        var file = FingerprintFile.Empty().WithEngine("E", Prints([1, 2, 3]));

        FingerprintFile
            .CoverageFor(Plan(("QUICKER_TEST_SEED", "2")))
            .Should()
            .Be(FingerprintCoverage.Intersection);
        FingerprintFile
            .CoverageFor(Plan(("QUICKER_TEST_SEEDS", "9")))
            .Should()
            .Be(FingerprintCoverage.Intersection);

        var diff = file.Compare("E", Prints([2, 9], changed: 2), FingerprintCoverage.Intersection);
        diff.Changed.Should().Equal(2);
        diff.NotRecorded.Should().BeEmpty();
        diff.NotComputed.Should().BeEmpty();
        file.Compare("E", Prints([2, 9]), FingerprintCoverage.Intersection)
            .IsEmpty.Should()
            .BeTrue();
    }

    [Fact(DisplayName = "保存した記録は並びがそろい LF で、読み直すと同じ内容になる")]
    public void SaveAndLoad_RoundTrips()
    {
        var path = PathOf("engine-fingerprints.json");
        var file = FingerprintFile
            .Empty()
            .WithEngine("SyncPlan/sqlite", new Dictionary<int, string> { [10] = "b", [2] = "a" })
            .WithEngine("Dsl", new Dictionary<int, string> { [1] = "c" });

        file.Save(path);
        var text = File.ReadAllText(path);
        var loaded = FingerprintFile.Load(path);

        text.Should().NotContain("\r");
        text.IndexOf("\"Dsl\"", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("\"SyncPlan/sqlite\"", StringComparison.Ordinal));
        text.IndexOf("\"2\"", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("\"10\"", StringComparison.Ordinal));
        File.ReadAllBytes(path).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        loaded.Serialize().Should().Be(text);
        loaded
            .For("SyncPlan/sqlite")
            .Should()
            .BeEquivalentTo(new Dictionary<int, string> { [2] = "a", [10] = "b" });
        FingerprintFile.Load(PathOf("missing.json")).Engines.Should().BeEmpty();
    }

    [Fact(DisplayName = "照合は差があれば種を名指しして赤になり、一致すれば通る")]
    public void Verify_ThrowsOnMismatch()
    {
        var path = PathOf("verify.json");
        FingerprintFile.Empty().WithEngine("E", Prints([1, 2, 3])).Save(path);

        FluentActions
            .Invoking(() =>
                FingerprintFile.Verify(path, "E", Prints([1, 2, 3]), Plan(), regenerate: false)
            )
            .Should()
            .NotThrow();
        FluentActions
            .Invoking(() =>
                FingerprintFile.Verify(
                    path,
                    "E",
                    Prints([1, 2, 3], changed: 2),
                    Plan(),
                    regenerate: false
                )
            )
            .Should()
            .Throw<XunitException>()
            .WithMessage("*指紋が変わった種 1 件: 2*");
    }

    [Fact(
        DisplayName = "記録の書き直しは普段の既定の種の数で流したときだけ許し、ほかのエンジンの記録は残す"
    )]
    public void Verify_RegeneratesOnlyAtRegularDefault()
    {
        var path = PathOf("regen.json");
        FingerprintFile
            .Empty()
            .WithEngine("E", Prints([1, 2, 3]))
            .WithEngine("Other", Prints([1]))
            .Save(path);

        FluentActions
            .Invoking(() =>
                FingerprintFile.Verify(
                    path,
                    "E",
                    Prints([1, 2, 3, 4, 5]),
                    Plan((SeedSource.ProfileVariable, "nightly")),
                    regenerate: true
                )
            )
            .Should()
            .Throw<InvalidOperationException>();
        FluentActions
            .Invoking(() =>
                FingerprintFile.Verify(
                    path,
                    "E",
                    Prints([2]),
                    Plan(("QUICKER_TEST_SEED", "2")),
                    regenerate: true
                )
            )
            .Should()
            .Throw<InvalidOperationException>();

        FingerprintFile.Verify(path, "E", Prints([1, 2, 3], changed: 1), Plan(), regenerate: true);

        var loaded = FingerprintFile.Load(path);
        loaded
            .Compare("E", Prints([1, 2, 3], changed: 1), FingerprintCoverage.All)
            .IsEmpty.Should()
            .BeTrue();
        loaded.For("Other").Should().HaveCount(1);
    }
}
