using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.AI.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>ダイアグラムを既存 DB と同期（差分 ALTER 実行）するためのダイアログ ViewModel</summary>
public partial class SchemaSyncDialogViewModel : ObservableObject
{
    /// <summary>同期先の DB プロバイダ（取込・スクリプト生成・実行を担う）</summary>
    private readonly IDatabaseProvider _provider;

    /// <summary>同期先 DB の接続設定</summary>
    private readonly DbConnectionSettings _settings;

    /// <summary>同期の目標とするエンティティ（ダイアグラム側の状態）</summary>
    private readonly IReadOnlyList<Entity> _targetEntities;

    /// <summary>同期の目標とするリレーション（ダイアグラム側の状態）</summary>
    private readonly IReadOnlyList<Relationship> _targetRelationships;

    /// <summary>確認・通知ダイアログの表示先（テストではスタブへ差し替える）</summary>
    private readonly IDialogService _dialogs;

    /// <summary>差分項目一覧（UI のチェックボックスツリー用）</summary>
    public ObservableCollection<SchemaDiffItem> DiffItems { get; } = new();

    /// <summary>選択中の差分から生成した T-SQL プレビュー</summary>
    [ObservableProperty]
    private string _scriptPreview = string.Empty;

    /// <summary>状態メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>差分計算・実行中かどうか</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>差分が計算済みかどうか</summary>
    [ObservableProperty]
    private bool _hasDiff;

    /// <summary>ダイアログを閉じる際に呼ぶアクション（View が注入する）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>同期先プロバイダ・設定と目標スキーマを指定して ViewModel を生成する</summary>
    /// <param name="provider">同期先の DB プロバイダ</param>
    /// <param name="settings">同期先 DB の接続設定</param>
    /// <param name="targetEntities">同期の目標エンティティ</param>
    /// <param name="targetRelationships">同期の目標リレーション</param>
    /// <param name="dialogService">確認・通知ダイアログの表示先（省略時は MessageBox、テストではスタブを注入）</param>
    public SchemaSyncDialogViewModel(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        IDialogService? dialogService = null
    )
    {
        _provider = provider;
        _settings = settings;
        _targetEntities = targetEntities;
        _targetRelationships = targetRelationships;
        _dialogs = dialogService ?? new MessageBoxDialogService();
    }

    /// <summary>DB スキーマを取り込み、目標スキーマとの差分を再計算する</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = Strings.SchemaSync_Fetching;

        try
        {
            var connectionString = _provider.BuildConnectionString(_settings);
            var live = await _provider
                .SchemaImporter.ImportAsync(connectionString)
                .ConfigureAwait(true);
            var diff = new SchemaDiffService().Compute(
                live.Entities,
                live.Relationships,
                _targetEntities,
                _targetRelationships
            );

            // 列順差分は DB 同期対象外のため、検知時は選択不可の案内項目のみ追加する
            var orderChangedTables = SchemaDiffService.DetectColumnOrderChanges(
                live.Entities,
                _targetEntities
            );

            foreach (var tableName in orderChangedTables)
            {
                diff.Items.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.RebuildTable,
                        TableName = tableName,
                        Description = string.Format(
                            Strings.SchemaSync_ColumnOrderNotSynced,
                            tableName
                        ),
                        IsSelected = false,
                        IsSelectable = false,
                    }
                );
            }

            DiffItems.Clear();

            foreach (var item in diff.Items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SchemaDiffItem.IsSelected))
                    {
                        UpdatePreview();
                    }
                };

                DiffItems.Add(item);
            }

            HasDiff = DiffItems.Count > 0;
            UpdatePreview();
            StatusMessage = HasDiff
                ? string.Format(Strings.SchemaSync_DiffCount, DiffItems.Count)
                : Strings.SchemaSync_NoDiff;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.SchemaSync_DiffFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>選択中の差分から T-SQL プレビューを再生成する（選択変更時に呼ぶ）</summary>
    public void UpdatePreview()
    {
        ScriptPreview = _provider.SyncScriptBuilder.Build(DiffItems);
    }

    /// <summary>選択可能なすべての差分を選択する</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in DiffItems.Where(i => i.IsSelectable))
        {
            i.IsSelected = true;
        }

        UpdatePreview();
    }

    /// <summary>選択可能なすべての差分の選択を解除する</summary>
    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var i in DiffItems.Where(i => i.IsSelectable))
        {
            i.IsSelected = false;
        }

        UpdatePreview();
    }

    /// <summary>選択中の差分スクリプトを DB に対し実行する（破壊的変更を含む場合は警告確認する）</summary>
    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(ScriptPreview))
        {
            StatusMessage = Strings.SchemaSync_NoScript;
            return;
        }

        // 削除・型変更など破壊的変更を含む場合は、確認文言を切り替えて誤実行を防ぐ
        var destructive = DiffItems.Any(i => i.IsSelected && i.IsDestructive);
        var msg = destructive
            ? string.Format(Strings.SchemaSync_ExecuteConfirmDestructive, _settings.Database)
            : string.Format(Strings.SchemaSync_ExecuteConfirm, _settings.Database);
        if (!_dialogs.ConfirmWarning(msg, Strings.Common_Confirm))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = Strings.SchemaSync_Executing;

        try
        {
            var result = await _provider
                .SyncExecutor.ExecuteAsync(_settings, ScriptPreview)
                .ConfigureAwait(true);

            if (result.Committed)
            {
                StatusMessage = string.Format(
                    Strings.SchemaSync_ExecuteSucceeded,
                    result.Batches.Count
                );
                _dialogs.ShowInformation(StatusMessage, Strings.Common_Complete);
                // 適用後の最新状態を反映するため差分を再計算する
                await RefreshAsync().ConfigureAwait(true);
            }
            else
            {
                StatusMessage = string.Format(Strings.SchemaSync_ExecuteFailedStatus, result.Error);
                _dialogs.ShowError(
                    Strings.SchemaSync_RollbackMessage + "\n" + result.Error,
                    Strings.Common_Error
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.SchemaSync_ExecuteError, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ダイアログを閉じる</summary>
    [RelayCommand]
    private void Close() => CloseAction?.Invoke(true);
}
