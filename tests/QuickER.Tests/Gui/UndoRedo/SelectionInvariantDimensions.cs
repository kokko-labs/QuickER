using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>選択の不変条件の表（<see cref="SelectionInvariantRows"/>）の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>ケースは行の宣言（Covers）の 1 組。行を流さずに数える。種を持たないので、網羅しない組は毎回照合する。</remarks>
internal static class SelectionInvariantDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "selection-invariant";

    /// <summary>図から外れた要素を指し得るもの</summary>
    public const string Pointer = "Pointer";

    /// <summary>外れ方</summary>
    public const string Detach = "Detach";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Pointer,
                    Enum.GetValues<SelectionPointer>().Select(PointerLevel).ToList(),
                    "図から外れた要素を指し得るもの"
                ),
                new Factor(
                    Detach,
                    Enum.GetValues<SelectionDetach>()
                        .Select(detach => new Level(detach.ToString()))
                        .ToList(),
                    "要素が図から外れる・戻る経路"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// 受け入れ（製品の事実）の 4 組だけ：一覧の Reset × {選択中のテーブル・選択中のリレーション・作成中の始点・行の選択候補}。
    /// 製品はこれらの一覧を Reset しない。<c>Entities</c> / <c>Relationships</c> を <c>Clear()</c> するのは
    /// <c>MainViewModel.ClearDiagramCollections</c>（MainViewModel.cs:2654）だけで、これは図の置換（<see cref="SelectionDetach.Replace"/>）の
    /// 経路で行がある。素の <c>Clear()</c> を使わないことは <c>src/QuickER.Gui/ViewModels/CLAUDE.md</c>「Clear と購読解除の順序」の決まり
    /// （Reset 通知は OldItems を持たず自動の解除が効かない）。行の選択候補は <c>CandidateListSync.SyncTo</c> が RemoveAt / Move / Insert
    /// だけで合わせ、Clear も差し替えもしない。ObservableCollection としては起こり得るので isValid にはしない。
    /// 検索結果の Reset（<c>FillSearchResults</c> の <c>SearchResults.Clear()</c>）は行 <c>reset-search-results-rebuilt-from-live-entities</c> が見る。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.DiagramEdit,
            [
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-7",
                    "製品はこの一覧を Reset しない：Entities / Relationships の Clear は MainViewModel.ClearDiagramCollections"
                        + "（MainViewModel.cs:2654＝図の置換の経路）だけで、素の Clear を使わないことは ViewModels/CLAUDE.md の決まり。"
                        + "行の選択候補は CandidateListSync.SyncTo が差分だけで合わせる",
                    Pointer,
                    Detach,
                    (pointer, detach) =>
                        detach == nameof(SelectionDetach.Reset)
                        && pointer
                            is nameof(SelectionPointer.SelectedEntity)
                                or nameof(SelectionPointer.SelectedRelationship)
                                or nameof(SelectionPointer.PendingRelationshipSource)
                                or nameof(SelectionPointer.Candidate)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "Selection",
            "選択の不変条件の行（外れ得るもの × 外れ方）",
            Table,
            SelectionInvariantRows.Table.Count,
            [
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariant.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantDimensions.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantRows.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantScenario.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/SelectionInvariantTests.cs",
            ],
            ["src/QuickER.Gui/ViewModels"]
        );

    /// <summary>行の宣言を数える（行を流さない）</summary>
    public static void ObserveDeclarations(CoverageCounter counter)
    {
        foreach (var row in SelectionInvariantRows.Table)
        {
            foreach (var (pointer, detach) in row.Input.Covers.Distinct())
            {
                counter.Observe(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Pointer] = pointer.ToString(),
                        [Detach] = detach.ToString(),
                    }
                );
            }
        }
    }

    /// <summary>MainViewModel のプロパティであるものは製品の軸の項目として Source を持つ</summary>
    private static Level PointerLevel(SelectionPointer pointer) =>
        pointer switch
        {
            SelectionPointer.Candidate => new Level(pointer.ToString()),
            _ => new Level(
                pointer.ToString(),
                AxisMember.Property(typeof(MainViewModel), pointer.ToString())
            ),
        };

    /// <summary>原理的に起こり得ない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Pointer, out var pointer)
            || !assignment.TryGetValue(Detach, out var detach)
        )
        {
            return true;
        }

        // パネルを畳む・モードを抜けるのは表示とモードだけを変え、図の要素を外さない。外れるのは作成モードの始点だけ
        return detach != nameof(SelectionDetach.CollapsePanel)
            || pointer == nameof(SelectionPointer.PendingRelationshipSource);
    }
}
