namespace QuickER.Gui.Abstractions;

/// <summary>ファイル選択ダイアログの結果（選択パスとフィルター選択位置）</summary>
/// <param name="Path">選択されたファイルのフルパス</param>
/// <param name="FilterIndex">選択されたフィルターの 1 始まりインデックス（拡張子判定に使用）</param>
public sealed record FileDialogResult(string Path, int FilterIndex);

/// <summary>ファイル / フォルダ選択ダイアログの表示を抽象化するインターフェース</summary>
/// <remarks>ViewModel から <c>Microsoft.Win32</c> への直接依存を除去し、単体テストではスタブへ差し替える</remarks>
public interface IFileDialogService
{
    /// <summary>ファイルを開くダイアログを表示する（キャンセル時は null）</summary>
    FileDialogResult? PickOpenFile(string filter);

    /// <summary>
    /// 複数ファイルを選択できる「開く」ダイアログを表示する（キャンセル・未選択時は空配列）。
    /// 既定実装は単一選択（<see cref="PickOpenFile"/>）へフォールバックする（既存スタブの互換のため）。
    /// </summary>
    /// <param name="filter">ファイルフィルタ</param>
    IReadOnlyList<string> PickOpenFiles(string filter)
    {
        var picked = PickOpenFile(filter);
        return picked is null ? Array.Empty<string>() : [picked.Path];
    }

    /// <summary>ファイルを保存するダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="initialFileName">初期表示するファイル名（省略可）</param>
    /// <param name="initialDirectory">初期表示するフォルダ（省略可）</param>
    FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    );

    /// <summary>フォルダ選択ダイアログを表示する（キャンセル時は null）</summary>
    /// <param name="initialDirectory">初期表示するフォルダ（省略可）</param>
    string? PickFolder(string title, string? initialDirectory = null);
}
