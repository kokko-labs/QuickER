using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>判定役の対照の行が、端末のエンジンを外から壊す壊し方</summary>
internal enum JudgeBreakKind
{
    /// <summary>その実行でエンジンを呼ばない（何も送らず、何も戻さない＝ServerWins が戻せなかった形を作る）</summary>
    SkipRun,

    /// <summary>
    /// エンジンを呼んだ後で、実行の前の端末のフォルダーの内容をサーバーへ書く（行が無ければ挿入する）。エンジンが端末の内容を
    /// 送った結果を作る
    /// </summary>
    WriteLocalToServer,

    /// <summary>
    /// エンジンを呼ぶ前に、端末のフォルダーの行を記録させずにサーバーの内容へ戻す（サーバーに行が無ければ端末の行を消す）。
    /// エンジンが端末の内容を捨てた結果を作る
    /// </summary>
    RestoreFromServer,

    /// <summary>
    /// エンジンを呼ぶ前に、端末に無いフォルダーの行を、記録させずにサーバーの内容で端末へ入れる。エンジンがサーバーの行を
    /// 端末へ持ち込んだ結果を作る（<see cref="JudgeBreak.Throws"/> と組むと、持ち込んだ後で例外で終わる実行になる）
    /// </summary>
    BringInFromServer,

    /// <summary>
    /// エンジンを呼んだ後で、端末のローカル DB へ外部キーの検査を切った専用の接続（プールしない）で文を順に流す
    /// （<see cref="JudgeBreak.Statements"/>）。エンジンが外部キーの検査を切ったまま書いた結果を作る
    /// （<see cref="JudgeBreak.Throws"/> と組むと、エンジンを呼ばずに文を流した後で例外で終わる実行になる）
    /// </summary>
    WriteLocalWithoutForeignKeys,
}

/// <summary>壊し方 1 つ（どの端末の、何回目の実行で、どのフォルダーを壊すか）</summary>
/// <param name="Kind">壊し方</param>
/// <param name="Device">端末</param>
/// <param name="FolderId">壊すフォルダーのキー（<see cref="JudgeBreakKind.SkipRun"/> は使わない）</param>
/// <param name="OnRun">その端末のエンジンの面の <c>RunAsync</c> の何回目で壊すか（最初の形の静かな同期が 1 回目）</param>
internal sealed record JudgeBreak(JudgeBreakKind Kind, ModelActor Device, int FolderId, int OnRun)
{
    /// <summary>
    /// 壊した後で、そのキーの未送信の記録も消すか（<see cref="JudgeBreakKind.RestoreFromServer"/>・
    /// <see cref="JudgeBreakKind.WriteLocalToServer"/>・<see cref="JudgeBreakKind.BringInFromServer"/>）
    /// </summary>
    public bool ClearsRecords { get; init; }

    /// <summary>
    /// 端末の行を戻したことを、実行の結果の自分から変えた行に <see cref="SyncLocalRowChangeReason.DiscardedByServerWins"/> で
    /// 載せるか（<see cref="JudgeBreakKind.RestoreFromServer"/> だけ）
    /// </summary>
    public bool ReportsDiscarded { get; init; }

    /// <summary>
    /// 実行の結果の競合に、このフォルダーの追加・更新の競合（<see cref="SyncConflictReason.UniqueValueTakenOnServer"/>）を足すか
    /// </summary>
    public bool ReportsConflict { get; init; }

    /// <summary>
    /// 実行の結果の競合に、このフォルダーの削除の競合（<see cref="SyncConflictReason.DeleteRefusedOnServer"/>）を足すか
    /// </summary>
    public bool ReportsDeleteRefused { get; init; }

    /// <summary>この実行の壊し方を当てた後で、結果を返さずに例外を投げるか（内側のエンジンは呼ばない）</summary>
    public bool Throws { get; init; }

    /// <summary><see cref="JudgeBreakKind.WriteLocalWithoutForeignKeys"/> が流す文</summary>
    public IReadOnlyList<string> Statements { get; init; } = [];

    public string Describe() =>
        $"{Kind} {Device} folder={FolderId} run={OnRun}"
        + (Statements.Count > 0 ? $" sql=[{string.Join("; ", Statements)}]" : "")
        + (ClearsRecords ? " clear-records" : "")
        + (ReportsDiscarded ? " report-discarded" : "")
        + (ReportsConflict ? " report-conflict" : "")
        + (ReportsDeleteRefused ? " report-delete-refused" : "")
        + (Throws ? " throw" : "");
}

/// <summary>
/// 判定役の対照の行の端末の宣言（端末のエンジンの面を壊す包みで包み、必要なら状態の面の「サーバーが知っているか」を反転する）。
/// 製品の端末を作った後で、エンジンの面と状態の面だけを差し替える（<see cref="SyncModelDevice.Install"/>）。直結の転送だけで使う
/// </summary>
/// <param name="CarriesServerWinsDecision">包んだエンジンが名乗る宣言（<see cref="IModelDeviceSync.CarriesServerWinsDecision"/>）</param>
/// <param name="Breaks">壊し方</param>
internal sealed record SyncModelJudgeDevice(
    bool CarriesServerWinsDecision,
    IReadOnlyList<JudgeBreak> Breaks
)
{
    /// <summary>状態の面の「サーバーが知っているか」を反転して返す端末</summary>
    public IReadOnlyList<ModelActor> FlipsServerKnows { get; init; } = [];

    /// <summary>
    /// 世界の組み方に、この宣言の端末の作り方を足す。返す一覧は、実行の終わりに起きた壊し方を数える（起きなかった壊し方は
    /// 対照の行の誤り）
    /// </summary>
    public (SyncModelWorldOptions Options, HashSet<JudgeBreak> Fired) Apply(
        SyncModelWorldOptions options
    )
    {
        var fired = new HashSet<JudgeBreak>();

        return (
            options with
            {
                CreateDevice = async (request, ct) =>
                {
                    var device = await options.CreateDevice(request, ct);
                    var server =
                        request.Server as ISyncModelServerRole
                        ?? throw new InvalidOperationException(
                            "A judge device writes to the server directly, so it runs only on the direct transport."
                        );
                    device.Install(
                        device.CurrentParts() with
                        {
                            Sync = new SyncModelJudgeBreakSync(
                                device.Sync,
                                device,
                                server,
                                [.. Breaks.Where(b => b.Device == request.Actor)],
                                CarriesServerWinsDecision,
                                fired
                            ),
                            State = FlipsServerKnows.Contains(request.Actor)
                                ? new SyncModelJudgeFlippedState(device.State)
                                : device.State,
                        }
                    );

                    return device;
                },
            },
            fired
        );
    }
}

/// <summary>
/// 端末のエンジンの面を外から壊す包み。宣言した実行で、内側のエンジンを呼ぶ前後に端末のローカル DB かサーバーへ直接書き、
/// エンジンがそうした結果を作る（書き込みはモデルの履歴を通さない＝エンジンの仕業として見える）
/// </summary>
internal sealed class SyncModelJudgeBreakSync(
    IModelDeviceSync inner,
    SyncModelDevice device,
    ISyncModelServerRole server,
    IReadOnlyList<JudgeBreak> breaks,
    bool carriesServerWinsDecision,
    HashSet<JudgeBreak> fired
) : IModelDeviceSync
{
    private int _runs;

    public bool CarriesServerWinsDecision => carriesServerWinsDecision;

    /// <summary>包んだエンジンの宣言を引き継ぐ（包みは適用の書き方を変えない）</summary>
    public bool AppliesThroughSaveHooks => inner.AppliesThroughSaveHooks;

    /// <summary>包んだエンジンの宣言を引き継ぐ（包みは再開点の持ち方を変えない）</summary>
    public bool DerivesResumePointFromLocalVersions => inner.DerivesResumePointFromLocalVersions;

    /// <summary>包んだエンジンの宣言を引き継ぐ（包みは取得の仕方を変えない）</summary>
    public bool RefetchesHeldRowsInSameRun => inner.RefetchesHeldRowsInSameRun;

    /// <summary>包んだエンジンの宣言を引き継ぐ（包みは降ろす行の上限の扱いを変えない）</summary>
    public bool RefusesDownloadOverRowLimit => inner.RefusesDownloadOverRowLimit;

    public async Task<ModelSyncOutcome> RunAsync(
        ModelSyncKind kind,
        SyncOptions options,
        CancellationToken ct
    )
    {
        _runs++;
        var due = breaks.Where(b => b.OnRun == _runs).ToList();
        var captured = new Dictionary<int, ModelFolderEntity?>();

        foreach (var brk in due)
        {
            if (brk.Kind == JudgeBreakKind.WriteLocalToServer)
            {
                captured[brk.FolderId] = await device.RawFolders.GetByIdAsync(brk.FolderId, ct);
            }

            if (brk.Kind == JudgeBreakKind.RestoreFromServer)
            {
                await RestoreAsync(brk, ct);
            }

            if (brk.Kind == JudgeBreakKind.BringInFromServer)
            {
                await BringInAsync(brk, ct);
            }

            // 例外で終わる実行は内側のエンジンを呼ばないので、文は例外の前に流す
            if (brk.Kind == JudgeBreakKind.WriteLocalWithoutForeignKeys && brk.Throws)
            {
                await WriteWithoutForeignKeysAsync(brk, ct);
            }
        }

        // 結果を返さずに例外で終わる実行（例外は結果の一覧を運ばない）
        if (due.Any(b => b.Throws))
        {
            fired.UnionWith(due);

            throw new InvalidOperationException(
                $"The judge break ended the run with an exception: {string.Join("; ", due.Select(b => b.Describe()))}"
            );
        }

        var outcome = due.Any(b => b.Kind == JudgeBreakKind.SkipRun)
            ? new ModelSyncOutcome([], [])
            : await inner.RunAsync(kind, options, ct);

        foreach (var brk in due.Where(b => b.Kind == JudgeBreakKind.WriteLocalToServer))
        {
            await WriteToServerAsync(captured[brk.FolderId], brk, ct);
        }

        foreach (var brk in due.Where(b => b.Kind == JudgeBreakKind.WriteLocalWithoutForeignKeys))
        {
            await WriteWithoutForeignKeysAsync(brk, ct);
        }

        var discarded = due.Where(b => b.ReportsDiscarded)
            .Select(b => new SyncLocalRowChange(
                ModelTables.Folders,
                ModelDbState.Key(b.FolderId),
                SyncLocalRowChangeReason.DiscardedByServerWins
            ))
            .ToList();
        var conflicts = due.Where(b => b.ReportsConflict)
            .Select(b => new SyncConflict(
                ModelTables.Folders,
                ModelDbState.Key(b.FolderId),
                SyncJournalOperation.Upsert,
                SyncConflictReason.UniqueValueTakenOnServer,
                null,
                null
            ))
            .Concat(
                due.Where(b => b.ReportsDeleteRefused)
                    .Select(b => new SyncConflict(
                        ModelTables.Folders,
                        ModelDbState.Key(b.FolderId),
                        SyncJournalOperation.Delete,
                        SyncConflictReason.DeleteRefusedOnServer,
                        null,
                        null
                    ))
            )
            .ToList();

        fired.UnionWith(due);

        return discarded.Count == 0 && conflicts.Count == 0
            ? outcome
            : outcome with
            {
                Conflicts = [.. outcome.Conflicts, .. conflicts],
                LocalRowChanges = [.. outcome.LocalRowChanges, .. discarded],
            };
    }

    public Task<SyncRefreshResult> RefreshAsync(
        SyncRefreshOptions? options,
        CancellationToken ct
    ) => inner.RefreshAsync(options, ct);

    /// <summary>端末のフォルダーの行を、記録させずにサーバーの内容へ戻す（必要ならそのキーの未送信の記録も消す）</summary>
    private async Task RestoreAsync(JudgeBreak brk, CancellationToken ct)
    {
        var current = await server.Folders.GetByIdAsync(brk.FolderId, ct);

        await device.WriteUntrackedAsync(
            connection =>
                connection is not null
                    ? current is null
                        ? ExecuteOnAsync(
                            connection,
                            "DELETE FROM model_folders WHERE folder_id = @id",
                            [("@id", brk.FolderId)],
                            ct
                        )
                        : ExecuteOnAsync(
                            connection,
                            "UPDATE model_folders SET parent_id = @parent, name = @name, pinned_item_id = @pinned "
                                + "WHERE folder_id = @id",
                            [
                                ("@id", brk.FolderId),
                                ("@parent", current.ParentId),
                                ("@name", current.Name),
                                ("@pinned", current.PinnedItemId),
                            ],
                            ct
                        )
                    : current is null
                        ? device.Sql.ExecuteSqlAsync(
                            "DELETE FROM model_folders WHERE folder_id = @id",
                            new { id = brk.FolderId },
                            ct
                        )
                        : device.Sql.ExecuteSqlAsync(
                            "UPDATE model_folders SET parent_id = @parent, name = @name, pinned_item_id = @pinned "
                                + "WHERE folder_id = @id",
                            new
                            {
                                id = brk.FolderId,
                                parent = current.ParentId,
                                name = current.Name,
                                pinned = current.PinnedItemId,
                            },
                            ct
                        ),
            ct
        );

        await ClearRecordsAsync(brk, ct);
    }

    /// <summary>端末に無いフォルダーの行を、記録させずにサーバーの内容で端末へ入れる</summary>
    private async Task BringInAsync(JudgeBreak brk, CancellationToken ct)
    {
        var current =
            await server.Folders.GetByIdAsync(brk.FolderId, ct)
            ?? throw new InvalidOperationException(
                $"{brk.Describe()}: the server has no row to bring in."
            );

        await device.WriteUntrackedAsync(
            connection =>
                connection is not null
                    ? ExecuteOnAsync(
                        connection,
                        "INSERT INTO model_folders (folder_id, parent_id, name, pinned_item_id) "
                            + "VALUES (@id, @parent, @name, @pinned)",
                        [
                            ("@id", brk.FolderId),
                            ("@parent", current.ParentId),
                            ("@name", current.Name),
                            ("@pinned", current.PinnedItemId),
                        ],
                        ct
                    )
                    : device.Sql.ExecuteSqlAsync(
                        "INSERT INTO model_folders (folder_id, parent_id, name, pinned_item_id) "
                            + "VALUES (@id, @parent, @name, @pinned)",
                        new
                        {
                            id = brk.FolderId,
                            parent = current.ParentId,
                            name = current.Name,
                            pinned = current.PinnedItemId,
                        },
                        ct
                    ),
            ct
        );

        await ClearRecordsAsync(brk, ct);
    }

    /// <summary>
    /// 端末のローカル DB へ、外部キーの検査を切った接続で文を順に流す。記録しない書き込みの口
    /// （<see cref="SyncModelDevice.WriteUntrackedAsync"/>）を通すので、文が書いた行は端末の記録に残らない。口が接続を渡さない
    /// 端末では、専用の接続（プールしない）を自分で開く
    /// </summary>
    private Task WriteWithoutForeignKeysAsync(JudgeBreak brk, CancellationToken ct) =>
        device.WriteUntrackedAsync(
            async connection =>
            {
                if (connection is not null)
                {
                    foreach (var sql in brk.Statements)
                    {
                        await ExecuteOnAsync(connection, sql, [], ct);
                    }

                    return;
                }

                await using var own = new SqliteConnection(
                    SyncModelRun.WithoutForeignKeys(device.ConnectionString)
                );
                await own.OpenAsync(ct);

                foreach (var sql in brk.Statements)
                {
                    await using var command = own.CreateCommand();
                    command.CommandText = sql;
                    await command.ExecuteNonQueryAsync(ct);
                }
            },
            ct,
            withoutForeignKeys: true
        );

    /// <summary>渡された接続で 1 文を流す（パラメータの null は <see cref="DBNull"/>）</summary>
    private static async Task ExecuteOnAsync(
        DbConnection connection,
        string sql,
        (string Name, object? Value)[] parameters,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>宣言していれば、そのキーの未送信の記録を消す（消し方は端末ごと＝<see cref="SyncModelDevice.ClearPendingRecordsAsync"/>）</summary>
    private async Task ClearRecordsAsync(JudgeBreak brk, CancellationToken ct)
    {
        if (brk.ClearsRecords)
        {
            await device.ClearPendingRecordsAsync(
                ModelTables.Folders,
                ModelDbState.Key(brk.FolderId),
                ct
            );
        }
    }

    /// <summary>実行の前の端末のフォルダーの内容をサーバーへ書く（サーバーに行が無ければ挿入する）</summary>
    private async Task WriteToServerAsync(
        ModelFolderEntity? local,
        JudgeBreak brk,
        CancellationToken ct
    )
    {
        if (local is null)
        {
            throw new InvalidOperationException(
                $"{brk.Describe()}: the device has no row to write to the server."
            );
        }

        if (await server.Folders.GetByIdAsync(brk.FolderId, ct) is { } current)
        {
            current.ParentId = local.ParentId;
            current.Name = local.Name;
            current.PinnedItemId = local.PinnedItemId;
            await server.Folders.UpdateAsync(current, cancellationToken: ct);
        }
        else
        {
            await server.Folders.InsertAsync(
                new ModelFolderEntity
                {
                    FolderId = local.FolderId,
                    ParentId = local.ParentId,
                    Name = local.Name,
                    PinnedItemId = local.PinnedItemId,
                },
                ct
            );
        }

        await ClearRecordsAsync(brk, ct);
    }
}

/// <summary>状態の面の「サーバーが知っているか」を反転して返す包み（照合 4 の対照の行が使う）</summary>
internal sealed class SyncModelJudgeFlippedState(IModelDeviceSyncState inner)
    : IModelDeviceSyncState
{
    public async Task<IReadOnlyList<ModelPendingRow>> ReadPendingAsync(CancellationToken ct) =>
        [
            .. (await inner.ReadPendingAsync(ct)).Select(row =>
                row with
                {
                    ServerKnowsRow = !row.ServerKnowsRow,
                }
            ),
        ];

    public Task<int> CountPendingAsync(CancellationToken ct) => inner.CountPendingAsync(ct);
}
