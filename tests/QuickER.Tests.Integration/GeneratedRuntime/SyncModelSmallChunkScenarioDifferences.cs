using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 小さい塊の端末（<see cref="SyncModelEngineDevice.OnSmallChunks"/>）で決定的なシナリオを流した結果が、1 回で全部の道の端末
/// （直結の Theory）と違う行の理由の宣言（シナリオの名前 → 理由の 1 行）。
/// </summary>
/// <remarks>
/// 違う行の集合と、転送ごとに 2 方向に照合する（宣言の無い違う行も、宣言があるのに 1 回で全部の道の実行と同じ結果の行も赤。
/// <see cref="SyncModelSmallChunkScenarioRuns.AssertMatchesDirectAsync"/>）。理由は、塊の経路が 1 回で全部の経路と
/// 違う答えを出す具体的な仕組み（削除の段・キーを先に取る順・塊ごとの確定）を書く。どの転送でも違う行は <see cref="All"/>、
/// 1 つの転送でだけ違う行は <see cref="TransportOnly"/> に置く（その転送のサーバー役の性質＝取得の上限の固定などの仕組みを書く）。
/// </remarks>
internal static class SyncModelSmallChunkScenarioDifferences
{
    /// <summary>転送の宣言（どの転送でも違う行と、その転送でだけ違う行）</summary>
    /// <param name="transport">転送</param>
    public static IReadOnlyDictionary<string, string> For(SyncModelTransport transport) =>
        All.Concat(
                TransportOnly
                    .Where(pair => pair.Key.Transport == transport)
                    .Select(pair => KeyValuePair.Create(pair.Key.Scenario, pair.Value))
            )
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>1 つの転送でだけ違う行（転送, シナリオの名前）→ 理由</summary>
    public static IReadOnlyDictionary<
        (SyncModelTransport Transport, string Scenario),
        string
    > TransportOnly { get; } =
        new Dictionary<(SyncModelTransport Transport, string Scenario), string>();

    /// <summary>どの転送でも違う行：シナリオの名前 → 理由</summary>
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chunk-delete-stage-parent-gone-after-next-sync"] =
                "最初の同期の直後に親 3050 が端末に無いことを期待する（1 回で全部の道は、付け替えた子 3052 と親の削除を同じ適用で流す）。"
                + "塊の道の削除の段は付け替えの塊より前に 1 つのトランザクションで流れ、端末の 3052 がまだ 3050 を指すので外部キーの"
                + "違反を避けて 3050 を残す（署名 5/expect-rows:model_folders）。3050 はその実行の後も端末に残り、次の同期の削除伝搬で"
                + "消える（同じ行の 2 回目の同期の後の ExpectLocalOp は通る）",
        };
}
