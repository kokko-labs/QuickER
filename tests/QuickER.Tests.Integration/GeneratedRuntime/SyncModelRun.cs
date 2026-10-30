using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>表明の違反 1 件（署名で既知の一覧と照合し、詳細は人が読むために出す）</summary>
/// <param name="Signature">
/// 「表明の番号/種類:表:区別できる要素」の形の文字列。既知の一覧はこれで失敗を当てる。粗すぎると別の不具合が同じ
/// 署名へ吸い込まれて隠れるため、例外なら「エンジンの段＞最も内側の生成コードの段:例外の種類と文言」まで、表明 2・3 なら
/// 「表＋食い違いの形」まで含める。
/// </param>
/// <param name="Detail">人が読む詳細（キー・期待値・実際の値・例外の文）</param>
internal sealed record ModelFailure(string Signature, string Detail);

/// <summary>
/// 同期のモデルベーステストの 1 回の実行（世界を作り、操作を流し、静止化してから表明を確かめる）。
/// </summary>
/// <remarks>
/// <para>表明は 4 つ。</para>
/// <list type="number">
///   <item><b>止まらない</b>: 注入した失敗の無い同期が例外を投げない。最後の静止化（端末を交互に、Versioned＋Collect →
///   Versioned＋ServerWins → LastWriteWins の 3 段を安定するまで回す。いずれも全表・blob を運ぶ）が、例外なく、ジャーナル・
///   競合・打ち切りを残さずに終わる。</item>
///   <item><b>収束</b>: 静止化の後、端末 2 台とサーバーの全表の行と blob が一致する。</item>
///   <item><b>消失なし</b>: キーごとに書き込みの履歴を持つ。最終値は初期値か、誰かが実際に書いた値でなければならない。
///   書き手が 1 人だけで、その書き手へそのキーの競合が報告されておらず、ServerWins の実行がその書き手の変更を捨てても
///   いないキーは、その書き手の最後の値でなければならない（後勝ち＋ServerWins の実行が外部キーに由来する競合を捨てた
///   キーも、捨てたものとして外す）
///   （削除も値として扱う。誰も書いていないキーは初期値のまま。blob だけの書き込みも行の書き手に数える＝再生は行を
///   丸ごと送り、行の単位で勝ち負けが決まるため）。blob も同じ規則で、空や NULL へ戻ったら違反。
///   さらに、書き手がその端末だけのキーへの版の競合（ModifiedOnServer / MissingOnServer / DuplicateOnServer）は
///   根拠の無い競合として違反にする（偽の競合はポリシーによって黙って値を捨てさせるため）。サーバーが書き込みを断った
///   理由の競合（DeleteRefusedOnServer / UniqueValueTakenOnServer / ParentMissingOnServer）と後勝ち＋ServerWins の
///   決着も、実行の直後のサーバーの状態に根拠が無ければ違反にする（<see cref="CheckRefusalBasisAsync"/>。注入した失敗は
///   根拠にならない）。Versioned＋ServerWins の実行が捨てた記録は確かめない（理由で分類せず範囲の未送信をすべて捨てる、
///   利用者が頼んだ操作のため）。</item>
///   <item><b>外部キー</b>: どの DB でも <c>PRAGMA foreign_key_check</c> が空（SQLite は書き込み時に検査するので、
///   これが崩れるのはエンジンが外部キーの検査を切ったまま書いたときだけ）。</item>
/// </list>
/// <para>
/// 書き手が 2 人以上のキーの期待値（ポリシーごとの編集×編集・削除×編集）は、乱数の種ではなく決定的なシナリオが
/// 確かめる（<see cref="SyncModelScenarios"/>）。
/// </para>
/// </remarks>
internal sealed class SyncModelRun : IAsyncDisposable
{
    /// <summary>静止化の周回の上限（1 周＝端末 2 台 × 2 モード）</summary>
    public const int QuiesceRounds = 6;

    private readonly SyncModelWorld _world;
    private readonly CancellationToken _ct;
    private readonly List<string> _log = [];
    private readonly List<ModelFailure> _failures = [];

    // (表, キー) → 書き込みの履歴（書き手, 行の正規の文字列・削除は null）
    private readonly Dictionary<
        (string Table, string Key),
        List<(ModelActor Actor, string? Row)>
    > _rows = [];

    // 品目のキー → blob の書き込みの履歴
    private readonly Dictionary<string, List<(ModelActor Actor, string Hex)>> _blobs = new(
        StringComparer.Ordinal
    );

    // (表, キー) → 競合を報告された端末
    private readonly Dictionary<(string Table, string Key), HashSet<ModelActor>> _conflictsTo = [];

    /// <summary>端末ごとの、直前の同期が自分から変えたと報告したローカルの行（ExpectLocalRowChangeOp が見る）</summary>
    private readonly Dictionary<
        ModelActor,
        IReadOnlyList<SyncLocalRowChange>
    > _lastLocalRowChanges = [];

    /// <summary>blob を書いた後でその品目が削除された（作り直されたら blob の期待値を決めない＝N12 (ii)）</summary>
    private readonly HashSet<string> _blobHistoryCut = new(StringComparer.Ordinal);

    /// <summary>だれかが消したことのある品目（行の書き手の操作として見えたもの）</summary>
    private readonly HashSet<string> _deletedItems = new(StringComparer.Ordinal);

    /// <summary>
    /// 消された後で、その品目の行をサーバーへ上げ直した端末（同期の前にサーバーに無く、その端末の同期の後にある＝挿入の
    /// 経路になった。N12 の種 982 の形＝作り直しの挿入の blob の送信が失敗し、空の blob が残る）
    /// </summary>
    private readonly Dictionary<string, HashSet<ModelActor>> _recreators = new(
        StringComparer.Ordinal
    );

    // 報告された競合（根拠の確認用）
    private readonly List<(
        ModelActor Device,
        string Table,
        string Key,
        SyncConflictReason Reason
    )> _conflicts = [];

    private ModelDbState _initial = new();

    private SyncModelRun(SyncModelWorld world, CancellationToken ct)
    {
        _world = world;
        _ct = ct;
    }

    /// <summary>実行した操作の列（人が読める 1 行 1 操作）</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>見つかった違反</summary>
    public IReadOnlyList<ModelFailure> Failures => _failures;

    /// <summary>世界</summary>
    public SyncModelWorld World => _world;

    /// <summary>
    /// 世界を作り、サーバーへ初期データを入れて、端末 2 台を初期データへそろえる。
    /// </summary>
    /// <param name="setup">初期データ（サーバーの他者の書き込みだけ）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="options">世界の組み方（既定は直結・SQLite のサーバー役）</param>
    public static async Task<SyncModelRun> StartAsync(
        IReadOnlyList<ModelWriteOp> setup,
        CancellationToken ct,
        SyncModelWorldOptions? options = null
    )
    {
        options ??= SyncModelWorldOptions.Direct;
        var run = new SyncModelRun(await SyncModelWorld.CreateAsync(options, ct), ct);

        foreach (var device in options.ExternalTables)
        {
            run._log.Add($"setup: {device} uses external ISyncTable implementations");
        }

        foreach (var op in setup)
        {
            run._log.Add("setup: " + op.Describe());
            await run._world.ApplyAsync(op, ct);
        }

        foreach (var device in run._world.Devices)
        {
            var op = Quiet(device.Actor, SyncMode.LastWriteWins);
            run._log.Add("setup: " + op.Describe());
            var outcome = await run._world.SyncAsync(op, ct);

            if (outcome.Conflicts.Count > 0 || outcome.Truncations.Count > 0)
            {
                run._failures.Add(new("0/setup", "The initial sync did not finish cleanly."));
            }
        }

        run._initial = await run._world.ReadStateAsync(ModelActor.Server, ct);

        if (!await run.ConvergedAsync())
        {
            run._failures.Add(new("0/setup", "The devices did not converge on the initial data."));
        }

        return run;
    }

    /// <summary>
    /// 静止化の 1 周の中身。まず Collect で送れるものを送り、残った競合を ServerWins で決着させ（ローカルの変更を捨てる＝
    /// 競合の解決として文書が勧める手順）、最後に後勝ちで版なしの表を回収する。後勝ちのパスも ServerWins で流す
    /// （版なしの表の外部キーに由来する 2 つの競合を、文書が勧める決着の道で解くため。後勝ちのランで ServerWins が効くのは
    /// その 2 つだけで、ほかは Collect と同じ）。
    /// </summary>
    private static readonly (SyncMode Mode, SyncConflictPolicy Policy)[] QuiescePasses =
    [
        (SyncMode.Versioned, SyncConflictPolicy.Collect),
        (SyncMode.Versioned, SyncConflictPolicy.ServerWins),
        (SyncMode.LastWriteWins, SyncConflictPolicy.ServerWins),
    ];

    /// <summary>静止化と初期データの同期で使う同期（全表・blob を運ぶ・バッチは既定）</summary>
    public static ModelSyncOp Quiet(
        ModelActor device,
        SyncMode mode,
        SyncConflictPolicy policy = SyncConflictPolicy.Collect
    ) => new(device, ModelSyncKind.Sync, mode, policy, [], true, 500);

    /// <summary>
    /// 操作を 1 つ流す。書き込みが期待どおりに効かなかったら（失敗する書き込み・注入した失敗・フックが飛ばした書き込み）
    /// false を返す。2 つ組の操作（削除→作り直し）の後半は、前半が効かなければ流さない（前半が効いた前提で選んだ
    /// 操作なので、流すと生成器の前提の崩れが違反に見える）。
    /// </summary>
    public async Task<bool> ExecuteAsync(ModelOp op)
    {
        var fired = _world.Faults.Fired;

        _log.Add(op.Describe());

        switch (op)
        {
            case ModelSyncOp sync:
                await SyncAsync(sync, quiescing: false);
                break;

            case ModelWriteOp write:
                return await WriteAsync(write) && _world.Faults.Fired == fired;

            case ArmFaultOp arm:
                _world.Faults.Arm(arm.Kind, arm.Device);
                break;

            case ArmInterruptOp interrupt:
                if (interrupt.Write.Actor != interrupt.Device)
                {
                    throw new ArgumentException(
                        "An interrupt writes on the device whose sync it interrupts.",
                        nameof(op)
                    );
                }

                _world.Faults.ArmInterrupt(
                    interrupt.Device,
                    interrupt.Point,
                    () => InterruptWriteAsync(interrupt)
                );
                break;

            case LegacyKeyTextsOp legacy:
                foreach (
                    var table in new[]
                    {
                        "quicker_sync_journal",
                        "quicker_sync_ack",
                        "quicker_sync_blob_pending",
                    }
                )
                {
                    await _world
                        .Device(legacy.Device)
                        .Sql.ExecuteSqlAsync(
                            $"UPDATE {table} SET key_text = key_text || @suffix WHERE table_name = @tableName",
                            new { suffix = legacy.Suffix, tableName = legacy.Table },
                            _ct
                        );
                }

                break;

            case ExpectPayloadOp expect:
            {
                var payloads = (await _world.ReadStateAsync(expect.Actor, _ct)).Payloads();
                var actual = payloads.TryGetValue(ModelDbState.Key(expect.ItemId), out var hex)
                    ? hex
                    : null;

                if (!string.Equals(actual, expect.PayloadHex, StringComparison.Ordinal))
                {
                    _failures.Add(
                        new(
                            "5/expect-payload:model_items",
                            $"{expect.Describe()}: got {actual ?? "(none)"}"
                        )
                    );
                }

                break;
            }

            case ExpectLocalRowChangeOp expect:
            {
                var reported = _lastLocalRowChanges.GetValueOrDefault(expect.Device) ?? [];

                if (
                    !reported.Contains(
                        new SyncLocalRowChange(expect.Table, expect.KeyText, expect.Reason)
                    )
                )
                {
                    _failures.Add(
                        new(
                            $"5/expect-row-change:{expect.Table}",
                            $"{expect.Describe()}: reported "
                                + string.Join(
                                    ", ",
                                    reported.Select(c => $"{c.TableName}/{c.KeyText}/{c.Reason}")
                                )
                        )
                    );
                }

                break;
            }

            case ExpectNoAckOp expect:
            {
                var acks = await _world
                    .Device(expect.Device)
                    .Journal.ReadAcksAsync(expect.Table, _ct);

                if (acks.ContainsKey(expect.KeyText))
                {
                    _failures.Add(
                        new(
                            $"5/expect-no-ack:{expect.Table}",
                            $"{expect.Describe()}: an ack is recorded"
                        )
                    );
                }

                break;
            }

            case ExpectLocalOp expect:
            {
                var actual = (await _world.ReadStateAsync(expect.Device, _ct)).Row(
                    expect.Table,
                    expect.KeyText
                );

                if (!string.Equals(actual, expect.Row, StringComparison.Ordinal))
                {
                    _failures.Add(
                        new(
                            $"5/expect-local:{expect.Table}",
                            $"{expect.Describe()}: got {actual ?? "(none)"}"
                        )
                    );
                }

                break;
            }

            case ModelRefreshOp { ExpectScopeRefusal: true } refusal:
            {
                var before = await _world.ReadStateAsync(refusal.Device, _ct);

                try
                {
                    await _world.RefreshAsync(refusal, _ct);
                    _failures.Add(new("refresh/not-refused", $"{op.Describe()} succeeded"));
                }
                catch (SyncRefreshScopeException ex)
                {
                    _log.Add($"  -> refused: {ex.Message}");
                }

                var after = await _world.ReadStateAsync(refusal.Device, _ct);

                foreach (var table in ModelTables.All)
                {
                    var was = before.Rows(table);
                    var now = after.Rows(table);

                    if (
                        was.Count != now.Count
                        || was.Any(pair =>
                            !now.TryGetValue(pair.Key, out var row)
                            || !string.Equals(row, pair.Value, StringComparison.Ordinal)
                        )
                    )
                    {
                        _failures.Add(
                            new(
                                $"refresh/refused-changed:{table}",
                                $"{op.Describe()} changed {table}"
                            )
                        );
                    }
                }

                break;
            }

            case ModelRefreshOp refresh:
                try
                {
                    await _world.RefreshAsync(refresh, _ct);
                }
                catch (Exception ex) when (InjectedFaultException.IsInjected(ex))
                {
                    _log.Add($"  -> injected fault: {ex.GetType().Name}");
                }
                catch (Exception ex)
                {
                    var phase = Phase(ex);
                    var diagnosis = await DiagnoseForeignKeyAsync(
                        ex,
                        phase,
                        new ModelSyncOp(
                            refresh.Device,
                            ModelSyncKind.Download,
                            refresh.Mode,
                            SyncConflictPolicy.Collect,
                            [],
                            refresh.IncludeBlobs,
                            500
                        )
                    );
                    _failures.Add(
                        new($"1/throw:{phase}:{ErrorText(ex)}{diagnosis}", $"{op.Describe()}: {ex}")
                    );
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op.Describe(), null);
        }

        return true;
    }

    /// <summary>
    /// 割り込みの書き込みを流す（同期の実行と別の流れから呼ばれる。実行はサーバー役の点で割り込みの終わりを待つので、操作の記録と
    /// 履歴は逐次のまま）。書き込みは通常の書き込みと同じく書き手の操作として履歴へ積む
    /// </summary>
    private async Task InterruptWriteAsync(ArmInterruptOp interrupt)
    {
        _log.Add($"  -> interrupt {interrupt.Point}: {interrupt.Write.Describe()}");
        await WriteAsync(interrupt.Write);
    }

    /// <summary>書き込みを 1 つ流す（期待どおりに効いたら true）</summary>
    private async Task<bool> WriteAsync(ModelWriteOp op)
    {
        var before = await _world.ReadStateAsync(op.Actor, _ct);
        var applied = true;

        try
        {
            await _world.ApplyAsync(op, _ct);

            if (op.ExpectFailure)
            {
                _failures.Add(
                    new($"op/unexpected-success:{op.GetType().Name}", $"{op.Describe()} succeeded")
                );
            }
        }
        catch (Exception ex) when (op.ExpectFailure || InjectedFaultException.IsInjected(ex))
        {
            // 失敗する書き込み・注入した失敗。ジャーナルの記録だけが残る（journal-first）
            _log.Add($"  -> write failed: {ex.GetType().Name}");
            applied = false;
        }
        catch (Exception ex)
        {
            applied = false;
            // 失敗の注入の無い操作は、生成器が書き手の DB を見て選んでいるので失敗しない。失敗したらハーネスか
            // エンジンの記録の側の不具合として名指しする
            _failures.Add(
                new(
                    $"op/failed:{op.GetType().Name}:{ErrorText(ex)}",
                    $"{op.Describe()}: {ex.GetType().Name}: {ex.Message}"
                )
            );
        }

        // 書き込みの前後の内容を比べ、実際に変わった行と blob だけを履歴へ積む（失敗した書き込み・フックが飛ばした
        // 書き込みは何も積まない。キーを決めるフックが変えたキーも、変わった行として拾える）
        RecordChanges(op.Actor, before, await _world.ReadStateAsync(op.Actor, _ct));
        await CheckForeignKeysAsync(op.Actor, op.Describe());

        return applied;
    }

    private void RecordChanges(ModelActor actor, ModelDbState before, ModelDbState after)
    {
        foreach (var table in ModelTables.All)
        {
            var was = before.Rows(table);
            var now = after.Rows(table);

            foreach (var key in was.Keys.Union(now.Keys, StringComparer.Ordinal))
            {
                was.TryGetValue(key, out var oldRow);
                now.TryGetValue(key, out var newRow);

                if (!string.Equals(oldRow, newRow, StringComparison.Ordinal))
                {
                    Append(_rows, (table, key), (actor, newRow));

                    // blob を書いた品目が消えたら、それ以前の blob の書き込みは期待値の根拠にならない（作り直した端末の
                    // blob が入る＝挿入は全列を送る）
                    if (table == ModelTables.Items && newRow is null && _blobs.ContainsKey(key))
                    {
                        _blobHistoryCut.Add(key);
                    }

                    if (table == ModelTables.Items && newRow is null)
                    {
                        _deletedItems.Add(key);
                    }
                }
            }
        }

        var oldPayloads = before.Payloads();

        foreach (var (key, hex) in after.Payloads())
        {
            if (!oldPayloads.TryGetValue(key, out var oldHex) || oldHex != hex)
            {
                Append(_blobs, key, (actor, hex));
            }
        }
    }

    private async Task<bool> SyncAsync(ModelSyncOp op, bool quiescing)
    {
        if (
            op is { Mode: SyncMode.Versioned, Policy: SyncConflictPolicy.ServerWins }
            && op.Kind != ModelSyncKind.Download
        )
        {
            await RecordServerWinsDiscardsAsync(op);
        }

        // アップロードする実行の前の状態（サーバー・その端末のローカル・未送信の記録・注入した失敗の控えの位置）。
        // 後勝ち＋ServerWins の決着（どれを捨てたかは返らない）の推定と、競合・決着の理由の根拠の確認が使う
        var before = op.Kind != ModelSyncKind.Download ? await SnapshotAsync(op.Device) : null;

        // 後勝ち＋ServerWins の実行は、外部キー・一意な値に由来する競合だけを競合として返さずに捨てる。実行の前の未送信の
        // 記録と、実行の後で突き合わせる（RecordLastWriteWinsSettlementsAsync）
        var settles =
            op is { Mode: SyncMode.LastWriteWins, Policy: SyncConflictPolicy.ServerWins }
            && before is not null;

        ModelSyncOutcome outcome;
        var fired = _world.Faults.Fired;

        // 消された品目をこの同期が上げ直したか（挿入の経路になった）を、サーバーの行の有無の前後で見る
        var serverItemsBefore = before
            ?.Server.Rows(ModelTables.Items)
            .Keys.ToHashSet(StringComparer.Ordinal);

        try
        {
            outcome = await _world.SyncAsync(op, _ct);
        }
        catch (Exception ex)
            when (InjectedFaultException.IsInjected(ex)
                // HTTP 越しでは、サーバー側で起きた注入の例外は 500 の応答として届く（型が変わる）。その実行の間に
                // 仕掛けた失敗が起きたかで見分ける
                || (_world.Http && _world.Faults.Fired != fired)
            )
        {
            _log.Add($"  -> injected fault: {ex.GetType().Name}");
            await RecordRecreatorsAsync(op.Device, serverItemsBefore);
            _lastLocalRowChanges[op.Device] =
                ex.Data[SyncEngine.LocalRowChangesDataKey] as IReadOnlyList<SyncLocalRowChange>
                ?? [];
            RecordOutcomes(op, quiescing, [], _lastLocalRowChanges[op.Device]);

            // 例外で終わった後勝ち＋ServerWins の実行も、例外の前に決着させた変更を捨てている（結果は返らない）。
            // 実行の前の未送信の記録と突き合わせ、記録が消えた行を捨てた行とみなす（N11 の案 1）。決着の理由の根拠も確かめる
            if (settles)
            {
                await CheckRefusalBasisAsync(
                    op,
                    before!,
                    await RecordLastWriteWinsSettlementsAsync(
                        op,
                        before!,
                        _lastLocalRowChanges[op.Device]
                    ),
                    []
                );
            }

            return false;
        }
        catch (Exception ex)
        {
            var phase = Phase(ex);
            var diagnosis = await DiagnoseForeignKeyAsync(ex, phase, op);
            _failures.Add(
                new(
                    $"1/{(quiescing ? "quiesce-throw" : "throw")}:{phase}:{ErrorText(ex)}{diagnosis}",
                    $"{op.Describe()}: {ex}"
                )
            );

            return false;
        }

        _lastLocalRowChanges[op.Device] = outcome.LocalRowChanges;
        RecordOutcomes(op, quiescing, outcome.Conflicts, outcome.LocalRowChanges);
        await RecordRecreatorsAsync(op.Device, serverItemsBefore);

        foreach (var change in outcome.LocalRowChanges)
        {
            _log.Add(
                $"  -> local {ModelTables.Short(change.TableName)} key={CanonicalKey(change.TableName, change.KeyText)} {change.Reason}"
            );
        }

        foreach (var conflict in outcome.Conflicts)
        {
            var key = CanonicalKey(conflict.TableName, conflict.KeyText);
            _log.Add(
                $"  -> conflict {ModelTables.Short(conflict.TableName)} key={key} "
                    + $"op={conflict.Operation} reason={conflict.Reason}"
            );

            if (!_conflictsTo.TryGetValue((conflict.TableName, key), out var set))
            {
                _conflictsTo[(conflict.TableName, key)] = set = [];
            }

            set.Add(op.Device);
            _conflicts.Add((op.Device, conflict.TableName, key, conflict.Reason));
        }

        foreach (var truncation in outcome.Truncations)
        {
            _log.Add(
                $"  -> truncated {ModelTables.Short(truncation.TableName)} "
                    + $"pending={truncation.PendingKeyText ?? "-"} "
                    + $"by={truncation.TruncatedByTableName ?? "-"} "
                    + $"parent={(truncation.MissingParentTableName is { } parent ? ModelTables.Short(parent) : "-")}"
            );
        }

        if (before is not null)
        {
            // 外部キー・一意な値に由来する競合と、後勝ち＋ServerWins の決着は、理由の根拠を実行の直後のサーバーで確かめる
            var refusals = outcome
                .Conflicts.Where(c => RefusalReasons.Contains(c.Reason))
                .Select(c =>
                    (
                        c.TableName,
                        CanonicalKey(c.TableName, c.KeyText),
                        (SyncConflictReason?)c.Reason
                    )
                )
                .ToList();

            if (settles)
            {
                refusals.AddRange(
                    await RecordLastWriteWinsSettlementsAsync(op, before, outcome.LocalRowChanges)
                );
            }

            await CheckRefusalBasisAsync(
                op,
                before,
                refusals,
                outcome.Conflicts.Select(c => (c.TableName, CanonicalKey(c.TableName, c.KeyText)))
            );
        }

        await CheckForeignKeysAsync(op.Device, op.Describe());

        if (quiescing)
        {
            foreach (var conflict in outcome.Conflicts)
            {
                _quiesceConflicts.Add(
                    (
                        op.Device,
                        conflict.TableName,
                        CanonicalKey(conflict.TableName, conflict.KeyText),
                        conflict.Reason
                    )
                );
            }

            foreach (var truncation in outcome.Truncations)
            {
                _quiesceTruncations.Add(
                    (
                        op.Device,
                        truncation.TableName,
                        truncation.PendingKeyText is { } key
                            ? CanonicalKey(truncation.TableName, key)
                            : null
                    )
                );
            }
        }

        return outcome.Conflicts.Count == 0 && outcome.Truncations.Count == 0;
    }

    /// <summary>
    /// 静止化でない同期（生成器・シナリオの操作）が返した競合と、自分から変えた行の理由を、その同期のモードとポリシーと一緒に
    /// 控える（結果の次元表 <see cref="SyncModelOutcomeDimensions"/> が数える。静止化は決まったモードとポリシーで流すので数えない）
    /// </summary>
    private void RecordOutcomes(
        ModelSyncOp op,
        bool quiescing,
        IReadOnlyList<SyncConflict> conflicts,
        IReadOnlyList<SyncLocalRowChange> changes
    )
    {
        if (quiescing)
        {
            return;
        }

        foreach (var conflict in conflicts)
        {
            _outcomes.Add((op.Mode, op.Policy, conflict.Reason.ToString()));
        }

        foreach (var change in changes)
        {
            _outcomes.Add((op.Mode, op.Policy, change.Reason.ToString()));
        }
    }

    private readonly List<(SyncMode Mode, SyncConflictPolicy Policy, string Outcome)> _outcomes =
    [];

    /// <summary>静止化でない同期が返した競合・自分から変えた行の理由（モード・ポリシーつき）</summary>
    public IReadOnlyList<(SyncMode Mode, SyncConflictPolicy Policy, string Outcome)> Outcomes =>
        _outcomes;

    /// <summary>消された品目のうち、この同期の前にサーバーに無く、後にあるもの＝この端末が上げ直したものを控える。</summary>
    private async Task RecordRecreatorsAsync(ModelActor device, HashSet<string>? serverItemsBefore)
    {
        if (serverItemsBefore is null)
        {
            return;
        }

        foreach (
            var key in (await _world.ReadStateAsync(ModelActor.Server, _ct))
                .Rows(ModelTables.Items)
                .Keys
        )
        {
            if (!serverItemsBefore.Contains(key) && _deletedItems.Contains(key))
            {
                if (!_recreators.TryGetValue(key, out var set))
                {
                    _recreators[key] = set = [];
                }

                set.Add(device);
            }
        }
    }

    /// <summary>
    /// Versioned＋ServerWins の実行は、その範囲の未送信のジャーナルを送らずに捨て、サーバーの行で上書きする（この端末の
    /// ローカルの変更を捨てると頼む操作）。捨てる行は、競合を報告された行と同じく、その端末の書き込みを期待値から外す。
    /// </summary>
    private async Task RecordServerWinsDiscardsAsync(ModelSyncOp op)
    {
        var excluded = op.Excluded.Append(ModelTables.Labels).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in await _world.Device(op.Device).Journal.ReadAllAsync(_ct))
        {
            if (excluded.Contains(entry.TableName))
            {
                continue;
            }

            var key = (entry.TableName, CanonicalKey(entry.TableName, entry.KeyText));

            if (!_conflictsTo.TryGetValue(key, out var set))
            {
                _conflictsTo[key] = set = [];
            }

            set.Add(op.Device);
        }
    }

    /// <summary>
    /// 後勝ち＋ServerWins の実行が捨てた変更を、競合を報告された行と同じく、その端末の書き込みを期待値から外す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 捨てた行は単独のアップロードでは返らないので、実行の前に未送信だったキーのうち、記録が全部消えたのに、サーバーの行が
    /// 実行の前のその端末のローカルの行（送った値）になっていないものを、捨てた行とみなす（後勝ちのランは版で弾かないので、
    /// 送った変更はそのままサーバーの値になる＝同じ実行の中でほかの書き手は動かない）。
    /// </para>
    /// <para>
    /// 送った値を書き込みの履歴の最後の値で代えてはいけない（blob だけを書いた品目は行の履歴に書き手が無く、「削除を書いた」と
    /// 取り違える）。送るものが無くて記録を捨てた行（行ありで削除だけ・行なしで削除なし）は決着ではない。
    /// </para>
    /// </remarks>
    /// <returns>捨てたとみなしたキー（理由はエンジンが返さないので null。根拠の確認へ渡す）</returns>
    private async Task<
        List<(string Table, string Key, SyncConflictReason? Reason)>
    > RecordLastWriteWinsSettlementsAsync(
        ModelSyncOp op,
        RunSnapshot before,
        IReadOnlyList<SyncLocalRowChange> reported
    )
    {
        var pendingBefore = before.Pending;
        var localBefore = before.Local;
        var localAfter = await _world.ReadStateAsync(op.Device, _ct);
        var discarded = reported
            .Where(c => c.Reason == SyncLocalRowChangeReason.DiscardedByServerWins)
            .Select(c => (c.TableName, CanonicalKey(c.TableName, c.KeyText)))
            .ToHashSet();
        var settled = new List<(string Table, string Key, SyncConflictReason? Reason)>();
        var remaining = (await _world.Device(op.Device).Journal.ReadAllAsync(_ct))
            .Select(entry => entry.Id)
            .ToHashSet();
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);

        foreach (
            var row in pendingBefore.GroupBy(entry =>
                (entry.TableName, Key: CanonicalKey(entry.TableName, entry.KeyText))
            )
        )
        {
            if (row.Any(entry => remaining.Contains(entry.Id)))
            {
                continue;
            }

            var serverRow = server.Row(row.Key.TableName, row.Key.Key);
            var localRow = localBefore.Row(row.Key.TableName, row.Key.Key);
            var upserts = row.Any(e => e.Operation == nameof(SyncJournalOperation.Upsert));
            var deletes = row.Any(e => e.Operation == nameof(SyncJournalOperation.Delete));

            // 送るものが無い記録（行ありで削除だけ・行なしで削除なし）は、エンジンが送らずに捨てる（決着ではない）
            if ((localRow is not null && !upserts) || (localRow is null && !deletes))
            {
                continue;
            }

            // 送った値（実行の前のローカルの行）がサーバーの値になっていれば届いた。届かなかった行のうち、ServerWins が
            // サーバーの行でローカルを戻した行（実行の後のローカルの行がサーバーの行と一致する）・サーバーに行が無い upsert
            // （ServerWins は戻す行が無いのでローカルを変えない）・実行が捨てたと報告した行だけを捨てた行とみなす
            if (
                string.Equals(serverRow, localRow, StringComparison.Ordinal)
                || (
                    !string.Equals(
                        serverRow,
                        localAfter.Row(row.Key.TableName, row.Key.Key),
                        StringComparison.Ordinal
                    )
                    && serverRow is not null
                    && !discarded.Contains((row.Key.TableName, row.Key.Key))
                )
            )
            {
                continue;
            }

            _log.Add($"  -> settled {ModelTables.Short(row.Key.TableName)} key={row.Key.Key}");

            if (!_conflictsTo.TryGetValue((row.Key.TableName, row.Key.Key), out var set))
            {
                _conflictsTo[(row.Key.TableName, row.Key.Key)] = set = [];
            }

            set.Add(op.Device);
            settled.Add((row.Key.TableName, row.Key.Key, null));
        }

        return settled;
    }

    /// <summary>後勝ち＋ServerWins の upsert の決着（理由はエンジンが返さない）を数え・名指しするときの理由の名前</summary>
    public const string SettledUpsertKind = "settled-upsert";

    /// <summary>根拠を確かめる競合の理由（サーバーが書き込みを断った理由＝外部キー・一意な値）</summary>
    private static readonly HashSet<SyncConflictReason> RefusalReasons =
    [
        SyncConflictReason.DeleteRefusedOnServer,
        SyncConflictReason.UniqueValueTakenOnServer,
        SyncConflictReason.ParentMissingOnServer,
    ];

    /// <summary>アップロードする実行の前の状態（根拠の確認と決着の推定が使う）</summary>
    private sealed record RunSnapshot(
        ModelDbState Server,
        ModelDbState Local,
        IReadOnlyList<SyncJournalRow> Pending,
        int HitIndex
    );

    private async Task<RunSnapshot> SnapshotAsync(ModelActor device) =>
        new(
            await _world.ReadStateAsync(ModelActor.Server, _ct),
            await _world.ReadStateAsync(device, _ct),
            await _world.Device(device).Journal.ReadAllAsync(_ct),
            _world.Faults.WriteHitCount
        );

    /// <summary>理由ごとの、根拠を確かめた件数と根拠のあった件数（空振りしていないかを出力で確かめる）</summary>
    public IReadOnlyDictionary<string, (int Checked, int WithBasis)> BasisChecks => _basisChecks;

    private readonly Dictionary<string, (int Checked, int WithBasis)> _basisChecks = new(
        StringComparer.Ordinal
    );

    // 根拠の無い競合・決着の違反（静止化の成否に依らず最後に足す＝静止化の中で見つけても静止化を打ち切らない）
    private readonly List<ModelFailure> _basisFailures = [];

    /// <summary>
    /// サーバーが書き込みを断った理由（外部キー・一意な値）で分類された競合と、後勝ち＋ServerWins の決着に、実行の直後の
    /// サーバーの状態で根拠があるかを確かめ、無ければ違反 <c>3/spurious:{表}:{理由}:{形}</c> にする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 根拠はサーバーの状態からだけ取る（注入した失敗は根拠にならない。同じ実行の中でサーバーを変えるのはこの端末の
    /// アップロードだけなので、実行の直後のサーバーは、その実行が最後に見たサーバー）。送った値は実行の前のこの端末の
    /// ローカルの行（アップロードはダウンロードより前に走り、そのときのローカルの行を送る）。
    /// </para>
    /// <list type="bullet">
    ///   <item>DeleteRefusedOnServer：サーバーに、そのキーを参照する行がある。ただし、その行がこの端末で未送信の付け替え
    ///   （ローカルの行がもうそのキーを参照しない）で、その表がこの実行の範囲にあり、この実行で断られていないなら数えない
    ///   （その付け替えは同じ実行で届くはずなので、削除を先に断るのは順序の誤り）。</item>
    ///   <item>UniqueValueTakenOnServer：サーバーに、送った行と同じ一意な値を持つ別の行がある。ただし、その持ち主が
    ///   この端末で未送信で値を手放し（ローカルの行がもうその値を持たない）、この実行で断られていないなら数えない。</item>
    ///   <item>ParentMissingOnServer：送った行が参照する親のどれかがサーバーに無い。</item>
    ///   <item>後勝ち＋ServerWins の決着（理由は返らない）：削除なら DeleteRefusedOnServer、upsert なら
    ///   ParentMissingOnServer か UniqueValueTakenOnServer の根拠のどちらか。</item>
    /// </list>
    /// <para>
    /// 根拠の無いものは形で分ける：その実行で注入した失敗がそのキーに当たった（<c>after-fault</c>）・実行の前にそのキーを
    /// 参照していた行がこの端末の未送信で付け替わる（<c>pending-reparent</c>）・実行の前に同じ値を持っていた行がこの端末の
    /// 未送信で値を手放す（<c>pending-release</c>）・それ以外（<c>no-basis</c>）。根拠の無いものも消失なしの期待からは
    /// 外したままにする（消失の違反を重ねない＝1 つの形に 1 つの署名。版の競合の根拠の無い違反と同じ扱い）。
    /// </para>
    /// </remarks>
    private async Task CheckRefusalBasisAsync(
        ModelSyncOp op,
        RunSnapshot before,
        IReadOnlyList<(string Table, string Key, SyncConflictReason? Reason)> refusals,
        IEnumerable<(string Table, string Key)> conflicted
    )
    {
        if (refusals.Count == 0)
        {
            return;
        }

        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var hits = _world
            .Faults.WriteHitsSince(before.HitIndex)
            .Where(h => h.Device == op.Device)
            .Select(h => (h.Table, h.Key))
            .ToHashSet();
        var pending = before
            .Pending.Select(e => (e.TableName, CanonicalKey(e.TableName, e.KeyText)))
            .ToHashSet();
        // この実行で届かなかった行（理由を問わず競合になった行と決着させた行）。その行の付け替え・値の手放しは届いて
        // いないので、根拠を打ち消さない
        var refused = refusals.Select(r => (r.Table, r.Key)).Concat(conflicted).ToHashSet();

        // その実行が送る表（Versioned は版なしの表を送らない・除外した表も送らない）。範囲外の表の未送信の付け替え・
        // 値の手放しはその実行では届かないので、根拠を打ち消さない
        var inScope = ModelTables
            .All.Where(t =>
                !op.Excluded.Contains(t)
                && !(op.Mode == SyncMode.Versioned && t == ModelTables.Labels)
            )
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (table, key, reason) in refusals)
        {
            var deleting = before.Local.Row(table, key) is null;
            var kind =
                reason?.ToString()
                ?? (
                    deleting ? nameof(SyncConflictReason.DeleteRefusedOnServer) : SettledUpsertKind
                );
            var basis = reason switch
            {
                SyncConflictReason.DeleteRefusedOnServer => HasReferrer(server),
                SyncConflictReason.UniqueValueTakenOnServer => HasHolder(server),
                SyncConflictReason.ParentMissingOnServer => ParentMissing(),
                _ => deleting ? HasReferrer(server) : ParentMissing() || HasHolder(server),
            };

            var (count, withBasis) = _basisChecks.GetValueOrDefault(kind);
            _basisChecks[kind] = (count + 1, withBasis + (basis ? 1 : 0));

            if (basis)
            {
                continue;
            }

            var shape =
                hits.Contains((table, key)) ? "after-fault"
                : deleting && Referrers(before.Server, table, key).Any(IsReparented)
                    ? "pending-reparent"
                : !deleting && Holders(before.Server).Any(IsReleased) ? "pending-release"
                : "no-basis";
            _basisFailures.Add(
                new(
                    $"3/spurious:{table}:{kind}:{shape}",
                    $"{op.Device} got {kind} on {key} with no basis on the server after {op.Describe()} "
                        + $"(sent: {before.Local.Row(table, key) ?? "(deleted)"}; "
                        + $"server: {server.Row(table, key) ?? "(none)"}; journal: "
                        + string.Join(
                            ",",
                            before
                                .Pending.Where(e =>
                                    e.TableName == table && CanonicalKey(table, e.KeyText) == key
                                )
                                .Select(e => e.Operation)
                        )
                        + ")"
                )
            );

            continue;

            // サーバーにそのキーを参照する行がある（この端末の未送信の付け替えで、この実行で断られていない行は除く）
            bool HasReferrer(ModelDbState state) =>
                Referrers(state, table, key).Any(r => !IsReparented(r));

            // サーバーに同じ一意な値を持つ別の行がある（この端末の未送信で値を手放し、この実行で断られていない行は除く）
            bool HasHolder(ModelDbState state) => Holders(state).Any(h => !IsReleased(h));

            // 送った行が参照する親のどれかがサーバーに無い
            bool ParentMissing() =>
                References
                    .Where(r => r.Child == table)
                    .Any(r =>
                        r.Read(before.Local).FirstOrDefault(p => p.Key == key).ParentKey
                            is { } parentKey
                        && server.Row(r.Parent, parentKey) is null
                    );

            IEnumerable<string> Holders(ModelDbState state) =>
                UniqueValue(before.Local, table, key) is { } value
                    ? state
                        .Rows(table)
                        .Keys.Where(k =>
                            k != key
                            && string.Equals(
                                UniqueValue(state, table, k),
                                value,
                                StringComparison.Ordinal
                            )
                        )
                    : [];

            // 持ち主 h がこの端末の未送信で値を手放した（ローカルの行がまだあり、値が違う）
            bool IsReleased(string holder) =>
                inScope.Contains(table)
                && pending.Contains((table, holder))
                && !refused.Contains((table, holder))
                && before.Local.Row(table, holder) is not null
                && !string.Equals(
                    UniqueValue(before.Local, table, holder),
                    UniqueValue(before.Local, table, key),
                    StringComparison.Ordinal
                );

            // 参照する行 r がこの端末の未送信で付け替わった（ローカルの行がまだあり、もうこのキーを参照しない）
            bool IsReparented((string Table, string Key, int Reference) referrer)
            {
                if (
                    !inScope.Contains(referrer.Table)
                    || !pending.Contains((referrer.Table, referrer.Key))
                    || refused.Contains((referrer.Table, referrer.Key))
                )
                {
                    return false;
                }

                var local = References[referrer.Reference]
                    .Read(before.Local)
                    .Where(p => p.Key == referrer.Key)
                    .ToList();

                return local.Count > 0 && local[0].ParentKey != key;
            }
        }
    }

    /// <summary>その状態で、表 <paramref name="table"/> のキー <paramref name="key"/> を参照する行（表・キー・外部キーの番号）</summary>
    private static IEnumerable<(string Table, string Key, int Reference)> Referrers(
        ModelDbState state,
        string table,
        string key
    ) =>
        References
            .Select((reference, index) => (reference, index))
            .Where(r => r.reference.Parent == table)
            .SelectMany(r =>
                r.reference.Read(state)
                    .Where(p => p.ParentKey == key)
                    .Select(p => (r.reference.Child, p.Key, r.index))
            );

    /// <summary>行の一意な値（フォルダーの name・品目の code。それ以外の表と、行が無ければ null）</summary>
    private static string? UniqueValue(ModelDbState state, string table, string key) =>
        table switch
        {
            ModelTables.Folders
                when state.Folders.TryGetValue(
                    int.Parse(key, CultureInfo.InvariantCulture),
                    out var folder
                ) => folder.Name,
            ModelTables.Items
                when state.Items.TryGetValue(
                    int.Parse(key, CultureInfo.InvariantCulture),
                    out var item
                ) => item.Code,
            _ => null,
        };

    /// <summary>
    /// 静止化してから、収束と消失なしを確かめる（静止化が失敗したら、その失敗だけを報告する＝後段の表明は
    /// 静止化した状態を前提にしており、静止化の失敗に引きずられた違反で署名を濁さない）。
    /// </summary>
    public async Task FinishAsync()
    {
        // 仕掛けたのに点に来なかった割り込みは違反（点の置き場所がずれた・点に来ない操作の列＝黙った空振り）。
        // 割り込みの書き込みが上限の時間で終わらなかったのも違反（エンジンがロックを持ったまま点に来た疑い）
        if (_world.Faults.PendingInterrupt is { } pending)
        {
            _failures.Add(
                new(
                    $"interrupt/not-fired:{pending}",
                    "The armed interrupt never reached its point."
                )
            );
        }

        if (_world.Faults.InterruptTimedOut is { } timedOut)
        {
            _failures.Add(
                new(
                    $"interrupt/deadlock:{timedOut}",
                    "The interrupt write did not finish in time (the engine may hold the local write lock at the point)."
                )
            );
        }

        // 仕掛けたまま起きなかった失敗と割り込みは外す（静止化は失敗の無い世界で行う）
        _world.Faults.Clear();
        var quiesced = await QuiesceAsync();

        // 根拠の無い競合・決着は、静止化の成否に依らず報告する（静止化の中で見つけたものを含む）
        _failures.AddRange(Distinct(_basisFailures));

        if (!quiesced)
        {
            return;
        }

        await CheckConvergenceAsync();
        await CheckNoLossAsync();
    }

    private async Task<bool> QuiesceAsync()
    {
        string? reason = null;
        var settled = false;

        for (var round = 0; round < QuiesceRounds; round++)
        {
            var clean = true;
            _quiesceConflicts.Clear();
            _quiesceTruncations.Clear();

            foreach (var (mode, policy) in QuiescePasses)
            {
                foreach (var device in _world.Devices)
                {
                    var op = Quiet(device.Actor, mode, policy);
                    _log.Add("quiesce: " + op.Describe());
                    var before = _failures.Count;
                    clean &= await SyncAsync(op, quiescing: true);

                    if (_failures.Count > before)
                    {
                        return false;
                    }
                }
            }

            var pending = 0;

            foreach (var device in _world.Devices)
            {
                pending += await device.PendingCountAsync(_ct);
            }

            if (!clean)
            {
                reason = "conflicts-or-truncations";
                continue;
            }

            if (pending > 0)
            {
                reason = "journal";
                continue;
            }

            // 送るものが無くなった周でも、その周の中で後から上がった変更は先に回った端末へまだ届いていない。もう 1 周
            // 回して届ける（それでも一致しなければ、静止したものとして収束の表明に任せる）
            if (settled || await ConvergedAsync())
            {
                return true;
            }

            settled = true;
        }

        var classified =
            reason == "conflicts-or-truncations" ? await ClassifyStuckTruncationsAsync() : null;

        foreach (var signature in classified?.Signatures ?? [])
        {
            _failures.Add(
                new(
                    signature,
                    $"The run did not settle within {QuiesceRounds} rounds ({signature})."
                )
            );
        }

        if (classified is null || classified.Unclassified)
        {
            _failures.Add(
                new(
                    $"1/no-quiesce:{reason}",
                    $"The run did not settle within {QuiesceRounds} rounds ({reason})."
                )
            );
        }

        return false;
    }

    // 静止化の最後の周で返った競合と打ち切り（端末・表・行のキー）
    private readonly List<(
        ModelActor Device,
        string Table,
        string Key,
        SyncConflictReason Reason
    )> _quiesceConflicts = [];
    private readonly List<(ModelActor Device, string Table, string? Key)> _quiesceTruncations = [];

    /// <summary>静止化の失敗の分類（分類できた署名と、分類できないものが残ったか）</summary>
    private sealed record StuckClassification(IReadOnlyList<string> Signatures, bool Unclassified);

    /// <summary>
    /// 静止化の最後の周に残った打ち切りを 1 件ずつ分類する。(a) 一意な値の厳密な入れ替え（打ち切った行 R のサーバーの値を
    /// ローカルの別の行 H が持ち、H のサーバーの値を R がローカルで持つ）＝<c>1/no-quiesce:unique-swap:{表}</c>。
    /// (b) R のサーバーの値をローカルで持つ H がサーバーに無く、R のローカルの行が H を参照する（値を取る行自身の古い参照が
    /// 持ち主を残す）＝<c>1/no-quiesce:holder-referenced-by-taker:{表}</c>。続いて、説明の付いていない競合の行を端末・表ごとに集め、
    /// (c) 端末側の一意な値の循環（どの行もローカルの値を、集まりの別の行がサーバーで持つ）＝<c>1/no-quiesce:pending-unique-cycle:{表}</c>
    /// に分ける。どれにも当たらない打ち切りか、説明の付かない競合が最後の周に残ったら、分類できないものがある（呼び出し側が粗い署名もあわせて出す＝分類できた分が隠さない）。
    /// </summary>
    private async Task<StuckClassification> ClassifyStuckTruncationsAsync()
    {
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var signatures = new SortedSet<string>(StringComparer.Ordinal);
        var unclassified = _quiesceTruncations.Count == 0 && _quiesceConflicts.Count == 0;
        var explained = new HashSet<(ModelActor, string, string)>();
        var leftTruncations = new List<(ModelActor Device, string Table, string? Key)>();

        // (a)・(b)：打ち切りを 1 件ずつ分類する（打ち切った行と、その形の持ち主の行は説明済み）
        foreach (var (device, table, key) in _quiesceTruncations.Distinct())
        {
            var local = await _world.ReadStateAsync(device, _ct);
            var stuck = key is null ? null : StuckShape(server, local, table, key);

            if (stuck is not { } found)
            {
                leftTruncations.Add((device, table, key));
                continue;
            }

            signatures.Add($"1/no-quiesce:{found.Shape}:{table}");
            explained.Add((device, table, key!));
            explained.Add((device, table, found.Holder));
        }

        // (c) 端末側の一意な値の循環：説明の付いていない競合の行を端末・表ごとに集め、どの行もローカルの値を、集まりの別の行が
        // サーバーで持つ（値はどれも別々。2 行の入れ替えも 3 行以上の循環も同じ形＝未送信の値の並べ替えを、行ごとの書き込みでは
        // 上げられない）＝<c>1/no-quiesce:pending-unique-cycle:{表}</c>。集まりの行の競合・打ち切りは説明済み
        foreach (
            var group in _quiesceConflicts
                .Where(c => !explained.Contains((c.Device, c.Table, c.Key)))
                .GroupBy(c => (c.Device, c.Table))
        )
        {
            var local = await _world.ReadStateAsync(group.Key.Device, _ct);
            var rows = group.Select(c => c.Key).Distinct(StringComparer.Ordinal).ToList();

            if (
                rows.Count >= 2
                && group.Any(c => c.Reason == SyncConflictReason.UniqueValueTakenOnServer)
                && IsPendingCycle(server, local, group.Key.Table, rows)
            )
            {
                signatures.Add($"1/no-quiesce:pending-unique-cycle:{group.Key.Table}");

                foreach (var row in rows)
                {
                    explained.Add((group.Key.Device, group.Key.Table, row));
                }
            }
        }

        // 分類できない打ち切りか、説明の付かない競合が残れば、分類できないものがある
        if (
            leftTruncations.Any(t =>
                t.Key is null || !explained.Contains((t.Device, t.Table, t.Key))
            ) || _quiesceConflicts.Any(c => !explained.Contains((c.Device, c.Table, c.Key)))
        )
        {
            unclassified = true;
        }

        return new([.. signatures], unclassified);
    }

    /// <summary>
    /// 行の集まりが端末側の一意な値の循環か：どの行もローカルの値を持ち、その値を集まりの別の行がサーバーで持つ（値はどれも
    /// 別々）
    /// </summary>
    private static bool IsPendingCycle(
        ModelDbState server,
        ModelDbState local,
        string table,
        IReadOnlyList<string> rows
    )
    {
        var localValues = rows.Select(row => UniqueValue(local, table, row)).ToList();

        if (
            localValues.Any(value => value is null)
            || localValues.Distinct(StringComparer.Ordinal).Count() != rows.Count
        )
        {
            return false;
        }

        return rows.All(row =>
            rows.Any(other =>
                other != row
                && string.Equals(
                    UniqueValue(server, table, other),
                    UniqueValue(local, table, row),
                    StringComparison.Ordinal
                )
            )
        );
    }

    /// <summary>打ち切った行の形（unique-swap・holder-referenced-by-taker）と、その形の持ち主の行（どちらでもなければ null）</summary>
    private static (string Shape, string Holder)? StuckShape(
        ModelDbState server,
        ModelDbState local,
        string table,
        string key
    )
    {
        if (UniqueValue(server, table, key) is not { } wanted)
        {
            return null;
        }

        var holders = local
            .Rows(table)
            .Keys.Where(k =>
                k != key
                && string.Equals(UniqueValue(local, table, k), wanted, StringComparison.Ordinal)
            )
            .ToList();

        foreach (var holder in holders)
        {
            // (a) 厳密な入れ替え：持ち主のサーバーの値が、打ち切った行のローカルの値と同じ（片側だけの一致は数えない）
            if (
                UniqueValue(server, table, holder) is { } given
                && string.Equals(given, UniqueValue(local, table, key), StringComparison.Ordinal)
            )
            {
                return ("unique-swap", holder);
            }

            // (b) 持ち主がサーバーに無く、打ち切った行のローカルの行が持ち主を参照する
            if (
                server.Row(table, holder) is null
                && References
                    .Where(r => r.Child == table && r.Parent == table)
                    .Any(r => r.Read(local).Any(p => p.Key == key && p.ParentKey == holder))
            )
            {
                return ("holder-referenced-by-taker", holder);
            }
        }

        return null;
    }

    /// <summary>端末 2 台がサーバーと一致しているか（表明 2 の判定の本体）</summary>
    private async Task<bool> ConvergedAsync() => (await DivergencesAsync()).Count == 0;

    private async Task<List<ModelFailure>> DivergencesAsync()
    {
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var found = new List<ModelFailure>();

        foreach (var device in _world.Devices)
        {
            var local = await _world.ReadStateAsync(device.Actor, _ct);

            foreach (var table in ModelTables.All)
            {
                Compare(table, server.Rows(table), local.Rows(table), device.Actor, found);
            }

            Compare(
                "model_items:payload",
                server.Payloads(),
                local.Payloads(),
                device.Actor,
                found
            );
        }

        return found;
    }

    private static void Compare(
        string what,
        IReadOnlyDictionary<string, string> server,
        IReadOnlyDictionary<string, string> local,
        ModelActor device,
        List<ModelFailure> found
    )
    {
        foreach (var (key, row) in server)
        {
            if (!local.TryGetValue(key, out var localRow))
            {
                found.Add(
                    new($"2/diverge:{what}:missing-on-device", $"{device} lacks {key} ({row})")
                );
            }
            else if (!string.Equals(row, localRow, StringComparison.Ordinal))
            {
                found.Add(
                    new(
                        $"2/diverge:{what}:value",
                        $"{device} has {key} = {localRow}, server has {row}"
                    )
                );
            }
        }

        foreach (var (key, row) in local)
        {
            if (!server.ContainsKey(key))
            {
                found.Add(
                    new($"2/diverge:{what}:extra-on-device", $"{device} keeps {key} ({row})")
                );
            }
        }
    }

    private async Task CheckConvergenceAsync() =>
        _failures.AddRange(Distinct(await DivergencesAsync()));

    private async Task CheckNoLossAsync()
    {
        var final = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var found = new List<ModelFailure>();

        foreach (var table in ModelTables.All)
        {
            var initial = _initial.Rows(table);
            var current = final.Rows(table);
            var keys = initial
                .Keys.Concat(_rows.Keys.Where(k => k.Table == table).Select(k => k.Key))
                .Distinct(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                initial.TryGetValue(key, out var initialRow);
                current.TryGetValue(key, out var finalRow);
                _rows.TryGetValue((table, key), out var history);

                // blob だけの書き込みも行への書き込み（再生は行を丸ごと送る＝行の単位で勝ち負けが決まる）
                var blobWriters =
                    table == ModelTables.Items
                        ? (_blobs.GetValueOrDefault(key) ?? []).Select(h => h.Actor)
                        : [];
                CheckKey(
                    table,
                    key,
                    initialRow,
                    finalRow,
                    history ?? [],
                    blobWriters,
                    _conflictsTo.GetValueOrDefault((table, key)),
                    found
                );
            }
        }

        // blob: 行が残っている品目だけを見る（行が消えた品目の blob は行の表明が見る）
        var initialPayloads = _initial.Payloads();

        foreach (var (key, hex) in final.Payloads())
        {
            initialPayloads.TryGetValue(key, out var initialHex);
            _blobs.TryGetValue(key, out var history);
            history ??= [];
            var conflicted = _conflictsTo.GetValueOrDefault((ModelTables.Items, key));

            if (hex is "" or "NULL")
            {
                // 空でそろってよいのは、空でない blob を書いた端末がすべて、その品目の競合を報告されたか、ServerWins に
                // 変更を捨てられた（＝行の単位で負けた）ときだけ（N12 (i)）。それ以外の根拠では見逃さない
                // 消された品目を上げ直した端末も、その品目の blob の書き手に数える（作り直しの挿入が blob を運ぶ＝その
                // 送信が失敗すると空の blob が残る。種 982）。根拠になるのは、その端末の記録が競合として報告されたか、
                // ServerWins に捨てられたことだけ
                var nonEmptyWriters = history
                    .Where(h => h.Hex is not ("" or "NULL"))
                    .Select(h => h.Actor)
                    .Concat(_recreators.GetValueOrDefault(key) ?? [])
                    .Distinct()
                    .ToList();
                var lostByPolicy =
                    nonEmptyWriters.Count > 0
                    && conflicted is not null
                    && nonEmptyWriters.All(conflicted.Contains);

                if (!lostByPolicy)
                {
                    found.Add(new("3/blob-empty:model_items", $"item {key} payload is {hex}"));
                }

                continue;
            }

            var written = history.Select(h => h.Hex).Append(initialHex).OfType<string>();

            if (!written.Contains(hex, StringComparer.Ordinal))
            {
                found.Add(new("3/blob-invented:model_items", $"item {key} payload {hex}"));
                continue;
            }

            // blob を書いた後で品目が消え、作り直された（行が残っている）なら、期待値を決めない（N12 (ii)）
            if (_blobHistoryCut.Contains(key))
            {
                continue;
            }

            var writers = history.Select(h => h.Actor).Distinct().ToList();
            var expected = writers.Count switch
            {
                0 => initialHex,
                1 when conflicted is null || !conflicted.Contains(writers[0]) => history[^1].Hex,
                _ => null,
            };

            if (expected is not null && !string.Equals(expected, hex, StringComparison.Ordinal))
            {
                found.Add(
                    new(
                        "3/blob-lost:model_items",
                        $"item {key} payload {hex}, expected {expected} "
                            + $"({(writers.Count == 0 ? "untouched" : writers[0].ToString())})"
                    )
                );
            }
        }

        foreach (var (device, table, key, reason) in _conflicts)
        {
            if (
                reason
                is not (
                    SyncConflictReason.ModifiedOnServer
                    or SyncConflictReason.MissingOnServer
                    or SyncConflictReason.DuplicateOnServer
                )
            )
            {
                continue;
            }

            var others = (_rows.GetValueOrDefault((table, key)) ?? [])
                .Select(h => h.Actor)
                .Concat(
                    table == ModelTables.Items
                        ? (_blobs.GetValueOrDefault(key) ?? []).Select(h => h.Actor)
                        : []
                )
                .Where(a => a != device);

            if (!others.Any())
            {
                found.Add(
                    new(
                        $"3/spurious:{table}:{reason}",
                        $"{device} got {reason} on {key}, which no one else wrote"
                    )
                );
            }
        }

        _failures.AddRange(Distinct(found));
    }

    private static void CheckKey(
        string table,
        string key,
        string? initialRow,
        string? finalRow,
        List<(ModelActor Actor, string? Row)> history,
        IEnumerable<ModelActor> blobWriters,
        HashSet<ModelActor>? conflicted,
        List<ModelFailure> found
    )
    {
        if (
            finalRow is not null
            && finalRow != initialRow
            && !history.Any(h => string.Equals(h.Row, finalRow, StringComparison.Ordinal))
        )
        {
            found.Add(new($"3/invented:{table}", $"{key} = {finalRow}"));

            return;
        }

        var writers = history.Select(h => h.Actor).Concat(blobWriters).Distinct().ToList();
        string? expected;

        if (writers.Count == 0)
        {
            expected = initialRow;
        }
        else if (
            writers.Count == 1
            && (conflicted is null || !conflicted.Contains(writers[0]))
            && history.Count > 0
        )
        {
            expected = history[^1].Row;
        }
        else if (writers.Count == 1 && history.Count == 0)
        {
            // blob だけを書いたキー: 行の値は初期値のまま
            expected = conflicted is null || !conflicted.Contains(writers[0]) ? initialRow : null;

            if (conflicted is not null && conflicted.Contains(writers[0]))
            {
                return;
            }
        }
        else
        {
            return;
        }

        if (!string.Equals(expected, finalRow, StringComparison.Ordinal))
        {
            var shape = (expected, finalRow) switch
            {
                (null, _) => "deleted-but-present",
                (_, null) => "present-but-absent",
                _ => "value",
            };
            var who = writers.Count == 0 ? "untouched" : writers[0].ToString();
            found.Add(
                new(
                    $"3/lost:{table}:{shape}",
                    $"{key} by {who}: expected {expected ?? "(none)"}, got {finalRow ?? "(none)"}"
                )
            );
        }
    }

    private async Task CheckForeignKeysAsync(ModelActor actor, string after)
    {
        var connectionString =
            actor == ModelActor.Server
                ? _world.Server.SqliteConnectionString
                : _world.Device(actor).ConnectionString;

        // 実 SQL Server のサーバー役は外部キーを DB が強制するので確かめない
        if (connectionString is null)
        {
            return;
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(_ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(_ct);

        if (await reader.ReadAsync(_ct))
        {
            _failures.Add(
                new(
                    $"4/foreign-key:{reader.GetString(0)}",
                    $"{actor} has a row of {reader.GetString(0)} whose parent is missing, after {after}"
                )
            );
        }
    }

    /// <summary>モデルの外部キー（子の表・親の表・子の行のキーと、それが参照する親のキー）</summary>
    private static readonly (
        string Child,
        string Parent,
        Func<ModelDbState, IEnumerable<(string Key, string? ParentKey)>> Read
    )[] References =
    [
        (
            ModelTables.Folders,
            ModelTables.Folders,
            s =>
                s.Folders.Select(p =>
                    (
                        ModelDbState.Key(p.Key),
                        p.Value.ParentId is { } v ? ModelDbState.Key(v) : null
                    )
                )
        ),
        (
            ModelTables.Labels,
            ModelTables.Folders,
            s => s.Labels.Select(p => (p.Key, (string?)ModelDbState.Key(p.Value.FolderId)))
        ),
        (
            ModelTables.Items,
            ModelTables.Folders,
            s =>
                s.Items.Select(p =>
                    (ModelDbState.Key(p.Key), (string?)ModelDbState.Key(p.Value.FolderId))
                )
        ),
        (
            ModelTables.Items,
            ModelTables.Labels,
            s => s.Items.Select(p => (ModelDbState.Key(p.Key), p.Value.LabelCode))
        ),
        (
            ModelTables.Events,
            ModelTables.Items,
            s => s.Events.Select(p => (p.Key, (string?)ModelDbState.Key(p.Value.ItemId)))
        ),
    ];

    /// <summary>
    /// ローカルの外部キーで失敗した同期について、どの表のどの形で止まったかを推定して署名へ足す（SQLite の例外の文言には
    /// 表が出ないため、同じ段・同じ文言の別の不具合を見分けられない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 署名には表の名前を入れない（同じ不具合がどの表の組で起きたかで署名が割れると、既知の一覧が表の組の数だけ要る）。
    /// </para>
    /// <para>
    /// 削除伝搬で止まったとき（<c>[delete:形]</c>）: 子→親の順に、サーバーに無くローカルにあり未送信でない行を
    /// 削除の候補とし、その候補を参照するローカルの行を探す。形は、同じ表（<c>self</c>）・その実行の範囲外の表
    /// （<c>out-of-scope-child</c>）・未送信の行（<c>pending-child</c>）・それ以外（<c>child</c>）の順に判定する。
    /// </para>
    /// <para>
    /// 行の適用で止まったとき（<c>[apply:self]</c> / <c>[apply:cross:範囲:未送信の削除]</c>）: 親→子の順に、ローカルと違う
    /// サーバーの行のうち、参照する親がローカルに無いものを探す。同じ表なら <c>self</c>、別の表なら、親の表がその実行の
    /// 範囲の内か外か（<c>in-scope</c> / <c>out-of-scope</c>）と、親の削除が未送信か（<c>pending-delete</c>）を足す。親の削除が未送信の行は、エンジンが適用を見送る行なので数えない
    /// （ServerWins の実行は除く）。
    /// </para>
    /// <para>推定なので、当てはまる形が無ければ何も足さない。</para>
    /// </remarks>
    private async Task<string> DiagnoseForeignKeyAsync(Exception ex, string phase, ModelSyncOp op)
    {
        if (
            ex is not SqliteException { SqliteErrorCode: 19 }
            || !ex.Message.Contains("FOREIGN KEY", StringComparison.Ordinal)
        )
        {
            return "";
        }

        var local = await _world.ReadStateAsync(op.Device, _ct);
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var pending = (await _world.Device(op.Device).Journal.ReadAllAsync(_ct))
            .Select(e => (e.TableName, CanonicalKey(e.TableName, e.KeyText)))
            .ToHashSet();
        var outOfScope = op.Excluded.ToHashSet(StringComparer.Ordinal);

        if (op.Mode == SyncMode.Versioned)
        {
            outOfScope.Add(ModelTables.Labels);
        }

        if (phase.Contains("DeleteRows", StringComparison.Ordinal))
        {
            foreach (var table in ModelTables.All.Reverse().Where(t => !outOfScope.Contains(t)))
            {
                var serverKeys = server.Rows(table).Keys.ToHashSet(StringComparer.Ordinal);
                var candidates = local
                    .Rows(table)
                    .Keys.Where(k => !serverKeys.Contains(k) && !pending.Contains((table, k)))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var (child, _, read) in References.Where(r => r.Parent == table))
                {
                    foreach (var (key, parentKey) in read(local))
                    {
                        if (parentKey is null || !candidates.Contains(parentKey))
                        {
                            continue;
                        }

                        var shape =
                            child == table ? "self"
                            : outOfScope.Contains(child) ? "out-of-scope-child"
                            : pending.Contains((child, key)) ? "pending-child"
                            : "child";

                        return $"[delete:{shape}]";
                    }
                }
            }

            return "";
        }

        // ServerWins の実行は捨てる予定の行で適用を見送らない（ローカルの変更をすべて捨てる操作のため）
        var holdsBack =
            op is not { Mode: SyncMode.Versioned, Policy: SyncConflictPolicy.ServerWins };

        foreach (var table in ModelTables.All.Where(t => !outOfScope.Contains(t)))
        {
            var localRows = local.Rows(table);
            var changed = server
                .Rows(table)
                .Where(p => !localRows.TryGetValue(p.Key, out var row) || row != p.Value)
                .Select(p => p.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var (_, parent, read) in References.Where(r => r.Child == table))
            {
                var localParents = local.Rows(parent).Keys.ToHashSet(StringComparer.Ordinal);

                foreach (var (key, parentKey) in read(server))
                {
                    if (
                        changed.Contains(key)
                        && parentKey is not null
                        && !localParents.Contains(parentKey)
                        && !(holdsBack && pending.Contains((parent, parentKey)))
                    )
                    {
                        if (parent == table)
                        {
                            return "[apply:self]";
                        }

                        var scope = outOfScope.Contains(parent) ? "out-of-scope" : "in-scope";
                        var deleted = pending.Contains((parent, parentKey))
                            ? ":pending-delete"
                            : "";

                        return $"[apply:cross:{scope}{deleted}]";
                    }
                }
            }
        }

        return "";
    }

    /// <summary>競合のキーを、モデルの照合に使う格納値の文字列へそろえる（日時は Kind を落とす）</summary>
    private static string CanonicalKey(string table, string keyText) =>
        table == ModelTables.Events
            ? ModelTables.EventKeyText(
                DateTime.Parse(keyText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            )
            : keyText;

    private static IEnumerable<ModelFailure> Distinct(IEnumerable<ModelFailure> failures) =>
        failures.GroupBy(f => f.Signature).Select(g => g.First());

    private static void Append<TKey, TValue>(
        Dictionary<TKey, List<TValue>> map,
        TKey key,
        TValue value
    )
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(value);
    }

    private static readonly Regex FrameRegex = new(
        @"at QuickER\.Tests\.(?:GeneratedSyncModelFixture|Integration\.GeneratedRuntime)\.(?<path>[^\s(]+)\(",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 例外が起きたエンジンの段（同期の型の最も内側のメソッド）と、最も内側の生成コード・ハーネスの段を
    /// 「段＞内側」の形で返す（型名は名前空間と型引数の数を落とす）。
    /// </summary>
    internal static string Phase(Exception ex)
    {
        // 表の公開の入口として名指しするメソッド（削除伝搬はエンジンが内部の面から呼ぶので、入口として出ないことがある＝
        // 入口に数えない）
        var frames = FrameRegex
            .Matches(ex.StackTrace ?? "")
            .Select(m => Frame(m.Groups["path"].Value))
            // 外部の ISyncTable 実装の代役は転送するだけ＝署名から外す（包むかどうかで署名が割れないように）
            .Where(f => !f.StartsWith("SyncModelExternalTable.", StringComparison.Ordinal))
            .ToList();

        var inner = frames.FirstOrDefault() ?? "?";
        var sync =
            frames.FirstOrDefault(f =>
                f.StartsWith("Sync", StringComparison.Ordinal)
                || f.StartsWith("VersionlessSync", StringComparison.Ordinal)
            ) ?? "?";

        // 表の公開の入口（ダウンロード・ServerWins の適用・洗い替え・アップロード・削除伝搬のどれから来たか）。表の型
        // （版あり・版なし）は落とす＝同じ不具合が表の種類で別の署名へ割れないように
        var entry =
            frames.LastOrDefault(f =>
                (
                    f.StartsWith("SyncTableBase.", StringComparison.Ordinal)
                    || f.StartsWith("SyncTable.", StringComparison.Ordinal)
                    || f.StartsWith("VersionlessSyncTable.", StringComparison.Ordinal)
                ) && EntryMethods.Contains(f[(f.IndexOf('.', StringComparison.Ordinal) + 1)..])
            ) ?? sync;
        // 公開の入口の本体を分けた内部メソッド（〜CoreAsync）は、公開の名前へそろえる（実装の分け方で署名が割れないように）
        var entryMethod = entry[(entry.IndexOf('.', StringComparison.Ordinal) + 1)..]
            .Replace("CoreAsync", "Async", StringComparison.Ordinal);
        var path = entry == sync ? sync : $"{entryMethod}~{sync}";

        return sync == inner ? path : $"{path}>{inner}";
    }

    private static readonly HashSet<string> EntryMethods = new(StringComparer.Ordinal)
    {
        "DownloadAsync",
        "ApplyServerRowsAsync",
        "ApplyServerRowsCoreAsync",
        "RefreshAsync",
        "RefreshCoreAsync",
        "UploadAsync",
    };

    /// <summary>「名前空間.型.メソッド」から「型.メソッド」を取り出す（入れ子の型・状態機械・ラムダの名前は元へ戻す）</summary>
    private static string Frame(string path)
    {
        var parts = path.Split('.');
        var type = parts.Length >= 2 ? parts[^2] : "?";
        var arity = type.IndexOf('`', StringComparison.Ordinal);

        if (arity >= 0)
        {
            type = type[..arity];
        }

        var nested = type.IndexOf('+', StringComparison.Ordinal);

        if (nested >= 0)
        {
            type = type[..nested];
        }

        return $"{type}.{MethodName(parts[^1])}";
    }

    private static string MethodName(string method)
    {
        // 非同期の状態機械・ラムダ（<Name>d__1.MoveNext など）は元のメソッド名へ戻す
        var match = Regex.Match(method, "<(?<name>[A-Za-z]\\w*)>");

        return match.Success ? match.Groups["name"].Value : method;
    }

    /// <summary>例外の種類と文言（数字は # へ畳んでキーの値に依らない形にする）</summary>
    internal static string ErrorText(Exception ex)
    {
        var (kind, message) = ex switch
        {
            SqliteException sqlite => (
                $"SQLite{sqlite.SqliteErrorCode}",
                SqliteMessage(sqlite.Message)
            ),
            SaveConflictException conflict => ("SaveConflict", conflict.Reason.ToString()),
            _ => (ex.GetType().Name, ex.Message),
        };

        var text = $"{kind}:{Regex.Replace(message, @"\d+", "#")}";

        return text.Length <= 120 ? text : text[..120];
    }

    private static string SqliteMessage(string message)
    {
        var match = Regex.Match(message, "'(?<text>[^']*)'");

        return match.Success ? match.Groups["text"].Value : message;
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();
}
