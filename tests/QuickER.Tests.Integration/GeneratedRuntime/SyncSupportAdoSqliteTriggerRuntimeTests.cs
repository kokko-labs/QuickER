using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のトリガーの記録の表（<see cref="SyncTriggerRecordingRows"/>）を 1 行ずつ流し、ローカルの SQLite の
/// <c>SyncLocalTracking.Ensure</c> が張ったトリガーが、素の生成 Repository・生 SQL・接続の上の文の書き込みを
/// <c>quicker_sync_row</c> へ記録することを確かめる。あわせて、同期のトリガーの確認の表
/// （<see cref="SyncTriggerGuardRows"/>）を 1 行ずつ流し、<c>SyncLocalTrackingGuard</c> で包んだ接続ファクトリが、
/// 外からの変更の後に開いた接続で確かめ・張り直すことを確かめる（共通の表明は表の remarks）。
/// </summary>
/// <remarks>
/// <para>
/// 1 行の流れ：一時の DB に図の DDL を当てる → 前準備 → <c>Ensure</c> → 張った直後の表明 → 手 → 記録の表明。
/// </para>
/// <para>
/// 張った直後の表明：<c>Ensure</c> が渡した表の名前を全部返し、2 回目は空を返す。状態の表は 1 行で
/// <c>format = 1</c>・<c>applying = 0</c>・<c>next_seq = 1</c>・<c>db_id</c> が空でない。渡した表にだけ
/// <c>TriggersFor</c> のトリガーがあり、ほかの表には無い。行の状態の表は空（前準備の行は記録に無い）。
/// </para>
/// <para>
/// 記録の表明：行の状態の表が期待の並びと一致し、変更の番号が並びの順に厳密に増え、どれも <c>next_seq</c> より小さい。
/// <c>applying</c> は 0 のまま。手の前後で内容の変わらない前準備の行は記録に無い。記録したキーで元の表を引くと、送る行は
/// 1 行、削除の行は 0 行が見つかる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportAdoSqliteTriggerRuntimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory(
        DisplayName = "[Sync/トリガー] 書き込みの道ごとに、トリガーが行の状態の表へ期待どおりに記録する"
    )]
    [MemberData(
        nameof(SyncTriggerRecordingRows.Ids),
        MemberType = typeof(SyncTriggerRecordingRows)
    )]
    public async Task Row_IsRecordedAsExpected(string id)
    {
        var input = SyncTriggerRecordingRows.Table[id].Input;
        using var harness = await SyncTriggerHarness.CreateAsync(
            input.Fixture,
            input.AlterDiagram,
            Ct
        );

        if (input.Prepare is { } prepare)
        {
            await prepare(harness, Ct);
        }

        var before = await harness.ReadContentsAsync(Ct);
        var tracked = input.TrackedTables ?? harness.AllTableNames;

        await ShouldEnsureAsync(id, harness, tracked);

        await input.Act(harness, Ct);

        var rows = await harness.ReadRowsAsync(Ct);
        var state = await harness.ReadStateAsync(Ct);
        var actualText = Describe(rows);

        rows.Select(r => new SyncTriggerExpectedRow(
                r.Table,
                r.KeyType,
                r.KeyText,
                r.Dirty,
                r.Deleted,
                r.BlobColumns
            ))
            .Should()
            .Equal(input.Expected, $"行 {id} の記録が期待の並びと違う（実際：{actualText}）");

        for (var i = 1; i < rows.Count; i++)
        {
            rows[i]
                .ChangeSeq.Should()
                .BeGreaterThan(
                    rows[i - 1].ChangeSeq,
                    $"行 {id} の変更の番号は並びの順に厳密に増える（実際：{actualText}）"
                );
        }

        foreach (var row in rows)
        {
            row.ChangeSeq.Should()
                .BeGreaterThanOrEqualTo(
                    1,
                    $"行 {id} の変更の番号は 1 から振る（実際：{actualText}）"
                );
            row.ChangeSeq.Should()
                .BeLessThan(
                    state.NextSeq,
                    $"行 {id} の変更の番号は次の番号より小さい（実際：{actualText}）"
                );
        }

        state.Applying.Should().Be(0, $"行 {id} の手の後も適用中の印は 0 のまま");

        var after = await harness.ReadContentsAsync(Ct);
        var recorded = rows.Select(r => (r.Table, r.KeyType, r.KeyText)).ToHashSet();

        foreach (var (key, content) in before)
        {
            if (
                after.TryGetValue(key, out var now)
                && string.Equals(now, content, StringComparison.Ordinal)
            )
            {
                recorded
                    .Should()
                    .NotContain(key, $"行 {id} で手が触らなかった前準備の行 {key} は記録に無い");
            }
        }

        foreach (var row in rows)
        {
            (await harness.CountRowsByRecordedKeyAsync(row.Table, row.ChangeSeq, Ct))
                .Should()
                .Be(
                    row.Deleted == 0 ? 1 : 0,
                    $"行 {id} の記録したキー {row.Table}/{row.KeyText} で元の表を引き直せる（送る行は 1 行・削除は 0 行）"
                );
        }

        // エンジンが「未送信の行を残す」ときの条件（製品の NOT IN の形）でも、記録した行がちょうど残る
        foreach (var table in rows.Select(r => r.Table).Distinct())
        {
            (await harness.CountRowsKeptAsUnsentAsync(table, Ct))
                .Should()
                .Be(
                    rows.Count(r => r.Table == table && r.Dirty == 1 && r.Deleted == 0),
                    $"行 {id} の表 {table} で、製品の未送信の行を外す条件が、記録した送る行だけを残す（実際：{actualText}）"
                );
        }
    }

    [Theory(
        DisplayName = "[Sync/トリガー] 外からの変更の後、包んだ接続ファクトリで次に開いた接続が期待どおりに確かめ・張り直す"
    )]
    [MemberData(nameof(SyncTriggerGuardRows.Ids), MemberType = typeof(SyncTriggerGuardRows))]
    public async Task Guard_HandlesOutsideChangeAsExpected(string id)
    {
        var input = SyncTriggerGuardRows.Table[id].Input;
        using var harness = await SyncTriggerHarness.CreateAsync(input.Fixture, null, Ct);
        var guard = harness.CreateGuard(
            input.GuardTables,
            input.Inner,
            input.ConnectionString?.Invoke(harness)
        );
        await using var ctx = new SyncTriggerGuardContext(harness, guard);

        if (input.Prepared)
        {
            var setup = harness.CreateGuard(input.GuardTables);
            (await SyncTriggerGuardContext.TryOpenAsync(setup.CreateConnection, Ct))
                .Should()
                .BeNull($"行 {id} の準備で張る");
            setup.FullCheckCount.Should().Be(1, $"行 {id} の準備で 2 段目に 1 回入る");
            setup.RepairCount.Should().Be(1, $"行 {id} の準備で 1 回張る");
            ctx.DbIdBefore = (await harness.ReadStateAsync(Ct)).DbId;
        }

        if (input.External is { } external)
        {
            await external(ctx, Ct);
        }

        var schemaBefore = input.ChecksFile ? await harness.ReadSchemaAsync(Ct) : [];
        var fullBefore = guard.FullCheckCount;
        var repairBefore = guard.RepairCount;
        Exception? failure = null;

        if (input.Open is { } open)
        {
            await open(ctx, Ct);
        }
        else
        {
            failure = await ctx.TryOpenAsync(Ct);
        }

        var counts = input.Counts;
        var fullDelta = guard.FullCheckCount - fullBefore;
        var repairDelta = guard.RepairCount - repairBefore;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"行 {id}：2 段目 +{fullDelta}・張り直し +{repairDelta}"
        );
        fullDelta
            .Should()
            .BeInRange(
                counts.Full,
                counts.FullMax ?? counts.Full,
                $"行 {id} の次に開く処理で 2 段目に入った回数（張り直しは {repairDelta} 回）"
            );
        repairDelta
            .Should()
            .BeInRange(
                counts.Repair,
                counts.RepairMax ?? counts.Repair,
                $"行 {id} の次に開く処理で張り直した回数（2 段目は {fullDelta} 回）"
            );

        if (input.Failure is { } expected)
        {
            failure.Should().NotBeNull($"行 {id} の次に開く処理は失敗する");
            ShouldMatchFailure(id, failure!, expected);

            if (input.ChecksFile)
            {
                (await harness.ReadSchemaAsync(Ct))
                    .Should()
                    .Equal(schemaBefore, $"行 {id} の失敗した開く処理は DB のスキーマを変えない");
            }

            if (expected.FailsAgain)
            {
                var again = await ctx.TryOpenAsync(Ct);
                again.Should().NotBeNull($"行 {id} はもう一度開いても失敗する");
                ShouldMatchFailure(id, again!, expected);
            }

            if (input.Recover is not { } recover)
            {
                return;
            }

            await recover(ctx, Ct);
            await ctx.OpenAsync($"行 {id} は戻した後に開ける", Ct);
        }
        else
        {
            failure.Should().BeNull($"行 {id} の次に開く処理は成功する");
        }

        if (input.ChecksFile)
        {
            await ctx.ShouldHaveExpectedTriggersAsync($"行 {id} の開けた後", Ct);
            var state = await harness.ReadStateAsync(Ct);
            state.DbId.Should().NotBeNullOrWhiteSpace($"行 {id} の開けた後の状態の表の 1 行");
        }

        if (input.WriteRecorded)
        {
            var key = ctx.NextKey();
            var table = await guard.WriteAsync(key, Ct);
            (await harness.CountRecordedAsync(table, key, Ct))
                .Should()
                .Be(1, $"行 {id} の開けた後の Repository の書き込み {table}/{key} は記録に残る");
        }

        if (input.FastPathAfter)
        {
            var full = guard.FullCheckCount;
            await ctx.OpenAsync($"行 {id} はもう一度開ける", Ct);
            guard
                .FullCheckCount.Should()
                .Be(full, $"行 {id} はもう一度開くと速い道を通る（2 段目に入らない）");
        }

        if (input.After is { } after)
        {
            await after(ctx, Ct);
        }
    }

    /// <summary>開く処理の失敗が期待の型・内側の型・文の断片に合うことを表明する</summary>
    private static void ShouldMatchFailure(
        string id,
        Exception failure,
        SyncTriggerGuardFailure expected
    )
    {
        failure
            .GetType()
            .Should()
            .Be(expected.ExceptionType, $"行 {id} の失敗の型（実際：{failure}）");

        if (expected.InnerType is { } innerType)
        {
            failure.InnerException.Should().NotBeNull($"行 {id} の失敗は内側の例外を持つ");
            failure
                .InnerException!.GetType()
                .Should()
                .BeAssignableTo(innerType, $"行 {id} の内側の例外の型");
        }
        else
        {
            failure.InnerException.Should().BeNull($"行 {id} の失敗は内側の例外を持たない");
        }

        foreach (var fragment in expected.Fragments)
        {
            failure.Message.Should().Contain(fragment, $"行 {id} の失敗の文");
        }
    }

    /// <summary>トリガーを張り、張った直後の状態を表明する</summary>
    private static async Task ShouldEnsureAsync(
        string id,
        SyncTriggerHarness harness,
        IReadOnlyList<string> tracked
    )
    {
        harness
            .Ensure(tracked)
            .Should()
            .Equal(tracked, $"行 {id} の初回の Ensure は渡した表を全部返す");
        harness
            .Ensure(tracked)
            .Should()
            .BeEmpty($"行 {id} の 2 回目の Ensure はトリガーが期待どおりなので何も返さない");

        var state = await harness.ReadStateAsync(Ct);
        state.Format.Should().Be(1, $"行 {id} の状態の表の形式");
        state.Applying.Should().Be(0, $"行 {id} の張った直後の適用中の印");
        state.NextSeq.Should().Be(1, $"行 {id} の張った直後の次の変更の番号");
        state.DbId.Should().NotBeNullOrWhiteSpace($"行 {id} の DB の識別子");

        foreach (var table in harness.AllTableNames)
        {
            var expected = tracked.Contains(table, StringComparer.Ordinal)
                ? harness.ExpectedTriggerNames(table).Order(StringComparer.Ordinal).ToList()
                : [];

            (await harness.ReadTriggerNamesAsync(table, Ct))
                .Order(StringComparer.Ordinal)
                .Should()
                .Equal(expected, $"行 {id} の表 {table} のトリガー（渡した表だけに張る）");
        }

        (await harness.ReadRowsAsync(Ct))
            .Should()
            .BeEmpty($"行 {id} の張った直後は前準備の行を記録していない");
    }

    private static string Describe(IReadOnlyList<SyncTriggerRowState> rows) =>
        rows.Count == 0
            ? "なし"
            : string.Join(
                " / ",
                rows.Select(r =>
                    $"#{r.ChangeSeq} {r.Table} {r.KeyType}:{r.KeyText} dirty={r.Dirty} deleted={r.Deleted} blob='{r.BlobColumns}'"
                )
            );
}
