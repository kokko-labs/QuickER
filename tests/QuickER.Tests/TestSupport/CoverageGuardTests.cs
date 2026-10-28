using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストのエンジンが黙って空振りしないためのガード（<see cref="CoverageGuard"/>）を固定する
/// </summary>
public sealed class CoverageGuardTests
{
    [Fact(DisplayName = "件数が最低件数に届かなければ、何の件数かを名指しして赤になる")]
    public void AtLeast_FailsBelowMinimum()
    {
        FluentActions
            .Invoking(() => CoverageGuard.AtLeast("照合したクエリ", 100, 100))
            .Should()
            .NotThrow();
        FluentActions
            .Invoking(() => CoverageGuard.AtLeast("照合したクエリ", 99, 100))
            .Should()
            .Throw<XunitException>()
            .WithMessage("照合したクエリ: 99 件しか流れていない（最低 100 件）*");
    }

    [Fact(DisplayName = "最低件数が 0 以下なら何も守らないので断る")]
    public void AtLeast_RejectsNonPositiveMinimum()
    {
        FluentActions
            .Invoking(() => CoverageGuard.AtLeast("x", 0, 0))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "必ず出るべき値が欠けていれば、欠けた値を名指しして赤になる")]
    public void ContainsAll_FailsWhenMissing()
    {
        FluentActions
            .Invoking(() => CoverageGuard.ContainsAll("形", ["a", "b", "a"], ["a", "b"]))
            .Should()
            .NotThrow();
        FluentActions
            .Invoking(() => CoverageGuard.ContainsAll("形", ["a"], ["a", "b", "c"]))
            .Should()
            .Throw<XunitException>()
            .WithMessage("形: 必ず出るべき 3 個のうち 2 個が出ていない: b, c");
    }

    [Fact(DisplayName = "必ず出るべき値が空なら何も守らないので断る")]
    public void ContainsAll_RejectsEmptyRequirement()
    {
        FluentActions
            .Invoking(() => CoverageGuard.ContainsAll<string>("形", ["a"], []))
            .Should()
            .Throw<ArgumentException>();
    }
}
