using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 変更の無い同じインスタンスに 2 つ以上の道で届き、その下に変更を持つ行がある保存を軸から組んだシナリオ
/// （<c>n3-unchanged-instance-{道の数}-{経路}-{変更の位置}</c>）：道の数（2・3）× 保存の経路（単数の SaveAsync＝根の紹介先のコレクションに
/// 同じ顧客を道の数だけ置く・複数形の SaveAsync＝同じ根を道の数だけ渡す）× 変更の位置（子＝新しい注文・孫＝変更の無い注文の下の新しい明細・
/// none＝下に変更が無い対照）
/// </summary>
/// <remarks>
/// <para>
/// 道ごとに下を歩き直すので、どの実装先も、変更を持つ下の行を道の数だけ書こうとする（QuickER 版は主キーの例外で何も書かず、EF Core 版は
/// 1 行を書いて After のフックを道の数だけ鳴らしていた＝3 回目の効果測定の HB8）。整合の検査（<c>GraphConsistency.Check</c>）が書く前・フックの
/// 前に断る（src/QuickER.Runtime/CLAUDE.md「整合の検査」）ので、どの実装先も InvalidOperationException で何も書かず、フックも鳴らない。
/// 下に変更の無い対照は、どの実装先も何も書かずに成功する（変更を持たないインスタンスへ 2 つの道で届くことは、2 つの道がどちらも
/// インスタンスを削除するときを除いて断らない。削除の道を含む組は <see cref="UnchangedTwiceDeletionScenarios"/>）。
/// </para>
/// </remarks>
public static partial class BackendParityScenarios
{
    /// <summary>道の数の水準</summary>
    internal static IReadOnlyList<int> UnchangedTwicePathCounts => [2, 3];

    /// <summary>保存の経路の水準（単数の SaveAsync・複数形の SaveAsync）</summary>
    internal static IReadOnlyList<string> UnchangedTwiceVias => ["save", "save-many"];

    /// <summary>変更の位置の水準（子・孫・下に変更が無い対照）</summary>
    internal static IReadOnlyList<string> UnchangedTwiceDepths => ["child", "grandchild", "none"];

    /// <summary>軸の組から作る名前</summary>
    internal static string UnchangedTwiceName(int paths, string via, string depth) =>
        $"n3-unchanged-instance-{paths}-{via}-{depth}";

    /// <summary>軸から組んだシナリオ（全積）</summary>
    private static IEnumerable<ParityScenario> UnchangedTwiceScenarios() =>
        from paths in UnchangedTwicePathCounts
        from via in UnchangedTwiceVias
        from depth in UnchangedTwiceDepths
        select UnchangedTwice(paths, via, depth);

    /// <summary>
    /// 顧客 5（紹介者は顧客 1）を変更なしで置き、<paramref name="depth"/> の位置に新しい行を足して、顧客 5 のインスタンスに
    /// <paramref name="paths"/> 本の道で届く保存をする
    /// </summary>
    private static ParityScenario UnchangedTwice(int paths, string via, string depth)
    {
        var name = UnchangedTwiceName(paths, via, depth);
        var grandchild = depth == "grandchild";
        List<ParityOp> setup =
        [
            .. Basic,
            new SeedOp(new CustomerRow(5, "Eve", Referrer: 1)),
            .. grandchild ? [new SeedOp(new OrderRow(81, 5, 7m))] : System.Array.Empty<ParityOp>(),
        ];

        // 顧客 5 のグラフを置くスロット：単数の経路は根の顧客 1 の紹介先、複数形の経路は根そのもの
        var slot = via == "save" ? "R" : "A";
        List<ParityOp> ops = [];

        if (via == "save")
        {
            ops.Add(new NewRootOp("R", new CustomerRow(1, "Alice", "vip"), RowState.Unchanged));
            ops.Add(
                new AddChildOp(
                    "R",
                    "customers/1",
                    new CustomerRow(5, "Eve", Referrer: 1),
                    RowState.Unchanged
                )
            );
        }
        else
        {
            ops.Add(new NewRootOp("A", new CustomerRow(5, "Eve", Referrer: 1), RowState.Unchanged));
        }

        switch (depth)
        {
            case "child":
                ops.Add(
                    new AddChildOp(slot, "customers/5", new OrderRow(80, 5, 5m), RowState.Added)
                );
                break;
            case "grandchild":
                ops.Add(
                    new AddChildOp(slot, "customers/5", new OrderRow(81, 5, 7m), RowState.Unchanged)
                );
                ops.Add(
                    new AddChildOp(slot, "orders/81", new LineRow(900, 81, "x", 1), RowState.Added)
                );
                break;
        }

        if (via == "save")
        {
            for (var extra = 1; extra < paths; extra++)
            {
                ops.Add(new LinkChildCustomerOp("R", "customers/1", "customers/5"));
            }

            ops.Add(new SaveOp("R"));
        }
        else
        {
            ops.Add(new SaveManyCustomersOp([.. Enumerable.Repeat("A", paths)]));
        }

        var seeded = 9 + (grandchild ? 1 : 0);

        if (depth == "none")
        {
            ops.Add(ParityExpect.Succeeds($"{name}-saves"));
            ops.Add(ParityExpect.RawHooks($"{name}-fires-no-hook", []));
            ops.Add(ParityExpect.StateRows($"{name}-writes-nothing", seeded));
        }
        else
        {
            ops.Add(ParityExpect.Throws($"{name}-refused", "InvalidOperationException"));
            ops.Add(ParityExpect.RawHooks($"{name}-fires-no-hook", []));
            ops.Add(ParityExpect.StateRows($"{name}-writes-nothing", seeded));
        }

        return new ParityScenario(name, setup, ops);
    }

    /// <summary>削除の道の水準（削除の道が 2 つ・削除の道の後に保存の道・保存の道の後に削除の道）</summary>
    internal static IReadOnlyList<string> UnchangedTwiceDeletionPaths =>
        ["deleted-twice", "deleted-then-saved", "saved-then-deleted"];

    /// <summary>削除の道の下の行の水準（下に行が無い・変更の無い注文・新しい注文）</summary>
    internal static IReadOnlyList<string> UnchangedTwiceDeletionBelow =>
        ["none", "unchanged", "added"];

    /// <summary>削除の道の組から作る名前</summary>
    internal static string UnchangedTwiceDeletionName(string paths, string below) =>
        $"n3-unchanged-instance-{paths}-{below}";

    /// <summary>
    /// 削除の道の組から組んだシナリオ（全積）。削除の道が 2 つなら下に何があっても断る（2 つの道がどちらもインスタンスを消す）。
    /// 削除の道と保存の道なら、下に変更を持つ行（新しい注文）があるときだけ断り（削除の道が消し、保存の道が書く）、道の順に依らない。
    /// 下に変更の無い行だけなら、削除の道だけが消すので断らない
    /// </summary>
    private static IEnumerable<ParityScenario> UnchangedTwiceDeletionScenarios() =>
        from paths in UnchangedTwiceDeletionPaths
        from below in UnchangedTwiceDeletionBelow
        select UnchangedTwiceDeletion(paths, below);

    /// <summary>
    /// 顧客 5（紹介者は顧客 6）を変更なしで置き、削除する顧客 6（根）の紹介先のコレクションから届かせる。
    /// deleted-twice は同じ顧客 5 を顧客 6 のコレクションへ 2 回置いて単数の SaveAsync で保存し、deleted-then-saved・saved-then-deleted は
    /// 顧客 5 を別のスロットの根にもして、複数形の SaveAsync へ顧客 6 と顧客 5 をその順に渡す
    /// </summary>
    private static ParityScenario UnchangedTwiceDeletion(string paths, string below)
    {
        var name = UnchangedTwiceDeletionName(paths, below);
        var unchangedBelow = below == "unchanged";
        List<ParityOp> setup =
        [
            .. Basic,
            new SeedOp(new CustomerRow(6, "Frank")),
            new SeedOp(new CustomerRow(5, "Eve", Referrer: 6)),
            .. unchangedBelow
                ? [new SeedOp(new OrderRow(81, 5, 7m))]
                : System.Array.Empty<ParityOp>(),
        ];

        List<ParityOp> ops = [new NewRootOp("R", new CustomerRow(6, "Frank"), RowState.Removed)];
        var eve = new CustomerRow(5, "Eve", Referrer: 6);

        // 顧客 5 のグラフを置くスロット：削除の道が 2 つなら顧客 6 の紹介先、ほかは顧客 5 を根にした別のスロット
        var slot = paths == "deleted-twice" ? "R" : "A";

        if (paths == "deleted-twice")
        {
            ops.Add(new AddChildOp("R", "customers/6", eve, RowState.Unchanged));
        }
        else
        {
            ops.Add(new NewRootOp("A", eve, RowState.Unchanged));
        }

        switch (below)
        {
            case "unchanged":
                ops.Add(
                    new AddChildOp(slot, "customers/5", new OrderRow(81, 5, 7m), RowState.Unchanged)
                );
                break;
            case "added":
                ops.Add(
                    new AddChildOp(slot, "customers/5", new OrderRow(80, 5, 5m), RowState.Added)
                );
                break;
        }

        if (paths == "deleted-twice")
        {
            ops.Add(new LinkChildCustomerOp("R", "customers/6", "customers/5"));
            ops.Add(new SaveOp("R"));
        }
        else
        {
            ops.Add(new LinkRootCustomerOp("R", "customers/6", "A"));
            ops.Add(
                new SaveManyCustomersOp(paths == "deleted-then-saved" ? ["R", "A"] : ["A", "R"])
            );
        }

        var seeded = 10 + (unchangedBelow ? 1 : 0);

        if (paths == "deleted-twice" || below == "added")
        {
            ops.Add(ParityExpect.Throws($"{name}-refused", "InvalidOperationException"));
            ops.Add(ParityExpect.RawHooks($"{name}-fires-no-hook", []));
            ops.Add(ParityExpect.StateRows($"{name}-writes-nothing", seeded));
        }
        else
        {
            // 削除の道だけが顧客 6・5（と変更の無い注文）を消す
            ops.Add(ParityExpect.Succeeds($"{name}-saves"));
            ops.Add(
                ParityExpect.StateRows(
                    $"{name}-deletes-once",
                    seeded - 2 - (unchangedBelow ? 1 : 0)
                )
            );
        }

        return new ParityScenario(name, setup, ops);
    }
}
