using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Models;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// ダイアグラムを既存 DB と同期 (差分 ALTER 実行) するためのダイアログ ViewModel。
/// </summary>
public partial class SchemaSyncDialogViewModel : ObservableObject
{
    private readonly SqlConnectionSettings _settings;
    private readonly IReadOnlyList<Entity> _targetEntities;
    private readonly IReadOnlyList<Relationship> _targetRelationships;

    /// <summary>差分項目一覧 (UI のチェックボックスツリー用)。</summary>
    public ObservableCollection<SchemaDiffItem> DiffItems { get; } = new();

    /// <summary>生成された T-SQL プレビュー。</summary>
    [ObservableProperty] private string _scriptPreview = string.Empty;

    /// <summary>状態メッセージ。</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>差分計算 / 実行中か。</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>差分が計算済みか。</summary>
    [ObservableProperty] private bool _hasDiff;

    /// <summary>ダイアログを閉じるためのアクション (View が注入)。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>
    /// 新しい ViewModel を生成します。
    /// </summary>
    public SchemaSyncDialogViewModel(
        SqlConnectionSettings settings,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships)
    {
        _settings = settings;
        _targetEntities = targetEntities;
        _targetRelationships = targetRelationships;
    }

    /// <summary>差分を再計算します。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "DB スキーマを取得中...";
        try
        {
            var importer = new SqlServerSchemaImporter();
            var live = await importer.ImportAsync(_settings).ConfigureAwait(true);
            var diff = new SchemaDiffService().Compute(
                live.Entities, live.Relationships,
                _targetEntities, _targetRelationships);

            DiffItems.Clear();
            foreach (var item in diff.Items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SchemaDiffItem.IsSelected))
                        UpdatePreview();
                };
                DiffItems.Add(item);
            }
            HasDiff = DiffItems.Count > 0;
            UpdatePreview();
            StatusMessage = HasDiff
                ? $"{DiffItems.Count} 件の差分があります。"
                : "差分はありません。";
        }
        catch (Exception ex)
        {
            StatusMessage = "差分計算に失敗しました: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>選択を変更したときにプレビューを更新します (View からチェックボックス変更時に呼ばれる想定)。</summary>
    public void UpdatePreview()
    {
        ScriptPreview = SchemaSyncScriptBuilder.Build(DiffItems);
    }

    /// <summary>すべての差分を選択します (破壊的でないもののみ既定 ON にしたい場合は引数で制御)。</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in DiffItems) i.IsSelected = true;
        UpdatePreview();
    }

    /// <summary>すべての差分の選択を解除します。</summary>
    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var i in DiffItems) i.IsSelected = false;
        UpdatePreview();
    }

    /// <summary>選択中の差分を実行します。</summary>
    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptPreview))
        {
            StatusMessage = "実行するスクリプトがありません。";
            return;
        }

        var destructive = DiffItems.Any(i => i.IsSelected && i.IsDestructive);
        var msg = destructive
            ? $"破壊的な変更 (削除/型変更) を含むスクリプトを {_settings.Database} に実行します。よろしいですか？"
            : $"スクリプトを {_settings.Database} に実行します。よろしいですか？";
        var ans = System.Windows.MessageBox.Show(msg, "確認",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (ans != System.Windows.MessageBoxResult.OK) return;

        IsBusy = true;
        StatusMessage = "実行中...";
        try
        {
            var executor = new SchemaSyncExecutor();
            var result = await executor.ExecuteAsync(_settings, ScriptPreview).ConfigureAwait(true);
            if (result.Committed)
            {
                StatusMessage = $"成功: {result.Batches.Count} バッチを実行し COMMIT しました。";
                System.Windows.MessageBox.Show(StatusMessage, "完了",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                // 自動で再計算
                await RefreshAsync().ConfigureAwait(true);
            }
            else
            {
                StatusMessage = "失敗: " + result.Error;
                System.Windows.MessageBox.Show("実行に失敗したため ROLLBACK されました:\n" + result.Error,
                    "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "実行に失敗しました: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ダイアログを閉じます。</summary>
    [RelayCommand]
    private void Close() => CloseAction?.Invoke(true);
}
