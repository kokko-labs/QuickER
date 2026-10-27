using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ケースの台帳（<see cref="KnownCaseLedger"/>）と、種を持たない照合（<see cref="KnownFailureSet.Classify(IEnumerable{EngineFailure}, Func{string, bool})"/>）・
/// 問題の文（<see cref="KnownReport.Problem"/>）を固定する
/// </summary>
/// <remarks>
/// 台帳は照合の規則を持たず、ケースの例外を範囲＝ケース ID の失敗にして渡すだけなので、旧来の台帳と同じ 4 つの結果
/// （台帳にない失敗・台帳にあるのに起きない・期待と別の理由・台帳どおり）になることをここで確かめる。
/// </remarks>
public sealed class KnownCaseLedgerTests
{
    private static KnownCaseLedger Ledger(params KnownEntry[] known) => new("TestCases", known);

    private static KnownEntry Case(string id, string reason = "期待した理由") =>
        KnownCaseLedger.Case(id, "F-1", "P1-2", reason);

    [Fact(DisplayName = "台帳にないケースが失敗したら赤")]
    public void UnlistedFailure_IsReported()
    {
        var problems = new List<string>();

        Ledger().Run("c1", () => throw new InvalidOperationException("壊れた"), problems);

        problems.Should().ContainSingle().Which.Should().Contain("c1").And.Contain("壊れた");
    }

    [Fact(DisplayName = "台帳にあるケースが失敗しなければ赤")]
    public void ListedButPassing_IsReported()
    {
        var problems = new List<string>();

        Ledger(Case("c1")).Run("c1", () => { }, problems);

        problems.Should().ContainSingle().Which.Should().Contain("起きなかった項目 1 件");
    }

    [Fact(DisplayName = "台帳にあるケースが別の理由で失敗したら赤")]
    public void ListedWithOtherReason_IsReported()
    {
        var problems = new List<string>();

        Ledger(Case("c1"))
            .Run("c1", () => throw new InvalidOperationException("別の原因"), problems);

        problems.Should().ContainSingle().Which.Should().Contain("理由の食い違い 1 件");
    }

    [Fact(DisplayName = "台帳どおりの理由で失敗したケースと、台帳にない通ったケースは緑")]
    public void ListedWithExpectedReason_AndUnlistedPassing_AreClean()
    {
        var problems = new List<string>();
        var ledger = Ledger(Case("c1"));

        ledger.Run("c1", () => throw new InvalidOperationException("…期待した理由…"), problems);
        ledger.Run("c2", () => { }, problems);

        problems.Should().BeEmpty();
        ledger.Ids.Should().BeEquivalentTo(["c1"]);
    }

    [Fact(DisplayName = "問題の集め先が無ければ、最初の問題をその場で例外にする（非同期も同じ）")]
    public async Task WithoutProblems_ThrowsImmediately()
    {
        FluentActions
            .Invoking(() => Ledger().Run("c1", () => throw new InvalidOperationException("x")))
            .Should()
            .Throw<XunitException>();
        await FluentActions
            .Awaiting(() => Ledger(Case("c1")).RunAsync("c1", () => Task.CompletedTask))
            .Should()
            .ThrowAsync<XunitException>();
    }

    [Fact(DisplayName = "種を持たない照合は、受け持つ範囲に乱数の項目があれば断る")]
    public void ClassifyWithoutSeeds_RejectsOwnedRandomEntries()
    {
        var set = new KnownFailureSet(
            "TestEngine",
            [
                new KnownEntry(
                    "F-1",
                    KnownKind.Pending,
                    "P1-2",
                    null,
                    "random",
                    KnownMatch.KeyOnly,
                    RandomSeeds: true
                ),
            ]
        );

        FluentActions
            .Invoking(() => set.Classify([], scope => scope == "random"))
            .Should()
            .Throw<InvalidOperationException>();
        set.Classify([], scope => scope == "other").IsClean.Should().BeTrue();
    }

    [Fact(DisplayName = "問題の文は問題が無ければ null で、AssertClean はその文で失敗する")]
    public void Problem_IsNullWhenClean_AndMatchesAssertClean()
    {
        var set = new KnownFailureSet("TestEngine", []);
        var clean = set.Classify([], _ => true);
        var dirty = set.Classify([new EngineFailure("s", "sig", "detail")], _ => true);

        clean.Problem("t").Should().BeNull();
        dirty.Problem("t").Should().Contain("新しい失敗 1 件");
        FluentActions
            .Invoking(() => dirty.AssertClean("t"))
            .Should()
            .Throw<XunitException>()
            .WithMessage(dirty.Problem("t"));
    }
}
