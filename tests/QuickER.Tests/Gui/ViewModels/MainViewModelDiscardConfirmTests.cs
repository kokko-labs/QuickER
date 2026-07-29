using System.IO;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図の内容を失う操作の確認水準（クリーン＝Question／ダーティ＝Warning）を検証するテストクラス。
/// </summary>
/// <remarks>
/// ダーティ時に警告水準（<see cref="QuickER.Gui.Abstractions.IDialogService.ConfirmWarning"/>）へ
/// 切り替わる分岐は <c>MainViewModelTests</c> の NewDiagram テストが担うため、
/// ここでは「保存済みクリーン状態なら通常確認（Question）のまま」の側を、
/// 実ファイルへ紐付いたクリーン VM で検証する。実ファイル入出力は一時フォルダへ隔離する。
/// </remarks>
public sealed class MainViewModelDiscardConfirmTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-discard-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelDiscardConfirmTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>単一テーブルの図をファイルへ書き出す（クリーンな現在ファイルの準備）</summary>
    private static void WriteDiagram(string path, string tableName)
    {
        var document = new DiagramDocument
        {
            Schema = new ErDiagram
            {
                Entities = { new Entity { TableName = tableName } },
                TargetDbms = "sqlserver",
            },
            Layout = null,
        };
        JsonStorageService.Save(path, document);
    }

    /// <summary>指定内容の図を書き出してから、その図を開いた（現在パス紐付き・クリーン）VM を返す</summary>
    private MainViewModel OpenClean(string path, string tableName, StubDialogService dialogs)
    {
        WriteDiagram(path, tableName);
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
        vm.IsDirty.Should().BeFalse();
        return vm;
    }

    /// <summary>保存済みクリーン状態の図クリアは、警告でなく通常確認（Question）で確認することを検証する</summary>
    [Fact(DisplayName = "NewDiagram: クリーン時は通常確認（Question）のまま")]
    public void NewDiagram_Clean_UsesPlainConfirm()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = OpenClean(path, "Saved", dialogs);

        vm.NewDiagramCommand.Execute(null);

        // 保存済み内容はファイルから開き直せるため、警告水準へ引き上げない
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_ClearDiagram);
        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.Entities.Should().ContainSingle(e => e.TableName == "Saved");
    }
}
