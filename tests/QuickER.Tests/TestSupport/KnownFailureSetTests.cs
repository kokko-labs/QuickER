using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストのエンジンの既知の失敗の一覧（<see cref="KnownFailureSet"/>）の照合と構築時の整合を固定する
/// </summary>
/// <remarks>
/// 3 種の赤（載っていない・載っているのに起きない・理由の食い違い）と、乱数の項目の「既定の種の数で流したときだけ起きないを数える」
/// 規則を、それぞれ別のテストで確かめる（判定を 1 つ外すと、その判定のテストだけが赤になる）。
/// </remarks>
public sealed class KnownFailureSetTests
{
    private static readonly SeedSettings Settings = new("QUICKER_TEST_SEEDS", 4, 8);

    private static SeedPlan DefaultPlan(EngineProfile profile = EngineProfile.Regular) =>
        SeedSource.Resolve(
            Settings,
            name =>
                name == SeedSource.ProfileVariable && profile == EngineProfile.Nightly
                    ? "nightly"
                    : null
        );

    private static SeedPlan CountPlan() =>
        SeedSource.Resolve(Settings, name => name == Settings.CountVariable ? "2" : null);

    private static KnownEntry Pending(
        string scope,
        string? signature,
        KnownMatch match = KnownMatch.ExactSignature,
        bool random = false,
        bool reproducible = true,
        string? reasonContains = null
    ) =>
        new(
            "F-1",
            KnownKind.Pending,
            "P1-2",
            null,
            scope,
            match,
            Signature: signature,
            ReasonContains: reasonContains,
            RandomSeeds: random,
            ReproducibleAtDefaultSeeds: reproducible
        );

    private static KnownFailureSet Set(params KnownEntry[] entries) => new("TestEngine", entries);

    [Fact(DisplayName = "一覧が空で失敗も無ければ照合は通る")]
    public void Empty_IsClean()
    {
        var report = Set().Classify([], DefaultPlan());

        report.IsClean.Should().BeTrue();
        FluentActions.Invoking(() => report.AssertClean("t")).Should().NotThrow();
    }

    [Fact(DisplayName = "載っていない失敗は新しい失敗として赤になる")]
    public void UnlistedFailure_IsUnknown()
    {
        var failure = new EngineFailure("scenario-a", "sig", "detail");
        var report = Set().Classify([failure], DefaultPlan());

        report.Unknown.Should().Equal(failure);
        report.IsClean.Should().BeFalse();
        FluentActions
            .Invoking(() => report.AssertClean("t"))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*新しい失敗 1 件: 直すか、直す区切りを決めて一覧に載せる*");
    }

    [Fact(
        DisplayName = "載っているのに起きない決定的なシナリオの項目は赤になる（流した種に依らない）"
    )]
    public void ListedButNotObservedScenario_IsNotObserved()
    {
        var entry = Pending("scenario-a", "sig");
        var set = Set(entry);

        set.Classify([], DefaultPlan()).NotObserved.Should().Equal(entry);
        set.Classify([], CountPlan()).NotObserved.Should().Equal(entry);
        FluentActions
            .Invoking(() => set.Classify([], DefaultPlan()).AssertClean("t"))
            .Should()
            .Throw<XunitException>()
            .WithMessage("*起きなかった項目 1 件: 直ったなら一覧から消す*F-1*");
    }

    [Fact(
        DisplayName = "鍵は当たるが理由の断片を含まない失敗は理由の食い違いとして赤になり、起きなかったへは重ねない"
    )]
    public void WrongReason_IsReasonMismatch()
    {
        var entry = Pending(
            "case-1",
            null,
            KnownMatch.KeyOnly,
            reasonContains: "expected fragment"
        );
        var failure = new EngineFailure("case-1", "any", "another reason");
        var report = Set(entry).Classify([failure], DefaultPlan());

        report
            .ReasonMismatch.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KnownMatchResult(failure, entry));
        report.NotObserved.Should().BeEmpty();
        report.Unknown.Should().BeEmpty();
        FluentActions
            .Invoking(() => report.AssertClean("t"))
            .Should()
            .Throw<XunitException>()
            .WithMessage(
                "*理由の食い違い 1 件*期待した理由: expected fragment*実際: another reason*"
            );
    }

    [Fact(DisplayName = "理由の断片を含む失敗は既知として数える")]
    public void MatchingReason_IsMatched()
    {
        var entry = Pending("case-1", null, KnownMatch.KeyOnly, reasonContains: "frag");
        var failure = new EngineFailure("case-1", "any", "has frag inside");
        var report = Set(entry).Classify([failure], DefaultPlan());

        report.IsClean.Should().BeTrue();
        report.Matched.Should().Equal(new KnownMatchResult(failure, entry));
    }

    [Fact(DisplayName = "乱数の項目は、既定の種の数で流したときだけ起きないを数える")]
    public void RandomEntry_NotObservedOnlyAtDefaultCount()
    {
        var entry = Pending("random", "sig", random: true);
        var set = Set(entry);

        set.Classify([], DefaultPlan()).NotObserved.Should().Equal(entry);
        set.Classify([], DefaultPlan(EngineProfile.Nightly)).NotObserved.Should().Equal(entry);
        set.Classify([], CountPlan()).NotObserved.Should().BeEmpty();
        set.Classify(
                [],
                SeedSource.Resolve(
                    new SeedSettings(
                        "QUICKER_TEST_SEEDS",
                        4,
                        8,
                        singleVariable: "QUICKER_TEST_SEED"
                    ),
                    name => name == "QUICKER_TEST_SEED" ? "3" : null
                )
            )
            .NotObserved.Should()
            .BeEmpty();
    }

    [Fact(DisplayName = "既定の種の数で必ず起きるとは限らない乱数の項目は、起きないを数えない")]
    public void NonReproducibleRandomEntry_IsNeverNotObserved()
    {
        var entry = Pending("random", "sig", random: true, reproducible: false);

        Set(entry).Classify([], DefaultPlan()).NotObserved.Should().BeEmpty();
    }

    [Fact(DisplayName = "乱数の項目は種のある失敗にだけ、シナリオの項目は種の無い失敗にだけ当たる")]
    public void RandomAndScenarioEntries_MatchOnlyTheirKind()
    {
        var random = Pending("s", "sig", random: true);
        var seeded = new EngineFailure("s", "sig", "", Seed: 3);
        var deterministic = new EngineFailure("s", "sig", "");

        Set(random).Classify([deterministic], CountPlan()).Unknown.Should().Equal(deterministic);
        Set(random).Classify([seeded], CountPlan()).IsClean.Should().BeTrue();
        Set(Pending("s", "sig")).Classify([seeded], CountPlan()).Unknown.Should().Equal(seeded);
    }

    [Fact(DisplayName = "4 つの照合の仕方（署名の全文・正規化した署名・述語・範囲だけ）")]
    public void MatchModes()
    {
        var failure = new EngineFailure("s", "upload:items:NotFound", "", Seed: 1);

        Set(Pending("s", "upload:items:NotFound", random: true))
            .Classify([failure], CountPlan())
            .IsClean.Should()
            .BeTrue();
        Set(Pending("s", "upload:items", random: true))
            .Classify([failure], CountPlan())
            .Unknown.Should()
            .ContainSingle();

        var family = new KnownFailureSet(
            "TestEngine",
            [Pending("s", "upload:NotFound", KnownMatch.Family, random: true)],
            signature => signature.Replace(":items", "", StringComparison.Ordinal)
        );
        family.Classify([failure], CountPlan()).IsClean.Should().BeTrue();

        var predicate = Set(
            new KnownEntry(
                "F-1",
                KnownKind.Pending,
                "P1-2",
                null,
                "s",
                KnownMatch.Predicate,
                Predicate: f => f.Signature.EndsWith("NotFound", StringComparison.Ordinal),
                RandomSeeds: true
            )
        );
        predicate.Classify([failure], CountPlan()).IsClean.Should().BeTrue();

        Set(Pending("s", null, KnownMatch.KeyOnly, random: true))
            .Classify([failure], CountPlan())
            .IsClean.Should()
            .BeTrue();
        Set(Pending("other", null, KnownMatch.KeyOnly, random: true))
            .Classify([failure], CountPlan())
            .Unknown.Should()
            .ContainSingle();
    }

    [Fact(DisplayName = "受け持たない範囲の項目は照合も起きなかったの判定もしない")]
    public void OwnsScope_LimitsEntries()
    {
        var sqlite = Pending("sqlite", "sig");
        var mysql = Pending("mysql", "sig");
        var set = Set(sqlite, mysql);
        var failure = new EngineFailure("mysql", "sig", "");

        var report = set.Classify([], DefaultPlan(), scope => scope == "sqlite");
        report.NotObserved.Should().Equal(sqlite);

        set.Classify([failure], DefaultPlan(), scope => scope == "sqlite")
            .Unknown.Should()
            .Equal(failure);
    }

    [Fact(DisplayName = "複数の項目に当たるときは理由の合う先の項目を採る")]
    public void FirstReasonMatchingEntryWins()
    {
        var wrong = Pending("c", null, KnownMatch.KeyOnly, reasonContains: "other");
        var right = new KnownEntry(
            "F-2",
            KnownKind.Pending,
            "P1-2",
            null,
            "c",
            KnownMatch.Predicate,
            Predicate: _ => true,
            ReasonContains: "real"
        );
        var failure = new EngineFailure("c", "x", "real reason");

        var report = Set(wrong, right).Classify([failure], DefaultPlan());

        report.Matched.Should().Equal(new KnownMatchResult(failure, right));
        report.NotObserved.Should().Equal(wrong);
    }

    [Fact(DisplayName = "受け入れた差は理由が要り、直す区切りを持たない")]
    public void AcceptedEntry_RequiresReason()
    {
        var accepted = new KnownEntry(
            "P1",
            KnownKind.Accepted,
            null,
            "インメモリは外部キーを検査しない",
            "s",
            KnownMatch.KeyOnly
        );

        Set(accepted)
            .Classify([new EngineFailure("s", "x", "")], DefaultPlan())
            .IsClean.Should()
            .BeTrue();

        FluentActions
            .Invoking(() => Set(accepted with { Reason = null }))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Accepted の項目に受け入れた理由（Reason）が無い*");
        FluentActions
            .Invoking(() => Set(accepted with { FixedIn = "P2" }))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Accepted の項目は直す区切り（FixedIn）を持たない*");
    }

    [Fact(DisplayName = "一時的な項目は直す区切りが要る")]
    public void PendingEntry_RequiresFixedIn()
    {
        FluentActions
            .Invoking(() => Set(Pending("s", "sig") with { FixedIn = " " }))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*TestEngine*Pending の項目に直す区切り（FixedIn）が無い*");
    }

    [Theory(DisplayName = "照合の仕方に合わない項目は構築時に断る")]
    [InlineData("exact-no-signature")]
    [InlineData("exact-with-predicate")]
    [InlineData("family-without-function")]
    [InlineData("predicate-missing")]
    [InlineData("predicate-with-signature")]
    [InlineData("keyonly-with-signature")]
    [InlineData("scenario-not-reproducible")]
    [InlineData("duplicate")]
    [InlineData("empty-finding")]
    [InlineData("empty-scope")]
    public void InvalidEntries_AreRejected(string shape)
    {
        var baseEntry = Pending("s", "sig");
        KnownEntry[] entries = shape switch
        {
            "exact-no-signature" => [baseEntry with { Signature = null }],
            "exact-with-predicate" => [baseEntry with { Predicate = _ => true }],
            "family-without-function" => [baseEntry with { Match = KnownMatch.Family }],
            "predicate-missing" =>
            [
                baseEntry with
                {
                    Match = KnownMatch.Predicate,
                    Signature = null,
                },
            ],
            "predicate-with-signature" =>
            [
                baseEntry with
                {
                    Match = KnownMatch.Predicate,
                    Predicate = _ => true,
                },
            ],
            "keyonly-with-signature" => [baseEntry with { Match = KnownMatch.KeyOnly }],
            "scenario-not-reproducible" => [baseEntry with { ReproducibleAtDefaultSeeds = false }],
            "duplicate" => [baseEntry, baseEntry with { Finding = "F-9" }],
            "empty-finding" => [baseEntry with { Finding = "" }],
            "empty-scope" => [baseEntry with { Scope = "" }],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        FluentActions.Invoking(() => Set(entries)).Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "照合の失敗の文面に 3 種の件数を並べる")]
    public void AssertClean_ListsCounts()
    {
        var report = Set(Pending("gone", "sig"))
            .Classify([new EngineFailure("new", "sig", "d")], DefaultPlan());

        FluentActions
            .Invoking(() => report.AssertClean("エンジン X"))
            .Should()
            .Throw<XunitException>()
            .WithMessage(
                "エンジン X: 既知の失敗の一覧と一致しない（新しい失敗 1 件・起きなかった項目 1 件・理由の食い違い 0 件）*"
            );
    }
}
