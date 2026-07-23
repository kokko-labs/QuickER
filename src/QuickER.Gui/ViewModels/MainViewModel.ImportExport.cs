using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>ER 図のエクスポート形式</summary>
internal enum DiagramExportFormat
{
    /// <summary>PNG 画像</summary>
    Png,

    /// <summary>SVG 画像</summary>
    Svg,

    /// <summary>SQL（DDL）スクリプト</summary>
    Sql,

    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,

    /// <summary>HTML テーブル定義書</summary>
    Html,

    /// <summary>スキーマのみ JSON（配置情報なし・再取込可能）</summary>
    SchemaJson,
}

/// <summary>ER 図のインポート形式</summary>
internal enum DiagramImportFormat
{
    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,
}

/// <summary>MainViewModel の入出力機能を担う partial クラス</summary>
/// <remarks>
/// 自動保存・復元、各種フォーマットのエクスポート・インポート、SQL Server 連携、
/// AI 生成、C# コード生成のコマンドを担当する
/// </remarks>
public partial class MainViewModel
{
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
        if (!TryLoadDiagramDocument(CurrentFilePath, out var document) || document is null)
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
    /// <remarks>
    /// <see cref="JsonStorageService.Load"/> は無関係な JSON も「空図」として読めてしまうため、
    /// ルートが JSON オブジェクトで <c>Version</c>・<c>Schema</c> キーを持つことを検証してから読み込む
    /// （<see cref="JsonStorageService"/> の読込仕様に合わせ大文字小文字を区別する）。
    /// </remarks>
    private static bool TryLoadDiagramDocument(string path, out DiagramDocument? document)
    {
        document = null;

        try
        {
            var text = File.ReadAllText(path);
            var root = System.Text.Json.Nodes.JsonNode.Parse(text);

            if (
                root is not System.Text.Json.Nodes.JsonObject obj
                || obj["Version"] is null
                || obj["Schema"] is not System.Text.Json.Nodes.JsonObject
            )
            {
                return false;
            }

            document = JsonStorageService.Load(path);
            return true;
        }
        catch
        {
            // IO エラー・不正 JSON は現状維持（呼び出し側が通知する）
            return false;
        }
    }

    /// <summary>起動復元の直後に、現ファイルの内容が最終既知ハッシュと異なれば外部変更として扱う</summary>
    /// <remarks>
    /// 復元した作業状態（last_diagram.json）が現ファイルと乖離しているかを、記録済みハッシュと
    /// 現ファイルのハッシュ比較で判定する。相違があれば通常の変更検知と同じ規則
    /// （復元がクリーン→自動再読込・ダーティ→確認）を適用する。監視サービス起動前でも一度検査する。
    /// </remarks>
    private void CheckExternalChangeOnStartup()
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

    /// <summary>ウィンドウタイトル（無題は「QuickER」、ファイル紐付きは「ファイル名 - QuickER」・ダーティ時は * 付き）</summary>
    /// <remarks>無題（パスなし）のダーティは * を付けず「QuickER」のままにする（保存先が無く * が意味を持たないため）</remarks>
    public string WindowTitle =>
        string.IsNullOrEmpty(LastDocumentFileName)
            ? "QuickER"
            : $"{LastDocumentFileName}{(IsDirty ? "*" : string.Empty)} - QuickER";

    /// <summary>ダイアグラム自動保存ファイルの既定パス（%APPDATA%\QuickER\last_diagram.json）</summary>
    private static readonly string DefaultAutoSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
    private void UpdateDocumentIdentity(string path)
    {
        CurrentFilePath = path;
        _lastKnownFileHash = TryComputeContentHash(path);
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
            JsonStorageService.Save(_autoSavePath, ToDocument());

            // UI 表示状態・文書メタは GUI 全体設定の各セクション。他のセクション（言語など）を消さないよう
            // Load → 該当セクションのみ差し替え → Save の read-modify-write で書き込む
            var settings = _guiSettingsStore.Load();
            settings.DiagramView = new DiagramViewSettings
            {
                ShowColumnDescriptions = ShowColumnDescriptionsInDiagram,
                ShowNullability = ShowNullabilityInDiagram,
                IsCompactView = IsCompactViewInDiagram,
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

        if (!File.Exists(_autoSavePath))
        {
            return;
        }

        try
        {
            var document = JsonStorageService.Load(_autoSavePath);

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

    /// <summary>保存ダイアログで選択した形式に応じて ER 図を書き出す</summary>
    /// <param name="visual">PNG 出力時に使用するキャンバスの Visual</param>
    [RelayCommand]
    private void ExportDiagram(object? visual)
    {
        // 並び順は「画像 → DB 構築 → スキーマ交換（可逆な Schema JSON を先頭）→ 定義書」の用途グループ。
        // 標準ダイアログのフィルタは見出し行を持てないため、接頭辞（Image/Database/Schema/Document）で
        // グループを可視化する。先頭＝既定形式（PNG）は従来どおり
        var picked = _files.PickSaveFile(
            "Image - PNG (*.png)|*.png|Image - SVG (*.svg)|*.svg|Database - SQL Script (*.sql)|*.sql|Schema - JSON (*.json)|*.json|Schema - Mermaid (*.mmd)|*.mmd|Schema - Mermaid (*.mermaid)|*.mermaid|Schema - DBML (*.dbml)|*.dbml|Document - Excel Workbook (*.xlsx)|*.xlsx|Document - HTML (*.html)|*.html",
            ".png"
        );

        if (picked is null)
        {
            return;
        }

        var format = GetExportFormat(picked.Path, picked.FilterIndex);

        try
        {
            SaveDiagram(format, picked.Path, visual);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Export_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

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

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを署名比較で判定する</summary>
    private bool HasSameStructure(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var current = ToDiagramModel();
        var currentSignature = SchemaSignature.Compute(current.Entities, current.Relationships);
        var newSignature = SchemaSignature.Compute(entities, relationships);

        return currentSignature == newSignature;
    }

    /// <summary>構造変更を伴う置換の場合のみ確認ダイアログを表示する</summary>
    /// <remarks>空の図、または構造が同一の場合は確認なしで続行する</remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships,
        string message
    )
    {
        if (Entities.Count == 0 || HasSameStructure(entities, relationships))
        {
            return true;
        }

        return _dialogs.Confirm(message, Strings.Common_Confirm);
    }

    /// <summary>ファイル選択ダイアログで選択したファイルの形式に応じて ER 図を取り込む</summary>
    [RelayCommand]
    private void ImportDiagram()
    {
        var picked = _files.PickOpenFile(
            "Mermaid Diagram (*.mmd;*.mermaid)|*.mmd;*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx"
        );

        if (picked is null)
        {
            return;
        }

        var format = GetImportFormat(picked.Path, picked.FilterIndex);

        try
        {
            ImportDiagramFile(format, picked.Path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Import_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>指定形式でダイアグラムをファイルへ書き出し、完了を通知する</summary>
    private void SaveDiagram(DiagramExportFormat format, string path, object? visual)
    {
        var displayName = format switch
        {
            DiagramExportFormat.Png => Strings.ExportFormat_Png,
            DiagramExportFormat.Svg => Strings.ExportFormat_Svg,
            DiagramExportFormat.Sql => "SQL DDL",
            DiagramExportFormat.Mermaid => "Mermaid",
            DiagramExportFormat.Dbml => "DBML",
            DiagramExportFormat.Excel => Strings.Format_DefinitionDocument,
            DiagramExportFormat.Html => Strings.Format_DefinitionDocumentHtml,
            DiagramExportFormat.SchemaJson => "Schema JSON",
            _ => Strings.Format_File,
        };

        switch (format)
        {
            case DiagramExportFormat.Png:
                if (visual is not Visual pngVisual)
                {
                    throw new InvalidOperationException(Strings.Export_PngCanvasInfoMissing);
                }

                ImageExportService.ExportPng(pngVisual, path);
                break;

            case DiagramExportFormat.Svg:
                ImageExportService.ExportSvg(this, path);
                break;

            case DiagramExportFormat.Sql:
                File.WriteAllText(
                    path,
                    CurrentProvider.DdlGenerator.Build(ToDiagramModel()),
                    System.Text.Encoding.UTF8
                );
                break;

            case DiagramExportFormat.Mermaid:
                MermaidExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Dbml:
                DbmlExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Excel:
                TableDefinitionDocumentExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Html:
                TableDefinitionHtmlExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.SchemaJson:
                // 配置情報（layout）を持たないスキーマのみ文書。Layout = null で保存すると
                // layout キー自体が出力されず、読込時に自動整列される可逆形式になる
                JsonStorageService.Save(
                    path,
                    new DiagramDocument { Schema = ToDiagramModel(), Layout = null }
                );
                break;
        }

        _dialogs.ShowInformation(
            string.Format(Strings.Export_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>指定形式のダイアグラムファイルを読み込み、確認のうえ現在の図を置換する</summary>
    private void ImportDiagramFile(DiagramImportFormat format, string path)
    {
        var diagram = format switch
        {
            DiagramImportFormat.Mermaid => MermaidImporter.Load(path),
            DiagramImportFormat.Dbml => DbmlImporter.Load(path),
            DiagramImportFormat.Excel => TableDefinitionDocumentImporter.Load(path),
            _ => throw new InvalidOperationException(Strings.Import_UnsupportedFormat),
        };

        var displayName = format switch
        {
            DiagramImportFormat.Mermaid => "Mermaid",
            DiagramImportFormat.Dbml => "DBML",
            DiagramImportFormat.Excel => Strings.Format_DefinitionDocument,
            _ => Strings.Format_File,
        };

        // Excel 定義書は再取込のマージ（Guid 引継＝クエリ定義・手配置レイアウトの温存）に対応する。
        // Mermaid / DBML は方言情報を持たず定義書用途でもないため、従来どおり丸ごと置換（クエリ消滅・全体整列）。
        if (format == DiagramImportFormat.Excel)
        {
            ImportExcelMerging(diagram, displayName);
            return;
        }

        if (
            !ConfirmDiagramReplacement(
                diagram.Entities,
                diagram.Relationships,
                string.Format(Strings.Import_ReplaceConfirm, displayName)
            )
        )
        {
            return;
        }

        ReplaceDiagramWithoutHistory(diagram.Entities, diagram.Relationships, autoLayout: true);
        _dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>Excel 定義書をマージ取込する（Guid 引継でクエリ定義・レイアウトを温存する）</summary>
    /// <remarks>
    /// 取込結果の Id を現在図へ寄せ、生存クエリ・レイアウト引継を <see cref="ReplaceDiagramFromModule"/> の
    /// マージ経路に委ねる。Excel 定義書は Memo を保持するため、一致エンティティの Memo は取込値を正とする
    /// （<c>preserveExistingMemo: false</c>）。壊れクエリがあれば確認メッセージへ削除対象名を付加する。
    /// </remarks>
    private void ImportExcelMerging(ErDiagram diagram, string displayName)
    {
        var merged = DiagramMergeReconciler.Reconcile(
            ToDiagramModel(),
            diagram.Entities,
            diagram.Relationships,
            preserveExistingMemo: false
        );

        if (
            !ConfirmMergedReplacement(
                merged,
                string.Format(Strings.Import_ReplaceConfirm, displayName)
            )
        )
        {
            return;
        }

        // Excel 定義書は対象 DBMS を保持しているため方言も復元する（ReplaceDiagramFromModule 内で採用）。
        // 生存クエリのみを引き継ぐ（壊れクエリは確認のうえ削除済み）。
        var mergedDiagram = new ErDiagram
        {
            Entities = merged.Entities.ToList(),
            Relationships = merged.Relationships.ToList(),
            TargetDbms = diagram.TargetDbms,
            Queries = merged.SurvivingQueries.ToList(),
        };
        ReplaceDiagramFromModule(mergedDiagram);

        _dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>マージ取込用の置換確認（構造同一かつ壊れクエリなしなら無確認・壊れクエリは削除対象名を付加する）</summary>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmMergedReplacement(DiagramMergeResult merged, string message)
    {
        var structurallySame =
            Entities.Count == 0 || HasSameStructure(merged.Entities, merged.Relationships);

        // 構造同一かつ壊れクエリなしなら従来どおり無確認で続行する
        if (structurallySame && merged.BrokenQueries.Count == 0)
        {
            return true;
        }

        // 壊れクエリがあれば削除対象のクエリ名を確認メッセージへ付加する（キャンセルで取込中止）
        var fullMessage =
            merged.BrokenQueries.Count > 0
                ? message
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Format(
                        Strings.Import_BrokenQueriesWarning,
                        string.Join(
                            Environment.NewLine,
                            merged.BrokenQueries.Select(query => "- " + query.Name)
                        )
                    )
                : message;

        return _dialogs.Confirm(fullMessage, Strings.Common_Confirm);
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から出力形式を判定する</summary>
    private static DiagramExportFormat GetExportFormat(string path, int filterIndex)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".png" => DiagramExportFormat.Png,
            ".svg" => DiagramExportFormat.Svg,
            ".sql" => DiagramExportFormat.Sql,
            ".mmd" => DiagramExportFormat.Mermaid,
            ".mermaid" => DiagramExportFormat.Mermaid,
            ".dbml" => DiagramExportFormat.Dbml,
            ".xlsx" => DiagramExportFormat.Excel,
            ".html" => DiagramExportFormat.Html,
            ".htm" => DiagramExportFormat.Html,
            ".json" => DiagramExportFormat.SchemaJson,
            _ => filterIndex switch
            {
                1 => DiagramExportFormat.Png,
                2 => DiagramExportFormat.Svg,
                3 => DiagramExportFormat.Sql,
                4 => DiagramExportFormat.SchemaJson,
                5 => DiagramExportFormat.Mermaid,
                6 => DiagramExportFormat.Mermaid,
                7 => DiagramExportFormat.Dbml,
                8 => DiagramExportFormat.Excel,
                9 => DiagramExportFormat.Html,
                _ => throw new InvalidOperationException(Strings.Export_FormatUndetermined),
            },
        };
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から取込形式を判定する</summary>
    private static DiagramImportFormat GetImportFormat(string path, int filterIndex)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".mmd" => DiagramImportFormat.Mermaid,
            ".mermaid" => DiagramImportFormat.Mermaid,
            ".dbml" => DiagramImportFormat.Dbml,
            ".xlsx" => DiagramImportFormat.Excel,
            _ => filterIndex switch
            {
                1 => DiagramImportFormat.Mermaid,
                2 => DiagramImportFormat.Dbml,
                3 => DiagramImportFormat.Excel,
                _ => throw new InvalidOperationException(Strings.Import_FormatUndetermined),
            },
        };
    }

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

        // 無題（未保存）なら従来どおり保存ダイアログで保存先を選ばせる
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
    /// </remarks>
    private void SaveToPath(string path)
    {
        _fileWatcher.Suspend();

        try
        {
            JsonStorageService.Save(path, ToDocument());
            UpdateDocumentIdentity(path);
        }
        finally
        {
            _fileWatcher.Resume();
        }
    }

    /// <summary>JSON ファイルからダイアグラムを読み込み、現在の図と置換する（ダイアログ表示）</summary>
    [RelayCommand]
    private void Open()
    {
        var picked = _files.PickOpenFile("ER Diagram (*.json)|*.json");

        if (picked is null)
        {
            return;
        }

        var document = JsonStorageService.Load(picked.Path);

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
