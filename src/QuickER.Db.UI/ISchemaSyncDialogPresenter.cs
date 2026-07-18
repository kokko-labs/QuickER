using System.Windows;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Db.UI;

/// <summary>
/// スキーマ同期ダイアログの表示を抽象化するシーム（テスト容易性のためコマンドサービスから分離する）。
/// </summary>
/// <remarks>
/// コマンドサービスから <c>SchemaSyncDialog</c> ウィンドウへの直接依存を切り離すための切断面。
/// 単体テストでは呼び出しを記録するフェイクへ置換する。
/// </remarks>
public interface ISchemaSyncDialogPresenter
{
    /// <summary>スキーマ同期ダイアログを表示する（方言・接続設定・目標スキーマを渡す）</summary>
    void Show(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    );
}

/// <summary>WPF の <see cref="SchemaSyncDialog"/> を用いた <see cref="ISchemaSyncDialogPresenter"/> の既定実装</summary>
public sealed class SchemaSyncDialogPresenter : ISchemaSyncDialogPresenter
{
    /// <inheritdoc />
    public void Show(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    )
    {
        var viewModel = new SchemaSyncDialogViewModel(provider, settings, entities, relationships);
        var dialog = new SchemaSyncDialog(viewModel) { Owner = Application.Current?.MainWindow };

        dialog.ShowDialog();
    }
}
