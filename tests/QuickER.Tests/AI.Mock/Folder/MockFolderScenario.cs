namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>行の主な手の入口</summary>
internal enum MockFolderEntry
{
    /// <summary>MockFolderStore.CreateNew</summary>
    CreateNew,

    /// <summary>MockFolderStore.Open（開いた後の手まで流す行。読み込みの検証は出力の表が持つ）</summary>
    Open,

    /// <summary>MockFolderStore.SaveScreen</summary>
    SaveScreen,

    /// <summary>MockFolderStore.RemoveScreen</summary>
    RemoveScreen,

    /// <summary>MockFolderStore.SaveStylesheet</summary>
    SaveStylesheet,

    /// <summary>MockFolderStore.UpdateSourceSchema</summary>
    UpdateSourceSchema,

    /// <summary>読み取り（GetScreenHtml・IsMockFolder など）</summary>
    Read,

    /// <summary>純関数の正規化（NormalizeOperations・NormalizeEntities）</summary>
    Normalize,

    /// <summary>セッションの会話の開始（新規・再開の初回ターン）</summary>
    SessionStart,

    /// <summary>セッションの修正の指示（初回の後のターン）</summary>
    SessionFeedback,

    /// <summary>セッションのツールの実行</summary>
    SessionTool,
}

/// <summary>行の主な手の結果</summary>
internal enum MockFolderOutcome
{
    /// <summary>成功する</summary>
    Succeeds,

    /// <summary>入力・状態を断る（例外・失敗の結果・失敗や中断のターン。I/O は失敗しない）</summary>
    Refused,

    /// <summary>書き込みが I/O で失敗する（読み取り専用の mock.json・同名のフォルダ・別のプロセスの作成）</summary>
    IoFailure,
}

/// <summary>モックフォルダの保存単位の表の 1 行の入力</summary>
/// <param name="Entry">主な手の入口</param>
/// <param name="Outcome">主な手の結果</param>
/// <param name="Body">組の手で流して表明する本体（組の不変条件はテストが本体の後に表明する）</param>
internal sealed record MockFolderScenario(
    MockFolderEntry Entry,
    MockFolderOutcome Outcome,
    Func<MockFolderHarness, Task> Body
);
