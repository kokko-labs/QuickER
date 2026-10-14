using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;

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

    /// <summary>直近の取込で得たテーブルの CREATE 文全文（再構築で失われる列属性の検出に用いる）</summary>
    private IReadOnlyDictionary<string, string> _liveTableCreateSql =
        new Dictionary<string, string>();

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
    /// <remarks>
    /// 立っている間は再計算・実行・全選択・全解除を受け付けない。再計算中に実行すると古いスクリプトが流れ、
    /// 実行中に再計算すると実行中の DB を読み直すことになる。画面のボタンの無効化（CanExecute）に加えて、
    /// 各コマンドの先頭でも同じ条件で止める（直接 <c>ExecuteAsync</c> を呼ぶ経路は CanExecute を通らない）。
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool _isBusy;

    /// <summary>同期スクリプトを DB に対して実行している最中か（このあいだはダイアログを閉じられない）</summary>
    /// <remarks>
    /// 閉じると呼び出し側へ制御が戻り、DDL の実行がダイアログの外で走り続ける。止めるのは実行器を
    /// 待っている間だけで、実行後の再計算（DB を読むだけ）の最中は閉じてよい。
    /// × ボタンでの閉じ方はビュー（<see cref="SchemaSyncDialog"/>）がこのフラグを見て取り消す。
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isExecuting;

    /// <summary>全選択・全解除で項目の選択をまとめて変えている最中か（項目ごとのプレビュー再生成を抑える）</summary>
    /// <remarks>
    /// 項目ごとに計画とスクリプトを作り直すと、N 件の一括変更で N 回の全再生成＝O(N²) になる。
    /// 一括変更の間は抑え、最後に 1 回だけ作り直す。
    /// </remarks>
    private bool _bulkSelecting;

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

    /// <summary>差分計算・実行中でないか（再計算・実行・全選択・全解除を受け付けるか）</summary>
    private bool CanOperate() => !IsBusy;

    /// <summary>同期の実行中でないか（ダイアログを閉じてよいか）</summary>
    private bool CanClose() => !IsExecuting;

    /// <summary>DB スキーマを取り込み、目標スキーマとの差分を再計算する</summary>
    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RefreshCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>差分の再計算の本体（<see cref="IsBusy"/> を触らない＝実行の後の取り直しからも呼ぶ）</summary>
    /// <remarks>
    /// 失敗したときは前回の差分・スクリプト・計画・取込結果をすべて捨てる。残すと、回線断などで
    /// 取り直せなかったのに古いスクリプトを実行できてしまう（MySQL / Oracle の部分適用の後に取り直しが
    /// 失敗した場合、回線が戻ってから古いスクリプトを再実行することになる）。
    /// </remarks>
    private async Task RefreshCoreAsync()
    {
        StatusMessage = Strings.SchemaSync_Fetching;

        try
        {
            var connectionString = _provider.BuildConnectionString(_settings);
            var live = await _provider
                .SchemaImporter.ImportAsync(connectionString, _settings.CommandTimeoutSeconds)
                .ConfigureAwait(true);

            // rebuild 方言（SQLite）の再構築計画は「DB 現状（live）＋選択差分のみ」を合成するため、
            // 取込結果を保持して以降の UpdatePreview へ SyncPlanContext として渡せるようにする
            _liveEntities = live.Entities;
            _liveRelationships = live.Relationships;
            _liveAuxiliaryObjects = live.AuxiliaryObjects;
            _liveTableCreateSql = live.TableCreateSql;

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
                    if (e.PropertyName == nameof(SchemaDiffItem.IsSelected) && !_bulkSelecting)
                    {
                        UpdatePreview();
                    }
                };

                DiffItems.Add(entry);
            }

            HasDiff = DiffItems.Count > 0;

            // プレビュー生成が失敗したときは、その理由（ステータス行）を件数表示で上書きしない
            if (TryUpdatePreview())
            {
                StatusMessage = HasDiff
                    ? string.Format(Strings.SchemaSync_DiffCount, DiffItems.Count)
                    : Strings.SchemaSync_NoDiff;
            }
        }
        catch (Exception ex)
        {
            ClearDiffState();
            StatusMessage = string.Format(Strings.SchemaSync_DiffFailed, ex.Message);
        }
    }

    /// <summary>差分・スクリプト・計画・取込結果を空にする（取り直しに失敗したとき＝実行できない状態へ戻す）</summary>
    private void ClearDiffState()
    {
        DiffItems.Clear();
        HasDiff = false;
        _liveEntities = [];
        _liveRelationships = [];
        _liveAuxiliaryObjects = [];
        _liveTableCreateSql = new Dictionary<string, string>();
        _currentPlan = new SyncPlan();
        ScriptPreview = string.Empty;
    }

    /// <summary>直近の取込結果から、計画組み立て用の live スキーマ入力を作る</summary>
    private SyncPlanContext BuildPlanContext() =>
        new()
        {
            LiveEntities = _liveEntities,
            LiveRelationships = _liveRelationships,
            AuxiliaryObjects = _liveAuxiliaryObjects,
            TableCreateSql = _liveTableCreateSql,
        };

    /// <summary>選択中の差分から実行計画を組み立て、スクリプトプレビューを再生成する（選択変更時に呼ぶ）</summary>
    /// <remarks>
    /// rebuild 方言（SQLite）は再構築の合成に DB 現状（live）が必須で、Refresh 前に呼ばれると
    /// <see cref="SyncPlanner.BuildPlan"/> が <see cref="InvalidOperationException"/> を投げる。
    /// 取込結果を <see cref="SyncPlanContext"/> として常に渡すことで、未取込時も空の live を土台に
    /// 空プレビューへ落ち着かせる（DiffItems も Refresh 前は空のため実質何も生成しない）。
    /// </remarks>
    public void UpdatePreview() => TryUpdatePreview();

    /// <summary>計画とスクリプトプレビューを作り直し、成功したかを返す（失敗の理由はステータス行へ出す）</summary>
    private bool TryUpdatePreview()
    {
        // 計画組み立て・スクリプト生成は図の内容次第で失敗し得る（型表記・名前の入口検証など）。
        // このメソッドはチェックボックスの選択変更から直接呼ばれるため、例外を外へ出すと
        // WPF の未処理例外になりアプリごと落ちる。失敗は既存の流儀どおりステータス行で伝える。
        try
        {
            _currentPlan = _planner.BuildPlan(
                DiffItems,
                _provider.SyncCapabilities,
                BuildPlanContext()
            );
            ScriptPreview = _provider.SyncScriptBuilder.Build(_currentPlan);
            return true;
        }
        catch (Exception ex)
        {
            _currentPlan = new SyncPlan();
            ScriptPreview = string.Empty;
            StatusMessage = string.Format(Strings.SchemaSync_PreviewFailed, ex.Message);
            return false;
        }
    }

    /// <summary>選択可能なすべての差分を選択する</summary>
    [RelayCommand(CanExecute = nameof(CanOperate))]
    private void SelectAll() => SetAllSelectable(true);

    /// <summary>選択可能なすべての差分の選択を解除する</summary>
    [RelayCommand(CanExecute = nameof(CanOperate))]
    private void DeselectAll() => SetAllSelectable(false);

    /// <summary>選択可能なすべての差分の選択をまとめて変え、プレビューを 1 回だけ作り直す</summary>
    private void SetAllSelectable(bool selected)
    {
        if (IsBusy)
        {
            return;
        }

        _bulkSelecting = true;

        try
        {
            foreach (var i in DiffItems.Where(i => i.IsSelectable))
            {
                i.IsSelected = selected;
            }
        }
        finally
        {
            _bulkSelecting = false;
        }

        UpdatePreview();
    }

    /// <summary>選択中の差分スクリプトを DB に対し実行する（破壊的変更を含む場合は警告確認する）</summary>
    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task ExecuteAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ScriptPreview))
        {
            StatusMessage = Strings.SchemaSync_NoScript;
            return;
        }

        if (!ConfirmExecution())
        {
            return;
        }

        IsBusy = true;
        StatusMessage = Strings.SchemaSync_Executing;

        try
        {
            SchemaSyncResult? result = null;
            Exception? failure = null;

            // 閉じられないのは実行器を待っている間だけ（後の取り直しは DB を読むだけなので閉じてよい。
            // 成功して差分が無くなったときの自動クローズもこの外で行う）。例外は捕まえて外で扱う＝
            // 失敗のモーダルと取り直しを、閉じられない状態のまま出さない
            IsExecuting = true;

            try
            {
                result = await _provider
                    .SyncExecutor.ExecuteAsync(_settings, ScriptPreview)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                IsExecuting = false;
            }

            if (failure is not null)
            {
                // 実行器が例外で抜けるのは接続断などの途中でも起き、MySQL / Oracle では部分適用の後かもしれない。
                // Committed=false の失敗と同じく、モーダルで知らせてから取り直す（取り直しも失敗すれば
                // 差分とスクリプトが空になり、古いスクリプトを再実行できなくなる）
                StatusMessage = string.Format(Strings.SchemaSync_ExecuteError, failure.Message);
                _dialogs.ShowError(
                    Strings.SchemaSync_ExecuteFailedMessage + "\n" + failure.Message,
                    Strings.Common_Error
                );
                await RefreshCoreAsync().ConfigureAwait(true);
                return;
            }

            if (result!.Committed)
            {
                // ダイアログ内は簡潔な状態表示、モーダルは結果の要約（実行バッチ数）と役割を分ける。
                // 同じ文字列を 2 箇所へ出すと重複した通知に見えるため、文言を分けて重ねる意味を持たせる
                StatusMessage = Strings.SchemaSync_ExecuteSucceededStatus;
                _dialogs.ShowInformation(
                    string.Format(Strings.SchemaSync_ExecuteSucceeded, result.Batches.Count),
                    Strings.Common_Complete
                );
                // 適用後の最新状態を反映するため差分を再計算する
                await RefreshCoreAsync().ConfigureAwait(true);

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
                await RefreshCoreAsync().ConfigureAwait(true);
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

    /// <summary>実行前の確認を出し、続行してよいかを返す</summary>
    /// <remarks>
    /// <para>
    /// 本文は要約（再構築あり ＞ 破壊的変更あり ＞ 通常 の優先順で文言を選ぶ）。再構築するテーブルの一覧と
    /// 計画の警告は判断材料なので、あるときは <see cref="IDialogService.ConfirmWarningDetails"/> の
    /// スクロールできる詳細領域へ畳まずに全件載せる。標準 MessageBox へ連結すると、件数が多いときに
    /// OK / キャンセルが画面の外へ出る（もっとも破壊的な確認でそれが起きる）。
    /// 一覧が無い確認は従来どおり単文の <see cref="IDialogService.ConfirmWarning"/>。
    /// </para>
    /// <para>
    /// 再構築（<c>CreateOnly=false</c>）はテーブルを作り直して全行データを移し替える。新規テーブル作成のみ
    /// （<c>CreateOnly=true</c>）はデータの移し替えを伴わないため対象外。
    /// </para>
    /// </remarks>
    private bool ConfirmExecution()
    {
        var rebuildTables = _currentPlan
            .Rebuilds.Where(r => !r.CreateOnly)
            .Select(r => r.TableName)
            .ToList();

        // 削除・型変更など破壊的変更を含む場合は、確認文言を切り替えて誤実行を防ぐ
        var destructive = DiffItems.Any(i => i.IsSelected && i.IsDestructive);

        string summary;

        if (rebuildTables.Count > 0)
        {
            summary = string.Format(
                Strings.SchemaSync_ExecuteConfirmRebuild,
                SyncTargetDisplayName,
                rebuildTables.Count
            );
        }
        else if (destructive)
        {
            summary = string.Format(
                Strings.SchemaSync_ExecuteConfirmDestructive,
                SyncTargetDisplayName
            );
        }
        else
        {
            summary = string.Format(Strings.SchemaSync_ExecuteConfirm, SyncTargetDisplayName);
        }

        var details = BuildConfirmDetails(rebuildTables);

        return details.Length == 0
            ? _dialogs.ConfirmWarning(summary, Strings.Common_Confirm)
            : _dialogs.ConfirmWarningDetails(summary, details, Strings.Common_Confirm);
    }

    /// <summary>
    /// 実行確認の詳細領域へ載せる判断材料（再構築するテーブルの一覧と、計画の警告）を整形する。
    /// </summary>
    /// <remarks>
    /// どちらも無ければ空文字を返す（＝単文の確認にする）。文言は UI 言語追従（resx）で、
    /// スクリプト内コメント（英語固定）とは別系統である点に注意。
    /// </remarks>
    private string BuildConfirmDetails(IReadOnlyList<string> rebuildTables)
    {
        var sections = new List<string>();

        if (rebuildTables.Count > 0)
        {
            // 対象テーブルは 1 行 1 テーブルの箇条書きで列挙する（横並びだと多テーブル時に読みづらいため）
            sections.Add(
                Strings.SchemaSync_ExecuteConfirmRebuildTablesHeader
                    + Environment.NewLine
                    + BulletList(rebuildTables)
            );
        }

        // 種別を宣言順に回して網羅 switch で文言化する。個別に拾う形だと、将来 SyncPlanWarningKind へ
        // 足した種別が黙って落ちる（取込警告の文言化と同じ規則＝未知の種別は例外にする）
        foreach (var kind in Enum.GetValues<SyncPlanWarningKind>())
        {
            var warnings = _currentPlan.Warnings.Where(w => w.Kind == kind).ToList();

            if (warnings.Count > 0)
            {
                sections.Add(Describe(kind, warnings));
            }
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    /// <summary>1 種別ぶんの警告を、実行確認へ追記する文言へ整形する</summary>
    /// <remarks>
    /// 未知の種別は <see cref="ArgumentOutOfRangeException"/>（取込警告の文言化と同じ流儀）。
    /// 黙って落とすと、新しい種別の警告が実行確認に出ないまま危険な同期が通る。
    /// </remarks>
    private static string Describe(
        SyncPlanWarningKind kind,
        IReadOnlyList<SyncPlanWarning> warnings
    ) =>
        kind switch
        {
            // 主キー変更に巻き込まれて自動再作成される FK（参照先が候補キーでなくなると失敗しうる）
            SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey => string.Format(
                Strings.SchemaSync_ExecuteConfirmForeignKeyRebuildRisk,
                warnings.Count
            ),
            // 一意制約の削除で、その列を参照している外部キーが壊れうる
            SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey => string.Format(
                Strings.SchemaSync_ExecuteConfirmUniqueConstraintDropRisk,
                BulletList(
                    warnings.Select(w =>
                        string.IsNullOrEmpty(w.Detail) ? w.TableName : $"{w.TableName} / {w.Detail}"
                    )
                )
            ),
            // テーブル再構築で、意味モデルに無い列レベル属性（既定値・CHECK 等）が再現されず失われる
            SyncPlanWarningKind.TableRebuildDropsColumnAttribute => string.Format(
                Strings.SchemaSync_ExecuteConfirmRebuildDropsAttributes,
                BulletList(warnings.Select(w => $"{w.TableName} / {w.Detail}"))
            ),
            // 列の変更で両端の型が食い違う外部キーは外し直さない（＝列の変更が失敗する）
            SyncPlanWarningKind.ForeignKeyColumnTypeMismatch => string.Format(
                Strings.SchemaSync_ExecuteConfirmForeignKeyTypeMismatch,
                BulletList(warnings.Select(w => $"{w.TableName} / {w.Detail}"))
            ),
            // 選んでいない表・列の追加・主キーの変更を前提にする項目は計画から外した
            SyncPlanWarningKind.SelectedItemRequiresUnselectedItem => string.Format(
                Strings.SchemaSync_ExecuteConfirmSelectedItemRequiresUnselected,
                BulletList(warnings.Select(w => $"{w.TableName} / {w.Detail}"))
            ),
            // 選んだ外部キーの追加・作り直しの参照先が、同期後に候補キーでない
            SyncPlanWarningKind.ForeignKeyNotOnCandidateKey => string.Format(
                Strings.SchemaSync_ExecuteConfirmForeignKeyNotOnCandidateKey,
                BulletList(warnings.Select(w => $"{w.TableName} / {w.Detail}"))
            ),
            // 参照される複合キーと外部キーの列の並びだけが合わなくなる変更を外した（MySQL）
            SyncPlanWarningKind.ForeignKeyKeyOrderMismatch => string.Format(
                Strings.SchemaSync_ExecuteConfirmForeignKeyKeyOrderMismatch,
                BulletList(warnings.Select(w => $"{w.TableName} / {w.Detail}"))
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>項目を「  • 」付きの 1 行ずつへ並べる</summary>
    private static string BulletList(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(item => "  • " + item));

    /// <summary>ダイアログを閉じる</summary>
    /// <remarks>同期の実行中は閉じない（<see cref="IsExecuting"/>。Esc もこのコマンドのボタンを経由する）</remarks>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (IsExecuting)
        {
            return;
        }

        CloseAction?.Invoke(true);
    }
}
