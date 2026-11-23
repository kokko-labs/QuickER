using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 端末のエンジンの面。同期のモデルが端末の同期・洗い替えを流す入口（エンジンを直接呼ばず、この面を通す）。
/// 実装は <see cref="EngineModelDeviceSync"/>（<c>SyncEngine</c> を呼ぶ）。判定役の包みはこの面を実装して差し替える。
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
/// サーバーがこの行を知っているか（偽＝行はサーバーに届いていない。版なしの表は常に真）。エンジンが送らずに捨てる行かの判定の根拠
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

/// <summary>端末の同期の状態の面。未送信の行の事実を返す。実装は <see cref="EngineModelDeviceSyncState"/></summary>
internal interface IModelDeviceSyncState
{
    /// <summary>未送信の行の一覧（記録された順＝各行の最初の変更の順）</summary>
    Task<IReadOnlyList<ModelPendingRow>> ReadPendingAsync(CancellationToken ct);

    /// <summary>未送信の変更の件数</summary>
    Task<int> CountPendingAsync(CancellationToken ct);
}

/// <summary>端末の作り方（<see cref="SyncModelWorldOptions.CreateDevice"/>）へ渡す、端末 1 台の注文</summary>
/// <param name="Actor">端末</param>
/// <param name="Server">端末の差分ソース 4 つ（直結か HTTP 越し）</param>
/// <param name="Faults">失敗の注入（ローカルの Save フックが見る）</param>
internal sealed record SyncModelDeviceRequest(
    ModelActor Actor,
    ISyncModelSources Server,
    SyncModelFaults Faults
);

/// <summary>
/// 端末の作り方が <see cref="SyncModelDevice.Install"/> へ渡す部品。エンジンの面と状態の面は別の実装を持てる。今の端末の部品は
/// <see cref="SyncModelDevice.CurrentParts"/> で読める（包みは一部だけ差し替えて載せ直す）。
/// </summary>
internal sealed record SyncModelDeviceParts
{
    public required IModelFolderRepository Folders { get; init; }

    public required IModelLabelRepository Labels { get; init; }

    public required IModelItemRepository Items { get; init; }

    public required IModelEventRepository Events { get; init; }

    public required IModelDeviceSync Sync { get; init; }

    public required IModelDeviceSyncState State { get; init; }

    /// <summary>
    /// 記録しない書き込みを流す関数を受け取り、流す間は端末の未送信の記録が残らないように流す口。口は接続を開き、製品の
    /// 記録しない書き込みの API（<c>SyncLocalTracking.WriteUntrackedAsync</c>）の上で、その接続を関数へ渡す（関数は渡された
    /// 接続の SQL か <c>ILocalRowStore</c> だけで書く）。第 3 引数が真なら外部キーの検査を切った接続を渡す。前からある未送信の
    /// 記録は変えない（記録を消すのは <see cref="ClearPendingRecordsAsync"/> の別の手順）
    /// </summary>
    public required Func<
        Func<DbConnection, Task>,
        CancellationToken,
        bool,
        Task
    > WriteUntrackedAsync { get; init; }

    /// <summary>
    /// 1 行（表の名前とキーのテキスト）の未送信の記録を全部消す口（判定役の対照の行の包みが、エンジンが記録を捨てた結果を作るのに
    /// 使う）
    /// </summary>
    public required Func<
        string,
        string,
        CancellationToken,
        Task
    > ClearPendingRecordsAsync { get; init; }
}
