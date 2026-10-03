using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込による図の置き換えで確認を省いてよいかの判定（<see cref="DiagramReplacementCheck"/>）を検証するテストクラス
/// </summary>
/// <remarks>
/// DB 取込・C# コードの取込・Excel 定義書のマージ取込が共有する判定。各経路のサービスのテストは
/// 確認の出し方まで含めて見ており、ここでは判定の条件そのものを固定する。
/// </remarks>
public class DiagramReplacementCheckTests
{
    /// <summary>1 テーブル（Id 列のみ）の図を作る</summary>
    private static ErDiagram Diagram(string tableName = "Customer")
    {
        var entity = new Entity { TableName = tableName };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );

        return new ErDiagram { Entities = [entity] };
    }

    /// <summary>取込結果を作る（壊れるクエリと説明の上書き件数を指定する）</summary>
    private static DiagramMergeResult Merged(
        ErDiagram imported,
        int brokenQueries = 0,
        int descriptionOverwrites = 0
    ) =>
        new()
        {
            Entities = imported.Entities,
            Relationships = imported.Relationships,
            SurvivingQueries = [],
            BrokenQueries = Enumerable
                .Range(0, brokenQueries)
                .Select(n => new QueryDefinition { Name = $"q{n}" })
                .ToList(),
            DescriptionOverwriteCount = descriptionOverwrites,
        };

    /// <summary>失うものが無いのは、エンティティ・クエリ・未保存変更のどれも無いときだけ</summary>
    [Fact(DisplayName = "HasNothingToLose: エンティティ・クエリ・未保存変更のどれも無いときだけ真")]
    public void HasNothingToLose_RequiresEmptyAndClean()
    {
        DiagramReplacementCheck.HasNothingToLose(new ErDiagram(), isDirty: false).Should().BeTrue();
        DiagramReplacementCheck.HasNothingToLose(new ErDiagram(), isDirty: true).Should().BeFalse();
        DiagramReplacementCheck.HasNothingToLose(Diagram(), isDirty: false).Should().BeFalse();
        DiagramReplacementCheck
            .HasNothingToLose(
                new ErDiagram { Queries = [new QueryDefinition { Name = "q" }] },
                isDirty: false
            )
            .Should()
            .BeFalse("クエリだけの図も失うものがある");
    }

    /// <summary>構造が同じで、壊れるクエリも説明の上書きも無ければ確認を省ける</summary>
    [Fact(DisplayName = "CanSkipConfirmation: 構造が同じで失うものが無ければ省ける")]
    public void CanSkipConfirmation_SameStructureAndNoLoss_IsTrue()
    {
        var current = Diagram();
        var imported = Diagram();

        DiagramReplacementCheck
            .CanSkipConfirmation(false, current, Merged(imported), imported.Relationships)
            .Should()
            .BeTrue();
    }

    /// <summary>構造が違えば、失うものが無いとき以外は確認する</summary>
    [Fact(DisplayName = "CanSkipConfirmation: 構造が違えば確認する（失うものが無い図を除く）")]
    public void CanSkipConfirmation_DifferentStructure_RequiresConfirmationUnlessNothingToLose()
    {
        var current = Diagram("Customer");
        var imported = Diagram("Order");

        DiagramReplacementCheck
            .CanSkipConfirmation(false, current, Merged(imported), imported.Relationships)
            .Should()
            .BeFalse();
        DiagramReplacementCheck
            .CanSkipConfirmation(true, current, Merged(imported), imported.Relationships)
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// 構造が同じでも、壊れるクエリや説明の上書きがあれば確認する（構造署名はどちらも見ないため）。
    /// </summary>
    [Theory(
        DisplayName = "CanSkipConfirmation: 壊れるクエリ・説明の上書きがあれば構造が同じでも確認する"
    )]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    public void CanSkipConfirmation_LossOverridesSameStructure(
        int brokenQueries,
        int descriptionOverwrites,
        bool hasNothingToLose
    )
    {
        var current = Diagram();
        var imported = Diagram();

        DiagramReplacementCheck
            .CanSkipConfirmation(
                hasNothingToLose,
                current,
                Merged(imported, brokenQueries, descriptionOverwrites),
                imported.Relationships
            )
            .Should()
            .BeFalse();
    }

    /// <summary>構造は渡したリレーションで比べる（C# コードの取込は補った後のリレーションを渡す）</summary>
    [Fact(DisplayName = "CanSkipConfirmation: 構造は渡したリレーションで比べる")]
    public void CanSkipConfirmation_UsesGivenRelationships()
    {
        var current = Diagram();
        var imported = Diagram();
        var extra = new Relationship
        {
            SourceEntityId = imported.Entities[0].Id,
            TargetEntityId = imported.Entities[0].Id,
        };

        DiagramReplacementCheck
            .CanSkipConfirmation(false, current, Merged(imported), [extra])
            .Should()
            .BeFalse();
    }
}
