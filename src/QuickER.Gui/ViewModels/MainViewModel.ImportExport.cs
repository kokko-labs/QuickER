using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Behaviors;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>MainViewModel の入出力機能を担う partial クラス</summary>
/// <remarks>
/// 自動保存・復元、文書の保存／読込と外部変更検知を担当する。エクスポート・インポートの
/// 実行本体は <see cref="DiagramExportService"/> / <see cref="DiagramImportService"/> へ抽出済みで、
/// ここはコマンドの委譲と <see cref="IDiagramTransferHost"/>（サービスへ貸す能力）の実装だけを持つ
/// </remarks>
public partial class MainViewModel : IDiagramTransferHost
{
    // ---------------- Export / Import command services ----------------

    /// <summary>エクスポートコマンドの実行サービス（初回使用時に遅延生成＝ダイアログ群の解決後）</summary>
    private DiagramExportService? _exportService;

    /// <summary>インポートコマンドの実行サービス（初回使用時に遅延生成）</summary>
    private DiagramImportService? _importService;

    /// <summary>エクスポートサービス（欠落告知のセッション 1 回状態を持つため VM と同寿命の単一インスタンス）</summary>
    private DiagramExportService ExportService => _exportService ??= new(this, _dialogs, _files);

    /// <summary>インポートサービス</summary>
    private DiagramImportService ImportService => _importService ??= new(this, _dialogs, _files);

    // ---------------- Auto-save / restore ----------------

    /// <summary>現在編集中のダイアグラムに紐付くファイルのフルパス（無題＝未保存のときは null）</summary>
    /// <remarks>
    /// <see cref="Open"/> と「名前を付けて保存」（上書き保存を含む初回保存）でのみ設定・変更する。
    /// インポート・図の置換・DB 取込・AI 生成ではパスを維持し（無題化しない）、新規作成でのみ null へ戻す。
    /// 保存コマンドの分岐（無ダイアログ上書き／保存ダイアログ）と外部変更検知（ステージ B）の基準になる。
    /// </remarks>
    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>最後に読込／上書き保存した時点のファイル内容の SHA-256（16 進・未保存時は null）</summary>
    /// <remarks>自動保存メタへ書き出し、外部変更検知（ステージ B）で現ファイルとの一致判定に用いる</remarks>
    private string? _lastKnownFileHash;

    /// <summary>最終読込／上書き保存時点の Undo 世代（この値と現在世代が異なればダーティ）</summary>
    private int _savedChangeGeneration;

    /// <summary>復元時に「未保存の変更あり」状態だったことを引き継ぐフラグ（世代比較に依らずダーティ扱いにする）</summary>
    private bool _restoredDirty;

    // ---------------- External change detection (Stage B) ----------------

    /// <summary>現在パスに紐付くファイルの外部変更を監視するサービス（無題のときは監視しない）</summary>
    private readonly DocumentFileWatcher _fileWatcher = new();

    /// <summary>
    /// 監視イベント（スレッドプール発火）を UI スレッドへマーシャリングするデリゲート。
    /// 既定は同期実行（ヘッドレステスト向け）。本番は <see cref="SetUiPost"/> で Dispatcher 実装へ差し替える。
    /// </summary>
    private Action<Action> _uiPost = action => action();

    /// <summary>
    /// ダーティ時に「このまま続行」を選んだ外部バージョンの内容ハッシュ。
    /// 同一内容での再確認を抑止する（次の別内容変更では null に戻り再確認する）。
    /// </summary>
    private string? _ignoredExternalHash;

    /// <summary>再読込経路で View への fit-to-window 要求を抑止するフラグ（ビューポート維持のため）</summary>
    private bool _suppressFitToWindow;

    /// <summary>テスト専用: 実 FileSystemWatcher の起動を止めるフラグ（外部変更は注入で検証するため）</summary>
    private bool _fileWatchingDisabled;

    /// <summary>
    /// テスト専用: 上書き保存の書き込み完了直後（内容ハッシュの記録より前）に差し込む処理。
    /// 「保存したファイルを外部プロセスが即座に書き換えた」競合窓を決定的に再現するための入口。
    /// </summary>
    /// <remarks>
    /// この窓は監視の一時停止中にあたるため、外部書き込みの FSW イベントは捨てられる。
    /// 実プロセスの割り込みはタイミング依存で再現できないので、窓そのものをシームとして開ける。
    /// </remarks>
    internal Action? AfterSaveWriteForTests { get; set; }

    /// <summary>ステータスバー左端に表示する一時通知のバッキングフィールド（既定は「準備完了」）</summary>
    private string _statusMessage = Strings.Status_Ready;

    /// <summary>直近の一時通知を既定表示へ戻すためのシングルショットタイマ</summary>
    private Timer? _statusRevertTimer;

    /// <summary>ステータスバー左端に表示するメッセージ（外部変更の控えめ通知に使う。既定は「準備完了」）</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>監視イベントを UI スレッドへ載せるデリゲートを差し替える（本番合成点＝View 側から設定する）</summary>
    /// <param name="uiPost">UI スレッドで指定処理を実行するデリゲート（Dispatcher.BeginInvoke 相当）</param>
    public void SetUiPost(Action<Action> uiPost)
    {
        _uiPost = uiPost ?? (action => action());
    }

    /// <summary>監視サービスの購読と期待ハッシュ供給を結線する（コンストラクタから 1 回だけ呼ぶ）</summary>
    private void InitializeFileWatcher()
    {
        // 発火時の内容ハッシュ比較に使う「最終既知ハッシュ」を都度供給する（自己書き込み抑制の第 1 段）
        _fileWatcher.ExpectedHashProvider = () => _lastKnownFileHash;
        _fileWatcher.FileChanged += OnDocumentFileChanged;
    }

    /// <summary>ステータスバーへ一時通知を表示し、数秒後に既定表示（準備完了）へ戻す</summary>
    private void NotifyStatus(string message)
    {
        StatusMessage = message;

        _statusRevertTimer?.Dispose();
        _statusRevertTimer = new Timer(
            _ => _uiPost(() => StatusMessage = Strings.Status_Ready),
            null,
            5000,
            Timeout.Infinite
        );
    }

    /// <summary>監視スレッドからの外部変更通知を UI スレッドへ載せ替えて処理する</summary>
    private void OnDocumentFileChanged(object? sender, DocumentFileChangedEventArgs e)
    {
        _uiPost(() => HandleDocumentFileChanged(e));
    }

    /// <summary>テスト専用: 外部変更イベントを注入し、UI ハンドラを（既定の同期 _uiPost で）実行する</summary>
    /// <param name="kind">変更種別</param>
    /// <param name="contentHash">内容ハッシュ（Modified のときのみ意味を持つ）</param>
    /// <param name="path">対象パス（省略時は現在パス）。パス突合ロジックの検証に用いる</param>
    internal void RaiseExternalChangeForTests(
        DocumentFileChangeKind kind,
        string? contentHash,
        string? path = null
    )
    {
        OnDocumentFileChanged(
            this,
            new DocumentFileChangedEventArgs(
                kind,
                path ?? CurrentFilePath ?? string.Empty,
                contentHash
            )
        );
    }

    /// <summary>外部変更を種別ごとに処理する（UI スレッド上で実行される）</summary>
    private void HandleDocumentFileChanged(DocumentFileChangedEventArgs e)
    {
        // パスがクリア／別ファイルへ変わった後に届いた遅延イベントは無視する
        if (
            string.IsNullOrEmpty(CurrentFilePath)
            || !string.Equals(e.Path, CurrentFilePath, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        switch (e.Kind)
        {
            case DocumentFileChangeKind.Deleted:
                // 削除は現状維持し通知のみ（監視は継続。次の再作成・Ctrl+S で復帰する）
                NotifyStatus(Strings.Status_ExternalFileDeleted);
                break;

            case DocumentFileChangeKind.Renamed:
                // 別名へのリネームも現状維持し通知のみ
                NotifyStatus(Strings.Status_ExternalFileRenamed);
                break;

            case DocumentFileChangeKind.Modified:
                HandleExternalModification(e.ContentHash);
                break;
        }
    }

    /// <summary>外部での内容変更を処理する（クリーンなら無確認再読込・ダーティなら確認）</summary>
    private void HandleExternalModification(string? contentHash)
    {
        // 進行中のマウス操作を、再読込・確認ダイアログのどちらへ進むより前に打ち切る。
        // 自動再読込では図が置き換わったあとの解放が幽霊 Undo を積み、確認ダイアログは
        // マウスキャプチャ中に開いて MouseUp を奪う（＝進行中状態が残る）ため、分岐の手前で止める
        CancelActiveCanvasInteractions();

        if (!IsDirty)
        {
            // クリーン（未保存変更なし）＝無確認で再読込し、控えめに通知する
            if (ReloadFromDisk())
            {
                NotifyStatus(Strings.Status_ExternalReloaded);
            }

            return;
        }

        // 同一内容で既に「続行」を選んでいれば再確認しない（次の別内容変更では再確認する）
        if (contentHash is not null && contentHash == _ignoredExternalHash)
        {
            return;
        }

        // ダーティ＝未保存変更があるため、破棄再読込か続行かをユーザーへ確認する
        if (_dialogs.ConfirmWarning(Strings.Confirm_ExternalChangeReload, Strings.Common_Confirm))
        {
            ReloadFromDisk();
        }
        else
        {
            // このバージョン（内容ハッシュ）は無視して編集を続行する
            _ignoredExternalHash = contentHash;
        }
    }

    /// <summary>進行中のキャンバス操作（移動・リサイズ・グループ移動・範囲選択）を開始時点へ戻して打ち切る</summary>
    /// <remarks>
    /// <para>
    /// 進行中状態はビヘイビアの静的フィールドが持つため（同時に操作できる対象は 1 つという前提）、
    /// インスタンスを介さずここから直接打ち切る。ビヘイビア側は既に ViewModel を直接参照しており
    /// （<see cref="ClampGroupDelta"/> / <see cref="OnEntityClicked"/>）、同一アセンブリ内の静的呼び出しに
    /// とどまるため新たな結線は増やさない。
    /// </para>
    /// <para>
    /// <b>既知の制限:</b> 打ち切るのはマウス操作だけで、機能モジュールが開いているモーダル
    /// （DB 同期・クエリ定義など）は閉じない。それらのダイアログは開いた時点の図を見ているため、
    /// 再読込後に確定すると古い内容を前提とした操作になり得る。
    /// </para>
    /// </remarks>
    private static void CancelActiveCanvasInteractions()
    {
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
    }

    /// <summary>現在パスのファイルを読み直し、履歴クリアで反映する（ビューポートは維持）</summary>
    /// <returns>再読込に成功した場合 true。破損・非文書・新フォーマットなどで現状維持した場合 false</returns>
    /// <remarks>
    /// 既存の読込フロー（<see cref="LoadDocumentIntoDiagram"/>）を流用するが、fit-to-window 要求を
    /// <see cref="_suppressFitToWindow"/> で抑止してズーム・スクロール位置を保つ。Guid 一致エンティティの
    /// レイアウトはファイル値を尊重し、欠落分のみ追記配置される（既存機構がそのまま効く）。
    /// </remarks>
    private bool ReloadFromDisk()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            return false;
        }

        // 破損（不正 JSON）・非 DiagramDocument は現状維持し、次の変更イベントで再試行する
        // （控えめな一時通知のため、ここでは失敗の原因までは見せない）
        if (
            !TryLoadDiagramDocument(CurrentFilePath, out var document, out _, out _)
            || document is null
        )
        {
            NotifyStatus(Strings.Status_ExternalReloadFailed);
            return false;
        }

        // 新フォーマット文書は未対応データを失う恐れがあるため自動反映しない（Open と同じ安全策）
        if (document.IsNewerFormat)
        {
            NotifyStatus(Strings.Status_ExternalReloadFailed);
            return false;
        }

        _suppressFitToWindow = true;

        try
        {
            SetCurrentProviderFromDbms(document.Schema.TargetDbms);
            LoadDocumentIntoDiagram(document);
        }
        finally
        {
            _suppressFitToWindow = false;
        }

        // 読み直した内容を最終既知として記録し、クリーン状態へ戻す
        UpdateDocumentIdentity(CurrentFilePath);
        _ignoredExternalHash = null;
        return true;
    }

    /// <summary>ファイルを DiagramDocument として妥当か検証したうえで読み込む（破損・非文書は false）</summary>
    /// <param name="path">読み込むファイルのフルパス</param>
    /// <param name="document">読み込んだ文書（失敗時は null）</param>
    /// <param name="kind">失敗の種別（成功時は <see cref="DocumentLoadError.None"/>）</param>
    /// <param name="error">
    /// 失敗の原因となった例外（IO エラー・不正 JSON・Id 重複）。形式検証で弾いた場合と成功時は null。
    /// 呼び出し側は「原因を持つ失敗」だけ他の失敗通知と同じく例外メッセージを連結して見せる。
    /// </param>
    /// <remarks>
    /// 検証は <see cref="JsonStorageService.TryLoad"/> に委ね、ここは種別と原因例外をそのまま
    /// 呼び出し側へ渡すだけを担う（種別ごとの文言は呼び出し側の責務）。
    /// </remarks>
    private static bool TryLoadDiagramDocument(
        string path,
        out DiagramDocument? document,
        out DocumentLoadError kind,
        out Exception? error
    )
    {
        error = null;
        kind = DocumentLoadError.None;

        try
        {
            if (JsonStorageService.TryLoad(path, out document, out kind, out var exception))
            {
                return true;
            }

            // IO エラー・不正 JSON・Id 重複は原因を持ち帰る（形式検証で弾いた場合は exception が null）
            error = exception;
            return false;
        }
        catch (Exception ex)
        {
            // TryLoad が分類し切れない想定外の失敗も現状維持で扱う（防御）
            document = null;
            kind = DocumentLoadError.InvalidJson;
            error = ex;
            return false;
        }
    }

    /// <summary>起動復元の直後に、現ファイルの内容が最終既知ハッシュと異なれば外部変更として扱う</summary>
    /// <remarks>
    /// 復元した作業状態（last_diagram.json）が現ファイルと乖離しているかを、記録済みハッシュと
    /// 現ファイルのハッシュ比較で判定する。相違があれば通常の変更検知と同じ規則
    /// （復元がクリーン→自動再読込・ダーティ→確認）を適用する。監視サービス起動前でも一度検査する。
    /// </remarks>
    private void CheckExternalChangeOnStartup() => CheckExternalChangeAgainstDisk();

    /// <summary>現ファイルの内容が最終既知ハッシュと異なれば、通常の外部変更検知と同じ規則で処理する</summary>
    /// <remarks>
    /// 監視サービスの通知が届かない（届かなかった）タイミングで、ディスクとの乖離を 1 回だけ拾い直す
    /// 共有経路。呼び出し元は起動復元の直後（<see cref="CheckExternalChangeOnStartup"/>）と、
    /// 上書き保存で監視を再開した直後（<see cref="SaveToPath"/>）の 2 箇所。
    /// </remarks>
    private void CheckExternalChangeAgainstDisk()
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
        {
            return;
        }

        var currentHash = DocumentContentHash.TryCompute(CurrentFilePath);

        // 算出不能（IO エラー）や一致（変更なし）は何もしない
        if (
            currentHash is null
            || string.Equals(currentHash, _lastKnownFileHash, StringComparison.Ordinal)
        )
        {
            return;
        }

        HandleExternalModification(currentHash);
    }

    /// <summary>最後に保存／読込した JSON のファイル名（拡張子なし。現在パスから導出する）</summary>
    /// <remarks>
    /// ウィンドウタイトルと印刷ダイアログのタイトル入力欄の初期値に使用する。
    /// 保存フォーマット・Undo 履歴には一切関与しない（無題のときは null）
    /// </remarks>
    public string? LastDocumentFileName =>
        string.IsNullOrEmpty(CurrentFilePath)
            ? null
            : Path.GetFileNameWithoutExtension(CurrentFilePath);

    /// <summary>
    /// 最終読込／上書き保存以降に未保存の変更があるか（読込直後・保存直後はクリーン）
    /// </summary>
    /// <remarks>
    /// Undo 世代の比較で判定する。Undo で保存時点の内容へ戻しても世代は進むため「変更あり」扱いになる
    /// （安全側）。復元時に未保存だった状態も引き継ぐ（<see cref="_restoredDirty"/>）。
    /// </remarks>
    public bool IsDirty => _restoredDirty || UndoRedo.ChangeGeneration != _savedChangeGeneration;

    /// <summary>図を丸ごと置き換えても失うものが無いか（エンティティ・名前付きクエリ・未保存変更のいずれも無い）</summary>
    /// <remarks>
    /// 図を置換する全経路（新規作成・開く・Mermaid/DBML 取込・Excel 定義書取込・DB 取込・コードリバース）が
    /// 「無確認で進めてよいか」を判定する単一の述語。エンティティ数だけで判定すると、クエリだけの図や
    /// 未保存の編集内容を無言で捨ててしまう（構造署名が一致する再取込でもクエリと手配置レイアウトは失われる）。
    /// ホスト契約の <c>IErDiagramHost.IsEmpty</c>（＝エンティティ 0）は AI チャットの自動整列判定に
    /// 使われており意味が異なるため、こちらを別の述語として持つ。
    /// </remarks>
    public bool HasNothingToLose => Entities.Count == 0 && Queries.Count == 0 && !IsDirty;

    /// <summary>ウィンドウタイトル（無題は「QuickER」、ファイル紐付きは「ファイル名 - QuickER」・ダーティ時は * 付き）</summary>
    /// <remarks>無題（パスなし）のダーティは * を付けず「QuickER」のままにする（保存先が無く * が意味を持たないため）</remarks>
    public string WindowTitle =>
        string.IsNullOrEmpty(LastDocumentFileName)
            ? "QuickER"
            : $"{LastDocumentFileName}{(IsDirty ? "*" : string.Empty)} - QuickER";

    /// <summary>ダイアグラム自動保存ファイルの既定パス（%LOCALAPPDATA%\QuickER\last_diagram.json）</summary>
    /// <remarks>
    /// 保存先がローミング（<c>%APPDATA%</c>）ではなくローカルなのは、この PC に固有の作業状態だから
    /// （理由の正本は <c>JsonSettingsStore</c> の既定コンストラクタ）。
    /// </remarks>
    private static readonly string DefaultAutoSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickER",
        "last_diagram.json"
    );

    /// <summary>ダイアグラム自動保存ファイルのパス（テストでは差し替える）</summary>
    private string _autoSavePath = DefaultAutoSavePath;

    /// <summary>GUI 全体設定（UI 表示状態・文書メタを含む）を gui-settings.json へ永続化するストア</summary>
    private GuiAppSettingsStore _guiSettingsStore = new();

    /// <summary>永続化先（設定ストア・自動保存ファイル）を差し替える（テスト専用。Initialize/AutoSave 前に呼ぶこと）</summary>
    internal void UsePersistenceForTests(GuiAppSettingsStore settingsStore, string autoSavePath)
    {
        _guiSettingsStore = settingsStore;
        _autoSavePath = autoSavePath;
    }

    /// <summary>現在パスが変わったら、そこから導出するタイトル関連プロパティと外部変更監視へ反映する</summary>
    partial void OnCurrentFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(LastDocumentFileName));
        OnPropertyChanged(nameof(WindowTitle));

        // 別文書へ切り替わったので、直前の「続行」による再確認抑止はリセットする
        _ignoredExternalHash = null;

        // テストでは実 FileSystemWatcher を起動しない（外部変更は注入で検証する）
        if (_fileWatchingDisabled)
        {
            return;
        }

        // 無題（パスなし）は監視しない。ファイルに紐付いたらそのファイルを監視する
        if (string.IsNullOrEmpty(value))
        {
            _fileWatcher.Stop();
        }
        else
        {
            _fileWatcher.Watch(value);
        }
    }

    /// <summary>テスト専用: 実 FileSystemWatcher の起動を止める（外部変更は <see cref="RaiseExternalChangeForTests"/> で注入する）</summary>
    internal void DisableFileWatchingForTests()
    {
        _fileWatchingDisabled = true;
        _fileWatcher.Stop();
    }

    /// <summary>Undo 世代が動いたらダーティ／タイトルを再評価する（コンストラクタで購読する）</summary>
    private void OnUndoRedoStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoRedo.ChangeGeneration))
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>現在の Undo 世代をクリーン基準として記録し、ダーティ／タイトルを更新する</summary>
    private void MarkClean()
    {
        _restoredDirty = false;
        _savedChangeGeneration = UndoRedo.ChangeGeneration;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>読込／保存の成功時に現在パス・内容ハッシュを更新し、クリーン状態にする共通ヘルパ</summary>
    /// <param name="path">紐付けるファイルのフルパス（読込元／保存先）</param>
    /// <remarks>内容ハッシュはディスクから読み直して求める（読込経路・再読込経路はこれが正）</remarks>
    private void UpdateDocumentIdentity(string path) =>
        UpdateDocumentIdentity(path, TryComputeContentHash(path));

    /// <summary>内容ハッシュを明示して文書アイデンティティを更新する</summary>
    /// <param name="path">紐付けるファイルのフルパス</param>
    /// <param name="contentHash">最終既知として記録する内容ハッシュ（算出不能なら null）</param>
    /// <remarks>
    /// 保存経路は「書き出した内容そのもの」から求めたハッシュを渡す
    /// （理由の正本は <see cref="DocumentContentHash.ComputeForText"/>）。
    /// </remarks>
    private void UpdateDocumentIdentity(string path, string? contentHash)
    {
        CurrentFilePath = path;
        _lastKnownFileHash = contentHash;
        MarkClean();
    }

    /// <summary>ファイル内容の SHA-256（16 進）を計算する（IO エラー時は null）</summary>
    /// <remarks>監視サービスと同じ算出規則を共有するため <see cref="DocumentContentHash"/> へ委譲する</remarks>
    private static string? TryComputeContentHash(string path) =>
        DocumentContentHash.TryCompute(path);

    /// <summary>現在のダイアグラムと UI 表示状態・文書メタを自動保存ファイルへ書き出す</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(_autoSavePath)!;
            Directory.CreateDirectory(dir);

            // 復旧ファイルはアトミックに書き換える。クラッシュ時の緊急保存もこの経路を通るため、
            // 書き込み途中で落ちても直前の自動保存内容が壊れずに残る
            JsonStorageService.SaveAtomic(_autoSavePath, ToDocument());

            // UI 表示状態・文書メタは GUI 全体設定の各セクション。他のセクション（言語など）を消さないよう
            // Load → 該当セクションのみ差し替え → Save の read-modify-write で書き込む
            var settings = _guiSettingsStore.Load();
            settings.DiagramView = new DiagramViewSettings
            {
                ShowColumnDescriptions = ShowColumnDescriptionsInDiagram,
                ShowNullability = ShowNullabilityInDiagram,
                IsCompactView = IsCompactViewInDiagram,
            };
            settings.Panels = new PanelVisibilitySettings
            {
                IsToolboxVisible = IsToolboxVisible,
                IsPropertyPanelVisible = IsPropertyPanelVisible,
            };
            settings.CurrentDocument = new CurrentDocumentSettings
            {
                FilePath = CurrentFilePath,
                LastKnownHash = _lastKnownFileHash,
                IsDirty = IsDirty,
            };
            _guiSettingsStore.Save(settings);
        }
        catch
        {
            // 自動保存の失敗は操作を妨げないため無視する
        }
    }

    /// <summary>クラッシュ時に、現在の編集内容を復旧用の自動保存ファイルへ緊急退避する</summary>
    /// <remarks>
    /// 中身は通常の <see cref="AutoSave"/>（失敗は握り潰す）。壊れた状態から呼ばれても
    /// 例外を外へ出さない契約であることを呼び出し側（クラッシュハンドラ）へ明示するための公開口。
    /// </remarks>
    public void TryEmergencyAutoSave() => AutoSave();

    /// <summary>起動時に前回の自動保存ファイルから UI 状態・ダイアグラム・文書メタを復元する</summary>
    private void RestoreLastDiagram()
    {
        // UI 表示状態を GUI 全体設定から反映する（ファイル無し・破損時は既定値が返り、
        // その既定値は VM 側の初期値と一致するため常時反映しても挙動は変わらない）
        var settings = _guiSettingsStore.Load();
        var diagramView = settings.DiagramView;
        ShowColumnDescriptionsInDiagram = diagramView.ShowColumnDescriptions;
        ShowNullabilityInDiagram = diagramView.ShowNullability;
        IsCompactViewInDiagram = diagramView.IsCompactView;

        var panels = settings.Panels;
        IsToolboxVisible = panels.IsToolboxVisible;
        IsPropertyPanelVisible = panels.IsPropertyPanelVisible;

        if (!File.Exists(_autoSavePath))
        {
            return;
        }

        try
        {
            // 復元も形式検証込みの経路を通す。手編集・外部の書き換えで Id が重複した作業状態を
            // 復元すると、以後の上書き保存も自動保存も失敗し続ける（＝作業を保存する手段が消える）
            if (
                !JsonStorageService.TryLoad(_autoSavePath, out var document, out _, out _)
                || document is null
            )
            {
                return;
            }

            SetCurrentProviderFromDbms(document.Schema.TargetDbms);
            LoadDocumentIntoDiagram(document);

            // 文書メタ（紐付くファイルパス・最終既知ハッシュ・ダーティ）を復元する。
            // 作業状態は自動保存ファイルが正のため、ハッシュは再計算せず記録値をそのまま引き継ぐ
            // （ステージ B の外部変更判定で現ファイルと比較するのは復元したこの値）。
            var meta = settings.CurrentDocument;
            CurrentFilePath = meta.FilePath;
            _lastKnownFileHash = meta.LastKnownHash;
            MarkClean();

            // 前回終了時に未保存だった場合はダーティ状態を引き継ぐ（タイトルへ * を再現する）
            if (meta.IsDirty)
            {
                _restoredDirty = true;
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
        catch
        {
            // 復元失敗時は空のダイアグラムで起動する
        }
    }

    // ---------------- Export ----------------

    /// <summary>保存ダイアログで選択した形式に応じて ER 図を書き出す（実行本体は <see cref="DiagramExportService"/>）</summary>
    /// <param name="visual">PNG 出力時に使用するキャンバスの Visual</param>
    [RelayCommand]
    private void ExportDiagram(object? visual) => ExportService.Export(visual);

    /// <summary>図全体を用紙 1 ページへ印刷する（縮小フィット／原寸大を選択）</summary>
    /// <remarks>
    /// 図はキャンバスの Visual を写すのではなく、VM から直接ベクタ描画する
    /// （<see cref="DiagramPrintService"/> → <see cref="DiagramVectorRenderer"/>）。
    /// 選択枠・減光など画面状態の影響を受けないため、キャンバス参照の受け渡しや
    /// IsSelected / IsDimmed のスナップショット・復元は不要
    /// </remarks>
    [RelayCommand]
    private void PrintDiagram()
    {
        // 印刷オプション（サイズモード・タイトル・日時印字）を選択させる。キャンセル時は何もしない
        // タイトル欄の初期値には最後に保存／読込した文書名を提示する
        var options = _appDialogs.ShowPrintOptionsDialog(LastDocumentFileName);

        if (options is null)
        {
            return;
        }

        try
        {
            DiagramPrintService.Print(
                this,
                options.Title,
                options.IncludeTimestamp,
                options.SizeMode
            );
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Print_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>現在の ER 図を意味モデル（<see cref="ErDiagram"/>・視覚情報なし）へ変換する</summary>
    /// <remarks>名前付きクエリ定義（<see cref="Queries"/>）も保存単位として含める</remarks>
    public ErDiagram ToDiagramModel() =>
        new()
        {
            Entities = Entities.Select(entity => entity.ToModel()).ToList(),
            Relationships = Relationships.Select(relationship => relationship.ToModel()).ToList(),
            TargetDbms = CurrentProvider.Name,
            // クエリ一覧は独立コピー（新リスト）として渡す。フィーチャーモジュール（AI チャットのクエリツール）は
            // 取得した図の Queries を直接 add/remove/置換して更新し、成功時のみ ReplaceQueries で VM へ書き戻す。
            // 同一参照を返すと検証途中の破壊的変更が VM の実体へ漏れる（QueryDefinition 自体は不変運用のため浅いコピーで十分）。
            Queries = Queries.ToList(),
        };

    /// <summary>現在の ER 図を保存文書（意味モデル＋レイアウトサイドカー）へ変換する</summary>
    public DiagramDocument ToDocument() =>
        new()
        {
            Schema = ToDiagramModel(),
            Layout = Entities.ToDictionary(entity => entity.Id, entity => entity.ToLayout()),
        };

    /// <summary>ファイル選択ダイアログで選択したファイルの形式に応じて ER 図を取り込む（実行本体は <see cref="DiagramImportService"/>）</summary>
    [RelayCommand]
    private void ImportDiagram() => ImportService.Import();

    // ---------------- IDiagramTransferHost（入出力サービスへ貸す能力） ----------------

    /// <inheritdoc />
    ErDiagram IDiagramTransferHost.BuildModel() => ToDiagramModel();

    /// <inheritdoc />
    IDatabaseProvider IDiagramTransferHost.CurrentProvider => CurrentProvider;

    /// <inheritdoc />
    bool IDiagramTransferHost.IsDirty => IsDirty;

    /// <inheritdoc />
    bool IDiagramTransferHost.HasNothingToLose => HasNothingToLose;

    /// <inheritdoc />
    int IDiagramTransferHost.QueryCount => Queries.Count;

    /// <inheritdoc />
    void IDiagramTransferHost.RenderSvg(string path) => ImageExportService.ExportSvg(this, path);

    /// <inheritdoc />
    void IDiagramTransferHost.ReplaceWholesale(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    ) => ReplaceDiagramWithoutHistory(entities, relationships, autoLayout: true);

    /// <inheritdoc />
    void IDiagramTransferHost.ReplaceMerged(ErDiagram diagram) => ReplaceDiagramFromModule(diagram);

    // ---------------- Save / Load ----------------

    /// <summary>現在パスがあれば無ダイアログで上書き保存し、無ければ保存ダイアログを表示する</summary>
    [RelayCommand]
    private void Save()
    {
        // ファイルに紐付いていれば、確認・ダイアログなしでその場所へ上書き保存する
        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            SaveToPath(CurrentFilePath);
            return;
        }

        // 無題（未保存）なら保存ダイアログで保存先を選ばせる
        SaveWithDialog();
    }

    /// <summary>保存ダイアログで保存先を選び、常に別名として保存する（現在パスを更新する）</summary>
    [RelayCommand]
    private void SaveAs() => SaveWithDialog();

    /// <summary>保存ダイアログでパスを選び、現在のダイアグラムを JSON 形式で保存して現在パスを更新する</summary>
    private void SaveWithDialog()
    {
        var picked = _files.PickSaveFile(
            "ER Diagram (*.json)|*.json",
            ".json",
            LastDocumentFileName
        );

        if (picked is null)
        {
            return;
        }

        SaveToPath(picked.Path);
    }

    /// <summary>指定パスへ現在の文書を保存し、内容ハッシュ更新までを自己書き込み抑止の下で行う</summary>
    /// <remarks>
    /// 書き込み前後で監視を一時停止し、自分の保存が外部変更として跳ね返らないようにする
    /// （<see cref="DocumentFileWatcher.ExpectedHashProvider"/> のハッシュ比較と二重の抑止）。
    /// 書き込みは <see cref="JsonStorageService.SaveAtomic"/>（一時ファイル経由の差し替え）で行い、
    /// 途中で落ちてもユーザーのファイルが半端な JSON にならないようにする。失敗時は通知のうえ
    /// 文書アイデンティティ（現在パス・ハッシュ・クリーン状態）を更新せず、ダーティのまま保持する。
    /// <para>
    /// 競合窓への対処は 2 段構え。(1) 最終既知ハッシュは<b>書き出した内容そのもの</b>から求める
    /// （ディスクの読み直しでは、書き込み完了から採取までの隙間に外部プロセスが書いた内容を
    /// 「自分が保存した内容」として記録してしまい、以後どの検知経路も差分を見つけられなくなる）。
    /// (2) 監視の再開直後にディスクと 1 回だけ突き合わせる（一時停止中に届いた変更イベントは
    /// 捨てられ、デバウンスも再スケジュールされないため、拾い直す経路をここに置く）。
    /// </para>
    /// <para>
    /// 書き込みの前には <see cref="ConfirmOverwriteBeforeSave"/> で「上書きして失うものが無いか」を
    /// ディスクの現状から確かめる（保存後の再照合は自分が書いた内容と一致するため、外部の内容を
    /// 黙って上書きしたことは後からでは分からない）。
    /// </para>
    /// </remarks>
    private void SaveToPath(string path)
    {
        // 書き込みを始める前に、上書きで失うものが無いかをディスクの現状から確かめる
        if (!ConfirmOverwriteBeforeSave(path))
        {
            return;
        }

        _fileWatcher.Suspend();
        var saved = false;

        try
        {
            var contents = JsonStorageService.SaveAtomic(path, ToDocument());
            AfterSaveWriteForTests?.Invoke();
            UpdateDocumentIdentity(path, DocumentContentHash.ComputeForText(contents));
            saved = true;

            // ER 図ファイル自身の読み書きは、作業を止めないステータスバーの一時通知で知らせる
            // （外部形式との入出力＝モーダル、との使い分け。ファイル名はタイトルバーに出るため入れない）
            NotifyStatus(Strings.Status_Saved);
        }
        catch (Exception ex)
        {
            // 保存できていないのにクリーン扱いにすると編集内容を失う。ダーティのまま通知して再保存へ促す
            _dialogs.ShowError(
                Strings.Save_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
        finally
        {
            _fileWatcher.Resume();
        }

        if (saved)
        {
            // 監視停止中に外部が書いていれば、ここで初めて検知できる（再開後の防御の二重化）
            CheckExternalChangeAgainstDisk();
        }
    }

    /// <summary>上書きで失うものが無いかをディスクの現状から確かめ、保存へ進んでよいかを返す</summary>
    /// <param name="path">保存先のフルパス</param>
    /// <returns>保存へ進んでよい場合 true（キャンセルされた場合は false＝保存しない）</returns>
    /// <remarks>
    /// <para>
    /// 判定材料は<b>すべてディスク上のファイル</b>から採り、VM に状態を持たない。将来版を開いたことを
    /// フラグで覚える方式では、(1) 図を丸ごと置き換えても現在パスは遷移しない（DB 取込・AI 生成は
    /// パスを維持する）ため置換後の保存で確認が消え、(2) 起動時の作業状態復元でフラグが戻らず、
    /// (3) 別セッション・別プロセスが将来版で書いたファイルには最初から効かない。ディスクを見る方式は
    /// この 3 つを同じ 1 つの判定で塞ぐ。
    /// </para>
    /// <para>
    /// 規則は 2 つで、効く範囲が違う。
    /// <list type="number">
    /// <item><b>将来版の書き戻し</b>は保存先がどのファイルでも確認する（相手のファイルにだけ残っている
    /// 未対応データを、この版の形式で書き潰すため。判定は
    /// <see cref="JsonStorageService.IsNewerFormatFile"/>＝逆直列化を通さない軽い読み取り）。</item>
    /// <item><b>外部変更の上書き</b>は「いま開いているファイル」のときだけ確認する（最終既知ハッシュと
    /// いう比較対象を持つのがその 1 つだけのため）。クリーン時の自動再読込は破損 JSON・将来版・
    /// 型不一致では失敗して一時通知を残すだけで、文書はクリーン・最終既知ハッシュは旧値のまま残る。
    /// ここで確かめないと、その次の上書き保存が外部の新しい内容を黙って消す（保存後の再照合は
    /// 自分が書いた内容と一致するため検知できない）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 確認は<b>多くとも 1 回</b>。両方立つときは失うものが大きい将来版の文言だけを出す
    /// （確認を 2 つ続けて出すと、どちらも「はい」で流される形骸化した確認になるため）。
    /// </para>
    /// <para>
    /// 照合してから書き込むまでの隙間に外部が書く余地（TOCTOU）は残るが、保存後の再照合
    /// （<see cref="CheckExternalChangeAgainstDisk"/>）がその窓を拾うため許容する。
    /// </para>
    /// </remarks>
    private bool ConfirmOverwriteBeforeSave(string path)
    {
        // 新規作成される保存先は誰の内容も消さない（版も読めない）
        if (!File.Exists(path))
        {
            return true;
        }

        if (JsonStorageService.IsNewerFormatFile(path))
        {
            return _dialogs.ConfirmWarning(
                Strings.Confirm_OverwriteNewerFormat,
                Strings.Common_Confirm
            );
        }

        // 外部変更の照合は「いま開いているファイル」限定（別ファイルには比較すべき最終既知ハッシュが無い）
        if (!IsCurrentDocumentPath(path) || !HasDiskDivergedFromLastKnown(path))
        {
            return true;
        }

        return _dialogs.ConfirmWarning(
            Strings.Confirm_OverwriteExternalChange,
            Strings.Common_Confirm
        );
    }

    /// <summary>指定パスが現在紐付いている文書と同じファイルか（フルパス正規化・大文字小文字を区別しない）</summary>
    /// <remarks>
    /// 「名前を付けて保存」で同じファイルを選び直した場合も上書きになるため、コマンドではなくパスで判定する。
    /// </remarks>
    private bool IsCurrentDocumentPath(string path) =>
        !string.IsNullOrEmpty(CurrentFilePath)
        && string.Equals(
            NormalizePath(path),
            NormalizePath(CurrentFilePath),
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>パスをフルパスへ正規化する（正規化できないパスは素の文字列を返す）</summary>
    /// <remarks>
    /// 不正なパスでも例外を出さない（この後の書き込みが失敗として扱う＝保存経路の例外処理へ委ねる）。
    /// </remarks>
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
            when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>ディスク上の内容が、最終既知ハッシュとも「続行」で無視したハッシュとも違うか</summary>
    /// <remarks>
    /// ファイルが無い（削除された）場合と算出不能（IO エラー）の場合は保存を妨げない
    /// ＝前者は新規作成と同じで、後者はここで止めても状況が良くならない。
    /// </remarks>
    private bool HasDiskDivergedFromLastKnown(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var currentHash = DocumentContentHash.TryCompute(path);

        if (currentHash is null)
        {
            return false;
        }

        return !string.Equals(currentHash, _lastKnownFileHash, StringComparison.Ordinal)
            && !string.Equals(currentHash, _ignoredExternalHash, StringComparison.Ordinal);
    }

    /// <summary>JSON ファイルからダイアグラムを読み込み、現在の図と置換する（ダイアログ表示）</summary>
    /// <remarks>
    /// 読込は現在の図を丸ごと置き換えるため、失うものがあるときは他の置換経路と同じ確認
    /// （<see cref="DialogServiceExtensions.ConfirmDiscard"/>）を通す。読込自体は
    /// <see cref="TryLoadDiagramDocument"/> 経由とし、破損 JSON・無関係な JSON・IO 失敗では
    /// 現在の図も現在パスも一切変えずに通知するだけにする（無言の全消し・別ファイルへの誤紐付けを防ぐ）。
    /// </remarks>
    [RelayCommand]
    private void Open()
    {
        var picked = _files.PickOpenFile("ER Diagram (*.json)|*.json");

        if (picked is null)
        {
            return;
        }

        // 失うものがない（空・クエリなし・クリーン）ときは無確認で開く
        if (
            !HasNothingToLose
            && !_dialogs.ConfirmDiscard(
                IsDirty,
                Strings.Confirm_OpenDiagram,
                Strings.Common_Confirm
            )
        )
        {
            return;
        }

        // 破損 JSON・非 DiagramDocument JSON・Id 重複・IO 失敗は現状維持のうえ通知する
        if (
            !TryLoadDiagramDocument(picked.Path, out var document, out var kind, out var error)
            || document is null
        )
        {
            // 原因を持つ失敗（IO エラー・不正 JSON・Id 重複）は他の失敗通知と同じ流儀で例外メッセージを
            // 連結する。形式検証で弾いた場合は例外が無いため、本文が挙げる原因候補だけを示す。
            // Id 重複だけは復旧手段が JSON の手編集しかないため、そう案内する専用の文言を使う
            var message =
                kind == DocumentLoadError.DuplicateId
                    ? string.Format(Strings.Open_DuplicateId, picked.Path)
                    : string.Format(Strings.Open_Failed, picked.Path);

            if (error is not null)
            {
                message += Environment.NewLine + error.Message;
            }

            _dialogs.ShowError(message, Strings.Common_Error);
            return;
        }

        // 新しいフォーマットの文書は未対応のデータが失われる可能性があるため、開く前に確認する
        if (document.IsNewerFormat)
        {
            var message = string.Format(
                Strings.Confirm_NewerDocumentFormat,
                document.Version,
                DiagramDocument.CurrentVersion
            );

            if (!_dialogs.ConfirmWarning(message, Strings.Common_Confirm))
            {
                return;
            }
        }

        SetCurrentProviderFromDbms(document.Schema.TargetDbms);
        LoadDocumentIntoDiagram(document);

        // 読込したファイルを現在パスとして紐付け、内容ハッシュを記録してクリーン状態にする
        UpdateDocumentIdentity(picked.Path);

        // 保存と同じく ER 図ファイル自身の読み書きなので、ステータスバーの一時通知で知らせる
        NotifyStatus(Strings.Status_Opened);
    }

    /// <summary>読み込んだ文書を現在の図へ反映する（配置なし文書は全体を自動整列する）</summary>
    /// <remarks>
    /// 保存された配置（layout）があればそれを復元し、layout が欠落または空（スキーマのみ JSON
    /// ＝配置なしエクスポート／レガシー空 layout）のときはエンティティが 1 件以上あれば全体を
    /// 自動整列する。これによりスキーマのみ形式でエクスポートしたファイルもそのまま開ける（可逆）。
    /// 部分欠落（一部エンティティのみ layout がない＝外部ツールがエンティティだけ追記した文書など）は、
    /// layout を持つ既存エンティティを一切動かさず、欠落分のみを空き領域へ追記配置する（<see cref="AutoLayoutService.LayoutAppend"/>）。
    /// </remarks>
    private void LoadDocumentIntoDiagram(DiagramDocument document)
    {
        if (HasNoLayout(document) && document.Schema.Entities.Count > 0)
        {
            ReplaceDiagramWithoutHistory(
                document.Schema.Entities,
                document.Schema.Relationships,
                autoLayout: true,
                document.Schema.Queries
            );
            return;
        }

        ReplaceDiagram(
            document.Schema.Entities,
            document.Schema.Relationships,
            clearUndoHistory: true,
            document.Layout,
            document.Schema.Queries
        );

        // 部分欠落: layout を持たないエンティティのみ、既存配置を保ったまま空き領域へ追記配置する
        ArrangeEntitiesMissingLayout(document.Layout);
    }

    /// <summary>layout に含まれないエンティティのみを空き領域へ追記配置する（既存＝layout 保有分は不動）</summary>
    /// <remarks>
    /// 欠落分は <see cref="ReplaceDiagram"/> で既定レイアウト（原点・既定幅）が割り当てられているため、
    /// まず幅を内容に合わせて自動調整してから、layout 保有分を固定群として <see cref="AutoLayoutService.LayoutAppend"/>
    /// で空き領域へ格子配置する。全欠落は呼び出し側で自動整列済みのためここには来ない。
    /// </remarks>
    private void ArrangeEntitiesMissingLayout(IReadOnlyDictionary<Guid, EntityLayout>? layout)
    {
        // layout が null（全欠落）は上流で処理済み。ここは部分欠落のみを扱う
        if (layout is null)
        {
            return;
        }

        var missing = Entities.Where(entity => !layout.ContainsKey(entity.Id)).ToList();

        if (missing.Count == 0)
        {
            return;
        }

        var placed = Entities.Where(entity => layout.ContainsKey(entity.Id)).ToList();

        // 追記配置は Undo 対象外（読込直後の初期配置）。位置変更が履歴へ積まれないよう追跡を抑止する
        _changeTracker.RunWithoutTracking(() =>
        {
            AutoFitEntityWidths(missing);
            AutoLayoutService.LayoutAppend(placed, missing, Relationships);
            RefreshCanvasSize();
        });
    }

    /// <summary>文書が配置情報（layout）を持たない（null または空）かどうかを判定する</summary>
    private static bool HasNoLayout(DiagramDocument document) =>
        document.Layout is null or { Count: 0 };

    /// <summary>外部変更監視サービスと一時通知タイマを破棄する</summary>
    /// <remarks>DI シングルトンとして生成されるため、コンテナ破棄時に呼ばれる（FileSystemWatcher の解放）</remarks>
    public void Dispose()
    {
        _fileWatcher.FileChanged -= OnDocumentFileChanged;
        _fileWatcher.Dispose();
        _statusRevertTimer?.Dispose();
        _statusRevertTimer = null;
    }
}
