using QuickER.Gui.Abstractions;

namespace QuickER.Tests.TestDoubles;

/// <summary>
/// 何も表示せず常にキャンセル相当（null）を返す <see cref="IFileDialogService"/> のテスト用スタブ。
/// </summary>
/// <remarks>
/// ファイル選択の結果に関心が無い（依存解決だけ満たしたい）テストで、DI へ登録する no-op 実装。
/// 結果を差し替えたい場合は、戻り値を設定できる <see cref="StubFileDialogService"/> を使う。
/// </remarks>
public sealed class NullFileDialogService : IFileDialogService
{
    public FileDialogResult? PickOpenFile(string filter) => null;

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    ) => null;

    public string? PickFolder(string title, string? initialDirectory = null) => null;
}
