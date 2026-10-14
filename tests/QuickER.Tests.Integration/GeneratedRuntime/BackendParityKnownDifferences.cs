using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 今わかっている実装先の違い（基準の QuickER 版と食い違うシナリオ・実装先・観点）。
/// </summary>
/// <remarks>
/// <para>
/// 一覧は厳格に扱う。載っていない違いが出たら失敗し、載っている項目が違いを出さなくなっても失敗する（直したら一覧から
/// 外す）。項目は指摘の番号と、直す区切りを持つ。
/// </para>
/// </remarks>
public static class BackendParityKnownDifferences
{
    /// <summary>既知の違いの 1 項目</summary>
    /// <param name="Scenario">シナリオの名前</param>
    /// <param name="Backend">食い違う実装先</param>
    /// <param name="Aspect">食い違う観点（<see cref="ParityStep.Aspects"/>）</param>
    /// <param name="Finding">指摘の番号</param>
    /// <param name="Section">直す区切り</param>
    public sealed record Entry(
        string Scenario,
        ParityBackendKind Backend,
        string Aspect,
        string Finding,
        int Section
    );

    private const ParityBackendKind EfCore = ParityBackendKind.EfCore;
    private const ParityBackendKind InMemory = ParityBackendKind.InMemory;

    /// <summary>既知の違いの一覧</summary>
    public static IReadOnlyList<Entry> All { get; } =
    [
        // F4 の決定で残した差（受容・直さない）: 外した子への参照を持ったまま、その子を直接もう一度保存すると、QuickER 版・
        // インメモリ・リモートは 0 行の削除を黙認し、EF Core は行なしとして NotFound にする（「行なし削除」の既知の非対称）
        new("save-removed-child-again", EfCore, "outcome", "行なし削除の非対称（受容）", 0),
        new("save-removed-child-again", EfCore, "hooks-after", "行なし削除の非対称（受容）", 0),
        // P1・P2（受容・直さない）: インメモリは外部キーを検査せず、参照動作（ON DELETE CASCADE など）も再現しない。
        // DeleteAsync(id) で親を消しても子は残り（P1）、親の無い明細・無い顧客を指す注文も挿入する（P2）。
        // インメモリの振る舞いは変えず、docs の「実 DB との既知の違い」に書く（決定 2026-10-13・docs は区切り 25）
        new("delete-parent-by-id", InMemory, "state", "P1", 0),
        new("constraint-violations", InMemory, "outcome", "P2", 0),
        new("constraint-violations", InMemory, "state", "P2", 0),
    ];

    /// <summary>乱数の操作の列で、決まった形の一覧に残っている指摘の違いを一時的に見逃す規則</summary>
    /// <param name="Finding">指摘（決まった形の一覧に、同じ指摘の項目が残っている間だけ有効＝整合テストが強制する）</param>
    /// <param name="Section">直す区切り</param>
    /// <param name="Covers">その違いがこの指摘のものか</param>
    public sealed record RandomTolerance(
        string Finding,
        int Section,
        Func<ParityViolation, bool> Covers
    );

    /// <summary>乱数の操作の列で一時的に見逃す違い</summary>
    public static IReadOnlyList<RandomTolerance> RandomTolerances { get; } = [];
}
