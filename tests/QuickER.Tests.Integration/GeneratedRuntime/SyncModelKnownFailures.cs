using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>既知の失敗 1 件（直すまでモデルベーステストから一時的に外す）</summary>
/// <param name="Finding">指摘の番号（三巡目レビューのまとめの番号。新しく見つけたものはその番号の案）</param>
/// <param name="Section">直す区切り（30 または 31）</param>
/// <param name="Scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
/// <param name="Signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
internal sealed record KnownSyncModelFailure(
    string Finding,
    int Section,
    string Scenario,
    string Signature
);

/// <summary>
/// 同期のモデルベーステストの既知の失敗の一覧（厳格）。
/// </summary>
/// <remarks>
/// <para>
/// 載っているシナリオがその署名の組で失敗しなければ赤（直ったか、別の形で失敗した）。載っていない失敗も赤。
/// 乱数の種の失敗は、ここに載ったどれかの署名に当たれば既知として数える。
/// </para>
/// <para>
/// 区切り 30 の終わりに Section=30 の行を、区切り 31 で Section=31 の行を消し、この一覧を空にする。
/// </para>
/// </remarks>
internal static class SyncModelKnownFailures
{
    public static IReadOnlyList<KnownSyncModelFailure> All { get; } = [];
}
