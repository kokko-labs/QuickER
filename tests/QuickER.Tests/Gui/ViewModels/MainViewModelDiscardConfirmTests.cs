using System.IO;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図の内容を失う操作の「確認を出すか」（<see cref="MainViewModel.HasNothingToLose"/>）と
/// 確認水準（クリーン＝Question／ダーティ＝Warning）を検証するテストクラス。
/// </summary>
/// <remarks>
/// ダーティ時に警告水準（<see cref="QuickER.Gui.Abstractions.IDialogService.ConfirmWarning"/>）へ
/// 切り替わる分岐は <c>MainViewModelTests</c> の NewDiagram テストが担うため、
/// ここでは「保存済みクリーン状態なら通常確認（Question）のまま」の側と、
/// 「エンティティ数だけを見ると取りこぼす損失（名前付きクエリ・未保存変更）でも確認が出る」側を、
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

    // ---------------- 損失判定（HasNothingToLose） ----------------

    /// <summary>エンティティが無くてもクエリだけは残っている図の新規作成は、確認を出すことを検証する</summary>
    [Fact(DisplayName = "NewDiagram: クエリだけの図（クリーン）でも確認を出す")]
    public void NewDiagram_QueriesOnlyAndClean_Confirms()
    {
        var path = Path.Combine(_folder, "QueriesOnly.json");
        WriteDocument(path, entity: null, queryCount: 1);
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(dialogs, new SequencedFileDialogService(path));
        vm.OpenCommand.Execute(null);
        vm.Entities.Should().BeEmpty();
        vm.Queries.Should().ContainSingle();
        vm.IsDirty.Should().BeFalse();

        vm.NewDiagramCommand.Execute(null);

        // エンティティ数だけで判定すると、このクエリ定義が無確認で消える
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_ClearDiagram);
        vm.Queries.Should().ContainSingle("キャンセルすればクエリは残る");
    }

    /// <summary>内容は空でも未保存の変更がある状態の新規作成は、確認を出すことを検証する</summary>
    [Fact(DisplayName = "NewDiagram: 空でもダーティなら確認を出す")]
    public void NewDiagram_EmptyButDirty_Confirms()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(dialogs, new SequencedFileDialogService());

        // 追加してから取り消す＝内容は空だが未保存の変更がある状態（Undo で戻しても安全側でダーティ）
        vm.AddEntityCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        vm.Entities.Should().BeEmpty();
        vm.IsDirty.Should().BeTrue();

        vm.NewDiagramCommand.Execute(null);

        // ダーティなので警告水準（Warning）で確認する
        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_ClearDiagram);
    }

    /// <summary>完全に空でクリーンな図の新規作成は、従来どおり無確認で進むことを検証する</summary>
    [Fact(DisplayName = "NewDiagram: 空でクリーンなら確認を出さない")]
    public void NewDiagram_EmptyAndClean_DoesNotConfirm()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(dialogs, new SequencedFileDialogService());

        vm.NewDiagramCommand.Execute(null);

        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().BeEmpty();
    }

    /// <summary>
    /// 構造が同一の Mermaid 再取込でも、名前付きクエリがあれば確認を出し、
    /// 削除件数を確認メッセージへ付加することを検証する。
    /// </summary>
    [Fact(DisplayName = "Mermaid 再取込: 構造同一でもクエリがあれば件数付きで確認する")]
    public void ImportMermaid_SameStructureWithQueries_ConfirmsWithQueryCount()
    {
        var jsonPath = Path.Combine(_folder, "WithQuery.json");
        var mermaidPath = Path.Combine(_folder, "WithQuery.mmd");
        WriteDocument(jsonPath, entity: BuildCustomerEntity(), queryCount: 2);
        WriteMatchingMermaid(mermaidPath);

        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(dialogs, new SequencedFileDialogService(jsonPath, mermaidPath));
        vm.OpenCommand.Execute(null);
        vm.IsDirty.Should().BeFalse();
        vm.Queries.Should().HaveCount(2);

        vm.ImportDiagramCommand.Execute(null);

        // 構造署名は一致するが、Mermaid はクエリ定義を持たないため置換でクエリが全消えする
        dialogs
            .ConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(string.Format(Strings.Import_QueriesRemovedWarning, 2));
        vm.Queries.Should().HaveCount(2, "キャンセルすればクエリは残る");
    }

    /// <summary>構造が同一でクエリも未保存変更も無い Mermaid 再取込は、従来どおり無確認で進むことを検証する</summary>
    /// <remarks>直前のテストの対照。構造署名の一致判定そのものが効いていることを示す</remarks>
    [Fact(DisplayName = "Mermaid 再取込: 構造同一でクエリが無ければ確認しない")]
    public void ImportMermaid_SameStructureWithoutQueries_DoesNotConfirm()
    {
        var jsonPath = Path.Combine(_folder, "NoQuery.json");
        var mermaidPath = Path.Combine(_folder, "NoQuery.mmd");
        WriteDocument(jsonPath, entity: BuildCustomerEntity(), queryCount: 0);
        WriteMatchingMermaid(mermaidPath);

        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateViewModel(dialogs, new SequencedFileDialogService(jsonPath, mermaidPath));
        vm.OpenCommand.Execute(null);

        vm.ImportDiagramCommand.Execute(null);

        dialogs.ConfirmMessages.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().BeEmpty();
        vm.Entities.Should().ContainSingle(e => e.TableName == "Customer");
    }

    // ---------------- テスト補助 ----------------

    /// <summary>永続化先を一時フォルダへ隔離し、実 FileSystemWatcher を起動しない VM を生成する</summary>
    private MainViewModel CreateViewModel(StubDialogService dialogs, IFileDialogService files)
    {
        var vm = new MainViewModel(dialogs, files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        return vm;
    }

    /// <summary>署名比較用の固定エンティティ（Mermaid と往復しても構造署名が変わらない最小構成）</summary>
    private static Entity BuildCustomerEntity() =>
        new()
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

    /// <summary>指定のエンティティ（省略可）と件数分の名前付きクエリを持つ図ファイルを書き出す</summary>
    private static void WriteDocument(string path, Entity? entity, int queryCount)
    {
        var schema = new ErDiagram { TargetDbms = "sqlserver" };

        if (entity is not null)
        {
            schema.Entities.Add(entity);
        }

        for (var index = 0; index < queryCount; index++)
        {
            schema.Queries.Add(
                new QueryDefinition
                {
                    Name = $"Query{index}",
                    EntityId = entity?.Id ?? Guid.NewGuid(),
                }
            );
        }

        JsonStorageService.Save(path, new DiagramDocument { Schema = schema, Layout = null });
    }

    /// <summary><see cref="BuildCustomerEntity"/> と構造署名が一致する Mermaid ファイルを書き出す</summary>
    private static void WriteMatchingMermaid(string path) =>
        File.WriteAllText(
            path,
            "erDiagram"
                + Environment.NewLine
                + "    Customer {"
                + Environment.NewLine
                + "        int CustomerId PK"
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
        );

    /// <summary>PickOpenFile の戻り値を呼び出しごとに切り替えるテスト用ファイルダイアログ</summary>
    /// <remarks>
    /// 既存スタブは戻り値が <c>init</c> 固定で、1 つの VM に「JSON を開く → Mermaid を取り込む」を
    /// 続けて行わせられないため、この検証専用に用意する（設定より多く呼ばれたらキャンセル扱い）。
    /// </remarks>
    private sealed class SequencedFileDialogService(params string[] openPaths) : IFileDialogService
    {
        private int _index;

        public FileDialogResult? PickOpenFile(string filter) =>
            _index < openPaths.Length ? new FileDialogResult(openPaths[_index++], 1) : null;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        ) => null;

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }
}
