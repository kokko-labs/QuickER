using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.CodeGen.UI;

/// <summary>C# コード生成ダイアログの ViewModel</summary>
/// <remarks>
/// 設定は <see cref="CSharpGenerationSettingsStore"/> で永続化し、生成確定時に保存・次回構築時に復元する。
/// <see cref="GeneratedRegex"/> を利用するため partial クラスとして定義する
/// </remarks>
public partial class CSharpGenerationDialogViewModel : ObservableObject
{
    /// <summary>設定の永続化ストア</summary>
    private readonly CSharpGenerationSettingsStore _store;

    /// <summary>出力先のファイル / フォルダ選択ダイアログの表示先</summary>
    private readonly IFileDialogService _files;

    /// <summary>設定の保存／読込の結果（成功通知・失敗エラー）を表示するメッセージダイアログ</summary>
    private readonly IDialogService _dialogs;

    /// <summary>ルート名前空間変更時の子名前空間追従更新を一時的に抑止するフラグ（設定適用中に使う）</summary>
    private bool _suppressNamespaceFollow;

    /// <summary>確定結果（OK 確定まで null）</summary>
    public CSharpGenerationDialogResult? Result { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（引数は確定可否）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>設定ストアとファイル選択サービスを指定して ViewModel を生成し、保存済み設定を復元する</summary>
    /// <param name="currentProvider">
    /// アプリの現在のプロバイダ。QuickER 版 Repository の対象 DB チェックは、図の方言が対応方言
    /// （<see cref="CodeGenerationOptions.SupportedRepositoryDialects"/>）ならその方言のみ初期 ON にし、
    /// 未対応方言（PostgreSQL / MySQL / Oracle）なら両方 OFF から始める（null は SQL Server 扱い）
    /// </param>
    public CSharpGenerationDialogViewModel(
        CSharpGenerationSettingsStore? store = null,
        IFileDialogService? files = null,
        IDatabaseProvider? currentProvider = null,
        IDialogService? dialogs = null
    )
    {
        _store = store ?? new CSharpGenerationSettingsStore();
        _files = files ?? NullFileDialogService.Instance;
        _dialogs = dialogs ?? NullDialogService.Instance;

        // 対象 DB チェックの初期値: 図の方言が対応方言（sqlserver/sqlite）ならその方言のみ ON、
        // 未対応方言（PostgreSQL 等）なら両方 OFF（ユーザーに明示的な選択を求める）。
        // null（判定不要文脈）のみ sqlserver を既定 ON にする（主にテスト用途。実 GUI 経路では常に currentProvider が渡る）。
        var dialectName = currentProvider?.Name;
        _targetSqlServer =
            dialectName is null
            || string.Equals(
                dialectName,
                SqlServerProvider.ProviderName,
                StringComparison.OrdinalIgnoreCase
            );
        _targetSqlite = string.Equals(
            dialectName,
            SqliteProvider.ProviderName,
            StringComparison.OrdinalIgnoreCase
        );

        ApplySettings(_store.Load());
    }

    // ===== 出力モード =====

    /// <summary>出力をカテゴリごとに分割するか（false=1ファイルにまとめる）</summary>
    [ObservableProperty]
    private bool _splitFilesByCategory;

    /// <summary>1 ファイルにまとめるか（<see cref="SplitFilesByCategory"/> の反転。モード①ラジオ用）</summary>
    public bool MergeIntoSingleFile
    {
        get => !SplitFilesByCategory;
        set => SplitFilesByCategory = !value;
    }

    // ===== 名前空間 =====

    /// <summary>ルート名前空間</summary>
    [ObservableProperty]
    private string _rootNamespace = CSharpGenerationSettings.DefaultRootNamespace;

    /// <summary>共有基盤（Runtime）名前空間</summary>
    [ObservableProperty]
    private string _runtimeNamespace = string.Empty;

    /// <summary>Entity 名前空間</summary>
    [ObservableProperty]
    private string _entityNamespace = string.Empty;

    /// <summary>EditModel 名前空間</summary>
    [ObservableProperty]
    private string _editModelNamespace = string.Empty;

    /// <summary>Mapper 名前空間</summary>
    [ObservableProperty]
    private string _mapperNamespace = string.Empty;

    /// <summary>Repository 名前空間</summary>
    [ObservableProperty]
    private string _repositoryNamespace = string.Empty;

    /// <summary>ValueObject 名前空間</summary>
    [ObservableProperty]
    private string _valueObjectNamespace = string.Empty;

    // ===== 出力先 =====

    /// <summary>出力先パス（非分割時はファイルパス、分割時は出力フォルダパス）</summary>
    [ObservableProperty]
    private string _outputPath = CSharpGenerationSettings.DefaultOutputFilePath;

    // ===== 生成対象 =====

    /// <summary>EditModel クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateEditModels = true;

    /// <summary>Mapper クラスを生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateMappers = true;

    /// <summary>QuickER 版 Repositoryを生成するかどうか（DB アクセスの排他選択の一角）</summary>
    [ObservableProperty]
    private bool _generateRepositories;

    /// <summary>EF Core 用コード（DbContext＋EF Core 版 Repository）を生成するかどうか（DB アクセスの排他選択の一角）</summary>
    [ObservableProperty]
    private bool _generateEfCore;

    /// <summary>
    /// DB 非依存のインメモリ Repository 群（テスト用）を生成するかどうか（既定 OFF）。
    /// </summary>
    /// <remarks>
    /// DB アクセスの排他ラジオとは独立に選べる（「なし」/ QuickER 版 Repository / EF Core のいずれとも併用可能）。
    /// パッケージ参照モード（<see cref="UseRuntimePackages"/>）とは併用できず、<see cref="Ok"/> で併用をブロックする。
    /// </remarks>
    [ObservableProperty]
    private bool _generateInMemoryRepositories;

    /// <summary>インメモリ実装生成チェックボックスのツールチップ</summary>
    public string GenerateInMemoryToolTip => Strings.CodeGen_GenerateInMemoryToolTip;

    /// <summary>API リファレンス Markdown（.g.md）を追加出力するかどうか（既定 OFF。DB アクセス選択とは独立）</summary>
    [ObservableProperty]
    private bool _generateApiDocs;

    /// <summary>
    /// 日本語版 API リファレンス Markdown（.ja.g.md）も併産するかどうか（既定 OFF。正本は英語）。
    /// </summary>
    /// <remarks>
    /// <see cref="GenerateApiDocs"/> の下位オプションで、実効は API リファレンス出力が ON のときに限る
    /// （XAML 側で IsEnabled を <see cref="GenerateApiDocs"/> に連動させる）。OFF に戻しても値は保持する。
    /// </remarks>
    [ObservableProperty]
    private bool _includeJapaneseApiDocs;

    /// <summary>
    /// データアノテーション属性（[Table] / [Key] / [Column] 等）を付与するかどうか（UI 非表示。既定 true）。
    /// </summary>
    /// <remarks>
    /// UI には出さないが、読み込んだ設定ファイルの値を保持し、保存・生成の双方へ書き戻す（GUI 経由でも値が失われない）。
    /// クリア／初回起動では既定 true に戻る。
    /// </remarks>
    private bool _includeDataAnnotations = true;

    /// <summary>
    /// 親参照ナビゲーションへ [JsonIgnore] を付与するかどうか（UI 非表示。既定 true）。
    /// </summary>
    /// <remarks>
    /// <see cref="_includeDataAnnotations"/> と同じく UI 非表示で、読込値を保持して保存・生成へ書き戻す。
    /// </remarks>
    private bool _includeJsonIgnoreOnParentNavigation = true;

    /// <summary>
    /// 無制限バイナリ列（varbinary(max) / BLOB 等）をQuickER 版 Repository の SELECT / UPDATE から除外するかどうか（既定 OFF）
    /// </summary>
    /// <remarks>
    /// QuickER 版 Repository を生成する場合のみ意味を持つため、行ごと <see cref="ShowExcludeUnboundedBinary"/> で
    /// 表示制御する（値は保持され、QuickER 版 Repository を選び直すと再び表示される）
    /// </remarks>
    [ObservableProperty]
    private bool _excludeUnboundedBinaryColumns;

    /// <summary>全カラムを値オブジェクト（Value Object）として生成するかどうか</summary>
    [ObservableProperty]
    private bool _generateValueObjects;

    /// <summary>string 型の主キーを GuidKey 値オブジェクト（GUID 自動採番）にするかどうか</summary>
    [ObservableProperty]
    private bool _useGuidKeyForStringPrimaryKey;

    /// <summary>
    /// ランタイム（固定コード）を生成物に含めず、NuGet パッケージ QuickER.Runtime.* への参照で賄うかどうか
    /// </summary>
    /// <remarks>
    /// EF Core（<see cref="GenerateEfCore"/>）とも併用できる（EF Core 固定 infra は QuickER.Runtime.EntityFrameworkCore
    /// パッケージが担い、スキーマ依存の QuickErDbContext・DI 登録は生成側に出力される）。常に操作可能。
    /// </remarks>
    [ObservableProperty]
    private bool _useRuntimePackages;

    /// <summary>パッケージ参照モードのチェックボックスのツールチップ</summary>
    public string UseRuntimePackagesToolTip => Strings.CodeGen_UseRuntimePackagesToolTip;

    /// <summary>
    /// リモート操作用の Repository インターフェイス（<c>I{Entity}RemoteRepository</c>）を追加生成するか（既定 false）
    /// </summary>
    /// <remarks>
    /// 純粋に追加的なオプションで、ON にしても <c>I{Entity}Repository</c>（全機能面）は変わらない。
    /// DB アクセスが「なし」のときは Repository 契約自体が生成されず無意味なため、行ごと非表示にする
    /// （<see cref="ShowRemoteContracts"/>）。値は保持され、DB アクセスを選び直すと再び表示される。
    /// </remarks>
    [ObservableProperty]
    private bool _generateRemoteContracts;

    /// <summary>
    /// リモート面の HTTP クライアント／サーバー実装（<c>Http{Entity}RemoteRepository</c>・
    /// <c>{ベース名}.RemoteServer.g.cs</c>）を生成するかどうか（既定 false）
    /// </summary>
    /// <remarks>
    /// ON にすると <see cref="GenerateRemoteContracts"/>（リモート面インターフェイス）を自動的に含意する
    /// （UI 連動＝<see cref="OnGenerateRemoteServicesChanged"/> で親を ON にし、親を OFF にすると
    /// <see cref="OnGenerateRemoteContractsChanged"/> で子も OFF に戻る）。リモート対応行と同じく、
    /// DB アクセスが「なし」のときは非表示にする（<see cref="ShowRemoteContracts"/>）
    /// </remarks>
    [ObservableProperty]
    private bool _generateRemoteServices;

    /// <summary>
    /// リモート対応の行を表示するかどうか（DB アクセスが「なし」以外＝Repository 契約が生成される場合のみ）
    /// </summary>
    public bool ShowRemoteContracts => GenerateRepositories || GenerateEfCore;

    /// <summary>リモート対応チェックボックスのツールチップ</summary>
    public string RemoteContractsToolTip => Strings.CodeGen_RemoteContractsToolTip;

    /// <summary>HTTP クライアント／サーバー実装チェックボックスのツールチップ</summary>
    public string RemoteServicesToolTip => Strings.CodeGen_RemoteServicesToolTip;

    /// <summary>リモート面の HTTP 実装 ON はリモート面インターフェイスの生成を自動的に含意する（親を ON にする）</summary>
    partial void OnGenerateRemoteServicesChanged(bool value)
    {
        if (value)
        {
            GenerateRemoteContracts = true;
        }

        // サーバーファイル（{ベース名}.RemoteServer.g.cs）の有無が変わるため、出力ファイルのプレビューを更新する
        RefreshPreview();
    }

    /// <summary>リモート面インターフェイスを OFF にしたら、それに依存する HTTP 実装も OFF に戻す（親 OFF で子も OFF）</summary>
    partial void OnGenerateRemoteContractsChanged(bool value)
    {
        if (!value)
        {
            GenerateRemoteServices = false;
        }
    }

    /// <summary>入力エラーや補助メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ===== DB アクセス（排他選択ラジオ） =====

    /// <summary>DB アクセスコードを生成しない（既定）</summary>
    public bool DbAccessNone
    {
        get => !GenerateRepositories && !GenerateEfCore;
        set
        {
            if (value)
            {
                GenerateRepositories = false;
                GenerateEfCore = false;
            }
        }
    }

    /// <summary>QuickER 版 Repositoryを生成する（対応方言の図でのみ選択可）</summary>
    public bool DbAccessRepository
    {
        get => GenerateRepositories;
        set
        {
            if (value)
            {
                GenerateRepositories = true;
                GenerateEfCore = false;
            }
        }
    }

    /// <summary>EF Core（DbContext＋EF Core 版 Repository）を生成する（方言非依存）</summary>
    public bool DbAccessEfCore
    {
        get => GenerateEfCore;
        set
        {
            if (value)
            {
                GenerateEfCore = true;
                GenerateRepositories = false;
            }
        }
    }

    /// <summary>
    /// 「Repository (QuickER)」ラジオのツールチップ（常時選択可。対象 DB をチェックで選ぶ運用を案内する）
    /// </summary>
    public string QuickErRepositoryToolTip => Strings.CodeGen_QuickErRepositoryToolTip;

    // ===== QuickER 版 Repository の対象 DB（チェックボックス群。Repository ラジオ選択時のみ表示） =====

    /// <summary>対象 DB に SQL Server を含めるか</summary>
    [ObservableProperty]
    private bool _targetSqlServer;

    /// <summary>対象 DB に SQLite を含めるか</summary>
    [ObservableProperty]
    private bool _targetSqlite;

    /// <summary>対象 DB チェックボックス群を表示するか（QuickER 版 Repository 選択時のみ）</summary>
    public bool ShowRepositoryDialectTargets => GenerateRepositories;

    /// <summary>無制限バイナリ列の除外チェックボックスを表示するか（QuickER 版 Repository 選択時のみ）</summary>
    public bool ShowExcludeUnboundedBinary => GenerateRepositories;

    /// <summary>無制限バイナリ列の除外チェックボックスのツールチップ</summary>
    public string ExcludeUnboundedBinaryToolTip => Strings.CodeGen_ExcludeUnboundedBinaryToolTip;

    partial void OnTargetSqlServerChanged(bool value) => RefreshPreview();

    partial void OnTargetSqliteChanged(bool value) => RefreshPreview();

    /// <summary>生成されるファイルのプレビュー（「ファイル名 → namespace」の一覧。設定に追従して更新）</summary>
    public ObservableCollection<string> PreviewFiles { get; } = new();

    // ===== 表示制御（派生） =====

    /// <summary>非分割時の出力ファイル欄を表示するか</summary>
    public bool ShowSingleFileOutput => !SplitFilesByCategory;

    /// <summary>分割時の詳細（カテゴリ別名前空間・出力フォルダ）を表示するか</summary>
    public bool ShowSplitOptions => SplitFilesByCategory;

    /// <summary>Runtime 名前空間欄を表示するか（分割時は常に必要）</summary>
    public bool ShowRuntimeNamespace => SplitFilesByCategory;

    /// <summary>Entity 名前空間欄を表示するか（Entity は常時生成のため分割時は常に表示）</summary>
    public bool ShowEntityNamespace => SplitFilesByCategory;

    /// <summary>EditModel 名前空間欄を表示するか</summary>
    public bool ShowEditModelNamespace => SplitFilesByCategory && GenerateEditModels;

    /// <summary>Mapper 名前空間欄を表示するか</summary>
    public bool ShowMapperNamespace => SplitFilesByCategory && GenerateMappers;

    /// <summary>
    /// Repository 名前空間欄を表示するか。EF Core・インメモリ選択時も Repository バケット（共通契約＋Repository
    /// インターフェイス）は出力されるため、それらのいずれかが有効なら表示する
    /// </summary>
    public bool ShowRepositoryNamespace =>
        SplitFilesByCategory
        && (GenerateRepositories || GenerateEfCore || GenerateInMemoryRepositories);

    /// <summary>ValueObject 名前空間欄を表示するか</summary>
    public bool ShowValueObjectNamespace => SplitFilesByCategory && GenerateValueObjects;

    // ===== 変更フック =====

    partial void OnSplitFilesByCategoryChanged(bool value)
    {
        OnPropertyChanged(nameof(MergeIntoSingleFile));
        RaiseDerivedChanged();
    }

    partial void OnGenerateEditModelsChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateMappersChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateRepositoriesChanged(bool value)
    {
        RaiseDbAccessChanged();
        RaiseDerivedChanged();
    }

    partial void OnGenerateEfCoreChanged(bool value)
    {
        // EF Core はパッケージ参照モードと併用できるため、チェックの強制解除・無効化は行わない。
        // 参照案内（EF Core パッケージの追加）は生成後メッセージ・ヘッダで反映されるためプレビューのみ追従する。
        RaiseDbAccessChanged();
        RaiseDerivedChanged();
    }

    // インメモリ実装の切替は Repository バケット（契約＋インメモリ実装）の有無を変えるため、表示制御・プレビューを追従させる
    partial void OnGenerateInMemoryRepositoriesChanged(bool value) => RaiseDerivedChanged();

    partial void OnGenerateValueObjectsChanged(bool value) => RaiseDerivedChanged();

    // パッケージ参照モードの切替は Runtime ファイルの有無を変えるため、生成されるファイルのプレビューを追従させる
    partial void OnUseRuntimePackagesChanged(bool value) => RefreshPreview();

    /// <summary>DB アクセスラジオ（なし／QuickER 版 Repository／EF Core）の表示状態を再通知する</summary>
    private void RaiseDbAccessChanged()
    {
        OnPropertyChanged(nameof(DbAccessNone));
        OnPropertyChanged(nameof(DbAccessRepository));
        OnPropertyChanged(nameof(DbAccessEfCore));
        // リモート対応行の表示/非表示は DB アクセス選択に連動する（「なし」で非表示）
        OnPropertyChanged(nameof(ShowRemoteContracts));
    }

    partial void OnRuntimeNamespaceChanged(string value) => RefreshPreview();

    partial void OnEntityNamespaceChanged(string value) => RefreshPreview();

    partial void OnEditModelNamespaceChanged(string value) => RefreshPreview();

    partial void OnMapperNamespaceChanged(string value) => RefreshPreview();

    partial void OnRepositoryNamespaceChanged(string value) => RefreshPreview();

    partial void OnValueObjectNamespaceChanged(string value) => RefreshPreview();

    partial void OnOutputPathChanged(string value) => RefreshPreview();

    /// <summary>ルート名前空間が変わったら、既定（{旧root}.{接尾辞}）のままの子名前空間を新ルートへ追従させる</summary>
    partial void OnRootNamespaceChanged(string? oldValue, string newValue)
    {
        if (!_suppressNamespaceFollow && oldValue is not null)
        {
            FollowRootNamespace(oldValue, newValue);
        }

        RefreshPreview();
    }

    /// <summary>派生プロパティ（表示制御）の変更通知を発行し、プレビューを更新する</summary>
    private void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(ShowSingleFileOutput));
        OnPropertyChanged(nameof(ShowSplitOptions));
        OnPropertyChanged(nameof(ShowRuntimeNamespace));
        OnPropertyChanged(nameof(ShowEntityNamespace));
        OnPropertyChanged(nameof(ShowEditModelNamespace));
        OnPropertyChanged(nameof(ShowMapperNamespace));
        OnPropertyChanged(nameof(ShowRepositoryNamespace));
        OnPropertyChanged(nameof(ShowValueObjectNamespace));
        OnPropertyChanged(nameof(ShowRepositoryDialectTargets));
        OnPropertyChanged(nameof(ShowExcludeUnboundedBinary));
        RefreshPreview();
    }

    /// <summary>
    /// ルート追従（<see cref="FollowRootNamespace"/>）とプリフィル（<see cref="ApplySettings"/>）が対象にする
    /// 6 つの子カテゴリ名前空間の入出力（現在値の取得・設定・対応バケット・設定オブジェクトからの取得）を 1 箇所へ集約する。
    /// </summary>
    /// <remarks>
    /// 並び順は UI のカテゴリ別 namespace 欄・ApplySettings の代入順と一致させる（Runtime → Entity → EditModel →
    /// Mapper → Repository → ValueObject）。追従・プリフィルの 6 バケット分の反復をこの表 1 つで回す。
    /// </remarks>
    private IReadOnlyList<(
        Func<string> Get,
        Action<string> Set,
        GenerationBucket Bucket,
        Func<CSharpGenerationSettings, string> FromSettings
    )> ChildNamespaceAccessors =>
        [
            (
                () => RuntimeNamespace,
                value => RuntimeNamespace = value,
                GenerationBucket.Runtime,
                settings => settings.RuntimeNamespace
            ),
            (
                () => EntityNamespace,
                value => EntityNamespace = value,
                GenerationBucket.Entity,
                settings => settings.EntityNamespace
            ),
            (
                () => EditModelNamespace,
                value => EditModelNamespace = value,
                GenerationBucket.EditModel,
                settings => settings.EditModelNamespace
            ),
            (
                () => MapperNamespace,
                value => MapperNamespace = value,
                GenerationBucket.Mapper,
                settings => settings.MapperNamespace
            ),
            (
                () => RepositoryNamespace,
                value => RepositoryNamespace = value,
                GenerationBucket.Repository,
                settings => settings.RepositoryNamespace
            ),
            (
                () => ValueObjectNamespace,
                value => ValueObjectNamespace = value,
                GenerationBucket.ValueObject,
                settings => settings.ValueObjectNamespace
            ),
        ];

    /// <summary>各子名前空間が「{旧root}.{接尾辞}」既定のままなら新ルートへ更新する（手編集済みは保持）</summary>
    private void FollowRootNamespace(string oldRoot, string newRoot)
    {
        _suppressNamespaceFollow = true;
        try
        {
            foreach (var (get, set, bucket, _) in ChildNamespaceAccessors)
            {
                set(FollowOne(get(), oldRoot, newRoot, bucket));
            }
        }
        finally
        {
            _suppressNamespaceFollow = false;
        }
    }

    /// <summary>子名前空間が空または旧既定なら新既定へ、手編集済みならそのままにする</summary>
    private static string FollowOne(
        string current,
        string oldRoot,
        string newRoot,
        GenerationBucket bucket
    )
    {
        var suffix = GeneratedFilePlanner.DefaultSuffix(bucket);
        var oldDefault = $"{oldRoot}.{suffix}";
        return string.IsNullOrWhiteSpace(current) || current == oldDefault
            ? $"{newRoot}.{suffix}"
            : current;
    }

    /// <summary>設定値を各プロパティへ適用する（空の子名前空間は {root}.{接尾辞} でプリフィルする）</summary>
    private void ApplySettings(CSharpGenerationSettings settings)
    {
        _suppressNamespaceFollow = true;
        try
        {
            SplitFilesByCategory = settings.SplitFilesByCategory;
            RootNamespace = settings.RootNamespace;

            // 空の子名前空間は {root}.{接尾辞} でプリフィルする（6 バケット分を集約表で回す）
            foreach (var (_, set, bucket, fromSettings) in ChildNamespaceAccessors)
            {
                set(Prefill(fromSettings(settings), bucket));
            }

            GenerateEditModels = settings.GenerateEditModels;
            GenerateMappers = settings.GenerateMappers;
            // DB アクセスは排他選択。両方 true の保存値（手編集等）はQuickER 版 Repository を優先する
            // （Repository ラジオは常時選択可のため、方言による無効化は行わない）
            GenerateRepositories = settings.GenerateRepositories;
            GenerateEfCore = settings.GenerateEfCore && !GenerateRepositories;
            // 対象 DB チェック（SQL Server / SQLite）は、保存値のリストが非空ならその内容で復元する。
            // 空リスト（未指定＝旧設定 / クリア）のときは ctor で図の方言から導出した初期値を保つ。
            if (settings.RepositoryDialects.Count > 0)
            {
                TargetSqlServer = settings.RepositoryDialects.Contains(
                    SqlServerProvider.ProviderName,
                    StringComparer.OrdinalIgnoreCase
                );
                TargetSqlite = settings.RepositoryDialects.Contains(
                    SqliteProvider.ProviderName,
                    StringComparer.OrdinalIgnoreCase
                );
            }
            // インメモリ実装（テスト用）は排他ラジオと独立のため、保存値をそのまま復元する
            GenerateInMemoryRepositories = settings.GenerateInMemoryRepositories;
            // 属性系（UI 非表示）は読込値を保持し、ToSettings / ToOptions で書き戻す（GUI 経由でも値が失われない）
            _includeDataAnnotations = settings.IncludeDataAnnotations;
            _includeJsonIgnoreOnParentNavigation = settings.IncludeJsonIgnoreOnParentNavigation;
            // パッケージ参照モードは EF Core とも併用できるため、保存値をそのまま復元する
            UseRuntimePackages = settings.UseRuntimePackages;
            // リモート対応（リモート面の追加生成）は保存値をそのまま復元する（行の表示/非表示は UI 側で連動）
            GenerateRemoteContracts = settings.GenerateRemoteContracts;
            // リモート面の HTTP クライアント／サーバー実装も保存値をそのまま復元する
            // （親（GenerateRemoteContracts）を先に復元しているため、含意の UI 連動で親が意図せず OFF になることはない）
            GenerateRemoteServices = settings.GenerateRemoteServices;
            // API リファレンス出力は DB アクセス選択とは独立のため、保存値をそのまま復元する
            GenerateApiDocs = settings.GenerateApiDocs;
            // 日本語版 API リファレンスの併産も保存値をそのまま復元する（実効は GenerateApiDocs && この値）
            IncludeJapaneseApiDocs = settings.IncludeJapaneseApiDocs;
            // 無制限バイナリ列の除外はQuickER 版 Repository 選択時のみ効くが、値は保存値のまま復元する（行の表示/非表示は UI 側で連動）
            ExcludeUnboundedBinaryColumns = settings.ExcludeUnboundedBinaryColumns;
            GenerateValueObjects = settings.GenerateValueObjects;
            UseGuidKeyForStringPrimaryKey = settings.UseGuidKeyForStringPrimaryKey;
            // 非分割時のみ、未指定の出力先を既定ファイル名でプリフィルする
            // （分割時は空のまま＝Ok() の「出力フォルダを指定してください」検証を効かせる）
            OutputPath =
                string.IsNullOrWhiteSpace(settings.OutputPath) && !settings.SplitFilesByCategory
                    ? CSharpGenerationSettings.DefaultOutputFilePath
                    : settings.OutputPath;
        }
        finally
        {
            _suppressNamespaceFollow = false;
        }

        StatusMessage = string.Empty;
        RaiseDerivedChanged();
    }

    /// <summary>子名前空間が空なら {root}.{接尾辞} を返す（プリフィル）</summary>
    private string Prefill(string value, GenerationBucket bucket) =>
        string.IsNullOrWhiteSpace(value)
            ? $"{RootNamespace}.{GeneratedFilePlanner.DefaultSuffix(bucket)}"
            : value;

    /// <summary>現在の設定値から設定オブジェクトを組み立てる（永続化用）</summary>
    private CSharpGenerationSettings ToSettings() =>
        new()
        {
            SplitFilesByCategory = SplitFilesByCategory,
            RootNamespace = RootNamespace.Trim(),
            RuntimeNamespace = RuntimeNamespace.Trim(),
            EntityNamespace = EntityNamespace.Trim(),
            EditModelNamespace = EditModelNamespace.Trim(),
            MapperNamespace = MapperNamespace.Trim(),
            RepositoryNamespace = RepositoryNamespace.Trim(),
            ValueObjectNamespace = ValueObjectNamespace.Trim(),
            GenerateEditModels = GenerateEditModels,
            GenerateMappers = GenerateMappers,
            GenerateRepositories = GenerateRepositories,
            RepositoryDialects = SelectedRepositoryDialects(),
            GenerateEfCore = GenerateEfCore,
            GenerateInMemoryRepositories = GenerateInMemoryRepositories,
            UseRuntimePackages = UseRuntimePackages,
            GenerateRemoteContracts = GenerateRemoteContracts,
            GenerateRemoteServices = GenerateRemoteServices,
            GenerateApiDocs = GenerateApiDocs,
            IncludeJapaneseApiDocs = IncludeJapaneseApiDocs,
            ExcludeUnboundedBinaryColumns = ExcludeUnboundedBinaryColumns,
            GenerateValueObjects = GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey = UseGuidKeyForStringPrimaryKey,
            // UI 非表示の属性系は保持値をそのまま書き戻す（読込→保存で値が失われない）
            IncludeDataAnnotations = _includeDataAnnotations,
            IncludeJsonIgnoreOnParentNavigation = _includeJsonIgnoreOnParentNavigation,
            // 出力先は OutputPath に一本化する。CLI（--config）は保存された OutputPath のファイル名部分のみを
            // 出力ファイル名として使う（出力先ディレクトリは常に --out）。分割時はフォルダパスが入る
            OutputPath = OutputPath.Trim(),
        };

    /// <summary>現在の設定値からコード生成オプションを組み立てる</summary>
    /// <remarks>
    /// 設定→生成オプションのマッピングは <see cref="CSharpGenerationSettings.ToCodeGenerationOptions"/> に集約し、
    /// ここでは現在値から <see cref="ToSettings"/> を作り、GUI 固有の出力ファイル名（分割時は inert な既定名、
    /// 非分割時は出力先パスのファイル名部分）だけを与えて委譲する（設定・生成・CLI 互換の変換を 1 箇所に保つ）。
    /// 分割時の OutputFileName は .cs（カテゴリ別固定名）・.md（固定名 ApiDocs.g.md）とも出力名に関与しない。
    /// </remarks>
    public CodeGenerationOptions ToOptions() =>
        ToSettings()
            .ToCodeGenerationOptions(
                SplitFilesByCategory
                    ? CSharpGenerationSettings.DefaultOutputFilePath
                    : Path.GetFileName(OutputPath.Trim())
            );

    /// <summary>チェックされた対象 DB を固定順（SQL Server → SQLite）で返す</summary>
    private List<string> SelectedRepositoryDialects()
    {
        var dialects = new List<string>();

        if (TargetSqlServer)
        {
            dialects.Add(SqlServerProvider.ProviderName);
        }

        if (TargetSqlite)
        {
            dialects.Add(SqliteProvider.ProviderName);
        }

        return dialects;
    }

    /// <summary>現在の設定で生成されるファイル一覧（「ファイル名 → namespace」）を再計算する</summary>
    private void RefreshPreview()
    {
        PreviewFiles.Clear();

        if (string.IsNullOrWhiteSpace(RootNamespace))
        {
            return;
        }

        foreach (var spec in GeneratedFilePlanner.Plan(ToOptions()))
        {
            PreviewFiles.Add($"{spec.FileName}  →  namespace {spec.NamespaceName}");
        }
    }

    /// <summary>出力先を選択し、結果をパスへ反映する（分割時はフォルダ、非分割時はファイルを選ぶ）</summary>
    /// <remarks>
    /// 分割モードでフォルダを選んだときは、そのフォルダから名前空間の候補を導出し、確認ダイアログで
    /// 承諾された場合のみ <see cref="RootNamespace"/> を書き換える（既定パターンのままの子カテゴリ別
    /// namespace は <see cref="FollowRootNamespace"/> の連動で自動追従する）。
    /// キャンセル・候補が現在値と同一・導出不能のいずれでも namespace は触らず、フォルダパスの反映のみ従来どおり行う
    /// </remarks>
    [RelayCommand]
    private void BrowseOutput()
    {
        if (SplitFilesByCategory)
        {
            var selectedPath = _files.PickFolder(Strings.CodeGen_PickFolderTitle, OutputPath);

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            OutputPath = selectedPath;
            StatusMessage = string.Empty;

            // 選択フォルダから名前空間の候補を導出し、現在値と異なるときだけ確認して書き換える
            MaybeSuggestNamespaceFromFolder(selectedPath);
            return;
        }

        var fileName = Path.GetFileName(OutputPath);
        var result = _files.PickSaveFile(
            "C# Generated Code (*.g.cs)|*.g.cs",
            ".g.cs",
            string.IsNullOrWhiteSpace(fileName) ? "QuickEREntities.g.cs" : fileName,
            Path.GetDirectoryName(OutputPath)
        );

        if (result is null)
        {
            return;
        }

        OutputPath = result.Path;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 選択された出力先フォルダから名前空間の候補を導出し、現在の <see cref="RootNamespace"/> と異なる場合のみ
    /// 確認ダイアログで承諾を得て書き換える
    /// </summary>
    /// <remarks>
    /// 書き換えると、既定パターン（{旧root}.{接尾辞}）のままの子カテゴリ別 namespace は
    /// <see cref="FollowRootNamespace"/> の連動で自動的に新ルートへ追従する（手編集済みの子は保持）。
    /// 導出不能（null）・現在値と同一・確認でキャンセルのいずれでも namespace は一切変更しない
    /// </remarks>
    private void MaybeSuggestNamespaceFromFolder(string folderPath)
    {
        var suggestion = OutputFolderNamespaceSuggester.TryDerive(folderPath);

        // 導出できない、または現在のルート名前空間（前後空白を無視）と同一なら確認せず何もしない
        if (
            suggestion is null
            || string.Equals(suggestion, RootNamespace.Trim(), StringComparison.Ordinal)
        )
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            string.Format(Strings.CodeGen_ConfirmNamespaceFromFolder, suggestion),
            Strings.CodeGen_SettingsDialogTitle
        );

        if (confirmed)
        {
            RootNamespace = suggestion;
        }
    }

    /// <summary>全設定を工場出荷既定へ戻す（ディスクへの反映は次の生成確定時）</summary>
    [RelayCommand]
    private void Clear() => ApplySettings(CSharpGenerationSettings.CreateDefault());

    /// <summary>現在の設定一式を名前を付けて JSON ファイルへ保存する（プロジェクト別プリセットのエクスポート）</summary>
    /// <remarks>
    /// 対象 DB チェックを含む設定一式を、CLI の <c>--config</c> にそのまま渡せるスキーマで書き出す。
    /// %APPDATA% の codegen-settings.json へは書き込まず、選択された任意ファイルへ書き出す
    /// （永続化は生成確定時の <see cref="Ok"/> の責務）。成功時は情報ダイアログで通知し、
    /// アクセス拒否・IO 失敗時はエラーダイアログを表示する（いずれも表示状態は変更しない）
    /// </remarks>
    [RelayCommand]
    private void SaveSettingsAs()
    {
        var result = _files.PickSaveFile(
            "QuickER CodeGen Settings (*.json)|*.json",
            ".json",
            "codegen-settings.json"
        );

        if (result is null)
        {
            // キャンセル時は何もしない（現在の表示状態は変更しない）
            return;
        }

        try
        {
            _store.SaveTo(result.Path, ToSettings());
            // 保存成功は情報ダイアログで通知する（CLI の --config へ渡せる旨も併記）
            _dialogs.ShowInformation(
                string.Format(Strings.CodeGen_SettingsSavedMessage, Path.GetFileName(result.Path)),
                Strings.CodeGen_SettingsDialogTitle
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // アクセス拒否・IO 失敗はダイアログを落とさずエラーダイアログで通知する
            _dialogs.ShowError(
                string.Format(
                    Strings.CodeGen_SettingsSaveFailedMessage,
                    Path.GetFileName(result.Path)
                ),
                Strings.CodeGen_SettingsDialogTitle
            );
        }
    }

    /// <summary>保存済みの設定 JSON ファイルを読み込み、ダイアログの表示状態へ反映する（プリセットのインポート）</summary>
    /// <remarks>
    /// 反映のみ行い %APPDATA% の codegen-settings.json へは書き込まない。成功時は無通知（表示へ反映するのみ）。
    /// 解析不能・不正・IO 失敗時はエラーダイアログを表示し、現在の表示状態は変更しない
    /// </remarks>
    [RelayCommand]
    private void LoadSettingsFrom()
    {
        var result = _files.PickOpenFile("QuickER CodeGen Settings (*.json)|*.json");

        if (result is null)
        {
            // キャンセル時は何もしない
            return;
        }

        CSharpGenerationSettings? settings;

        try
        {
            settings = _store.TryLoadFrom(result.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // IO 失敗（アクセス拒否等）も解析失敗と同じくエラー表示にとどめ、表示状態は変更しない
            settings = null;
        }

        if (settings is null)
        {
            _dialogs.ShowError(
                string.Format(
                    Strings.CodeGen_SettingsLoadFailedMessage,
                    Path.GetFileName(result.Path)
                ),
                Strings.CodeGen_SettingsDialogTitle
            );
            return;
        }

        // 読み込み成功は無通知（表示状態へ反映するのみ）
        ApplySettings(settings);
    }

    /// <summary>入力内容を検証して確定し、設定を保存する（不正時はステータスにエラーを表示する）</summary>
    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(RootNamespace))
        {
            StatusMessage = Strings.CodeGen_Status_NamespaceRequired;
            return;
        }

        if (!IsValidNamespace(RootNamespace))
        {
            StatusMessage = Strings.CodeGen_Status_NamespaceInvalid;
            return;
        }

        if (GenerateRepositories && !TargetSqlServer && !TargetSqlite)
        {
            StatusMessage = Strings.CodeGen_Status_TargetDbRequired;
            return;
        }

        // インメモリ実装（生成側の固定 infra を要する）とパッケージ参照モード（固定 infra を出力しない）は併用不可
        if (GenerateInMemoryRepositories && UseRuntimePackages)
        {
            StatusMessage = Strings.CodeGen_Status_InMemoryRuntimePackagesConflict;
            return;
        }

        if (SplitFilesByCategory)
        {
            if (!ValidateSplitNamespaces(out var invalidName))
            {
                StatusMessage = string.Format(
                    Strings.CodeGen_Status_NamespaceInvalidFormat,
                    invalidName
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                StatusMessage = Strings.CodeGen_Status_OutputFolderRequired;
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(OutputPath))
        {
            StatusMessage = Strings.CodeGen_Status_OutputFileRequired;
            return;
        }

        _store.Save(ToSettings());

        var outputDirectory = SplitFilesByCategory
            ? OutputPath.Trim()
            : Path.GetDirectoryName(OutputPath.Trim()) ?? string.Empty;

        Result = new CSharpGenerationDialogResult(ToOptions(), outputDirectory);
        CloseAction?.Invoke(true);
    }

    /// <summary>確定せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    /// <summary>表示中のカテゴリ別名前空間（非空のもの）の形式を検証する</summary>
    /// <param name="invalidName">最初に不正だった項目名（呼び出し側のメッセージ用）</param>
    private bool ValidateSplitNamespaces(out string invalidName)
    {
        var targets = new (bool Show, string Value, string Name)[]
        {
            (ShowRuntimeNamespace, RuntimeNamespace, Strings.CodeGen_NamespaceLabel_Runtime),
            (ShowEntityNamespace, EntityNamespace, Strings.CodeGen_NamespaceLabel_Entity),
            (ShowEditModelNamespace, EditModelNamespace, Strings.CodeGen_NamespaceLabel_EditModel),
            (ShowMapperNamespace, MapperNamespace, Strings.CodeGen_NamespaceLabel_Mapper),
            (
                ShowRepositoryNamespace,
                RepositoryNamespace,
                Strings.CodeGen_NamespaceLabel_Repository
            ),
            (
                ShowValueObjectNamespace,
                ValueObjectNamespace,
                Strings.CodeGen_NamespaceLabel_ValueObject
            ),
        };

        foreach (var target in targets)
        {
            if (
                target.Show
                && !string.IsNullOrWhiteSpace(target.Value)
                && !IsValidNamespace(target.Value)
            )
            {
                invalidName = target.Name;
                return false;
            }
        }

        invalidName = string.Empty;
        return true;
    }

    /// <summary>namespace として妥当な形式かを各セグメントの識別子検証で簡易判定する</summary>
    private static bool IsValidNamespace(string value)
    {
        var segments = value.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return segments.Length > 0 && segments.All(segment => IdentifierRegex().IsMatch(segment));
    }

    /// <summary>C# 識別子として有効なセグメントにマッチする正規表現</summary>
    [GeneratedRegex(@"^[_\p{L}][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}

/// <summary>C# コード生成ダイアログの確定結果</summary>
/// <param name="Options">生成に使うオプション</param>
/// <param name="OutputDirectory">生成ファイルの書き出し先ディレクトリ（モード①はファイルの親、モード②は出力フォルダ）</param>
public sealed record CSharpGenerationDialogResult(
    CodeGenerationOptions Options,
    string OutputDirectory
);

/// <summary>何も表示せず常にキャンセル扱いを返す <see cref="IFileDialogService"/>（未注入時＝テスト用の既定）</summary>
/// <remarks>実 GUI 経路では合成側（WpfAppDialogService）が必ず実装を注入するため、この既定は使われない</remarks>
file sealed class NullFileDialogService : IFileDialogService
{
    /// <summary>共有インスタンス（状態を持たないため単一でよい）</summary>
    public static NullFileDialogService Instance { get; } = new();

    /// <inheritdoc />
    public FileDialogResult? PickOpenFile(string filter) => null;

    /// <inheritdoc />
    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    ) => null;

    /// <inheritdoc />
    public string? PickFolder(string title, string? initialDirectory = null) => null;
}

/// <summary>何も表示しない <see cref="IDialogService"/>（未注入時＝テスト用の既定）</summary>
/// <remarks>実 GUI 経路では合成側（WpfAppDialogService）が必ず実装を注入するため、この既定は使われない</remarks>
file sealed class NullDialogService : IDialogService
{
    /// <summary>共有インスタンス（状態を持たないため単一でよい）</summary>
    public static NullDialogService Instance { get; } = new();

    /// <inheritdoc />
    public bool Confirm(string message, string title) => false;

    /// <inheritdoc />
    public bool ConfirmWarning(string message, string title) => false;

    /// <inheritdoc />
    public void ShowInformation(string message, string title) { }

    /// <inheritdoc />
    public void ShowError(string message, string title) { }

    /// <inheritdoc />
    public void ShowInformationDetails(string message, string details, string title) { }

    /// <inheritdoc />
    public void ShowErrorDetails(string message, string details, string title) { }
}
