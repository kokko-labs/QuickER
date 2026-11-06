using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

/// <summary>接続ダイアログの表の行が手を打つときの状態</summary>
internal enum ConnectionDialogState
{
    /// <summary>空のストア・取込モード（入力欄は既定）</summary>
    Fresh,

    /// <summary>保存済みのプロファイル・前回接続・旧形式の connections.json がある（未選択）</summary>
    Profiles,

    /// <summary>プロファイルを選んだ</summary>
    Selected,

    /// <summary>同期モード（方言を図に固定）</summary>
    Sync,

    /// <summary>SQLite（ファイル型）を選んだ</summary>
    Sqlite,

    /// <summary>connections.json を掴まれて読めない（中止）</summary>
    StoreLocked,

    /// <summary>暗号ファイルの書き込み・削除が失敗する（暗号フォルダを塞ぐ・暗号ファイルを掴む）</summary>
    WriteFails,

    /// <summary>保存済みのパスワードを復号できない（DPAPI）</summary>
    Unreadable,

    /// <summary>接続テストの実行中</summary>
    Testing,
}

/// <summary>接続ダイアログの表の行の手の種類</summary>
internal enum ConnectionDialogOp
{
    /// <summary>開く（前回接続の復元・一覧の読込）</summary>
    Open,

    /// <summary>プロファイルの選択</summary>
    Select,

    /// <summary>入力欄・方言の変更と、方言ごとの表示</summary>
    Edit,

    /// <summary>プロファイルの保存</summary>
    Save,

    /// <summary>プロファイルの名前の変更</summary>
    Rename,

    /// <summary>プロファイルの削除</summary>
    Delete,

    /// <summary>接続テスト</summary>
    Test,

    /// <summary>SQLite のファイルの参照・新規作成</summary>
    Browse,

    /// <summary>OK（検証・確定・前回接続の記録）</summary>
    Ok,

    /// <summary>取り消し</summary>
    Cancel,
}

/// <summary>接続ダイアログの表の行の入力（触る状態と手の種類・本体）</summary>
/// <param name="Touches">触る（状態, 手の種類）の組（網羅の計数に使う）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ConnectionDialogCase(
    IReadOnlyList<(ConnectionDialogState State, ConnectionDialogOp Op)> Touches,
    Func<ConnectionDialogHarness, Task> Body
);

/// <summary>接続ダイアログ（<see cref="QuickER.Db.UI.DbConnectionDialogViewModel"/>）の操作の列の表</summary>
/// <remarks>
/// 行は元のテストの入力（ストアの初期状態・手の列）と期待をそのまま持つ。組（<see cref="ConnectionDialogHarness"/>）が
/// どの手の後にも掛ける不変条件を見張り、行の終わりに止めた接続テストを終わらせて表明する。主題ごとの行は
/// <c>ConnectionDialogRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class ConnectionDialogRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ConnectionDialogCase> Table =>
        new(
            EngineIds.Ai,
            [.. OpenRows(), .. ProfileRows(), .. FailureRows(), .. OkRows(), .. SqliteRows()]
        );

    /// <summary>行を作る</summary>
    private static ScenarioRow<ConnectionDialogCase> Row(
        string id,
        string description,
        (ConnectionDialogState State, ConnectionDialogOp Op)[] touches,
        Func<ConnectionDialogHarness, Task> body
    ) => new(id, null, description, new ConnectionDialogCase(touches, body));

    /// <summary>同期の本体の行を作る</summary>
    private static ScenarioRow<ConnectionDialogCase> Row(
        string id,
        string description,
        (ConnectionDialogState State, ConnectionDialogOp Op)[] touches,
        Action<ConnectionDialogHarness> body
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
}
