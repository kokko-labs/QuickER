using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 端末のエンジンの面。同期のモデルが端末の同期・洗い替えを流す入口（製品の <c>SyncEngine</c> を直接呼ばず、この面を通す）。
/// 既定の実装は <see cref="ProductModelDeviceSync"/>（製品の <c>SyncEngine</c> を呼ぶ）。別のエンジンはこの面を実装して差し替える。
/// </summary>
internal interface IModelDeviceSync
{
    /// <summary>同期・アップロード・ダウンロードのどれか 1 回を流し、結果を返す</summary>
    /// <param name="kind">どれを流すか</param>
    /// <param name="options">エンジンへ渡すオプション</param>
    /// <param name="ct">キャンセルトークン</param>
    Task<ModelSyncOutcome> RunAsync(ModelSyncKind kind, SyncOptions options, CancellationToken ct);

    /// <summary>洗い替えを 1 回流す（options が null ならエンジンへオプションを渡さない）</summary>
    Task<SyncRefreshResult> RefreshAsync(SyncRefreshOptions? options, CancellationToken ct);

    /// <summary>
    /// ServerWins の実行が戻せなかった行（端末の行がサーバーの行と違うまま残った行）の決定を、後の実行へ持ち越すエンジンか
    /// （真＝後の実行はその行の内容をサーバーへ書かず、端末の行をサーバーの行へ戻す。偽＝後の実行がその行の内容を送ってよい）。
    /// 判定役は、真のエンジンだけ、持ち越した決定の内容をサーバーへ書くことを違反にし、果たされた決定をその行の期待から外す
    /// </summary>
    bool CarriesServerWinsDecision { get; }
}

/// <summary>
/// 端末の未送信の行 1 行（同じ表・キーの未送信の変更をまとめた事実）。判定役はこの事実だけを読む。
/// </summary>
/// <param name="Table">表</param>
/// <param name="KeyText">キーのテキスト（端末が持つ綴りのまま。表記の正規化は読む側が行う）</param>
/// <param name="HasUpsert">追加か更新を含むか</param>
/// <param name="HasDelete">削除を含むか</param>
/// <param name="ServerKnowsRow">
/// サーバーがこの行を知っているか（偽＝行はサーバーに届いていない。版なしの表は常に真。版を持つ削除の記録か受理記録が
/// あれば真）。エンジンが送らずに捨てる行かの判定の根拠
/// </param>
/// <param name="ChangeMarks">
/// 変わる印（この行の未送信の変更 1 つごとの識別子。順序つき）。同期の前後で印のどれかが残っているかで、その行の未送信が
/// 消えたかを判定する
/// </param>
internal sealed record ModelPendingRow(
    string Table,
    string KeyText,
    bool HasUpsert,
    bool HasDelete,
    bool ServerKnowsRow,
    IReadOnlyList<long> ChangeMarks
)
{
    /// <summary>
    /// 失敗の文へ載せる表示用の操作名（<see cref="ChangeMarks"/> と同じ並び・同じ長さ。判定には使わない。空でもよい）
    /// </summary>
    public IReadOnlyList<string> Operations { get; init; } = [];
}

/// <summary>端末の同期の状態の面。未送信の行の事実を返す。既定の実装は <see cref="JournalModelDeviceSyncState"/></summary>
internal interface IModelDeviceSyncState
{
    /// <summary>未送信の行の一覧（記録された順＝各行の最初の変更の順）</summary>
    Task<IReadOnlyList<ModelPendingRow>> ReadPendingAsync(CancellationToken ct);

    /// <summary>未送信の変更の件数（行の数ではなく、記録の数）</summary>
    Task<int> CountPendingAsync(CancellationToken ct);
}

/// <summary>製品の <c>SyncEngine</c> を呼ぶ既定の実装（もとの <c>SyncModelWorld</c> の組み立てをそのまま移した）</summary>
internal sealed class ProductModelDeviceSync(SyncEngine engine) : IModelDeviceSync
{
    /// <summary>製品のエンジンは、戻せなかった行の記録を残し、後の実行がその行を送る（持ち越さない）</summary>
    public bool CarriesServerWinsDecision => false;

    public async Task<ModelSyncOutcome> RunAsync(
        ModelSyncKind kind,
        SyncOptions options,
        CancellationToken ct
    )
    {
        switch (kind)
        {
            case ModelSyncKind.Sync:
            {
                var result = await engine.SyncAsync(options, ct);

                return new ModelSyncOutcome(result.Conflicts, result.Truncations)
                {
                    LocalRowChanges = result.LocalRowChanges,
                    Uploaded = result.Uploaded,
                    Downloaded = result.Downloaded,
                    DeletedLocally = result.DeletedLocally,
                    Discarded = result.Discarded,
                    ReportedFlags = (result.HasConflicts, result.HasTruncations),
                };
            }

            case ModelSyncKind.Upload:
            {
                var conflicts = new List<SyncConflict>();
                var (uploaded, discarded) = await engine.UploadAsync(options, conflicts, ct);

                return new ModelSyncOutcome(conflicts, [])
                {
                    Uploaded = uploaded,
                    Discarded = discarded,
                };
            }

            case ModelSyncKind.Download:
            {
                var (downloaded, deletedLocally) = await engine.DownloadAsync(options, ct);

                return new ModelSyncOutcome([], [])
                {
                    Downloaded = downloaded,
                    DeletedLocally = deletedLocally,
                };
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public Task<SyncRefreshResult> RefreshAsync(
        SyncRefreshOptions? options,
        CancellationToken ct
    ) => engine.RefreshAsync(options, ct);
}

/// <summary>
/// 製品のジャーナル（<c>SyncJournal</c>）の記録と受理記録から、未送信の行の事実を導く既定の実装。
/// </summary>
/// <remarks>
/// 同じ（表, 記録のキー）の記録を 1 行にまとめる。<see cref="ModelPendingRow.ServerKnowsRow"/> は、行が無く削除の記録がある行を
/// エンジンが送らずに捨てるかの判定（docs の「送る操作の決め方」の表の「Gone｜Otherwise＝行はサーバーに届いていない」の写し）の
/// 否定：版を持つ表で、削除の記録のどれも版を持たず、受理記録も無いときだけ偽（版なしの表は常に真）。
/// </remarks>
internal sealed class JournalModelDeviceSyncState(SyncJournal journal) : IModelDeviceSyncState
{
    public async Task<IReadOnlyList<ModelPendingRow>> ReadPendingAsync(CancellationToken ct)
    {
        var acknowledged = new HashSet<(string Table, string KeyText)>();

        foreach (var table in ModelTables.All)
        {
            foreach (var keyText in (await journal.ReadAcksAsync(table, ct)).Keys)
            {
                acknowledged.Add((table, keyText));
            }
        }

        return
        [
            .. (await journal.ReadAllAsync(ct))
                .GroupBy(entry => (entry.TableName, entry.KeyText))
                .Select(group => new ModelPendingRow(
                    group.Key.TableName,
                    group.Key.KeyText,
                    group.Any(e => e.Operation == nameof(SyncJournalOperation.Upsert)),
                    group.Any(e => e.Operation == nameof(SyncJournalOperation.Delete)),
                    !group.All(entry =>
                        entry.TableName != ModelTables.Labels
                        && (
                            entry.Operation != nameof(SyncJournalOperation.Delete)
                            || entry.OriginalRowVersion is null
                        )
                        && !acknowledged.Contains((entry.TableName, entry.KeyText))
                    ),
                    [.. group.Select(e => e.Id)]
                )
                {
                    Operations = [.. group.Select(e => e.Operation)],
                }),
        ];
    }

    public async Task<int> CountPendingAsync(CancellationToken ct) =>
        (await journal.ReadAllAsync(ct)).Count;
}

/// <summary>端末の作り方（<see cref="SyncModelWorldOptions.CreateDevice"/>）へ渡す、端末 1 台の注文</summary>
/// <param name="Actor">端末</param>
/// <param name="Server">端末の差分ソース 4 つ（直結か HTTP 越し）</param>
/// <param name="Faults">失敗の注入（ローカルの Save フックが見る）</param>
/// <param name="ExternalTables">表を外部の <see cref="ISyncTable"/> 実装の代役で包むか</param>
internal sealed record SyncModelDeviceRequest(
    ModelActor Actor,
    ISyncModelSources Server,
    SyncModelFaults Faults,
    bool ExternalTables
);

/// <summary>
/// 別のエンジンの端末が <see cref="SyncModelDevice.Install"/> へ渡す部品。リポジトリはジャーナル記録デコレータで包まなくてよい
/// （包まないなら Raw 系は省略して同じものを使う）。エンジンの面と状態の面は別の実装を持てる。今の端末の部品は
/// <see cref="SyncModelDevice.CurrentParts"/> で読める（包みは一部だけ差し替えて載せ直す）。
/// </summary>
internal sealed record SyncModelDeviceParts
{
    public required IModelFolderRepository Folders { get; init; }

    public required IModelLabelRepository Labels { get; init; }

    public required IModelItemRepository Items { get; init; }

    public required IModelEventRepository Events { get; init; }

    /// <summary>記録しないフォルダーのリポジトリ（省略なら <see cref="Folders"/>）</summary>
    public IModelFolderRepository? RawFolders { get; init; }

    /// <summary>記録しないラベルのリポジトリ（省略なら <see cref="Labels"/>）</summary>
    public IModelLabelRepository? RawLabels { get; init; }

    /// <summary>記録しない品目のリポジトリ（省略なら <see cref="Items"/>）</summary>
    public IModelItemRepository? RawItems { get; init; }

    public required IModelDeviceSync Sync { get; init; }

    public required IModelDeviceSyncState State { get; init; }

    /// <summary>
    /// 記録しない書き込み（Raw 系のリポジトリで書いた行）を、端末の未送信の記録から忘れさせる口（引数は表の名前と、その表の
    /// 記述子 <c>GeneratedSyncTables.*</c> の <c>FormatKey</c> の書式のキーのテキスト）。同期のモデルは記録しない書き込みの後、
    /// 書いた行ごとに呼ぶ。省略なら何もしない（記録デコレータの内側のリポジトリで書く端末は、記録しない書き込みが記録を作らない）
    /// </summary>
    public Func<string, string, CancellationToken, Task>? ForgetUntrackedAsync { get; init; }

    /// <summary>
    /// 1 行（表の名前とキーのテキスト）の未送信の記録を全部消す口（判定役の対照の行の包みが、エンジンが記録を捨てた結果を作るのに
    /// 使う）。省略なら製品のジャーナルの表からその行の記録を消す（<see cref="SyncModelDevice.ClearPendingRecordsAsync"/>）
    /// </summary>
    public Func<string, string, CancellationToken, Task>? ClearPendingRecordsAsync { get; init; }
}
