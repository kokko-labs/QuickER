using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

// SQL Server の系統の乱数の保存の比べ方の例外（BackendParitySqlServerSaveComparison）の自己検査（DB は使わない。
// 比べ方の例外の自己検査は、ほかの 2 つと同じくこのクラスに置く）
public sealed partial class BackendParityRuntimeTests
{
    /// <summary>競合の後の行（版と保存先。例外のときも等しさを求める部分）</summary>
    private static readonly string[] SqlServerSaveAfterConflict =
    [
        "kit 1 version-matches-store=False",
        "kit 2 version-matches-store=False",
        "kits/2 code=K2 name=other",
    ];

    /// <summary>kit 1 は他者が消し、kit 2 は他者が版を進めた（どちらも単独で競合になる）Optimistic の保存</summary>
    private static SqlServerSaveScenario SqlServerSaveTwoConflicts(bool skipStale = false) =>
        new(
            1,
            SaveArguments.Optimistic,
            Hooks: skipStale,
            [
                new SaveRowPlan(
                    1,
                    SaveRowRole.ChangeCode,
                    SaveRowCondition.Gone,
                    false,
                    "K1",
                    "C1",
                    0
                ),
                new SaveRowPlan(
                    2,
                    SaveRowRole.Rename,
                    SaveRowCondition.Stale,
                    skipStale,
                    "K2",
                    null,
                    0
                ),
                new SaveRowPlan(
                    3,
                    SaveRowRole.ChangeCode,
                    SaveRowCondition.Fresh,
                    false,
                    "K3",
                    "C3",
                    0
                ),
            ],
            []
        );

    private static IReadOnlyList<string> SqlServerSaveLines(
        string outcome,
        params string[] after
    ) => [outcome, .. after.Length == 0 ? SqlServerSaveAfterConflict : after];

    [Fact(
        DisplayName = "[Parity/SqlServer] 乱数の保存の比べ方：単独で競合になる行が 2 つ以上あると、候補の行と役に合う理由の組どうしは同じ"
    )]
    public void TwoIndependentConflicts_ReportedDifferently_AreTheSame()
    {
        var scenario = SqlServerSaveTwoConflicts();

        BackendParitySqlServerSaveComparison
            .IndependentConflicts(scenario)
            .Should()
            .BeEquivalentTo([
                (SaveConflictReason.NotFound, "KitEntity", "1"),
                (SaveConflictReason.Modified, "KitEntity", "2"),
            ]);
        BackendParitySqlServerSaveComparison
            .Difference(
                scenario,
                SqlServerSaveLines(
                    "throws SaveConflictException reason=NotFound type=KitEntity key=1"
                ),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=Modified type=KitEntity key=2"
                )
            )
            .Should()
            .BeNull();

        // 更新する部品を他者が消すと、部品も候補になる（型の名前で kit と分ける）
        var withGonePart = scenario with
        {
            Parts = [GoneUpdatedPart],
        };

        BackendParitySqlServerSaveComparison
            .IndependentConflicts(withGonePart)
            .Should()
            .BeEquivalentTo([
                (SaveConflictReason.NotFound, "KitEntity", "1"),
                (SaveConflictReason.Modified, "KitEntity", "2"),
                (SaveConflictReason.NotFound, "KitPartEntity", "1"),
            ]);
        BackendParitySqlServerSaveComparison
            .Difference(
                withGonePart,
                SqlServerSaveLines(
                    "throws SaveConflictException reason=NotFound type=KitPartEntity key=1"
                ),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=Modified type=KitEntity key=2"
                )
            )
            .Should()
            .BeNull();
    }

    /// <summary>kit 2 の下で更新する部品 1 を、読んだ後に他者が消した</summary>
    private static SavePartPlan GoneUpdatedPart =>
        new(1, 2, SavePartRole.Update, SaveRowCondition.Gone, 2, SavePartForeignKey.Explicit);

    [Fact(
        DisplayName = "[Parity/SqlServer] 乱数の保存の比べ方：単独で競合になる行が 1 つの保存では、理由とキーの違いは違い"
    )]
    public void SingleIndependentConflict_ReportedDifferently_Differs()
    {
        // kit 2 は Before のフックが飛ばすので、単独で競合になるのは kit 1 だけ
        var scenario = SqlServerSaveTwoConflicts(skipStale: true);

        BackendParitySqlServerSaveComparison.IndependentConflicts(scenario).Should().HaveCount(1);
        BackendParitySqlServerSaveComparison
            .Difference(
                scenario,
                SqlServerSaveLines(
                    "throws SaveConflictException reason=NotFound type=KitEntity key=1"
                ),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=Modified type=KitEntity key=2"
                )
            )
            .Should()
            .NotBeNull();
    }

    [Theory(
        DisplayName = "[Parity/SqlServer] 乱数の保存の比べ方：候補の外のキー・型・役に合わない理由・競合でない結果は違い"
    )]
    [InlineData("throws SaveConflictException reason=Modified type=KitEntity key=3")]
    [InlineData("throws SaveConflictException reason=Modified type=KitPartEntity key=2")]
    [InlineData("throws SaveConflictException reason=Modified type=KitEntity key=1")]
    [InlineData("throws SaveConflictException reason=NotFound type=KitEntity key=2")]
    [InlineData("throws SaveConflictException reason=Unknown type=KitEntity key=1")]
    [InlineData("throws store-rejected")]
    public void OutsideTheCandidates_Differs(string reported)
    {
        BackendParitySqlServerSaveComparison
            .Difference(
                SqlServerSaveTwoConflicts(),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=NotFound type=KitEntity key=1"
                ),
                SqlServerSaveLines(reported)
            )
            .Should()
            .NotBeNull();
    }

    [Fact(
        DisplayName = "[Parity/SqlServer] 乱数の保存の比べ方：例外のときも、競合の後の版と保存先は等しさを求める"
    )]
    public void TwoIndependentConflicts_DifferentState_Differs()
    {
        BackendParitySqlServerSaveComparison
            .Difference(
                SqlServerSaveTwoConflicts(),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=NotFound type=KitEntity key=1"
                ),
                SqlServerSaveLines(
                    "throws SaveConflictException reason=Modified type=KitEntity key=2",
                    "kit 1 version-matches-store=False",
                    "kit 2 version-matches-store=True",
                    "kits/2 code=K2 name=other"
                )
            )
            .Should()
            .NotBeNull();
    }

    [Fact(
        DisplayName = "[Parity/SqlServer] 乱数の保存の比べ方：insertWhenUpdateMissing の消えた行と ForceOverwrite の版の古い行は候補にならない"
    )]
    public void InsertWhenMissingAndForceOverwrite_AreNotCandidates()
    {
        foreach (
            var arguments in new[]
            {
                SaveArguments.OptimisticInsertWhenMissing,
                SaveArguments.ForceOverwrite,
                SaveArguments.ForceOverwriteInsertWhenMissing,
            }
        )
        {
            var scenario = SqlServerSaveTwoConflicts() with
            {
                Arguments = arguments,
                Parts = [GoneUpdatedPart],
            };

            // 他者が消した部品も、insertWhenUpdateMissing なら挿入へ切り替わるので候補にならない（ForceOverwrite だけなら候補）
            BackendParitySqlServerSaveComparison
                .IndependentConflicts(scenario)
                .Should()
                .HaveCountLessThan(
                    arguments == SaveArguments.ForceOverwrite ? 3 : 2,
                    arguments.ToString()
                );
            BackendParitySqlServerSaveComparison
                .Difference(
                    scenario,
                    SqlServerSaveLines(
                        "throws SaveConflictException reason=NotFound type=KitEntity key=1"
                    ),
                    SqlServerSaveLines(
                        "throws SaveConflictException reason=Modified type=KitEntity key=2"
                    )
                )
                .Should()
                .NotBeNull(arguments.ToString());
        }
    }
}
