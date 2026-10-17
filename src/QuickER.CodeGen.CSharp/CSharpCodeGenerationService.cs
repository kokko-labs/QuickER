using System.Globalization;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Platform;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// ER 図定義から C# コードを生成するライブラリのエントリポイント
/// </summary>
/// <remarks>
/// 処理は「検証 → 生成モデル構築（<see cref="CSharpGenerationModelBuilder"/>）→
/// ファイル構成決定（<see cref="GeneratedFilePlanner"/>）→ テンプレート描画（<see cref="ScribanCSharpRenderer"/>）」の段階で進む。
/// 非分割時は全クラスを単一の .g.cs ファイルへ、分割時はカテゴリ（＋共有基盤 Runtime）ごとに別ファイル・別名前空間で出力する
/// </remarks>
public sealed partial class CSharpCodeGenerationService
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
    /// 呼び出し側（<c>QuickER.Provider.SqlServer</c> 等のプロバイダ）が行って渡す</param>
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

        // 実効方言の解決（未対応方言の指定は ArgumentException になるため、診断へ変換して返す）。
        // Validate より前に解決する＝層別出力の層フォルダ検証（ファイル計画経由）や名前空間検証も
        // 実効方言へ触れるため、後にすると未対応方言が診断でなく例外のまま呼び出し元へ抜ける
        IReadOnlyList<string> effectiveDialects;

        try
        {
            effectiveDialects = options.ResolveRepositoryDialects();
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(GenerationDiagnostic.Error(ex.Message));
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        Validate(diagram, options, diagnostics);

        // マルチ方言（実効方言 2 つ以上）レイアウトと EF Core 生成は併存できない。
        // マルチ方言では契約 namespace に ADO・方言 SQL を一切置かず、EntitySaveMetadata / EntityGraphSaver を
        // 各方言 namespace へ複製する（自方言メタデータ・自前キャッシュ）。一方 EF Core（方言非依存・契約 namespace）は
        // これらを参照するため、マルチ方言時は契約 namespace に該当型が存在せず解決不能になる。EF Core は QuickER
        // マルチターゲットと排他（GUI はラジオで排他）で、パリティ用の両 ON は単一方言でのみ意味を持つため、
        // GenerateRepositories の実効方言が 2 つ以上かつ GenerateEfCoreRepositories のときは早期に診断エラーとする。
        if (
            options.GenerateRepositories
            && options.GenerateEfCoreRepositories
            && effectiveDialects.Count >= 2
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_MultiTargetEfCoreExclusive)
            );
        }

        // インメモリ Repository とランタイムのパッケージ参照モードは併用できる（インメモリ基盤の固定 infra
        // ＝InMemoryDataStore・InMemoryRepositoryCore 基底・保存ステージングをパッケージ QuickER.Runtime.InMemory へ
        // 切り出したため、参照先を失わない）。per-entity のインメモリ実装・シーダー・DI 登録はスキーマ依存物として
        // パッケージモードでも常に生成側へ出力する。

        // ランタイムのパッケージ参照モードと EF Core 生成は併用できる（EF Core 固定 infra を TContext ジェネリック化した
        // ことで、EF Core エンジン（EfCoreRepositoryCore / EfCoreSqlExecutor 等）は具象 QuickErDbContext を参照しなくなった）。
        // スキーマ依存物（QuickErDbContext・Fluent 構成・EfCore{Entity}Repository・AddGeneratedEfCoreRepositories）は
        // パッケージモードでも常に生成側に出力し、EF Core 固定 infra はパッケージ QuickER.Runtime.EntityFrameworkCore が担う。
        // なお EF Core と QuickER 版 Repository のマルチターゲット（実効方言 2 つ以上）の排他は別理由（契約の型同一性）で上に残す。

        // マルチ辞書が渡されているときは方言間の C# 型不一致を検証し、[SqlColumnType] を sqlserver 辞書から補完する。
        // QuickER 版 Repository を生成しない構成（DB アクセスなし・EF Core 単独）では方言辞書を無視する＝
        // 方言指定は生成物に影響しないという既定（ResolveRepositoryDialects() の未検証フォールバックと同じ線）を
        // ここでも守る。無視しないと、呼び出し側が渡した隠れた 2 方言辞書が型統合（SQLite 図の timestamp が
        // byte[] 行バージョン化）・Info 診断・[SqlColumnType] 補完として単一方言の出力と食い違いを生む
        var columnTypes = primaryColumnTypes;

        if (columnTypesByDialect is not null && options.GenerateRepositories)
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

        // 計算列は型解決に依らず図（Column.IsComputed）で決まるが、診断の並びを揃えてここで検証する
        ValidateComputedPrimaryKeys(diagram, diagnostics);

        // 主キーの採番列（Column.IsIdentity）も同じ位置で検証する。計算列と違い生成は止めず警告にとどめる
        WarnIdentityPrimaryKeys(diagram, options, diagnostics);

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

        // 無制限バイナリ列の除外（ExcludeUnboundedBinaryColumns）が ON のとき、除外対象列を Info 診断で通知する
        // （利用者へ「どの列が SELECT / UPDATE から外れたか」を明示。生成コード側の可視化は各プロパティの
        //   [UnboundedBinaryColumn] 属性が担う）
        if (options.ExcludeUnboundedBinaryColumns)
        {
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_ExcludedUnboundedBinaryColumns,
                BuildExcludedColumnLines(model)
            );
        }

        // 計算列・生成列を Info 診断で通知する（利用者へ「どの列が INSERT / UPDATE から外れたか」を明示）。
        // オプションに依らず常に効く除外なので、除外の有無をここで一度だけ名指しする。
        AddListInfo(
            diagnostics,
            Strings.CodeGen_Info_ComputedColumns,
            BuildComputedColumnLines(model)
        );

        AddNotNullComputedColumnWarning(model, options, diagnostics);

        AddNotNullIdentityColumnWarning(model, options, diagnostics);

        // 主キーでない採番列も計算列と同じく常に効く除外なので、ここで一度だけ名指しする
        AddListInfo(
            diagnostics,
            Strings.CodeGen_Info_IdentityColumns,
            BuildIdentityColumnLines(model)
        );

        // 型表記が中立トークン経由で復元できない列（[DbColumnMeta] へ元の表記も刻んだ列）を Info 診断で通知する。
        // 「同義だが綴りの違う型」は生成物を読んでも気づけず、コード取込の往復で図の型表記が変わる／変わらない
        // の分かれ目になるため、どの列が特別扱いなのかを生成時に一度だけ明示する。属性を出さない構成
        // （IncludeDataAnnotations=false）では元表記も刻まれないので出さない
        if (options.IncludeDataAnnotations)
        {
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_NonCanonicalTypeSpellingColumns,
                BuildNonCanonicalSpellingLines(diagram, columnTypes)
            );
        }

        // EditModel に載らない列（配列列・inet の列）を含む UNIQUE 制約は EditModel の重複検証から外れる（その列が EditModel に載らないため、
        // 参照すると実在しないプロパティを指してコンパイルできない）。黙って検証を外さないよう名指しする
        if (options.GenerateEditModels)
        {
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_ArrayUniqueConstraintsNotValidated,
                BuildArrayUniqueConstraintLines(diagram, columnTypes)
            );
        }

        // 配列列（PostgreSQL の integer[] 等）は EditModel・値オブジェクトに載らない。
        // Entity・EF Core・インメモリ・リモート転送では通常どおり扱えるが、編集画面に出ない／
        // 値オブジェクトにならないことは生成物を読んだだけでは気づけないため名指しする
        if (options.GenerateEditModels || options.GenerateValueObjects)
        {
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_ArrayColumnsNotEditable,
                BuildColumnLines(diagram, columnTypes, typeInfo => typeInfo.IsArray)
            );

            // テキスト入力で扱えない型の列（PostgreSQL の inet）も同じ理由で EditModel・値オブジェクトから外れる
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_TextInputUnsupportedColumns,
                BuildColumnLines(diagram, columnTypes, typeInfo => typeInfo.IsTextInputUnsupported)
            );
        }

        // 型カタログが DB 型表記を解析できず string へフォールバックした列（CSharpTypeInfo.IsFallbackType）を
        // Info 診断で通知する。生成コードのプロパティ型が実際には解決できていないことに利用者が生成物を
        // 読んだだけでは気づけないため、方言に依らず常に検証する（IncludeDataAnnotations には依存しない＝
        // 属性の有無でなくプロパティ型そのものの話であるため）
        AddListInfo(
            diagnostics,
            Strings.CodeGen_Info_FallbackTypeColumns,
            BuildFallbackTypeLines(diagram, columnTypes)
        );

        // 負のスケールの decimal 列（PostgreSQL の numeric(10,-2)・Oracle の NUMBER(10,-2)）は、
        // 型マッパーが精度・スケールを読まない＝生成物に桁数の情報が一切載らない。DB 側は値を丸めるので、
        // 生成物を読んでも「丸められること」にも「検証が無いこと」にも気づけない
        AddListInfo(
            diagnostics,
            Strings.CodeGen_Info_NegativeScaleColumns,
            BuildNegativeScaleColumnLines(diagram)
        );

        // 同期支援が有効なとき、実際に同期対象になったテーブルを FK 順のまま Info 診断で通知する
        // （対象は「Repository 契約が生成される単一主キーのテーブル」という導出条件なので、どのテーブルが
        //   入ったかは生成物を読むまで分からない）。rowversion 列を持たないテーブルは後勝ち専用として名指しする
        if (options.GenerateSyncSupport)
        {
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_SyncSupportTables,
                model
                    .SyncTables.Select(table =>
                        $"{table.TableName} ({table.EntityClassName})"
                        + (
                            table.IsVersionless
                                ? Strings.CodeGen_Info_SyncSupportVersionlessTableSuffix
                                : string.Empty
                        )
                    )
                    .ToList()
            );

            // 同期対象テーブルに除外列があるときだけ、「通常の同期では運ばれない列」を名指しで通知する。
            // 除外オプションが OFF の図・除外列を持つ同期対象テーブルが 1 つも無い図では発火しない
            // （＝知らせるべき乖離が実在しないため。全体の除外列一覧は別の Info が既に出している）。
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_SyncSupportUnboundedBinaryColumns,
                model
                    .SyncTables.Where(table => table.BinaryColumnPropertyNames.Count > 0)
                    .Select(table =>
                        $"{table.TableName}: {string.Join(", ", table.BinaryColumnPropertyNames)}"
                    )
                    .ToList()
            );
        }
        // 出力ファイルの構成（非分割=1 ファイル、分割=カテゴリごと、マルチ方言=契約＋方言別実装）を決め、
        // 各ファイルを範囲を絞って描画する。1 ファイルに複数スペック（非分割マルチ方言）が対応する場合は連結する。
        var specs = GeneratedFilePlanner.Plan(options);
        var files = RenderFiles(model, options, specs, packageGuidanceLines);

        // 全 .g.cs のヘッダーへ生成元の版と内容ハッシュを刻む（再生成時の手編集検出に使う）。
        // 刻むのは描画・連結がすべて済んだ後の 1 箇所だけ＝ハッシュが最終的に書き出す本文と必ず一致する
        var generatorVersion = RuntimePackages.ResolveGuidanceVersion();
        files = files
            .Select(file => new GeneratedFile
            {
                FileName = file.FileName,
                RelativeDirectory = file.RelativeDirectory,
                Content = GeneratedContentHash.Stamp(file.Content, generatorVersion),
            })
            .ToList();

        // API リファレンス Markdown（既定 OFF）。ON のとき、その図のスキーマに即した .g.md / .ja.g.md を
        // ApiDocsLanguage が指す言語の分だけ追加する（英語→日本語の順）。
        // ここは検証エラーで早期 return した後の経路のため、Files が空になる場合は Markdown も出ない（自然に乗る）。
        if (options.GenerateApiDocs)
        {
            // 出力先サブフォルダ（ApiDocsSubdirectory）。空白は既定＝出力ディレクトリ直下（null）
            var apiDocsSubdirectory = string.IsNullOrWhiteSpace(options.ApiDocsSubdirectory)
                ? null
                : options.ApiDocsSubdirectory.Trim();

            foreach (var (language, fileName) in ApiDocsOutputs(options))
            {
                files.Add(
                    new GeneratedFile
                    {
                        FileName = fileName,
                        RelativeDirectory = apiDocsSubdirectory,
                        Content = _apiDocRenderer.Render(model, options, language),
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
            AddListInfo(
                diagnostics,
                Strings.CodeGen_Info_MultiTargetRowVersionColumns,
                group.Lines,
                group.Dialect
            );
        }
    }

    /// <summary>導入文に項目の一覧を添えた Info 診断を足す（一覧が空なら何もしない）</summary>
    /// <param name="diagnostics">診断の追加先</param>
    /// <param name="format">導入文の書式。最後の書式引数が一覧になる</param>
    /// <param name="lines">一覧の各行（インデントは付けずに渡す）</param>
    /// <param name="leadingArguments">一覧より前の書式引数</param>
    /// <remarks>
    /// ダイアログ／CLI で 1 行 1 項目に見えるよう、導入文の後に改行＋インデント 2 スペースで各行を並べる
    /// （一覧の書式はこの 1 か所だけが決める）。
    /// </remarks>
    private static void AddListInfo(
        ICollection<GenerationDiagnostic> diagnostics,
        string format,
        IReadOnlyCollection<string> lines,
        params object[] leadingArguments
    )
    {
        if (lines.Count == 0)
        {
            return;
        }

        var list =
            Environment.NewLine
            + string.Join(Environment.NewLine, lines.Select(line => "  " + line));
        diagnostics.Add(
            GenerationDiagnostic.Info(string.Format(format, [.. leadingArguments, list]))
        );
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

        foreach (var group in specs.GroupBy(spec => spec.FileName))
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
                GeneratedContentHash.AutoGeneratedLine
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
        // クロス using ごとに、それを要するスペックの名前空間を集める（using を落とせるかの判定に使う）
        var crossRequesters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var ownNamespaces = members.Select(m => m.NamespaceName).ToHashSet(StringComparer.Ordinal);

        foreach (var member in members)
        {
            foreach (var u in GeneratedFileUsings.Resolve(member, options))
            {
                // クロス using（他 namespace 参照）は連結後に自ファイル内の namespace を指す場合があるため別に集める
                if (member.CrossNamespaceUsings.Contains(u, StringComparer.Ordinal))
                {
                    if (!crossRequesters.TryGetValue(u, out var requesters))
                    {
                        requesters = [];
                        crossRequesters.Add(u, requesters);
                    }

                    requesters.Add(member.NamespaceName);
                }
                else
                {
                    external.Add(u);
                }
            }
        }

        // 外部 using の並び順規則は GeneratedFileUsings と共有する（単一ファイル・連結ファイルでバイト一致）。
        var ordered = GeneratedFileUsings.OrderExternalUsings(external);

        ordered.AddRange(
            crossRequesters
                .Where(entry =>
                    !IsResolvedByEnclosingNamespace(entry.Key, entry.Value, ownNamespaces)
                )
                .Select(entry => entry.Key)
                .OrderBy(ns => ns, StringComparer.Ordinal)
        );

        return ordered;
    }

    /// <summary>
    /// 連結ファイル内のクロス using が、using なしでも名前解決できるか（＝先頭の using から落としてよいか）を判定する
    /// </summary>
    /// <remarks>
    /// C# の名前解決が using なしで見に行くのは、自分の名前空間とその外側の名前空間だけ。
    /// 同じファイル内で宣言される名前空間でも、それを要するブロックの外側（自分自身かその祖先）でなければ解決できない
    /// （例: <c>namespace Data.Repos.SqlServer { }</c> から <c>MyApp</c> の型は見えない）。
    /// そのため「同じファイル内で宣言され、かつ要する全ブロックがその名前空間自身かその入れ子」のときだけ落とす。
    /// </remarks>
    private static bool IsResolvedByEnclosingNamespace(
        string usingNamespace,
        IReadOnlyList<string> requesterNamespaces,
        IReadOnlySet<string> ownNamespaces
    ) =>
        ownNamespaces.Contains(usingNamespace)
        && requesterNamespaces.All(requester =>
            string.Equals(requester, usingNamespace, StringComparison.Ordinal)
            || requester.StartsWith(usingNamespace + ".", StringComparison.Ordinal)
        );

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

        return [RuntimePackages.ForDialect(spec.Dialect)];
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
        if (options.GeneratesRemoteContracts && !options.GeneratesRepositoryContract)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(Strings.CodeGen_Error_RemoteRequiresContract)
            );
        }

        // 名前空間を実際に決めたオプション（出所）を計画から 1 回だけ求め、名前空間・層フォルダの検証が共有する
        var usedNamespaceSources = GeneratedFilePlanner.UsedNamespaceSources(options);
        ValidateNamespaces(options, usedNamespaceSources, diagnostics);
        ValidateLayerDirectories(options, usedNamespaceSources, diagnostics);
        ValidateCodeSubdirectory(options, diagnostics);
        ValidateApiDocsSubdirectory(options, diagnostics);
        ValidateOutputFileName(options, diagnostics);
        ValidateApiDocsFileName(options, diagnostics);
        ValidateApiDocsLanguage(options, diagnostics);

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

        ValidateSchemaNameDuplicates(diagram, diagnostics);
        ValidateGeneratedTypeNames(diagram, diagnostics);
        ValidateColumnPropertyNameUniqueness(diagram, diagnostics);
        ValidateNameControlCharacters(diagram, diagnostics);
    }

    /// <summary>図の名前（テーブル・列・制約・クエリ）に改行・制御文字が含まれていないかを検証する</summary>
    /// <remarks>
    /// <para>
    /// 図の名前は GUI の自由入力・DB 取込・DBML/Excel 取込・MCP/AI 経由の任意文字列で、改行を入れられる。
    /// 改行入りの名前は (1) 生成される C# の 1 行リテラルを行またぎで壊し (2) DDL の <c>--</c> コメント行を
    /// 突き破って 2 行目以降を実行される SQL に変え (3) そもそもどの DB も識別子として受け付けない。
    /// 出力側のエスケープ・サニタイズは防壁として残すが、名前に関しては入口で止めるほうが利用者に親切なので
    /// 生成前診断で Error にする。
    /// </para>
    /// <para>
    /// Error にするのは改行・制御文字だけで、<c>"</c> や <c>'</c> や <c>]</c> は対象にしない。これらは DB 取込で
    /// 実在し得る名前で、出力側のエスケープ（C# リテラル・方言クォート）で正しく扱えるため止める理由が無い。
    /// </para>
    /// <para>
    /// 場所の表記は SQL の綴りを使って言語中立にする（<c>SyncPlanWarning.Detail</c> と同じ流儀）。
    /// 名前そのものを文面へ載せるため、表示前に制御文字を空白へ畳む。
    /// </para>
    /// </remarks>
    private static void ValidateNameControlCharacters(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var offenders = new List<string>();

        foreach (var entity in diagram.Entities)
        {
            AddIfUnsafe(entity.TableName, $"TABLE '{Sanitize(entity.TableName)}'");

            foreach (var column in entity.Columns)
            {
                AddIfUnsafe(
                    column.Name,
                    $"COLUMN '{Sanitize(entity.TableName)}'.'{Sanitize(column.Name)}'"
                );
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                AddIfUnsafe(
                    constraint.Name,
                    $"UNIQUE '{Sanitize(entity.TableName)}'.'{Sanitize(constraint.Name)}'"
                );
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            AddIfUnsafe(
                relationship.ConstraintName,
                $"FOREIGN KEY '{Sanitize(relationship.ConstraintName)}'"
            );
        }

        foreach (var query in diagram.Queries)
        {
            AddIfUnsafe(query.Name, $"QUERY '{Sanitize(query.Name)}'");
        }

        if (offenders.Count > 0)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_NameContainsControlCharacter,
                        string.Join(", ", offenders)
                    )
                )
            );
        }

        void AddIfUnsafe(string? name, string location)
        {
            if (ContainsControlCharacter(name))
            {
                offenders.Add(location);
            }
        }

        static string Sanitize(string? name) =>
            name is null
                ? string.Empty
                : new string(name.Select(ch => IsControlCharacter(ch) ? ' ' : ch).ToArray());
    }

    /// <summary>文字列が改行・制御文字（U+0000〜U+001F・U+007F・U+0085・U+2028・U+2029）を含むか</summary>
    /// <remarks>
    /// DDL / 同期スクリプト側の同規則は <c>QuickER.Provider.SqlComment</c> が持つ。CodeGen.CSharp は DB 非依存で
    /// Provider を参照できない（依存方向は Provider → CodeGen.CSharp）ため、この 2 メソッドだけは意図的に
    /// 二重に置く。片方だけ変えないこと。
    /// </remarks>
    private static bool ContainsControlCharacter(string? text) =>
        text is not null && text.Any(IsControlCharacter);

    /// <summary>
    /// C0 制御文字（U+0000〜U+001F）・DEL（U+007F）・Unicode の行区切り（U+0085 / U+2028 / U+2029）か。
    /// </summary>
    /// <remarks>
    /// 行区切り 3 種を含めるのは、これらが C# 言語仕様上の new-line だから（集合の正本は <see cref="GeneratedLiteralText.IsLineBreak"/>）。
    /// <c>///</c> コメントや 1 行リテラルの行またぎを許すと、名前 1 つで生成コードへ任意の C# を注入できる。
    /// </remarks>
    private static bool IsControlCharacter(char ch) =>
        ch < ' ' || ch == (char)0x7F || GeneratedLiteralText.IsLineBreak(ch);

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
    /// 主キー列が計算列・生成列でないことを検証する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 計算列は DB が式から値を作るため <c>[ComputedColumn]</c> が付き、
    /// <c>EntitySaveMetadata</c> が INSERT / UPDATE の対象から外す。その列が主キーだと生成 INSERT は
    /// キー列を送らず、しかも「行の同一性」がもとになった列の更新のたびに変わる値に乗る
    /// （行バージョン列を主キーにしたときと同じ壊れ方）。DB 取込では自然に発生し得る構成なので、
    /// 黙って生成せず生成時に止める。
    /// </para>
    /// <para>
    /// 判定は図の <c>Column.IsComputed</c>＝<c>[ComputedColumn]</c> の付与条件と同一。
    /// 方言にも生成オプションにも依らない（列自身の事実のため）。
    /// </para>
    /// </remarks>
    private static void ValidateComputedPrimaryKeys(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns.Where(c => c.IsPrimaryKey && c.IsComputed))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_PrimaryKeyComputedColumn,
                            entity.TableName,
                            column.Name
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// 主キー列が DB の採番列（<see cref="Column.IsIdentity"/>）なら警告する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 生成 Repository は単一キーをアプリ側で決めて書き込む前提のため、主キーの採番列には <c>[IdentityColumn]</c> を
    /// 付けず、普通の列として INSERT へ載せる。指定した値を拒む DB では行の追加が失敗するので、生成時に名指しする。
    /// </para>
    /// <para>
    /// 計算列の主キー（<see cref="ValidateComputedPrimaryKeys"/>）と違い Error にはしない。DB 側で明示の値を許す
    /// 設定（SQL Server の IDENTITY_INSERT 等）で動かす構成があり、図からはそれを判定できないため。
    /// </para>
    /// <para>
    /// 出すのは Repository 契約（QuickER 版・EF Core・インメモリのどれか）を生成するときだけ。
    /// キーを書き込む前提が効くのはそのときで、エンティティだけを生成する構成では行を追加する生成コードが無い。
    /// </para>
    /// </remarks>
    private static void WarnIdentityPrimaryKeys(
        ErDiagram diagram,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GeneratesRepositoryContract)
        {
            return;
        }

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns.Where(c => c.IsPrimaryKey && c.IsIdentity))
            {
                diagnostics.Add(
                    GenerationDiagnostic.Warning(
                        string.Format(
                            Strings.CodeGen_Warning_IdentityPrimaryKeyColumn,
                            entity.TableName,
                            column.Name
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// テーブル名・同じテーブルの列名が <see cref="SchemaNames"/> の規則（前後の空白を除き大文字小文字を区別しない）で
    /// 重複していないことを検証する
    /// </summary>
    /// <remarks>
    /// ツール・取込・プロパティパネルは重複を入口で断るが、手で編集した図ファイルや古い版の図は重複を持ち得る。
    /// 生成する C# の識別子が衝突するかどうか（<see cref="ValidateGeneratedTypeNames"/> など）とは別に、
    /// 図が持ってはいけない状態として名指しで止める（<c>Äpfel</c> と <c>äpfel</c> のように C# では別の名前になる組も含む）。
    /// </remarks>
    private static void ValidateSchemaNameDuplicates(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (
            var group in diagram
                .Entities.Where(entity => !string.IsNullOrWhiteSpace(entity.TableName))
                .GroupBy(entity => entity.TableName, SchemaNames.Comparer)
                .Where(group => group.Count() > 1)
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_DuplicateTableName,
                        SchemaNames.ToStoredForm(group.First().TableName),
                        string.Join(", ", group.Select(entity => $"'{entity.TableName}'"))
                    )
                )
            );
        }

        foreach (var entity in diagram.Entities)
        {
            foreach (
                var group in entity
                    .Columns.Where(column => !string.IsNullOrWhiteSpace(column.Name))
                    .GroupBy(column => column.Name, SchemaNames.Comparer)
                    .Where(group => group.Count() > 1)
            )
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_DuplicateColumnName,
                            entity.TableName,
                            SchemaNames.ToStoredForm(group.First().Name),
                            string.Join(", ", group.Select(column => $"'{column.Name}'"))
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// テーブル・列から決まる生成型名がほかの出どころの型名・生成コードの固定の型名とぶつからず、リモートのルート名が
    /// 大文字小文字だけ違わないことを検証する（エラー）。大文字小文字だけ違う型名は警告する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 型名は単数形化と接尾辞で作り、名前がすでに接尾辞で終わっていれば付け足さないため、綴りの違うテーブルでも同じ型名になる
    /// （<c>customer</c> と <c>customers</c> はどちらも <c>CustomerEntity</c>・<c>order_edit_model</c> は <c>order</c> の EditModel と同じ
    /// <c>OrderEditModel</c>・<c>i_order</c> は <c>order</c> の <c>IOrderRepository</c>）。ランタイム・固定の生成物・単純名で使う外部の型と
    /// 同じ名前になる表・列もある（表 <c>raw_sql</c> の Mapper が <c>RawSqlMapper</c>）。どれも診断なしではコンパイルできない出力になる。
    /// </para>
    /// <para>
    /// 判定の本体と範囲（構成と名前空間に依らない・型名は Ordinal・ルート名だけ大小無視）は
    /// <see cref="GeneratedTypeNames.Analyze"/> が持ち、ここは診断の文面を組み立てるだけ。
    /// </para>
    /// </remarks>
    private static void ValidateGeneratedTypeNames(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var report = GeneratedTypeNames.Analyze(diagram.Entities);

        foreach (var collision in report.Collisions)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_GeneratedTypeNameCollision,
                        DescribeTypeNameSources(collision.Sources),
                        QuoteNames(collision.Names)
                    )
                )
            );
        }

        foreach (var use in report.Reserved)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_GeneratedTypeNameReserved,
                        DescribeTypeNameSources(use.Sources),
                        use.Name
                    )
                )
            );
        }

        foreach (var collision in report.RouteCaseCollisions)
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_RemoteRouteNameCaseCollision,
                        QuoteNames(collision.Tables),
                        QuoteNames(collision.RouteNames)
                    )
                )
            );
        }

        foreach (var variants in report.CaseVariants)
        {
            diagnostics.Add(
                GenerationDiagnostic.Warning(
                    string.Format(
                        Strings.CodeGen_Warning_GeneratedTypeNamesDifferOnlyInCase,
                        string.Join(
                            ", ",
                            variants.Select(use =>
                                $"'{use.Name}' ({DescribeTypeNameSources(use.Sources)})"
                            )
                        )
                    )
                )
            );
        }
    }

    /// <summary>型名の出どころ（テーブル・列）を診断の文面へ並べる</summary>
    private static string DescribeTypeNameSources(IEnumerable<GeneratedTypeNameSource> sources) =>
        string.Join(
            ", ",
            sources.Select(source =>
                source.ColumnName is null
                    ? string.Format(Strings.CodeGen_Origin_Table, source.TableName)
                    : string.Format(
                        Strings.CodeGen_Origin_TableColumn,
                        source.TableName,
                        source.ColumnName
                    )
            )
        );

    /// <summary>名前を引用符で囲んで並べる</summary>
    private static string QuoteNames(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));

    /// <summary>
    /// 列由来のプロパティ名がエンティティごとに一意で、大文字小文字だけ違う組も無いことを検証する
    /// </summary>
    /// <remarks>
    /// 列名はパスカルケースへ正規化してからプロパティ名にするため、<c>user-id</c> / <c>user_id</c> / <c>USER_ID</c> の
    /// ように綴りが違う列でも同じプロパティ名（<c>UserId</c>）になりうる。同一エンティティ内で衝突すると
    /// Entity / EditModel に同名メンバーが重複宣言され（CS0102）、EditModel の partial メソッドも二重宣言になる
    /// （CS0111）ため、コンパイル不能な出力が診断なしに書き出される。<c>user_id</c> と <c>userid</c> のように
    /// 大文字小文字だけ違うプロパティ名（<c>UserId</c> と <c>Userid</c>）はコンパイルできるが、エンティティを読み書きする
    /// JSON の設定が大文字小文字を区別しないので実行時に失敗する（構成に依らず止める）。判定の本体は
    /// <see cref="ColumnPropertyNameCollisions.Find"/>。
    /// 衝突判定はエンティティ単位＝別テーブルの同名列は別クラスのメンバーになるため衝突ではない。
    /// なお、生成メンバー名の衝突全般（EditModel の派生名 <c>Binding…</c> / <c>_…</c> / <c>…Snapshot</c>、
    /// 列とナビゲーション・基底の公開プロパティの衝突）は <c>CSharpGenerationModelBuilder</c> のシンボル表検証が担う。
    /// 本メソッドは最頻の誤り（同名列）へ「どの列同士か」を挙げた具体的なメッセージで先に答える早期検証として残す。
    /// ここでエラーになるとビルダーへ到達しないため、両者の診断が同時に出ることはない。
    /// </remarks>
    private static void ValidateColumnPropertyNameUniqueness(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        foreach (
            var entity in diagram.Entities.Where(entity =>
                !string.IsNullOrWhiteSpace(entity.TableName)
            )
        )
        {
            foreach (var collision in ColumnPropertyNameCollisions.Find(entity))
            {
                var columns = string.Join(", ", collision.Columns.Select(name => $"'{name}'"));

                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        collision.PropertyNames.Count == 1
                            ? string.Format(
                                Strings.CodeGen_Error_ColumnPropertyNameCollision,
                                entity.TableName,
                                collision.PropertyNames[0],
                                columns
                            )
                            : string.Format(
                                Strings.CodeGen_Error_ColumnPropertyNamesDifferOnlyInCase,
                                entity.TableName,
                                QuoteNames(collision.PropertyNames),
                                columns
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
    /// フォールバックするため検証対象外。検証するのは、計画がその名前空間を決めるのに実際に使ったオプション
    /// （<see cref="GeneratedFilePlanner.UsedNamespaceSources"/>）だけ＝「名前空間導出に実際に使われる値だけ検証する」。
    /// 例: 単一方言の非分割では Repository 名前空間を使わない・パッケージ参照モードでは Runtime 名前空間を使わない・
    /// 層別出力ではルート名前空間を使わない（いずれも検証しない）。
    /// </remarks>
    private static void ValidateNamespaces(
        CodeGenerationOptions options,
        IReadOnlySet<NamespaceSource> usedNamespaceSources,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 並びは NamespaceSource の宣言順（ルート→カテゴリ別）。層フォルダは ValidateLayerDirectories が担う
        foreach (var source in Enum.GetValues<NamespaceSource>())
        {
            if (!usedNamespaceSources.Contains(source) || IsLayerDirectorySource(source))
            {
                continue;
            }

            var (optionName, value) = GeneratedFilePlanner.DescribeNamespaceSource(options, source);
            AddInvalidNamespaceDiagnostic(optionName, value, diagnostics);
        }
    }

    /// <summary>出所が層フォルダのオプションか（名前空間オプションでないか）</summary>
    private static bool IsLayerDirectorySource(NamespaceSource source) =>
        Enum.GetValues<GeneratedLayer>()
            .Any(layer => GeneratedFilePlanner.LayerDirectorySource(layer) == source);

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
        IReadOnlySet<NamespaceSource> usedNamespaceSources,
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
        foreach (var layer in Enum.GetValues<GeneratedLayer>())
        {
            if (!usedLayers.Contains(layer))
            {
                continue;
            }

            var source = GeneratedFilePlanner.LayerDirectorySource(layer);
            var (name, value) = GeneratedFilePlanner.DescribeNamespaceSource(options, source);

            if (
                !string.IsNullOrWhiteSpace(value)
                && LayerDirectoryValidator.Check(value) is { } violation
            )
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_InvalidOutputSubdirectory,
                            name,
                            value.Trim(),
                            PathSegments.Describe(violation, CultureInfo.CurrentUICulture)
                        )
                    )
                );

                continue;
            }

            // 層別出力は名前空間の既定を層フォルダから導出する（フォルダ追従）ため、その層で導出が実際に
            // 使われる（＝計画がその層のフォルダから名前空間を決めた）場合は、フォルダが C# の
            // 名前空間として成立することも要求する（ハイフン等はパスとしては合法でも名前空間になれない）。
            // 明示の名前空間オプションで全バケットが賄われている層は、パス検証だけで足りる
            if (
                usedNamespaceSources.Contains(source)
                && !CSharpNamespaceValidator.IsValid(
                    GeneratedFilePlanner.LayerNamespaceRoot(options, layer)
                )
            )
            {
                diagnostics.Add(
                    GenerationDiagnostic.Error(
                        string.Format(
                            Strings.CodeGen_Error_LayerDirectoryNotNamespace,
                            name,
                            GeneratedFilePlanner.ResolveLayerDirectory(options, layer),
                            GeneratedFilePlanner.LayerNamespaceRoot(options, layer)
                        )
                    )
                );
            }
        }
    }

    /// <summary>
    /// 生成コードの出力先サブフォルダ（<see cref="CodeGenerationOptions.CodeSubdirectory"/>）を検証する
    /// </summary>
    /// <remarks>
    /// 判定は層フォルダ・書き出し防御と同じ <see cref="LayerDirectoryValidator"/>（単一正本）で、
    /// 出力モードに依らず指定があるときだけ効く（空白は既定＝サブフォルダなしのため対象外）。
    /// 名前空間の導出に入らない値なので、層フォルダと違い C# 識別子としての検証はない
    /// （<c>generated-code</c> のような名前も許す＝<see cref="CodeGenerationOptions.CodeSubdirectory"/> の設計）。
    /// </remarks>
    private static void ValidateCodeSubdirectory(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    ) =>
        ValidateOutputSubdirectory(
            options.CodeSubdirectory,
            nameof(CodeGenerationOptions.CodeSubdirectory),
            diagnostics
        );

    /// <summary>
    /// API リファレンスの出力先サブフォルダ（<see cref="CodeGenerationOptions.ApiDocsSubdirectory"/>）を検証する
    /// </summary>
    /// <remarks>
    /// 判定は層フォルダ・書き出し防御と同じ <see cref="LayerDirectoryValidator"/>（単一正本）。
    /// 層別出力に依らず、API リファレンスを出力する構成でのみ検証する（空白は既定＝直下のため対象外）。
    /// ドキュメントは C# コードでないため名前空間導出の検証はない（パスの妥当性だけ）。
    /// </remarks>
    private static void ValidateApiDocsSubdirectory(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GenerateApiDocs)
        {
            return;
        }

        ValidateOutputSubdirectory(
            options.ApiDocsSubdirectory,
            nameof(CodeGenerationOptions.ApiDocsSubdirectory),
            diagnostics
        );
    }

    /// <summary>出力先サブフォルダ（出力ディレクトリからの相対パス）の妥当性を検証する共通処理</summary>
    /// <remarks>空白は「未指定＝既定」を意味するため対象外。判定の正本は <see cref="LayerDirectoryValidator"/>。</remarks>
    private static void ValidateOutputSubdirectory(
        string? value,
        string optionName,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || LayerDirectoryValidator.Check(value) is not { } violation
        )
        {
            return;
        }

        diagnostics.Add(
            GenerationDiagnostic.Error(
                string.Format(
                    Strings.CodeGen_Error_InvalidOutputSubdirectory,
                    optionName,
                    value.Trim(),
                    PathSegments.Describe(violation, CultureInfo.CurrentUICulture)
                )
            )
        );
    }

    /// <summary>
    /// 非分割の出力ファイル名（<see cref="CodeGenerationOptions.OutputFileName"/>）を検証する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 許すのは「ディレクトリ要素を含まない単一のファイル名」だけ（置き場を決めるのは出力先と
    /// <see cref="CodeGenerationOptions.CodeSubdirectory"/> の役割）。診断がないと、<c>sub/Foo.g.cs</c> は
    /// <see cref="GeneratedFileWriter"/> の <c>Path.GetFileName</c> がディレクトリ要素を落として黙って <c>Foo.g.cs</c> になり、
    /// 予約名・8.3 の別名は書き込みの時点まで通ってから例外になる。
    /// </para>
    /// <para>
    /// 判定するのは利用者の値（前後の空白を除く）と、そこから作るファイル名すべて（<c>.g.cs</c> へ正規化した本体・
    /// サーバー実装の <c>.RemoteServer.g.cs</c>・ファイル名を指定していない API リファレンス）。付け足す分で長さの上限を超える名前も断る。
    /// 分割出力ではこの値を使わないので検証しない（使われない値で生成を止めない）。空白は既定のため対象外。
    /// </para>
    /// </remarks>
    private static void ValidateOutputFileName(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (
            !options.EffectiveSplitFilesByCategory
            && !string.IsNullOrWhiteSpace(options.OutputFileName)
            && CheckOutputFileName(options) is { } violation
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_InvalidOutputFileName,
                        options.OutputFileName.Trim(),
                        PathSegments.Describe(violation, CultureInfo.CurrentUICulture)
                    )
                )
            );
        }
    }

    /// <summary>非分割の出力ファイル名と、そこから作るファイル名を断る理由を返す（断らないなら <c>null</c>）</summary>
    private static PathSegmentViolation? CheckOutputFileName(CodeGenerationOptions options)
    {
        var trimmed = options.OutputFileName.Trim();

        if (PathSegments.CheckSegment(trimmed) is { } violation)
        {
            return violation;
        }

        var normalized = GeneratedFilePlanner.NormalizeOutputFileName(trimmed);

        // 拡張子だけの指定（".g.cs" 等）はベース名が空＝隠しファイルになるため拒否する
        if (GeneratedFilePlanner.StripGeneratedCSharpSuffix(normalized).Length == 0)
        {
            return PathSegmentViolation.Empty;
        }

        var derived = new List<string> { normalized };

        if (options.GenerateRemoteServices)
        {
            derived.Add(GeneratedFilePlanner.RemoteServerFileName(trimmed));
        }

        // 未定義の言語は言語の診断（ValidateApiDocsLanguage）が名指しするので、ここでは名前を求めない
        if (
            options.GenerateApiDocs
            && string.IsNullOrWhiteSpace(options.ApiDocsFileName)
            && Enum.IsDefined(options.ApiDocsLanguage)
        )
        {
            derived.AddRange(ResolveApiDocsFileNames(options));
        }

        return derived
            .Select(name => PathSegments.CheckSegment(name))
            .FirstOrDefault(result => result is not null);
    }

    /// <summary>
    /// API リファレンスの出力ファイル名（<see cref="CodeGenerationOptions.ApiDocsFileName"/>）を検証する
    /// </summary>
    /// <remarks>
    /// 許すのは「ディレクトリ要素を含まない単一のファイル名」だけ（置き場を決めるのは
    /// <see cref="CodeGenerationOptions.ApiDocsSubdirectory"/> の役割）。<see cref="GeneratedFileWriter"/> は
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
            && CheckApiDocsFileName(options.ApiDocsFileName) is { } violation
        )
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_InvalidApiDocsFileName,
                        options.ApiDocsFileName.Trim(),
                        PathSegments.Describe(violation, CultureInfo.CurrentUICulture)
                    )
                )
            );
        }
    }

    /// <summary>
    /// API リファレンスの出力言語（<see cref="CodeGenerationOptions.ApiDocsLanguage"/>）が定義済みの値かを検証する
    /// </summary>
    /// <remarks>
    /// 設定 JSON は数値でも列挙値を受け付けるため、未定義の値（例: <c>99</c>）が届き得る。検証しないと
    /// <see cref="RenderLanguagesOf"/> の例外が生成全体を落とすので、どのオプションの値が悪いかを名指しして止める。
    /// </remarks>
    private static void ValidateApiDocsLanguage(
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (options.GenerateApiDocs && !Enum.IsDefined(options.ApiDocsLanguage))
        {
            diagnostics.Add(
                GenerationDiagnostic.Error(
                    string.Format(
                        Strings.CodeGen_Error_InvalidApiDocsLanguage,
                        (int)options.ApiDocsLanguage,
                        string.Join(", ", Enum.GetNames<ApiDocsLanguage>())
                    )
                )
            );
        }
    }

    /// <summary>
    /// 出力する言語の組み合わせを、描画する言語の並び（英語→日本語の順）へ分解する
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値（生成前の診断が止めるため通常は到達しない）</exception>
    private static IReadOnlyList<ApiDocRenderLanguage> RenderLanguagesOf(
        ApiDocsLanguage language
    ) =>
        language switch
        {
            ApiDocsLanguage.English => [ApiDocRenderLanguage.English],
            ApiDocsLanguage.Japanese => [ApiDocRenderLanguage.Japanese],
            ApiDocsLanguage.Both => [ApiDocRenderLanguage.English, ApiDocRenderLanguage.Japanese],
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

    /// <summary>
    /// API リファレンスの出力ファイル名として断る理由を返す（単一のファイル名で、ベース名が空でないこと。断らないなら <c>null</c>）
    /// </summary>
    /// <remarks>1 区間の名前の判定は <see cref="PathSegments.CheckSegment"/>（パス区切り・ドライブ指定も断る）。</remarks>
    private static PathSegmentViolation? CheckApiDocsFileName(string value)
    {
        // 値全体の前後の空白は取り除いてから判定する（ファイル名を決める側も同じく取り除いて使う）
        var trimmed = value.Trim();

        if (PathSegments.CheckSegment(trimmed) is { } violation)
        {
            return violation;
        }

        // 拡張子だけの指定（".g.md" 等）はベース名が空＝隠しファイルになるため拒否する
        return StripApiDocsSuffix(trimmed).Length > 0 ? null : PathSegmentViolation.Empty;
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

    /// <summary>分割出力時の API リファレンス Markdown の固定ベース名（カテゴリ別固定名の流儀に合わせる）</summary>
    private const string SplitApiDocsBaseName = "ApiDocs";

    /// <summary>
    /// 現在のオプションで実際に出力される API リファレンス Markdown のファイル名を、出力順（英語→日本語）で返す。
    /// </summary>
    /// <remarks>
    /// GUI が「未指定のときに使われる既定名」をプレースホルダとして見せるための公開口。
    /// 生成本体と同じ一覧（<see cref="ApiDocsOutputs"/>）を読むため、表示と実出力がずれない。
    /// <see cref="CodeGenerationOptions.GenerateApiDocs"/> の ON/OFF は見ない（欄が表示されるのは ON のときだけのため）。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CodeGenerationOptions.ApiDocsLanguage"/> が未定義の値</exception>
    public static IReadOnlyList<string> ResolveApiDocsFileNames(CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ApiDocsOutputs(options).Select(output => output.FileName).ToList();
    }

    /// <summary>
    /// 出力する API リファレンス Markdown の「描画言語とファイル名」の組を、出力順（英語→日本語）で返す
    /// （生成本体と <see cref="ResolveApiDocsFileNames"/> が共有する唯一の正）
    /// </summary>
    private static IEnumerable<(ApiDocRenderLanguage Language, string FileName)> ApiDocsOutputs(
        CodeGenerationOptions options
    ) =>
        RenderLanguagesOf(options.ApiDocsLanguage)
            .Select(language =>
                (
                    language,
                    language == ApiDocRenderLanguage.Japanese
                        ? JapaneseApiDocsFileName(options)
                        : ApiDocsFileName(options)
                )
            );

    /// <summary>API リファレンス Markdown の拡張子サフィックス（英語版）</summary>
    private const string ApiDocsSuffix = ".g.md";

    /// <summary>API リファレンス Markdown の拡張子サフィックス（日本語版）</summary>
    private const string JapaneseApiDocsSuffix = ".ja.g.md";

    /// <summary>
    /// API リファレンス Markdown の出力ファイル名を導出する。
    /// </summary>
    /// <remarks>
    /// <see cref="CodeGenerationOptions.ApiDocsFileName"/> の指定があればそれ（拡張子は ".g.md" へ正規化）を
    /// 出力モードに依らず優先する。空白なら既定の導出で、非分割時は <see cref="GeneratedFilePlanner.NormalizeOutputFileName"/> で
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
            GeneratedFilePlanner.NormalizeOutputFileName(options.OutputFileName)
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
    /// 型表記が中立トークン経由では復元できない列を「テーブル.列: 図の表記 -&gt; トークン経由で戻る表記」の
    /// 1 行ずつへ整形する（Info 診断専用）。
    /// </summary>
    /// <remarks>
    /// 判定材料は生成モデルではなく列型辞書（<see cref="CSharpTypeInfo.VerbatimDbType"/>）で、
    /// <c>[DbColumnMeta].NativeType</c> を刻むかどうかの判定と同一のデータ。列の並びは図の宣言順にする。
    /// </remarks>
    private static IReadOnlyList<string> BuildNonCanonicalSpellingLines(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes
    ) =>
        diagram
            .Entities.SelectMany(entity =>
                entity
                    .Columns.Select(column => (entity, column))
                    .Where(pair =>
                        columnTypes.TryGetValue(pair.column.Id, out var typeInfo)
                        && typeInfo.VerbatimDbType is not null
                    )
            )
            .Select(pair =>
                $"{pair.entity.TableName}.{pair.column.Name}: {columnTypes[pair.column.Id].VerbatimDbType} -> {columnTypes[pair.column.Id].CanonicalRoundTripDbType}"
            )
            .ToList();

    /// <summary>
    /// 列型辞書の印で選んだ列を「テーブル.列 (元の DB 型表記)」の 1 行ずつへ整形する（Info 診断専用）。
    /// </summary>
    /// <remarks>
    /// 配列列（<see cref="CSharpTypeInfo.IsArray"/>）とテキスト入力で扱えない型の列
    /// （<see cref="CSharpTypeInfo.IsTextInputUnsupported"/>）を別々の Info で名指しするために使う。
    /// 列の並びは図の宣言順にする。
    /// </remarks>
    private static IReadOnlyList<string> BuildColumnLines(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        Func<CSharpTypeInfo, bool> predicate
    ) =>
        diagram
            .Entities.SelectMany(entity =>
                entity
                    .Columns.Where(column =>
                        columnTypes.TryGetValue(column.Id, out var typeInfo) && predicate(typeInfo)
                    )
                    .Select(column => $"{entity.TableName}.{column.Name} ({column.DataType})")
            )
            .ToList();

    /// <summary>
    /// 型カタログが解析できず string へフォールバックした列を「テーブル.列 (元の DB 型表記)」の
    /// 1 行ずつへ整形する（Info 診断専用）。
    /// </summary>
    /// <remarks>
    /// 判定材料は生成モデルではなく列型辞書（<see cref="CSharpTypeInfo.IsFallbackType"/>）で、
    /// [DbColumnMeta] のトークン付加とは独立（未知の型はそもそもトークンを持たない）。列の並びは図の宣言順にする。
    /// </remarks>
    private static IReadOnlyList<string> BuildFallbackTypeLines(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes
    ) =>
        diagram
            .Entities.SelectMany(entity =>
                entity
                    .Columns.Where(column =>
                        columnTypes.TryGetValue(column.Id, out var typeInfo)
                        && typeInfo.IsFallbackType
                    )
                    .Select(column => $"{entity.TableName}.{column.Name} ({column.DataType})")
            )
            .ToList();

    /// <summary>
    /// EditModel に載らない列（配列列・テキスト入力で扱えない型の列）を含む UNIQUE 制約を
    /// 「テーブル.制約名 (構成列)」の 1 行ずつへ整形する（Info 診断専用）。
    /// </summary>
    /// <remarks>
    /// 名前を持たない制約は、生成側と同じ規則（<see cref="UniqueConstraint.SynthesizeName"/>）で合成した名前を出す。
    /// 既存の配列列 Info（<c>CodeGen_Info_ArrayColumnsNotEditable</c>）へ相乗りさせないのは、配列列があっても
    /// 制約に絡まないときに「制約を外した」と言ってしまうため。
    /// </remarks>
    private static IReadOnlyList<string> BuildArrayUniqueConstraintLines(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes
    ) =>
        diagram
            .Entities.SelectMany(entity =>
            {
                var columnsById = entity.Columns.ToDictionary(column => column.Id);

                return entity
                    .UniqueConstraints.Where(constraint =>
                        constraint.ColumnIds.Count > 0
                        && constraint.ColumnIds.All(columnsById.ContainsKey)
                        && constraint.ColumnIds.Any(id =>
                            columnTypes.TryGetValue(id, out var typeInfo)
                            && typeInfo.IsExcludedFromEditModel
                        )
                    )
                    .Select(constraint =>
                    {
                        var members = constraint
                            .ColumnIds.Select(id => columnsById[id].Name)
                            .ToList();
                        var name = string.IsNullOrWhiteSpace(constraint.Name)
                            ? UniqueConstraint.SynthesizeName(
                                entity.TableName,
                                members,
                                IdentifierLengthLimits.ForDiagramTarget(diagram.TargetDbms)
                            )
                            : constraint.Name;

                        return $"{entity.TableName}.{name} ({string.Join(", ", members)})";
                    });
            })
            .ToList();

    /// <summary>
    /// 負のスケールを持つ decimal 列を「テーブル.列 (元の DB 型表記)」の 1 行ずつへ整形する（Info 診断専用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定は<b>図の型表記から</b>行い、型マッパーの戻り値は見ない。5 方言の型マッパーは負のスケールを
    /// <b>意図的に読まない</b>ため（精度・スケールとも null になる）、マッパー由来の値からは区別が付かない。
    /// </para>
    /// <para>
    /// <b>Warning ではなく Info。</b>負のスケールは誤って書く型ではなく、「DB に百単位で丸めさせる」ことを
    /// 意図して書くものなので、生成のたびに警告で鳴らすと形骸化する（解析できない DB 型を
    /// <see cref="CSharpTypeInfo.IsFallbackType"/> の Info にしたのと同じ理屈）。
    /// </para>
    /// <para>
    /// <b>値オブジェクトの有無で絞らない。</b>丸められるという事実は値オブジェクトと無関係で、
    /// 値オブジェクトはオプトインなので絞ると既定の構成では鳴らなくなる。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildNegativeScaleColumnLines(ErDiagram diagram) =>
        diagram
            .Entities.SelectMany(entity =>
                entity
                    .Columns.Where(column => HasNegativeScale(column.DataType))
                    .Select(column => $"{entity.TableName}.{column.Name} ({column.DataType})")
            )
            .ToList();

    /// <summary>DB 型表記が負のスケール（<c>(精度,-スケール)</c>）を持つか</summary>
    /// <remarks>
    /// 型名は見ない（負のスケールを書ける型は方言ごとに違い、自由記述の型表記もあるため）。
    /// 括弧の第 2 引数が負であることだけを見る。
    /// </remarks>
    private static bool HasNegativeScale(string? dataType) =>
        dataType is not null && NegativeScaleRegex().IsMatch(dataType);

    /// <summary>"(精度,-スケール)" を検出する正規表現（診断専用・型マッパーの読み取りには使わない）</summary>
    [GeneratedRegex(@"\(\s*[0-9]+\s*,\s*-\s*[0-9]+\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex NegativeScaleRegex();

    /// <summary>
    /// 無制限バイナリ列の除外対象一覧を <c>{EntityClass}.{Property} ({テーブル}.{列名})</c> 形式の行で組み立てる。
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
                        $"{entity.ClassName}.{property.PropertyName} ({entity.TableName}.{property.ColumnName})"
                    )
            )
            .ToList();

    /// <summary>
    /// NOT NULL の計算列を、式を評価しないエンジンへも書く構成（双方向同期支援・マルチターゲット）のときだけ
    /// Warning 診断で名指しする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 計算列は方言に依らず INSERT / UPDATE の対象から外れる（式が値を作るので正しい）。ところが式を持つのは
    /// 取込元の DB だけで、ミラー（マルチターゲットのもう一方の方言）やローカル（双方向同期）には普通の列として
    /// 作られる——そこへ行を追加すると、送られない列の NOT NULL 制約で<b>必ず</b>落ちる。生成物を読んでも
    /// 気づけず実行時に初めて分かる乖離なので、生成時に予告する。
    /// </para>
    /// <para>
    /// Error にはしない。ミラー側のスキーマを既定値や同じ式で整えてあれば正しく動く構成が実在し、
    /// 図からはそれを判定できないため（生成を止めると正当な使い方まで塞ぐ）。
    /// </para>
    /// </remarks>
    private static void AddNotNullComputedColumnWarning(
        CSharpGenerationModel model,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        // 「式を評価しないエンジンへも同じ行を書く」構成だけが対象。単一方言・同期なしの構成では、
        // 計算列を持つ DB は式を持つ当の DB だけなので予告するものが無い。
        // 同期支援は現状マルチターゲット（sqlserver+sqlite ちょうど 2 つ）でしか有効にならないため
        // 第 1 項は第 2 項に含まれるが、2 つの理由を明示的に並べる（同期の前提が緩んだとき、
        // 警告が黙って落ちるのを避ける）
        var writesToMirror =
            options.GenerateSyncSupport
            || (options.GenerateRepositories && options.ResolveRepositoryDialects().Count >= 2);

        if (!writesToMirror)
        {
            return;
        }

        var lines = model
            .EntityClasses.SelectMany(entity =>
                entity
                    .Properties.Where(property => property.IsComputed && !property.IsNullable)
                    .Select(property =>
                        $"{entity.ClassName}.{property.PropertyName} ({entity.TableName}.{property.ColumnName})"
                    )
            )
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        // ダイアログ／CLI で 1 行 1 列に見えるよう、導入文の後に改行＋インデント 2 スペースで各列を並べる
        diagnostics.Add(
            GenerationDiagnostic.Warning(
                string.Format(
                    Strings.CodeGen_Warning_NotNullComputedColumns,
                    Environment.NewLine
                        + string.Join(Environment.NewLine, lines.Select(l => "  " + l))
                )
            )
        );
    }

    /// <summary>
    /// 主キーでない NOT NULL の採番列（<see cref="Column.IsIdentity"/>）を、QuickER 版 Repository の実効方言の
    /// どれかがその種類の列を持てない（<see cref="UnsupportedDefinitionKind.IdentityRejectingValues"/> の宣言が
    /// 「あり得ない」）とき Warning 診断で名指しする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 採番列は方言に依らず INSERT / UPDATE の対象から外れる。ところが持てない方言の DB（SQLite。単一方言の生成でも、
    /// マルチターゲットのミラーでも）には普通の列として作られ、そこへ行を追加すると送られない列の NOT NULL 制約で
    /// 必ず落ちる（<see cref="AddNotNullComputedColumnWarning"/> と同じ乖離）。方言の数では絞らない
    /// （単一方言の SQLite も同じく落ちる）。持てるかは方言の宣言で決め、方言名で分岐しない。
    /// </para>
    /// <para>
    /// Error にはしない。その DB のスキーマを既定値などで整えてあれば動く構成があり、図からは判定できないため。
    /// </para>
    /// </remarks>
    private static void AddNotNullIdentityColumnWarning(
        CSharpGenerationModel model,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        if (!options.GenerateRepositories)
        {
            return;
        }

        var dialects = options.ResolveRepositoryDialects();

        if (
            dialects.All(dialect =>
                DialectDeclarations.CanOccur(
                    dialect,
                    UnsupportedDefinitionKind.IdentityRejectingValues
                )
            )
        )
        {
            return;
        }

        var lines = model
            .EntityClasses.SelectMany(entity =>
                entity
                    .Properties.Where(property => property.IsIdentity && !property.IsNullable)
                    .Select(property =>
                        $"{entity.ClassName}.{property.PropertyName} ({entity.TableName}.{property.ColumnName})"
                    )
            )
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        diagnostics.Add(
            GenerationDiagnostic.Warning(
                string.Format(
                    Strings.CodeGen_Warning_NotNullIdentityColumns,
                    Environment.NewLine
                        + string.Join(Environment.NewLine, lines.Select(l => "  " + l))
                )
            )
        );
    }

    /// <summary>
    /// 計算列・生成列の一覧を <c>{EntityClass}.{Property} ({テーブル}.{列名})</c> 形式の行で組み立てる。
    /// </summary>
    /// <remarks>
    /// 生成 Entity のプロパティのうち <see cref="CSharpPropertyModel.IsComputed"/> のものを対象にする
    /// （マーカー属性 <c>[ComputedColumn]</c> の付与対象と一致）。Info 診断のメッセージ組み立てに使う。
    /// </remarks>
    private static IReadOnlyList<string> BuildComputedColumnLines(CSharpGenerationModel model) =>
        model
            .EntityClasses.SelectMany(entity =>
                entity
                    .Properties.Where(property => property.IsComputed)
                    .Select(property =>
                        $"{entity.ClassName}.{property.PropertyName} ({entity.TableName}.{property.ColumnName})"
                    )
            )
            .ToList();

    /// <summary>
    /// 主キーでない採番列の一覧を <c>{EntityClass}.{Property} ({テーブル}.{列名})</c> 形式の行で組み立てる。
    /// </summary>
    /// <remarks>
    /// 生成 Entity のプロパティのうち <see cref="CSharpPropertyModel.IsIdentity"/> のものを対象にする
    /// （マーカー属性 <c>[IdentityColumn]</c> の付与対象と一致）。Info 診断のメッセージ組み立てに使う。
    /// </remarks>
    private static IReadOnlyList<string> BuildIdentityColumnLines(CSharpGenerationModel model) =>
        model
            .EntityClasses.SelectMany(entity =>
                entity
                    .Properties.Where(property => property.IsIdentity)
                    .Select(property =>
                        $"{entity.ClassName}.{property.PropertyName} ({entity.TableName}.{property.ColumnName})"
                    )
            )
            .ToList();
}
