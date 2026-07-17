using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

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

    /// <summary>生成モデルを API リファレンス Markdown へ描画する Scriban レンダラー</summary>
    private readonly ApiReferenceDocRenderer _apiDocRenderer = new();

    /// <summary>
    /// ER 図定義から C# コードを生成する（単一方言。共有バケット・Repository 実装ともに <paramref name="columnTypes"/> を使う）
    /// </summary>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="columnTypes">カラム ID → 解決済み C# 型情報。生成器は DB 非依存のため、SQL 型の解決は
    /// 呼び出し側（<c>QuickER.SqlServer</c> 等のプロバイダ）が行って渡す</param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    /// <returns>生成ファイルと診断情報。検証でエラーがあった場合はファイルを含まず診断のみを返す</returns>
    /// <remarks>
    /// 後方互換のためのオーバーロード。実効方言が複数ある場合でも各方言実装は同一の型辞書で解決されるため、
    /// マルチ辞書（方言ごとに解決した辞書）を渡したい場合は
    /// <see cref="Generate(ErDiagram, IReadOnlyDictionary{Guid, CSharpTypeInfo}, IReadOnlyDictionary{string, IReadOnlyDictionary{Guid, CSharpTypeInfo}}, CodeGenerationOptions)"/>
    /// を使う。
    /// </remarks>
    public CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(columnTypes);

        return Generate(diagram, columnTypes, columnTypesByDialect: null, options);
    }

    /// <summary>
    /// ER 図定義から C# コードを生成する（名前付きクエリの型トークン辞書つき・単一辞書）。
    /// </summary>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="columnTypes">カラム ID → 解決済み C# 型情報</param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    /// <param name="queryParameterTypes">
    /// 名前付きクエリの型トークン（例: <c>int32</c> / <c>string(50)</c>）→ 解決済み C# 型情報。
    /// 列型と同じく解決はプロバイダ側の責務（<c>QueryParameterTypeResolver</c>）。null なら空として扱い、
    /// クエリ定義がトークンを参照すると解決不能の診断エラーになる
    /// </param>
    public CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options,
        IReadOnlyDictionary<string, CSharpTypeInfo>? queryParameterTypes
    )
    {
        ArgumentNullException.ThrowIfNull(columnTypes);

        return Generate(
            diagram,
            columnTypes,
            columnTypesByDialect: null,
            options,
            queryParameterTypes
        );
    }

    /// <summary>
    /// ER 図定義から C# コードを生成する（マルチ辞書対応）。
    /// </summary>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="primaryColumnTypes">共有バケット（Entity / EditModel / Mapper / VO）に使う主辞書。図の方言で解決したもの</param>
    /// <param name="columnTypesByDialect">
    /// 方言名 → その方言で解決した列型辞書。各方言の Repository 実装バケットの型解決に使う。
    /// <c>null</c> のときはすべて <paramref name="primaryColumnTypes"/> を使う（単一方言・後方互換）。
    /// </param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    /// <returns>生成ファイルと診断情報。検証・型不一致でエラーがあった場合はファイルを含まず診断のみを返す</returns>
    /// <remarks>
    /// 生成器は DB 非依存を保つため、型解決（DB 型 → C# 型）は呼び出し側（プロバイダ）が方言ごとに行って渡す。
    /// 共有 Entity は 1 型のため、方言間で C# 型（型名・参照/値区分）が食い違うと生成物が壊れる。ここで不一致を
    /// 診断エラーにして黙って劣化させない。また sqlserver がターゲットに含まれる場合、<c>[SqlColumnType]</c> の
    /// メタ情報（SqlDbType・Size 等）を sqlserver 辞書から主辞書へ補完する（図の方言が非 sqlserver でも属性を出す）。
    /// </remarks>
    public CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> primaryColumnTypes,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<Guid, CSharpTypeInfo>
        >? columnTypesByDialect,
        CodeGenerationOptions options,
        IReadOnlyDictionary<string, CSharpTypeInfo>? queryParameterTypes = null
    )
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(primaryColumnTypes);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<GenerationDiagnostic>();
        Validate(diagram, options, diagnostics);

        // 実効方言の解決（未対応方言の指定は ArgumentException になるため、診断へ変換して返す）
        IReadOnlyList<string> effectiveDialects;

        try
        {
            effectiveDialects = options.EffectiveRepositoryDialects;
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(GenerationDiagnostic.Error(ex.Message));
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        // マルチ方言（実効方言 2 つ以上）レイアウトと EF Core 生成は併存できない。
        // マルチ方言では契約 namespace に ADO・方言 SQL を一切置かず、EntitySaveMetadata / EntityGraphSaver を
        // 各方言 namespace へ複製する（自方言メタデータ・自前キャッシュ）。一方 EF Core（方言非依存・契約 namespace）は
        // これらを参照するため、マルチ方言時は契約 namespace に該当型が存在せず解決不能になる。EF Core は QuickER
        // マルチターゲットと排他（GUI はラジオで排他）で、パリティ用の両 ON は単一方言でのみ意味を持つため、
        // GenerateRepositories の実効方言が 2 つ以上かつ GenerateEfCore のときは早期に診断エラーとする。
        if (options.GenerateRepositories && options.GenerateEfCore && effectiveDialects.Count >= 2)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_MultiTargetEfCoreExclusive)
            );
        }

        // インメモリ Repository はパッケージ参照モードと併用できない。インメモリ実行器（InMemoryQueryExecutor・
        // InMemoryDataStore）は生成側の固定 infra として出力され、パッケージ（QuickER.Runtime.*）には存在しない。
        // UseRuntimePackages は固定 infra を出力しないため、インメモリ実装が参照先を失いコンパイル不能になる。
        // よって併用指定は早期に診断エラーとする（インメモリはインライン既定＝固定 infra 同梱でのみ成立する）。
        if (options.GenerateInMemoryRepositories && options.UseRuntimePackages)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_InMemoryRuntimePackagesExclusive)
            );
        }

        // ランタイムのパッケージ参照モードと EF Core 生成は併用できる（EF Core 固定 infra を TContext ジェネリック化した
        // ことで、EF Core エンジン（EfCoreRepository / EfCoreSqlExecutor 等）は具象 QuickErDbContext を参照しなくなった）。
        // スキーマ依存物（QuickErDbContext・Fluent 構成・EfCore{Entity}Repository・AddGeneratedEfCoreRepositories）は
        // パッケージモードでも常に生成側に出力し、EF Core 固定 infra はパッケージ QuickER.Runtime.EntityFrameworkCore が担う。
        // なお EF Core と QuickER 版 Repository のマルチターゲット（実効方言 2 つ以上）の排他は別理由（契約の型同一性）で上に残す。

        // マルチ辞書が渡されているときは方言間の C# 型不一致を検証し、[SqlColumnType] を sqlserver 辞書から補完する
        var columnTypes = primaryColumnTypes;

        if (columnTypesByDialect is not null)
        {
            MultiDialectTypeReconciler.DiagnoseTypeMismatches(
                diagram,
                effectiveDialects,
                columnTypesByDialect,
                diagnostics
            );
            columnTypes = MultiDialectTypeReconciler.SupplementSqlColumnTypes(
                primaryColumnTypes,
                columnTypesByDialect
            );
        }

        // エラー検出時（検証・型不一致）は生成処理に進まず、診断のみを返して呼び出し側に修正を促す
        if (
            diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error)
        )
        {
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        var model = _modelBuilder.Build(
            diagram,
            columnTypes,
            options,
            diagnostics,
            queryParameterTypes
        );

        // 名前付きクエリの検証（メソッド名衝突・条件式・型トークン等）でエラーが出た場合もファイルを出さない
        if (
            diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error)
        )
        {
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        // パッケージ参照モードでは、各生成ファイルの先頭コメントへ必要な PackageReference を案内する。
        // 通常生成では空リスト（ヘッダに追加行なし＝バイト不変）。案内はファイル横断で同一のため 1 度だけ組み立てる。
        var packageGuidanceLines = options.UseRuntimePackages
            ? RuntimePackageReferenceGuidance.BuildGuidanceLines(
                options,
                RuntimePackages.ResolveGuidanceVersion()
            )
            : [];

        // 無制限バイナリ列の除外（ExcludeUnboundedBinaryColumns）が ON のとき、除外対象列の一覧を組み立てる
        // （Info 診断専用。生成コード側の可視化は各プロパティの [UnboundedBinaryColumn] 属性が担う）。
        var excludedColumnLines = options.ExcludeUnboundedBinaryColumns
            ? BuildExcludedColumnLines(model)
            : [];

        // 除外列が 1 つ以上あれば Info 診断で通知する（利用者へ「どの列が SELECT / UPDATE から外れたか」を明示）。
        // ダイアログ／CLI で 1 行 1 列に見えるよう、導入文の後に改行＋インデント 2 スペースで各列を並べる。
        if (excludedColumnLines.Count > 0)
        {
            var excludedColumnList = string.Join(
                Environment.NewLine,
                excludedColumnLines.Select(line => "  " + line)
            );
            diagnostics.Add(
                GenerationDiagnostic.Info(
                    string.Format(
                        Strings.CodeGen_Info_ExcludedUnboundedBinaryColumns,
                        Environment.NewLine + excludedColumnList
                    )
                )
            );
        }

        // 出力ファイルの構成（非分割=1 ファイル、分割=カテゴリごと、マルチ方言=契約＋方言別実装）を決め、
        // 各ファイルを範囲を絞って描画する。1 ファイルに複数スペック（非分割マルチ方言）が対応する場合は連結する。
        var specs = GeneratedFilePlanner.Plan(options);
        var files = RenderFiles(model, options, specs, packageGuidanceLines);

        // API リファレンス Markdown（既定 OFF）。ON のとき、その図のスキーマに即した英語の .g.md を追加する。
        // ここは検証エラーで早期 return した後の経路のため、Files が空になる場合は Markdown も出ない（自然に乗る）。
        if (options.GenerateApiDocs)
        {
            files.Add(
                new GeneratedFile
                {
                    FileName = ApiDocsFileName(options.OutputFileName),
                    Content = _apiDocRenderer.Render(model, options, ApiDocLanguage.English),
                }
            );

            // IncludeJapaneseApiDocs が ON のときだけ、日本語版 {ベース名}.ja.g.md を併産する。
            if (options.IncludeJapaneseApiDocs)
            {
                files.Add(
                    new GeneratedFile
                    {
                        FileName = JapaneseApiDocsFileName(options.OutputFileName),
                        Content = _apiDocRenderer.Render(model, options, ApiDocLanguage.Japanese),
                    }
                );
            }
        }

        return new CodeGenerationResult { Files = files, Diagnostics = diagnostics };
    }

    /// <summary>
    /// ファイルスペック群を描画し、同一ファイル名のスペックを 1 ファイルへ連結する。
    /// </summary>
    /// <remarks>
    /// 1 ファイル 1 スペックの場合は従来どおり（ヘッダ＋file-scoped namespace・バイト不変）。
    /// 同一ファイルへ複数スペック（非分割マルチ方言）が対応する場合は、using を全スペックの和集合として
    /// 先頭でまとめて出し、各スペックを block namespace で包んで連結する（using は namespace より前必須のため）。
    /// </remarks>
    private List<GeneratedFile> RenderFiles(
        CSharpGenerationModel model,
        CodeGenerationOptions options,
        IReadOnlyList<GeneratedFileSpec> specs,
        IReadOnlyList<string> packageGuidanceLines
    )
    {
        var files = new List<GeneratedFile>();

        foreach (var group in specs.GroupBy(spec => SanitizeFileName(spec.FileName)))
        {
            var members = group.ToList();

            // 1 ファイル 1 スペック: 従来経路（ヘッダあり・file-scoped namespace）
            if (members.Count == 1)
            {
                var scope = BuildScope(
                    members[0],
                    options,
                    blockNamespace: false,
                    renderHeader: true,
                    packageGuidanceLines,
                    blockUsings: []
                );
                files.Add(
                    new GeneratedFile
                    {
                        FileName = group.Key,
                        Content = _renderer.Render(model, options, scope),
                    }
                );

                continue;
            }

            // パッケージ参照モードのマルチ方言連結では、方言エンジンパッケージ（.SqlServer / .Sqlite）を
            // ファイル先頭で両方開くと ISqlConnectionFactory 等が方言間で曖昧参照になる。方言別実装スペックの
            // その using は各方言 namespace ブロックの内側へ限定し、先頭の共通 using からは除外する。
            var blockUsingsBySpec = members.ToDictionary(
                spec => spec,
                spec => ResolveDialectPackageBlockUsings(spec, options)
            );
            var topExcludedUsings = blockUsingsBySpec
                .Values.SelectMany(u => u)
                .ToHashSet(StringComparer.Ordinal);

            // 複数スペックを 1 ファイルへ: using を先頭で 1 回だけ出し、各スペックを block namespace で連結する
            var mergedUsings = MergeUsings(members, options)
                .Where(ns => !topExcludedUsings.Contains(ns))
                .ToList();
            // パッケージ参照モードでは、テンプレートのヘッダ経路（render_header）を通らないこの連結ヘッダにも
            // 案内コメントを差し込む（各行に // 接頭辞）。通常生成では空リストで追加行なし（バイト不変）。
            var guidanceComment = string.Concat(
                packageGuidanceLines.Select(line => $"// {line}" + Environment.NewLine)
            );
            var header =
                "// <auto-generated />"
                + Environment.NewLine
                + "#nullable enable"
                + Environment.NewLine
                + guidanceComment
                + Environment.NewLine
                + string.Concat(mergedUsings.Select(u => $"using {u};" + Environment.NewLine))
                + Environment.NewLine;

            var bodies = members.Select(spec =>
            {
                var scope = BuildScope(
                    spec,
                    options,
                    blockNamespace: true,
                    renderHeader: false,
                    packageGuidanceLines,
                    blockUsingsBySpec[spec]
                );
                return _renderer.Render(model, options, scope);
            });

            files.Add(
                new GeneratedFile
                {
                    FileName = group.Key,
                    Content = header + string.Join(Environment.NewLine, bodies),
                }
            );
        }

        return files;
    }

    /// <summary>連結ファイルの using を全スペックの和集合として解決する（先頭で 1 回だけ出すため）</summary>
    private static IReadOnlyList<string> MergeUsings(
        IReadOnlyList<GeneratedFileSpec> members,
        CodeGenerationOptions options
    )
    {
        var external = new HashSet<string>(StringComparer.Ordinal);
        var cross = new HashSet<string>(StringComparer.Ordinal);
        var ownNamespaces = members.Select(m => m.NamespaceName).ToHashSet(StringComparer.Ordinal);

        foreach (var member in members)
        {
            foreach (var u in GeneratedFileUsings.Resolve(member, options))
            {
                // クロス using（他 namespace 参照）は連結後に自ファイル内の namespace を指す場合があるため除外する
                if (member.CrossNamespaceUsings.Contains(u, StringComparer.Ordinal))
                {
                    cross.Add(u);
                }
                else
                {
                    external.Add(u);
                }
            }
        }

        // 連結後は各 namespace ブロックが同一ファイル内にあるため、自ファイル内 namespace への using は不要（除外）。
        // 外部 using の並び順規則は GeneratedFileUsings と共有する（単一ファイル・連結ファイルでバイト一致）。
        var ordered = GeneratedFileUsings.OrderExternalUsings(external);

        ordered.AddRange(
            cross.Where(ns => !ownNamespaces.Contains(ns)).OrderBy(ns => ns, StringComparer.Ordinal)
        );

        return ordered;
    }

    /// <summary>
    /// ファイル計画から描画スコープ（名前空間・using・出力バケット）を組み立てる
    /// </summary>
    /// <remarks>
    /// using は <see cref="GeneratedFileUsings"/> がバケット単位で解決する（そのファイルが含む全バケットの
    /// 外部 using の和集合＋依存グラフ由来のクロス名前空間 using）。これにより SqlClient / EntityFrameworkCore /
    /// DependencyInjection 等が、それらを使わないファイルへ漏れない。
    /// </remarks>
    private static RenderScope BuildScope(
        GeneratedFileSpec spec,
        CodeGenerationOptions options,
        bool blockNamespace,
        bool renderHeader,
        IReadOnlyList<string> packageGuidanceLines,
        IReadOnlyList<string> blockUsings
    )
    {
        var hasRepository = spec.Buckets.Contains(GenerationBucket.Repository);

        return new RenderScope
        {
            NamespaceName = spec.NamespaceName,
            Usings = GeneratedFileUsings.Resolve(spec, options),
            Runtime = spec.Buckets.Contains(GenerationBucket.Runtime),
            ValueObjects = spec.Buckets.Contains(GenerationBucket.ValueObject),
            Entities = spec.Buckets.Contains(GenerationBucket.Entity),
            EditModels = spec.Buckets.Contains(GenerationBucket.EditModel),
            Mappers = spec.Buckets.Contains(GenerationBucket.Mapper),
            EfCore = spec.Buckets.Contains(GenerationBucket.EfCore),
            // 共通契約は Repository バケットを含むスペックで出す。単一方言時は契約＋実装スペック（1 つ）で true、
            // マルチ方言時は契約スペック（ContractOnly=true）のみ true・方言実装スペックは false（契約を 1 回だけ出す）
            RenderContract = hasRepository && (spec.ContractOnly || !spec.MultiDialect),
            Dialect = spec.Dialect,
            MultiDialect = spec.MultiDialect,
            BlockNamespace = blockNamespace,
            RenderHeader = renderHeader,
            // 方言実装（ADO 依存）は Repository バケットを含み、契約のみでなく、GenerateRepositories が有効なときだけ出力する
            RepositoryImpl = options.GenerateRepositories && hasRepository && !spec.ContractOnly,
            // DB 非依存のインメモリ実装は独立バケット（InMemory）を含むスペックだけが出力する
            // （分割時は Repositories.InMemory.g.cs・非分割時は他バケットと同一ファイルへ連結）
            InMemory = spec.Buckets.Contains(GenerationBucket.InMemory),
            // リモート面のサーバー実装はサーバー専用スペック（{ベース名}.RemoteServer.g.cs）だけが出力する
            RemoteServer = spec.Buckets.Contains(GenerationBucket.RemoteServer),
            // パッケージ参照モードでは固定 infra（契約・方言エンジン・EntityBase/属性/VO 基底 等）を出力せず、
            // 生成コードはパッケージ QuickER.Runtime.* の型を using で参照する。スキーマ依存物（Entity/EditModel/
            // Mapper/VO 具象/I{Entity}Repository/エンティティ別実装/DI 登録）は本フラグに依らず出力する。
            EmitSharedInfra = !options.UseRuntimePackages,
            // ヘッダ（render_header=true のファイル）へ載せる案内行。renderHeader=false の連結スペックでは
            // テンプレート側で出さないため空でよいが、呼び出し側が共通で渡す（render_header 経路のみ描画する）。
            PackageGuidanceLines = renderHeader ? packageGuidanceLines : [],
            // ブロック名前空間の内側へ限定する方言エンジンパッケージ using（非分割マルチ方言のパッケージ参照モードのみ）。
            BlockUsings = blockUsings,
        };
    }

    /// <summary>
    /// パッケージ参照モードの非分割マルチ方言連結で、方言別実装スペックが自方言のブロック内に限定して開くべき
    /// 方言エンジンパッケージ名前空間（<c>QuickER.Runtime.SqlServer</c> / <c>QuickER.Runtime.Sqlite</c>）を返す。
    /// </summary>
    /// <remarks>
    /// パッケージ参照モードでない、または方言実装を出さないスペック（契約のみ・Repository 非生成）は空を返す。
    /// これにより先頭 using は両方言パッケージを含めず、各方言ブロックが自方言だけを開いて曖昧参照を避ける。
    /// </remarks>
    private static IReadOnlyList<string> ResolveDialectPackageBlockUsings(
        GeneratedFileSpec spec,
        CodeGenerationOptions options
    )
    {
        if (
            !options.UseRuntimePackages
            || !options.GenerateRepositories
            || spec.ContractOnly
            || !spec.Buckets.Contains(GenerationBucket.Repository)
        )
        {
            return [];
        }

        var package = string.Equals(spec.Dialect, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? RuntimePackages.Sqlite
            : RuntimePackages.SqlServer;

        return [package];
    }

    /// <summary>
    /// 生成前の入力検証を行い、問題を診断リストへ追加する
    /// </summary>
    /// <remarks>
    /// エラー: エンティティが存在しない、テーブル名が空、生成対象間の依存違反
    /// （Mapper は EditModel が必要、Repository / EF Core / インメモリは DataAnnotations が必要）。
    /// Entity は常時生成されるため「生成対象なし」「Repository は Entity 必須」は起こらない。
    /// 警告: 複合主キー（[Key] 属性の生成が最小限になる）
    /// </remarks>
    private static void Validate(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // Entity は常時生成されるため、生成対象が皆無になることはなく、Repository / EF Core / インメモリの
        // 前提となる Entity 生成も常に満たされる（かつての「生成対象なし」「Repository は Entity 必須」検証は不要になった）。

        // Mapper は Entity クラスと EditModel クラスの両方を参照する。Entity は常時生成されるため、
        // EditModel を出さないと単独生成になりコンパイル不能になる
        if (options.GenerateMappers && !options.GenerateEditModels)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_MapperRequiresEntityAndEditModel)
            );
        }

        // Repository の SQL 組み立て・EF Core・インメモリのマッピング（EntitySaveMetadata）は [Table] / [Key] / [Column]
        // 属性をリフレクションで参照するため、DataAnnotations を無効にすると実行時に初期化例外となる。生成前に検出する
        if (options.GeneratesRepositoryContract && !options.IncludeDataAnnotations)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_RepositoryRequiresDataAnnotations)
            );
        }

        if (diagram.Entities.Count == 0)
        {
            diagnostics.Add(GenerationDiagnostic.Error(Strings.CodeGen_Error_NoEntities));
        }

        foreach (var entity in diagram.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.TableName))
            {
                diagnostics.Add(GenerationDiagnostic.Error(Strings.CodeGen_Error_EmptyTableName));
            }

            if (entity.Columns.Count(column => column.IsPrimaryKey) > 1)
            {
                diagnostics.Add(
                    GenerationDiagnostic.Warning(
                        string.Format(Strings.CodeGen_Warning_CompositeKey, entity.TableName)
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
        return value.EndsWith(
            GeneratedFilePlanner.GeneratedCSharpSuffix,
            StringComparison.OrdinalIgnoreCase
        )
            ? value
            : Path.GetFileNameWithoutExtension(value) + GeneratedFilePlanner.GeneratedCSharpSuffix;
    }

    /// <summary>
    /// API リファレンス Markdown の出力ファイル名を、正規化済みの C# 出力ファイル名から導出する。
    /// </summary>
    /// <remarks>
    /// <see cref="SanitizeFileName"/> で ".g.cs" に正規化した名前の末尾を ".g.md" に置換する
    /// （例: <c>EcOrder.g.cs</c> → <c>EcOrder.g.md</c>＝生成コードと同じベース名・拡張子でドキュメントと判別する）。
    /// <see cref="GeneratedFileWriter"/> は ".g.md" 末尾の書き出しを許可する（手書きファイルの保護は維持する）。
    /// </remarks>
    private static string ApiDocsFileName(string outputFileName)
    {
        var normalized = SanitizeFileName(outputFileName);
        return GeneratedFilePlanner.StripGeneratedCSharpSuffix(normalized) + ".g.md";
    }

    /// <summary>
    /// 日本語版 API リファレンス Markdown の出力ファイル名を、正規化済みの C# 出力ファイル名から導出する。
    /// </summary>
    /// <remarks>
    /// <see cref="ApiDocsFileName"/> の英語版（<c>.g.md</c>）に対し、日本語版は <c>.ja.g.md</c> を付す
    /// （例: <c>EcOrder.g.cs</c> → <c>EcOrder.ja.g.md</c>）。<c>.g.md</c> で終わるため
    /// <see cref="GeneratedFileWriter"/> の書き出しガードも従来どおり通る。
    /// </remarks>
    private static string JapaneseApiDocsFileName(string outputFileName)
    {
        var normalized = SanitizeFileName(outputFileName);
        return GeneratedFilePlanner.StripGeneratedCSharpSuffix(normalized) + ".ja.g.md";
    }

    /// <summary>
    /// 無制限バイナリ列の除外対象一覧を <c>{EntityClass}.{Property}（{テーブル}.{列名}）</c> 形式の行で組み立てる。
    /// </summary>
    /// <remarks>
    /// 生成 Entity のプロパティのうち <see cref="CSharpPropertyModel.IsUnboundedBinary"/> のものを対象にする
    /// （マーカー属性 <c>[UnboundedBinaryColumn]</c> の付与対象と一致）。Info 診断のメッセージ組み立てに使う。
    /// </remarks>
    private static IReadOnlyList<string> BuildExcludedColumnLines(CSharpGenerationModel model) =>
        model
            .EntityClasses.SelectMany(entity =>
                entity
                    .Properties.Where(property => property.IsUnboundedBinary)
                    .Select(property =>
                        $"{entity.ClassName}.{property.PropertyName}（{entity.TableName}.{property.ColumnName}）"
                    )
            )
            .ToList();
}
