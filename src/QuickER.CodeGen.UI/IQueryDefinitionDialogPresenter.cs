using System.Collections.Generic;
using System.Windows;
using QuickER.Model;

namespace QuickER.CodeGen.UI;

/// <summary>
/// 名前付きクエリ定義エディタの表示を抽象化するシーム（テスト容易性のためコマンドサービスから分離する）。
/// </summary>
/// <remarks>
/// ViewModel から <c>Views.*</c> ウィンドウへの直接依存を切り離すための切断面。
/// 単体テストでは戻り値を差し替えたフェイクへ置換する。
/// </remarks>
public interface IQueryDefinitionDialogPresenter
{
    /// <summary>名前付きクエリ定義エディタを表示し、確定した定義リストを返す（キャンセル時は null）</summary>
    /// <param name="diagram">エンティティと既存クエリを含む現在の ER 図（この参照は変更しない）</param>
    List<QueryDefinition>? Show(ErDiagram diagram);
}

/// <summary>WPF の <see cref="QueryDefinitionDialog"/> を用いた <see cref="IQueryDefinitionDialogPresenter"/> の既定実装</summary>
public sealed class QueryDefinitionDialogPresenter : IQueryDefinitionDialogPresenter
{
    /// <inheritdoc />
    public List<QueryDefinition>? Show(ErDiagram diagram)
    {
        var viewModel = new QueryDefinitionDialogViewModel(diagram);
        var dialog = new QueryDefinitionDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }
}
