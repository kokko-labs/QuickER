using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<c>ISaveHook</c>）のシナリオ：Before のスキップ（新しい行・更新・子・削除の根）・直接の書き込みが素通りすること・
/// 複数形の保存の発火・After が context で書く除外 blob と生 SQL・After の例外での巻き戻り。
/// </summary>
/// <remarks>
/// 生成コードのランタイムテスト（SaveHook の系統）から移した。元の図（BinaryFixture の documents → document_notes）は、
/// 注文 → 明細（除外 blob の payload・thumb を持つ根と子）と顧客（新しい行のスキップ）へ写した。
/// </remarks>
public static partial class BackendParityScenarios
{
    /// <summary><see cref="Basic"/> の注文 10 の除外 blob（payload・thumb）の 16 進</summary>
    private const string Order10Blobs = "payload=0102 thumb=0A";

    /// <summary>After が context で書いた除外 blob の 16 進（<see cref="ParityHookRecorder.HookPayload"/>・HookThumb）</summary>
    private static string HookBlobs =>
        $"payload={Convert.ToHexString(ParityHookRecorder.HookPayload)} thumb={Convert.ToHexString(ParityHookRecorder.HookThumb)}";

    private static IEnumerable<ParityScenario> SaveHookRuntimeScenarios()
    {
        // Before が false の新しい行は保存されず Added のまま・After も走らず、同じ複数形の保存の他の行は保存され Unchanged になる
        // （リモートでもサーバーのスキップが応答で戻り、クライアントの Save は成功する）
        yield return new(
            "hook-skip-insert-others-saved",
            Basic,
            [
                new NewRootOp("A", new CustomerRow(9, "Ivy"), RowState.Added),
                new NewRootOp("B", new CustomerRow(10, "Jo"), RowState.Added),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Succeeds("skip-insert-save-succeeds"),
                ParityExpect.RootState("skip-insert-skipped-stays-added", "A", RowState.Added),
                ParityExpect.RootState("skip-insert-saved-is-unchanged", "B", RowState.Unchanged),
                ParityExpect.Hooks(
                    "skip-insert-before",
                    "Before",
                    "Insert:customers/9",
                    "Insert:customers/10"
                ),
                ParityExpect.Hooks("skip-insert-after", "After", "Insert:customers/10"),
                ParityExpect.StateKeys("skip-insert-rows", KeysWithCustomers(1, 2, 10)),
            ],
            new ParityBackendOptions(SkipBefore: ["Insert:customers/9"])
        );

        // Before が更新を飛ばすと、保存先は元の値のまま・渡した行は Updated のまま
        yield return new(
            "hook-skip-update-keeps-stored-row",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new EditMemoOp(S, 10, "changed"),
                new SaveOp(S),
                ParityExpect.Succeeds("skip-update-save-succeeds"),
                ParityExpect.RootState("skip-update-stays-updated", S, RowState.Updated),
                ParityExpect.StateRow("skip-update-stored-row", "orders/10", "memo='first'"),
                ParityExpect.Hooks("skip-update-after", "After"),
            ],
            new ParityBackendOptions(SkipBefore: ["Update:orders/10"])
        );

        // カスケードの子（新しい明細）だけが飛ばされても、親は保存され Unchanged・子は Added のまま（リモートでも応答の走査が
        // カスケードをたどる）
        yield return new(
            "hook-skip-new-child-keeps-it-added",
            Basic,
            [
                new NewRootOp(S, new OrderRow(30, 1, 1m), RowState.Added),
                new AddLineOp(S, new LineRow(300, 30, "x", 1)),
                new KeepOp(S, "order_lines/300", "held"),
                new SaveOp(S),
                ParityExpect.Succeeds("skip-child-save-succeeds"),
                ParityExpect.RootState("skip-child-parent-unchanged", S, RowState.Unchanged),
                ParityExpect.RootState("skip-child-stays-added", "held", RowState.Added),
                ParityExpect.StateKeys(
                    "skip-child-rows",
                    "customers/1",
                    "customers/2",
                    "orders/10",
                    "orders/11",
                    "orders/12",
                    "orders/30",
                    "order_lines/100",
                    "order_lines/101",
                    "order_lines/102"
                ),
            ],
            new ParityBackendOptions(SkipBefore: ["Insert:order_lines/300"])
        );

        // 根ごと削除する保存で、根の削除だけを Before が飛ばすと、子ごとに Before / After（Delete）が走って子は消え、根は Before
        // だけが走って残る（削除フックは子孫から順＝F5）
        yield return new(
            "hook-skip-root-delete-children-deleted",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new AttachLineOp(S, 100),
                new AttachLineOp(S, 101),
                new MarkRemovedOp(S, "orders/10"),
                new SaveOp(S),
                ParityExpect.Succeeds("skip-root-delete-succeeds"),
                ParityExpect.Hooks(
                    "skip-root-delete-before",
                    "Before",
                    "Delete:order_lines/100",
                    "Delete:order_lines/101",
                    "Delete:orders/10"
                ),
                ParityExpect.Hooks(
                    "skip-root-delete-after",
                    "After",
                    "Delete:order_lines/100",
                    "Delete:order_lines/101"
                ),
                ParityExpect.RootState("skip-root-delete-root-stays-removed", S, RowState.Removed),
                ParityExpect.StateKeys("skip-root-delete-rows", LeftAfter(lines: [102])),
            ],
            new ParityBackendOptions(SkipBefore: ["Delete:orders/10"])
        );

        // 新しい親の挿入を Before が飛ばし、新しい子だけを保存すると、外部キーに断られて全体が巻き戻る（インメモリは外部キーを検査
        // しない＝受け入れた差で打ち切る）
        yield return new(
            "hook-skip-parent-orphans-child",
            Basic,
            [
                new NewRootOp(S, new OrderRow(50, 1, 1m), RowState.Added),
                new AddLineOp(S, new LineRow(500, 50, "orphan", 1)),
                new DeviatingOp(new SaveOp(S), ParityDeviation.OrphanInsert),
                ParityExpect.Throws("skip-parent-fk-rejected", "store-rejected"),
                ParityExpect.StateKeys(
                    "skip-parent-rolled-back",
                    LeftAfter(lines: [100, 101, 102])
                ),
            ],
            new ParityBackendOptions(SkipBefore: ["Insert:orders/50"])
        );

        // 複数形の保存（SaveAsync(IEnumerable)）は、行ごとに Before と After を 1 回ずつ、Before を自分の After より先に走らせる
        yield return new(
            "hook-save-many-fires-each-entity",
            Basic,
            [
                new NewRootOp("A", new CustomerRow(70, "Ann"), RowState.Added),
                new NewRootOp("B", new CustomerRow(71, "Ben"), RowState.Added),
                new SaveManyCustomersOp(["A", "B"]),
                ParityExpect.Succeeds("save-many-hooks-succeeds"),
                ParityExpect.Hooks(
                    "save-many-hooks-before",
                    "Before",
                    "Insert:customers/70",
                    "Insert:customers/71"
                ),
                ParityExpect.Hooks(
                    "save-many-hooks-after",
                    "After",
                    "Insert:customers/70",
                    "Insert:customers/71"
                ),
                ParityExpect.HooksOncePerEntity(
                    "save-many-hooks-each-once-before-after",
                    "Insert:customers/70",
                    "Insert:customers/71"
                ),
                ParityExpect.StateKeys("save-many-hooks-rows", KeysWithCustomers(1, 2, 70, 71)),
            ]
        );

        // 直接の Insert・Update・Delete は Save フックを素通りする（グラフ保存だけが契約）
        yield return new(
            "hook-direct-crud-does-not-fire",
            Basic,
            [
                new InsertOp(new OrderRow(40, 1, 1m, "direct-insert")),
                ParityExpect.Hooks("direct-insert-no-before", "Before"),
                ParityExpect.Hooks("direct-insert-no-after", "After"),
                new LoadOp(S, "orders", 40),
                new EditMemoOp(S, 40, "direct-update"),
                new UpdateOrderOp(S),
                ParityExpect.Outcome("direct-update-updated", "updated=True"),
                ParityExpect.Hooks("direct-update-no-before", "Before"),
                ParityExpect.Hooks("direct-update-no-after", "After"),
                new DeleteOp("orders", 40),
                ParityExpect.Outcome("direct-delete-deleted", "deleted=True"),
                ParityExpect.Hooks("direct-delete-no-before", "Before"),
                ParityExpect.Hooks("direct-delete-no-after", "After"),
            ]
        );

        // BulkInsert も Save フックを素通りする（全機能面だけ＝リモートには無い）
        yield return new(
            "hook-bulk-insert-does-not-fire",
            Basic,
            [
                new BulkInsertOp([new OrderRow(41, 1, 1m, "bulk")]),
                ParityExpect.Outcome("bulk-insert-inserted", "inserted=1"),
                ParityExpect.Hooks("bulk-insert-no-before", "Before"),
                ParityExpect.Hooks("bulk-insert-no-after", "After"),
                ParityExpect.StateRow("bulk-insert-row", "orders/41", "memo='bulk'"),
            ]
        );

        // After が context で除外 blob（payload は Stream・thumb はファイル糖衣）を書くと、同じトランザクションで書かれ、保存の
        // 更新と一緒に保存先に残り、Stream アクセサ（リモートはバイナリの GET）で読める。更新と挿入の両方（EF Core の実装先は
        // context の blob を持たない＝下のシナリオ）
        yield return new(
            "hook-after-writes-blobs",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new EditMemoOp(S, 10, "hooked"),
                new SaveOp(S),
                ParityExpect.Outcome("after-blob-update-rows", "rows=1"),
                ParityExpect.StateRow(
                    "after-blob-update-stored",
                    "orders/10",
                    "memo='hooked'",
                    HookBlobs
                ),
                new ReadBlobOp(10, "payload"),
                ParityExpect.Outcome(
                    "after-blob-update-payload-read",
                    $"read=True bytes={Convert.ToHexString(ParityHookRecorder.HookPayload)}"
                ),
                new ReadBlobOp(10, "thumb"),
                ParityExpect.Outcome(
                    "after-blob-update-thumb-read",
                    $"read=True bytes={Convert.ToHexString(ParityHookRecorder.HookThumb)}"
                ),
                new NewRootOp("B", new OrderRow(13, 1, 5m), RowState.Added),
                new SaveOp("B"),
                ParityExpect.Outcome("after-blob-insert-rows", "rows=1"),
                ParityExpect.StateRow("after-blob-insert-stored", "orders/13", HookBlobs),
                new ReadBlobOp(13, "payload"),
                ParityExpect.Outcome(
                    "after-blob-insert-payload-read",
                    $"read=True bytes={Convert.ToHexString(ParityHookRecorder.HookPayload)}"
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Update:orders/10", ParityHookBehavior.AfterWriteBlob),
                    new ParityHookRule("Insert:orders/13", ParityHookBehavior.AfterWriteBlob),
                ]
            )
        );

        // 同じ形を全実装先で：EF Core の context の blob の書き込みは NotSupportedException（文言で名指しする）で保存ごと巻き戻り、
        // EF Core 単独のサーバーは 500 になる（docs に書いてある違い＝基準と比べず、実装先ごとの期待で書く）
        yield return new(
            "hook-after-write-blob-not-supported-on-ef-core",
            Basic,
            [
                new LoadOp(S, "orders", 10),
                new EditMemoOp(S, 10, "hooked"),
                new MessageOp(new SaveOp(S)),
                ParityExpect.OutcomeByKind(
                    "after-blob-outcome",
                    "rows=1",
                    efCore: "throws NotSupportedException",
                    remoteEfCoreOnly: ParityStep.StoreRejected
                ),
                ParityExpect.Message(
                    "after-blob-ef-core-message",
                    ParityBackendKind.EfCore,
                    "Writing excluded binary columns from a save hook (WriteBinaryColumnAsync) is not available in EF Core mode."
                ),
                ParityExpect.Either(
                    "after-blob-stored",
                    ParityBackendKinds.HasBlobStream,
                    ParityExpect.StateRow("written", "orders/10", "memo='hooked'", HookBlobs),
                    ParityExpect.StateRow("rolled-back", "orders/10", "memo='first'", Order10Blobs)
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Update:orders/10", ParityHookBehavior.AfterWriteBlob),
                ]
            ),
            CompareWithBaseline: false
        );

        // 前の行の After が除外 blob を書き、後の行の After が投げると、行の更新も blob も巻き戻る（EF Core は blob の書き込みで
        // NotSupportedException になり、同じく巻き戻る。リモートは 500）
        yield return new(
            "hook-after-writes-blob-then-later-after-throws",
            Basic,
            [
                new LoadOp("A", "orders", 10),
                new EditMemoOp("A", 10, "doomed"),
                new LoadOp("B", "orders", 12),
                new EditMemoOp("B", 12, "doomed"),
                new SaveManyOrdersOp(["A", "B"]),
                ParityExpect.OutcomeByKind(
                    "after-blob-then-throw-fails",
                    ParityStep.HookFailure,
                    efCore: "throws NotSupportedException",
                    remote: ParityStep.StoreRejected,
                    remoteEfCoreOnly: ParityStep.StoreRejected
                ),
                ParityExpect.StateRow(
                    "after-blob-then-throw-row-and-blob-rolled-back",
                    "orders/10",
                    "memo='first'",
                    Order10Blobs
                ),
                ParityExpect.StateRow(
                    "after-blob-then-throw-later-row-rolled-back",
                    "orders/12",
                    "memo=null"
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Update:orders/10", ParityHookBehavior.AfterWriteBlob),
                    new ParityHookRule("Update:orders/12", ParityHookBehavior.ThrowAfter),
                ]
            ),
            CompareWithBaseline: false
        );

        // After が context の生 SQL で別の表の行を書くと、同じトランザクションで書かれて残る（インメモリの context は生 SQL を
        // 持たず NotSupportedException で保存ごと巻き戻る）
        yield return new(
            "hook-after-executes-sql",
            Basic,
            [
                new LoadOp(S, "orders", 11),
                new EditMemoOp(S, 11, "hooked"),
                new SaveOp(S),
                ParityExpect.OutcomeByKind(
                    "after-sql-outcome",
                    "rows=1",
                    inMemory: "throws NotSupportedException"
                ),
                ParityExpect.Either(
                    "after-sql-stored",
                    kind => kind != ParityBackendKind.InMemory,
                    ParityExpect.StateRow("written", "customers/2", "note='audit'"),
                    ParityExpect.StateRow("rolled-back", "customers/2", "note=null")
                ),
                ParityExpect.Either(
                    "after-sql-row",
                    kind => kind != ParityBackendKind.InMemory,
                    ParityExpect.StateRow("written", "orders/11", "memo='hooked'"),
                    ParityExpect.StateRow("rolled-back", "orders/11", "memo=null")
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Update:orders/11", ParityHookBehavior.AfterExecuteSql),
                ]
            ),
            CompareWithBaseline: false
        );

        // 前の行の After が生 SQL で別の表の行を書き、後の行の After が投げると、行の更新も生 SQL の行も巻き戻る（インメモリは生 SQL
        // で NotSupportedException になり、同じく巻き戻る。リモートは 500）
        yield return new(
            "hook-after-sql-then-later-after-throws",
            Basic,
            [
                new LoadOp("A", "orders", 11),
                new EditMemoOp("A", 11, "doomed"),
                new LoadOp("B", "orders", 12),
                new EditMemoOp("B", 12, "doomed"),
                new SaveManyOrdersOp(["A", "B"]),
                ParityExpect.OutcomeByKind(
                    "after-sql-then-throw-fails",
                    ParityStep.HookFailure,
                    inMemory: "throws NotSupportedException",
                    remote: ParityStep.StoreRejected,
                    remoteEfCoreOnly: ParityStep.StoreRejected
                ),
                ParityExpect.StateRow(
                    "after-sql-then-throw-sql-row-rolled-back",
                    "customers/2",
                    "note=null"
                ),
                ParityExpect.StateRow(
                    "after-sql-then-throw-row-rolled-back",
                    "orders/11",
                    "memo=null"
                ),
                ParityExpect.StateRow(
                    "after-sql-then-throw-later-row-rolled-back",
                    "orders/12",
                    "memo=null"
                ),
            ],
            new ParityBackendOptions(
                HookRules:
                [
                    new ParityHookRule("Update:orders/11", ParityHookBehavior.AfterExecuteSql),
                    new ParityHookRule("Update:orders/12", ParityHookBehavior.ThrowAfter),
                ]
            ),
            CompareWithBaseline: false
        );
    }
}

/// <summary>Save フックのシナリオの期待</summary>
public static partial class ParityExpect
{
    /// <summary>
    /// 直前の手の Save フックの呼び出し（Before と After を混ぜた発火順・畳まない）が、指定の呼び出しごとに Before と After を
    /// ちょうど 1 回ずつ、Before を自分の After より先に持ち、ほかの呼び出しを持たない（行をまたいだ順序は見ない＝QuickER 版は
    /// 行ごとに Before→After、EF Core とインメモリは Before を先に全部）
    /// </summary>
    public static ExpectOp HooksOncePerEntity(string label, params string[] calls) =>
        new(
            label,
            e =>
            {
                var raw = e.Previous.RawHooks;

                if (raw.Count != calls.Length * 2)
                {
                    return $"hooks [{string.Join(", ", raw)}], expected one Before and one After for each of [{string.Join(", ", calls)}]";
                }

                foreach (var call in calls)
                {
                    var before = raw.Count(h => h == "Before:" + call);
                    var after = raw.Count(h => h == "After:" + call);

                    if (
                        before != 1
                        || after != 1
                        || raw.ToList().IndexOf("Before:" + call)
                            > raw.ToList().IndexOf("After:" + call)
                    )
                    {
                        return $"hooks [{string.Join(", ", raw)}]: {call} fires Before {before} and After {after} times, or After first";
                    }
                }

                return null;
            }
        );

    /// <summary>
    /// 実装先が <paramref name="when"/> を満たせば <paramref name="then"/>、満たさなければ <paramref name="otherwise"/> の期待を確かめる
    /// （docs に書いてある実装先の違いで結果が 2 つに分かれるシナリオ用）
    /// </summary>
    public static ExpectOp Either(
        string label,
        Func<ParityBackendKind, bool> when,
        ExpectOp then,
        ExpectOp otherwise
    ) =>
        new(
            label,
            e =>
                when(e.Kind)
                    ? then.Check(e) is { } a
                        ? $"{then.Label}: {a}"
                        : null
                    : otherwise.Check(e) is { } b
                        ? $"{otherwise.Label}: {b}"
                        : null
        );
}
