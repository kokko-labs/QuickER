namespace QuickER.Generator;

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
    public string OutputFileName { get; init; } = "QuickEREntities.g.cs";

    /// <summary>エンティティクラスを生成するかどうか</summary>
    public bool GenerateEntityClasses { get; init; } = true;

    /// <summary>WPF バインディング向けの EditModel クラスを生成するかどうか</summary>
    public bool GenerateEditModels { get; init; } = true;

    /// <summary>Entity と EditModel を相互変換する Mapper クラスを生成するかどうか</summary>
    public bool GenerateMappers { get; init; } = true;

    /// <summary>SQL Server 向けの Repository クラス群（インターフェース・基底クラス・DI 拡張を含む）を生成するかどうか</summary>
    public bool GenerateRepositories { get; init; } = true;

    /// <summary>
    /// EF Core 用コード（DbContext と Fluent API 構成）を生成するかどうか。
    /// </summary>
    /// <remarks>
    /// 生成される DbContext は既存 Entity をそのまま既存スキーマへ接続する用途（方言非依存・1 本）で、
    /// スキーマ作成（Migrations / EnsureCreated）は範囲外とする。<see cref="GenerateRepositories"/> が必要
    /// </remarks>
    public bool GenerateEfCore { get; init; }

    /// <summary>[Table] [Key] [Column] [Required] [MaxLength] などのデータアノテーション属性を付与するかどうか</summary>
    public bool IncludeDataAnnotations { get; init; } = true;

    /// <summary>親参照ナビゲーションへ [JsonIgnore] を付与するかどうか（JSON シリアライズ時の循環参照対策）</summary>
    public bool IncludeJsonIgnoreOnParentNavigation { get; init; } = true;

    /// <summary>全カラムを値オブジェクト（Value Object）として生成するかどうか。ON で Entity/EditModel/Mapper/Repository のプロパティ型が VO になる</summary>
    public bool GenerateValueObjects { get; init; }

    /// <summary>string 型の主キーを GuidKey 値オブジェクト（GUID を文字列保持・無引数生成で自動採番）にするかどうか。<see cref="GenerateValueObjects"/> が ON かつ PK が string のときのみ適用</summary>
    public bool UseGuidKeyForStringPrimaryKey { get; init; }

    /// <summary>
    /// 出力をカテゴリ（Entity / EditModel / Mapper / Repository / ValueObject / Runtime）ごとに別ファイル・別名前空間へ分割するかどうか
    /// </summary>
    /// <remarks>
    /// false（既定）: 全クラスを <see cref="NamespaceName"/> の単一ファイル（<see cref="OutputFileName"/>）へ出力する（従来動作）。
    /// true: 生成対象カテゴリと共有基盤（Runtime）をそれぞれ 1 カテゴリ 1 ファイルへ出力し、各ファイルに個別の名前空間を与える
    /// </remarks>
    public bool SplitFilesByCategory { get; init; }

    /// <summary>分割時の共有基盤（基底クラス・属性・VO 基底・JSON コンバータ）の名前空間。空なら <c>{NamespaceName}.Runtime</c> へフォールバックする</summary>
    public string? RuntimeNamespace { get; init; }

    /// <summary>分割時の Entity クラスの名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? EntityNamespace { get; init; }

    /// <summary>分割時の EditModel クラスの名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? EditModelNamespace { get; init; }

    /// <summary>分割時の Mapper クラスの名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? MapperNamespace { get; init; }

    /// <summary>分割時の Repository クラス群の名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? RepositoryNamespace { get; init; }

    /// <summary>分割時の値オブジェクトクラスの名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? ValueObjectNamespace { get; init; }

    /// <summary>分割時の EfCore（DbContext・構成）クラスの名前空間。空なら <see cref="NamespaceName"/> へフォールバックする</summary>
    public string? EfCoreNamespace { get; init; }
}
