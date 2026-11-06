using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>モック生成のダイアログの表の行が手を打つときの状態</summary>
internal enum MockDialogState
{
    /// <summary>作った直後（モックフォルダなし）</summary>
    Idle,

    /// <summary>モックフォルダが使えない（相対パス・モックフォルダでない既存フォルダ・壊れた mock.json）</summary>
    Blocked,

    /// <summary>モックフォルダを指定した・会話の前（既存のモックフォルダを開いた場合を含む）</summary>
    Ready,

    /// <summary>会話を開始した（ターンの外）</summary>
    Conversation,

    /// <summary>ターンの実行中</summary>
    Turn,

    /// <summary>モックプロジェクトの生成中</summary>
    GenRun,

    /// <summary>モックプロジェクトの生成が終わった後</summary>
    GenDone,
}

/// <summary>モック生成のダイアログの表の行の手の種類</summary>
internal enum MockDialogOp
{
    /// <summary>モックフォルダの指定</summary>
    SetFolder,

    /// <summary>接続方式・プロバイダー・接続の確認の反映・モデル</summary>
    Backend,

    /// <summary>会話の開始・送信・ターンの終わり・中断</summary>
    Converse,

    /// <summary>画面の保存・削除・共有 CSS の保存（ツールの実行）と画面一覧</summary>
    Screen,

    /// <summary>添付の追加・送信・残り方</summary>
    Attach,

    /// <summary>単一 HTML・設計書の出力</summary>
    Export,

    /// <summary>モックプロジェクトの生成（可否・確認・実行・結果・フォルダを開く）</summary>
    Generate,

    /// <summary>設定の保存とモデルの履歴の記録</summary>
    Persist,

    /// <summary>エンジンの破棄・終了の中断要求</summary>
    Shutdown,

    /// <summary>画面全体のクリア</summary>
    Reset,
}

/// <summary>モック生成のダイアログの表の行の入力（触る状態と手の種類・本体）</summary>
/// <param name="Touches">触る（状態, 手の種類）の組（網羅の計数に使う）</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record MockDialogCase(
    IReadOnlyList<(MockDialogState State, MockDialogOp Op)> Touches,
    Func<MockDialogHarness, Task> Body
);

/// <summary>モック生成のダイアログ（<see cref="QuickER.AI.Mock.MockGenerationDialogViewModel"/>）の操作の列の表</summary>
/// <remarks>
/// 行は元のテストの入力（初期状態・手の列）と期待をそのまま持つ。組（<see cref="MockDialogHarness"/>）が
/// どの手の後にも掛ける不変条件を見張り、行の終わりに実行中を解いてエンジンを破棄して表明する。主題ごとの行は
/// <c>MockDialogRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class MockDialogRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<MockDialogCase> Table =>
        new(
            EngineIds.Ai,
            [
                .. FolderRows(),
                .. ConversationRows(),
                .. ScreenRows(),
                .. GenerateRows(),
                .. BackendRows(),
                .. ShutdownRows(),
            ]
        );

    /// <summary>行を作る</summary>
    private static ScenarioRow<MockDialogCase> Row(
        string id,
        string description,
        (MockDialogState State, MockDialogOp Op)[] touches,
        Func<MockDialogHarness, Task> body
    ) => new(id, null, description, new MockDialogCase(touches, body));

    /// <summary>同期の本体の行を作る</summary>
    private static ScenarioRow<MockDialogCase> Row(
        string id,
        string description,
        (MockDialogState State, MockDialogOp Op)[] touches,
        Action<MockDialogHarness> body
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

    /// <summary>PNG シグネチャ付きバイト列（添付の行用）</summary>
    private static byte[] PngBytes()
    {
        var data = new byte[16];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(signature, data, signature.Length);
        return data;
    }

    /// <summary>書式文字列の先頭（最初のプレースホルダより前）（文言の照合用）</summary>
    private static string FormatPrefix(string format) => format[..format.IndexOf('{')];
}
