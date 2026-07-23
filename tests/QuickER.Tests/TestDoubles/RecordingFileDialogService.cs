using QuickER.Gui.Abstractions;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// ファイル選択ダイアログを表示せず設定済みの結果を返し、呼び出し回数・引数を記録する
/// <see cref="IFileDialogService"/> のテスト用スタブ。
/// </summary>
/// <remarks>
/// 「上書き保存はダイアログを開かない」といった呼び出しの有無に関心があるテストで、
/// <see cref="SaveDialogCallCount"/> / <see cref="OpenDialogCallCount"/> を検証するために使う。
/// </remarks>
public sealed class RecordingFileDialogService : IFileDialogService
{
    /// <summary>PickOpenFile が返す結果（null ならキャンセル扱い）</summary>
    public FileDialogResult? OpenResult { get; init; }

    /// <summary>PickSaveFile が返す結果（null ならキャンセル扱い）</summary>
    public FileDialogResult? SaveResult { get; init; }

    /// <summary>PickSaveFile が呼ばれた回数</summary>
    public int SaveDialogCallCount { get; private set; }

    /// <summary>PickOpenFile が呼ばれた回数</summary>
    public int OpenDialogCallCount { get; private set; }

    /// <summary>直近の PickSaveFile に渡された初期ファイル名</summary>
    public string? LastSaveInitialFileName { get; private set; }

    public FileDialogResult? PickOpenFile(string filter)
    {
        OpenDialogCallCount++;
        return OpenResult;
    }

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    )
    {
        SaveDialogCallCount++;
        LastSaveInitialFileName = initialFileName;
        return SaveResult;
    }

    public string? PickFolder(string title, string? initialDirectory = null) => null;
}
