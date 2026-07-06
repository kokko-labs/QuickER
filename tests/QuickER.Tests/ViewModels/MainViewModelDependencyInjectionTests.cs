using System.IO;
using FluentAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// DI 注入された各ダイアログサービスをスタブへ差し替え、<see cref="MainViewModel"/> が
/// ウィンドウを一切表示せずにコマンドを実行できる（＝ View 層から分離されている）ことを検証する
/// </summary>
public class MainViewModelDependencyInjectionTests
{
    /// <summary>AI チャットを開くコマンドが、窓を直接 new せずランチャへ委譲することを検証する</summary>
    [Fact(DisplayName = "OpenAiChatCommand は IAiChatLauncher.Open へ委譲する")]
    public void OpenAiChatCommand_DelegatesToLauncher()
    {
        var launcher = new RecordingAiChatLauncher();
        var vm = new MainViewModel(
            new StubDialogService(),
            new StubAppDialogService(),
            new StubFileDialogService(),
            launcher
        );

        vm.OpenAiChatCommand.Execute(null);

        launcher.OpenedHost.Should().BeSameAs(vm);
    }

    /// <summary>終了時の AI チャット強制終了が、ランチャの Close へ委譲することを検証する</summary>
    [Fact(DisplayName = "CloseAiChatDialog は IAiChatLauncher.Close へ委譲する")]
    public void CloseAiChatDialog_DelegatesToLauncher()
    {
        var launcher = new RecordingAiChatLauncher();
        var vm = new MainViewModel(
            new StubDialogService(),
            new StubAppDialogService(),
            new StubFileDialogService(),
            launcher
        );

        vm.CloseAiChatDialog();

        launcher.CloseCount.Should().Be(1);
    }

    /// <summary>保存コマンドが、ファイル選択スタブの返すパスへ実際にドキュメントを書き出すことを検証する</summary>
    [Fact(DisplayName = "SaveCommand はファイル選択結果のパスへ保存する")]
    public void SaveCommand_WritesDocumentToPickedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-di-{Guid.NewGuid()}.json");
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(path, 1) };
        var vm = new MainViewModel(
            new StubDialogService(),
            new StubAppDialogService(),
            files,
            new RecordingAiChatLauncher()
        );
        vm.AddEntityCommand.Execute(null);

        try
        {
            vm.SaveCommand.Execute(null);

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>ファイル選択がキャンセル（null）された場合は何も書き出さないことを検証する</summary>
    [Fact(DisplayName = "SaveCommand はキャンセル時に保存しない")]
    public void SaveCommand_DoesNothing_WhenCancelled()
    {
        var files = new StubFileDialogService { SaveResult = null };
        var vm = new MainViewModel(
            new StubDialogService(),
            new StubAppDialogService(),
            files,
            new RecordingAiChatLauncher()
        );
        vm.AddEntityCommand.Execute(null);

        var act = () => vm.SaveCommand.Execute(null);

        act.Should().NotThrow();
    }

    // ---------------- スタブ実装 ----------------

    /// <summary>メッセージボックスを表示せず既定応答を返すスタブ</summary>
    private sealed class StubDialogService : IDialogService
    {
        public bool Confirm(string message, string title) => true;

        public bool ConfirmWarning(string message, string title) => true;

        public void ShowInformation(string message, string title) { }

        public void ShowError(string message, string title) { }
    }

    /// <summary>アプリ固有ダイアログを表示せず常にキャンセル相当を返すスタブ</summary>
    private sealed class StubAppDialogService : IAppDialogService
    {
        public CSharpGenerationDialogResult? ShowCSharpGenerationDialog(
            IDatabaseProvider currentProvider
        ) => null;

        public DbConnectionDialogResult? ShowDbConnectionDialog(
            DbConnectionDialogMode mode,
            IDatabaseProvider? fixedProvider = null,
            string? title = null
        ) => null;

        public void ShowSchemaSyncDialog(
            IDatabaseProvider provider,
            DbConnectionSettings settings,
            IReadOnlyList<Entity> entities,
            IReadOnlyList<Relationship> relationships
        ) { }

        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;
    }

    /// <summary>ファイル選択ダイアログを表示せず、設定済みの結果を返すスタブ</summary>
    private sealed class StubFileDialogService : IFileDialogService
    {
        public FileDialogResult? OpenResult { get; init; }

        public FileDialogResult? SaveResult { get; init; }

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

    /// <summary>AI チャットウィンドウを生成せず、呼び出しを記録するスタブ</summary>
    private sealed class RecordingAiChatLauncher : IAiChatLauncher
    {
        public MainViewModel? OpenedHost { get; private set; }

        public int CloseCount { get; private set; }

        public void Open(MainViewModel host) => OpenedHost = host;

        public void Close() => CloseCount++;
    }
}
