using QuickER.Model;

namespace QuickER.Generator;

/// <summary>
/// ER 図定義から C# コードを生成するライブラリのエントリポイント
/// </summary>
/// <remarks>
/// 処理は「検証 → 生成モデル構築（<see cref="CSharpGenerationModelBuilder"/>）→
/// ファイル構成決定（<see cref="GeneratedFilePlanner"/>）→ テンプレート描画（<see cref="ScribanCSharpRenderer"/>）」の段階で進む。
/// 非分割時は全クラスを単一の .g.cs ファイルへ、分割時はカテゴリ（＋共有基盤 Runtime）ごとに別ファイル・別名前空間で出力する
/// </remarks>
public sealed class CSharpCodeGenerationService
{
    /// <summary>ER 図定義をテンプレート入力用の生成モデルへ変換するビルダー</summary>
    private readonly CSharpGenerationModelBuilder _modelBuilder = new();

    /// <summary>生成モデルを C# ソース文字列へ描画する Scriban レンダラー</summary>
    private readonly ScribanCSharpRenderer _renderer = new();

    /// <summary>
    /// ER 図定義から C# コードを生成する
    /// </summary>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="columnTypes">カラム ID → 解決済み C# 型情報。生成器は DB 非依存のため、SQL 型の解決は
    /// 呼び出し側（<c>QuickER.SqlServer</c> 等のプロバイダ）が行って渡す</param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    /// <returns>生成ファイルと診断情報。検証でエラーがあった場合はファイルを含まず診断のみを返す</returns>
    public CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(columnTypes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<GenerationDiagnostic>();
        Validate(diagram, options, diagnostics);

        // エラー検出時は生成処理に進まず、診断のみを返して呼び出し側に修正を促す
        if (
            diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error)
        )
        {
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        var model = _modelBuilder.Build(diagram, columnTypes, options, diagnostics);

        // 出力ファイルの構成（非分割=1 ファイル、分割=カテゴリごと）を決め、各ファイルを範囲を絞って描画する
        var files = GeneratedFilePlanner
            .Plan(options)
            .Select(spec => new GeneratedFile
            {
                FileName = SanitizeFileName(spec.FileName),
                Content = _renderer.Render(model, options, BuildScope(spec, model.Usings, options)),
            })
            .ToList();

        return new CodeGenerationResult { Files = files, Diagnostics = diagnostics };
    }

    /// <summary>
    /// ファイル計画から描画スコープ（名前空間・using・出力バケット）を組み立てる
    /// </summary>
    /// <remarks>
    /// using はモデルの System フレームワーク using に、他ファイルの名前空間（クロス参照）を加える。
    /// <c>// &lt;auto-generated /&gt;</c> 出力により未使用 using 警告は抑止されるため、過剰な付与は無害
    /// </remarks>
    private static RenderScope BuildScope(
        GeneratedFileSpec spec,
        IReadOnlyList<string> systemUsings,
        CodeGenerationOptions options
    )
    {
        var usings = new List<string>(systemUsings);
        foreach (var crossNamespace in spec.CrossNamespaceUsings)
        {
            if (!usings.Contains(crossNamespace))
            {
                usings.Add(crossNamespace);
            }
        }

        return new RenderScope
        {
            NamespaceName = spec.NamespaceName,
            Usings = usings,
            Runtime = spec.Buckets.Contains(GenerationBucket.Runtime),
            ValueObjects = spec.Buckets.Contains(GenerationBucket.ValueObject),
            Entities = spec.Buckets.Contains(GenerationBucket.Entity),
            EditModels = spec.Buckets.Contains(GenerationBucket.EditModel),
            Mappers = spec.Buckets.Contains(GenerationBucket.Mapper),
            Repositories = spec.Buckets.Contains(GenerationBucket.Repository),
            EfCore = spec.Buckets.Contains(GenerationBucket.EfCore),
            // 自作 SQL Server 実装は Repository バケットを含むファイルにのみ、かつ GenerateRepositories が有効なときだけ出力する
            SqlServerImpl =
                options.GenerateRepositories && spec.Buckets.Contains(GenerationBucket.Repository),
        };
    }

    /// <summary>
    /// 生成前の入力検証を行い、問題を診断リストへ追加する
    /// </summary>
    /// <remarks>
    /// エラー: 生成対象が一つもない、エンティティが存在しない、テーブル名が空、
    /// 生成対象間の依存違反（Mapper は Entity+EditModel、Repository / EF Core は Entity と DataAnnotations が必要）。
    /// 警告: 複合主キー（[Key] 属性の生成が最小限になる）
    /// </remarks>
    private static void Validate(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (
            !options.GenerateEntityClasses
            && !options.GenerateEditModels
            && !options.GenerateMappers
            && !options.GenerateRepositories
            && !options.GenerateEfCore
        )
        {
            diagnostics.Add(
                Error(
                    "Entity / EditModel / Mapper / Repository / EF Core のいずれも生成対象になっていません。少なくとも一つを有効にしてください。"
                )
            );
        }

        // Mapper は Entity クラスと EditModel クラスの両方を参照するため、単独生成するとコンパイル不能になる
        if (
            options.GenerateMappers
            && (!options.GenerateEntityClasses || !options.GenerateEditModels)
        )
        {
            diagnostics.Add(
                Error(
                    "Mapper の生成には Entity クラスと EditModel クラスの両方が必要です。両方を生成対象に含めてください。"
                )
            );
        }

        // Repository・EF Core はともに Entity クラス（および共通契約）を参照するため、Entity 生成が必須
        if (
            (options.GenerateRepositories || options.GenerateEfCore)
            && !options.GenerateEntityClasses
        )
        {
            diagnostics.Add(
                Error(
                    "Repository / EF Core の生成には Entity クラスが必要です。Entity を生成対象に含めてください。"
                )
            );
        }

        // Repository の SQL 組み立ておよび EF Core のマッピング（EntitySaveMetadata）は [Table] / [Key] / [Column]
        // 属性をリフレクションで参照するため、DataAnnotations を無効にすると実行時に初期化例外となる。生成前に検出する
        if (
            (options.GenerateRepositories || options.GenerateEfCore)
            && !options.IncludeDataAnnotations
        )
        {
            diagnostics.Add(
                Error(
                    "Repository / EF Core は [Table] / [Key] / [Column] 属性を利用するため、データアノテーションの付与が必要です。データアノテーションを有効にしてください。"
                )
            );
        }

        if (diagram.Entities.Count == 0)
        {
            diagnostics.Add(Error("ER 図にエンティティがありません。"));
        }

        foreach (var entity in diagram.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.TableName))
            {
                diagnostics.Add(Error("テーブル名が空のエンティティがあります。"));
            }

            if (entity.Columns.Count(column => column.IsPrimaryKey) > 1)
            {
                diagnostics.Add(
                    Warning(
                        $"テーブル '{entity.TableName}' は複合主キーのため [Key] 属性生成は最小限になります。MVP では単一主キーを推奨します。"
                    )
                );
            }
        }
    }

    /// <summary>
    /// 出力ファイル名を ".g.cs" 拡張子に正規化する
    /// </summary>
    /// <remarks>
    /// <see cref="GeneratedFileWriter"/> が ".g.cs" 以外の上書きを拒否するため、
    /// 空白なら既定名、それ以外は拡張子を ".g.cs" に置き換えて手書きファイルの誤上書きを防ぐ
    /// </remarks>
    private static string SanitizeFileName(string fileName)
    {
        var value = string.IsNullOrWhiteSpace(fileName) ? "QuickEREntities.g.cs" : fileName.Trim();
        return value.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            ? value
            : Path.GetFileNameWithoutExtension(value) + ".g.cs";
    }

    /// <summary>エラー診断を作成する</summary>
    private static GenerationDiagnostic Error(string message) =>
        new() { Severity = GenerationDiagnosticSeverity.Error, Message = message };

    /// <summary>警告診断を作成する</summary>
    private static GenerationDiagnostic Warning(string message) =>
        new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };
}
