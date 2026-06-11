using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>AI 更新差分プレビューダイアログの ViewModel</summary>
public partial class AiUpdatePreviewDialogViewModel : ObservableObject
{
    /// <summary>カテゴリ別にまとめた差分グループ一覧</summary>
    public ObservableCollection<AiUpdateDiffGroup> DiffGroups { get; } = new();

    /// <summary>差分概要メッセージの実体フィールド</summary>
    private string _summaryMessage = string.Empty;

    /// <summary>選択中の差分項目の実体フィールド</summary>
    private AiUpdateDiffItem? _selectedItem;

    /// <summary>差分概要メッセージ</summary>
    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetProperty(ref _summaryMessage, value);
    }

    /// <summary>選択中の差分項目（変更時に詳細・見出しの再評価を促す）</summary>
    public AiUpdateDiffItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(SelectedDetails));
                OnPropertyChanged(nameof(SelectedItemTitle));
            }
        }
    }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（引数は適用可否）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>選択項目の Before / After 比較行一覧</summary>
    public IReadOnlyList<AiUpdateDiffDetailRow> SelectedDetails => SelectedItem?.Details ?? [];

    /// <summary>右ペインの見出し（未選択時はプレースホルダー）</summary>
    public string SelectedItemTitle => SelectedItem?.Title ?? "差分を選択してください。";

    /// <summary>適用ボタンを有効にするかどうか（差分が 1 件以上ある場合）</summary>
    public bool CanApply => DiffGroups.Sum(group => group.Items.Count) > 0;

    /// <summary>差分計算結果から ViewModel を生成し、先頭の差分を初期選択する</summary>
    public AiUpdatePreviewDialogViewModel(AiUpdateDiffResult diff)
    {
        foreach (var group in diff.Groups)
        {
            DiffGroups.Add(group);
        }

        SummaryMessage = diff.HasChanges ? $"AI が {diff.TotalChanges} 件の変更を提案しました。" : "差分はありません。";
        SelectedItem = DiffGroups.SelectMany(group => group.Items).FirstOrDefault();
    }

    /// <summary>差分を適用してダイアログを閉じる</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        CloseAction?.Invoke(true);
    }

    /// <summary>適用せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }
}
