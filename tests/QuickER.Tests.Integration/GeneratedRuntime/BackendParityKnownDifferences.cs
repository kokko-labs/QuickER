using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 今わかっている実装先の違い（基準の QuickER 版と食い違うシナリオ・実装先・観点）。
/// </summary>
/// <remarks>
/// <para>
/// 一覧は厳格に扱う。載っていない違いが出たら失敗し、載っている項目が違いを出さなくなっても失敗する（直したら一覧から
/// 外す）。項目は範囲＝シナリオの名前・署名＝<see cref="SignatureOf"/>（実装先と観点）で当たり、期待・実際の中身は見ない。
/// 直す区切りのある違いは <see cref="Pending"/>、直さないと決めた違いは <see cref="Accepted"/>（理由つき）で作る。
/// 照合の規則は <see cref="KnownFailureSet"/> が持つ。
/// </para>
/// </remarks>
public static class BackendParityKnownDifferences
{
    /// <summary>乱数の操作の列の違いの範囲</summary>
    public const string RandomScope = "random";

    private const ParityBackendKind EfCore = ParityBackendKind.EfCore;
    private const ParityBackendKind InMemory = ParityBackendKind.InMemory;

    /// <summary>既知の違いの一覧（決まった形のシナリオ）</summary>
    public static IReadOnlyList<KnownEntry> All { get; } =
    [
        // F4 の決定で残した差（受容・直さない）: 外した子への参照を持ったまま、その子を直接もう一度保存すると、QuickER 版・
        // インメモリ・リモートは 0 行の削除を黙認し、EF Core は行なしとして NotFound にする（「行なし削除」の既知の非対称）
        Accepted("save-removed-child-again", EfCore, "outcome", "F4", RowlessDeleteReason),
        Accepted("save-removed-child-again", EfCore, "hooks-after", "F4", RowlessDeleteReason),
        // 区切り 39 の決定（受容・直さない）: DB にある行を消し、同じキーの新しい行を足す保存は、どの実装先も同じく
        // 置き換わる（保存先・フックは一致）。EF Core は同じキーの削除と挿入を 1 つの UPDATE に畳むので、戻り値の行数だけが 1 になる
        Accepted(
            "graph-remove-and-add-same-key-roots-added-first",
            EfCore,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        Accepted(
            "graph-remove-and-add-same-key-roots-removed-first",
            EfCore,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        // P1・P2（受容・直さない）: インメモリは外部キーを検査せず、参照動作（ON DELETE CASCADE など）も再現しない。
        // DeleteAsync(id) で親を消しても子は残り（P1）、親の無い明細・無い顧客を指す注文も挿入する（P2）。
        // インメモリの振る舞いは変えず、docs の「実 DB との既知の違い」に書く（決定 2026-10-13・docs は区切り 25）
        Accepted("delete-parent-by-id", InMemory, "state", "P1", InMemoryForeignKeyReason),
        Accepted("constraint-violations", InMemory, "outcome", "P2", InMemoryForeignKeyReason),
        Accepted("constraint-violations", InMemory, "state", "P2", InMemoryForeignKeyReason),
    ];

    /// <summary>乱数の操作の列で一時的に見逃す違い（<see cref="Tolerance"/> で作る）</summary>
    public static IReadOnlyList<KnownEntry> RandomTolerances { get; } = [];

    /// <summary>一覧（決まった形の違いと、乱数の列の見逃し）</summary>
    public static KnownFailureSet Set { get; } =
        new("BackendParity", [.. All, .. RandomTolerances]);

    private const string RowlessDeleteReason = "行なし削除の非対称（F4 の決定で直さない）";
    private const string SameKeyReplaceReason =
        "同じキーの削除と挿入の行数（区切り 39 の決定で直さない）";
    private const string InMemoryForeignKeyReason =
        "インメモリは外部キーと参照動作を再現しない（決定 2026-10-13・docs の実 DB との既知の違い）";

    /// <summary>違いの署名（実装先と観点）</summary>
    public static string SignatureOf(ParityBackendKind backend, string aspect) =>
        $"{backend}:{aspect}";

    /// <summary>署名を実装先と観点へ戻す</summary>
    public static (ParityBackendKind Backend, string Aspect) Split(string signature)
    {
        var index = signature.IndexOf(':', StringComparison.Ordinal);
        return (Enum.Parse<ParityBackendKind>(signature[..index]), signature[(index + 1)..]);
    }

    /// <summary>直さないと決めた違いの項目を作る</summary>
    public static KnownEntry Accepted(
        string scenario,
        ParityBackendKind backend,
        string aspect,
        string finding,
        string reason
    ) =>
        new(
            finding,
            KnownKind.Accepted,
            null,
            reason,
            scenario,
            KnownMatch.ExactSignature,
            SignatureOf(backend, aspect)
        );

    /// <summary>直す区切りのある違いの項目を作る</summary>
    public static KnownEntry Pending(
        string scenario,
        ParityBackendKind backend,
        string aspect,
        string finding,
        string fixedIn
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            scenario,
            KnownMatch.ExactSignature,
            SignatureOf(backend, aspect)
        );

    /// <summary>
    /// 乱数の操作の列で、決まった形の一覧に残っている指摘の違いを一時的に見逃す項目を作る
    /// （同じ指摘・同じ区切りの項目が <see cref="All"/> に残っている間だけ有効＝整合テストが強制する）
    /// </summary>
    /// <param name="finding">指摘</param>
    /// <param name="fixedIn">直す区切り</param>
    /// <param name="covers">その違いがこの指摘のものか</param>
    public static KnownEntry Tolerance(
        string finding,
        string fixedIn,
        Func<ParityViolation, bool> covers
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            RandomScope,
            KnownMatch.Predicate,
            Predicate: failure => failure.Source is ParityViolation violation && covers(violation),
            RandomSeeds: true,
            Reproduction: KnownReproduction.NotGuaranteed
        );

    /// <summary>違いを照合に渡す失敗にする（元の違いを持つ）</summary>
    /// <param name="scope">範囲（シナリオの名前か <see cref="RandomScope"/>）</param>
    /// <param name="violation">違い</param>
    /// <param name="seed">乱数の種（決まった形のシナリオなら null）</param>
    public static EngineFailure ToFailure(
        string scope,
        ParityViolation violation,
        int? seed = null
    ) =>
        new(
            scope,
            SignatureOf(violation.Backend, violation.Aspect),
            violation.Describe(),
            seed,
            violation
        );
}
