using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;

namespace ERDesigner.ViewModels;

/// <summary>
/// AI 更新差分プレビューのダイアログ ViewModel です。
/// </summary>
public partial class AiUpdatePreviewDialogViewModel : ObservableObject
{
    /// <summary>差分グループ一覧です。</summary>
    public ObservableCollection<AiUpdateDiffGroup> DiffGroups { get; } = new();

    private string _summaryMessage = string.Empty;

    private AiUpdateDiffItem? _selectedItem;

    /// <summary>差分概要メッセージです。</summary>
    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetProperty(ref _summaryMessage, value);
    }

    /// <summary>選択中の差分項目です。</summary>
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

    /// <summary>ダイアログを閉じるためのアクションです。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>詳細表示用の比較行一覧です。</summary>
    public IReadOnlyList<AiUpdateDiffDetailRow> SelectedDetails => SelectedItem?.Details ?? [];

    /// <summary>右ペイン見出しです。</summary>
    public string SelectedItemTitle => SelectedItem?.Title ?? "差分を選択してください。";

    /// <summary>適用ボタンを有効にするかどうかです。</summary>
    public bool CanApply => DiffGroups.Sum(group => group.Items.Count) > 0;

    /// <summary>新しい ViewModel を生成します。</summary>
    public AiUpdatePreviewDialogViewModel(AiUpdateDiffResult diff)
    {
        foreach (var group in diff.Groups)
        {
            DiffGroups.Add(group);
        }

        SummaryMessage = diff.HasChanges ? $"AI が {diff.TotalChanges} 件の変更を提案しました。" : "差分はありません。";
        SelectedItem = DiffGroups.SelectMany(group => group.Items).FirstOrDefault();
    }

    /// <summary>差分を適用します。</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        CloseAction?.Invoke(true);
    }

    /// <summary>差分の適用をキャンセルします。</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }
}
