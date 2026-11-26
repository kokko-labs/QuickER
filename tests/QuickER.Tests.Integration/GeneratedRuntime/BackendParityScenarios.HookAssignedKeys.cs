using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Before のフックが新しい行のキーを決める保存（<see cref="ParityHookBehavior.AssignKey"/>）を軸から組んだシナリオ
/// （<c>n2-hook-assigned-keys-{型}-{数}-{置き場所}-{経路}</c>）：フックがキーを決める新しい行の数（1・2・3）× 型（根の顧客・注文・明細）×
/// 置き場所（同じ親・別の親。根の顧客は根）× 保存の経路（単数の SaveAsync・複数形の SaveAsync）
/// </summary>
/// <remarks>
/// <para>
/// 新しい行のキーは未設定（0）で組み、フックが保存ごとの通し番号で違うキーを決める（最初のキーは
/// <see cref="ParityHookRecorder.AssignedCustomerKey"/> などのまま＝<c>n2-hook-assigned-keys-reach-new-children</c> の期待は変わらない）。
/// 同じ型の新しい行が 2 つ以上ある保存も、どの実装先でも同じ結果になる（EF Core 版は挿入を Before のフックの後に追跡へ入れるので、未設定の
/// キーの行どうしが追跡でぶつからない＝3 回目の効果測定の HB1 の受け入れ）。作らない形は <c>BackendParityHookAssignedKeysNotGenerated</c>。
/// </para>
/// </remarks>
public static partial class BackendParityScenarios
{
    /// <summary>軸の型の水準</summary>
    internal static IReadOnlyList<string> HookAssignedKeyTypes => ["customers", "orders", "lines"];

    /// <summary>フックがキーを決める新しい行の数の水準</summary>
    internal static IReadOnlyList<int> HookAssignedKeyCounts => [1, 2, 3];

    /// <summary>保存の経路の水準（単数の SaveAsync・複数形の SaveAsync）</summary>
    internal static IReadOnlyList<string> HookAssignedKeyPaths => ["save", "save-many"];

    /// <summary>軸の組から作る名前（<c>n2-hook-assigned-keys-{型}-{数}-{置き場所}-{経路}</c>）</summary>
    internal static string HookAssignedKeysName(
        string type,
        int count,
        string placement,
        string path
    ) => $"n2-hook-assigned-keys-{type}-{count}-{placement}-{path}";

    /// <summary>
    /// 作る組か（作らない組は <c>BackendParityHookAssignedKeysNotGenerated</c> に宣言する）：根の顧客は置き場所が根だけで、単数の SaveAsync は
    /// 根を 1 つしか取らないので新しい顧客 1 つだけ。別の親は 2 つ以上の行にだけある（1 つの行の親は 1 つ）
    /// </summary>
    internal static bool HookAssignedKeysBuilt(
        string type,
        int count,
        string placement,
        string path
    ) =>
        (type, placement) switch
        {
            ("customers", "roots") => path == "save-many" || count == 1,
            ("customers", _) => false,
            (_, "roots") => false,
            (_, "different") => count >= 2,
            _ => true,
        };

    /// <summary>置き場所の水準（根の顧客は根・注文と明細は同じ親か別の親）</summary>
    internal static IReadOnlyList<string> HookAssignedKeyPlacements =>
        ["roots", "same", "different"];

    /// <summary>軸から組んだシナリオ</summary>
    private static IEnumerable<ParityScenario> HookAssignedKeyScenarios()
    {
        foreach (var type in HookAssignedKeyTypes)
        {
            foreach (var count in HookAssignedKeyCounts)
            {
                foreach (var placement in HookAssignedKeyPlacements)
                {
                    foreach (var path in HookAssignedKeyPaths)
                    {
                        if (HookAssignedKeysBuilt(type, count, placement, path))
                        {
                            yield return HookAssignedKeys(type, count, placement, path);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Before のフックが飛ばす新しい行（キーは未設定の 0 のまま）が同じ型に 2 つある保存（注文・明細 × 同じ親 × 単数・複数形の SaveAsync）。
    /// どの実装先も何も書かずに成功する（EF Core 版は飛ばした新しい行を追跡に入れないので、未設定のキーどうしがぶつからない）
    /// </summary>
    private static IEnumerable<ParityScenario> HookSkippedNewRowScenarios() =>
        from type in new[] { "orders", "lines" }
        from path in HookAssignedKeyPaths
        select HookAssignedKeys(type, 2, "same", path, ParityHookBehavior.Skip);

    /// <summary>
    /// 1 つの組のシナリオ：<see cref="Basic"/> に、顧客 1 を紹介者に持つ顧客 3・4 と顧客 1 の注文 13 を足し、新しい行（キー 0）を
    /// 置き場所どおりに組んで保存し、最後にフックが決めたキーの行が保存先にあることを期待の手で表明する
    /// </summary>
    private static ParityScenario HookAssignedKeys(
        string type,
        int count,
        string placement,
        string path,
        ParityHookBehavior behavior = ParityHookBehavior.AssignKey
    )
    {
        var name =
            behavior == ParityHookBehavior.Skip
                ? $"n2-hook-skipped-new-rows-{type}-{count}-{placement}-{path}"
                : HookAssignedKeysName(type, count, placement, path);
        var ops = new List<ParityOp>();
        var slots = new List<string>();
        string lastKey;
        string lastFragment;

        switch (type)
        {
            case "customers":
                for (var i = 0; i < count; i++)
                {
                    var slot = $"S{i}";
                    ops.Add(
                        new NewRootOp(
                            slot,
                            new CustomerRow(0, $"New{i}", Code: $"KN{i}"),
                            RowState.Added
                        )
                    );
                    slots.Add(slot);
                }

                lastKey = $"customers/{ParityHookRecorder.AssignedCustomerKey + count - 1}";
                lastFragment = $"name=New{count - 1}";
                break;

            case "orders":
                // 同じ親：顧客 1 の下に count 個。別の親：単数の保存は顧客 1 の根の下の紹介先（顧客 3・4）、複数形は顧客 1・2・3 の根
                var orderParents =
                    placement == "same" ? Enumerable.Repeat(1, count).ToArray()
                    : path == "save" ? new[] { 1, 3, 4 }.Take(count).ToArray()
                    : new[] { 1, 2, 3 }.Take(count).ToArray();

                if (path == "save" || placement == "same")
                {
                    ops.Add(
                        new NewRootOp("S", new CustomerRow(1, "Alice", "vip"), RowState.Unchanged)
                    );
                    slots.Add("S");

                    foreach (var referred in orderParents.Distinct().Where(id => id != 1))
                    {
                        ops.Add(
                            new AddChildOp(
                                "S",
                                "customers/1",
                                ReferredCustomer(referred),
                                RowState.Unchanged
                            )
                        );
                    }

                    for (var i = 0; i < count; i++)
                    {
                        ops.Add(
                            new AddChildOp(
                                "S",
                                $"customers/{orderParents[i]}",
                                new OrderRow(0, orderParents[i], 200m + i),
                                RowState.Added
                            )
                        );
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        var slot = $"S{i}";
                        ops.Add(
                            new NewRootOp(
                                slot,
                                orderParents[i] switch
                                {
                                    1 => new CustomerRow(1, "Alice", "vip"),
                                    2 => new CustomerRow(2, "Bob"),
                                    _ => ReferredCustomer(orderParents[i]),
                                },
                                RowState.Unchanged
                            )
                        );
                        ops.Add(
                            new AddChildOp(
                                slot,
                                $"customers/{orderParents[i]}",
                                new OrderRow(0, orderParents[i], 200m + i),
                                RowState.Added
                            )
                        );
                        slots.Add(slot);
                    }
                }

                lastKey = $"orders/{ParityHookRecorder.AssignedOrderKey + count - 1}";
                lastFragment = $"customer={orderParents[^1]}";
                break;

            default:
                // 同じ親：注文 10 の下に count 個。別の親：注文 10・11・13（どれも顧客 1）の下に 1 つずつ。単数の保存の別の親は顧客 1 の根の下
                var lineParents =
                    placement == "same"
                        ? Enumerable.Repeat(10, count).ToArray()
                        : new[] { 10, 11, 13 }.Take(count).ToArray();

                if (path == "save" && placement == "different")
                {
                    ops.Add(
                        new NewRootOp("S", new CustomerRow(1, "Alice", "vip"), RowState.Unchanged)
                    );
                    slots.Add("S");

                    foreach (var order in lineParents)
                    {
                        ops.Add(
                            new AddChildOp(
                                "S",
                                "customers/1",
                                UnchangedOrder(order),
                                RowState.Unchanged
                            )
                        );
                    }
                }
                else if (placement == "same")
                {
                    ops.Add(new NewRootOp("S", UnchangedOrder(10), RowState.Unchanged));
                    slots.Add("S");
                }
                else
                {
                    foreach (var order in lineParents)
                    {
                        var slot = $"S{order}";
                        ops.Add(new NewRootOp(slot, UnchangedOrder(order), RowState.Unchanged));
                        slots.Add(slot);
                    }
                }

                for (var i = 0; i < count; i++)
                {
                    var slot = placement == "same" || path == "save" ? "S" : $"S{lineParents[i]}";
                    ops.Add(
                        new AddChildOp(
                            slot,
                            $"orders/{lineParents[i]}",
                            new LineRow(0, lineParents[i], $"n{i}", i + 1),
                            RowState.Added
                        )
                    );
                }

                lastKey = $"order_lines/{ParityHookRecorder.AssignedLineKey + count - 1}";
                lastFragment = $"order={lineParents[^1]}";
                break;
        }

        ops.Add(
            path == "save" ? new SaveOp(slots[0])
            : type == "lines" ? new SaveManyOrdersOp(slots)
            : new SaveManyCustomersOp(slots)
        );
        ops.Add(ParityExpect.Succeeds($"{name}-saves"));

        if (behavior == ParityHookBehavior.AssignKey)
        {
            ops.Add(ParityExpect.StateRow($"{name}-last-key-stored", lastKey, lastFragment));
        }

        return new(
            name,
            [
                .. Basic,
                new SeedOp(ReferredCustomer(3)),
                new SeedOp(ReferredCustomer(4)),
                new SeedOp(new OrderRow(13, 1, 20m)),
            ],
            ops,
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Insert:customers", behavior),
                    new ParityHookRule("Insert:orders", behavior),
                    new ParityHookRule("Insert:order_lines", behavior),
                ]
            )
        );
    }

    /// <summary>顧客 1 を紹介者に持つ顧客（3 は Carol・4 は Dan）</summary>
    private static CustomerRow ReferredCustomer(int id) =>
        new(id, id == 3 ? "Carol" : "Dan", Referrer: 1);

    /// <summary>顧客 1 の既存の注文を、除外 blob を読んでいない形で組む（10 と 11 は種まきの金額・13 は足した注文）</summary>
    private static OrderRow UnchangedOrder(int id) =>
        new(
            id,
            1,
            id switch
            {
                10 => 100m,
                11 => 50m,
                _ => 20m,
            },
            id == 10 ? "first" : null,
            UnsetBlobs: true
        );
}
