using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
/// MVVM ツールキットのソースジェネレーター（<c>[ObservableProperty]</c> / <c>[RelayCommand]</c>）を
/// 利用するため partial クラスとして定義する
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

    /// <summary>
    /// 生成ファイルを層別サブフォルダ（ドメイン／プレゼンテーション／インフラ／サーバー）へ振り分けて出力するか（既定 OFF）
    /// </summary>
    /// <remarks>
    /// ON は分割出力（<see cref="SplitFilesByCategory"/>）を自動的に含意する（単一ファイルは層へ割れないため）。
    /// UI では含意を可視化するため、ON の間だけ出力モードのラジオを分割固定＋操作不可にする
    /// （<see cref="OnLayeredOutputChanged"/> / <see cref="CanEditSplitFilesByCategory"/>）。
    /// 生成オプションへは <see cref="LayeredOutput"/> と <see cref="SplitFilesByCategory"/> の両方をそのまま渡し、
    /// 含意の解釈はコア側（<c>CodeGenerationOptions.EffectiveSplitFilesByCategory</c>）へ委ねる
    /// </remarks>
    [ObservableProperty]
    private bool _layeredOutput;

    /// <summary>層別出力時のドメイン層フォルダ（出力先からの相対パス。既定値でプリフィルする）</summary>
    [ObservableProperty]
    private string _domainLayerDirectory = GeneratedFilePlanner.DefaultLayerDirectory(
        GeneratedLayer.Domain
    );

    /// <summary>層別出力時のプレゼンテーション層フォルダ（出力先からの相対パス）</summary>
    [ObservableProperty]
    private string _presentationLayerDirectory = GeneratedFilePlanner.DefaultLayerDirectory(
        GeneratedLayer.Presentation
    );

    /// <summary>層別出力時のインフラストラクチャ層フォルダ（出力先からの相対パス）</summary>
    [ObservableProperty]
    private string _infrastructureLayerDirectory = GeneratedFilePlanner.DefaultLayerDirectory(
        GeneratedLayer.Infrastructure
    );

    /// <summary>層別出力時のサーバー層フォルダ（出力先からの相対パス。リモートサービス生成時のみ使われる）</summary>
    [ObservableProperty]
    private string _serverLayerDirectory = GeneratedFilePlanner.DefaultLayerDirectory(
        GeneratedLayer.Server
    );

    /// <summary>
    /// 生成コードの出力先サブフォルダ（層フォルダ／出力先の 1 段下。空＝サブフォルダなし）。
    /// 出力モードに依らず有効で、既定は空のままプリフィルしない（サブフォルダなしが既定であることを空欄で表す）
    /// </summary>
    [ObservableProperty]
    private string _codeSubdirectory = string.Empty;

    // サブフォルダは生成ファイルの配置（プレビュー表示）だけを変える。層フォルダと違い名前空間の既定へは
    // 一切影響しないため、名前空間の追従（FollowDefaultNamespaces）は呼ばない
    partial void OnCodeSubdirectoryChanged(string value) => RefreshPreview();

    /// <summary>層別出力チェックボックスのツールチップ</summary>
    public string LayeredOutputToolTip => Strings.CodeGen_LayeredOutputToolTip;

    /// <summary>生成コードの出力先サブフォルダ欄のツールチップ</summary>
    public string CodeSubdirectoryToolTip => Strings.CodeGen_CodeSubdirectoryToolTip;

    /// <summary>
    /// 出力モード（1 ファイル／分割）のラジオを操作できるか。層別出力 ON の間は分割固定のため false になる
    /// </summary>
    public bool CanEditSplitFilesByCategory => !LayeredOutput;

    /// <summary>層フォルダの入力欄を表示するか（層別出力 ON のときのみ）</summary>
    public bool ShowLayerDirectories => LayeredOutput;

    /// <summary>
    /// サーバー層フォルダの入力欄を表示するか（層別出力 ON かつリモートサービス生成 ON のときのみ）
    /// </summary>
    /// <remarks>サーバー層へ出るのはリモートサーバー実装だけのため、生成しない構成では欄ごと隠す</remarks>
    public bool ShowServerLayerDirectory => LayeredOutput && GenerateRemoteServices;

    /// <summary>
    /// 層別出力の切替に伴い、分割出力の強制 ON・名前空間の既定切替・関連する表示制御を反映する
    /// </summary>
    /// <remarks>
    /// 名前空間の既定は層別出力の ON/OFF でモードごと変わる（層由来 ⇔ ルート由来）ため、
    /// 既定のままの欄を新しい既定へ追従させる（手編集済みの欄は保持）
    /// </remarks>
    partial void OnLayeredOutputChanged(bool value)
    {
        if (value)
        {
            // 層別出力は分割出力を構造的に前提とする（単一ファイルは層へ割れない）。
            // OFF に戻したときは分割の値はそのままにし、ラジオの操作可能状態だけを戻す
            SplitFilesByCategory = true;
        }

        if (!_suppressNamespaceFollow)
        {
            // bool の切替なので、変更前の状態は「反転した層別出力」で再現できる
            FollowDefaultNamespaces(NamespaceDefaultContext(layeredOutput: !value));
        }

        OnPropertyChanged(nameof(CanEditSplitFilesByCategory));
        RaiseDerivedChanged();
    }

    // 層フォルダの変更は生成ファイルの配置（プレビュー表示）と、層別出力時の名前空間の既定を変えるため、
    // 既定のままの名前空間欄（その層に属するバケットのもの）とプレビューを追従させる
    partial void OnDomainLayerDirectoryChanged(string? oldValue, string newValue) =>
        ApplyLayerDirectoryChange(NamespaceDefaultContext(domainLayerDirectory: oldValue));

    partial void OnPresentationLayerDirectoryChanged(string? oldValue, string newValue) =>
        ApplyLayerDirectoryChange(NamespaceDefaultContext(presentationLayerDirectory: oldValue));

    partial void OnInfrastructureLayerDirectoryChanged(string? oldValue, string newValue) =>
        ApplyLayerDirectoryChange(NamespaceDefaultContext(infrastructureLayerDirectory: oldValue));

    partial void OnServerLayerDirectoryChanged(string? oldValue, string newValue) =>
        ApplyLayerDirectoryChange(NamespaceDefaultContext(serverLayerDirectory: oldValue));

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
    private bool _generateEfCoreRepositories;

    /// <summary>
    /// DB 非依存のインメモリ Repository 群（テスト用）を生成するかどうか（既定 OFF）。
    /// </summary>
    /// <remarks>
    /// DB アクセスの排他ラジオとは独立に選べる（「なし」/ QuickER 版 Repository / EF Core のいずれとも併用可能）。
    /// パッケージ参照モード（<see cref="UseRuntimePackages"/>）とは併用できず、<see cref="Ok"/> で併用をブロックする。
    /// </remarks>
    [ObservableProperty]
    private bool _generateInMemoryRepositories;

    /// <summary>
    /// サーバー（SQL Server）＋ローカル（SQLite）構成の双方向同期支援を生成するかどうか（既定 OFF）。
    /// </summary>
    /// <remarks>
    /// QuickER 版 Repository で対象 DB を SQL Server と SQLite の両方に選び、かつ図に <c>rowversion</c> 列を持つ
    /// テーブルがあるときだけ意味を持つ（満たさない構成は生成時に診断エラーになる）。チェック欄の表示は
    /// <see cref="ShowSyncSupport"/> が両方言選択に連動させる。
    /// </remarks>
    [ObservableProperty]
    private bool _generateSyncSupport;

    /// <summary>インメモリ実装生成チェックボックスのツールチップ</summary>
    public string GenerateInMemoryToolTip => Strings.CodeGen_GenerateInMemoryToolTip;

    /// <summary>API リファレンス Markdown（.g.md）を追加出力するかどうか（既定 OFF。DB アクセス選択とは独立）</summary>
    [ObservableProperty]
    private bool _generateApiDocs;

    /// <summary>API リファレンス出力を OFF にしたら、下位の日本語版併産チェックも OFF に連動させる（無効化＋残チェックの見かけ矛盾を防ぐ）</summary>
    partial void OnGenerateApiDocsChanged(bool value)
    {
        if (!value)
        {
            IncludeJapaneseApiDocs = false;
        }
    }

    /// <summary>
    /// 日本語版 API リファレンス Markdown（.ja.g.md）も併産するかどうか（既定 OFF。正本は英語）。
    /// </summary>
    /// <remarks>
    /// <see cref="GenerateApiDocs"/> の下位オプションで、API リファレンス出力が ON のときのみ選べる
    /// （XAML 側で IsEnabled を <see cref="GenerateApiDocs"/> に連動・親を OFF にするとこの値も OFF に戻る）。
    /// </remarks>
    [ObservableProperty]
    private bool _includeJapaneseApiDocs;

    /// <summary>
    /// API リファレンス Markdown の出力先サブフォルダ（出力フォルダからの相対パス。空＝直下）。
    /// 層別出力に依らず有効で、既定は空のままプリフィルしない（直下が既定であることを空欄で表す）
    /// </summary>
    [ObservableProperty]
    private string _apiDocsSubdirectory = string.Empty;

    /// <summary>
    /// API リファレンス Markdown の出力ファイル名（空＝導出名を使う）。
    /// </summary>
    /// <remarks>
    /// 既定はプリフィルせず空のままにする（実名を焼き付けると、その後に出力ファイル名や出力モードを変えても
    /// ドキュメント名だけ古い名前で固定されるため）。空欄のときに使われる名前は
    /// <see cref="ApiDocsFileNameHint"/> がプレースホルダとして見せる
    /// </remarks>
    [ObservableProperty]
    private string _apiDocsFileName = string.Empty;

    /// <summary>出力ファイル名欄が空のとき、実際に使われる導出名をプレースホルダとして見せるか</summary>
    public bool ShowApiDocsFileNameHint => string.IsNullOrWhiteSpace(ApiDocsFileName);

    /// <summary>
    /// 出力ファイル名を指定しなかったときに使われるファイル名（プレースホルダ表示用）
    /// </summary>
    /// <remarks>
    /// 導出は生成本体と同じ経路（<see cref="CSharpCodeGenerationService.ResolveApiDocsFileName"/>）へ委ね、
    /// 明示指定を外したオプションを渡して「空欄なら何になるか」を求める（表示と実出力がずれない）。
    /// 出力ファイル名・出力モードの変更に追従する（<see cref="RaiseDerivedChanged"/> と
    /// <see cref="OnOutputPathChanged"/> が通知する）
    /// </remarks>
    public string ApiDocsFileNameHint =>
        CSharpCodeGenerationService.ResolveApiDocsFileName(
            ToOptions() with
            {
                ApiDocsFileName = null,
            }
        );

    /// <summary>出力ファイル名欄の入力有無でプレースホルダの表示が切り替わる</summary>
    partial void OnApiDocsFileNameChanged(string value) =>
        OnPropertyChanged(nameof(ShowApiDocsFileNameHint));

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
    /// EF Core（<see cref="GenerateEfCoreRepositories"/>）とも併用できる（EF Core 固定 infra は QuickER.Runtime.EntityFrameworkCore
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
    public bool ShowRemoteContracts => GenerateRepositories || GenerateEfCoreRepositories;

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

        // サーバー層フォルダの欄はリモートサーバー実装を生成するときだけ意味を持つため、表示可否を再通知する
        OnPropertyChanged(nameof(ShowServerLayerDirectory));
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
        get => !GenerateRepositories && !GenerateEfCoreRepositories;
        set
        {
            if (value)
            {
                GenerateRepositories = false;
                GenerateEfCoreRepositories = false;
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
                GenerateEfCoreRepositories = false;
            }
        }
    }

    /// <summary>EF Core（DbContext＋EF Core 版 Repository）を生成する（方言非依存）</summary>
    public bool DbAccessEfCore
    {
        get => GenerateEfCoreRepositories;
        set
        {
            if (value)
            {
                GenerateEfCoreRepositories = true;
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

    // 対象 DB の選択は同期支援の欄の表示可否（両方言のときだけ意味を持つ）にも効くため、派生を再通知する
    partial void OnTargetSqlServerChanged(bool value) => RaiseDerivedChanged();

    partial void OnTargetSqliteChanged(bool value) => RaiseDerivedChanged();

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
        && (GenerateRepositories || GenerateEfCoreRepositories || GenerateInMemoryRepositories);

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

    partial void OnGenerateEfCoreRepositoriesChanged(bool value)
    {
        // EF Core はパッケージ参照モードと併用できるため、チェックの強制解除・無効化は行わない。
        // 参照案内（EF Core パッケージの追加）は生成後メッセージ・ヘッダで反映されるためプレビューのみ追従する。
        RaiseDbAccessChanged();
        RaiseDerivedChanged();
    }

    // インメモリ実装の切替は Repository バケット（契約＋インメモリ実装）の有無を変えるため、表示制御・プレビューを追従させる
    partial void OnGenerateInMemoryRepositoriesChanged(bool value) => RaiseDerivedChanged();

    // 同期支援の切替は Sync バケット（同期記述子・デコレータ・DI）の有無を変えるため、プレビューを追従させる
    partial void OnGenerateSyncSupportChanged(bool value) => RaiseDerivedChanged();

    /// <summary>
    /// 同期支援のチェック欄を表示するか（QuickER 版 Repository で SQL Server と SQLite の両方を対象にしたときのみ）。
    /// </summary>
    /// <remarks>
    /// 同期はサーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成専用のため、片方言だけの構成では
    /// 選べても生成できない。選べない構成では欄ごと隠して、生成してからエラーで気づく形にしない。
    /// </remarks>
    public bool ShowSyncSupport => GenerateRepositories && TargetSqlServer && TargetSqlite;

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

    partial void OnOutputPathChanged(string value)
    {
        // 非分割時の API リファレンス既定名は出力ファイル名から導出されるため、プレースホルダを追従させる
        OnPropertyChanged(nameof(ApiDocsFileNameHint));
        RefreshPreview();
    }

    /// <summary>ルート名前空間が変わったら、既定（{旧root}.{接尾辞}）のままの子名前空間を新ルートへ追従させる</summary>
    partial void OnRootNamespaceChanged(string? oldValue, string newValue)
    {
        if (!_suppressNamespaceFollow && oldValue is not null)
        {
            FollowDefaultNamespaces(NamespaceDefaultContext(rootNamespace: oldValue));
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
        // 同期支援の欄はQuickER 版 Repository の選択と対象 DB（両方言）に連動する
        OnPropertyChanged(nameof(ShowSyncSupport));
        // 層フォルダの欄は層別出力の ON/OFF に、サーバー層はさらにリモートサービス生成に連動する
        OnPropertyChanged(nameof(ShowLayerDirectories));
        OnPropertyChanged(nameof(ShowServerLayerDirectory));
        // API リファレンスの既定ファイル名は出力モード（分割／非分割）で変わるため、プレースホルダを追従させる
        OnPropertyChanged(nameof(ApiDocsFileNameHint));
        RefreshPreview();
    }

    /// <summary>
    /// 既定追従（<see cref="FollowDefaultNamespaces"/>）とプリフィル（<see cref="ApplySettings"/>）が対象にする
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

    /// <summary>
    /// 名前空間の既定（プリフィル・追従）を求めるための最小の生成オプションを組み立てる。
    /// </summary>
    /// <remarks>
    /// 既定の規約（通常分割は <c>{root}.{接尾辞}</c>・層別出力は <c>{層フォルダ由来ルート}.{接尾辞}</c>）は
    /// planner が正本のため、UI 側で規約を再実装せず <see cref="GeneratedFilePlanner.ResolveNamespace"/> へ委ねる。
    /// 明示の名前空間は載せない（載せると既定でなく明示値が返り、既定値そのものが求まらない）。
    /// 各引数は「変更前の状態」を再現するための差し替えで、省略（null）時は現在の VM の値を使う
    /// </remarks>
    private CodeGenerationOptions NamespaceDefaultContext(
        bool? layeredOutput = null,
        string? rootNamespace = null,
        string? domainLayerDirectory = null,
        string? presentationLayerDirectory = null,
        string? infrastructureLayerDirectory = null,
        string? serverLayerDirectory = null
    ) =>
        new()
        {
            RootNamespace = rootNamespace ?? RootNamespace,
            LayeredOutput = layeredOutput ?? LayeredOutput,
            DomainLayerDirectory = domainLayerDirectory ?? DomainLayerDirectory,
            PresentationLayerDirectory = presentationLayerDirectory ?? PresentationLayerDirectory,
            InfrastructureLayerDirectory =
                infrastructureLayerDirectory ?? InfrastructureLayerDirectory,
            ServerLayerDirectory = serverLayerDirectory ?? ServerLayerDirectory,
        };

    /// <summary>
    /// 名前空間の既定が変わったとき、既定のままの子名前空間欄だけを新しい既定へ追従させる（手編集済みは保持）。
    /// </summary>
    /// <param name="previousContext">
    /// 変更前の状態を表す文脈（<see cref="NamespaceDefaultContext"/> で 1 つだけ旧値へ差し替えたもの）。
    /// 旧既定と一致する欄が「ユーザーが触っていない欄」の判定基準になる
    /// </param>
    /// <remarks>
    /// ルート名前空間の変更・層別出力の ON/OFF・層フォルダの変更のいずれもこの 1 経路へ集約する
    /// （既定の求め方が planner 1 箇所なので、モードが増えても追従規則を書き足す必要がない）。
    /// 旧既定と新既定が同じ変更（例: 層別出力 ON 中のルート変更）では代入しても値が変わらず実質 no-op になる
    /// </remarks>
    private void FollowDefaultNamespaces(CodeGenerationOptions previousContext)
    {
        var currentContext = NamespaceDefaultContext();

        _suppressNamespaceFollow = true;
        try
        {
            foreach (var (get, set, bucket, _) in ChildNamespaceAccessors)
            {
                var current = get();
                var oldDefault = GeneratedFilePlanner.ResolveNamespace(previousContext, bucket);

                if (string.IsNullOrWhiteSpace(current) || current == oldDefault)
                {
                    set(GeneratedFilePlanner.ResolveNamespace(currentContext, bucket));
                }
            }
        }
        finally
        {
            _suppressNamespaceFollow = false;
        }
    }

    /// <summary>層フォルダの変更を、既定のままの名前空間欄の追従とプレビュー更新へ反映する</summary>
    /// <param name="previousContext">変更前の層フォルダを持つ文脈（旧既定の判定に使う）</param>
    private void ApplyLayerDirectoryChange(CodeGenerationOptions previousContext)
    {
        if (!_suppressNamespaceFollow)
        {
            FollowDefaultNamespaces(previousContext);
        }

        RefreshPreview();
    }

    /// <summary>
    /// 設定値を各プロパティへ適用する（空の子名前空間はモード別の既定＝通常分割なら <c>{root}.{接尾辞}</c>・
    /// 層別出力なら <c>{層フォルダ由来ルート}.{接尾辞}</c> でプリフィルする）
    /// </summary>
    private void ApplySettings(CSharpGenerationSettings settings)
    {
        _suppressNamespaceFollow = true;
        try
        {
            SplitFilesByCategory = settings.SplitFilesByCategory;
            // 層別出力は分割出力より後に適用する（含意の連動で分割が強制 ON になるため。
            // 外部編集された「層別 ON＋分割 OFF」の設定でも UI 不変条件へ揃う）
            LayeredOutput = settings.LayeredOutput;
            // 空の層フォルダは planner の既定名（Domain / Presentation / …）でプリフィルする
            DomainLayerDirectory = PrefillLayer(
                settings.DomainLayerDirectory,
                GeneratedLayer.Domain
            );
            PresentationLayerDirectory = PrefillLayer(
                settings.PresentationLayerDirectory,
                GeneratedLayer.Presentation
            );
            InfrastructureLayerDirectory = PrefillLayer(
                settings.InfrastructureLayerDirectory,
                GeneratedLayer.Infrastructure
            );
            ServerLayerDirectory = PrefillLayer(
                settings.ServerLayerDirectory,
                GeneratedLayer.Server
            );
            // サブフォルダは既定が「なし」なのでプリフィルせず保存値をそのまま反映する
            CodeSubdirectory = settings.CodeSubdirectory;
            RootNamespace = settings.RootNamespace;

            // 空の子名前空間は現在のモードの既定でプリフィルする（6 バケット分を集約表で回す）。
            // 層別出力・層フォルダ・ルート名前空間はこの時点で適用済み＝既定が正しいモードで求まる
            foreach (var (_, set, bucket, fromSettings) in ChildNamespaceAccessors)
            {
                set(Prefill(fromSettings(settings), bucket));
            }

            GenerateEditModels = settings.GenerateEditModels;
            GenerateMappers = settings.GenerateMappers;
            // DB アクセスは排他選択。両方 true の保存値（手編集等）はQuickER 版 Repository を優先する
            // （Repository ラジオは常時選択可のため、方言による無効化は行わない）
            GenerateRepositories = settings.GenerateRepositories;
            GenerateEfCoreRepositories =
                settings.GenerateEfCoreRepositories && !GenerateRepositories;
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
            // 同期支援は対象 DB の選択（両方言）に意味が依存するが、保存値はそのまま復元する
            // （欄の表示は ShowSyncSupport が連動し、成立しない構成は生成時の診断が止める）
            GenerateSyncSupport = settings.GenerateSyncSupport;
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
            // 日本語版 API リファレンスの併産は「親 OFF なら子も OFF」の UI 不変条件に合わせてクランプして復元する
            // （外部編集された設定ファイルの親 OFF＋子 ON の組み合わせで、無効なのにチェック済みの表示になるのを防ぐ）
            IncludeJapaneseApiDocs = settings.GenerateApiDocs && settings.IncludeJapaneseApiDocs;
            // API リファレンスの出力先サブフォルダは保存値をそのまま復元する（空＝直下が既定・プリフィルなし）
            ApiDocsSubdirectory = settings.ApiDocsSubdirectory;
            // 出力ファイル名も同様に保存値をそのまま復元する（空＝導出名。プレースホルダで既定名を見せる）
            ApiDocsFileName = settings.ApiDocsFileName;
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

    /// <summary>層フォルダが空なら層の既定フォルダ名（<c>Domain</c> 等）を返す（プリフィル）</summary>
    private static string PrefillLayer(string value, GeneratedLayer layer) =>
        string.IsNullOrWhiteSpace(value)
            ? GeneratedFilePlanner.DefaultLayerDirectory(layer)
            : value;

    /// <summary>
    /// 子名前空間が空なら現在のモードの既定（通常分割は <c>{root}.{接尾辞}</c>・層別出力は
    /// <c>{層フォルダ由来ルート}.{接尾辞}</c>）を返す（プリフィル）
    /// </summary>
    /// <remarks>
    /// 呼び出し前に層別出力・層フォルダ・ルート名前空間を適用しておくこと（既定の算出がそれらを読む）。
    /// 保存値が「どちらかのモードの既定形」と一致する場合も未編集として扱い、現在のモードの既定へ
    /// 置き換える（過去のビルドは既定を実体化したまま保存しており、それを明示値扱いすると層フォルダの
    /// 変更に追従しない欄が残るため。既定形と偶然一致する明示値は選び直せば済む＝実害より回復を優先）
    /// </remarks>
    private string Prefill(string value, GenerationBucket bucket)
    {
        var current = GeneratedFilePlanner.ResolveNamespace(NamespaceDefaultContext(), bucket);

        if (string.IsNullOrWhiteSpace(value))
        {
            return current;
        }

        var trimmed = value.Trim();
        var layeredDefault = GeneratedFilePlanner.ResolveNamespace(
            NamespaceDefaultContext(layeredOutput: true),
            bucket
        );
        var plainDefault = GeneratedFilePlanner.ResolveNamespace(
            NamespaceDefaultContext(layeredOutput: false),
            bucket
        );

        return trimmed == layeredDefault || trimmed == plainDefault ? current : trimmed;
    }

    /// <summary>永続化する子名前空間の値を決める（現在の既定と一致する欄は空＝既定は保存しない）</summary>
    /// <remarks>
    /// プリフィルは欄へ実体化されるため、そのまま保存すると次回以降「手編集した明示値」と区別できず、
    /// 層フォルダ・モード・ルート名前空間の変更に追従しない欄になる。既定は空として保存し、読込時に
    /// 改めて導出する。生成オプションへも空（＝導出）として渡るが、導出結果は欄の表示と同じ値になる
    /// </remarks>
    private string NamespaceForPersistence(string value, GenerationBucket bucket)
    {
        var trimmed = value.Trim();

        return trimmed == GeneratedFilePlanner.ResolveNamespace(NamespaceDefaultContext(), bucket)
            ? string.Empty
            : trimmed;
    }

    /// <summary>永続化する層フォルダの値を決める（既定フォルダ名と一致する欄は空＝既定は保存しない）</summary>
    /// <remarks>子名前空間（<see cref="NamespaceForPersistence"/>）と同じ理由の同型規則</remarks>
    private static string LayerDirectoryForPersistence(string value, GeneratedLayer layer)
    {
        var trimmed = value.Trim();

        return trimmed == GeneratedFilePlanner.DefaultLayerDirectory(layer)
            ? string.Empty
            : trimmed;
    }

    /// <summary>現在の設定値から設定オブジェクトを組み立てる（永続化用）</summary>
    private CSharpGenerationSettings ToSettings() =>
        new()
        {
            SplitFilesByCategory = SplitFilesByCategory,
            LayeredOutput = LayeredOutput,
            // 層フォルダ・子名前空間とも「既定と一致する欄」は空で保存する（プリフィルの実体化値を
            // 明示値として持ち越さない＝次回以降もフォルダ・モード変更への追従が効き続ける）
            DomainLayerDirectory = LayerDirectoryForPersistence(
                DomainLayerDirectory,
                GeneratedLayer.Domain
            ),
            PresentationLayerDirectory = LayerDirectoryForPersistence(
                PresentationLayerDirectory,
                GeneratedLayer.Presentation
            ),
            InfrastructureLayerDirectory = LayerDirectoryForPersistence(
                InfrastructureLayerDirectory,
                GeneratedLayer.Infrastructure
            ),
            ServerLayerDirectory = LayerDirectoryForPersistence(
                ServerLayerDirectory,
                GeneratedLayer.Server
            ),
            // サブフォルダは既定（＝空）以外に実体化するプリフィルが無いため、トリムだけして保存する
            CodeSubdirectory = CodeSubdirectory.Trim(),
            RootNamespace = RootNamespace.Trim(),
            RuntimeNamespace = NamespaceForPersistence(RuntimeNamespace, GenerationBucket.Runtime),
            EntityNamespace = NamespaceForPersistence(EntityNamespace, GenerationBucket.Entity),
            EditModelNamespace = NamespaceForPersistence(
                EditModelNamespace,
                GenerationBucket.EditModel
            ),
            MapperNamespace = NamespaceForPersistence(MapperNamespace, GenerationBucket.Mapper),
            RepositoryNamespace = NamespaceForPersistence(
                RepositoryNamespace,
                GenerationBucket.Repository
            ),
            ValueObjectNamespace = NamespaceForPersistence(
                ValueObjectNamespace,
                GenerationBucket.ValueObject
            ),
            GenerateEditModels = GenerateEditModels,
            GenerateMappers = GenerateMappers,
            GenerateRepositories = GenerateRepositories,
            RepositoryDialects = SelectedRepositoryDialects(),
            GenerateEfCoreRepositories = GenerateEfCoreRepositories,
            GenerateInMemoryRepositories = GenerateInMemoryRepositories,
            // 選択できない構成（片方言）では保存値をそのまま持ち越さず落とす（隠れた欄の値で生成が止まらないように）
            GenerateSyncSupport = ShowSyncSupport && GenerateSyncSupport,
            UseRuntimePackages = UseRuntimePackages,
            // 同上（DB アクセス「なし」ではリモート対応の行を隠すため、隠れた保存値が生成時エラーを踏まないよう落とす）
            GenerateRemoteContracts = ShowRemoteContracts && GenerateRemoteContracts,
            GenerateRemoteServices = ShowRemoteContracts && GenerateRemoteServices,
            GenerateApiDocs = GenerateApiDocs,
            IncludeJapaneseApiDocs = IncludeJapaneseApiDocs,
            ApiDocsSubdirectory = ApiDocsSubdirectory.Trim(),
            ApiDocsFileName = ApiDocsFileName.Trim(),
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
            // 層別出力では出力先が層フォルダ配下へ変わるため、相対フォルダを付けて配置まで見せる
            // （層別でないスペックは RelativeDirectory が null＝ファイル名だけ）
            var displayPath = string.IsNullOrWhiteSpace(spec.RelativeDirectory)
                ? spec.FileName
                : $"{spec.RelativeDirectory}/{spec.FileName}";

            PreviewFiles.Add($"{displayPath}  →  namespace {spec.NamespaceName}");
        }
    }

    /// <summary>出力先を選択し、結果をパスへ反映する（分割時はフォルダ、非分割時はファイルを選ぶ）</summary>
    /// <remarks>
    /// 分割モードでフォルダを選んだときは、そのフォルダから名前空間の候補を導出し、確認ダイアログで
    /// 承諾された場合のみ <see cref="RootNamespace"/> を書き換える（既定パターンのままの子カテゴリ別
    /// namespace は <see cref="FollowRootNamespace"/> の連動で自動追従する）。
    /// キャンセル・候補が現在値と同一・導出不能のいずれでも namespace は触らず、フォルダパスの反映だけを行う
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

    /// <summary>namespace として妥当な形式かを判定する</summary>
    /// <remarks>
    /// 判定規則は生成前検証（<c>CSharpCodeGenerationService</c>）と共有するため
    /// <see cref="CSharpNamespaceValidator"/> へ委譲する（GUI と CLI / MCP で判定をずらさない）
    /// </remarks>
    private static bool IsValidNamespace(string value) => CSharpNamespaceValidator.IsValid(value);
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
    public bool ConfirmWarningDetails(string message, string details, string title) => false;

    /// <inheritdoc />
    public void ShowInformation(string message, string title) { }

    /// <inheritdoc />
    public void ShowError(string message, string title) { }

    /// <inheritdoc />
    public void ShowInformationDetails(string message, string details, string title) { }

    /// <inheritdoc />
    public void ShowErrorDetails(string message, string details, string title) { }
}
