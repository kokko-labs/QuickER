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
///   Versioned＋ServerWins → LastWriteWins の 3 段を安定するまで回す。いずれも全表・blob を運ぶ）が、例外なく、未送信・
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
///   <item><b>外部キー</b>: 書き込みの手・一括挿入・実行（同期・単独のアップロード／ダウンロード・洗い替え。例外で終わった
///   実行も）の前後で <c>PRAGMA foreign_key_check</c> の違反の集合を比べ、新しく出来た違反だけを 1 件ずつ
///   <c>4/foreign-key:{子の表}</c> として数える（SQLite は書き込み時に検査するので、新しい違反が出来るのはエンジンが外部キーの
///   検査を切ったまま書いたときだけ）。違反の同一性は（子の表・子の行の主キーの値・親の表・外部キーの番号）で、rowid では
///   比べない（消して同じキーで入れ直した行は同じ違反）。前からある違反（利用者の手が外部キーの検査を切って作った親の無い行）は
///   数えない。サーバーの他者の手はサーバーの SQLite の代役の DB を見る。実 SQL Server のサーバー役は外部キーを DB が強制する
///   ので確かめない。行は rowid から主キーの値を引いて見分けるので、rowid の無い表の違反が現れたら例外で止める（モデルの表は
///   どれも rowid を持つ）。実行の間の割り込みの書き込みの手も前後で比べるので、その間に実行が作った違反は、割り込みの手の後と
///   実行の後の両方で数えることがある（署名は同じ。説明の文が割り込みの手を名指しすることがある）。</item>
/// </list>
/// <para>
/// 書き手が 2 人以上のキーの期待値（ポリシーごとの編集×編集・削除×編集）は、乱数の種ではなく決定的なシナリオが
/// 確かめる（<see cref="SyncModelScenarios"/>）。
/// </para>
/// </remarks>
internal sealed partial class SyncModelRun : IAsyncDisposable
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

    /// <summary>
    /// 品目のキー → 最後に書き手がその品目を消した時点の blob の履歴の長さ（空の blob の判定は、それより後の書き込みだけを
    /// 空でない blob の書き手に数える。消した品目の blob は削除と一緒に消えた）
    /// </summary>
    private readonly Dictionary<string, int> _blobDeletedAt = new(StringComparer.Ordinal);

    /// <summary>だれかが消したことのある品目（行の書き手の操作として見えたもの）</summary>
    private readonly HashSet<string> _deletedItems = new(StringComparer.Ordinal);

    /// <summary>
    /// 消された後で、その品目の行をサーバーへ上げ直した端末（同期の前にサーバーに無く、その端末の同期の後にある＝挿入の
    /// 経路になった。N12 の種 982 の形＝作り直しの挿入の blob の送信が失敗し、空の blob が残る）
    /// </summary>
    private readonly Dictionary<string, HashSet<ModelActor>> _recreators = new(
        StringComparer.Ordinal
    );

    // 報告された競合（根拠の確認用）。InitialUnseen＝競合の時点で、その端末がまだ受け取っていない初期データのキーだったか
    private readonly List<(
        ModelActor Device,
        string Table,
        string Key,
        SyncConflictReason Reason,
        bool InitialUnseen
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
    /// 世界を作り、サーバーへ初期データを入れて、端末を最初の形にする（既定は全端末を初期データへそろえる）。
    /// </summary>
    /// <param name="setup">初期データ（サーバーの他者の書き込みだけ）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="options">世界の組み方（既定は直結・SQLite のサーバー役）</param>
    /// <param name="preloads">
    /// 端末ごとの最初の形（無い端末は <see cref="ModelPreload.None"/>＝後勝ちの静かな同期 1 回でそろえる）。
    /// <see cref="ModelPreload.Refresh"/> は洗い替え 1 回で作り、<see cref="ModelPreload.Journaled"/> は書き込みの手を書き手の
    /// 操作として流し（履歴へ積む）、<see cref="ModelPreload.Untracked"/> は行を Repository を通らずに入れる（誰の書き込みでも
    /// ない＝履歴へ積まない）。後ろ 2 つは最初の同期をしない。初期データへそろったかの照合は前の 2 つの端末にだけ掛ける
    /// </param>
    public static async Task<SyncModelRun> StartAsync(
        IReadOnlyList<ModelWriteOp> setup,
        CancellationToken ct,
        SyncModelWorldOptions? options = null,
        IReadOnlyList<SyncModelPreload>? preloads = null
    )
    {
        options ??= SyncModelWorldOptions.Direct;
        var run = new SyncModelRun(await SyncModelWorld.CreateAsync(options, ct), ct);

        // 世界を作った後の準備が例外で抜けたら、世界（一時の DB・サーバー役）を破棄してから投げる
        try
        {
            await SetUpAsync(run, setup, options, preloads, ct);
        }
        catch
        {
            await run.DisposeAsync();
            throw;
        }

        return run;
    }

    /// <summary>作った世界へ初期データを入れ、端末を最初の形にする（<see cref="StartAsync"/> の本体）</summary>
    private static async Task SetUpAsync(
        SyncModelRun run,
        IReadOnlyList<ModelWriteOp> setup,
        SyncModelWorldOptions options,
        IReadOnlyList<SyncModelPreload>? preloads,
        CancellationToken ct
    )
    {
        foreach (var op in setup)
        {
            run._log.Add("setup: " + op.Describe());
            await run._world.ApplyAsync(op, ct);
        }

        // 端末の最初の形はサーバーへ書かない（空の端末の静かな同期は送るものが無く、洗い替えは読むだけ）ので、初期データはここで読む
        run._initial = await run._world.ReadStateAsync(ModelActor.Server, ct);

        // 最初の同期をしない端末から見ると、初期データはまだ受け取っていないほかの書き手の内容（消失なし・根拠のない競合の判定が
        // 使う。その端末がサーバーと同じ内容で持った時点で外す＝ForgetSeenInitialAsync）。最初の書き込みより前に控える
        var unsynced = (preloads ?? [])
            .Where(p => p.Kind is ModelPreload.Journaled or ModelPreload.Untracked)
            .Select(p => p.Device)
            .ToList();

        foreach (var device in unsynced)
        {
            run._initialUnseen[device] =
            [
                .. ModelTables.All.SelectMany(table =>
                    run._initial.Rows(table).Keys.Select(key => (table, key))
                ),
            ];
        }

        foreach (var device in run._world.Devices)
        {
            var preload =
                preloads?.SingleOrDefault(p => p.Device == device.Actor)
                ?? new SyncModelPreload(device.Actor, ModelPreload.None, []);

            switch (preload.Kind)
            {
                case ModelPreload.None:
                {
                    var op = Quiet(device.Actor, SyncMode.LastWriteWins);
                    run._log.Add("setup: " + op.Describe());
                    var outcome = await run._world.SyncAsync(op, ct);

                    if (outcome.Conflicts.Count > 0 || outcome.Truncations.Count > 0)
                    {
                        run._failures.Add(
                            new("0/setup", "The initial sync did not finish cleanly.")
                        );
                    }

                    break;
                }

                case ModelPreload.Refresh:
                    run._log.Add($"setup: {device.Actor} Refresh blobs=True");
                    await run._world.RefreshAsync(
                        device.Actor,
                        new SyncRefreshOptions { IncludeUnboundedBinary = true },
                        ct
                    );
                    run.CountPreloadEffect(RefreshBuilt);
                    break;

                case ModelPreload.Journaled:
                    foreach (var write in preload.Writes)
                    {
                        run._log.Add("setup: " + write.Describe());
                        run._preloadWritten.Add((device.Actor, write.Table, write.KeyText));

                        if (!await run.WriteAsync(write))
                        {
                            run._failures.Add(
                                new("0/setup", $"A preload write did not apply: {write.Describe()}")
                            );
                        }
                    }

                    break;

                case ModelPreload.Untracked:
                    foreach (var write in preload.Writes)
                    {
                        run._log.Add("setup: untracked " + write.Describe());
                        await run._world.ApplyUntrackedAsync(write, ct);
                        run._untrackedPreload.Add((write.Table, write.KeyText));
                    }

                    break;
            }
        }

        if ((await run.DivergencesAsync(unsynced)).Count > 0)
        {
            run._failures.Add(new("0/setup", "The devices did not converge on the initial data."));
        }

        // 最初の形の後の、サーバーのキーを最後に変えた書き手と、端末が最後に見た番号を始める（照合 5 の始まり）
        await run.StartLedgerAsync();

        // 最初の形の後の「サーバーが知っているか」を観測する（照合 4 の始まり）
        var serverAfterSetup = await run._world.ReadStateAsync(ModelActor.Server, ct);

        foreach (var device in run._world.Devices)
        {
            run.ObserveServerKnows(
                device.Actor,
                serverAfterSetup,
                await run._world.ReadStateAsync(device.Actor, ct),
                threw: false,
                forgets: true,
                _ => true
            );
        }
    }

    /// <summary>
    /// 最初の同期をしていない端末が、まだ受け取っていない初期データのキー（その端末の版の競合では、初期データがほかの書き手に
    /// なる。端末がそのキーをサーバーと同じ内容で持った時点で外す）
    /// </summary>
    private readonly Dictionary<ModelActor, HashSet<(string Table, string Key)>> _initialUnseen =
    [];

    /// <summary>
    /// 端末が、まだ受け取っていない初期データのキーへ書いたキー（初期データもそのキーの書き手になる＝消失なしの表明は、書き手が
    /// 2 人のキーとして期待を決めない）
    /// </summary>
    private readonly HashSet<(string Table, string Key)> _initialContested = [];

    /// <summary>
    /// 端末が Repository を通らずに入れ、まだどの書き込みも触れていない行（端末が初めて書いた時点で、その行の今の内容と blob を
    /// その端末の書き込みとして履歴へ積む＝Repository 越しの更新・blob の書き込みは、サーバーへの挿入として全列を送る）
    /// </summary>
    private readonly HashSet<(string Table, string Key)> _untrackedPreload = [];

    /// <summary>空振りの守り：洗い替えで作った端末が、初期データへそろった</summary>
    public const string RefreshBuilt = "refresh-built";

    /// <summary>空振りの守り：最初の同期の前に初期データのキーへ書いた記録が、サーバーへ上がって DuplicateOnServer になった</summary>
    public const string JournaledDuplicateOnInitialKey = "journaled-duplicate-on-initial-key";

    /// <summary>空振りの守り：最初の同期の前に書いた記録が、サーバーの一意な値の持ち主に断られた（UniqueValueTakenOnServer）</summary>
    public const string JournaledUniqueValueTaken = "journaled-unique-value-taken";

    /// <summary>空振りの守り：どの書き込みも触れなかった追跡しない行が、静止化の後にどの端末にも無い</summary>
    public const string UntrackedRemoved = "untracked-removed";

    /// <summary>空振りの守りの種類（<see cref="PreloadEffects"/> の鍵）</summary>
    public static IReadOnlyList<string> PreloadEffectKinds { get; } =
    [RefreshBuilt, JournaledDuplicateOnInitialKey, JournaledUniqueValueTaken, UntrackedRemoved];

    /// <summary>端末の最初の形が実際に効いた件数（空振りしていないかを、既定の種の数で表明する）</summary>
    public IReadOnlyDictionary<string, int> PreloadEffects => _preloadEffects;

    private readonly Dictionary<string, int> _preloadEffects = new(StringComparer.Ordinal);

    /// <summary>最初の同期の前に Repository 越しに書いた（端末, 表, キー）</summary>
    private readonly HashSet<(ModelActor Device, string Table, string Key)> _preloadWritten = [];

    private void CountPreloadEffect(string kind) =>
        _preloadEffects[kind] = _preloadEffects.GetValueOrDefault(kind) + 1;

    /// <summary>端末がまだ受け取っていない初期データのキーへ書いたら、そのキーを初期データとの書き手 2 人のキーとして控える</summary>
    private void NoteInitialContested(ModelActor actor, string table, string key)
    {
        if (_initialUnseen.TryGetValue(actor, out var unseen) && unseen.Contains((table, key)))
        {
            _initialContested.Add((table, key));
        }
    }

    /// <summary>
    /// 端末の同期の後、まだ受け取っていない初期データのキーのうち、端末がサーバーと同じ内容（品目は blob も）で持つものを外す
    /// </summary>
    private async Task ForgetSeenInitialAsync(ModelActor device)
    {
        if (!_initialUnseen.TryGetValue(device, out var unseen) || unseen.Count == 0)
        {
            return;
        }

        var local = await _world.ReadStateAsync(device, _ct);
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var localPayloads = local.Payloads();
        var serverPayloads = server.Payloads();

        unseen.RemoveWhere(entry =>
            local.Row(entry.Table, entry.Key) is { } row
            && string.Equals(row, server.Row(entry.Table, entry.Key), StringComparison.Ordinal)
            && (
                entry.Table != ModelTables.Items
                || string.Equals(
                    localPayloads.GetValueOrDefault(entry.Key),
                    serverPayloads.GetValueOrDefault(entry.Key),
                    StringComparison.Ordinal
                )
            )
        );
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

            case MirrorServerRowOp mirror:
                await MirrorServerRowAsync(mirror);
                break;

            case ArmInterruptOp interrupt:
                if (
                    interrupt.Writes.Count == 0
                    || interrupt.Writer != interrupt.Device && interrupt.Writer != ModelActor.Server
                    || interrupt.Writes.Any(write => write.Actor != interrupt.Writer)
                )
                {
                    throw new ArgumentException(
                        "An interrupt writes either on the device whose sync it interrupts or as the server, one writer per interrupt.",
                        nameof(op)
                    );
                }

                _world.Faults.ArmInterrupt(
                    interrupt.Device,
                    interrupt.Point,
                    interrupt.Writer,
                    () => InterruptWriteAsync(interrupt)
                );
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
            {
                var frame = await BeginRefreshRunAsync(
                    refresh,
                    refresh.Device,
                    refresh.ToOptions()
                );

                try
                {
                    RecordRefreshResult(
                        refresh,
                        refresh.Device,
                        await _world.RefreshAsync(refresh, _ct)
                    );

                    // 洗い替えの結果は自分から変えた行を名指ししない（空として照らす）
                    await EndRunAsync(frame, [], [], threw: false);
                }
                catch (Exception ex) when (ConsumeExpectedThrow(refresh.Device, ex))
                {
                    // 期待した例外（結果の面に残した）
                    await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);
                }
                catch (Exception ex) when (InjectedFaultException.IsInjected(ex))
                {
                    _log.Add($"  -> injected fault: {ex.GetType().Name}");
                    _lastResult[refresh.Device] = ExceptionLines(ex);
                    await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);
                }
                catch (Exception ex)
                {
                    _lastResult[refresh.Device] = ExceptionLines(ex);
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
                    await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);
                }

                break;
            }

            default:
                if (!await ExecuteScenarioOpAsync(op))
                {
                    throw new ArgumentOutOfRangeException(nameof(op), op.Describe(), null);
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// 割り込みの書き込みを流す（同期の実行と別の流れから呼ばれる。実行はサーバー役の点で割り込みの終わりを待つので、操作の記録と
    /// 履歴は逐次のまま）。書き込みは通常の書き込みと同じく書き手の操作として履歴へ積む
    /// </summary>
    private async Task InterruptWriteAsync(ArmInterruptOp interrupt)
    {
        // 割り込みの書き込みが変えたキーは、実行の間に書かれたキーとして実行ごとの照合から外す
        _interrupting = true;

        try
        {
            foreach (var write in interrupt.Writes)
            {
                _log.Add($"  -> interrupt {interrupt.Point}: {write.Describe()}");
                await WriteAsync(write);
            }
        }
        finally
        {
            _interrupting = false;
        }

        if (interrupt.ThenArm is { } kind)
        {
            _log.Add($"  -> interrupt {interrupt.Point}: arm {kind}");
            _world.Faults.Arm(kind, interrupt.Device);
        }
    }

    /// <summary>
    /// 端末の行をサーバーの今の行で記録させずに書き換え、記録しない書き込みとして台帳へ反映する（持ち分があるキーの内容だけを
    /// 置き換える）。生成器は端末の DB で成功する形だけを作るので、失敗は書き込みの失敗と同じく名指しする
    /// </summary>
    private async Task MirrorServerRowAsync(MirrorServerRowOp op)
    {
        var before = await _world.ReadStateAsync(op.Device, _ct);
        var foreignKeysBefore = await ReadForeignKeyViolationsAsync(op.Device);

        try
        {
            await _world.MirrorServerRowAsync(op, _ct);
        }
        catch (Exception ex)
        {
            _failures.Add(
                new(
                    $"op/failed:{nameof(MirrorServerRowOp)}:{ErrorText(ex)}",
                    $"{op.Describe()}: {ex.GetType().Name}: {ex.Message}"
                )
            );
        }

        var after = await _world.ReadStateAsync(op.Device, _ct);
        NoteWrite(op.Device, before, after, tracked: false, op.Describe());
        RecordChanges(op.Device, before, after);
        await CheckNewForeignKeyViolationsAsync(op.Device, foreignKeysBefore, op.Describe());
    }

    /// <summary>書き込みを 1 つ流す（期待どおりに効いたら true）</summary>
    private async Task<bool> WriteAsync(ModelWriteOp op)
    {
        var before = await _world.ReadStateAsync(op.Actor, _ct);
        var foreignKeysBefore = await ReadForeignKeyViolationsAsync(op.Actor);
        var applied = true;
        var firedBefore = _world.Faults.Fired;

        try
        {
            await _world.ApplyAsync(op, _ct);

            if (op.Spelling is { } spelling)
            {
                _spelled[(op.Actor, op.Table, op.KeyText)] = spelling;
            }

            if (op.ExpectFailure)
            {
                _failures.Add(
                    new($"op/unexpected-success:{op.GetType().Name}", $"{op.Describe()} succeeded")
                );
            }
        }
        catch (Exception ex) when (op.ExpectFailure || InjectedFaultException.IsInjected(ex))
        {
            // 失敗する書き込み・注入した失敗。トリガーの記録は書き込みと一緒に巻き戻る
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
        var after = await _world.ReadStateAsync(op.Actor, _ct);
        var succeeded = applied && _world.Faults.Fired == firedBefore ? op : null;
        // 遅れている blob の行の付け替えは、付け替え先の blob の期待を付け替えの時点のサーバーの写す元のキーから決める
        var behindBlobs = await BehindMoveBlobsAsync(op.Actor, succeeded, after);
        // 成功した手（失敗も注入した失敗・フックの飛ばしも無い）は、内容が変わらなくても手が名指しする行の持ち分を立てる
        NoteWrite(op.Actor, before, after, tracked: true, op.Describe(), succeeded, behindBlobs);
        RecordChanges(op.Actor, before, after, succeeded, behindBlobs);
        await CheckNewForeignKeyViolationsAsync(op.Actor, foreignKeysBefore, op.Describe());

        return applied;
    }

    /// <param name="actor">書き手</param>
    /// <param name="before">書き込みの前の書き手の DB</param>
    /// <param name="after">書き込みの後の書き手の DB</param>
    /// <param name="op">成功した書き込みの手（付け替えの扱いだけが見る。失敗した手と手でない書き込みは null）</param>
    /// <param name="behindMoves">
    /// 遅れている blob の行を動かす付け替えの組（付け替え先 → 写す元のキーと送るべき blob。<see cref="BehindMoveBlobsAsync"/>）
    /// </param>
    private void RecordChanges(
        ModelActor actor,
        ModelDbState before,
        ModelDbState after,
        ModelWriteOp? op = null,
        IReadOnlyDictionary<string, (string Origin, string Blob)>? behindMoves = null
    )
    {
        behindMoves ??= new Dictionary<string, (string Origin, string Blob)>();

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
                    NoteInitialContested(actor, table, key);
                    Append(_rows, (table, key), (actor, newRow));

                    // 追跡しない行に端末が初めて書いた：その行の blob はこの端末の内容としてサーバーへ挿入される（挿入は全列を送る）
                    if (
                        _untrackedPreload.Remove((table, key))
                        && table == ModelTables.Items
                        && newRow is not null
                        && after.Payloads().TryGetValue(key, out var adoptedHex)
                    )
                    {
                        Append(_blobs, key, (actor, adoptedHex));
                    }

                    // blob を書いた品目が消えたら、それ以前の blob の書き込みは期待値の根拠にならない（作り直した端末の
                    // blob が入る＝挿入は全列を送る）
                    if (table == ModelTables.Items && newRow is null && _blobs.ContainsKey(key))
                    {
                        _blobHistoryCut.Add(key);
                    }

                    if (table == ModelTables.Items && newRow is null)
                    {
                        _deletedItems.Add(key);
                        // 空の blob の判定は、この削除より後の書き込みだけを見る（この削除で、それまでの blob は消えた）
                        _blobDeletedAt[key] = _blobs.TryGetValue(key, out var written)
                            ? written.Count
                            : 0;
                    }
                }
            }
        }

        var oldPayloads = before.Payloads();

        foreach (var (key, hex) in after.Payloads())
        {
            if (!oldPayloads.TryGetValue(key, out var oldHex) || oldHex != hex)
            {
                NoteInitialContested(actor, ModelTables.Items, key);

                // 遅れている blob の行の付け替え先は、端末の値でなく写す元のキーのサーバーの blob を積む（下の呼び出し）
                if (!behindMoves.ContainsKey(key))
                {
                    Append(_blobs, key, (actor, hex));
                }

                // 追跡しない行に端末が初めて書いたのが blob だけ：その行の内容もこの端末の内容としてサーバーへ挿入される
                if (
                    _untrackedPreload.Remove((ModelTables.Items, key))
                    && after.Row(ModelTables.Items, key) is { } adoptedRow
                )
                {
                    Append(_rows, (ModelTables.Items, key), (actor, adoptedRow));
                }
            }
        }

        NoteBlobBehindWrite(actor, op, behindMoves, before, after);
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

        // 実行ごとの照合の入力（実行の前のサーバーと端末の内容・その端末の持ち分）
        var frame = await BeginSyncRunAsync(op, before);

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

            if (!quiescing && !ConsumeExpectedThrow(op.Device, ex))
            {
                _lastResult[op.Device] = ExceptionLines(ex);
            }

            await RecordRecreatorsAsync(op.Device, serverItemsBefore);
            _lastLocalRowChanges[op.Device] =
                ex.Data[SyncEngine.LocalRowChangesDataKey] as IReadOnlyList<SyncLocalRowChange>
                ?? [];
            RecordOutcomes(op, quiescing, [], _lastLocalRowChanges[op.Device]);

            // 例外で終わった後勝ち＋ServerWins の実行も、例外の前に決着させた変更を捨てている（結果は返らない）。
            // 実行の前の未送信の記録と突き合わせ、記録が消えた行を捨てた行とみなす（N11 の案 1）。決着の理由の根拠も確かめる
            if (settles)
            {
                var (settled, fulfilled) = await RecordLastWriteWinsSettlementsAsync(
                    op,
                    before!,
                    _lastLocalRowChanges[op.Device],
                    frame.Carried
                );
                await CheckRefusalBasisAsync(op, before!, settled, fulfilled);
            }

            await ForgetSeenInitialAsync(op.Device);
            await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);

            return false;
        }
        catch (Exception ex) when (!quiescing && ConsumeExpectedThrow(op.Device, ex))
        {
            await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);

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
            await EndRunAsync(frame, [], ThrownLocalRowChanges(ex), threw: true);

            return false;
        }

        // 結果が名乗る有無は、一覧の件数と食い違わない
        if (
            outcome.ReportedFlags is { } flags
            && (
                flags.Conflicts != outcome.Conflicts.Count > 0
                || flags.Truncations != outcome.Truncations.Count > 0
            )
        )
        {
            _failures.Add(
                new(
                    "5/result-flags",
                    $"{op.Describe()}: HasConflicts={flags.Conflicts} for {outcome.Conflicts.Count} conflicts, "
                        + $"HasTruncations={flags.Truncations} for {outcome.Truncations.Count} truncations"
                )
            );
        }

        _lastLocalRowChanges[op.Device] = outcome.LocalRowChanges;
        RecordOutcomes(op, quiescing, outcome.Conflicts, outcome.LocalRowChanges);

        if (!quiescing)
        {
            RecordSyncResult(op, outcome);
        }
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

            if (
                conflict.Reason == SyncConflictReason.DuplicateOnServer
                && _initialUnseen.TryGetValue(op.Device, out var unseenNow)
                && unseenNow.Contains((conflict.TableName, key))
            )
            {
                CountPreloadEffect(JournaledDuplicateOnInitialKey);
            }

            if (
                conflict.Reason == SyncConflictReason.UniqueValueTakenOnServer
                && _preloadWritten.Contains((op.Device, conflict.TableName, key))
            )
            {
                CountPreloadEffect(JournaledUniqueValueTaken);
            }

            _conflicts.Add(
                (
                    op.Device,
                    conflict.TableName,
                    key,
                    conflict.Reason,
                    _initialUnseen.TryGetValue(op.Device, out var unseen)
                        && unseen.Contains((conflict.TableName, key))
                )
            );
        }

        foreach (var truncation in outcome.Truncations)
        {
            _log.Add(
                $"  -> truncated {ModelTables.Short(truncation.TableName)} "
                    + $"pending={truncation.PendingKeyText ?? "-"} "
                    + $"parent={(truncation.MissingParentTableName is { } parent ? ModelTables.Short(parent) : "-")}"
            );
        }

        await ForgetSeenInitialAsync(op.Device);

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

            // 競合を報告された行と、持ち越した決定を果たした行は、この実行でサーバーへ届いていない
            var undelivered = outcome
                .Conflicts.Select(c => (c.TableName, CanonicalKey(c.TableName, c.KeyText)))
                .ToList();

            if (settles)
            {
                var (settled, fulfilled) = await RecordLastWriteWinsSettlementsAsync(
                    op,
                    before,
                    outcome.LocalRowChanges,
                    frame.Carried
                );
                refusals.AddRange(settled);
                undelivered.AddRange(fulfilled);
            }

            await CheckRefusalBasisAsync(op, before, refusals, undelivered);
        }

        // 実行ごとの照合（根拠の判定と同じく実行の直後に確定する）
        await EndRunAsync(
            frame,
            outcome.Conflicts,
            outcome.LocalRowChanges,
            threw: false,
            outcome.Truncations.Select(t => t.TableName).ToHashSet(StringComparer.Ordinal)
        );

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

    /// <summary>
    /// 消された品目のうち、この同期の前にサーバーに無く、後にあるもの＝この端末が上げ直したものを控える。上げ直した端末の
    /// blob が空なら控えない（端末は空でない blob を持っていない＝写し待ちの代わりの値のまま上げ直した。本当の blob は
    /// サーバーの削除で消えた）
    /// </summary>
    private async Task RecordRecreatorsAsync(ModelActor device, HashSet<string>? serverItemsBefore)
    {
        if (serverItemsBefore is null)
        {
            return;
        }

        var localPayloads = (await _world.ReadStateAsync(device, _ct)).Payloads();

        foreach (
            var key in (await _world.ReadStateAsync(ModelActor.Server, _ct))
                .Rows(ModelTables.Items)
                .Keys
        )
        {
            if (
                !serverItemsBefore.Contains(key)
                && _deletedItems.Contains(key)
                && localPayloads.GetValueOrDefault(key) is not (null or "" or "NULL")
            )
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
    /// Versioned＋ServerWins の実行は、その範囲の未送信の行を送らずに捨て、サーバーの行で上書きする（この端末の
    /// ローカルの変更を捨てると頼む操作）。捨てる行は、競合を報告された行と同じく、その端末の書き込みを期待値から外す。
    /// </summary>
    private async Task RecordServerWinsDiscardsAsync(ModelSyncOp op)
    {
        var excluded = op.Excluded.Append(ModelTables.Labels).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in await _world.Device(op.Device).State.ReadPendingAsync(_ct))
        {
            if (excluded.Contains(entry.Table))
            {
                continue;
            }

            var key = (entry.Table, CanonicalKey(entry.Table, entry.KeyText));

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
    /// <para>
    /// 端末のエンジンが決定の持ち越しを宣言し（<see cref="IModelDeviceSync.CarriesServerWinsDecision"/>）、台帳が実行の前から
    /// 戻せなかった行の決定（<c>_carried</c>）を持っていて、この実行が DiscardedByServerWins を報告した行は、サーバーが書き込みを
    /// 断ったのではなく、その決定を果たした決着なので、根拠の確認へ渡さない。台帳の決定は宣言に依らず作るので、宣言が偽の
    /// エンジンの決着は今までどおり根拠を確かめる（期待値から外すのと記録の
    /// 行は同じに行う。決定の果たし方は実行ごとの照合の <see cref="CheckCarried"/> が照らす）。台帳はテスト側の記録だけで、
    /// エンジンの状態の表は読まない。
    /// </para>
    /// <para>
    /// 決定を果たした行は送られていない（エンジンはその行を送らずにサーバーの行で戻す）ので、根拠を確かめる行とは別に
    /// 返す。ほかの行の拒否の根拠を確かめるとき、その行の未送信の付け替え・値の手放しは届いていないものとして数える
    /// （<see cref="CheckRefusalBasisAsync"/> の届かなかった行）。
    /// </para>
    /// </remarks>
    /// <param name="carried">実行の始めに、その端末の戻せなかった行の決定があったキー（<see cref="RunFrame.Carried"/>）</param>
    /// <returns>
    /// 捨てたとみなしたキーのうち根拠を確かめるもの（理由はエンジンが返さないので null）と、持ち越した決定を果たしたキー
    /// （根拠は確かめない・この実行で届かなかった行）
    /// </returns>
    private async Task<(
        List<(string Table, string Key, SyncConflictReason? Reason)> Settled,
        List<(string Table, string Key)> Fulfilled
    )> RecordLastWriteWinsSettlementsAsync(
        ModelSyncOp op,
        RunSnapshot before,
        IReadOnlyList<SyncLocalRowChange> reported,
        IReadOnlySet<(string Table, string Key)> carried
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
        var fulfilled = new List<(string Table, string Key)>();

        // 決定を持ち越すと宣言するエンジンだけが、台帳の決定を果たす（宣言が偽のエンジンの決着は、サーバーが断った決着）
        var carriesDecisions = _world.Device(op.Device).Sync.CarriesServerWinsDecision;
        var remaining = (await _world.Device(op.Device).State.ReadPendingAsync(_ct))
            .SelectMany(row => row.ChangeMarks)
            .ToHashSet();
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);

        foreach (var row in pendingBefore)
        {
            if (row.Marks.Any(remaining.Contains))
            {
                continue;
            }

            var serverRow = server.Row(row.Table, row.Key);
            var localRow = localBefore.Row(row.Table, row.Key);
            var upserts = row.HasUpsert;
            var deletes = row.HasDelete;

            // 送るものが無い記録（行ありで削除だけ・行なしで削除なし・行なしで削除ありでもサーバーの版が分からない）は、エンジンが
            // 送らずに捨てる（決着ではない。同じ実行のダウンロードが同じキーのほかの書き手の行を降ろしても、ServerWins が戻したの
            // ではない）
            if (
                (localRow is not null && !upserts)
                || (localRow is null && !deletes)
                || (localRow is null && !row.ServerKnows)
            )
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
                        localAfter.Row(row.Table, row.Key),
                        StringComparison.Ordinal
                    )
                    && serverRow is not null
                    && !discarded.Contains((row.Table, row.Key))
                )
            )
            {
                continue;
            }

            _log.Add($"  -> settled {ModelTables.Short(row.Table)} key={row.Key}");

            if (!_conflictsTo.TryGetValue((row.Table, row.Key), out var set))
            {
                _conflictsTo[(row.Table, row.Key)] = set = [];
            }

            set.Add(op.Device);

            // 持ち越した決定を果たした決着は、サーバーが断った理由を持たない
            if (
                carriesDecisions
                && carried.Contains((row.Table, row.Key))
                && discarded.Contains((row.Table, row.Key))
            )
            {
                _log.Add(
                    $"  -> carried decision fulfilled {ModelTables.Short(row.Table)} key={row.Key}"
                );
                fulfilled.Add((row.Table, row.Key));
                continue;
            }

            settled.Add((row.Table, row.Key, null));
        }

        return (settled, fulfilled);
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
        IReadOnlyList<PendingGroup> Pending,
        int HitIndex
    );

    /// <summary>
    /// 未送信の行（端末の同期の状態の面が返す行）を、表記の正規化後のキーでまとめたもの（同じキーの行は、変わる印を印の順に
    /// 合わせ、「サーバーが知っているか」はどれかが知っていれば真）
    /// </summary>
    private sealed record PendingGroup(
        string Table,
        string Key,
        bool HasUpsert,
        bool HasDelete,
        bool ServerKnows,
        IReadOnlyList<long> Marks,
        IReadOnlyList<string> Operations
    );

    private static IReadOnlyList<PendingGroup> GroupPending(IReadOnlyList<ModelPendingRow> rows) =>
        [
            .. rows.GroupBy(row => (row.Table, Key: CanonicalKey(row.Table, row.KeyText)))
                .Select(group =>
                {
                    var changes = group
                        .SelectMany(row =>
                            row.ChangeMarks.Select(
                                (mark, i) =>
                                    (
                                        Mark: mark,
                                        Operation: i < row.Operations.Count ? row.Operations[i] : ""
                                    )
                            )
                        )
                        .OrderBy(change => change.Mark)
                        .ToList();

                    return new PendingGroup(
                        group.Key.Table,
                        group.Key.Key,
                        group.Any(row => row.HasUpsert),
                        group.Any(row => row.HasDelete),
                        group.Any(row => row.ServerKnowsRow),
                        [.. changes.Select(change => change.Mark)],
                        [.. changes.Select(change => change.Operation)]
                    );
                }),
        ];

    private async Task<RunSnapshot> SnapshotAsync(ModelActor device)
    {
        var snapshot = new RunSnapshot(
            await _world.ReadStateAsync(ModelActor.Server, _ct),
            await _world.ReadStateAsync(device, _ct),
            GroupPending(await _world.Device(device).State.ReadPendingAsync(_ct)),
            _world.Faults.WriteHitCount
        );

        // 判定役が「サーバーが知っているか」を読む行で、状態の面の答えをモデルの観測と比べる（照合 4）
        CompareServerKnows(device, snapshot.Local, snapshot.Pending);

        return snapshot;
    }

    /// <summary>理由ごとの、根拠を確かめた件数と根拠のあった件数（空振りしていないかを出力で確かめる）</summary>
    public IReadOnlyDictionary<string, (int Checked, int WithBasis)> BasisChecks => _basisChecks;

    private readonly Dictionary<string, (int Checked, int WithBasis)> _basisChecks = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// 一意な値の形ごとの、その形で書いた行の upsert をサーバーが同じ値の持ち主を根拠に断った件数（UniqueValueTakenOnServer と、
    /// 後勝ち＋ServerWins の決着）。実 SQL Server の転送で、照合順序が同じとみなす別表記が実際に断られたかを数える
    /// </summary>
    public IReadOnlyDictionary<UniqueSpelling, int> SpellingRefusals => _spellingRefusals;

    private readonly Dictionary<UniqueSpelling, int> _spellingRefusals = [];

    // 書き手・表・キーごとの、最後に生成器が値を選んだ書き込みの値の形
    private readonly Dictionary<
        (ModelActor Actor, string Table, string Key),
        UniqueSpelling
    > _spelled = [];

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
    ///   この端末で未送信で値を手放し（ローカルの行がもうその値を持たない）、この実行で断られていないなら数えない。
    ///   手放す書き込み自身に実行の後のサーバーで断られる根拠（親が無い・手放されていない別の行が値を持つ）があれば数える
    ///   （ServerWins が戻せずに決着を保留した行は報告されないので、断られたかを根拠で決める。判定は 1 段だけ）。
    ///   同じ値かどうかはサーバーの一意制約の比べ方（<see cref="ISyncModelServerRole.UniqueValues"/>＝実 SQL Server は
    ///   大文字小文字と末尾の空白を区別しない）で決める。</item>
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
        var pending = before.Pending.Select(e => (e.Table, e.Key)).ToHashSet();
        // この実行で届かなかった行（理由を問わず競合になった行・決着させた行・持ち越した決定を果たした行）。その行の
        // 付け替え・値の手放しは届いていないので、根拠を打ち消さない
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

            if (
                !deleting
                && _spelled.TryGetValue((op.Device, table, key), out var spelling)
                && HasHolder(server)
            )
            {
                _spellingRefusals[spelling] = _spellingRefusals.GetValueOrDefault(spelling) + 1;
            }

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
                                .Pending.Where(e => e.Table == table && e.Key == key)
                                .SelectMany(e => e.Operations)
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

            // 送った行（of を渡せばその行）が参照する親のどれかがサーバーに無い
            bool ParentMissing(string? of = null) =>
                References
                    .Where(r => r.Child == table)
                    .Any(r =>
                        r.Read(before.Local).FirstOrDefault(p => p.Key == (of ?? key)).ParentKey
                            is { } parentKey
                        && server.Row(r.Parent, parentKey) is null
                    );

            // 送った行（of を渡せばその行）のローカルの一意な値を持つ、その状態の別の行
            IEnumerable<string> Holders(ModelDbState state, string? of = null) =>
                UniqueValue(before.Local, table, of ?? key) is { } value
                    ? state
                        .Rows(table)
                        .Keys.Where(k =>
                            k != (of ?? key)
                            && _world.Server.UniqueValues.Equals(
                                UniqueValue(state, table, k),
                                value
                            )
                        )
                    : [];

            // 持ち主 h がこの端末の未送信で値を手放した（Releases）。ただし、手放す書き込み自身に実行の後のサーバーで断られる
            // 根拠があれば、手放しはこの実行で届かなかったので割り引かない（ServerWins が戻せずに決着を保留した行は記録が残り、
            // 競合にも決着にも報告されないので refused に入らない）。手放しの根拠の判定は 1 段だけで、その中の持ち主の割り引きは
            // 再帰させない。循環（手放す行の値をサーバーで持つのが送った行自身）は根拠に数えない＝今までどおり割り引く（厳しい側）
            bool IsReleased(string holder) =>
                Releases(holder, key) && !ReleaseRefusedOnServer(holder);

            // 持ち主 h が、行 of の値をこの端末の未送信で手放した（ローカルの行がまだあり、値が違う）
            bool Releases(string holder, string of) =>
                inScope.Contains(table)
                && pending.Contains((table, holder))
                && !refused.Contains((table, holder))
                && before.Local.Row(table, holder) is not null
                && !_world.Server.UniqueValues.Equals(
                    UniqueValue(before.Local, table, holder),
                    UniqueValue(before.Local, table, of)
                );

            // 持ち主 h の手放す書き込みに、実行の後のサーバーで断られる根拠がある（親が無い・h のローカルの値を、手放されて
            // いない別の行が持つ。送った行自身は循環なので数えない）
            bool ReleaseRefusedOnServer(string holder) =>
                ParentMissing(holder)
                || Holders(server, holder).Any(h => h != key && !Releases(h, holder));

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
    /// <param name="requireArmedFaultsFired">
    /// 仕掛けたまま起きなかった失敗を <c>fault/not-fired:{種類}</c> の違反にするか（決まった形のシナリオ＝失敗を狙って仕掛ける。
    /// 乱数の種と網羅の行は確率で仕掛けるので、起きないのは正当）
    /// </param>
    public async Task FinishAsync(bool requireArmedFaultsFired = false)
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

        // 仕掛けたのに起きなかった失敗も同じ空振り（失敗の口がサーバー役から外れても、結果が同じになる行が黙って緑になる）
        if (requireArmedFaultsFired)
        {
            foreach (var kind in _world.Faults.PendingFaults)
            {
                _failures.Add(new($"fault/not-fired:{kind}", "The armed fault never fired."));
            }
        }

        foreach (var device in _expectThrow)
        {
            _failures.Add(
                new(
                    "op/expect-throw-unused",
                    $"{device} expected a throw, but no sync or refresh followed."
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

        // 根拠の無い競合・決着と実行ごとの照合の違反は、静止化の成否に依らず報告する（静止化の中で見つけたものを含む）
        _failures.AddRange(Distinct(_basisFailures));
        _failures.AddRange(Distinct(_runFailures));

        if (!quiesced)
        {
            return;
        }

        await CheckConvergenceAsync();
        await CheckNoLossAsync();

        // どの書き込みも触れなかった追跡しない行が、静止化の後にどの端末にも無い（上がらず削除伝搬で消えた）
        foreach (var (table, key) in _untrackedPreload)
        {
            var present = false;

            foreach (var device in _world.Devices)
            {
                present |=
                    (await _world.ReadStateAsync(device.Actor, _ct)).Row(table, key) is not null;
            }

            if (!present)
            {
                CountPreloadEffect(UntrackedRemoved);
            }
        }
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
    /// 静止化の最後の周に残った打ち切りを 1 件ずつ分類する。(a) 一意な値の回転（打ち切った行 R のサーバーの値をローカルの別の行
    /// H1 が持ち、H1 のサーバーの値を H2 が持ち…と辿って R に戻る。<see cref="UniqueRotation"/>）＝<c>1/no-quiesce:unique-swap:{表}</c>。
    /// 回転の行はすべて説明済みにする。2 行の厳密な入れ替え（最短の形）はここで、3 行以上の回転は、ほかの形（端末側の循環を含む）が
    /// どれも説明しなかった打ち切りにだけ最後に当てる（端末だけで回した循環は (c) の形のまま数える）。
    /// (b) R のサーバーの値をローカルで持つ H がサーバーに無く、R のローカルの行が H を参照する（値を取る行自身の古い参照が
    /// 持ち主を残す）＝<c>1/no-quiesce:holder-referenced-by-taker:{表}</c>。
    /// (b') R のサーバーの値をローカルで持つ H がサーバーに無く、別の表のローカルの行 C が H を（推移的に）参照し、C のサーバーの行が R の
    /// 下にある（C は H を参照したまま R を待つ）＝<c>1/no-quiesce:holder-referenced-by-other-table:{表}</c>。H・C と、サーバーで R の
    /// 下にあるために止まった打ち切りの行も説明済みにする。続いて、説明の付いていない競合の行を端末・表ごとに集め、
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
        var takers = new List<(ModelActor Device, string Table, string Key)>();

        // (a)・(b)・(b')：打ち切りを 1 件ずつ分類する（打ち切った行と、その形の持ち主の行は説明済み）
        foreach (var (device, table, key) in _quiesceTruncations.Distinct())
        {
            var local = await _world.ReadStateAsync(device, _ct);

            // (a) 2 行の厳密な入れ替え（回転の最短の形）。3 行以上の回転は、ほかの形がどれも説明しなかった打ち切りにだけ、最後に当てる
            if (
                key is not null
                && UniqueRotation(server, local, table, key, _world.Server.UniqueValues)
                    is { Count: 2 } swap
            )
            {
                signatures.Add($"1/no-quiesce:unique-swap:{table}");
                explained.UnionWith(swap.Select(row => (device, table, row)));
                continue;
            }

            var stuck = key is null
                ? null
                : StuckShape(server, local, table, key, _world.Server.UniqueValues);

            if (
                stuck is null
                && key is not null
                && OtherTableHolder(server, local, table, key, _world.Server.UniqueValues)
                    is { } other
            )
            {
                // 引き留める行の中に最新の写し（ローカルの行がサーバーの行と同じ）があれば、エンジンが消さない残りの形
                var shape = HasCurrentReferrer(server, local, other.Referrers)
                    ? "holder-referenced-by-other-table-current-child"
                    : "holder-referenced-by-other-table";
                signatures.Add($"1/no-quiesce:{shape}:{table}");
                explained.Add((device, table, key));
                explained.Add((device, table, other.Holder));
                takers.Add((device, table, key));

                foreach (var (referrerTable, referrer) in other.Referrers)
                {
                    explained.Add((device, referrerTable, referrer));
                }

                continue;
            }

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

        // (b') の値を取る行 R を待つ打ち切り：サーバーの行が R の下にある行は、R が降りるまで止まる（R の形で説明済み）
        foreach (var (device, table, key) in leftTruncations)
        {
            if (
                key is not null
                && takers.Any(t =>
                    t.Device == device && IsUnder(server, table, key, t.Table, t.Key)
                )
            )
            {
                explained.Add((device, table, key));
            }
        }

        // (a') 3 行以上の一意な値の回転：ほかの形（端末側の循環を含む）がどれも説明しなかった打ち切りから辿る。回転の途中に未送信の
        // 行と古い写しの行が混ざる形（サーバーと端末の両方で値が動いた）を含む。端末だけで回した循環は (c) が先に説明する
        foreach (var (device, table, key) in leftTruncations)
        {
            if (key is null || explained.Contains((device, table, key)))
            {
                continue;
            }

            var local = await _world.ReadStateAsync(device, _ct);

            if (
                UniqueRotation(server, local, table, key, _world.Server.UniqueValues) is
                { } rotation
            )
            {
                signatures.Add($"1/no-quiesce:unique-swap:{table}");
                explained.UnionWith(rotation.Select(row => (device, table, row)));
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
    /// 別々。サーバーが持つかはサーバーの一意制約の比べ方＝<see cref="ISyncModelServerRole.UniqueValues"/> で見る）
    /// </summary>
    private bool IsPendingCycle(
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
                && _world.Server.UniqueValues.Equals(
                    UniqueValue(server, table, other),
                    UniqueValue(local, table, row)
                )
            )
        );
    }

    /// <summary>
    /// 打ち切った行 R の形 (b')：R のサーバーの値をローカルで持つ H がサーバーに無く、別の表のローカルの行 C が H を（推移的に）
    /// 参照し、C のサーバーの行が R の下にある。持ち主 H と、その形の C の一覧（当たらなければ null）。一意な値の同じさはサーバー役の
    /// 比べ方（<see cref="ISyncModelServerRole.UniqueValues"/>）で見る
    /// </summary>
    internal static (
        string Holder,
        IReadOnlyList<(string Table, string Key)> Referrers
    )? OtherTableHolder(
        ModelDbState server,
        ModelDbState local,
        string table,
        string key,
        IEqualityComparer<string> uniqueValues
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
                && server.Row(table, k) is null
                && uniqueValues.Equals(UniqueValue(local, table, k), wanted)
            )
            .Order(StringComparer.Ordinal);

        foreach (var holder in holders)
        {
            var referrers = ModelTables
                .All.Where(other => other != table)
                .SelectMany(other => local.Rows(other).Keys.Select(k => (Table: other, Key: k)))
                .Where(c =>
                    IsUnder(local, c.Table, c.Key, table, holder)
                    && server.Row(c.Table, c.Key) is not null
                    && IsUnder(server, c.Table, c.Key, table, key)
                )
                .ToList();

            if (referrers.Count > 0)
            {
                return (holder, referrers);
            }
        }

        return null;
    }

    /// <summary>
    /// 別の表の参照の形 (b') の参照する行のうち、ローカルの行がサーバーの行と同じもの（最新の写し）があるか。エンジンは、消すと
    /// ダウンロードが降ろし直さない最新の写しを消さないので、この形は静止化しない（<c>holder-referenced-by-other-table-current-child</c>）
    /// </summary>
    internal static bool HasCurrentReferrer(
        ModelDbState server,
        ModelDbState local,
        IReadOnlyList<(string Table, string Key)> referrers
    ) =>
        referrers.Any(r =>
            local.Row(r.Table, r.Key) is { } row
            && string.Equals(row, server.Row(r.Table, r.Key), StringComparison.Ordinal)
        );

    /// <summary>状態の中で、行 (rowTable, rowKey) の祖先（外部キーを親へ辿る。行自身は含めない）に (table, key) があるか</summary>
    private static bool IsUnder(
        ModelDbState state,
        string rowTable,
        string rowKey,
        string table,
        string key
    )
    {
        var seen = new HashSet<(string, string)>();
        var queue = new Queue<(string Table, string Key)>();
        queue.Enqueue((rowTable, rowKey));

        while (queue.TryDequeue(out var row))
        {
            if (!seen.Add(row))
            {
                continue;
            }

            foreach (var (_, parent, read) in References.Where(r => r.Child == row.Table))
            {
                foreach (var (_, parentKey) in read(state).Where(p => p.Key == row.Key))
                {
                    if (parentKey is null)
                    {
                        continue;
                    }

                    if (parent == table && parentKey == key)
                    {
                        return true;
                    }

                    queue.Enqueue((parent, parentKey));
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 打ち切った行 R から始まる一意な値の回転（R・H1・H2…の行のキー。当たらなければ null）：R のサーバーの値をローカルの
    /// 別の行 H1 が持ち、H1 のサーバーの値をローカルの別の行 H2 が持ち…と辿って R に戻る。2 行の厳密な入れ替え（H1 のサーバーの
    /// 値を R がローカルで持つ）はその最短の形。docs の既知の割り切り（一意な値を回した行は、2 行でも 3 行以上でも適用できない）の
    /// 形で、回転の途中の行が未送信の変更を持っていても、古い写しのままでも同じ。一意な値の同じさはサーバー役の比べ方
    /// （<see cref="ISyncModelServerRole.UniqueValues"/>）で見る
    /// </summary>
    internal static IReadOnlyList<string>? UniqueRotation(
        ModelDbState server,
        ModelDbState local,
        string table,
        string key,
        IEqualityComparer<string> uniqueValues
    )
    {
        var path = new List<string> { key };
        var onPath = new HashSet<string>(StringComparer.Ordinal) { key };

        // 深さ優先で辿る（ローカルの一意制約は序数なので、サーバー役の比べ方で同じ値の持ち主が 2 つ以上あり得る）
        bool Walk(string current)
        {
            if (UniqueValue(server, table, current) is not { } wanted)
            {
                return false;
            }

            foreach (
                var holder in local
                    .Rows(table)
                    .Keys.Where(k =>
                        k != current && uniqueValues.Equals(UniqueValue(local, table, k), wanted)
                    )
                    .Order(StringComparer.Ordinal)
            )
            {
                if (holder == key)
                {
                    return path.Count >= 2;
                }

                if (!onPath.Add(holder))
                {
                    continue;
                }

                path.Add(holder);

                if (Walk(holder))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
                onPath.Remove(holder);
            }

            return false;
        }

        return Walk(key) ? path : null;
    }

    /// <summary>
    /// 打ち切った行の形（holder-referenced-by-taker）と、その形の持ち主の行（当たらなければ null）。一意な値の回転（unique-swap）は
    /// <see cref="UniqueRotation"/> が先に見る。一意な値の同じさはサーバー役の比べ方（<see cref="ISyncModelServerRole.UniqueValues"/>）で見る
    /// </summary>
    internal static (string Shape, string Holder)? StuckShape(
        ModelDbState server,
        ModelDbState local,
        string table,
        string key,
        IEqualityComparer<string> uniqueValues
    )
    {
        if (UniqueValue(server, table, key) is not { } wanted)
        {
            return null;
        }

        var holders = local
            .Rows(table)
            .Keys.Where(k => k != key && uniqueValues.Equals(UniqueValue(local, table, k), wanted))
            .ToList();

        foreach (var holder in holders)
        {
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

    private async Task<List<ModelFailure>> DivergencesAsync(
        IReadOnlyCollection<ModelActor>? skipped = null
    )
    {
        var server = await _world.ReadStateAsync(ModelActor.Server, _ct);
        var found = new List<ModelFailure>();

        foreach (
            var device in _world.Devices.Where(d => skipped is null || !skipped.Contains(d.Actor))
        )
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
                if (_untrackedKeys.Contains((table, key)))
                {
                    continue;
                }

                initial.TryGetValue(key, out var initialRow);
                current.TryGetValue(key, out var finalRow);
                _rows.TryGetValue((table, key), out var history);

                // blob だけの書き込みも行への書き込み（再生は行を丸ごと送る＝行の単位で勝ち負けが決まる）。まだ受け取って
                // いない初期データのキーへ端末が書いたキーは、初期データ（サーバーの他者）も書き手に数える
                var blobWriters = (
                    table == ModelTables.Items
                        ? (_blobs.GetValueOrDefault(key) ?? []).Select(h => h.Actor)
                        : []
                ).Concat(_initialContested.Contains((table, key)) ? [ModelActor.Server] : []);
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
            if (_untrackedKeys.Contains((ModelTables.Items, key)))
            {
                continue;
            }

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
                // 書き手が品目を消したなら、その削除より前の書き込み（初期データを含む）は削除と一緒に消えた。削除の後に
                // 空でない blob を書いた（上げ直した）端末が無ければ、空でそろってよい（写し待ちの代わりの空の値のまま上げ直した形）
                var deletedAt = _blobDeletedAt.TryGetValue(key, out var cut) ? cut : (int?)null;
                var nonEmptyWriters = history
                    .Skip(deletedAt ?? 0)
                    .Where(h => h.Hex is not ("" or "NULL"))
                    .Select(h => h.Actor)
                    .Concat(_recreators.GetValueOrDefault(key) ?? [])
                    .Distinct()
                    .ToList();

                if (deletedAt is not null && nonEmptyWriters.Count == 0)
                {
                    continue;
                }

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

            var writers = history
                .Select(h => h.Actor)
                .Concat(
                    _initialContested.Contains((ModelTables.Items, key)) ? [ModelActor.Server] : []
                )
                .Distinct()
                .ToList();
            var expected = writers.Count switch
            {
                0 => initialHex,
                1
                    when history.Count > 0
                        && (conflicted is null || !conflicted.Contains(writers[0])) => history[
                    ^1
                ].Hex,
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

        foreach (var (device, table, key, reason, initialUnseen) in _conflicts)
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

            // 果たされた持ち越しの決定（宣言が真のエンジンだけ）は、その端末の競合の根拠を問わない
            if (!others.Any() && !initialUnseen && !_carryFulfilled.Contains((device, table, key)))
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

    /// <summary>
    /// 外部キーの違反 1 件の同一性（子の表・子の行の主キーの値のテキスト・親の表・外部キーの番号）。rowid は持たない（消して同じ
    /// キーで入れ直した行は rowid が変わるが、同じ違反）
    /// </summary>
    internal readonly record struct ForeignKeyViolation(
        string Table,
        string Key,
        string Parent,
        long ForeignKeyId
    );

    /// <summary>
    /// 書き手の DB の外部キーの違反の集合を読む（<c>PRAGMA foreign_key_check</c> の行の rowid を、その表の主キーの値のテキストへ
    /// 引き直す）。実 SQL Server のサーバー役は外部キーを DB が強制するので読まない（null）
    /// </summary>
    private async Task<IReadOnlySet<ForeignKeyViolation>?> ReadForeignKeyViolationsAsync(
        ModelActor actor
    )
    {
        var connectionString =
            actor == ModelActor.Server
                ? _world.Server.SqliteConnectionString
                : _world.Device(actor).ConnectionString;

        if (connectionString is null)
        {
            return null;
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(_ct);
        var found = new List<(string Table, long? RowId, string Parent, long ForeignKeyId)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await command.ExecuteReaderAsync(_ct);

            while (await reader.ReadAsync(_ct))
            {
                found.Add(
                    (
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetInt64(1),
                        reader.GetString(2),
                        reader.GetInt64(3)
                    )
                );
            }
        }

        var violations = new HashSet<ForeignKeyViolation>();

        foreach (var (table, rowId, parent, foreignKeyId) in found)
        {
            violations.Add(
                new(
                    table,
                    await PrimaryKeyTextAsync(connection, table, rowId),
                    parent,
                    foreignKeyId
                )
            );
        }

        return violations;
    }

    /// <summary>行の rowid から、その表の主キーの列の値のテキスト（複数の列は | でつなぐ）を引く</summary>
    private async Task<string> PrimaryKeyTextAsync(
        SqliteConnection connection,
        string table,
        long? rowId
    )
    {
        // rowid の無い表（WITHOUT ROWID）の違反は行を見分けられない（同じ表・同じ外部キーの違反が 1 つの同一性にまとまり、前から
        // ある違反が新しい違反を隠す）。モデルの表はどれも rowid を持つので、持たない表が現れたら黙って進めずに止める
        if (rowId is null)
        {
            throw new InvalidOperationException(
                $"PRAGMA foreign_key_check returned no rowid for a row of {table}: the foreign key check tells "
                    + "violations apart by the row's primary key, which it reads through the rowid."
            );
        }

        var keyColumns = new List<(int Order, string Name)>();

        await using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
            await using var reader = await info.ExecuteReaderAsync(_ct);

            while (await reader.ReadAsync(_ct))
            {
                var order = reader.GetInt32(reader.GetOrdinal("pk"));

                if (order > 0)
                {
                    keyColumns.Add((order, reader.GetString(reader.GetOrdinal("name"))));
                }
            }
        }

        var columns =
            keyColumns.Count == 0
                ? ["rowid"]
                : keyColumns.OrderBy(c => c.Order).Select(c => c.Name).ToList();
        await using var select = connection.CreateCommand();
        select.CommandText =
            $"SELECT {string.Join(", ", columns.Select(c => $"\"{c.Replace("\"", "\"\"")}\""))} "
            + $"FROM \"{table.Replace("\"", "\"\"")}\" WHERE rowid = $rowid";
        select.Parameters.AddWithValue("$rowid", rowId.Value);
        await using var row = await select.ExecuteReaderAsync(_ct);

        if (!await row.ReadAsync(_ct))
        {
            return $"(rowid {rowId.Value.ToString(CultureInfo.InvariantCulture)})";
        }

        return string.Join(
            "|",
            Enumerable
                .Range(0, columns.Count)
                .Select(i =>
                    row.IsDBNull(i)
                        ? "null"
                        : Convert.ToString(row.GetValue(i), CultureInfo.InvariantCulture)
                )
        );
    }

    /// <summary>
    /// 外部キーの表明：書き手の DB の外部キーの違反のうち、手か実行の前（<paramref name="before"/>）に無かった違反を 1 件ずつ
    /// <c>4/foreign-key:{子の表}</c> として足す（前からある違反は数えない）
    /// </summary>
    /// <param name="actor">書き手</param>
    /// <param name="before">手か実行の前の違反の集合（実 SQL Server のサーバー役は null＝確かめない）</param>
    /// <param name="after">手か実行の説明</param>
    private async Task CheckNewForeignKeyViolationsAsync(
        ModelActor actor,
        IReadOnlySet<ForeignKeyViolation>? before,
        string after
    )
    {
        if (before is null || await ReadForeignKeyViolationsAsync(actor) is not { } now)
        {
            return;
        }

        foreach (
            var violation in now.Where(v => !before.Contains(v))
                .OrderBy(v => v.Table, StringComparer.Ordinal)
                .ThenBy(v => v.Key, StringComparer.Ordinal)
        )
        {
            _failures.Add(
                new(
                    $"4/foreign-key:{violation.Table}",
                    $"{actor} has a row of {violation.Table} (key {violation.Key}) whose parent in "
                        + $"{violation.Parent} is missing (foreign key {violation.ForeignKeyId.ToString(CultureInfo.InvariantCulture)}), "
                        + $"newly after {after}"
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
            ModelTables.Folders,
            ModelTables.Items,
            s =>
                s.Folders.Select(p =>
                    (
                        ModelDbState.Key(p.Key),
                        p.Value.PinnedItemId is { } v ? ModelDbState.Key(v) : null
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
        var pending = GroupPending(await _world.Device(op.Device).State.ReadPendingAsync(_ct))
            .Select(e => (e.Table, e.Key))
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
            .ToList();

        var inner = frames.FirstOrDefault() ?? "?";
        // 同期の型は Sync で始まる型（SyncEngine・表の SyncTable とその派生・エンジンの内部の型ほか）
        var sync =
            frames.FirstOrDefault(f => f.StartsWith("Sync", StringComparison.Ordinal)) ?? "?";

        // 入口（取得と適用・送信・洗い替えのどれから来たか）。エンジンは表ごとの入口を持たず、エンジンの段が入口に当たる
        var entry =
            frames.LastOrDefault(f =>
                f.StartsWith("SyncEngine.", StringComparison.Ordinal)
                && EntryMethods.Contains(f[(f.IndexOf('.', StringComparison.Ordinal) + 1)..])
            ) ?? sync;
        // 公開の入口の本体を分けた内部メソッド（〜CoreAsync）は、公開の名前へそろえる（実装の分け方で署名が割れないように）
        var entryMethod = entry[(entry.IndexOf('.', StringComparison.Ordinal) + 1)..]
            .Replace("CoreAsync", "Async", StringComparison.Ordinal);
        var path = entry == sync ? sync : $"{entryMethod}~{sync}";

        return sync == inner ? path : $"{path}>{inner}";
    }

    /// <summary>
    /// エンジン（SyncEngine）の入口として名指しするメソッド：エンジンの段（取得と適用・送信）と、公開の入口の
    /// 単独の取得・送信・洗い替え。同期の実行（SyncAsync）は段を順に呼ぶだけなので数えない（段の名前が入口になる）
    /// </summary>
    private static readonly HashSet<string> EntryMethods = new(StringComparer.Ordinal)
    {
        "DownloadAsync",
        "DownloadCoreAsync",
        "RefreshAsync",
        "UploadAsync",
        "UploadCoreAsync",
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
