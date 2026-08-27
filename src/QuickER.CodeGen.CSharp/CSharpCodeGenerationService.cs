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

        // インメモリ Repository とランタイムのパッケージ参照モードは併用できる（インメモリ基盤の固定 infra
        // ＝InMemoryDataStore・InMemoryRepository 基底・保存ステージングをパッケージ QuickER.Runtime.InMemory へ
        // 切り出したため、参照先を失わない）。per-entity のインメモリ実装・シーダー・DI 登録はスキーマ依存物として
        // パッケージモードでも常に生成側へ出力する。

        // ランタイムのパッケージ参照モードと EF Core 生成は併用できる（EF Core 固定 infra を TContext ジェネリック化した
        // ことで、EF Core エンジン（EfCoreRepository / EfCoreSqlExecutor 等）は具象 QuickErDbContext を参照しなくなった）。
        // スキーマ依存物（QuickErDbContext・Fluent 構成・EfCore{Entity}Repository・AddGeneratedEfCoreRepositories）は
        // パッケージモードでも常に生成側に出力し、EF Core 固定 infra はパッケージ QuickER.Runtime.EntityFrameworkCore が担う。
        // なお EF Core と QuickER 版 Repository のマルチターゲット（実効方言 2 つ以上）の排他は別理由（契約の型同一性）で上に残す。

        // マルチ辞書が渡されているときは方言間の C# 型不一致を検証し、[SqlColumnType] を sqlserver 辞書から補完する
        var columnTypes = primaryColumnTypes;

        if (columnTypesByDialect is not null)
        {
            // 行バージョン列は方言によって型が食い違う（sqlserver: byte[] / sqlite: DateTime）が、統一先が
            // 一意に決まるため不一致エラーにせず、行バージョンとして解決した方言の型へ共有 Entity を寄せる
            var rowVersions = MultiDialectTypeReconciler.ReconcileRowVersionTypes(
                diagram,
                effectiveDialects,
                primaryColumnTypes,
                columnTypesByDialect
            );
            MultiDialectTypeReconciler.DiagnoseTypeMismatches(
                diagram,
                effectiveDialects,
                columnTypesByDialect,
                rowVersions.UnifiedColumnIds,
                diagnostics
            );
            columnTypes = MultiDialectTypeReconciler.SupplementSqlColumnTypes(
                rowVersions.ColumnTypes,
                columnTypesByDialect
            );

            AddMultiTargetRowVersionInfo(rowVersions, diagnostics);
        }

        // 版列の本数は型解決の結果（CSharpTypeInfo.IsRowVersion）で決まるため、列型辞書が確定した後で検証する
        ValidateRowVersionColumns(diagram, columnTypes, diagnostics);

        // 無制限バイナリ列かどうかも型解決の結果（CSharpTypeInfo.IsUnboundedBinary）で決まるため同じ位置で検証する
        ValidateUnboundedBinaryPrimaryKeys(diagram, columnTypes, options, diagnostics);

        // 同期支援の前提（方言構成・Repository 実装・行バージョン列の存在）も列型辞書が確定した後で検証する
        ValidateSyncSupport(diagram, columnTypes, options, effectiveDialects, diagnostics);

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
        // 通常生成では空リスト（ヘッダに追加行なし）。案内はファイル横断で同一のため 1 度だけ組み立てる。
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

        // 同期支援が有効なとき、実際に同期対象になったテーブルを FK 順のまま Info 診断で通知する
        // （対象は「Repository 契約が生成される単一主キーのテーブル」という導出条件なので、どのテーブルが
        //   入ったかは生成物を読むまで分からない）。rowversion 列を持たないテーブルは後勝ち専用として名指しする
        if (options.GenerateSyncSupport && model.SyncTables.Count > 0)
        {
            var syncTableList = string.Join(
                Environment.NewLine,
                model.SyncTables.Select(table =>
                    $"  {table.TableName}（{table.EntityClassName}）"
                    + (
                        table.IsVersionless
                            ? Strings.CodeGen_Info_SyncSupportVersionlessTableSuffix
                            : string.Empty
                    )
                )
            );
            diagnostics.Add(
                GenerationDiagnostic.Info(
                    string.Format(
                        Strings.CodeGen_Info_SyncSupportTables,
                        Environment.NewLine + syncTableList
                    )
                )
            );

            // 同期対象テーブルに除外列があるときだけ、「通常の同期では運ばれない列」を名指しで通知する。
            // 除外オプションが OFF の図・除外列を持つ同期対象テーブルが 1 つも無い図では発火しない
            // （＝知らせるべき乖離が実在しないため。全体の除外列一覧は別の Info が既に出している）。
            var syncBinaryLines = model
                .SyncTables.Where(table => table.BinaryColumnPropertyNames.Count > 0)
                .Select(table =>
                    $"  {table.TableName}: {string.Join(", ", table.BinaryColumnPropertyNames)}"
                )
                .ToList();

            if (syncBinaryLines.Count > 0)
            {
                diagnostics.Add(
                    GenerationDiagnostic.Info(
                        string.Format(
                            Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns,
                            Environment.NewLine + string.Join(Environment.NewLine, syncBinaryLines)
                        )
                    )
                );
            }
        }

        // 出力ファイルの構成（非分割=1 ファイル、分割=カテゴリごと、マルチ方言=契約＋方言別実装）を決め、
        // 各ファイルを範囲を絞って描画する。1 ファイルに複数スペック（非分割マルチ方言）が対応する場合は連結する。
        var specs = GeneratedFilePlanner.Plan(options);
        var files = RenderFiles(model, options, specs, packageGuidanceLines);

        // API リファレンス Markdown（既定 OFF）。ON のとき、その図のスキーマに即した英語の .g.md を追加する。
        // ここは検証エラーで早期 return した後の経路のため、Files が空になる場合は Markdown も出ない（自然に乗る）。
        if (options.GenerateApiDocs)
        {
            // 出力先サブフォルダ（ApiDocsDirectory）。空白は既定＝出力ディレクトリ直下（null）
            var apiDocsDirectory = string.IsNullOrWhiteSpace(options.ApiDocsDirectory)
                ? null
                : options.ApiDocsDirectory.Trim();

            files.Add(
                new GeneratedFile
                {
                    FileName = ApiDocsFileName(options),
                    RelativeDirectory = apiDocsDirectory,
                    Content = _apiDocRenderer.Render(model, options, ApiDocLanguage.English),
                }
            );

            // IncludeJapaneseApiDocs が ON のときだけ、日本語版（.ja.g.md）を併産する。
            if (options.IncludeJapaneseApiDocs)
            {
                files.Add(
                    new GeneratedFile
                    {
                        FileName = JapaneseApiDocsFileName(options),
                        RelativeDirectory = apiDocsDirectory,
                        Content = _apiDocRenderer.Render(model, options, ApiDocLanguage.Japanese),
                    }
                );
            }
        }

        return new CodeGenerationResult { Files = files, Diagnostics = diagnostics };
    }

    /// <summary>
    /// マルチターゲットで行バージョン列の型を統一したとき、方言間の意味の違いを Info 診断で通知する。
    /// </summary>
    /// <remarks>
    /// 「共有 Entity は 1 つの <c>byte[]</c> プロパティだが、並行性トークンとして扱うのは行バージョンとして
    /// 解決した方言の Repository だけで、他方言では通常のバイナリ列（INSERT / UPDATE で書き込む・版ガードなし）になる」
    /// という非対称は生成物のどこにも書かれないため、生成時に一度だけ明示する。
    /// 統一対象が 1 つも無いとき（単一方言・行バージョン列のない図）は何も出さない。
    /// 診断は行バージョンとして解決した方言（owner）ごとに 1 件出し、その方言が採番する列だけを並べる
    /// （行バージョンを解決できる方言が 2 つ以上になったとき、最初の 1 列の owner で全列を括ると
    /// 実際には別方言が採番する列まで誤った方言名で通知してしまうため）。owner が 1 つの通常ケースでは
    /// 1 件・同一文面になる。
    /// </remarks>
    private static void AddMultiTargetRowVersionInfo(
        MultiDialectTypeReconciler.RowVersionReconciliation rowVersions,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (var group in rowVersions.Groups)
        {
            // ダイアログ／CLI で 1 行 1 列に見えるよう、導入文の後に改行＋インデント 2 スペースで各列を並べる
            var columnList = string.Join(
                Environment.NewLine,
                group.Lines.Select(line => "  " + line)
            );
            diagnostics.Add(
                GenerationDiagnostic.Info(
                    string.Format(
                        Strings.CodeGen_Info_MultiTargetRowVersionColumns,
                        group.Dialect,
                        Environment.NewLine + columnList
                    )
                )
            );
        }
    }

    /// <summary>
    /// ファイルスペック群を描画し、同一ファイル名のスペックを 1 ファイルへ連結する。
    /// </summary>
    /// <remarks>
    /// 1 ファイル 1 スペックの場合はヘッダ＋file-scoped namespace で出す。
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
                        // 層別出力の層フォルダをそのまま引き継ぐ（層別でなければ null＝出力ディレクトリ直下）
                        RelativeDirectory = members[0].RelativeDirectory,
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
            // 案内コメントを差し込む（各行に // 接頭辞）。通常生成では空リストで追加行なし。
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
                    // 連結出力（非分割マルチ方言）は層別出力と組み合わさらないため実質 null だが、先頭スペックから引き継ぐ
                    RelativeDirectory = members[0].RelativeDirectory,
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
            // 同期支援は Sync バケットを含むスペックだけが出力する
            // （分割時は Repositories.Sync.g.cs＋Runtime.Sync.g.cs・非分割時は本体ファイルへ同居）
            Sync = spec.Buckets.Contains(GenerationBucket.Sync),
            // リモート面の HTTP クライアントは Http バケットを含むスペックだけが出力する
            // （分割時は Repositories.Http.g.cs・非分割時は本体スペックに同居し従来位置へ描画される）
            RenderHttpClient = spec.Buckets.Contains(GenerationBucket.Http),
            // パッケージ参照モードでは固定 infra（契約・方言エンジン・EntityBase/属性/VO 基底 等）を出力せず、
            // 生成コードはパッケージ QuickER.Runtime.* の型を using で参照する。スキーマ依存物（Entity/EditModel/
            // Mapper/VO 具象/I{Entity}Repository/エンティティ別実装/DI 登録）は本フラグに依らず出力する。
            // 分割時は固定 infra を Runtime 系ファイルへ集約するため、スペック側でも出し分ける
            // （非分割は 1 ファイルへ全部入るため spec 側は既定の true）。
            EmitSharedInfra = spec.EmitSharedInfra && !options.UseRuntimePackages,
            // スキーマ依存物（per-entity・DI 登録・DbContext）は固定 infra 専用ファイル以外が出力する
            // （非分割・スキーマ依存ファイルとも true。パッケージ用ソースの書き出しは RuntimePackageSourceRenderer が false）。
            EmitSchemaDependent = spec.EmitSchemaDependent,
            // 固定 infra の可視性。層別出力は生成物を複数プロジェクト（別アセンブリ）へ分けるため、
            // NuGet パッケージ配布（RuntimePackageSourceRenderer）と同じ public にする＝EditModel の
            // Owner/OwnerModel・IncludeNode・CascadeNavigation 等を別層の生成物が参照できる
            // （単一アセンブリ配置＝非分割・通常分割は internal）。
            InfraVisibility = options.LayeredOutput ? "public" : "internal",
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
    /// （Mapper は EditModel が必要、Repository / EF Core / インメモリは DataAnnotations が必要、
    /// リモート対応は Repository 契約が必要）、
    /// 名前空間オプションの形式不正、エンティティクラス名の衝突、列由来プロパティ名の衝突。
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
        // 前提となる Entity 生成も常に満たされる（＝「生成対象なし」「Repository は Entity 必須」の検証は不要）。

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

        // リモート面（インターフェイス・HTTP クライアント／サーバー）は Repository 契約を拡張する形で出力される。
        // 契約が無い構成では計画（GeneratedFilePlanner）が該当バケットを丸ごと落とすため、指定しても診断ゼロで
        // 何も生成されない＝「ON にしたのに出ない」が黙って通る。GUI はチェック欄ごと隠すので、実際に踏むのは
        // CLI / MCP / 手書き config だが、指定が無視される事実は生成前に伝える
        if (
            (options.GenerateRemoteContracts || options.GenerateRemoteServices)
            && !options.GeneratesRepositoryContract
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_RemoteRequiresContract)
            );
        }

        ValidateNamespaces(options, diagnostics);
        ValidateLayerDirectories(options, diagnostics);
        ValidateApiDocsDirectory(options, diagnostics);
        ValidateApiDocsFileName(options, diagnostics);

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

        ValidateEntityClassNameUniqueness(diagram, diagnostics);
        ValidateColumnPropertyNameUniqueness(diagram, diagnostics);
    }

    /// <summary>
    /// rowversion 列の置き方を検証する（1 エンティティに 2 本以上ないこと・主キー列が rowversion でないこと）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 版の読み書き（<c>RowVersionCollector</c> / <c>RemoteEntityGraph</c> / インメモリの版スタンプ）は
    /// 「1 型につき版列は 1 本」を前提に <c>FirstOrDefault</c> で先頭 1 本だけを見る。SQL Server も 1 テーブルに
    /// 1 本しか許さないため実 DB では成立しない構成だが、図の上では 2 本置けてしまい、そのまま生成すると
    /// 「どちらが版か」が黙って決まった生成物が出て DDL 適用で初めて落ちる。生成時に止めて理由を示す。
    /// </para>
    /// <para>
    /// 主キー列が rowversion の場合も同様に止める。rowversion は DB 採番のため
    /// <c>[StoreGeneratedColumn]</c> が付き <c>EntitySaveMetadata</c> が INSERT / UPDATE の対象から外すので、
    /// 生成される INSERT はキー列を送らず、しかも「行の同一性」が更新のたびに変わる値に乗る。
    /// DB 取込では自然に発生し得る構成（版列を主キーに含めた既存スキーマ）なのに、
    /// 黙って通ると退化した SQL やインメモリ辞書のキー破壊まで到達する。
    /// </para>
    /// <para>
    /// 判定は型マッパーの解決結果（<see cref="CSharpTypeInfo.IsRowVersion"/>）＝<c>[StoreGeneratedColumn]</c> の付与条件と同一。
    /// </para>
    /// </remarks>
    private static void ValidateRowVersionColumns(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (var entity in diagram.Entities)
        {
            var rowVersionColumns = entity
                .Columns.Where(column =>
                    columnTypes.TryGetValue(column.Id, out var typeInfo) && typeInfo.IsRowVersion
                )
                .ToList();

            if (rowVersionColumns.Count > 1)
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_MultipleRowVersionColumns,
                            entity.TableName,
                            string.Join(
                                ", ",
                                rowVersionColumns.Select(column => $"'{column.Name}'")
                            )
                        )
                    )
                );
            }

            foreach (var column in rowVersionColumns.Where(column => column.IsPrimaryKey))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_PrimaryKeyRowVersionColumn,
                            entity.TableName,
                            column.Name
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// 同期支援（<see cref="CodeGenerationOptions.GenerateSyncSupport"/>）の前提条件を検証する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前提は 3 つ。(1) 実効方言がちょうど <c>sqlserver</c>（サーバー）と <c>sqlite</c>（ローカル）の 2 つであること
    /// ＝差分走査は SQL Server の <c>rowversion</c> と <c>MIN_ACTIVE_ROWVERSION()</c> に、ミラー列の書き込みは
    /// SQLite が同じ列を通常列として扱うことに、それぞれ依存している。(2) QuickER 版 Repository の実装を生成すること
    /// ＝エンジンはその読み書き経路そのものを使う。(3) 同期可能なテーブル（Repository 契約が生成される単一主キーの
    /// テーブル）が 1 つ以上あること＝1 つも無ければ同期対象が空の生成物が黙って出てしまう。rowversion 列の有無は
    /// 対象かどうかを決めず、モード（版あり＝増分＋競合検出／版なし＝後勝ち・全量）を決める。
    /// </para>
    /// <para>
    /// 無制限バイナリ列の除外との併用は<b>止めない</b>。除外列は行の転送に載らないだけで、blob は列単位の
    /// ストリーミングコピー（実行時引数 <c>SyncOptions.IncludeUnboundedBinary</c>）で運べる。何が運ばれないかは
    /// エラーでなく Info 診断で伝える（生成後に <c>model.SyncTables</c> から組み立てる）。
    /// </para>
    /// </remarks>
    private static void ValidateSyncSupport(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options,
        IReadOnlyList<string> effectiveDialects,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GenerateSyncSupport)
        {
            return;
        }

        if (!options.GenerateRepositories)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_SyncSupportRequiresRepositories)
            );
        }
        else if (
            effectiveDialects.Count != 2
            || !effectiveDialects.Contains("sqlserver", StringComparer.OrdinalIgnoreCase)
            || !effectiveDialects.Contains("sqlite", StringComparer.OrdinalIgnoreCase)
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_SyncSupportRequiresDialects,
                        string.Join(", ", effectiveDialects)
                    )
                )
            );
        }

        // 対象は「Repository 契約が生成される（単一主キーの）テーブル」。rowversion 列の有無はモード素材であって
        // 対象かどうかを決めない（版なしテーブルは後勝ちランでのみ同期される）
        var hasEligibleTable = diagram.Entities.Any(entity =>
            entity.Columns.Count(column => column.IsPrimaryKey) == 1
        );

        if (!hasEligibleTable)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_SyncSupportRequiresEligibleTables)
            );
        }
    }

    /// <summary>
    /// 主キー列が無制限バイナリ列でないことを検証する（無制限バイナリ列の除外が有効なときのみ）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ExcludeUnboundedBinaryColumns</c> が有効なとき、無制限バイナリ列は
    /// <c>EntitySaveMetadata.SelectProperties</c> から外れる（SELECT で読まない）。その列が主キーだと
    /// <c>GetByIdAsync</c> / <c>GetAllAsync</c> がキー未設定のエンティティを返し、それを起点にした保存・更新が
    /// 別の行を対象にする。読み取り経路が黙って壊れる形なので生成時に止める。
    /// </para>
    /// <para>
    /// 除外オプションが無効なら無制限バイナリ列も通常列として全経路で読み書きされるため実害がなく、
    /// 診断は発火させない（オプション非依存で常時エラーにすると、従来生成できていた図が理由なく止まる）。
    /// 判定は型マッパーの解決結果（<see cref="CSharpTypeInfo.IsUnboundedBinary"/>）＝
    /// <c>[UnboundedBinaryColumn]</c> の付与条件と同一。
    /// </para>
    /// </remarks>
    private static void ValidateUnboundedBinaryPrimaryKeys(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.ExcludeUnboundedBinaryColumns)
        {
            return;
        }

        foreach (var entity in diagram.Entities)
        {
            var offenders = entity.Columns.Where(column =>
                column.IsPrimaryKey
                && columnTypes.TryGetValue(column.Id, out var typeInfo)
                && typeInfo.IsUnboundedBinary
            );

            foreach (var column in offenders)
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_PrimaryKeyUnboundedBinaryColumn,
                            entity.TableName,
                            column.Name
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// エンティティクラス名（テーブル名由来）が図の中で一意であることを検証する
    /// </summary>
    /// <remarks>
    /// テーブル名は単数形化してからクラス名にするため、<c>customer</c> と <c>customers</c> のように
    /// 綴りが違うテーブルでも同じクラス名（<c>CustomerEntity</c>）になりうる。生成 Entity は partial クラスのため
    /// 同名クラスは 1 つへ統合され、EditModel の partial メソッド重複などでコンパイル不能な出力が
    /// 診断なしに書き出される。ここで衝突を検出して生成を止める。
    /// EditModel / Mapper / VO も同じ基底名から導出されるため、Entity クラス名の一意性検証で足りる
    /// （射影 DTO 名の衝突は <see cref="CSharpGenerationModelBuilder"/> のクエリ検証が別途担う）。
    /// </remarks>
    private static void ValidateEntityClassNameUniqueness(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var converter = new CSharpNameConverter();
        // 図の並び順で最初に現れたクラス名から順に報告するため、挿入順を保つ辞書へ集約する
        var tablesByClassName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (
            var entity in diagram.Entities.Where(entity =>
                !string.IsNullOrWhiteSpace(entity.TableName)
            )
        )
        {
            var className = converter.ToEntityClassName(entity.TableName);

            if (!tablesByClassName.TryGetValue(className, out var tables))
            {
                tables = [];
                tablesByClassName.Add(className, tables);
                order.Add(className);
            }

            tables.Add(entity.TableName);
        }

        foreach (var className in order.Where(name => tablesByClassName[name].Count > 1))
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_EntityClassNameCollision,
                        className,
                        string.Join(", ", tablesByClassName[className].Select(name => $"'{name}'"))
                    )
                )
            );
        }
    }

    /// <summary>
    /// 列由来のプロパティ名がエンティティごとに一意であることを検証する
    /// </summary>
    /// <remarks>
    /// 列名はパスカルケースへ正規化してからプロパティ名にするため、<c>user-id</c> / <c>user_id</c> / <c>USER_ID</c> の
    /// ように綴りが違う列でも同じプロパティ名（<c>UserId</c>）になりうる。同一エンティティ内で衝突すると
    /// Entity / EditModel に同名メンバーが重複宣言され（CS0102）、EditModel の partial メソッドも二重宣言になる
    /// （CS0111）ため、コンパイル不能な出力が診断なしに書き出される。ここで衝突を検出して生成を止める。
    /// 衝突判定はエンティティ単位＝別テーブルの同名列は別クラスのメンバーになるため衝突ではない。
    /// なお、生成メンバー名の衝突全般（EditModel の派生名 <c>Binding…</c> / <c>_…</c> / <c>…Snapshot</c>、
    /// 列とナビゲーションの衝突）は <c>CSharpGenerationModelBuilder</c> のシンボル表検証が担う。本メソッドは
    /// 最頻の誤り（同名列）へ「どの列同士か」を挙げた具体的なメッセージで先に答える早期検証として残す。
    /// ここでエラーになるとビルダーへ到達しないため、両者の診断が同時に出ることはない。
    /// </remarks>
    private static void ValidateColumnPropertyNameUniqueness(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var converter = new CSharpNameConverter();

        foreach (
            var entity in diagram.Entities.Where(entity =>
                !string.IsNullOrWhiteSpace(entity.TableName)
            )
        )
        {
            // 列の並び順で最初に現れたプロパティ名から順に報告するため、挿入順を保つ辞書へ集約する
            var columnsByPropertyName = new Dictionary<string, List<string>>(
                StringComparer.Ordinal
            );
            var order = new List<string>();

            foreach (
                var column in entity.Columns.Where(column =>
                    !string.IsNullOrWhiteSpace(column.Name)
                )
            )
            {
                var propertyName = converter.ToPropertyName(column.Name);

                if (!columnsByPropertyName.TryGetValue(propertyName, out var columns))
                {
                    columns = [];
                    columnsByPropertyName.Add(propertyName, columns);
                    order.Add(propertyName);
                }

                columns.Add(column.Name);
            }

            foreach (var propertyName in order.Where(name => columnsByPropertyName[name].Count > 1))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_ColumnPropertyNameCollision,
                            entity.TableName,
                            propertyName,
                            string.Join(
                                ", ",
                                columnsByPropertyName[propertyName].Select(name => $"'{name}'")
                            )
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// 名前空間オプションが C# の名前空間として妥当かを検証する
    /// </summary>
    /// <remarks>
    /// 判定は GUI の入力検証と同じ <see cref="CSharpNamespaceValidator"/>（単一正本）で行い、CLI / MCP でも
    /// 同じ規則を効かせる。空白のオプションは既定値（<c>Generated</c> / <c>{root}.{接尾辞}</c>）へ
    /// フォールバックするため検証対象外。カテゴリ別名前空間は実際に使われる構成でのみ検証する
    /// ＝分割時（<see cref="CodeGenerationOptions.SplitFilesByCategory"/>）の有効バケットのみ。
    /// ただし Repository 名前空間は非分割のマルチ方言レイアウトでも使われるため、
    /// Repository バケットが有効なら分割の有無に依らず検証する。
    /// </remarks>
    private static void ValidateNamespaces(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        AddInvalidNamespaceDiagnostic(
            nameof(CodeGenerationOptions.RootNamespace),
            options.RootNamespace,
            diagnostics
        );

        var activeBuckets = GeneratedFilePlanner.ActiveBuckets(options);

        (GenerationBucket Bucket, string Name, string? Value)[] categoryNamespaces =
        [
            (
                GenerationBucket.Runtime,
                nameof(CodeGenerationOptions.RuntimeNamespace),
                options.RuntimeNamespace
            ),
            (
                GenerationBucket.Entity,
                nameof(CodeGenerationOptions.EntityNamespace),
                options.EntityNamespace
            ),
            (
                GenerationBucket.EditModel,
                nameof(CodeGenerationOptions.EditModelNamespace),
                options.EditModelNamespace
            ),
            (
                GenerationBucket.Mapper,
                nameof(CodeGenerationOptions.MapperNamespace),
                options.MapperNamespace
            ),
            (
                GenerationBucket.Repository,
                nameof(CodeGenerationOptions.RepositoryNamespace),
                options.RepositoryNamespace
            ),
            (
                GenerationBucket.ValueObject,
                nameof(CodeGenerationOptions.ValueObjectNamespace),
                options.ValueObjectNamespace
            ),
        ];

        foreach (var target in categoryNamespaces)
        {
            var used =
                activeBuckets.Contains(target.Bucket)
                && (
                    options.EffectiveSplitFilesByCategory
                    || target.Bucket == GenerationBucket.Repository
                );

            if (used)
            {
                AddInvalidNamespaceDiagnostic(target.Name, target.Value, diagnostics);
            }
        }
    }

    /// <summary>
    /// 層別出力（<see cref="CodeGenerationOptions.LayeredOutput"/>）の層フォルダパスを検証する
    /// </summary>
    /// <remarks>
    /// 判定は書き出し時の防御と同じ <see cref="LayerDirectoryValidator"/>（単一正本）。空白のオプションは
    /// 既定フォルダ名（Domain 等）へフォールバックするため検証対象外。名前空間検証と同じく
    /// 「実際に使われる層」だけを検証する（例: リモートサービスなしの構成ではサーバー層フォルダを検証しない）＝
    /// 使われる層は計画（<see cref="GeneratedFilePlanner.Plan"/>）から導く。
    /// </remarks>
    private static void ValidateLayerDirectories(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.LayeredOutput)
        {
            return;
        }

        var usedLayers = GeneratedFilePlanner
            .Plan(options)
            .Select(GeneratedFilePlanner.LayerOf)
            .ToHashSet();

        // 並びはドメイン→プレゼンテーション→インフラストラクチャ→サーバー＝GeneratedLayer の宣言順・
        // GUI の層フォルダ欄と同じ順。複数の層フォルダが同時に不正なとき、診断の列挙順が画面の欄順と食い違わない
        (GeneratedLayer Layer, string Name, string? Value)[] layerDirectories =
        [
            (
                GeneratedLayer.Domain,
                nameof(CodeGenerationOptions.DomainLayerDirectory),
                options.DomainLayerDirectory
            ),
            (
                GeneratedLayer.Presentation,
                nameof(CodeGenerationOptions.PresentationLayerDirectory),
                options.PresentationLayerDirectory
            ),
            (
                GeneratedLayer.Infrastructure,
                nameof(CodeGenerationOptions.InfrastructureLayerDirectory),
                options.InfrastructureLayerDirectory
            ),
            (
                GeneratedLayer.Server,
                nameof(CodeGenerationOptions.ServerLayerDirectory),
                options.ServerLayerDirectory
            ),
        ];

        foreach (var target in layerDirectories)
        {
            if (!usedLayers.Contains(target.Layer))
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(target.Value)
                && !LayerDirectoryValidator.IsValid(target.Value)
            )
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_InvalidLayerDirectory,
                            target.Name,
                            target.Value.Trim()
                        )
                    )
                );

                continue;
            }

            // 層別出力は名前空間の既定を層フォルダから導出する（フォルダ追従）ため、その層で導出が実際に
            // 使われる（＝明示の名前空間オプションで全バケットが賄われていない）場合は、フォルダが C# の
            // 名前空間として成立することも要求する（ハイフン等はパスとしては合法でも名前空間になれない）。
            if (
                LayerNamespaceDerivationUsed(options, target.Layer)
                && !CSharpNamespaceValidator.IsValid(
                    GeneratedFilePlanner.LayerNamespaceRoot(options, target.Layer)
                )
            )
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_LayerDirectoryNotNamespace,
                            target.Name,
                            GeneratedFilePlanner.ResolveLayerDirectory(options, target.Layer),
                            GeneratedFilePlanner.LayerNamespaceRoot(options, target.Layer)
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// API リファレンスの出力先サブフォルダ（<see cref="CodeGenerationOptions.ApiDocsDirectory"/>）を検証する
    /// </summary>
    /// <remarks>
    /// 判定は層フォルダ・書き出し防御と同じ <see cref="LayerDirectoryValidator"/>（単一正本）。
    /// 層別出力に依らず、API リファレンスを出力する構成でのみ検証する（空白は既定＝直下のため対象外）。
    /// ドキュメントは C# コードでないため名前空間導出の検証はない（パスの妥当性だけ）。
    /// </remarks>
    private static void ValidateApiDocsDirectory(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (
            options.GenerateApiDocs
            && !string.IsNullOrWhiteSpace(options.ApiDocsDirectory)
            && !LayerDirectoryValidator.IsValid(options.ApiDocsDirectory)
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_InvalidLayerDirectory,
                        nameof(CodeGenerationOptions.ApiDocsDirectory),
                        options.ApiDocsDirectory.Trim()
                    )
                )
            );
        }
    }

    /// <summary>
    /// API リファレンスの出力ファイル名（<see cref="CodeGenerationOptions.ApiDocsFileName"/>）を検証する
    /// </summary>
    /// <remarks>
    /// 許すのは「ディレクトリ要素を含まない単一のファイル名」だけ（置き場を決めるのは
    /// <see cref="CodeGenerationOptions.ApiDocsDirectory"/> の役割）。<see cref="GeneratedFileWriter"/> は
    /// <c>Path.GetFileName</c> でディレクトリ要素を落とすため、診断がないとパス付きの指定が黙って別の場所へ
    /// 落ちるのでなく黙って無視される＝指定と結果が食い違う。空白は既定（導出）のため対象外。
    /// </remarks>
    private static void ValidateApiDocsFileName(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (
            options.GenerateApiDocs
            && !string.IsNullOrWhiteSpace(options.ApiDocsFileName)
            && !IsValidApiDocsFileName(options.ApiDocsFileName)
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_InvalidApiDocsFileName,
                        options.ApiDocsFileName.Trim()
                    )
                )
            );
        }
    }

    /// <summary>
    /// API リファレンスの出力ファイル名として妥当か（単一のファイル名で、ベース名が空でないか）を判定する
    /// </summary>
    /// <remarks>
    /// パス区切りとドライブ指定は <see cref="LayerDirectoryValidator"/> と同じ理由で明示的に拒否する
    /// （<c>Path.GetInvalidFileNameChars</c> は Windows 以外では区切り文字を含まない）。
    /// </remarks>
    private static bool IsValidApiDocsFileName(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains(':'))
        {
            return false;
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        // 拡張子だけの指定（".g.md" 等）はベース名が空＝隠しファイルになるため拒否する
        return StripApiDocsSuffix(trimmed).Length > 0;
    }

    /// <summary>
    /// 層別出力時、その層で「フォルダ由来の名前空間導出」が実際に使われるかを判定する
    /// </summary>
    /// <remarks>
    /// 明示の名前空間オプションを持たないバケット（EF Core / インメモリ / 同期 / HTTP / サーバー実装）と、
    /// QuickER 版 Repository の方言別実装（{インフラ層ルート}.SqlServer 等へ常に導出）は無条件に導出を使う。
    /// 明示オプションを持つバケットは、そのオプションが空白のときだけ導出へフォールバックする。
    /// すべて明示指定で賄われている層は、フォルダが名前空間になれなくてもエラーにしない（パス検証だけで足りる）。
    /// </remarks>
    private static bool LayerNamespaceDerivationUsed(
        CodeGenerationOptions options,
        GeneratedLayer layer
    )
    {
        // 方言別実装（Repositories.{方言}.g.cs / Runtime.{方言}.g.cs）は常にインフラ層ルートから導出する
        if (
            layer == GeneratedLayer.Infrastructure
            && options.GenerateRepositories
            && options.GeneratesRepositoryContract
        )
        {
            return true;
        }

        var active = GeneratedFilePlanner.ActiveBuckets(options).ToHashSet();

        // サーバー実装（RemoteServer バケット）は ActiveBuckets に載らない専用スペックのため個別に加える
        if (options.GenerateRemoteServices && active.Contains(GenerationBucket.Repository))
        {
            active.Add(GenerationBucket.RemoteServer);
        }

        return active.Any(bucket =>
            GeneratedFilePlanner.LayerOfBucket(bucket) == layer
            && string.IsNullOrWhiteSpace(
                bucket switch
                {
                    GenerationBucket.Runtime => options.RuntimeNamespace,
                    GenerationBucket.ValueObject => options.ValueObjectNamespace,
                    GenerationBucket.Entity => options.EntityNamespace,
                    GenerationBucket.EditModel => options.EditModelNamespace,
                    GenerationBucket.Mapper => options.MapperNamespace,
                    GenerationBucket.Repository => options.RepositoryNamespace,
                    // 明示オプションを持たないバケットは常に導出（空白扱い）
                    _ => null,
                }
            )
        );
    }

    /// <summary>名前空間オプションが非空かつ不正な形式のときだけエラー診断を追加する</summary>
    private static void AddInvalidNamespaceDiagnostic(
        string optionName,
        string? value,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (string.IsNullOrWhiteSpace(value) || CSharpNamespaceValidator.IsValid(value))
        {
            return;
        }

        diagnostics.Add(
            GenerationDiagnostic.Error(
                string.Format(Strings.CodeGen_Error_InvalidNamespace, optionName, value.Trim())
            )
        );
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

    /// <summary>分割出力時の API リファレンス Markdown の固定ベース名（カテゴリ別固定名の流儀に合わせる）</summary>
    private const string SplitApiDocsBaseName = "ApiDocs";

    /// <summary>
    /// 現在のオプションで実際に出力される API リファレンス Markdown（英語版）のファイル名を返す。
    /// </summary>
    /// <remarks>
    /// GUI が「未指定のときに使われる既定名」をプレースホルダとして見せるための公開口。
    /// 生成本体と同じ導出（<see cref="ApiDocsFileName"/>）を通すため、表示と実出力がずれない。
    /// </remarks>
    public static string ResolveApiDocsFileName(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ApiDocsFileName(options);
    }

    /// <summary>API リファレンス Markdown の拡張子サフィックス（英語版）</summary>
    private const string ApiDocsSuffix = ".g.md";

    /// <summary>API リファレンス Markdown の拡張子サフィックス（日本語版）</summary>
    private const string JapaneseApiDocsSuffix = ".ja.g.md";

    /// <summary>
    /// API リファレンス Markdown の出力ファイル名を導出する。
    /// </summary>
    /// <remarks>
    /// <see cref="CodeGenerationOptions.ApiDocsFileName"/> の指定があればそれ（拡張子は ".g.md" へ正規化）を
    /// 出力モードに依らず優先する。空白なら既定の導出で、非分割時は <see cref="SanitizeFileName"/> で
    /// ".g.cs" に正規化した <see cref="CodeGenerationOptions.OutputFileName"/> の
    /// 末尾を ".g.md" に置換する（例: <c>EcOrder.g.cs</c> → <c>EcOrder.g.md</c>＝生成コードと同じベース名・拡張子で
    /// ドキュメントと判別する）。分割時（<see cref="CodeGenerationOptions.SplitFilesByCategory"/>）は <c>Entities.g.cs</c> 等の
    /// カテゴリ別固定名と同じ流儀の固定名 <c>ApiDocs.g.md</c> にする（分割時の OutputFileName は .cs / .md とも出力名に
    /// 関与しない＝GUI / CLI で仕様が揃う）。<see cref="GeneratedFileWriter"/> は ".g.md" 末尾の書き出しを許可する
    /// （手書きファイルの保護は維持する）。
    /// </remarks>
    private static string ApiDocsFileName(CodeGenerationOptions options) =>
        ApiDocsBaseName(options) + ApiDocsSuffix;

    /// <summary>
    /// 日本語版 API リファレンス Markdown の出力ファイル名を導出する。
    /// </summary>
    /// <remarks>
    /// <see cref="ApiDocsFileName"/> の英語版（<c>.g.md</c>）に対し、日本語版は同じベース名へ <c>.ja.g.md</c> を付す
    /// （非分割時の例: <c>EcOrder.g.cs</c> → <c>EcOrder.ja.g.md</c>・分割時は固定名 <c>ApiDocs.ja.g.md</c>・
    /// <see cref="CodeGenerationOptions.ApiDocsFileName"/> 指定時はその指定名のベース名）。
    /// <c>.g.md</c> で終わるため <see cref="GeneratedFileWriter"/> の書き出しガードも通る。
    /// </remarks>
    private static string JapaneseApiDocsFileName(CodeGenerationOptions options) =>
        ApiDocsBaseName(options) + JapaneseApiDocsSuffix;

    /// <summary>
    /// API リファレンス Markdown のベース名（拡張子を除いた部分）を決める。
    /// </summary>
    /// <remarks>
    /// 明示指定（<see cref="CodeGenerationOptions.ApiDocsFileName"/>）があればそのベース名を出力モードに依らず使い、
    /// 空白なら「分割＝固定名 <c>ApiDocs</c>／非分割＝出力ファイル名のベース名」へフォールバックする。
    /// 英語版・日本語版はここで決まる同じベース名に拡張子だけを付け替える（両者の名前がずれない唯一の正）。
    /// </remarks>
    private static string ApiDocsBaseName(CodeGenerationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiDocsFileName))
        {
            return StripApiDocsSuffix(options.ApiDocsFileName.Trim());
        }

        if (options.EffectiveSplitFilesByCategory)
        {
            return SplitApiDocsBaseName;
        }

        return GeneratedFilePlanner.StripGeneratedCSharpSuffix(
            SanitizeFileName(options.OutputFileName)
        );
    }

    /// <summary>
    /// API リファレンスの出力ファイル名指定から、拡張子を除いたベース名を取り出す。
    /// </summary>
    /// <remarks>
    /// 受け付ける表記は <c>EcOrder</c> / <c>EcOrder.md</c> / <c>EcOrder.g.md</c> / <c>EcOrder.g.cs</c> のいずれでも
    /// ベース名は <c>EcOrder</c>。<c>.g.md</c> ／ <c>.g.cs</c> は 2 段拡張子のため
    /// <see cref="Path.GetFileNameWithoutExtension(string)"/> だけでは <c>EcOrder.g</c> が残る（先に落とす）。
    /// </remarks>
    private static string StripApiDocsSuffix(string fileName)
    {
        if (fileName.EndsWith(ApiDocsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^ApiDocsSuffix.Length];
        }

        var stripped = GeneratedFilePlanner.StripGeneratedCSharpSuffix(fileName);
        return stripped.Length == fileName.Length
            ? Path.GetFileNameWithoutExtension(fileName)
            : stripped;
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
