using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 将来版フォーマットの文書を、この版の形式で上書きする前に確認することを検証するテストクラス。
/// </summary>
/// <remarks>
/// 将来版の文書は「続行」を選べば開けるが、読み込んだ時点で未知のプロパティは落ちている。
/// 開いた直後に記録される内容ハッシュはディスクと一致するため、外部変更の照合（保存前のディスク照合）
/// では止まらず、その後の Ctrl+S が黙って Version を現行へ書き戻して未知データを消す。
/// <para>
/// 判定は「これから上書きするファイルの版番号」をディスクから読んで行う（VM の状態に持たない）。
/// そのため図の置換後・新規作成後・別セッションが書いたファイルでも同じように効き、
/// 一度上書きして現行フォーマットになれば出なくなる——この 4 つをそれぞれ固定する。
/// </para>
/// </remarks>
public sealed class MainViewModelNewerFormatSaveTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-newerformat-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelNewerFormatSaveTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    /// <summary>指定フォーマット版の図をファイルへ書き出す</summary>
    private static void WriteDiagram(string path, string tableName, int version)
    {
        JsonStorageService.Save(
            path,
            new DiagramDocument
            {
                Version = version,
                Schema = new ErDiagram
                {
                    Entities = { new Entity { TableName = tableName } },
                    TargetDbms = "sqlserver",
                },
                Layout = null,
            }
        );
    }

    /// <summary>指定パスの図を開いた（現在パス紐付き）VM を返す（将来版の確認は dialogs の応答に従う）</summary>
    private MainViewModel OpenDiagram(string path, StubDialogService dialogs)
    {
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { OpenResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        vm.OpenCommand.Execute(null);
        return vm;
    }

    /// <summary>ファイルのフォーマット版を読み取る</summary>
    private static int ReadVersion(string path) => JsonStorageService.Load(path).Version;

    /// <summary>将来版の文書への上書き保存は確認し、キャンセルすると書き戻さないことを検証する</summary>
    [Fact(DisplayName = "上書き保存: 将来版の文書を確認し、キャンセルすると書き戻さない")]
    public void Save_NewerFormatDocument_CancelKeepsFile()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);
        vm.Entities.Should().ContainSingle("将来版でも続行すれば開ける");
        vm.AddEntityCommand.Execute(null);

        dialogs.ConfirmResult = false;
        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().Contain(Strings.Confirm_OverwriteNewerFormat);
        ReadVersion(path)
            .Should()
            .Be(DiagramDocument.CurrentVersion + 1, "キャンセルでは書き戻さない");
        vm.IsDirty.Should().BeTrue();
    }

    /// <summary>将来版の確認に続行すると、現行フォーマットで上書きすることを検証する</summary>
    [Fact(DisplayName = "上書き保存: 将来版の確認に続行すると現行フォーマットで上書きする")]
    public void Save_NewerFormatDocument_ConfirmOverwrites()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().Contain(Strings.Confirm_OverwriteNewerFormat);
        ReadVersion(path).Should().Be(DiagramDocument.CurrentVersion);
        vm.IsDirty.Should().BeFalse();
    }

    /// <summary>現行フォーマットの文書では将来版の確認を出さないことを検証する</summary>
    [Fact(DisplayName = "上書き保存: 現行フォーマットの文書では確認しない")]
    public void Save_CurrentFormatDocument_DoesNotConfirm()
    {
        var path = Path.Combine(_folder, "Current.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion);

        var dialogs = new StubDialogService();
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.StatusMessage.Should().Be(Strings.Status_Saved);
    }

    /// <summary>
    /// 図を丸ごと置き換えた後でも、保存先が将来版のファイルなら確認することを検証する
    /// </summary>
    /// <remarks>
    /// 図の置換では現在パスが遷移しない（DB 取込・AI 生成はパスを維持する）ため、置換後の Ctrl+S は
    /// 将来版のファイルへ向かう。判定を VM の状態に持たせると、この経路で確認が出なくなる。
    /// </remarks>
    [Fact(DisplayName = "上書き保存: 図を置換しても保存先が将来版なら確認する")]
    public void Save_AfterDiagramReplacement_StillConfirms()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);

        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { new Entity { TableName = "Imported" } },
                TargetDbms = "sqlserver",
            }
        );

        dialogs.WarningConfirmMessages.Clear();
        dialogs.ConfirmResult = false;
        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteNewerFormat);
        ReadVersion(path)
            .Should()
            .Be(DiagramDocument.CurrentVersion + 1, "キャンセルでは書き戻さない");
    }

    /// <summary>新規作成で図を空にした後でも、保存先が将来版のファイルなら確認することを検証する</summary>
    /// <remarks>
    /// 新規作成は現在パスを落とすため、保存はダイアログ経由で同じファイルを選ぶ形になる。
    /// 「今開いているファイルか」に依らず、書き潰す相手が将来版なら確認する。
    /// </remarks>
    [Fact(DisplayName = "上書き保存: 新規作成の後でも保存先が将来版なら確認する")]
    public void Save_AfterNewDiagram_ConfirmsWhenTargetIsNewerFormat()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService
            {
                OpenResult = new(path, 1),
                SaveResult = new(path, 1),
            }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        vm.OpenCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);
        vm.CurrentFilePath.Should().BeNull("新規作成は現在パスを落とす");

        dialogs.WarningConfirmMessages.Clear();
        dialogs.ConfirmResult = false;
        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteNewerFormat);
        ReadVersion(path).Should().Be(DiagramDocument.CurrentVersion + 1);
    }

    /// <summary>版を名乗っているのに解釈できないファイルも、確認する側へ倒すことを検証する</summary>
    /// <remarks>
    /// <c>"Version": "2"</c> はこの版が書かない形＝別の版か手編集のファイル。「将来版ではない」と読むと、
    /// 保存先が現在の文書でないこの経路では内容ハッシュ照合の網も掛からず、確認なしで上書きしてしまう。
    /// </remarks>
    [Fact(DisplayName = "上書き保存: 版を解釈できないファイルも確認する")]
    public void Save_UnreadableVersion_Confirms()
    {
        var path = Path.Combine(_folder, "Odd.json");
        File.WriteAllText(path, "{\"Version\":\"2\",\"Schema\":{\"Entities\":[]}}");

        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(
            dialogs,
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        vm.AddEntityCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteNewerFormat);
        File.ReadAllText(path).Should().Contain("\"Version\":\"2\"", "キャンセルでは書き戻さない");
    }

    /// <summary>1 度上書きしてしまえば、次の保存では確認しないことを検証する（ディスクが現行版になったため）</summary>
    [Fact(DisplayName = "上書き保存: 一度上書きした後の再保存では確認しない")]
    public void Save_AfterOverwritingOnce_DoesNotConfirmAgain()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        ReadVersion(path).Should().Be(DiagramDocument.CurrentVersion);

        dialogs.WarningConfirmMessages.Clear();
        vm.AddEntityCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.StatusMessage.Should().Be(Strings.Status_Saved);
    }

    /// <summary>将来版と外部変更が同時に立つとき、確認は将来版の文言 1 回だけであることを検証する</summary>
    [Fact(DisplayName = "上書き保存: 将来版と外部変更が重なっても確認は 1 回だけ")]
    public void Save_NewerFormatAndExternalChange_ConfirmsOnce()
    {
        var path = Path.Combine(_folder, "Newer.json");
        WriteDiagram(path, "Original", DiagramDocument.CurrentVersion + 1);

        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = OpenDiagram(path, dialogs);
        vm.AddEntityCommand.Execute(null);

        // 開いた後に外部が同じファイルを書き換える（＝両方の条件が立つ）
        WriteDiagram(path, "External", DiagramDocument.CurrentVersion + 1);
        dialogs.WarningConfirmMessages.Clear();
        dialogs.ConfirmResult = false;

        vm.SaveCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_OverwriteNewerFormat, "失うものが大きい方の文言を 1 回だけ出す");
    }
}
