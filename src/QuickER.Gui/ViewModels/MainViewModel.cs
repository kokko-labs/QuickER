using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Documents;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.UndoRedo;

namespace QuickER.ViewModels;

/// <summary>
/// アプリケーション全体の状態を統括する中心 ViewModel（ダイアグラム編集責務を担う主 partial）
/// エンティティ・リレーションの編集、選択管理、Undo/Redo 統合、キャンバスサイズ管理を担当する
/// </summary>
/// <remarks>
/// Undo/Redo は 2 系統で履歴を構成する
/// <list type="bullet">
/// <item>追加・削除・整列などの明示操作は <see cref="UndoRedoManager.Execute"/> にコマンドとして登録する</item>
/// <item>個々のプロパティ変更は <see cref="DiagramChangeTracker"/> が PropertyChanged を監視して自動記録する</item>
/// </list>
/// Undo/Redo の再生やダイアグラム一括置換などプログラム起点の変更は
/// <see cref="DiagramChangeTracker.RunWithoutTracking"/> で追跡を抑止し、履歴の二重登録を防ぐ
/// 入出力（保存・取込・各種生成）の責務は <c>MainViewModel.ImportExport.cs</c> 側の partial に分離する
/// </remarks>
public partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Undo/Redo 履歴を管理するスタック</summary>
    public UndoRedoManager UndoRedo { get; } = new();

    /// <summary>ツールバーの言語切替ボタン用の子 ViewModel（表示言語の選択・保存）</summary>
    public LanguageSwitchViewModel LanguageSwitch { get; }

    /// <summary>フィーチャーモジュールがツールバーへ寄与するボタン群（起動時にホストが設定する）</summary>
    [ObservableProperty]
    private IReadOnlyList<FeatureToolbarItem> _featureToolbarItems = [];

    /// <summary>ツールバーの折返し単位となる、グループ区切り（BeginsGroup）ごとのボタン群</summary>
    /// <remarks>
    /// WrapPanel 上で「DB 系」「AI 系」「コード生成系」のくくりを崩さず折り返すため、
    /// <see cref="FeatureToolbarItems"/> を BeginsGroup=true の直前で分割した形で公開する
    /// （View はグループごとに 1 つの ItemsControl を並べる＝1 グループが 1 つの折返し単位になる）。
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<FeatureToolbarItem>> FeatureToolbarItemGroups
    {
        get;
        private set;
    } = [];

    /// <summary>ボタン群の設定時に、折返し単位のグループ分割を再計算する</summary>
    partial void OnFeatureToolbarItemsChanged(IReadOnlyList<FeatureToolbarItem> value)
    {
        FeatureToolbarItemGroups = SplitToolbarGroups(value);
        OnPropertyChanged(nameof(FeatureToolbarItemGroups));
    }

    /// <summary>ボタン列を BeginsGroup=true の直前で分割し、グループ区切り単位の入れ子リストへ変換する</summary>
    /// <remarks>先頭要素は BeginsGroup に依らず最初のグループを開始する（全体先頭は App が false へ矯正済み）</remarks>
    internal static IReadOnlyList<IReadOnlyList<FeatureToolbarItem>> SplitToolbarGroups(
        IReadOnlyList<FeatureToolbarItem> items
    )
    {
        var groups = new List<IReadOnlyList<FeatureToolbarItem>>();
        var currentGroup = new List<FeatureToolbarItem>();

        foreach (var item in items)
        {
            if (item.BeginsGroup && currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
                currentGroup = new List<FeatureToolbarItem>();
            }

            currentGroup.Add(item);
        }

        if (currentGroup.Count > 0)
        {
            groups.Add(currentGroup);
        }

        return groups;
    }

    /// <summary>カラム名がユーザー編集で変更されたときに発火する（フィーチャーモジュールの条件式追従などに使用）</summary>
    /// <remarks>
    /// <see cref="OnColumnRenamed"/> から、列を保持するエンティティを解決できたときにのみ発火する。
    /// Undo/Redo 再生中（追跡停止中）や一括置換では発火しない（<see cref="DiagramChangeTracker"/> がその経路を通らない）。
    /// </remarks>
    public event EventHandler<ColumnRenamedEventArgs>? ColumnRenamed;

    /// <summary>対象 DBMS が切り替わったときに発火する（フィーチャーモジュールのツールバー活性・ツールチップ再評価などに使用）</summary>
    public event EventHandler? TargetDbmsChanged;

    /// <summary>選択中エンティティを内部バッファへコピーするコマンド</summary>
    /// <remarks>ペースト側の実行可否が非バインド対象の内部バッファに依存するため、生成属性を使わず手動で構築する</remarks>
    public IRelayCommand CopySelectedEntityCommand { get; }

    /// <summary>内部バッファのエンティティを複製して追加するコマンド</summary>
    public IRelayCommand PasteCopiedEntityCommand { get; }

    /// <summary>ダイアグラム上の全エンティティ</summary>
    public ObservableCollection<EntityViewModel> Entities { get; } = new();

    /// <summary>ダイアグラム上の全リレーション</summary>
    public ObservableCollection<RelationshipViewModel> Relationships { get; } = new();

    /// <summary>現在の図に付随する名前付きクエリ定義（意味モデル。キャンバスには表示しない）</summary>
    /// <remarks>
    /// エンティティ・列を Guid 参照する生モデルのまま保持する（座標などの視覚情報を持たないため
    /// ViewModel 化・ObservableCollection 化は不要）。保存文書（<see cref="ToDocument"/>）へ含め、
    /// ファイル由来の図に置き換えるときだけ復元する。ファイル由来でない置換（新規・取込・DB 取込・
    /// AI 生成）では古い図の Guid 参照が新しい図と噛み合わないため空にする（<see cref="ReplaceDiagram"/>）。
    /// エンティティ削除時の孤児クエリは v1 では削除しない（生成時に警告が出る設計のため、そこで気づける）。
    /// </remarks>
    public List<QueryDefinition> Queries { get; private set; } = new();

    /// <summary>主選択エンティティ（未選択時は null）</summary>
    /// <remarks>
    /// 選択の正はエンティティごとの <see cref="EntityViewModel.IsSelected"/> フラグであり、
    /// <see cref="SelectedEntity"/> はそのうち「最後に操作した 1 個」＝主選択を表す。
    /// プロパティパネル・コピー・複製・関連ハイライトなど「単一対象を前提とする機能」はこの主選択に作用する。
    /// 複数選択の集合は派生ヘルパ <see cref="SelectedEntities"/> で取得する。
    /// </remarks>
    [ObservableProperty]
    private EntityViewModel? _selectedEntity;

    /// <summary>選択中のリレーション（未選択時は null）</summary>
    [ObservableProperty]
    private RelationshipViewModel? _selectedRelationship;

    /// <summary>リレーション作成モードで 1 つ目にクリックされ、接続相手を待っているエンティティ</summary>
    [ObservableProperty]
    private EntityViewModel? _pendingRelationshipSource;

    /// <summary>リレーション作成モードで追加するリレーションの種別</summary>
    [ObservableProperty]
    private RelationshipType _pendingRelationshipType;

    /// <summary>リレーション作成モード中かどうかを示す</summary>
    [ObservableProperty]
    private bool _isRelationshipMode;

    /// <summary>プロパティパネルで選択中のカラム（DataGrid の SelectedItem）</summary>
    [ObservableProperty]
    private ColumnViewModel? _selectedColumn;

    /// <summary>カラムコピーの内部バッファ（クリップボードは使用しない）</summary>
    private Column? _copiedColumn;

    /// <summary>エンティティコピーの内部バッファ（クリップボードは使用しない）</summary>
    private Entity? _copiedEntity;
    private EntityLayout? _copiedEntityLayout;

    /// <summary>同一コピー元からのペースト回数（貼り付け位置を段階的にずらすために使用）</summary>
    private int _copiedEntityPasteCount;

    /// <summary>ER 図上のカラム行に「説明」を表示するかどうか（ツールバーから切替）</summary>
    [ObservableProperty]
    private bool _showColumnDescriptionsInDiagram;

    /// <summary>プロパティパネルの UNIQUE 制約カードを開いているかどうか</summary>
    /// <remarks>
    /// 既定はエンティティ選択のたびに「制約が定義されていれば開く・なければ畳む」で決まり
    /// （<see cref="OnSelectedEntityChanged"/>）、ヘッダーのトグルで手動開閉できる。
    /// 制約の追加操作（<see cref="AddUniqueConstraintCommand"/>）でも開く
    /// </remarks>
    [ObservableProperty]
    private bool _isUniqueConstraintCardExpanded;

    /// <summary><see cref="ShowNullabilityInDiagram"/> のバッキングフィールド（既定は表示）</summary>
    private bool _showNullabilityInDiagram = true;

    /// <summary><see cref="IsCompactViewInDiagram"/> のバッキングフィールド（既定は簡易表示なし）</summary>
    private bool _isCompactViewInDiagram;

    /// <summary>エンティティ最右端・最下端の外側に確保する余白（エンティティを外側へ広げるためのドラッグ用スペース、論理px）</summary>
    private const double CanvasContentMargin = 100;

    /// <summary>キャンバスの動的幅（ビューポート論理幅と「エンティティ最右端 + 余白」の大きい方）</summary>
    /// <remarks>図がビューポートに収まる間はビューポートと同寸に保ち、不要な横スクロールバーを出さない</remarks>
    public double CanvasWidth =>
        Math.Max(
            CanvasMinimumSize.Width,
            Entities.Count == 0 ? 0 : Entities.Max(e => e.X + e.Width) + CanvasContentMargin
        );

    /// <summary>キャンバスの動的高さ（ビューポート論理高さと「エンティティ最下端 + 余白」の大きい方）</summary>
    /// <remarks>図がビューポートに収まる間はビューポートと同寸に保ち、不要な縦スクロールバーを出さない</remarks>
    public double CanvasHeight =>
        Math.Max(
            CanvasMinimumSize.Height,
            Entities.Count == 0 ? 0 : Entities.Max(e => e.Y + e.DisplayHeight) + CanvasContentMargin
        );

    /// <summary>型 ComboBox に表示する、現在方言のデータ型一覧</summary>
    public IReadOnlyList<string> AvailableDataTypes => CurrentProvider.TypeCatalog.DataTypes;

    /// <summary>エンティティ見出しの背景色プリセット一覧</summary>
    public IReadOnlyList<EntityTitleColorOption> EntityTitleColorOptions =>
        EntityTitleColorPalette.Options;

    /// <summary>確認・通知ダイアログの表示先（テストではスタブに差し替える）</summary>
    private readonly IDialogService _dialogs;

    /// <summary>アプリ固有モーダルダイアログ（印刷オプション）の表示先</summary>
    private readonly IAppDialogService _appDialogs;

    /// <summary>ファイル選択ダイアログの表示先</summary>
    private readonly IFileDialogService _files;

    /// <summary>登録済み DB プロバイダのレジストリ（現在方言の解決に用いる）</summary>
    private readonly DatabaseProviderRegistry _providers;

    /// <summary>プロパティ変更を監視して Undo/Redo 履歴へ自動登録する追跡器</summary>
    private readonly DiagramChangeTracker _changeTracker;

    /// <summary>現在の図のターゲット DBMS（プロバイダ識別名。バッキングフィールド）</summary>
    private IDatabaseProvider _currentProvider;

    /// <summary>未知の TargetDbms を SQL Server へフォールバックした旨を既に警告したか（多重表示防止）</summary>
    private bool _fallbackWarningShown;

    /// <summary>現在の図のターゲット DBMS プロバイダ</summary>
    public IDatabaseProvider CurrentProvider => _currentProvider;

    /// <summary>登録済み DB プロバイダのレジストリ（モック生成の型解決など、方言別マッパ解決に用いる）</summary>
    public DatabaseProviderRegistry Providers => _providers;

    /// <summary>DBMS 切替 ComboBox の選択肢（登録済み全プロバイダ）</summary>
    public IReadOnlyList<IDatabaseProvider> AvailableProviders => _providers.All.ToList();

    /// <summary>DBMS 切替 ComboBox の選択項目（現在方言）。設定時に方言切替を実行する</summary>
    public IDatabaseProvider SelectedProvider
    {
        get => _currentProvider;
        set
        {
            if (value is not null)
            {
                ChangeTargetDbms(value);
            }
        }
    }

    /// <summary>SQL Server のみを登録した既定のプロバイダレジストリを生成する（テスト・既定合成点用）</summary>
    private static DatabaseProviderRegistry CreateDefaultRegistry() =>
        new(new IDatabaseProvider[] { new SqlServerProvider() });

    /// <summary>
    /// 全ダイアログ依存とプロバイダレジストリを注入するコンストラクター（DI 合成点・単体テスト用）
    /// </summary>
    /// <remarks>
    /// 全パラメーターが省略可能な単一コンストラクターへ集約している
    /// 省略された引数は既定実装で解決する（テスト・既定合成点用）
    /// </remarks>
    public MainViewModel(
        IDialogService? dialogService = null,
        IAppDialogService? appDialogs = null,
        IFileDialogService? files = null,
        DatabaseProviderRegistry? providers = null
    )
    {
        var resolvedFiles = files ?? new WpfFileDialogService();
        var resolvedProviders = providers ?? CreateDefaultRegistry();

        _dialogs = dialogService ?? new MessageBoxDialogService();
        _appDialogs = appDialogs ?? new WpfAppDialogService();
        _files = resolvedFiles;
        _providers = resolvedProviders;
        _currentProvider = ResolveProvider("sqlserver", warnOnFallback: false);
        _changeTracker = new DiagramChangeTracker(
            UndoRedo,
            Entities,
            Relationships,
            ApplyRelationshipColumnRules,
            OnColumnRenamed
        );
        LanguageSwitch = new LanguageSwitchViewModel(_dialogs);
        CopySelectedEntityCommand = new RelayCommand(CopySelectedEntity, CanCopySelectedEntity);
        PasteCopiedEntityCommand = new RelayCommand(PasteCopiedEntity, CanPasteCopiedEntity);
        Entities.CollectionChanged += OnEntitiesCollectionChanged;
        Relationships.CollectionChanged += OnRelationshipsCollectionChanged;

        // Undo 世代の変化（＝編集の発生）を購読し、ダーティ判定とウィンドウタイトルの * 表示へ反映する
        UndoRedo.PropertyChanged += OnUndoRedoStateChanged;

        // 外部変更監視サービスの購読・期待ハッシュ供給を結線する（監視の開始は現在パス設定に追従する）
        InitializeFileWatcher();
    }

    /// <summary>前回の自動保存ファイルを復元する。アプリ起動時に 1 回だけ呼び出すこと</summary>
    public void Initialize()
    {
        RestoreLastDiagram();

        // 復元後、現ファイルが最終既知ハッシュと乖離していれば外部変更として同じ規則で反映する
        CheckExternalChangeOnStartup();
    }

    /// <summary>キャンバスサイズを再計算して変更通知を発行する。エンティティの移動・サイズ変更後に呼び出す</summary>
    public void RefreshCanvasSize()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>ダイアグラム全体を指定モデルの内容で置き換える</summary>
    /// <param name="clearUndoHistory"><c>true</c> の場合は置換後に Undo/Redo 履歴を破棄する</param>
    /// <param name="queries">
    /// ファイル由来の図に付随する名前付きクエリ定義。<c>null</c>（既定）はファイル由来でない置換
    /// （新規・取込・DB 取込・AI 生成）を意味し、古い図の Guid 参照を持ち越さないようクエリを空にする
    /// </param>
    /// <remarks>既存リレーションは <see cref="RelationshipViewModel.Detach"/> で購読解除してから破棄し、イベントリークを防ぐ</remarks>
    private void ReplaceDiagram(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        bool clearUndoHistory,
        IReadOnlyDictionary<Guid, EntityLayout>? layout = null,
        IReadOnlyList<QueryDefinition>? queries = null
    )
    {
        // ファイル由来のときだけクエリを復元し、それ以外の置換ではクエリを空にする
        // （旧図のクエリが新図に残ると列・エンティティの Guid 参照が壊れるため）
        Queries = queries?.ToList() ?? new List<QueryDefinition>();

        _changeTracker.RunWithoutTracking(() =>
        {
            // Clear() は Reset 通知で OldItems を持たず CollectionChanged 側の自動解除が効かないため、
            // ここで明示的に購読を解除する
            foreach (var r in Relationships)
            {
                r.Detach();
            }

            Relationships.Clear();
            Entities.Clear();

            foreach (var entity in entities)
            {
                var entityLayout =
                    layout is not null && layout.TryGetValue(entity.Id, out var found)
                        ? found
                        : new EntityLayout();
                Entities.Add(new EntityViewModel(entity, entityLayout));
            }

            foreach (var relationship in relationships)
            {
                var src = Entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId);
                var tgt = Entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId);

                // 両端のエンティティを解決できないリレーションは不正データとして読み飛ばす
                if (src is null || tgt is null)
                {
                    continue;
                }

                Relationships.Add(new RelationshipViewModel(relationship, src, tgt));
            }

            SelectedEntity = null;
            SelectedRelationship = null;
            SelectedColumn = null;
        });

        if (clearUndoHistory)
        {
            ClearUndoRedoHistory();
        }

        // 図全体が置き換わったので、View 側で fit-to-window を要求する（開く・取込・DB取込・復元の共通点）
        RequestFitToWindow();
    }

    /// <summary>Undo/Redo 履歴を破棄し、ツールバーの有効状態を更新する</summary>
    private void ClearUndoRedoHistory()
    {
        UndoRedo.Clear();
        OnPropertyChanged(nameof(UndoRedo));
    }

    /// <summary>
    /// カラム名がユーザー編集で変更されたとき、その列を保持するエンティティを解決して
    /// <see cref="ColumnRenamed"/> を発火し、フィーチャーモジュールへ通知する
    /// （<see cref="DiagramChangeTracker"/> から通知される）
    /// </summary>
    /// <remarks>
    /// 通知するのはエンティティ単位（<see cref="ColumnRenamedEventArgs.EntityId"/>）で、名前付きクエリの
    /// 条件式（ミニ DSL）の追従書き換えはコード生成フィーチャーモジュール側（QueryConditionRenameFollower）が担う。
    /// この VM は列参照の書き換えロジックを持たない。
    /// </remarks>
    private void OnColumnRenamed(ColumnViewModel column, string oldName, string newName)
    {
        // 列を保持するエンティティを特定する（他エンティティに同名列があっても巻き込まないための単位）
        var owner = Entities.FirstOrDefault(entity => entity.Columns.Contains(column));

        if (owner is null)
        {
            return;
        }

        // フィーチャーモジュールへ通知する（名前付きクエリの条件式追従などに使用）
        ColumnRenamed?.Invoke(this, new ColumnRenamedEventArgs(owner.Id, oldName, newName));
    }

    /// <summary>名前付きクエリ定義を丸ごと差し替え、未保存変更として記録したうえで自動保存へ反映する</summary>
    /// <param name="queries">差し替える名前付きクエリ定義の一覧</param>
    /// <remarks>
    /// クエリは保存文書の一部（<see cref="ToDiagramModel"/> が含める）なので、Undo 履歴に積まなくても
    /// ダーティにはしなければならない（さもないと外部変更の自動再読込・新規作成で無警告に失われる）。
    /// 呼び出し元は「実際に変わったときだけ呼ぶ」契約（ダイアログの確定・クエリツールの成功・
    /// 条件式の追従書き換え発生時）なので、内容比較はせず呼ばれるたびに世代を進める。
    /// <see cref="ToDiagramModel"/> が返すクエリ一覧は要素を共有する浅いコピーで、呼び出し元は
    /// 定義そのものを直接書き換えてから渡してくるため、そもそも新旧の内容比較は成立しない。
    /// </remarks>
    public void ReplaceQueries(IReadOnlyList<QueryDefinition> queries)
    {
        Queries = queries.ToList();

        // Undo 非対象の変更なので、変更世代だけを進めてダーティ扱いにする
        UndoRedo.MarkChanged();
        AutoSave();
    }

    /// <summary>全エンティティの現在位置を履歴登録用にスナップショットする</summary>
    /// <returns>エンティティ ID をキーとする座標のディクショナリ</returns>
    private Dictionary<Guid, (double X, double Y)> CaptureEntityLayoutSnapshot()
    {
        return Entities.ToDictionary(entity => entity.Id, entity => (entity.X, entity.Y));
    }

    /// <summary>整列操作を適用し、移動前後の位置差分を Undo/Redo 履歴として登録する</summary>
    /// <param name="layoutAction">エンティティ位置を変更する整列処理</param>
    /// <param name="description">履歴に表示する操作名</param>
    private void ApplyLayoutWithUndo(Action layoutAction, string description)
    {
        var before = CaptureEntityLayoutSnapshot();

        // 整列による位置変更が個別のプロパティ変更履歴として二重登録されないよう追跡を抑止する
        _changeTracker.RunWithoutTracking(() =>
        {
            layoutAction();
            RefreshCanvasSize();
        });

        var after = CaptureEntityLayoutSnapshot();

        // 位置が 1 つも変わらなかった場合は空の履歴を積まない
        if (
            before.Count == after.Count
            && before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value)
        )
        {
            return;
        }

        UndoRedo.Push(
            new ArrangeEntitiesCommand(Entities, before, after, RefreshCanvasSize, description)
        );
    }

    /// <summary>履歴対象外でダイアグラムを置換し、幅自動調整と必要に応じた自動レイアウトをまとめて適用する</summary>
    /// <param name="autoLayout">
    /// <c>true</c> の場合は格子整列（<see cref="AutoLayoutService.LayoutGrid(IList{EntityViewModel},
    /// IList{RelationshipViewModel}, int)"/>）を適用する。
    /// 新規作成の全経路（取込・DB 取込・コード取込・AI 生成・配置なし文書の読込）で
    /// <see cref="AutoArrangeNewDiagram"/> と同じ整列にする
    /// </param>
    /// <param name="queries">復元する名前付きクエリ定義（省略時は空。配置なし JSON の受け入れで引き継ぐ）</param>
    private void ReplaceDiagramWithoutHistory(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships,
        bool autoLayout,
        IReadOnlyList<QueryDefinition>? queries = null
    )
    {
        ReplaceDiagram(entities, relationships, clearUndoHistory: true, queries: queries);

        _changeTracker.RunWithoutTracking(() =>
        {
            AutoFitEntityWidths(Entities);

            if (autoLayout)
            {
                AutoLayoutService.LayoutGrid(Entities, Relationships);
            }

            RefreshCanvasSize();
        });

        ClearUndoRedoHistory();
    }

    /// <summary>図を丸ごと差し替える（DB 取込などフィーチャーモジュールからの置換入口）</summary>
    /// <remarks>
    /// <para>方言採用の後、マージ取込（Guid 引継）に対応したレイアウト・クエリ透過ロジックで置換する。</para>
    /// <list type="bullet">
    /// <item>新図と現在図に同一 Id のエンティティが 1 件以上あれば（＝マージ取込）: 一致分の現在レイアウト
    /// （位置・色・幅）を引き継ぎ自動整列しない。新規エンティティは幅を自動調整したうえで、一致分を固定群として
    /// 空き領域へ追記配置する（<see cref="AutoLayoutService.LayoutAppend"/>＝一致分は不動・新規のみ配置）。
    /// クエリ（<see cref="ErDiagram.Queries"/>）はそのまま引き継ぐ</item>
    /// <item>一致が 1 件も無ければ（＝AI 生成・全新規取込）: 従来どおり全体を自動整列する。クエリは空でも
    /// 与えられていれば引き継ぐ（新規 Guid のみの経路では通常空）</item>
    /// </list>
    /// いずれも画面フィット要求までを含む。マージ照合（Id 書換え）自体は呼び出し側（DB 取込・Excel 取込）の責務。
    /// </remarks>
    public void ReplaceDiagramFromModule(ErDiagram diagram)
    {
        SetCurrentProviderFromDbms(diagram.TargetDbms);

        // 新図のエンティティ Id と現在図の Id の積集合＝マージで現在図へ寄った一致エンティティ
        var currentIds = Entities.Select(entity => entity.Id).ToHashSet();
        var matchedIds = diagram
            .Entities.Select(entity => entity.Id)
            .Where(currentIds.Contains)
            .ToHashSet();

        // 一致が 1 件も無ければ従来どおり全体自動整列（AI 生成・全新規取込の互換維持）
        if (matchedIds.Count == 0)
        {
            ReplaceDiagramWithoutHistory(
                diagram.Entities,
                diagram.Relationships,
                autoLayout: true,
                diagram.Queries
            );
            return;
        }

        // 一致エンティティの現在レイアウト（位置・幅・色）を採取して引き継ぐ（＝自動整列しない）
        var layout = Entities
            .Where(entity => matchedIds.Contains(entity.Id))
            .ToDictionary(entity => entity.Id, entity => entity.ToLayout());

        ReplaceDiagram(
            diagram.Entities,
            diagram.Relationships,
            clearUndoHistory: true,
            layout,
            diagram.Queries
        );

        // 新規エンティティ（レイアウト未継承）のみ幅を自動調整し、一致分を固定群として空き領域へ追記配置する
        // （一致分の保存幅・位置は尊重し不動。新規は原点に積まず既存の隣接領域へ格子配置する）
        _changeTracker.RunWithoutTracking(() =>
        {
            var newEntities = Entities.Where(entity => !matchedIds.Contains(entity.Id)).ToList();
            AutoFitEntityWidths(newEntities);

            if (newEntities.Count > 0)
            {
                var placed = Entities.Where(entity => matchedIds.Contains(entity.Id)).ToList();
                AutoLayoutService.LayoutAppend(placed, newEntities, Relationships);
            }

            RefreshCanvasSize();
        });
    }

    /// <summary>「説明」表示の切替を全エンティティへ伝播し、キャンバスサイズを更新する</summary>
    partial void OnShowColumnDescriptionsInDiagramChanged(bool value)
    {
        foreach (var entity in Entities)
        {
            entity.ShowDescriptionsInDiagram = value;
        }

        RefreshCanvasSize();
    }

    /// <summary>ER 図上のカラム行に NULL 許容を表示するかどうか（ツールバーから切替）</summary>
    /// <remarks>変更時に全エンティティへ設定を伝播する必要があるため、生成属性を使わず手動実装とする</remarks>
    public bool ShowNullabilityInDiagram
    {
        get => _showNullabilityInDiagram;
        set
        {
            if (!SetProperty(ref _showNullabilityInDiagram, value))
            {
                return;
            }

            foreach (var entity in Entities)
            {
                entity.ShowNullabilityInDiagram = value;
            }

            RefreshCanvasSize();
        }
    }

    /// <summary>ER 図上で簡易表示（PK/FK カラムのみ）を行うかどうか（ツールバーから切替）</summary>
    /// <remarks>変更時に全エンティティへ設定を伝播する必要があるため、生成属性を使わず手動実装とする</remarks>
    public bool IsCompactViewInDiagram
    {
        get => _isCompactViewInDiagram;
        set
        {
            if (!SetProperty(ref _isCompactViewInDiagram, value))
            {
                return;
            }

            foreach (var entity in Entities)
            {
                entity.IsCompactView = value;
            }

            RefreshCanvasSize();
        }
    }

    // ---------------- Commands ----------------

    /// <summary>確認のうえダイアグラムを空にする</summary>
    [RelayCommand]
    private void NewDiagram()
    {
        if (
            !HasNothingToLose
            && !_dialogs.ConfirmDiscard(
                IsDirty,
                Strings.Confirm_ClearDiagram,
                Strings.Common_Confirm
            )
        )
        {
            return;
        }

        ReplaceDiagram(Array.Empty<Entity>(), Array.Empty<Relationship>(), clearUndoHistory: true);

        // 新規図はどのファイルにも紐付かないため、現在パス・内容ハッシュをクリアし、
        // 空の新規文書としてクリーン状態にする（ウィンドウタイトルは既定の QuickER へ戻る）
        CurrentFilePath = null;
        _lastKnownFileHash = null;
        MarkClean();
    }

    /// <summary>PK カラム付きの新規エンティティを追加して選択する（Undo 可能）</summary>
    [RelayCommand]
    private void AddEntity()
    {
        var model = new Entity
        {
            TableName = "NewTable",
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        // 現在表示中のビューポート内へ、既存数に応じた斜めずらし付きで配置する
        // （スクロール／ズーム中でも画面外へ追加されない。View 未接続時は従来の左上カスケード）
        var layout = new EntityLayout();
        var position = ViewportCalculator.NextEntityPosition(
            ViewportContentBounds,
            Entities.Count,
            layout.Width
        );
        layout.X = position.X;
        layout.Y = position.Y;

        var vm = new EntityViewModel(model, layout);
        UndoRedo.Execute(new AddEntityCommand(this, vm));
        SelectedEntity = vm;
    }

    /// <summary>選択中のエンティティを削除する（Undo 可能）</summary>
    /// <remarks>
    /// 2 個以上選択されている場合は選択中の全エンティティ（と接続リレーション）を
    /// <see cref="UndoRedo.GroupRemoveEntitiesCommand"/> で一括削除し、Undo は複合 1 エントリとなる。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRemoveEntity))]
    private void RemoveSelectedEntity()
    {
        var selected = SelectedEntities;

        if (selected.Count >= 2)
        {
            UndoRedo.Execute(new GroupRemoveEntitiesCommand(this, selected));
            SelectedEntity = null;
            return;
        }

        if (SelectedEntity is null)
        {
            return;
        }

        UndoRedo.Execute(new RemoveEntityCommand(this, SelectedEntity));
        SelectedEntity = null;
    }

    /// <summary>エンティティ削除コマンドの実行可否</summary>
    private bool CanRemoveEntity() => SelectedEntity is not null;

    /// <summary>1 対 1 リレーションの作成モードを開始する</summary>
    [RelayCommand]
    private void StartAddOneToOne() => StartRelationshipMode(RelationshipType.OneToOne);

    /// <summary>1 対多リレーションの作成モードを開始する</summary>
    [RelayCommand]
    private void StartAddOneToMany() => StartRelationshipMode(RelationshipType.OneToMany);

    /// <summary>多対多リレーションの作成モードを開始する</summary>
    [RelayCommand]
    private void StartAddManyToMany() => StartRelationshipMode(RelationshipType.ManyToMany);

    /// <summary>リレーション作成モードを中断する</summary>
    [RelayCommand]
    private void CancelRelationshipMode()
    {
        IsRelationshipMode = false;
        PendingRelationshipSource = null;
    }

    /// <summary>指定種別でリレーション作成モードへ移行する。以降のエンティティクリック 2 回で接続を確定する</summary>
    private void StartRelationshipMode(RelationshipType type)
    {
        PendingRelationshipType = type;
        PendingRelationshipSource = null;
        IsRelationshipMode = true;
    }

    /// <summary>選択中のリレーションを削除する（Undo 可能）</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveRelationship))]
    private void RemoveSelectedRelationship()
    {
        if (SelectedRelationship is null)
        {
            return;
        }

        UndoRedo.Execute(new RemoveRelationshipCommand(this, SelectedRelationship));
        SelectedRelationship = null;
    }

    /// <summary>リレーション削除コマンドの実行可否</summary>
    private bool CanRemoveRelationship() => SelectedRelationship is not null;

    /// <summary>Delete キーの削除対象を選択状態に応じて振り分ける（リレーション優先）</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRelationship is not null)
        {
            RemoveSelectedRelationship();
            return;
        }

        if (SelectedEntity is not null)
        {
            RemoveSelectedEntity();
        }
    }

    /// <summary>選択中エンティティへ新規カラムを追加して選択する（Undo 可能）</summary>
    [RelayCommand(CanExecute = nameof(CanAddColumn))]
    private void AddColumn()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var column = new ColumnViewModel(
            new Column
            {
                Name = "NewColumn",
                DataType = CurrentProvider.TypeCatalog.DefaultDataType,
            }
        );

        UndoRedo.Execute(new AddColumnCommand(SelectedEntity.Columns, column));
        SelectedColumn = column;
    }

    /// <summary>カラム追加コマンドの実行可否</summary>
    private bool CanAddColumn() => SelectedEntity is not null;

    /// <summary>選択中エンティティの内容を内部バッファへコピーする</summary>
    private void CopySelectedEntity()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        _copiedEntity = SelectedEntity.ToModel();
        _copiedEntityLayout = SelectedEntity.ToLayout();
        _copiedEntityPasteCount = 0;
        PasteCopiedEntityCommand.NotifyCanExecuteChanged();
    }

    /// <summary>エンティティコピーコマンドの実行可否</summary>
    private bool CanCopySelectedEntity() => SelectedEntity is not null;

    /// <summary>内部バッファのエンティティを位置をずらして複製追加し、選択する（Undo 可能）</summary>
    private void PasteCopiedEntity()
    {
        if (_copiedEntity is null)
        {
            return;
        }

        // 連続ペーストのたびにオフセットを増やし、複製同士の重なりを避ける
        _copiedEntityPasteCount++;
        var pastedEntity = CreateEntityCopy(
            _copiedEntity,
            _copiedEntityLayout ?? new EntityLayout(),
            _copiedEntityPasteCount
        );
        UndoRedo.Execute(new AddEntityCommand(this, pastedEntity));
        SelectSingleEntity(pastedEntity);
    }

    /// <summary>エンティティペーストコマンドの実行可否</summary>
    private bool CanPasteCopiedEntity() => _copiedEntity is not null;

    /// <summary>選択中カラムの内容を内部バッファへコピーする</summary>
    [RelayCommand(CanExecute = nameof(CanCopySelectedColumn))]
    private void CopySelectedColumn()
    {
        if (SelectedColumn is null)
        {
            return;
        }

        // ID を引き継ぐとリレーションの参照が複製先と衝突するため、新規 ID で複製する
        _copiedColumn = SelectedColumn.ToModel().Clone(preserveId: false);
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
    }

    /// <summary>カラムコピーコマンドの実行可否</summary>
    private bool CanCopySelectedColumn() => SelectedColumn is not null;

    /// <summary>内部バッファのカラムを選択中カラムの直下へ複製追加し、選択する（Undo 可能）</summary>
    [RelayCommand(CanExecute = nameof(CanPasteCopiedColumn))]
    private void PasteCopiedColumn()
    {
        if (SelectedEntity is null || SelectedColumn is null || _copiedColumn is null)
        {
            return;
        }

        var insertIndex = SelectedEntity.Columns.IndexOf(SelectedColumn);

        if (insertIndex < 0)
        {
            return;
        }

        var pastedColumn = new ColumnViewModel(_copiedColumn.Clone(preserveId: false));
        UndoRedo.Execute(
            new AddColumnCommand(SelectedEntity.Columns, pastedColumn, insertIndex + 1)
        );
        SelectedColumn = pastedColumn;
    }

    /// <summary>カラムペーストコマンドの実行可否</summary>
    private bool CanPasteCopiedColumn() =>
        SelectedEntity is not null && SelectedColumn is not null && _copiedColumn is not null;

    /// <summary>指定カラムを選択中エンティティから削除する（行内の削除ボタン用、Undo 可能）</summary>
    /// <remarks>削除カラムを参照するリレーションを履歴コマンドへ渡し、Undo 時に参照を復元できるようにする</remarks>
    [RelayCommand]
    private void RemoveColumn(ColumnViewModel? column)
    {
        if (SelectedEntity is null || column is null)
        {
            return;
        }

        var affected = FindRelationshipsUsingColumn(column);
        UndoRedo.Execute(
            new RemoveColumnCommand(
                SelectedEntity,
                column,
                affected,
                () => ApplyRelationshipColumnRules()
            )
        );

        if (SelectedColumn == column)
        {
            SelectedColumn = null;
        }
    }

    /// <summary>DataGrid で選択中のカラムを削除する（ツールバーボタン用、Undo 可能）</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveSelectedColumn))]
    private void RemoveSelectedColumn()
    {
        if (SelectedEntity is null || SelectedColumn is null)
        {
            return;
        }

        var col = SelectedColumn;
        var affected = FindRelationshipsUsingColumn(col);
        UndoRedo.Execute(
            new RemoveColumnCommand(
                SelectedEntity,
                col,
                affected,
                () => ApplyRelationshipColumnRules()
            )
        );
        SelectedColumn = null;
    }

    /// <summary>カラム削除コマンド（ツールバー）の実行可否</summary>
    private bool CanRemoveSelectedColumn() =>
        SelectedEntity is not null && SelectedColumn is not null;

    /// <summary>選択中エンティティへ空の一意制約を追加する（Undo 可能）</summary>
    /// <remarks>構成列は追加後にカードの列行（コンボボックス）で選ぶ（＝「+ → 列行の + → 列選択」で単一列制約が作れる）</remarks>
    [RelayCommand(CanExecute = nameof(CanAddUniqueConstraint))]
    private void AddUniqueConstraint()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var constraint = new UniqueConstraintViewModel(SelectedEntity, new UniqueConstraint());
        UndoRedo.Execute(
            new AddUniqueConstraintCommand(SelectedEntity.UniqueConstraints, constraint)
        );

        // 畳んだままだと追加した制約が見えないため、追加操作ではカードを開く
        IsUniqueConstraintCardExpanded = true;
    }

    /// <summary>一意制約追加コマンドの実行可否</summary>
    private bool CanAddUniqueConstraint() => SelectedEntity is not null;

    /// <summary>指定の一意制約を選択中エンティティから削除する（Undo 可能）</summary>
    [RelayCommand]
    private void RemoveUniqueConstraint(UniqueConstraintViewModel? constraint)
    {
        if (SelectedEntity is null || constraint is null)
        {
            return;
        }

        UndoRedo.Execute(
            new RemoveUniqueConstraintCommand(SelectedEntity.UniqueConstraints, constraint)
        );
    }

    /// <summary>一意制約へ未選択の構成列行（空スロット）を 1 つ足す</summary>
    /// <remarks>
    /// この時点ではモデルを変えないため履歴に残さない（列が選ばれた時点で
    /// <see cref="SetUniqueConstraintMemberCommand"/> が 1 回の Undo 単位として確定させる）
    /// </remarks>
    [RelayCommand]
    private void AddUniqueConstraintMemberSlot(UniqueConstraintViewModel? constraint) =>
        constraint?.AddPendingSlot();

    /// <summary>構成列行で選ばれた列を正本へ確定する（Undo 可能）</summary>
    /// <remarks>
    /// 空スロットでの選択は末尾への追加、既存行での選択変更はその位置の差し替えになる
    /// （どちらも「行の並び＝宣言順」から新しい構成列一覧を組み立てるだけで表現できる）
    /// </remarks>
    [RelayCommand]
    private void SetUniqueConstraintMember(UniqueConstraintMemberViewModel? member)
    {
        if (member is null)
        {
            return;
        }

        ApplyUniqueConstraintColumns(
            member.Constraint,
            member.Constraint.BuildColumnIdsFromMembers()
        );
    }

    /// <summary>構成列行を 1 つ取り除く（Undo 可能。空スロットはビュー状態の破棄のみ）</summary>
    [RelayCommand]
    private void RemoveUniqueConstraintMember(UniqueConstraintMemberViewModel? member)
    {
        if (member is null)
        {
            return;
        }

        // 空スロットはまだモデルに反映されていないため、履歴を汚さず取り消すだけでよい
        if (member.IsPendingSlot)
        {
            member.Constraint.CancelPendingSlot();
            return;
        }

        ApplyUniqueConstraintColumns(
            member.Constraint,
            member.Constraint.BuildColumnIdsFromMembers(excluded: member)
        );
    }

    /// <summary>一意制約の構成列一覧を Undo 可能な差し替えとして適用する</summary>
    private void ApplyUniqueConstraintColumns(
        UniqueConstraintViewModel constraint,
        IReadOnlyList<Guid> after
    )
    {
        var before = constraint.ColumnIds.ToList();

        // 実質的な変化がなければ履歴を汚さない
        if (before.SequenceEqual(after))
        {
            return;
        }

        UndoRedo.Execute(new ChangeUniqueConstraintColumnsCommand(constraint, before, after));
    }

    /// <summary>カラム選択の変化に応じてカラム操作系コマンドの実行可否を更新する</summary>
    partial void OnSelectedColumnChanged(ColumnViewModel? value)
    {
        RemoveSelectedColumnCommand.NotifyCanExecuteChanged();
        CopySelectedColumnCommand.NotifyCanExecuteChanged();
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
    }

    /// <summary>リレーションへ未確定の列ペア行（空スロット）を 1 つ足す</summary>
    /// <remarks>
    /// この時点ではモデルを変えないため履歴に残さない（親列・子列の<b>両方</b>が選ばれた時点で
    /// <see cref="SetRelationshipColumnPairCommand"/> が 1 回の Undo 単位として確定させる）
    /// </remarks>
    [RelayCommand]
    private void AddRelationshipColumnPairSlot(RelationshipViewModel? relationship) =>
        relationship?.AddPendingColumnPairSlot();

    /// <summary>列ペア行で選ばれた列を正本へ確定する（Undo 可能）</summary>
    /// <remarks>
    /// 空スロットでの選択は末尾への追加、既存行での選択変更はその位置の差し替えになる
    /// （どちらも「行の並び＝宣言順」から新しい列ペア一覧を組み立てるだけで表現できる）。
    /// 片側だけ選ばれた行はまだ列ペアにならないため、この時点では履歴も動かない
    /// </remarks>
    [RelayCommand]
    private void SetRelationshipColumnPair(RelationshipColumnPairViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        ApplyRelationshipColumnPairs(row.Relationship, row.Relationship.BuildColumnPairsFromRows());
    }

    /// <summary>列ペア行を 1 つ取り除く（Undo 可能。空スロットはビュー状態の破棄のみ）</summary>
    [RelayCommand]
    private void RemoveRelationshipColumnPair(RelationshipColumnPairViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // 空スロットはまだモデルに反映されていないため、履歴を汚さず取り消すだけでよい
        if (row.IsPendingSlot)
        {
            row.Relationship.CancelPendingColumnPairSlot();
            return;
        }

        ApplyRelationshipColumnPairs(
            row.Relationship,
            row.Relationship.BuildColumnPairsFromRows(excluded: row)
        );
    }

    /// <summary>リレーションの列ペア一覧を Undo 可能な差し替えとして適用する</summary>
    private void ApplyRelationshipColumnPairs(
        RelationshipViewModel relationship,
        IReadOnlyList<RelationshipColumnPair> after
    )
    {
        var before = relationship.SnapshotColumnPairs();

        // 実質的な変化がなければ履歴を汚さない
        if (RelationshipViewModel.SameColumnPairs(before, after))
        {
            return;
        }

        UndoRedo.Execute(
            new ChangeRelationshipColumnPairsCommand(
                relationship,
                before,
                after,
                () => ApplyRelationshipColumnRules()
            )
        );
    }

    /// <summary>指定カラムを列ペアのいずれかで使用しているリレーション一覧を返す</summary>
    /// <remarks>カラム削除コマンドへ渡し、Undo 時に外部キー参照を復元するために用いる（UI 経由・AI ツール経由で共用）</remarks>
    internal IReadOnlyList<RelationshipViewModel> FindRelationshipsUsingColumn(
        ColumnViewModel column
    ) =>
        Relationships
            .Where(relationship =>
                relationship.ColumnPairs.Any(pair =>
                    pair.SourceColumnId == column.Id || pair.TargetColumnId == column.Id
                )
            )
            .ToList();

    // ---------------- Selection / Click handling ----------------

    /// <summary>エンティティクリック時の処理。リレーション作成モード中は接続確定、通常時は単一選択を行う</summary>
    public void OnEntityClicked(EntityViewModel entity)
    {
        if (IsRelationshipMode)
        {
            // 1 回目のクリックは始点の確定のみ
            if (PendingRelationshipSource is null)
            {
                PendingRelationshipSource = entity;
                return;
            }

            if (HasSameRelationship(PendingRelationshipSource, entity))
            {
                _dialogs.ShowInformation(
                    Strings.Relationship_DuplicateMessage,
                    Strings.Relationship_DuplicateTitle
                );

                IsRelationshipMode = false;
                PendingRelationshipSource = null;
                return;
            }

            // 参照元は始点の PK 全列、参照先は共通リゾルバの既定 FK 解決、制約名は FK_<参照側>_<被参照側> を初期値とする
            var rel = new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = PendingRelationshipSource.Id,
                    TargetEntityId = entity.Id,
                    Type = PendingRelationshipType,
                    ColumnPairs = ForeignKeyColumnResolver.ResolveColumnPairs(
                        PendingRelationshipSource,
                        entity,
                        Relationships
                    ),
                    ConstraintName =
                        $"FK_{SqlIdentifier.SafeName(entity.TableName)}_{SqlIdentifier.SafeName(PendingRelationshipSource.TableName)}",
                },
                PendingRelationshipSource,
                entity
            );

            UndoRedo.Execute(new AddRelationshipCommand(this, rel));

            IsRelationshipMode = false;
            PendingRelationshipSource = null;
        }
        else
        {
            SelectSingleEntity(entity);
        }
    }

    /// <summary>同一の始点・終点を持つリレーションが既に存在するかを判定する（種別は問わない）</summary>
    private bool HasSameRelationship(EntityViewModel source, EntityViewModel target)
    {
        return Relationships.Any(relationship =>
            relationship.Source == source && relationship.Target == target
        );
    }

    /// <summary>リレーションクリック時の処理。対象を単一選択し、エンティティ選択を解除する</summary>
    public void OnRelationshipClicked(RelationshipViewModel rel)
    {
        foreach (var e in Entities)
        {
            e.IsSelected = false;
        }

        foreach (var r in Relationships)
        {
            r.IsSelected = (r == rel);
        }

        SelectedEntity = null;
        SelectedRelationship = rel;
    }

    /// <summary>キャンバス空白部クリック時の処理。すべての選択を解除する</summary>
    public void OnCanvasClicked()
    {
        foreach (var e in Entities)
        {
            e.IsSelected = false;
        }

        foreach (var r in Relationships)
        {
            r.IsSelected = false;
        }

        SelectedEntity = null;
        SelectedRelationship = null;
    }

    /// <summary>エンティティ選択の変化に応じてエンティティ操作系コマンドの実行可否を更新する</summary>
    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        RemoveSelectedEntityCommand.NotifyCanExecuteChanged();
        AddColumnCommand.NotifyCanExecuteChanged();
        AddUniqueConstraintCommand.NotifyCanExecuteChanged();
        CopySelectedEntityCommand.NotifyCanExecuteChanged();
        PasteCopiedColumnCommand.NotifyCanExecuteChanged();
        DuplicateSelectedEntityCommand.NotifyCanExecuteChanged();

        // UNIQUE 制約カードの開閉既定: 制約が定義されていれば開き、なければ畳む（手動開閉は選択が変わるまで有効）
        IsUniqueConstraintCardExpanded = value is { UniqueConstraints.Count: > 0 };

        // 選択の変化に応じて関連ハイライト（減光／強調）を再計算する
        UpdateRelatedHighlights();
    }

    /// <summary>リレーション選択の変化に応じて削除コマンドの実行可否を更新する</summary>
    partial void OnSelectedRelationshipChanged(RelationshipViewModel? value)
    {
        RemoveSelectedRelationshipCommand.NotifyCanExecuteChanged();

        // 選択の変化に応じて関連ハイライト（減光／強調）を再計算する
        UpdateRelatedHighlights();
    }

    /// <summary>
    /// 現在の選択状態（エンティティ／リレーション）から各要素の関連ハイライト状態を再計算する
    /// </summary>
    /// <remarks>
    /// これらは選択状態から導出する純粋な表示状態であり、Undo 履歴・保存対象には含めない
    /// <list type="bullet">
    /// <item>エンティティ選択時（単一・複数とも）: 選択メンバー・その接続リレーション・相手側エンティティは
    /// 通常表示、それ以外を減光。接続リレーションは強調（複数選択は単一選択の意味論を選択集合の和へ一般化）</item>
    /// <item>リレーション選択時: 選択リレーションと両端エンティティは通常表示、それ以外を減光（強調は行わず選択自体の青強調に任せる）</item>
    /// <item>未選択時: 全要素とも通常表示（減光・強調なし）</item>
    /// </list>
    /// 自己参照リレーション（Source==Target）も選択エンティティに接続していれば強調対象となる
    /// </remarks>
    private void UpdateRelatedHighlights()
    {
        // 選択操作は必ずここを通るため、選択数・一括操作切替に関わる派生プロパティの通知もここへ集約する
        NotifySelectionChanged();

        var selectedEntities = Entities.Where(e => e.IsSelected).ToList();

        // 主選択のみ設定されるケース（プログラム操作・テスト）は単一選択として扱う
        if (selectedEntities.Count == 0 && SelectedEntity is not null)
        {
            selectedEntities.Add(SelectedEntity);
        }

        if (selectedEntities.Count > 0)
        {
            ApplyEntitySelectionHighlights(selectedEntities);
            return;
        }

        if (SelectedRelationship is not null)
        {
            ApplyRelationshipSelectionHighlights(SelectedRelationship);
            return;
        }

        ClearRelatedHighlights();
    }

    /// <summary>選択エンティティ集合を基準に、接続リレーションと相手側エンティティを通常表示に保ち他を減光する</summary>
    /// <remarks>単一選択は要素数 1 の集合として同じ規則で扱う（複数選択は関連の和集合）</remarks>
    private void ApplyEntitySelectionHighlights(IReadOnlyList<EntityViewModel> selected)
    {
        var selectedSet = selected.ToHashSet();

        // 選択メンバーに接続するリレーションと、その相手側エンティティ群を収集する
        var relatedEntities = new HashSet<EntityViewModel>(selectedSet);

        foreach (var relationship in Relationships)
        {
            var isConnected =
                selectedSet.Contains(relationship.Source)
                || selectedSet.Contains(relationship.Target);

            // 接続線は強調・非減光、相手側エンティティは非減光対象へ加える
            relationship.IsEmphasized = isConnected;
            relationship.IsDimmed = !isConnected;

            if (isConnected)
            {
                relatedEntities.Add(relationship.Source);
                relatedEntities.Add(relationship.Target);
            }
        }

        foreach (var entity in Entities)
        {
            entity.IsDimmed = !relatedEntities.Contains(entity);
        }
    }

    /// <summary>選択リレーションを基準に、両端エンティティを通常表示に保ち他を減光する（強調は行わない）</summary>
    private void ApplyRelationshipSelectionHighlights(RelationshipViewModel selected)
    {
        foreach (var entity in Entities)
        {
            entity.IsDimmed = entity != selected.Source && entity != selected.Target;
        }

        foreach (var relationship in Relationships)
        {
            // 選択リレーション自体は青強調（IsSelected）に任せ、それ以外を減光する
            relationship.IsEmphasized = false;
            relationship.IsDimmed = relationship != selected;
        }
    }

    /// <summary>全要素の減光・強調を解除し通常表示へ戻す</summary>
    private void ClearRelatedHighlights()
    {
        foreach (var entity in Entities)
        {
            entity.IsDimmed = false;
        }

        foreach (var relationship in Relationships)
        {
            relationship.IsDimmed = false;
            relationship.IsEmphasized = false;
        }
    }

    // ---------------- Undo/Redo ----------------

    /// <summary>直前の操作を取り消す</summary>
    /// <remarks>履歴の再生による変更が新たな履歴として記録されないよう追跡を抑止する</remarks>
    [RelayCommand]
    private void Undo() => _changeTracker.RunWithoutTracking(() => UndoRedo.Undo());

    /// <summary>取り消した操作をやり直す</summary>
    [RelayCommand]
    private void Redo() => _changeTracker.RunWithoutTracking(() => UndoRedo.Redo());

    /// <summary>エンティティクリックを処理するコマンド（View のイベントバインド用）</summary>
    [RelayCommand]
    private void EntityClick(EntityViewModel? entity)
    {
        if (entity is not null)
        {
            OnEntityClicked(entity);
        }
    }

    /// <summary>リレーションクリックを処理するコマンド（View のイベントバインド用）</summary>
    [RelayCommand]
    private void RelationshipClick(RelationshipViewModel? rel)
    {
        if (rel is not null)
        {
            OnRelationshipClicked(rel);
        }
    }

    /// <summary>キャンバスクリックを処理するコマンド（View のイベントバインド用）</summary>
    [RelayCommand]
    private void CanvasClick() => OnCanvasClicked();

    // ---------------- Duplicate (Ctrl+D) ----------------

    /// <summary>選択中エンティティを複製して選択する（Ctrl+D 用、Undo 可能）</summary>
    [RelayCommand(CanExecute = nameof(CanDuplicateSelectedEntity))]
    private void DuplicateSelectedEntity()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var cmd = new DuplicateEntityCommand(this, SelectedEntity);
        UndoRedo.Execute(cmd);

        if (cmd.Duplicated is not null)
        {
            SelectSingleEntity(cmd.Duplicated);
        }
    }

    /// <summary>エンティティ複製コマンドの実行可否</summary>
    private bool CanDuplicateSelectedEntity() => SelectedEntity is not null;

    // ---------------- Auto layout ----------------

    /// <summary>エンティティを格子状に整列する（リレーション線の交差をできるだけ減らす配置 Undo 可能）</summary>
    [RelayCommand]
    private void AutoLayoutGrid()
    {
        ApplyLayoutWithUndo(
            () => AutoLayoutService.LayoutGrid(Entities, Relationships),
            Strings.Toolbar_ArrangeGrid
        );

        // 整列後の全体像が収まるよう fit-to-window を要求する
        RequestFitToWindow();
    }

    /// <summary>エンティティをリレーション階層に基づくツリー状に整列する（Undo 可能）</summary>
    [RelayCommand]
    private void AutoLayoutTree()
    {
        ApplyLayoutWithUndo(
            () => AutoLayoutService.LayoutTree(Entities, Relationships),
            Strings.Toolbar_ArrangeTree
        );

        // 整列後の全体像が収まるよう fit-to-window を要求する
        RequestFitToWindow();
    }

    /// <summary>エンティティを力学モデルで配置し、リレーション線が水平/垂直に近づくよう整列する（Undo 可能）</summary>
    [RelayCommand]
    private void AutoLayoutForce()
    {
        ApplyLayoutWithUndo(
            () => AutoLayoutService.LayoutForceDirected(Entities, Relationships),
            Strings.Toolbar_ArrangeForce
        );

        // 整列後の全体像が収まるよう fit-to-window を要求する
        RequestFitToWindow();
    }

    /// <summary>AI によるER図の新規生成直後に、表示幅調整と格子整列をまとめて適用する（履歴には積まない）</summary>
    /// <remarks>
    /// Codex チャットが空のキャンバスからエンティティを生成した直後の呼び出しを想定する。
    /// 個々の生成操作（add_entity 等）と一体の初期配置として扱うため、Undo 履歴へは登録しない。
    /// </remarks>
    public void AutoArrangeNewDiagram()
    {
        if (Entities.Count == 0)
        {
            return;
        }

        _changeTracker.RunWithoutTracking(() =>
        {
            AutoFitEntityWidths(Entities);
            AutoLayoutService.LayoutGrid(Entities, Relationships);
            RefreshCanvasSize();
        });

        // AI 生成直後の初期配置が収まるよう fit-to-window を要求する
        RequestFitToWindow();
    }

    /// <summary>全エンティティの表示幅を内容に合わせて自動調整する</summary>
    /// <remarks>
    /// 幅は Undo 履歴へ積まない（ドラッグでのリサイズと同じ扱い）が、保存対象
    /// （<see cref="Documents.EntityLayout.Width"/>）なので実際に変化したときは未保存変更として記録する。
    /// </remarks>
    [RelayCommand]
    private void AutoFitEntityWidths()
    {
        var changed = AutoFitEntityWidths(Entities);
        RefreshCanvasSize();

        if (changed)
        {
            UndoRedo.MarkChanged();
        }
    }

    /// <summary>指定エンティティ群の表示幅を一括で自動調整する</summary>
    /// <returns>1 つでも幅が実際に変化した場合 true</returns>
    private static bool AutoFitEntityWidths(IEnumerable<EntityViewModel> entities)
    {
        var changed = false;

        foreach (var entity in entities)
        {
            var before = entity.Width;
            entity.AutoFitWidth();

            if (entity.Width != before)
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>リレーション構成に基づいて全カラムの PK/FK 編集可否と FK フラグを同期する</summary>
    /// <param name="excludedSnapshotTarget">スナップショット再取得から除外する対象（変更途中の値を確定値として誤記録しないために指定）</param>
    /// <remarks>
    /// いったん全カラムを編集可能へリセットし、リレーション管理下の FK フラグも解除したうえで、
    /// 現存するリレーションのみを根拠にロックと FK フラグを付け直す。リレーション削除時の解除漏れを防ぐ方式
    /// </remarks>
    public void ApplyRelationshipColumnRules(object? excludedSnapshotTarget = null)
    {
        _changeTracker.RunWithoutTracking(
            () =>
            {
                foreach (var entity in Entities)
                {
                    foreach (var column in entity.Columns)
                    {
                        column.IsPrimaryKeyEditable = true;
                        column.IsForeignKeyEditable = true;

                        // ユーザーが手動設定した FK は維持し、リレーション由来の FK のみ解除する
                        if (column.IsForeignKeyManagedByRelationship)
                        {
                            column.IsForeignKey = false;
                            column.IsForeignKeyManagedByRelationship = false;
                        }
                    }
                }

                foreach (var relationship in Relationships)
                {
                    LockRelationshipColumns(relationship);
                }
            },
            excludedSnapshotTarget
        );
    }

    /// <summary>指定リレーションが使用する全構成列の編集をロックし、参照先カラムへ FK フラグを設定する</summary>
    /// <remarks>複合外部キーではペアの数だけ両端の列が対象になる（1 組でも同じ処理で足りる）</remarks>
    private static void LockRelationshipColumns(RelationshipViewModel relationship)
    {
        foreach (var pair in relationship.ColumnPairs)
        {
            var sourceColumn = relationship.Source.Columns.FirstOrDefault(column =>
                column.Id == pair.SourceColumnId
            );
            var targetColumn = relationship.Target.Columns.FirstOrDefault(column =>
                column.Id == pair.TargetColumnId
            );

            if (sourceColumn is not null)
            {
                sourceColumn.IsPrimaryKeyEditable = false;
                sourceColumn.IsForeignKeyEditable = false;
            }

            if (targetColumn is not null)
            {
                targetColumn.IsPrimaryKeyEditable = false;
                targetColumn.IsForeignKeyEditable = false;
                targetColumn.IsForeignKeyManagedByRelationship = true;
                targetColumn.IsForeignKey = true;
            }
        }
    }

    /// <summary>コピー元 ViewModel から位置をずらした複製 ViewModel を生成する</summary>
    /// <param name="offsetMultiplier">位置オフセット（30px）の倍率。連続ペースト時に増やして重なりを避ける</param>
    internal EntityViewModel CreateEntityCopy(EntityViewModel source, int offsetMultiplier = 1) =>
        CreateEntityCopy(source.ToModel(), source.ToLayout(), offsetMultiplier);

    /// <summary>コピー元の意味モデルとレイアウトから位置をずらした複製 ViewModel を生成する。テーブル名は重複しない名前へ変更する</summary>
    /// <param name="offsetMultiplier">位置オフセット（30px）の倍率。1 未満は 1 に丸める</param>
    internal EntityViewModel CreateEntityCopy(
        Entity source,
        EntityLayout sourceLayout,
        int offsetMultiplier = 1
    )
    {
        var copy = source.Clone(preserveId: false);
        var normalizedOffsetMultiplier = Math.Max(1, offsetMultiplier);
        var offset = 30 * normalizedOffsetMultiplier;

        copy.TableName = GenerateCopyTableName(source.TableName);

        var layout = new EntityLayout
        {
            X = sourceLayout.X + offset,
            Y = sourceLayout.Y + offset,
            Width = sourceLayout.Width,
            TitleBackgroundColor = sourceLayout.TitleBackgroundColor,
        };

        return new EntityViewModel(copy, layout);
    }

    /// <summary>指定エンティティのみを選択状態にし、他の選択をすべて解除する</summary>
    private void SelectSingleEntity(EntityViewModel entity)
    {
        foreach (var currentEntity in Entities)
        {
            currentEntity.IsSelected = (currentEntity == entity);
        }

        foreach (var relationship in Relationships)
        {
            relationship.IsSelected = false;
        }

        SelectedEntity = entity;
        SelectedRelationship = null;
    }

    /// <summary>複製時に既存テーブル名と衝突しない名前を決定する</summary>
    /// <returns>「元名_Copy」を基本とし、衝突する場合は「元名_Copy2」以降の連番を採用する</returns>
    private string GenerateCopyTableName(string originalTableName)
    {
        var normalizedTableName = string.IsNullOrWhiteSpace(originalTableName)
            ? "NewTable"
            : originalTableName.Trim();
        var candidate = $"{normalizedTableName}_Copy";
        var suffix = 2;

        while (
            Entities.Any(entity =>
                string.Equals(entity.TableName, candidate, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            candidate = $"{normalizedTableName}_Copy{suffix}";
            suffix++;
        }

        return candidate;
    }

    // ---------------- ターゲット DBMS 切替 ----------------

    /// <summary>プロバイダ名から現在方言を解決する。未知の名前は SQL Server へフォールバックする</summary>
    /// <param name="dbms">解決するプロバイダ識別名</param>
    /// <param name="warnOnFallback"><c>true</c> かつ未知の名前だった場合に一度だけ警告を表示する</param>
    private IDatabaseProvider ResolveProvider(string dbms, bool warnOnFallback)
    {
        if (_providers.TryGet(dbms, out var provider))
        {
            return provider;
        }

        // 未知の方言は SQL Server として扱い、初回のみ警告を表示する
        if (warnOnFallback && !_fallbackWarningShown)
        {
            _fallbackWarningShown = true;
            _dialogs.ShowInformation(
                string.Format(Strings.Dbms_UnsupportedFallback, dbms),
                Strings.Dbms_Title
            );
        }

        return _providers.Get(SqlServerProvider.ProviderName);
    }

    /// <summary>読込・取込時に図の TargetDbms を現在方言へ反映する（型変換は行わない）</summary>
    /// <remarks>ファイル・DB から与えられた方言をそのまま採用し、UI の型候補・既定型を追随させる</remarks>
    private void SetCurrentProviderFromDbms(string dbms)
    {
        _currentProvider = ResolveProvider(dbms, warnOnFallback: true);
        RaiseProviderChanged();
    }

    /// <summary>方言変更に伴う派生プロパティの変更通知をまとめて発行する</summary>
    private void RaiseProviderChanged()
    {
        OnPropertyChanged(nameof(CurrentProvider));
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(AvailableDataTypes));

        // フィーチャーモジュール（DB 同期ボタンの活性・ツールチップ再評価など）へ方言切替を通知する
        TargetDbmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 現在の図のターゲット DBMS を切り替える。既存カラムの型をピボット変換し、
    /// 「TargetDbms 変更＋変換対象カラムの型変更」を単一の Undo 単位として適用する。
    /// </summary>
    /// <param name="target">切替先のプロバイダ</param>
    private void ChangeTargetDbms(IDatabaseProvider target)
    {
        // 同一方言なら何もしない
        if (ReferenceEquals(target, _currentProvider))
        {
            return;
        }

        var from = _currentProvider;
        var plan = DiagramTypeConverter.CreatePlan(
            ToDiagramModel(),
            from.TypeCatalog,
            target.TypeCatalog
        );

        // 変換対象カラムの ViewModel を ID で引けるよう索引化する
        var columnsById = Entities
            .SelectMany(entity => entity.Columns)
            .ToDictionary(column => column.Id);

        var command = new ChangeTargetDbmsCommand(
            from,
            target,
            plan.Converted,
            columnsById,
            applyProvider: SetProviderInternal
        );

        // 型変更が個別のプロパティ変更履歴として二重登録されないよう追跡を抑止して適用し、
        // 複合コマンドのみを 1 つの Undo 単位として履歴へ積む
        _changeTracker.RunWithoutTracking(command.Execute);
        UndoRedo.Push(command);

        // 変換できなかったカラムがあれば、導入文（message）と一覧（details）を分けて詳細ダイアログで提示する
        if (plan.Unconverted.Count > 0)
        {
            _dialogs.ShowInformationDetails(
                Strings.TypeConversion_WarningHeader,
                BuildUnconvertedColumnList(plan.Unconverted),
                Strings.TypeConversion_WarningTitle
            );
        }
    }

    /// <summary>Undo コマンドから方言を切り替えるための内部フック（派生通知も発行する）</summary>
    private void SetProviderInternal(IDatabaseProvider provider)
    {
        _currentProvider = provider;
        RaiseProviderChanged();
    }

    /// <summary>変換できなかったカラムの一覧（本文のみ・導入文は含めない）を整形する（上限超過分は省略）</summary>
    /// <remarks>導入文は呼び出し側が <see cref="Strings.TypeConversion_WarningHeader"/> を message に使う</remarks>
    private static string BuildUnconvertedColumnList(
        IReadOnlyList<ColumnTypeConversion> unconverted
    ) =>
        DialogItemList.Format(
            unconverted
                .Select(c =>
                    string.Format(
                        Strings.TypeConversion_ColumnLine,
                        c.TableName,
                        c.ColumnName,
                        c.OldType
                    )
                )
                .ToList(),
            Strings.Common_MoreItems
        );

    // ---------------- Collection changed handlers ----------------

    /// <summary>エンティティの増減に応じてイベント購読・変更追跡・表示設定の伝播を行う</summary>
    private void OnEntitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (EntityViewModel entity in e.OldItems)
            {
                entity.PropertyChanged -= OnEntityPropertyChanged;
                entity.UniqueConstraintMemberSelectionEdited -=
                    OnUniqueConstraintMemberSelectionEdited;
                _changeTracker.DetachEntity(entity);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (EntityViewModel entity in e.NewItems)
            {
                // 追加されたエンティティへ現在のツールバー表示設定を引き継ぐ
                entity.ShowDescriptionsInDiagram = ShowColumnDescriptionsInDiagram;
                entity.ShowNullabilityInDiagram = ShowNullabilityInDiagram;
                entity.IsCompactView = IsCompactViewInDiagram;
                entity.PropertyChanged += OnEntityPropertyChanged;

                // 一意制約カードのコンボボックス操作は VM 経由で届くため、ここで履歴化の入口へ結ぶ
                entity.UniqueConstraintMemberSelectionEdited +=
                    OnUniqueConstraintMemberSelectionEdited;
                _changeTracker.AttachEntity(entity);
            }
        }

        OnPropertyChanged(nameof(Entities));
        RefreshCanvasSize();

        // エンティティの増減により関連構成が変わるため、関連ハイライトを再計算する
        UpdateRelatedHighlights();

        // エンティティの増減でミニマップの射影・描画データを作り直す
        RecalculateMiniMap();
    }

    /// <summary>一意制約カードの構成列行で列が選び直されたときに、Undo 可能な差し替えとして確定させる</summary>
    private void OnUniqueConstraintMemberSelectionEdited(
        object? sender,
        UniqueConstraintMemberViewModel member
    ) => SetUniqueConstraintMemberCommand.Execute(member);

    /// <summary>リレーションの列ペア行で列が選び直されたときに、Undo 可能な差し替えとして確定させる</summary>
    private void OnRelationshipColumnPairSelectionEdited(
        object? sender,
        RelationshipColumnPairViewModel row
    ) => SetRelationshipColumnPairCommand.Execute(row);

    /// <summary>エンティティの位置・サイズ変更に追従してキャンバスサイズを更新する</summary>
    private void OnEntityPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(EntityViewModel.X)
                or nameof(EntityViewModel.Y)
                or nameof(EntityViewModel.Width)
                or nameof(EntityViewModel.DisplayHeight)
        )
        {
            RefreshCanvasSize();

            // エンティティの移動・サイズ変更（ドラッグ中含む）でミニマップの射影・描画データを作り直す
            RecalculateMiniMap();
        }
    }

    /// <summary>リレーションの増減に応じて端点購読・変更追跡を切り替え、カラムの PK/FK ルールを再適用する</summary>
    /// <remarks>
    /// <c>ObservableCollection.Clear()</c> は Reset 通知で <c>OldItems</c> を持たないため、
    /// 一括置換の経路（<see cref="ReplaceDiagram"/> / <c>ImportSchemaCommand</c>）は Clear の前に
    /// 明示的に <see cref="RelationshipViewModel.Detach"/> を呼ぶ（購読解除の取りこぼしを防ぐ）
    /// </remarks>
    private void OnRelationshipsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e
    )
    {
        if (e.OldItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.OldItems)
            {
                // 図から外れたリレーションの端点購読を解除する
                // （解除しないと削除済み VM が生きたエンティティのイベントから参照され続け、
                //   エンティティ移動のたびに孤児リレーションの幾何再計算・通知が走り続ける）
                relationship.Detach();
                relationship.ColumnPairSelectionEdited -= OnRelationshipColumnPairSelectionEdited;
                _changeTracker.DetachRelationship(relationship);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RelationshipViewModel relationship in e.NewItems)
            {
                // 図へ復帰したリレーションの端点購読を張り直す（Undo による削除取り消し・取込の Redo など）
                // 生成直後の VM は購読済みのため、この呼び出しは何もしない（二重購読ガード）
                relationship.Attach();

                // 列ペア行のコンボボックス操作は VM 経由で届くため、ここで履歴化の入口へ結ぶ
                relationship.ColumnPairSelectionEdited += OnRelationshipColumnPairSelectionEdited;
                _changeTracker.AttachRelationship(relationship);
            }
        }

        ApplyRelationshipColumnRules();
        OnPropertyChanged(nameof(Relationships));

        // リレーションの増減により関連構成が変わるため、関連ハイライトを再計算する
        UpdateRelatedHighlights();

        // リレーションの増減でミニマップの線データを作り直す
        RecalculateMiniMap();
    }
}
