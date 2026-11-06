using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ImportSync;

/// <summary>取込と同期の表の行が手を打つときの状態</summary>
internal enum ImportSyncState
{
    /// <summary>取込：現在の図に失うものが無い（エンティティもクエリも無く未保存でない）</summary>
    ImportEmpty,

    /// <summary>取込：現在の図に中身があり、未保存の変更は無い</summary>
    ImportClean,

    /// <summary>取込：現在の図に未保存の変更がある</summary>
    ImportDirty,

    /// <summary>同期のコマンド（接続ダイアログから同期のダイアログを開くまで）</summary>
    SyncCommand,

    /// <summary>同期のダイアログ：差分を再計算する前（手で置いた差分・スクリプトを含む）</summary>
    SyncFresh,

    /// <summary>同期のダイアログ：差分を再計算した後</summary>
    SyncDiff,

    /// <summary>同期のダイアログ：再計算か実行の最中</summary>
    SyncBusy,
}

/// <summary>取込と同期の表の行の手の種類</summary>
internal enum ImportSyncOp
{
    /// <summary>接続ダイアログ（取り消し・確定・開き方・接続設定の受け渡し）と同期の可否</summary>
    Pick,

    /// <summary>確認（取込の置き換え・同期の実行）を断る・承認する・確認の間に図が変わる</summary>
    Confirm,

    /// <summary>成功まで通す（図の置き換えと完了の通知・同期のダイアログを開く・同期の実行のコミット）</summary>
    Apply,

    /// <summary>失敗（取込の例外・実行器の失敗と例外・取り直しの失敗）</summary>
    Fail,

    /// <summary>差分の選択・全選択・全解除・スクリプトのプレビュー</summary>
    Select,

    /// <summary>差分の再計算</summary>
    Refresh,

    /// <summary>同期のダイアログを閉じる</summary>
    Close,
}

/// <summary>取込と同期の表の行の入力（触る状態と手の種類・本体）</summary>
/// <param name="Touches">触る（状態, 手の種類）の組（網羅の計数に使う）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ImportSyncCase(
    IReadOnlyList<(ImportSyncState State, ImportSyncOp Op)> Touches,
    Func<ImportSyncHarness, Task> Body
);

/// <summary>
/// DB 取込のコマンド（<see cref="QuickER.Db.UI.DbImportCommandService"/>）・DB 同期のコマンド
/// （<see cref="QuickER.Db.UI.DbSyncCommandService"/>）・同期のダイアログ（<see cref="QuickER.Db.UI.SchemaSyncDialogViewModel"/>）の操作の列の表
/// </summary>
/// <remarks>
/// 行は元のテストの入力（図・接続の答え・取込の結果・確認の答え・手の列）と期待をそのまま持つ。組（<see cref="ImportSyncHarness"/>）が
/// どの手の後にも掛ける不変条件を見張り、行の終わりに止めた取込・実行器を開けて表明する。主題ごとの行は
/// <c>ImportSyncRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class ImportSyncRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ImportSyncCase> Table =>
        new(
            EngineIds.Ai,
            [.. ImportRows(), .. SyncCommandRows(), .. SyncPreviewRows(), .. SyncExecuteRows()]
        );

    /// <summary>行を作る</summary>
    private static ScenarioRow<ImportSyncCase> Row(
        string id,
        string description,
        (ImportSyncState State, ImportSyncOp Op)[] touches,
        Func<ImportSyncHarness, Task> body
    ) => new(id, null, description, new ImportSyncCase(touches, body));

    /// <summary>同期の本体の行を作る</summary>
    private static ScenarioRow<ImportSyncCase> Row(
        string id,
        string description,
        (ImportSyncState State, ImportSyncOp Op)[] touches,
        Action<ImportSyncHarness> body
    ) =>
        Row(
            id,
            description,
            touches,
            h =>
            {
                body(h);
                return Task.CompletedTask;
            }
        );

    /// <summary>エンティティを組み立てる</summary>
    private static Entity TableOf(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var column in columns)
        {
            entity.Columns.Add(column);
        }

        return entity;
    }

    /// <summary>列定義を組み立てる</summary>
    private static Column Col(string name, string type, bool pk = false) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = pk,
        };
}
