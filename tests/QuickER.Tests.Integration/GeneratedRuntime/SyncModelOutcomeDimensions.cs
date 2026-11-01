using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの乱数の種で、同期が返した結果（競合の理由・実行が自分から変えた行の理由）の次元表と網羅しない組
/// </summary>
/// <remarks>
/// <para>
/// ケースは、静止化でない同期 1 回が返した競合 1 件か、自分から変えた行 1 件（<see cref="SyncModelRun.Outcomes"/>）。
/// 生成器の次元表（<see cref="SyncModelDimensions"/>）と違い、実行の結果を数える（操作の列の乱数を引かないので、種の指紋に
/// 関係しない）。理由の値ごとに、それを起こす操作の組（一意な値の付け替え・外部キーで断られる書き込み・版の競合・ServerWins の
/// 破棄など）を生成器が作っていることを、既定の種の数で各理由が 1 回以上返ることで確かめる。
/// </para>
/// <para>
/// 網羅しない組の照合は、直結のクラスの乱数の種を既定の数で流したときだけ行う（範囲 <see cref="SyncModelDimensions.DirectScope"/>）。
/// </para>
/// </remarks>
internal static class SyncModelOutcomeDimensions
{
    /// <summary>同期の結果の理由</summary>
    public const string Outcome = "Outcome";

    /// <summary>同期のモード</summary>
    public const string Mode = "Mode";

    /// <summary>競合ポリシー</summary>
    public const string Policy = "Policy";

    /// <summary>ServerWins のときだけ起きる、実行が自分から変えた行の理由</summary>
    private static readonly string[] ServerWinsChanges =
    [
        nameof(SyncLocalRowChangeReason.DiscardedByServerWins),
        nameof(SyncLocalRowChangeReason.RemovedBeforeDownload),
    ];

    /// <summary>版で弾く競合の理由（後勝ちのランは版を見ずに上書きするので返らない）</summary>
    private static readonly string[] VersionConflicts =
    [
        nameof(SyncConflictReason.ModifiedOnServer),
        nameof(SyncConflictReason.MissingOnServer),
        nameof(SyncConflictReason.DuplicateOnServer),
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncModel,
            [
                new Factor(
                    Outcome,
                    [
                        .. Enum.GetValues<SyncConflictReason>()
                            .Select(value => new Level(value.ToString(), AxisMember.Of(value))),
                        .. Enum.GetValues<SyncLocalRowChangeReason>()
                            .Select(value => new Level(value.ToString(), AxisMember.Of(value))),
                    ],
                    "同期が返した競合の理由（SyncResult.Conflicts）か、実行が自分から変えた行の理由（SyncResult.LocalRowChanges）"
                ),
                new Factor(
                    Mode,
                    Enum.GetValues<SyncMode>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "その同期のモード"
                ),
                new Factor(
                    Policy,
                    Enum.GetValues<SyncConflictPolicy>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "その同期の競合ポリシー"
                ),
            ],
            [],
            IsValid
        );

    /// <summary>網羅しない組の一覧（範囲は <see cref="SyncModelDimensions.DirectScope"/> だけ）</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncModel,
            [
                // DuplicateOnServer・後勝ちの RemovedBeforeDownload・Collect と LocalWins の ReloadedForUniqueValue は、乱数の種で
                // 作れないか欠けるので、網羅の行（SyncModelCoverageRows）が種の前に流して埋める
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncModel,
            "Outcomes",
            "同期のモデル（乱数の種で同期が返した競合・自分から変えた行の理由）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelOutcomeDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRun.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncModelRuntimeTests.cs",
            ],
            ["src/QuickER.Runtime.Sync"]
        );

    /// <summary>1 つの種の同期の結果を数える</summary>
    /// <param name="counter">数える先</param>
    /// <param name="outcomes">その種の結果（<see cref="SyncModelRun.Outcomes"/>）</param>
    public static void Observe(
        CoverageCounter counter,
        IReadOnlyList<(SyncMode Mode, SyncConflictPolicy Policy, string Outcome)> outcomes
    )
    {
        foreach (var (mode, policy, outcome) in outcomes)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Outcome] = outcome,
                    [Mode] = mode.ToString(),
                    [Policy] = policy.ToString(),
                }
            );
        }
    }

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Outcome, out var outcome);
        assignment.TryGetValue(Mode, out var mode);
        assignment.TryGetValue(Policy, out var policy);
        var conflict = outcome is not null && Enum.TryParse<SyncConflictReason>(outcome, out _);

        // ServerWins の破棄・先消しは、ServerWins のランだけが行う
        if (
            outcome is not null
            && ServerWinsChanges.Contains(outcome)
            && policy is not null
            && policy != nameof(SyncConflictPolicy.ServerWins)
        )
        {
            return false;
        }

        // 後勝ちのランは版を見ずに上書き・挿入・削除する（版の競合を返さない）
        if (
            outcome is not null
            && VersionConflicts.Contains(outcome)
            && mode == nameof(SyncMode.LastWriteWins)
        )
        {
            return false;
        }

        // LocalWins は版の競合 3 種をすべて強制して解決する（競合として返さない）
        if (
            outcome is not null
            && VersionConflicts.Contains(outcome)
            && policy == nameof(SyncConflictPolicy.LocalWins)
        )
        {
            return false;
        }

        // ServerWins のランは競合を返さない（Versioned はその範囲の未送信を送らずに捨て、後勝ちはサーバーが書き込みを断った理由
        // の競合を決着させる＝後勝ちが返し得る競合はその 3 つだけ）
        if (conflict && policy == nameof(SyncConflictPolicy.ServerWins))
        {
            return false;
        }

        return true;
    }
}
