using QuickER.CodeGen.CSharp;
using QuickER.Settings;

namespace QuickER.CodeGen.UI;

/// <summary>C# コード生成ダイアログの設定（次回起動時に復元する永続化対象）</summary>
/// <remarks>
/// このスキーマは CLI の quicker.json（<c>--config</c>）互換になるよう、CLI の
/// <c>CodeGenerationOptions</c> とキー集合・意味・既定値を揃えている（<see cref="RootNamespace"/> /
/// <see cref="RepositoryDialects"/> / <see cref="OutputPath"/> 等）。JSON は camelCase で書き出すが、
/// CLI は <c>PropertyNameCaseInsensitive</c> で読むため名前が一致すればそのまま解釈される。
/// <see cref="OutputPath"/> は CLI でも解釈され、CLI はそのファイル名部分のみを出力ファイル名として使う
/// （出力先ディレクトリは常に <c>--out</c> が正）。
/// プロパティ宣言順＝JSON 出力順のため、カテゴリ順（出力モード→名前空間→生成対象→…→出力先）に並べている。
/// </remarks>
public class CSharpGenerationSettings
{
    // ===== 出力モード =====

    /// <summary>出力をカテゴリごとに別ファイル・別名前空間へ分割するか（false=1ファイルにまとめる）</summary>
    public bool SplitFilesByCategory { get; set; }

    // ===== 名前空間 =====

    /// <summary>
    /// ルート名前空間。分割時は各カテゴリ名前空間のフォールバック元になる
    /// （CLI の <c>CodeGenerationOptions.RootNamespace</c> と同名＝<c>--config</c> でそのまま解釈される）
    /// </summary>
    public string RootNamespace { get; set; } = DefaultRootNamespace;

    /// <summary>分割時の共有基盤（Runtime）名前空間。空なら {base}.Runtime にフォールバック</summary>
    public string RuntimeNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Entity 名前空間。空なら {base}.Entities にフォールバック</summary>
    public string EntityNamespace { get; set; } = string.Empty;

    /// <summary>分割時の EditModel 名前空間。空なら {base}.EditModels にフォールバック</summary>
    public string EditModelNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Mapper 名前空間。空なら {base}.Mappers にフォールバック</summary>
    public string MapperNamespace { get; set; } = string.Empty;

    /// <summary>分割時の Repository 名前空間。空なら {base}.Repositories にフォールバック</summary>
    public string RepositoryNamespace { get; set; } = string.Empty;

    /// <summary>分割時の ValueObject 名前空間。空なら {base}.ValueObjects にフォールバック</summary>
    public string ValueObjectNamespace { get; set; } = string.Empty;

    // ===== 生成対象 =====

    /// <summary>EditModel クラスを生成するか（Entity は常時生成のためキーを持たない）</summary>
    public bool GenerateEditModels { get; set; } = true;

    /// <summary>Mapper クラスを生成するか</summary>
    public bool GenerateMappers { get; set; } = true;

    // ===== 値オブジェクト =====

    /// <summary>全カラムを値オブジェクト化するか</summary>
    public bool GenerateValueObjects { get; set; }

    /// <summary>string 主キーを GuidKey 値オブジェクト化するか</summary>
    public bool UseGuidKeyForStringPrimaryKey { get; set; }

    // ===== DB アクセス =====

    /// <summary>QuickER 版 Repository を生成するか（DB アクセスの排他選択。既定は「なし」）</summary>
    public bool GenerateRepositories { get; set; }

    /// <summary>
    /// QuickER 版 Repository の対象 DB（生成方言）の一覧。GUI の対象 DB チェック（SQL Server / SQLite）を
    /// 小文字方言名（"sqlserver" / "sqlite"）で永続化する。空リストは未指定を表す
    /// </summary>
    /// <remarks>
    /// CLI の <c>CodeGenerationOptions.RepositoryDialects</c> と同名キーとしても解釈され、CLI では
    /// <c>--repository-dialects</c> 未指定時にこのリスト（非空）がそのまま有効になる
    /// </remarks>
    public List<string> RepositoryDialects { get; set; } = new();

    /// <summary>
    /// 無制限バイナリ列（varbinary(max) / BLOB 等）をQuickER 版 Repository の SELECT / UPDATE から除外するか（既定 false）
    /// </summary>
    public bool ExcludeUnboundedBinaryColumns { get; set; }

    /// <summary>EF Core 用コード（DbContext＋EF Core 版 Repository）を生成するか（DB アクセスの排他選択）</summary>
    public bool GenerateEfCore { get; set; }

    /// <summary>
    /// DB 非依存のインメモリ Repository 群（テスト用）を生成するか（既定 false。パッケージ参照モードとは併用不可）
    /// </summary>
    public bool GenerateInMemoryRepositories { get; set; }

    /// <summary>
    /// サーバー（SQL Server）＋ローカル（SQLite）構成の双方向同期支援を生成するか（既定 false）
    /// </summary>
    /// <remarks>
    /// 対象 DB が sqlserver と sqlite のちょうど 2 つで、かつ rowversion 列を持つテーブルが必要
    /// （満たさない構成は生成時に診断エラーになる）。
    /// </remarks>
    public bool GenerateSyncSupport { get; set; }

    // ===== リモート対応 =====

    /// <summary>
    /// リモート操作用の Repository インターフェイス（<c>I{Entity}RemoteRepository</c>）を追加生成するか（既定 false）
    /// </summary>
    public bool GenerateRemoteContracts { get; set; }

    /// <summary>
    /// リモート面の HTTP クライアント／サーバー実装（<c>Http{Entity}RemoteRepository</c>・
    /// <c>{ベース名}.RemoteServer.g.cs</c>）を生成するか（既定 false。ON はリモート面の生成を自動的に含意する）
    /// </summary>
    public bool GenerateRemoteServices { get; set; }

    // ===== ランタイム・ドキュメント =====

    /// <summary>
    /// ランタイム（固定コード）を生成物に含めず、NuGet パッケージ QuickER.Runtime.* への参照で賄うか
    /// （既定 false。EF Core とは併用可能だがインメモリ生成とは併用不可）
    /// </summary>
    public bool UseRuntimePackages { get; set; }

    /// <summary>API リファレンス Markdown（.g.md）を追加出力するか（既定 false）</summary>
    public bool GenerateApiDocs { get; set; }

    /// <summary>
    /// 日本語版 API リファレンス Markdown（.ja.g.md）も併産するか（既定 false）。
    /// 実効は <see cref="GenerateApiDocs"/> が true のときに限る（正本は英語）
    /// </summary>
    public bool IncludeJapaneseApiDocs { get; set; }

    // ===== 属性（UI 非表示。読込値を保持して生成へ反映する） =====

    /// <summary>データアノテーション属性（[Table] / [Key] / [Column] / [Required] / [MaxLength] 等）を付与するか（既定 true）</summary>
    public bool IncludeDataAnnotations { get; set; } = true;

    /// <summary>親参照ナビゲーションへ [JsonIgnore] を付与するか（JSON シリアライズ時の循環参照対策。既定 true）</summary>
    public bool IncludeJsonIgnoreOnParentNavigation { get; set; } = true;

    // ===== 出力先 =====

    /// <summary>
    /// 出力先パス。GUI では非分割時はファイルパス、分割時は出力フォルダパスを表す
    /// （空は未指定。非分割時の既定ファイル名へのプリフィルは VM の ApplySettings が行う＝
    /// 分割時に非分割用の既定ファイル名が混入して検証をすり抜けるのを防ぐ）。
    /// </summary>
    /// <remarks>
    /// CLI（<c>--config</c> / <c>--output-path</c>）でも解釈され、<c>Path.GetFileName</c> でファイル名部分のみが
    /// 使われる（出力先ディレクトリは常に <c>--out</c> が正）。名前のみの値（例 <c>EcOrder.g.cs</c>）も可。
    /// </remarks>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>ルート名前空間の工場出荷既定（分割時は {root}.Entities 等のフォールバック元になるため接尾辞なし）</summary>
    public const string DefaultRootNamespace = "Generated";

    /// <summary>非分割時の出力ファイル名の工場出荷既定</summary>
    public const string DefaultOutputFilePath = "QuickEREntities.g.cs";

    /// <summary>工場出荷既定の設定を生成する（クリア／初回起動で使う）</summary>
    public static CSharpGenerationSettings CreateDefault() => new();

    /// <summary>
    /// この設定から <see cref="CodeGenerationOptions"/> を組み立てる（設定→生成オプション変換の唯一の正）。
    /// </summary>
    /// <param name="outputFileName">
    /// 出力ファイル名。分割時は planner がバケット別ファイル名を使うため inert（既定名）、非分割時は出力先パスの
    /// ファイル名部分を渡す（GUI 固有の分割/非分割の出し分けは呼び出し側が行う）。
    /// </param>
    /// <remarks>
    /// 空白の子名前空間は null へ畳み、planner のフォールバック（<c>{root}.{接尾辞}</c>）を効かせる。
    /// 対象 DB（<see cref="RepositoryDialects"/>）は保存時の固定順（sqlserver, sqlite）をそのまま採る。
    /// UI 非表示の属性系（<see cref="IncludeDataAnnotations"/> 等）も保持値のまま生成へ反映する。
    /// </remarks>
    internal CodeGenerationOptions ToCodeGenerationOptions(string outputFileName) =>
        new()
        {
            RootNamespace = RootNamespace.Trim(),
            OutputFileName = outputFileName,
            SplitFilesByCategory = SplitFilesByCategory,
            RuntimeNamespace = NullIfEmpty(RuntimeNamespace),
            EntityNamespace = NullIfEmpty(EntityNamespace),
            EditModelNamespace = NullIfEmpty(EditModelNamespace),
            MapperNamespace = NullIfEmpty(MapperNamespace),
            RepositoryNamespace = NullIfEmpty(RepositoryNamespace),
            ValueObjectNamespace = NullIfEmpty(ValueObjectNamespace),
            GenerateEditModels = GenerateEditModels,
            GenerateMappers = GenerateMappers,
            GenerateRepositories = GenerateRepositories,
            RepositoryDialects = RepositoryDialects,
            GenerateEfCore = GenerateEfCore,
            GenerateInMemoryRepositories = GenerateInMemoryRepositories,
            GenerateSyncSupport = GenerateSyncSupport,
            UseRuntimePackages = UseRuntimePackages,
            GenerateRemoteContracts = GenerateRemoteContracts,
            GenerateRemoteServices = GenerateRemoteServices,
            GenerateApiDocs = GenerateApiDocs,
            IncludeJapaneseApiDocs = IncludeJapaneseApiDocs,
            ExcludeUnboundedBinaryColumns = ExcludeUnboundedBinaryColumns,
            GenerateValueObjects = GenerateValueObjects,
            UseGuidKeyForStringPrimaryKey = UseGuidKeyForStringPrimaryKey,
            IncludeDataAnnotations = IncludeDataAnnotations,
            IncludeJsonIgnoreOnParentNavigation = IncludeJsonIgnoreOnParentNavigation,
        };

    /// <summary>空白を null へ畳む（オプションのフォールバックを効かせるため）</summary>
    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>C# コード生成ダイアログ設定を JSON ファイルへ保存・読込するストア</summary>
public class CSharpGenerationSettingsStore : JsonSettingsStore<CSharpGenerationSettings>
{
    /// <summary>既定の保存ファイル名（ダイアログの「設定保存」の既定ファイル名と同名に揃えている）</summary>
    public const string DefaultFileName = "codegen-settings.json";

    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public CSharpGenerationSettingsStore()
        : base(DefaultFileName) { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public CSharpGenerationSettingsStore(string folder)
        : base(DefaultFileName, folder) { }
}
