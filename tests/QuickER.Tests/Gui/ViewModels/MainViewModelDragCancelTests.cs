using System.IO;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 外部変更の検知が、進行中のドラッグを開始時点へ戻して打ち切ることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <c>DragBehavior</c> は対象 ViewModel を静的フィールドで保持するため、再読込で図が置き換わったあとに
/// マウスを離すと、破棄済み要素から生きた <c>UndoRedoManager</c> を引いて移動コマンドを積む
/// （＝再読込直後のクリーンな文書が即ダーティ化し、幽霊 Undo が残る）。またダーティ時の確認ダイアログは
/// マウスキャプチャ中に開き得るため、<c>MouseLeftButtonUp</c> が届かず進行中状態が残る。
/// どちらも入口（外部変更の処理開始時）でドラッグを打ち切ることで構造的に消す。
/// </para>
/// <para>静的状態を触るため <c>CanvasInteractionState</c> コレクションで直列化する。</para>
/// </remarks>
[Collection("CanvasInteractionState")]
public sealed class MainViewModelDragCancelTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-dragcancel-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelDragCancelTests()
    {
        Directory.CreateDirectory(_folder);
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
    }

    public void Dispose()
    {
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();

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

    /// <summary>単一テーブルの図をファイルへ書き出し、その内容ハッシュを返す（外部書き込みの模擬）</summary>
    private static string WriteDiagram(string path, string tableName)
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
        return DocumentContentHash.TryCompute(path)!;
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

    /// <summary>クリーン時の自動再読込で、進行中ドラッグが打ち切られ幽霊履歴が残らないことを検証する</summary>
    [Fact(DisplayName = "クリーン: ドラッグ中の外部変更で再読込しても履歴は空のまま")]
    public void Clean_ExternalChangeDuringDrag_CancelsDragAndKeepsHistoryEmpty()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var vm = OpenClean(path, "Original", new StubDialogService());

        var dragged = vm.Entities[0];
        DragBehavior.BeginInteractionForTests(dragged, startX: dragged.X, startY: dragged.Y);

        // ドラッグ中の逐次反映を模す（この座標は履歴に無い）
        dragged.X += 250;
        dragged.Y += 150;

        var externalHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);

        vm.Entities.Should().ContainSingle(e => e.TableName == "External");
        DragBehavior
            .IsInteractionActiveForTests.Should()
            .BeFalse("再読込の入口でドラッグを打ち切る");
        vm.UndoRedo.CanUndo.Should().BeFalse("幽霊 MoveEntityCommand を積まない");
        vm.IsDirty.Should().BeFalse("再読込直後はクリーンのまま");
    }

    /// <summary>ダーティ時の確認で「続行」を選んだとき、ドラッグ分の座標が開始位置へ戻ることを検証する</summary>
    /// <remarks>
    /// 図は置き換わらないため、打ち切りが単なる状態リセットだと「追跡外の位置変更」が残り Undo で戻せない。
    /// </remarks>
    [Fact(DisplayName = "ダーティ: ドラッグ中の外部変更で続行してもドラッグは開始位置へ戻る")]
    public void Dirty_ExternalChangeDuringDrag_RestoresDragStartPosition()
    {
        var path = Path.Combine(_folder, "Doc.json");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = OpenClean(path, "Original", dialogs);

        // 未保存編集でダーティにする（確認ダイアログ経路へ入れるため）
        vm.AddEntityCommand.Execute(null);
        vm.IsDirty.Should().BeTrue();

        var dragged = vm.Entities[0];
        var startX = dragged.X;
        var startY = dragged.Y;
        DragBehavior.BeginInteractionForTests(dragged, startX, startY);

        dragged.X = startX + 320;
        dragged.Y = startY + 240;

        var generationBefore = vm.UndoRedo.ChangeGeneration;
        var externalHash = WriteDiagram(path, "External");
        vm.RaiseExternalChangeForTests(DocumentFileChangeKind.Modified, externalHash);

        dialogs.WarningConfirmMessages.Should().ContainSingle("ダーティなので確認する");
        dragged.X.Should().Be(startX, "ドラッグ分は開始位置へ戻す");
        dragged.Y.Should().Be(startY);
        DragBehavior.IsInteractionActiveForTests.Should().BeFalse();
        vm.UndoRedo.ChangeGeneration.Should()
            .Be(generationBefore, "打ち切りは履歴・変更世代へ触れない");
        vm.IsDirty.Should().BeTrue("元からあった未保存変更はそのまま残る");
    }
}
