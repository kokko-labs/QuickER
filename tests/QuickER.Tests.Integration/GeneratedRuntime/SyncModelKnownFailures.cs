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
/// </remarks>
internal static class SyncModelKnownFailures
{
    /// <summary>乱数の種の失敗の範囲</summary>
    public const string RandomScope = "random";

    /// <summary>
    /// 新しいエンジンの端末の既知の失敗の一覧（直結の Theory <see cref="SyncModelRuntimeTests.Scenario_FailsExactlyAsListed"/> と乱数の種
    /// <see cref="SyncSupportAdoSqliteEngineRuntimeTests.RandomSeeds_ConvergeWithoutLoss"/>）。空で始める（載せる項目は、新しい
    /// エンジンの端末で出た失敗から理由つきで足す）
    /// </summary>
    public static KnownFailureSet EngineSet { get; } =
        Build("SyncModel", [], SyncModelRuntimeTests.Family);

    /// <summary>決定的なシナリオの項目を作る</summary>
    /// <param name="finding">指摘の番号（三巡目レビューのまとめの番号。新しく見つけたものはその番号の案）</param>
    /// <param name="fixedIn">直す区切り（区切りの名前か、きっかけを待つ台帳の項目）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
    /// <param name="reason">失敗の理由（製品の事実。省略できる）</param>
    public static KnownEntry Entry(
        string finding,
        string fixedIn,
        string scenario,
        string signature,
        string? reason = null
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            reason,
            scenario,
            KnownMatch.ExactSignature,
            signature
        );

    /// <summary>決定的なシナリオの、受け入れた失敗の項目を作る（製品の事実で直さない形）</summary>
    /// <param name="finding">指摘の番号</param>
    /// <param name="reason">受け入れる理由（製品の事実と根拠）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名</param>
    public static KnownEntry AcceptedEntry(
        string finding,
        string reason,
        string scenario,
        string signature
    ) =>
        new(
            finding,
            KnownKind.Accepted,
            null,
            reason,
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
    /// <param name="reproductions">正規形ごとの乱数の項目の再現の扱い（載っていない正規形は NotGuaranteed）</param>
    /// <param name="seedOnlySignatures">決定的な項目の署名のうち、正規形から乱数の項目を作らないもの</param>
    /// <param name="seedNamed">種を名指しした乱数の項目</param>
    public static KnownFailureSet Build(
        string engineId,
        IReadOnlyList<KnownEntry> scenarios,
        Func<string, string> family,
        IReadOnlyDictionary<string, KnownReproduction>? reproductions = null,
        IReadOnlySet<string>? seedOnlySignatures = null,
        IReadOnlyList<KnownEntry>? seedNamed = null
    ) =>
        new(
            engineId,
            [
                .. scenarios,
                .. scenarios
                    .Where(entry => seedOnlySignatures?.Contains(entry.Signature ?? "") != true)
                    .GroupBy(entry => family(entry.Signature ?? ""), StringComparer.Ordinal)
                    .Select(group =>
                        group.First() with
                        {
                            Scope = RandomScope,
                            Match = KnownMatch.Family,
                            Signature = group.Key,
                            RandomSeeds = true,
                            Reproduction =
                                reproductions?.GetValueOrDefault(
                                    group.Key,
                                    KnownReproduction.NotGuaranteed
                                ) ?? KnownReproduction.NotGuaranteed,
                        }
                    ),
                .. seedNamed ?? [],
            ],
            family
        );
}
