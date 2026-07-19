using QuickER.Gui.Abstractions;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// ファイル選択ダイアログを表示せず、設定済みの結果を返す <see cref="IFileDialogService"/> のテスト用スタブ。
/// </summary>
/// <remarks>
/// 各 Pick メソッドの戻り値をプロパティで差し替えられる（未設定はキャンセル相当の null）。
/// 開く／保存／フォルダ選択のいずれかの結果に関心があるテストが、必要な戻り値だけを設定して使う。
/// </remarks>
public sealed class StubFileDialogService : IFileDialogService
{
    /// <summary>PickOpenFile が返す結果（null ならキャンセル扱い）</summary>
    public FileDialogResult? OpenResult { get; init; }

    /// <summary>PickSaveFile が返す結果（null ならキャンセル扱い）</summary>
    public FileDialogResult? SaveResult { get; init; }

    /// <summary>PickFolder が返すフォルダパス（null ならキャンセル扱い）</summary>
    public string? FolderResult { get; init; }

    public FileDialogResult? PickOpenFile(string filter) => OpenResult;

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    ) => SaveResult;

    public string? PickFolder(string title, string? initialDirectory = null) => FolderResult;
}
