using QuickER.Settings;

namespace QuickER.CodeGen.UI;

/// <summary>C# コード生成ダイアログの設定（次回起動時に復元する永続化対象）</summary>
public class CSharpGenerationSettings
{
    /// <summary>出力をカテゴリごとに別ファイル・別名前空間へ分割するか（false=1ファイルにまとめる）</summary>
    public bool SplitFilesByCategory { get; set; }

    /// <summary>ベース（ルート）名前空間。分割時は各カテゴリ名前空間のフォールバック元になる</summary>
    public string BaseNamespace { get; set; } = DefaultBaseNamespace;

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

    /// <summary>分割時の EfCore 名前空間。空なら {base}.EfCore にフォールバック</summary>
    public string EfCoreNamespace { get; set; } = string.Empty;

    /// <summary>Entity クラスを生成するか</summary>
    public bool GenerateEntityClasses { get; set; } = true;

    /// <summary>EditModel クラスを生成するか</summary>
    public bool GenerateEditModels { get; set; } = true;

    /// <summary>Mapper クラスを生成するか</summary>
    public bool GenerateMappers { get; set; } = true;

    /// <summary>QuickER 版 Repository を生成するか（DB アクセスの排他選択。既定は「なし」）</summary>
    public bool GenerateRepositories { get; set; }

    /// <summary>EF Core 用コード（DbContext＋EF Core 版 Repository）を生成するか（DB アクセスの排他選択）</summary>
    public bool GenerateEfCore { get; set; }

    /// <summary>
    /// ランタイム（固定コード）を生成物に含めず、NuGet パッケージ QuickER.Runtime.* への参照で賄うか
    /// （既定 false。EF Core 生成とは併用不可のため EF Core 選択時は強制的に false になる）
    /// </summary>
    public bool UseRuntimePackages { get; set; }

    /// <summary>
    /// リモート操作用の Repository インターフェイス（<c>I{Entity}RemoteRepository</c>）を追加生成するか（既定 false）
    /// </summary>
    public bool GenerateRemoteContracts { get; set; }

    /// <summary>
    /// リモート面の HTTP クライアント／サーバー実装（<c>Http{Entity}RemoteRepository</c>・
    /// <c>{ベース名}.RemoteServer.g.cs</c>）を生成するか（既定 false。ON はリモート面の生成を自動的に含意する）
    /// </summary>
    public bool GenerateRemoteServices { get; set; }

    /// <summary>API リファレンス Markdown（.g.md）を追加出力するか（既定 false）</summary>
    public bool GenerateApiDocs { get; set; }

    /// <summary>
    /// 無制限バイナリ列（varbinary(max) / BLOB 等）をQuickER 版 Repository の SELECT / UPDATE から除外するか（既定 false）
    /// </summary>
    public bool ExcludeUnboundedBinaryColumns { get; set; }

    /// <summary>全カラムを値オブジェクト化するか</summary>
    public bool GenerateValueObjects { get; set; }

    /// <summary>string 主キーを GuidKey 値オブジェクト化するか</summary>
    public bool UseGuidKeyForStringPrimaryKey { get; set; }

    /// <summary>非分割（モード①）時の出力ファイルパス</summary>
    public string OutputFilePath { get; set; } = DefaultOutputFilePath;

    /// <summary>分割（モード②）時の出力フォルダパス</summary>
    public string OutputFolderPath { get; set; } = string.Empty;

    /// <summary>ベース名前空間の工場出荷既定（分割時は {base}.Entities 等のフォールバック元になるため接尾辞なし）</summary>
    public const string DefaultBaseNamespace = "Generated";

    /// <summary>非分割時の出力ファイル名の工場出荷既定</summary>
    public const string DefaultOutputFilePath = "QuickEREntities.g.cs";

    /// <summary>工場出荷既定の設定を生成する（クリア／初回起動で使う）</summary>
    public static CSharpGenerationSettings CreateDefault() => new();
}

/// <summary>C# コード生成ダイアログ設定を JSON ファイルへ保存・読込するストア</summary>
public class CSharpGenerationSettingsStore : JsonSettingsStore<CSharpGenerationSettings>
{
    /// <summary>既定の保存先（%APPDATA%\QuickER）で設定ストアを生成する</summary>
    public CSharpGenerationSettingsStore()
        : base("csharp-generation.json") { }

    /// <summary>保存先フォルダを指定して設定ストアを生成する（テスト用）</summary>
    public CSharpGenerationSettingsStore(string folder)
        : base("csharp-generation.json", folder) { }
}
