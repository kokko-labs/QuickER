using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 行列の軸（<see cref="MatrixAxis{TAdapter}"/>）と、派生クラスと軸の値の対応のガード（<see cref="MatrixAxisGuard"/>）を固定する
/// </summary>
public sealed class MatrixAxisTests
{
    private static readonly string[] Authoritative = ["sqlserver", "postgresql", "sqlite"];

    private sealed record Adapter(string AxisValue) : IMatrixAdapter;

    private static Adapter[] Complete() => [new("sqlite"), new("sqlserver"), new("postgresql")];

    [Fact(DisplayName = "正本の値ごとにアダプタがそろった軸は作れ、正本の並びで引ける")]
    public void CompleteAxis_IsAccepted()
    {
        var axis = new MatrixAxis<Adapter>("Dialect", Authoritative, Complete());

        axis.Name.Should().Be("Dialect");
        axis.Adapters.Select(a => a.AxisValue).Should().Equal(Authoritative);
        axis.Values().Select(row => row.Data).Should().Equal(Authoritative);
        axis.Get("postgresql").AxisValue.Should().Be("postgresql");
        FluentActions
            .Invoking(() => axis.Get("oracle"))
            .Should()
            .Throw<KeyNotFoundException>()
            .WithMessage("*Dialect*oracle*");
    }

    [Fact(DisplayName = "正本に値を足してアダプタを書き忘れると断る")]
    public void AuthoritativeValueWithoutAdapter_IsRejected()
    {
        FluentActions
            .Invoking(() =>
                new MatrixAxis<Adapter>("Dialect", [.. Authoritative, "oracle"], Complete())
            )
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*正本の値 oracle にアダプタも除外の理由も無い*");
    }

    [Fact(DisplayName = "アダプタを 1 つ外すと断る")]
    public void RemovedAdapter_IsRejected()
    {
        FluentActions
            .Invoking(() => new MatrixAxis<Adapter>("Dialect", Authoritative, Complete()[1..]))
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*正本の値 sqlite にアダプタも除外の理由も無い*");
    }

    [Fact(DisplayName = "同じ値のアダプタが 2 つあると断る")]
    public void DuplicateAdapter_IsRejected()
    {
        FluentActions
            .Invoking(() =>
                new MatrixAxis<Adapter>("Dialect", Authoritative, [.. Complete(), new("sqlite")])
            )
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*値 sqlite のアダプタが重複している*");
    }

    [Fact(DisplayName = "正本に無い値のアダプタは断る")]
    public void AdapterForUnknownValue_IsRejected()
    {
        FluentActions
            .Invoking(() =>
                new MatrixAxis<Adapter>("Dialect", Authoritative, [.. Complete(), new("db2")])
            )
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*値 db2 が正本に無い*");
    }

    [Fact(DisplayName = "理由つきで除外した値はアダプタが無くてよく、Theory のデータに出ない")]
    public void ExcludedWithReason_IsAccepted()
    {
        var axis = new MatrixAxis<Adapter>(
            "Dialect",
            Authoritative,
            [new("sqlserver"), new("sqlite")],
            new Dictionary<string, string> { ["postgresql"] = "この面を持たない" }
        );

        axis.Values().Select(row => row.Data).Should().Equal("sqlserver", "sqlite");
        axis.Excluded.Should().ContainKey("postgresql");
        FluentActions
            .Invoking(() => axis.Get("postgresql"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*postgresql*この面を持たない*");
    }

    [Fact(DisplayName = "除外の理由が空・除外した値のアダプタ・正本の値の重複は断る")]
    public void ExclusionProblems_AreRejected()
    {
        FluentActions
            .Invoking(() =>
                new MatrixAxis<Adapter>(
                    "Dialect",
                    [.. Authoritative, "sqlite"],
                    Complete(),
                    new Dictionary<string, string> { ["postgresql"] = " ", ["oracle"] = "x" }
                )
            )
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .ContainAll(
                "正本の値 sqlite が重複している",
                "除外した値 postgresql の理由が空",
                "除外した値 oracle が正本に無い",
                "除外した値 postgresql にアダプタ"
            );
    }

    // --- 派生クラスと軸の値の対応（シナリオごとに別の基底を使う＝アセンブリ全体の走査で混ざらない） ---

    private abstract class GoodBase;

    [MatrixAxisValue("good", "sqlserver")]
    private sealed class GoodSqlServer : GoodBase;

    [MatrixAxisValue("good", "postgresql")]
    private sealed class GoodPostgreSql : GoodBase;

    [MatrixAxisValue("good", "sqlite")]
    private sealed class GoodSqlite : GoodBase;

    [Fact(DisplayName = "正本の値ごとに派生クラスが 1 つずつあれば問題は無い")]
    public void DerivedClasses_Complete()
    {
        MatrixAxisGuard
            .DerivedClassesProblems(typeof(GoodBase), "good", Authoritative)
            .Should()
            .BeEmpty();
        FluentActions
            .Invoking(() =>
                MatrixAxisGuard.DerivedClassesCover(typeof(GoodBase), "good", Authoritative)
            )
            .Should()
            .NotThrow();
    }

    private abstract class MissingBase;

    [MatrixAxisValue("missing", "sqlserver")]
    private sealed class MissingSqlServer : MissingBase;

    private sealed class MissingNoAttribute : MissingBase;

    [Fact(DisplayName = "属性の無い派生クラスと、派生クラスも除外も無い正本の値を名指しする")]
    public void DerivedClasses_MissingAttributeAndValue()
    {
        var problems = MatrixAxisGuard.DerivedClassesProblems(
            typeof(MissingBase),
            "missing",
            Authoritative,
            new Dictionary<string, string> { ["sqlite"] = "この面を持たない" }
        );

        problems
            .Should()
            .HaveCount(2)
            .And.Contain(p =>
                p.Contains(nameof(MissingNoAttribute), StringComparison.Ordinal)
                && p.Contains("属性が無い", StringComparison.Ordinal)
            )
            .And.Contain("正本の値 postgresql に派生クラスも除外の理由も無い");
        FluentActions
            .Invoking(() =>
                MatrixAxisGuard.DerivedClassesCover(typeof(MissingBase), "missing", Authoritative)
            )
            .Should()
            .Throw<XunitException>();
    }

    private interface IBadMarker;

    [MatrixAxisValue("bad", "sqlserver")]
    [MatrixAxisValue("bad", "sqlite")]
    private sealed class BadTwoValues : IBadMarker;

    [MatrixAxisValue("bad", "db2")]
    private sealed class BadUnknown : IBadMarker;

    [MatrixAxisValue("bad", "postgresql")]
    private sealed class BadExcluded : IBadMarker;

    [MatrixAxisValue("bad", "sqlite")]
    private sealed class BadSqliteA : IBadMarker;

    [MatrixAxisValue("bad", "sqlite")]
    private sealed class BadSqliteB : IBadMarker;

    [Fact(
        DisplayName = "属性の重複・正本に無い値・除外した値・同じ値の派生クラス・理由の空の除外を名指しする（目印のインターフェイス）"
    )]
    public void DerivedClasses_AllProblems()
    {
        var problems = MatrixAxisGuard.DerivedClassesProblems(
            typeof(IBadMarker),
            "bad",
            Authoritative,
            new Dictionary<string, string> { ["postgresql"] = "この面を持たない", ["oracle"] = "" }
        );

        string.Join("\n", problems)
            .Should()
            .ContainAll(
                $"{nameof(BadTwoValues)} に軸 bad の値の属性が 2 個ある",
                $"{nameof(BadUnknown)} の値 db2 が正本に無い",
                $"{nameof(BadExcluded)} の値 postgresql は除外している",
                "値 sqlite の派生クラスが重複している",
                "正本の値 sqlserver に派生クラスも除外の理由も無い",
                "除外した値 oracle が正本に無い",
                "除外した値 oracle の理由が空"
            );
    }

    private abstract class GenericBase<T>;

    [MatrixAxisValue("generic", "sqlserver")]
    private sealed class GenericSqlServer : GenericBase<int>;

    [MatrixAxisValue("generic", "postgresql")]
    private sealed class GenericPostgreSql : GenericBase<string>;

    [Fact(DisplayName = "ジェネリックの基底の定義を渡すと、型引数を問わず派生クラスを調べる")]
    public void DerivedClasses_OpenGenericBase()
    {
        MatrixAxisGuard
            .DerivedClassesProblems(typeof(GenericBase<>), "generic", Authoritative)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("正本の値 sqlite に派生クラスも除外の理由も無い");
    }

    private interface IHonestMarker;

    [MatrixAxisValue("honest", "sqlserver")]
    private sealed class HonestSqlServer : IHonestMarker;

    [MatrixAxisValue("honest", "sqlite")]
    private sealed class LyingSqlite : IHonestMarker;

    private sealed class HonestNoAttribute : IHonestMarker;

    [Fact(
        DisplayName = "名乗る値と実際の値の食い違いを名指しし、属性の無いクラスは調べない（欠けは DerivedClassesProblems の担当）"
    )]
    public void DeclaredValues_MismatchIsNamed()
    {
        string? Actual(Type type) =>
            type == typeof(HonestSqlServer) ? "sqlserver"
            : type == typeof(LyingSqlite) ? "postgresql"
            : null;

        MatrixAxisGuard
            .DeclaredValueMismatches(typeof(IHonestMarker), "honest", Actual)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(nameof(LyingSqlite))
            .And.Contain("値 sqlite を名乗るが、実際は postgresql");
        FluentActions
            .Invoking(() =>
                MatrixAxisGuard.DeclaredValuesMatch(typeof(IHonestMarker), "honest", Actual)
            )
            .Should()
            .Throw<XunitException>();
    }
}
