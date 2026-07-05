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

    /// <summary>自作 SQL Server 実装（<c>Microsoft.Data.SqlClient</c> 依存）の Repository クラス群を生成するかどうか</summary>
    /// <remarks>
    /// SqlServerRepository 基底・各エンティティ実装・接続ファクトリ・SqlExecutor・SqlExpressionTranslator・
    /// <c>AddGeneratedRepositories</c> を生成する。共通契約（インターフェイス・SqlQuery・メタデータ等）は
    /// <see cref="GenerateEfCore"/> と共有し、どちらか一方が ON なら生成される
    /// </remarks>
    public bool GenerateRepositories { get; init; } = true;

    /// <summary>
    /// 自作 Repository の生成方言（後方互換の単一指定。実効値は <see cref="EffectiveRepositoryDialects"/> が解決する）。
    /// </summary>
    /// <remarks>
    /// 既定は <c>"sqlserver"</c>（現行の自作 SQL Server 実装）。テンプレートはこの値で方言別の識別子クォート・
    /// ADO 型・SQL 句を吐き分ける。複数方言を同時生成する場合は <see cref="RepositoryDialects"/> を使う。
    /// 両者を指定した場合は <see cref="RepositoryDialects"/>（リスト）を優先する（設定ファイル・CLI 互換のため
    /// 単一指定を残す）。対応方言は <see cref="SupportedRepositoryDialects"/> を参照（GUI / CLI 共通）。
    /// </remarks>
    public string RepositoryDialect { get; init; } = "sqlserver";

    /// <summary>
    /// 自作 Repository を同時生成する方言の一覧（複数指定で 1 回の生成に複数方言実装を同梱する）。
    /// </summary>
    /// <remarks>
    /// <c>null</c> または空のときは後方互換のため <see cref="RepositoryDialect"/>（単一）へフォールバックする。
    /// 指定時はこちらを優先する。実効値の解決・正規化（重複排除・未対応方言の検証）は
    /// <see cref="EffectiveRepositoryDialects"/> に 1 箇所へ集約する。
    /// </remarks>
    public IReadOnlyList<string>? RepositoryDialects { get; init; }

    /// <summary>
    /// 実効的な自作 Repository 生成方言の一覧を解決する（唯一の正）。
    /// </summary>
    /// <remarks>
    /// 解決規則:
    /// <list type="number">
    ///   <item><see cref="RepositoryDialects"/> が非空ならそれを、空/未指定なら <see cref="RepositoryDialect"/> の単一を採る（リスト優先）</item>
    ///   <item>各要素を Trim し、空要素は除去する</item>
    ///   <item>大文字小文字を無視して重複を除去する（初出の表記を保持し、指定順を維持する）</item>
    ///   <item>未対応方言（<see cref="SupportedRepositoryDialects"/> 外）が含まれる場合は <see cref="ArgumentException"/> を投げる</item>
    ///   <item>結果が空になった場合は既定 <c>"sqlserver"</c> の単一を返す（従来挙動の保険）</item>
    /// </list>
    /// </remarks>
    public IReadOnlyList<string> EffectiveRepositoryDialects
    {
        get
        {
            var source = RepositoryDialects is { Count: > 0 }
                ? RepositoryDialects
                : [RepositoryDialect];

            var resolved = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in source)
            {
                var value = raw?.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!SupportedRepositoryDialects.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"未対応の Repository 方言: '{value}'。対応方言: {string.Join(", ", SupportedRepositoryDialects)}"
                    );
                }

                if (seen.Add(value))
                {
                    resolved.Add(value);
                }
            }

            return resolved.Count > 0 ? resolved : ["sqlserver"];
        }
    }

    /// <summary>
    /// 自作 Repository が対応する生成方言の一覧（プロバイダ名と同一の識別子。例: <c>"sqlserver"</c>, <c>"sqlite"</c>）。
    /// </summary>
    /// <remarks>
    /// GUI（生成ダイアログの選択可否判定）と CLI（未対応方言の早期エラー）が単一ソースとして参照する。
    /// PostgreSQL / MySQL / Oracle は将来対応予定のためここには含めない。<c>QuickER.Generator</c> は DB 非依存を保つため、
    /// ここに置くのは文字列識別子の一覧のみで、各プロバイダの実装や型情報は一切参照しない。
    /// </remarks>
    public static IReadOnlyList<string> SupportedRepositoryDialects { get; } =
    ["sqlserver", "sqlite"];

    /// <summary>
    /// EF Core 用コード（DbContext・Fluent API 構成・EF 版 Repository 実装）を生成するかどうか。
    /// </summary>
    /// <remarks>
    /// 生成される DbContext は既存 Entity をそのまま既存スキーマへ接続する用途（方言非依存・1 本）で、
    /// スキーマ作成（Migrations / EnsureCreated）は範囲外とする。<see cref="GenerateRepositories"/> とは独立に選べ、
    /// EF 単独出力時は自作 SQL Server 実装（<c>Microsoft.Data.SqlClient</c> 依存）を一切含まない。
    /// 共通契約（インターフェイス・SqlQuery・メタデータ等）は <see cref="GenerateRepositories"/> と共有する
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
