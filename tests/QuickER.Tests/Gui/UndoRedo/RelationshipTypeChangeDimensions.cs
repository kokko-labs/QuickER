using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// リレーションの種別変更の Undo の全組合せ（<see cref="TrackedPropertyUndoExhaustiveTests"/> の
/// <c>RelationshipTypeChange_IsOneHistoryEntry_ForEveryCombination</c>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// ケースは、変更前の種別・変更後の種別・変更前の参照アクション（削除時・更新時）の組み合わせ 1 つ。観測はループが組み合わせを
/// 決めた位置（検証本体を台帳へ渡す前）で、組み合わせを読むだけ。種を持たないので、網羅しない組は毎回照合する。
/// </remarks>
internal static class RelationshipTypeChangeDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "relationship-type-change";

    /// <summary>変更前の種別</summary>
    public const string From = "From";

    /// <summary>変更後の種別</summary>
    public const string To = "To";

    /// <summary>変更前の削除時の参照アクション</summary>
    public const string OnDelete = "OnDelete";

    /// <summary>変更前の更新時の参照アクション</summary>
    public const string OnUpdate = "OnUpdate";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(From, TypeLevels(), "変更前のリレーションの種別"),
                new Factor(To, TypeLevels(), "変更後のリレーションの種別"),
                new Factor(
                    OnDelete,
                    ActionLevels(),
                    "変更前の削除時の参照アクション",
                    AxisMember.Property(typeof(Relationship), nameof(Relationship.OnDelete))
                ),
                new Factor(
                    OnUpdate,
                    ActionLevels(),
                    "変更前の更新時の参照アクション",
                    AxisMember.Property(typeof(Relationship), nameof(Relationship.OnUpdate))
                ),
            ],
            [new FactorGroup(From, To, OnDelete, OnUpdate)],
            IsValid
        );

    /// <summary>網羅しない組の一覧（全組合せを流すので空）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.DiagramEdit, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.DiagramEdit,
            "Undo",
            "リレーションの種別変更の Undo（全組合せ）",
            Table,
            0,
            [
                "tests/QuickER.Tests/Gui/UndoRedo/RelationshipTypeChangeDimensions.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/TrackedPropertyUndoExhaustiveTests.cs",
                "tests/QuickER.Tests/Gui/UndoRedo/UndoKnownFailures.cs",
            ],
            ["src/QuickER.Gui/ViewModels/RelationshipViewModel.cs", "src/QuickER.Gui/UndoRedo"]
        );

    /// <summary>組み合わせ 1 つを数える</summary>
    /// <param name="counter">数える先</param>
    /// <param name="from">変更前の種別</param>
    /// <param name="to">変更後の種別</param>
    /// <param name="onDelete">変更前の削除時の参照アクション</param>
    /// <param name="onUpdate">変更前の更新時の参照アクション</param>
    public static void Observe(
        CoverageCounter counter,
        RelationshipType from,
        RelationshipType to,
        ForeignKeyReferentialAction onDelete,
        ForeignKeyReferentialAction onUpdate
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [From] = from.ToString(),
                [To] = to.ToString(),
                [OnDelete] = onDelete.ToString(),
                [OnUpdate] = onUpdate.ToString(),
            }
        );

    private static List<Level> TypeLevels() =>
        Enum.GetValues<RelationshipType>()
            .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
            .ToList();

    private static List<Level> ActionLevels() =>
        Enum.GetValues<ForeignKeyReferentialAction>()
            .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
            .ToList();

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(From, out var from);
        assignment.TryGetValue(To, out var to);

        // 同じ種別への代入は値が変わらず、種別の変更にならない（履歴も積まれない）
        if (from is not null && from == to)
        {
            return false;
        }

        // 多対多は外部キーを持たず参照アクションを設定できない（RelationshipViewModel が NoAction に保つ）ので、
        // 変更前が多対多なら参照アクションは NoAction だけ
        if (from == nameof(RelationshipType.ManyToMany))
        {
            foreach (var factor in new[] { OnDelete, OnUpdate })
            {
                if (
                    assignment.TryGetValue(factor, out var action)
                    && action != nameof(ForeignKeyReferentialAction.NoAction)
                )
                {
                    return false;
                }
            }
        }

        return true;
    }
}
