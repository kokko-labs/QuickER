using System.Collections.Generic;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core の実行戦略（再試行する戦略で 1 回目のコミットを一時的に失敗させる）と、版の無い表で文が 1 行も当たらない書き込みの
/// シナリオ
/// </summary>
public static partial class BackendParityScenarios
{
    /// <summary>orders への <paramref name="statement"/>（UPDATE / DELETE）を黙って捨てる SQLite のトリガー</summary>
    private static string IgnoringTrigger(string statement) =>
        $"CREATE TRIGGER ignore_order_writes BEFORE {statement} ON orders BEGIN SELECT RAISE(IGNORE); END;";

    private static IEnumerable<ParityScenario> EfCoreRuntimeScenarios()
    {
        // 実行戦略：注文を付け替えて元の顧客を消す保存の 1 回目のコミットが失敗しても、2 回目の試行で保存先の外部キーを読み直して
        // 成立する（再試行する実行戦略は戦略の外のトランザクションを断るので、生成コードは試行ごとに組み直す）
        yield return new(
            "ef-retry-move-order-and-delete-old-customer",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
                // 除外 blob は未取得の形で入れる（付け替えの注文は UPDATE の経路へ回すので未取得の形にする。EF Core は全列を書く）
                new SeedOp(new OrderRow(20, 4, 20m, UnsetBlobs: true)),
            ],
            [
                new NewRootOp("A", new CustomerRow(4, "Dan"), RowState.Removed),
                new NewRootOp("B", new CustomerRow(5, "Eve"), RowState.Unchanged),
                new AddChildOp(
                    "B",
                    "customers/5",
                    new OrderRow(20, 5, 20m, UnsetBlobs: true),
                    RowState.Updated
                ),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Outcome("retry-move-saves-two-rows", "rows=2"),
                ParityExpect.StateRow("retry-move-order-moved", "orders/20", "customer=5"),
                ParityExpect.StateKeys(
                    "retry-move-old-customer-gone",
                    "customers/1",
                    "customers/2",
                    "customers/5",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/20",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
            ],
            new ParityBackendOptions(WithHooks: false, RetryEfCoreFirstCommit: true)
        );

        // 実行戦略：削除する顧客の代替キーを別の顧客の更新が取る保存（EF Core は後回しの行を 2 つめの文脈で保存する）も、1 回目の
        // コミットが失敗した後の試行で、2 つの SaveChanges を 1 つの試行として組み直して成立する
        yield return new(
            "ef-retry-updated-row-takes-alternate-key-of-deleted-row",
            [
                .. Basic,
                new SeedOp(new CustomerRow(4, "Dan")),
                new SeedOp(new CustomerRow(5, "Eve")),
            ],
            [
                new NewRootOp("A", new CustomerRow(5, "Eve", Code: "K4"), RowState.Updated),
                new NewRootOp("B", new CustomerRow(4, "Dan"), RowState.Removed),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Outcome("retry-alternate-key-saves-two-rows", "rows=2"),
                ParityExpect.StateRow("retry-alternate-key-taken", "customers/5", "code='K4'"),
                ParityExpect.StateKeys(
                    "retry-alternate-key-old-customer-gone",
                    KeysWithCustomers(1, 2, 5)
                ),
            ],
            new ParityBackendOptions(WithHooks: false, RetryEfCoreFirstCommit: true)
        );

        // 実行戦略：Save フックありの保存は明示のトランザクションを開く。1 回目のコミットが失敗すると試行し直し、EF Core のフックは
        // 試行の回数だけ走る（docs の Save フック節）。1 回目の試行はロールバックされ、行は 1 つだけ残る。フックの列が実装先で違う
        // のは docs に書いてある違いなので、基準とは比べず実装先ごとの期待で書く
        yield return new(
            "ef-retry-save-with-hooks-runs-hooks-per-attempt",
            Basic,
            [
                new NewRootOp(S, new OrderRow(13, 1, 13m), RowState.Added),
                new SaveOp(S),
                ParityExpect.Outcome("retry-hooks-saves-one-row", "rows=1"),
                ParityExpect.RawHooks(
                    "retry-hooks-per-attempt",
                    ["Before:Insert:orders/13", "After:Insert:orders/13"],
                    efCore:
                    [
                        "Before:Insert:orders/13",
                        "After:Insert:orders/13",
                        "Before:Insert:orders/13",
                        "After:Insert:orders/13",
                    ]
                ),
                ParityExpect.RootState("retry-hooks-root-unchanged", S, RowState.Unchanged),
                ParityExpect.StateKeys(
                    "retry-hooks-one-row-remains",
                    "customers/1",
                    "customers/2",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/13",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
            ],
            new ParityBackendOptions(WithHooks: true, RetryEfCoreFirstCommit: true),
            CompareWithBaseline: false
        );

        // 実行戦略：カスケードの一括削除も、1 回目のコミットが失敗した後の試行で成立する（注文 10 と明細 100・101）
        yield return new(
            "ef-retry-execute-delete-cascade",
            Basic,
            [
                new ExecuteDeleteOrdersOp(ParityOrderPredicate.OrderIs10, CascadeDelete: true),
                ParityExpect.Outcome("retry-execute-delete-rows", "rows=3"),
                ParityExpect.StateKeys(
                    "retry-execute-delete-remaining",
                    "customers/1",
                    "customers/2",
                    "orders/11",
                    "orders/12",
                    "order_lines/102"
                ),
            ],
            new ParityBackendOptions(WithHooks: false, RetryEfCoreFirstCommit: true)
        );

        // 版の無い表で、更新が競合以外の理由（トリガー）により 1 行も当たらない：UpdateAsync は false・グラフ保存は NotFound。
        // 強制上書きでも同じで、当たらない文を繰り返さない（インメモリにはトリガーが無いので流さない）
        foreach (var mode in new[] { ConcurrencyMode.Optimistic, ConcurrencyMode.ForceOverwrite })
        {
            var suffix = mode == ConcurrencyMode.Optimistic ? "optimistic" : "force-overwrite";

            yield return new(
                $"ignored-write-update-returns-false-{suffix}",
                Basic,
                [
                    new OutsideSqlOp(IgnoringTrigger("UPDATE")),
                    new LoadOp(S, "orders", 10),
                    new EditMemoOp(S, 10, "edited"),
                    new UpdateOrderOp(S, mode),
                    ParityExpect.Outcome("ignored-update-is-false", "updated=False"),
                    ParityExpect.StateRow("ignored-update-keeps-row", "orders/10", "memo='first'"),
                ],
                new ParityBackendOptions(WithHooks: false)
            );

            List<ParityOp> save =
            [
                new OutsideSqlOp(IgnoringTrigger("UPDATE")),
                new LoadOp(S, "orders", 10),
                new EditMemoOp(S, 10, "edited"),
                new MessageOp(new SaveOp(S, new ParitySaveOptions(Mode: mode))),
                ParityExpect.Throws(
                    "ignored-save-is-not-found",
                    "SaveConflictException reason=NotFound"
                ),
                ParityExpect.StateRow("ignored-save-keeps-row", "orders/10", "memo='first'"),
            ];

            if (mode == ConcurrencyMode.Optimistic)
            {
                // EF Core の NotFound は、流した操作の動詞で報告する
                save.Add(
                    ParityExpect.Message(
                        "ignored-save-names-update",
                        ParityBackendKind.EfCore,
                        "The record to update was not found"
                    )
                );
            }

            yield return new(
                $"ignored-write-save-reports-not-found-{suffix}",
                Basic,
                save,
                new ParityBackendOptions(WithHooks: false)
            );
        }

        // 版の無い表で、グラフ保存の削除が 1 行も当たらない：EF Core は削除の動詞で NotFound を報告する。QuickER 版とインメモリは
        // 行の無い削除を黙認する（「行なし削除」の既知の非対称＝docs）ので、EF Core でだけ確かめる
        yield return new(
            "ignored-write-delete-reports-not-found-on-ef-core",
            Basic,
            [
                new OutsideSqlOp(IgnoringTrigger("DELETE")),
                new LoadOp(S, "orders", 11),
                new MarkRemovedOp(S, "orders/11"),
                new MessageOp(new SaveOp(S)),
                ParityExpect.ForKind(
                    ParityBackendKind.EfCore,
                    ParityExpect.Throws(
                        "ignored-delete-is-not-found",
                        "SaveConflictException reason=NotFound"
                    )
                ),
                ParityExpect.Message(
                    "ignored-delete-names-delete",
                    ParityBackendKind.EfCore,
                    "The record to delete was not found"
                ),
            ],
            new ParityBackendOptions(WithHooks: false),
            CompareWithBaseline: false
        );
    }
}
