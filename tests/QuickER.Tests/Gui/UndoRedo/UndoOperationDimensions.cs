using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 操作の往復の表（<see cref="UndoOperationRows"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// ケースは行 1 つ。観測は行の宣言（入口と積む履歴の型）を読むだけで、行を流さない。種を持たないので、網羅しない組は毎回照合する。
/// コマンドの水準は製品の具象 <see cref="IUndoableCommand"/>（<see cref="UndoCommandRoundTripTests.ConcreteCommandTypes"/>）から作る。
/// </remarks>
internal static class UndoOperationDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "undo-operation";

    /// <summary>積む履歴の型</summary>
    public const string Command = "Command";

    /// <summary>操作の入口</summary>
    public const string Entry = "Entry";

    /// <summary>コマンドの型ごとの、製品に実在する入口（ソースの <c>new …Command(</c>・<c>UndoRedo.Execute / Push</c> の全箇所から）</summary>
    /// <remarks>ここに無い組は「その入口からは積まれない」＝原理的に起こり得ない（<see cref="IsValid"/>）。載っていない型は全入口を有効とみなす</remarks>
    private static readonly Dictionary<string, UndoEntry[]> Entries = new(StringComparer.Ordinal)
    {
        [nameof(AddEntityCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(RemoveEntityCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(GroupRemoveEntitiesCommand)] = [UndoEntry.Screen],
        [nameof(DuplicateEntityCommand)] = [UndoEntry.Screen],
        [nameof(AddRelationshipCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(RemoveRelationshipCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(AddColumnCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(RemoveColumnCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(MoveColumnOrderCommand)] = [UndoEntry.Behavior],
        [nameof(MoveEntityCommand)] = [UndoEntry.Behavior],
        [nameof(GroupMoveEntitiesCommand)] = [UndoEntry.Behavior],
        [nameof(GroupChangeTitleColorCommand)] = [UndoEntry.Screen],
        [nameof(ArrangeEntitiesCommand)] = [UndoEntry.Screen],
        [nameof(PropertyChangeCommand)] = [UndoEntry.Tracker, UndoEntry.Tool],
        [nameof(SnapshotChangeCommand)] = [UndoEntry.Tracker, UndoEntry.Tool],
        [nameof(AddUniqueConstraintCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(RemoveUniqueConstraintCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(ChangeUniqueConstraintColumnsCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(SetPrimaryKeyCommand)] = [UndoEntry.Screen, UndoEntry.Tool],
        [nameof(ChangeRelationshipColumnPairsCommand)] = [UndoEntry.Screen],
        [nameof(ChangeTargetDbmsCommand)] = [UndoEntry.Screen],
    };

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Command,
                    UndoCommandRoundTripTests
                        .ConcreteCommandTypes()
                        .Select(type => new Level(type.Name))
                        .ToList(),
                    "操作が積む履歴の型（製品の具象 IUndoableCommand）"
                ),
                new Factor(
                    Entry,
                    Enum.GetValues<UndoEntry>()
                        .Select(entry => new Level(entry.ToString()))
                        .ToList(),
                    "操作の入口"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// どれも Pending（区切り P4）。表の行を足すか、今の個別のテスト・既存の網羅のままにするかを P4 で判断する
    /// （P3-7 では単発を増やさない）。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.DiagramEdit,
            [
                CoverageGap.Matching(
                    Scope,
                    "P3-7",
                    "P4",
                    "ツールの入口からの往復は表の行に無い。ツールの結果は 2 ホストの表（ToolScenarioTests）が、ツールの Undo は"
                        + " TrackedPropertyUndoExhaustiveTests.ManualForeignKeyMark_SurvivesUndoOf* と ErDiagramDynamicTools*Tests の個別が見る",
                    Command,
                    Entry,
                    (_, entry) => entry == nameof(UndoEntry.Tool)
                ),
                CoverageGap.Tuple(
                    Scope,
                    "P3-7",
                    "P4",
                    "リレーション作成モードの 2 クリックの往復は表の行に無い（作成モードの始点の解除は選択の不変条件の表が見る・"
                        + "コマンドの往復は UndoCommandRoundTripTests が見る）",
                    (Command, nameof(AddRelationshipCommand)),
                    (Entry, nameof(UndoEntry.Screen))
                ),
                CoverageGap.Tuple(
                    Scope,
                    "P3-7",
                    "P4",
                    "方言の切り替えの往復は MainViewModelTargetDbmsTests（確認ダイアログの偽を使う手順・P3-5a と同じファイルなので"
                        + "触らない）と UndoCommandRoundTripTests が見る",
                    (Command, nameof(ChangeTargetDbmsCommand)),
                    (Entry, nameof(UndoEntry.Screen))
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "UndoOps",
            "VM の操作の往復の行（入口 × 積む履歴の型・並びまで戻る）",
            Table,
            UndoOperationRows.Table.Count,
            [
                "tests/QuickER.Tests/Gui/UndoRedo/UndoOperation.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/UndoOperationDimensions.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/UndoOperationRows.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/UndoOperationTests.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariant.cs",
            ],
            ["src/QuickER.Gui/UndoRedo", "src/QuickER.Gui/ViewModels"]
        );

    /// <summary>行の宣言を数える（行を流さない）</summary>
    public static void ObserveDeclarations(CoverageCounter counter)
    {
        foreach (var row in UndoOperationRows.Table)
        {
            foreach (var command in row.Input.Commands.Distinct())
            {
                counter.Observe(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Command] = command.Name,
                        [Entry] = row.Input.Entry.ToString(),
                    }
                );
            }
        }
    }

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Command, out var command)
            || !assignment.TryGetValue(Entry, out var entry)
        )
        {
            return true;
        }

        // その入口からはそのコマンドを積まない（製品の入口の一覧に無い組）
        return !Entries.TryGetValue(command, out var entries)
            || entries.Contains(Enum.Parse<UndoEntry>(entry));
    }
}
