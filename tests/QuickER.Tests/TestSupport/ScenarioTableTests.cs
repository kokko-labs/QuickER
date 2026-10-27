using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 決定的なシナリオの表（<see cref="ScenarioTable{TInput}"/>）の構築時の検査と引き方を固定する
/// </summary>
public sealed class ScenarioTableTests
{
    private static ScenarioRow<int> Row(string id, string description = "説明") =>
        new(id, null, description, 0);

    [Fact(DisplayName = "重複した Id は、重複をすべて名指しして断る")]
    public void Duplicates_AreRejected()
    {
        FluentActions
            .Invoking(() =>
                new ScenarioTable<int>("E", [Row("a"), Row("b"), Row("a"), Row("c"), Row("b")])
            )
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .ContainAll("Id a が重複している", "Id b が重複している");
    }

    [Fact(DisplayName = "空の Id と空の説明は断る")]
    public void BlankIdOrDescription_IsRejected()
    {
        FluentActions
            .Invoking(() => new ScenarioTable<int>("E", [Row(" "), Row("x", "")]))
            .Should()
            .Throw<ArgumentException>()
            .Which.Message.Should()
            .ContainAll("1 行目の Id が空", "2 行目（x）の説明が空");
    }

    [Fact(DisplayName = "Id は表の並びで Theory のデータになり、Id で引ける")]
    public void Ids_AndIndexer()
    {
        var table = new ScenarioTable<int>("E", [Row("z"), new("a", "F-1", "説明", 42), Row("m")]);

        table.Count.Should().Be(3);
        table.Ids().Select(row => row.Data).Should().Equal("z", "a", "m");
        table["a"].Input.Should().Be(42);
        table["a"].Finding.Should().Be("F-1");
        table.Select(row => row.Id).Should().Equal("z", "a", "m");
    }

    [Fact(DisplayName = "表に無い Id はエンジンと Id を名指しして断る")]
    public void UnknownId_Throws()
    {
        var table = new ScenarioTable<int>("E", [Row("a")]);

        FluentActions
            .Invoking(() => table["b"])
            .Should()
            .Throw<KeyNotFoundException>()
            .WithMessage("E のシナリオの表に Id b が無い");
    }
}
