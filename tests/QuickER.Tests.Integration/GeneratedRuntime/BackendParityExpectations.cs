using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 決定した振る舞いへの期待のうち、値そのもの（戻り値・保存先の行・フックの列・文言）を実装先ごとに表明するもの。
/// 基準（QuickER 版）と比べるだけでは、基準そのものが壊れても緑になるので、移したテストの絶対値はこれで書く。
/// </summary>
public static partial class ParityExpect
{
    /// <summary>
    /// 直前の手の戻り値を、実装先ごとに決めた値で表明する（指定しない実装先は <paramref name="expected"/>）。docs に書いてある
    /// 実装先の違いの上に立つシナリオ（基準と比べないもの）で使う
    /// </summary>
    public static ExpectOp OutcomeByKind(
        string label,
        string expected,
        string? efCore = null,
        string? inMemory = null,
        string? remote = null,
        string? remoteEfCoreOnly = null
    ) =>
        new(
            label,
            e =>
            {
                var wanted = e.Kind switch
                {
                    ParityBackendKind.EfCore => efCore ?? expected,
                    ParityBackendKind.InMemory => inMemory ?? expected,
                    ParityBackendKind.Remote => remote ?? expected,
                    ParityBackendKind.RemoteEfCoreOnly => remoteEfCoreOnly ?? expected,
                    _ => expected,
                };

                return e.Previous.Outcome == wanted
                    ? null
                    : $"outcome {e.Previous.Outcome}, expected {wanted}";
            }
        );

    /// <summary>直前の手の後の保存先の行のキー（<c>customers/1</c> の形・テーブル名順・キー順）が、この並びでちょうど一致する</summary>
    public static ExpectOp StateKeys(string label, params string[] keys) =>
        new(
            label,
            e =>
            {
                var actual = e.Previous.State.Select(row => row.Split(' ')[0]).ToList();

                return actual.SequenceEqual(keys, StringComparer.Ordinal)
                    ? null
                    : $"rows [{string.Join(", ", actual)}], expected [{string.Join(", ", keys)}]";
            }
        );

    /// <summary>
    /// 直前の手の後の保存先に、キーが <paramref name="key"/> の行がちょうど 1 つあり、列の断片（<c>referrer=5</c> の形）を
    /// すべて含む
    /// </summary>
    public static ExpectOp StateRow(string label, string key, params string[] fragments) =>
        new(
            label,
            e =>
            {
                var rows = e
                    .Previous.State.Where(row =>
                        row.StartsWith(key + " ", StringComparison.Ordinal)
                    )
                    .ToList();

                if (rows.Count != 1)
                {
                    return $"{rows.Count} rows for {key}";
                }

                var missing = fragments
                    .Where(f => !rows[0].Contains(" " + f, StringComparison.Ordinal))
                    .ToList();

                return missing.Count == 0
                    ? null
                    : $"row {rows[0]} lacks [{string.Join(", ", missing)}]";
            }
        );

    /// <summary>
    /// 直前の手の Save フックの呼び出しを、Before と After を混ぜた発火順のまま、繰り返しも畳まずにちょうど表明する
    /// （<c>Before:Insert:orders/13</c> の形。EF Core だけ違う列にするときは <paramref name="efCore"/>＝再試行の回数だけ走る）
    /// </summary>
    public static ExpectOp RawHooks(string label, string[] calls, string[]? efCore = null) =>
        new(
            label,
            e =>
            {
                var wanted =
                    e.Kind == ParityBackendKind.EfCore && efCore is not null ? efCore : calls;

                return e.Previous.RawHooks.SequenceEqual(wanted, StringComparer.Ordinal)
                    ? null
                    : $"hooks [{string.Join(", ", e.Previous.RawHooks)}], expected [{string.Join(", ", wanted)}]";
            }
        );

    /// <summary>スロットのグラフ（根から子への方向）の、キーが <paramref name="key"/> の行の RowState</summary>
    public static ExpectOp SlotRowState(string label, string slot, string key, RowState expected) =>
        new(
            label,
            e =>
            {
                var row = Walk(e.Backend.Slots[slot])
                    .FirstOrDefault(x => ParityFormat.KeyOf(x) == key);

                return row is null ? $"'{key}' is not in slot '{slot}'"
                    : row.RowState == expected ? null
                    : $"RowState {row.RowState}, expected {expected}";
            }
        );

    /// <summary>
    /// 直前の <see cref="MessageOp"/> が受けた例外の文言が <paramref name="prefix"/> で始まる（<paramref name="kind"/> の実装先だけを
    /// 見る。ほかの実装先の文言は、表明しない＝元のテストがその実装先の文言だけを見ていたため）
    /// </summary>
    public static ExpectOp Message(string label, ParityBackendKind kind, string prefix) =>
        new(
            label,
            e =>
            {
                if (e.Kind != kind)
                {
                    return null;
                }

                var message = e.Backend.LastExceptionMessage;

                return message is not null && message.StartsWith(prefix, StringComparison.Ordinal)
                    ? null
                    : $"message '{message}', expected to start with '{prefix}'";
            }
        );

    /// <summary>
    /// <paramref name="inner"/> の期待を <paramref name="kind"/> の実装先でだけ確かめる（ほかの実装先では満たしたとみなす。元のテストが
    /// その実装先でだけ流れていて、ほかの実装先の結果は docs に書いてある違いで決まらないとき）
    /// </summary>
    public static ExpectOp ForKind(ParityBackendKind kind, ExpectOp inner) =>
        new(inner.Label, e => e.Kind == kind ? inner.Check(e) : null);

    /// <summary>
    /// スロットのグラフ（根から子への方向）の行のキーが、この並び（<see cref="ParityFormat.Graph"/> の並び＝子はキーの順）でちょうど
    /// 一致する（読み込んだ・組み立てたグラフが持つ子の集合を見る）
    /// </summary>
    public static ExpectOp SlotKeys(string label, string slot, params string[] keys) =>
        new(
            label,
            e =>
            {
                if (!e.Backend.Slots.TryGetValue(slot, out var root))
                {
                    return $"slot '{slot}' is empty";
                }

                var actual = ParityFormat
                    .Graph(root)
                    .Select(line => line.TrimStart().Split(' ')[1])
                    .ToList();

                return actual.SequenceEqual(keys, StringComparer.Ordinal)
                    ? null
                    : $"slot keys [{string.Join(", ", actual)}], expected [{string.Join(", ", keys)}]";
            }
        );

    /// <summary>
    /// スロットのグラフに、キーが <paramref name="key"/> の行がちょうど 1 つあり、列の断片（<c>note='vip'</c> の形）をすべて含む
    /// （呼び出し側が持つエンティティの値を見る。保存先の値は <see cref="StateRow"/>）
    /// </summary>
    public static ExpectOp SlotRow(
        string label,
        string slot,
        string key,
        params string[] fragments
    ) =>
        new(
            label,
            e =>
            {
                if (!e.Backend.Slots.TryGetValue(slot, out var root))
                {
                    return $"slot '{slot}' is empty";
                }

                var rows = ParityFormat
                    .Graph(root)
                    .Select(line => line.TrimStart())
                    .Where(line => line.Split(' ')[1] == key)
                    .ToList();

                if (rows.Count != 1)
                {
                    return $"{rows.Count} rows for {key} in slot '{slot}'";
                }

                var missing = fragments
                    .Where(f => !rows[0].Contains(" " + f, StringComparison.Ordinal))
                    .ToList();

                return missing.Count == 0
                    ? null
                    : $"row {rows[0]} lacks [{string.Join(", ", missing)}]";
            }
        );

    /// <summary>根から子への方向にグラフの行を並べる</summary>
    private static IEnumerable<EntityBaseCore> Walk(EntityBaseCore root)
    {
        var stack = new Stack<EntityBaseCore>([root]);

        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            yield return entity;

            foreach (var child in entity.EnumerateCascadeChildren())
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>スロットのグラフの行（<paramref name="key"/>）が持つ親への参照（明細 → 注文・注文 → 顧客）の RowState</summary>
    public static ExpectOp ParentRowState(
        string label,
        string slot,
        string key,
        RowState expected
    ) =>
        new(
            label,
            e =>
            {
                var parent = ParentOf(e.Backend, slot, key);

                return parent is null ? $"'{key}' has no parent"
                    : parent.RowState == expected ? null
                    : $"parent RowState {parent.RowState}, expected {expected}";
            }
        );

    /// <summary>
    /// 2 つのスロットの行が持つ親への参照が、どちらも読まれていて、同じキーの別のインスタンスである（別々の読み込みで付いた親）
    /// </summary>
    public static ExpectOp DistinctParents(
        string label,
        string slotA,
        string keyA,
        string slotB,
        string keyB
    ) =>
        new(
            label,
            e =>
            {
                var a = ParentOf(e.Backend, slotA, keyA);
                var b = ParentOf(e.Backend, slotB, keyB);

                return a is null || b is null ? "a parent is not loaded"
                    : ParityFormat.KeyOf(a) != ParityFormat.KeyOf(b)
                        ? $"parents {ParityFormat.KeyOf(a)} and {ParityFormat.KeyOf(b)}"
                    : ReferenceEquals(a, b) ? "both rows share one parent instance"
                    : null;
            }
        );

    /// <summary>スロットのグラフの行の親への参照（明細 → 注文・注文 → 顧客。読まれていなければ null）</summary>
    private static EntityBaseCore? ParentOf(ParityBackend backend, string slot, string key) =>
        Walk(backend.Slots[slot]).Single(x => ParityFormat.KeyOf(x) == key) switch
        {
            OrderLineEntity l => l.Order,
            OrderEntity o => o.Customer,
            var other => throw new InvalidOperationException(
                $"'{ParityFormat.KeyOf(other)}' has no parent reference."
            ),
        };
}
