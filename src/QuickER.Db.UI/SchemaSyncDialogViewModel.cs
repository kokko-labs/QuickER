using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;

namespace QuickER.Db.UI;

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

    /// <summary>差分から方言中立の実行計画を組み立てるプランナー</summary>
    private readonly SyncPlanner _planner = new();

    /// <summary>直近の取込で得た DB 現状のエンティティ（再構築計画の合成に用いる）</summary>
    private IReadOnlyList<Entity> _liveEntities = [];

    /// <summary>直近の取込で得た DB 現状のリレーション（既存 FK 集合の復元に用いる）</summary>
    private IReadOnlyList<Relationship> _liveRelationships = [];

    /// <summary>直近の取込で得た補助オブジェクト（再構築で温存するインデックス・トリガー・一意制約）</summary>
    private IReadOnlyList<SchemaAuxiliaryObject> _liveAuxiliaryObjects = [];

    /// <summary>直近の <see cref="UpdatePreview"/> で組み立てた実行計画（実行確認の文言分岐に用いる）</summary>
    private SyncPlan _currentPlan = new();

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

    /// <summary>実行確認メッセージへ表示する同期先の名前（サーバー系はデータベース名・SQLite はファイルパス）</summary>
    /// <remarks>
    /// <see cref="DbConnectionSettings"/> は方言横断の共用型のため、SQLite 接続でも
    /// <see cref="DbConnectionSettings.Database"/> に別方言で最後に使った値が残り得る
    /// （対象 DB を切り替えても前回のデータベース名が表示される不具合の原因）。
    /// 表示は方言が実際に使うフィールドから選ぶ（接続ダイアログの ShowFilePath と同じ判定）。
    /// </remarks>
    private string SyncTargetDisplayName =>
        _provider.Name == SqliteProvider.ProviderName ? _settings.FilePath : _settings.Database;

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

            // rebuild 方言（SQLite）の再構築計画は「DB 現状（live）＋選択差分のみ」を合成するため、
            // 取込結果を保持して以降の UpdatePreview へ SyncPlanContext として渡せるようにする
            _liveEntities = live.Entities;
            _liveRelationships = live.Relationships;
            _liveAuxiliaryObjects = live.AuxiliaryObjects;

            // 対象方言のケーパビリティを渡す（SQLite は説明差分を抑止し FK 制約名を比較から除外する）
            var diff = new SchemaDiffService().Compute(
                live.Entities,
                live.Relationships,
                _targetEntities,
                _targetRelationships,
                _provider.SyncCapabilities
            );

            // 列順変更の扱いは方言のケーパビリティで分岐する。
            // 対応方言（MySQL=Native / SQLite=Rebuild）では Compute が選択可能な ReorderColumns 項目を生成するため、
            // ここでは何もしない（重複案内を出さない）。非対応方言（None）でのみ、検知した列順差分を
            // 選択不可の案内項目として追加する。
            if (_provider.SyncCapabilities.ColumnReorder == ColumnReorderMode.None)
            {
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
            }

            DiffItems.Clear();

            foreach (var entry in diff.Items)
            {
                entry.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SchemaDiffItem.IsSelected))
                    {
                        UpdatePreview();
                    }
                };

                DiffItems.Add(entry);
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

    /// <summary>直近の取込結果から、計画組み立て用の live スキーマ入力を作る</summary>
    private SyncPlanContext BuildPlanContext() =>
        new()
        {
            LiveEntities = _liveEntities,
            LiveRelationships = _liveRelationships,
            AuxiliaryObjects = _liveAuxiliaryObjects,
        };

    /// <summary>選択中の差分から実行計画を組み立て、スクリプトプレビューを再生成する（選択変更時に呼ぶ）</summary>
    /// <remarks>
    /// rebuild 方言（SQLite）は再構築の合成に DB 現状（live）が必須で、Refresh 前に呼ばれると
    /// <see cref="SyncPlanner.BuildPlan"/> が <see cref="InvalidOperationException"/> を投げる。
    /// 取込結果を <see cref="SyncPlanContext"/> として常に渡すことで、未取込時も空の live を土台に
    /// 空プレビューへ落ち着かせる（DiffItems も Refresh 前は空のため実質何も生成しない）。
    /// </remarks>
    public void UpdatePreview()
    {
        _currentPlan = _planner.BuildPlan(
            DiffItems,
            _provider.SyncCapabilities,
            BuildPlanContext()
        );
        ScriptPreview = _provider.SyncScriptBuilder.Build(_currentPlan);
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

        // テーブルを作り直して全行データを移し替える再構築（CreateOnly=false）を含む場合は、
        // 対象テーブルを列挙した専用文言に切り替える。新規テーブル作成のみ（CreateOnly=true）は
        // データ移し替えを伴わないため対象外とする
        var rebuildTables = _currentPlan
            .Rebuilds.Where(r => !r.CreateOnly)
            .Select(r => r.TableName)
            .ToList();

        // 削除・型変更など破壊的変更を含む場合は、確認文言を切り替えて誤実行を防ぐ
        var destructive = DiffItems.Any(i => i.IsSelected && i.IsDestructive);

        // 優先順位: 再構築あり ＞ 破壊的変更あり ＞ 通常
        string msg;

        if (rebuildTables.Count > 0)
        {
            // 対象テーブルは 1 行 1 テーブルの箇条書きで列挙する（横並びだと多テーブル時に読みづらいため）
            var tableList = string.Join(Environment.NewLine, rebuildTables.Select(t => "  • " + t));
            msg = string.Format(
                Strings.SchemaSync_ExecuteConfirmRebuild,
                SyncTargetDisplayName,
                tableList
            );
        }
        else if (destructive)
        {
            msg = string.Format(
                Strings.SchemaSync_ExecuteConfirmDestructive,
                SyncTargetDisplayName
            );
        }
        else
        {
            msg = string.Format(Strings.SchemaSync_ExecuteConfirm, SyncTargetDisplayName);
        }

        // 計画側で検出した注意事項（FK 自動再作成の失敗リスク・複合 FK による再構築ブロック）を追記する
        msg += BuildPlanWarningSuffix();

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
                // ダイアログ内は簡潔な状態表示、モーダルは結果の要約（実行バッチ数）と役割を分ける。
                // 同じ文字列を 2 箇所へ出すと重複した通知に見えるため、文言を分けて重ねる意味を持たせる
                StatusMessage = Strings.SchemaSync_ExecuteSucceededStatus;
                _dialogs.ShowInformation(
                    string.Format(Strings.SchemaSync_ExecuteSucceeded, result.Batches.Count),
                    Strings.Common_Complete
                );
                // 適用後の最新状態を反映するため差分を再計算する
                await RefreshAsync().ConfigureAwait(true);

                // 差分が完全に無くなった（案内項目すら残らない＝図と DB が一致した）ときは、
                // 役目を終えたダイアログを自動で閉じる
                if (DiffItems.Count == 0)
                {
                    CloseAction?.Invoke(true);
                }
            }
            else
            {
                StatusMessage = string.Format(Strings.SchemaSync_ExecuteFailedStatus, result.Error);

                // 見出しは方言中立に留める。ロールバック済みか部分適用の可能性があるかは方言によって異なり、
                // その説明は各 Executor が result.Error に詰めている（見出しで断定すると自己矛盾表示になる）
                _dialogs.ShowError(
                    Strings.SchemaSync_ExecuteFailedMessage + "\n" + result.Error,
                    Strings.Common_Error
                );

                // 部分適用が起こりうる方言（MySQL / Oracle）では、失敗しても DB が変わっていることがある。
                // 古い差分を出し続けないよう、エラーを読み終えた（＝モーダルを閉じた）後に取り直す
                await RefreshAsync().ConfigureAwait(true);
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

    /// <summary>
    /// 実行計画の警告（<see cref="SyncPlan.Warnings"/>）を、実行確認へ追記する文言へ整形する。
    /// </summary>
    /// <remarks>
    /// 警告が無ければ空文字を返す（＝確認文言は従来どおり）。文言は UI 言語追従（resx）で、
    /// スクリプト内コメント（英語固定）とは別系統である点に注意。
    /// </remarks>
    private string BuildPlanWarningSuffix()
    {
        if (_currentPlan.Warnings.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // 主キー変更に巻き込まれて自動再作成される FK（参照先が候補キーでなくなると失敗しうる）
        var rebuiltForeignKeys = _currentPlan.Warnings.Count(w =>
            w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey
        );

        if (rebuiltForeignKeys > 0)
        {
            sb.Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .Append(
                    string.Format(
                        Strings.SchemaSync_ExecuteConfirmForeignKeyRebuildRisk,
                        rebuiltForeignKeys
                    )
                );
        }

        // 一意制約の削除で、その列を参照している外部キーが壊れうる
        var brokenForeignKeys = _currentPlan
            .Warnings.Where(w =>
                w.Kind == SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey
            )
            .Select(w =>
                string.IsNullOrEmpty(w.Detail) ? w.TableName : $"{w.TableName} / {w.Detail}"
            )
            .ToList();

        if (brokenForeignKeys.Count > 0)
        {
            var fkList = string.Join(
                Environment.NewLine,
                brokenForeignKeys.Select(t => "  • " + t)
            );
            sb.Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .Append(
                    string.Format(Strings.SchemaSync_ExecuteConfirmUniqueConstraintDropRisk, fkList)
                );
        }

        return sb.ToString();
    }

    /// <summary>ダイアログを閉じる</summary>
    [RelayCommand]
    private void Close() => CloseAction?.Invoke(true);
}
