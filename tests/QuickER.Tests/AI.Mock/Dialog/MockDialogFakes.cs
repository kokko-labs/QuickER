using System.IO;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Gui.Abstractions;
using QuickER.Model;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>モックプロジェクトの生成（第 2 ステップ）を台本どおりに返す偽の生成器（実のプロセスへ行かない）</summary>
internal sealed class FakeMockProjectGenerator : IMockProjectGenerator
{
    private TaskCompletionSource? _hold;

    public bool ClaudeAvailable { get; set; } = true;

    public bool CodexAvailable { get; set; } = true;

    public bool DotnetAvailable { get; set; } = true;

    public bool ResultSuccess { get; set; } = true;

    /// <summary>返す結果の中断フラグ（true でユーザー中断＝VM は完了ダイアログを出さない）</summary>
    public bool ResultInterrupted { get; set; }

    /// <summary>返す結果の「最終ビルドの確認を取り消した」フラグ（利用者自身の選択＝失敗ではない）</summary>
    public bool ResultBuildDeclined { get; set; }

    /// <summary>中断が届いた回数</summary>
    public int InterruptCount { get; private set; }

    public int GenerateCallCount { get; private set; }

    public string? CapturedOutputFolder { get; private set; }

    public string? CapturedProjectName { get; private set; }

    public string? CapturedMockFolder { get; private set; }

    public string? CapturedInstructions { get; private set; }

    public ErChatBackendKind? CapturedBackend { get; private set; }

    public string? CapturedModel { get; private set; }

    public string? CapturedModelProvider { get; private set; }

    /// <summary>最後に渡された取り消しのトークン（中断が届いたかの確認用）</summary>
    public CancellationToken CapturedToken { get; private set; }

    public IReadOnlyList<MockProjectTarget> Targets { get; } =
    [MockProjectTarget.Blazor, MockProjectTarget.Wpf];

    public MockProjectTarget? CapturedTarget { get; private set; }

    /// <summary>進捗の出し方を差し替える台本（null なら 1 行だけ出す）</summary>
    public Action<Action<string>>? ProgressScript { get; set; }

    /// <summary>最後に渡された最終ビルドの確認 seam（VM が配線しているかの確認用）</summary>
    public Func<IReadOnlyList<string>, bool>? CapturedConfirmBuild { get; private set; }

    /// <summary>次の生成を <see cref="Release"/> まで止める（生成の実行中を作る）</summary>
    public void HoldNext() => _hold = new TaskCompletionSource();

    /// <summary>止めている生成を進める（止めていなければ何もしない）。進めたら true</summary>
    public bool Release()
    {
        var hold = _hold;
        _hold = null;
        return hold?.TrySetResult() ?? false;
    }

    public bool IsAgentAvailable(ErChatBackendKind backend) =>
        backend == ErChatBackendKind.Codex ? CodexAvailable : ClaudeAvailable;

    public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DotnetAvailable);

    public async Task<MockProjectGenerationResult> GenerateAsync(
        ErDiagram diagram,
        string mockFolder,
        string? additionalInstructions,
        string outputDirectory,
        string projectName,
        MockProjectTarget target,
        ErChatBackendKind backend,
        string model,
        string modelProvider,
        Action<string> onProgress,
        Func<IReadOnlyList<string>, bool>? confirmBuild = null,
        CancellationToken cancellationToken = default
    )
    {
        GenerateCallCount++;
        CapturedConfirmBuild = confirmBuild;
        CapturedOutputFolder = outputDirectory;
        CapturedProjectName = projectName;
        CapturedTarget = target;
        CapturedMockFolder = mockFolder;
        CapturedInstructions = additionalInstructions;
        CapturedBackend = backend;
        CapturedModel = model;
        CapturedModelProvider = modelProvider;
        CapturedToken = cancellationToken;

        if (ProgressScript is null)
        {
            onProgress("進捗: 生成中...\n");
        }
        else
        {
            ProgressScript(onProgress);
        }

        if (_hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return new MockProjectGenerationResult(
            ResultSuccess,
            ResultSuccess ? "完了しました。" : "失敗しました。",
            outputDirectory,
            Path.Combine(outputDirectory, "quickr-mock-generation.log"),
            ResultInterrupted,
            ResultBuildDeclined
        );
    }

    public Task InterruptAsync()
    {
        InterruptCount++;
        return Task.CompletedTask;
    }
}

/// <summary>選ぶ結果を設定でき、呼び出しを記録するファイルダイアログ（実のダイアログを開かない）</summary>
internal sealed class MockDialogFileDialogService : IFileDialogService
{
    /// <summary>保存先の選択が返す結果（null ならキャンセル）</summary>
    public FileDialogResult? SaveResult { get; set; }

    /// <summary>フォルダの選択が返すパス（null ならキャンセル）</summary>
    public string? FolderResult { get; set; }

    /// <summary>最後の保存先の選択に渡された既定のファイル名</summary>
    public string? LastInitialFileName { get; private set; }

    /// <summary>保存先の選択が呼ばれた回数</summary>
    public int PickSaveFileCallCount { get; private set; }

    /// <summary>フォルダの選択が呼ばれた回数</summary>
    public int PickFolderCallCount { get; private set; }

    public FileDialogResult? PickOpenFile(string filter) => null;

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    )
    {
        PickSaveFileCallCount++;
        LastInitialFileName = initialFileName;
        return SaveResult;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        PickFolderCallCount++;
        return FolderResult;
    }
}

/// <summary>UI スレッドへの移送をその場の同期実行にする</summary>
internal sealed class MockDialogSyncDispatcher : IUiDispatcher
{
    public T Invoke<T>(Func<T> func) => func();
}
