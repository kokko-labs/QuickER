using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルベーステストの既知の失敗の一覧（厳格）。
/// </summary>
/// <remarks>
/// <para>
/// 決定的なシナリオの項目は <see cref="Entry"/> で作る（範囲＝シナリオの名前・署名＝<see cref="ModelFailure.Signature"/> の全文）。
/// 載っているシナリオがその署名の組で失敗しなければ赤（直ったか、別の形で失敗した）。載っていない失敗も赤。
/// 乱数の種の失敗は、ここに載ったどれかの署名の正規形（<see cref="SyncModelRuntimeTests.Family"/>）に当たれば既知として数え、
/// 載っているのに起きないことは見ない（種の数が少なくて起きないことがある）。乱数の項目は <see cref="Build"/> が決定的な項目から作る。
/// </para>
/// <para>
/// 区切り 30 の終わりに直す区切り 30 の行を、区切り 31 で 31 の行を消し、この一覧を空にする。
/// </para>
/// </remarks>
internal static class SyncModelKnownFailures
{
    /// <summary>乱数の種の失敗の範囲</summary>
    public const string RandomScope = "random";

    /// <summary>決定的なシナリオの項目（<see cref="Entry"/> で作る）</summary>
    public static IReadOnlyList<KnownEntry> Scenarios { get; } = [];

    /// <summary>一覧（決定的なシナリオの項目と、その正規形から作った乱数の項目）</summary>
    public static KnownFailureSet Set { get; } =
        Build("SyncModel", Scenarios, SyncModelRuntimeTests.Family);

    /// <summary>決定的なシナリオの項目を作る</summary>
    /// <param name="finding">指摘の番号（三巡目レビューのまとめの番号。新しく見つけたものはその番号の案）</param>
    /// <param name="fixedIn">直す区切り（<c>区切り 30</c> または <c>区切り 31</c>）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
    public static KnownEntry Entry(
        string finding,
        string fixedIn,
        string scenario,
        string signature
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            scenario,
            KnownMatch.ExactSignature,
            signature
        );

    /// <summary>
    /// 決定的なシナリオの項目に、その署名を正規化した乱数の種の項目（同じ正規形は 1 つ）を足した一覧を作る
    /// </summary>
    /// <param name="engineId">一覧の名前</param>
    /// <param name="scenarios">決定的なシナリオの項目</param>
    /// <param name="family">乱数の種の失敗の署名の正規化</param>
    public static KnownFailureSet Build(
        string engineId,
        IReadOnlyList<KnownEntry> scenarios,
        Func<string, string> family
    ) =>
        new(
            engineId,
            [
                .. scenarios,
                .. scenarios
                    .GroupBy(entry => family(entry.Signature ?? ""), StringComparer.Ordinal)
                    .Select(group =>
                        group.First() with
                        {
                            Scope = RandomScope,
                            Match = KnownMatch.Family,
                            Signature = group.Key,
                            RandomSeeds = true,
                            Reproduction = KnownReproduction.NotGuaranteed,
                        }
                    ),
            ],
            family
        );
}
