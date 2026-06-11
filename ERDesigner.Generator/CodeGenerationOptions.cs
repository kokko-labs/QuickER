namespace ERDesigner.Generator;

/// <summary>
/// C# コード生成の動作を制御するオプション
/// </summary>
/// <remarks>
/// 生成対象（Entity / EditModel / Mapper / Repository）の選択と、
/// 出力先・属性付与の有無を指定する。全プロパティは <c>init</c> 専用で、生成中に変化しない
/// </remarks>
public sealed class CodeGenerationOptions
{
    /// <summary>生成コードを配置する名前空間名。空白の場合はビルダー側で既定値 "Generated" にフォールバックする</summary>
    public string NamespaceName { get; init; } = "Generated";

    /// <summary>出力ファイル名。".g.cs" で終わらない場合はサービス側で補正される</summary>
    public string OutputFileName { get; init; } = "ErDesignerEntities.g.cs";

    /// <summary>エンティティクラスを生成するかどうか</summary>
    public bool GenerateEntityClasses { get; init; } = true;

    /// <summary>WPF バインディング向けの EditModel クラスを生成するかどうか</summary>
    public bool GenerateEditModels { get; init; } = true;

    /// <summary>Entity と EditModel を相互変換する Mapper クラスを生成するかどうか</summary>
    public bool GenerateMappers { get; init; } = true;

    /// <summary>SQL Server 向けの Repository クラス群（インターフェース・基底クラス・DI 拡張を含む）を生成するかどうか</summary>
    public bool GenerateRepositories { get; init; } = true;

    /// <summary>[Table] [Key] [Column] [Required] [MaxLength] などのデータアノテーション属性を付与するかどうか</summary>
    public bool IncludeDataAnnotations { get; init; } = true;

    /// <summary>親参照ナビゲーションへ [JsonIgnore] を付与するかどうか（JSON シリアライズ時の循環参照対策）</summary>
    public bool IncludeJsonIgnoreOnParentNavigation { get; init; } = true;
}
