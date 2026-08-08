using System;
using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Resources;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 出力形式で表現できず落ちる情報の告知（完了時の内訳表示）を検証するテストクラス。
/// 内訳は形式ごとにセッション中 1 回だけ見せる（Mermaid は NOT NULL 列がある限りほぼ必ず
/// 告知対象になるため、毎回出すと通知が形骸化する）。
/// </summary>
public class MainViewModelExportOmissionTests
{
    /// <summary>保存先を呼び出しごとに差し替えられるテスト専用のファイルダイアログ</summary>
    /// <remarks>1 つの VM（＝1 セッション）で複数形式へ書き出し、形式ごとの告知を検証するために使う</remarks>
    private sealed class MutableSaveFileDialogService : IFileDialogService
    {
        public FileDialogResult? SaveResult { get; set; }

        public FileDialogResult? PickOpenFile(string filter) => null;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        ) => SaveResult;

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    /// <summary>Mermaid 出力で落ちる情報の内訳が、同じ形式では初回だけ提示されることを検証する</summary>
    [Fact(DisplayName = "Mermaid 出力: 落ちる情報の内訳は初回だけ提示する")]
    public void ExportMermaid_ShowsOmissionDetailsOnlyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"omission-{Guid.NewGuid()}.mmd");

        try
        {
            var dialogs = new StubDialogService();
            var vm = new MainViewModel(
                dialogs,
                files: new StubFileDialogService { SaveResult = new FileDialogResult(path, 5) }
            );

            // 既定エンティティは NOT NULL の ID 列を持つため、NULL 許可の指定が落ちる
            vm.AddEntityCommand.Execute(null);

            vm.ExportDiagramCommand.Execute(null);
            vm.ExportDiagramCommand.Execute(null);

            // 初回は要約＋内訳の詳細ダイアログ、2 回目は完了文だけ
            dialogs
                .InformationDetailsMessages.Should()
                .ContainSingle()
                .Which.Details.Should()
                .Contain(Strings.ExportOmission_ColumnNullability);
            dialogs.InformationMessages.Should().ContainSingle();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>告知の記録が形式ごとに独立していること（Mermaid で告知済みでも DBML では出る）を検証する</summary>
    [Fact(DisplayName = "出力形式ごとに独立して 1 回ずつ内訳を提示する")]
    public void Export_TracksOmissionNoticePerFormat()
    {
        var mermaidPath = Path.Combine(Path.GetTempPath(), $"omission-{Guid.NewGuid()}.mmd");
        var dbmlPath = Path.Combine(Path.GetTempPath(), $"omission-{Guid.NewGuid()}.dbml");

        try
        {
            var dialogs = new StubDialogService();
            var files = new MutableSaveFileDialogService();
            var vm = new MainViewModel(dialogs, files: files);
            vm.AddEntityCommand.Execute(null);

            // DBML が表現できないのはメモと名前付きクエリだけ。メモを入れて告知対象にする
            vm.Entities[0].Memo = "打ち合わせメモ";

            files.SaveResult = new FileDialogResult(mermaidPath, 5);
            vm.ExportDiagramCommand.Execute(null);

            files.SaveResult = new FileDialogResult(dbmlPath, 7);
            vm.ExportDiagramCommand.Execute(null);

            // 形式ごとに 1 回ずつ＝2 件。DBML 側の内訳はメモだけ
            dialogs.InformationDetailsMessages.Should().HaveCount(2);
            dialogs
                .InformationDetailsMessages[1]
                .Details.Should()
                .Be(string.Format(Strings.ExportOmission_Line, Strings.ExportOmission_TableMemo));
            dialogs.InformationMessages.Should().BeEmpty();
        }
        finally
        {
            foreach (var path in new[] { mermaidPath, dbmlPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    /// <summary>落ちる情報が無い形式（スキーマのみ JSON）では従来どおり完了文だけを出すことを検証する</summary>
    [Fact(DisplayName = "落ちる情報が無ければ完了文だけを出す")]
    public void Export_WithoutOmissions_ShowsPlainCompletion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"omission-{Guid.NewGuid()}.json");

        try
        {
            var dialogs = new StubDialogService();
            var vm = new MainViewModel(
                dialogs,
                files: new StubFileDialogService { SaveResult = new FileDialogResult(path, 4) }
            );
            vm.AddEntityCommand.Execute(null);

            vm.ExportDiagramCommand.Execute(null);

            dialogs.InformationMessages.Should().ContainSingle();
            dialogs.InformationDetailsMessages.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
