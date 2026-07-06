using System.Text;
using System.Text.RegularExpressions;
using Scriban;

namespace QuickER.Generator;

/// <summary>1 ファイル分の描画スコープ。名前空間・using と、出力するバケットの選択を表す</summary>
internal sealed class RenderScope
{
    /// <summary>このファイルの名前空間</summary>
    public required string NamespaceName { get; init; }

    /// <summary>このファイル冒頭に出力する using 名前空間一覧</summary>
    public required IReadOnlyList<string> Usings { get; init; }

    /// <summary>共有基盤（属性・基底・VO 基底・RowState）を出力するか</summary>
    public required bool Runtime { get; init; }

    /// <summary>値オブジェクトの具象クラスを出力するか</summary>
    public required bool ValueObjects { get; init; }

    /// <summary>Entity クラスを出力するか</summary>
    public required bool Entities { get; init; }

    /// <summary>EditModel クラスを出力するか</summary>
    public required bool EditModels { get; init; }

    /// <summary>Mapper クラスを出力するか</summary>
    public required bool Mappers { get; init; }

    /// <summary>EF Core 用コード（DbContext・構成）を出力するか</summary>
    public required bool EfCore { get; init; }

    /// <summary>自作 Repository の方言別実装（ADO 依存）を出力するか。Repository バケット内でこのフラグにより契約と実装を出し分ける</summary>
    public required bool RepositoryImpl { get; init; }

    /// <summary>共通契約（インターフェイス・SqlQuery・メタデータ等）を出力するか</summary>
    /// <remarks>
    /// 単一方言時は Repository バケットで契約＋実装を同一スコープに出す（true）。マルチ方言時は契約スペックのみ true、
    /// 方言実装スペックは false（契約は 1 回だけ出し、実装は各方言スペックが出す）。
    /// </remarks>
    public required bool RenderContract { get; init; }

    /// <summary>このスコープがレンダリングする自作 Repository の方言（"sqlserver" / "sqlite"）</summary>
    public required string Dialect { get; init; }

    /// <summary>マルチ方言レイアウト（実効方言 2 つ以上）かどうか。DI 拡張の方言別名＋keyed 版の出し分けに使う</summary>
    public required bool MultiDialect { get; init; }

    /// <summary>名前空間をブロック形式（<c>namespace X { ... }</c>）で出力するか。非分割マルチ方言で同一ファイルへ複数 namespace を連結するときに true</summary>
    public required bool BlockNamespace { get; init; }

    /// <summary>ファイル冒頭のヘッダ（<c>// &lt;auto-generated /&gt;</c>・<c>#nullable enable</c>・using）を出力するか</summary>
    /// <remarks>1 ファイルに複数スペックを連結する場合、2 つ目以降はヘッダを出さず using は先頭スペックへ集約する</remarks>
    public required bool RenderHeader { get; init; }

    /// <summary>
    /// ランタイム（スキーマ非依存の固定コード）を NuGet パッケージ用ソースとして書き出すモードかどうか（既定 false）。
    /// </summary>
    /// <remarks>
    /// true のとき、スキーマが空でも固定 infra コードを完全に書き出せるよう、テンプレートの
    /// 「スキーマが空だと出力されない」ガード（<c>entity_classes.size &gt; 0</c> 等）を加算的に緩める。
    /// 既定 false では通常生成の出力はバイト 1 つ変わらない（<see cref="RuntimePackageSourceRenderer"/> のみが true を渡す）。
    /// </remarks>
    public bool RuntimePackageExport { get; init; }

    /// <summary>
    /// パッケージ化対象の固定 infra 型・メンバーの可視性（既定 <c>"internal"</c>）。
    /// </summary>
    /// <remarks>
    /// 通常生成では <c>"internal"</c> のままで出力はバイト不変。パッケージ書き出し時は <c>"public"</c> を渡し、
    /// 生成コード（別アセンブリ）や別パッケージから参照可能にする。
    /// </remarks>
    public string InfraVisibility { get; init; } = "internal";

    /// <summary>
    /// スキーマ非依存の固定 infra 型（契約・方言エンジン・EF 共通部品・EntityBase/属性/VO 基底 等）を出力するか（既定 true）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通常生成（インライン）では <c>true</c> で固定 infra を生成コードに同梱し、出力はバイト不変。
    /// パッケージ参照モード（<see cref="CodeGenerationOptions.UseRuntimePackages"/>）の生成では <c>false</c> を渡し、
    /// 固定 infra を出力せず NuGet パッケージ <c>QuickER.Runtime.*</c> の型を <c>using</c> で参照する。
    /// </para>
    /// <para>
    /// <see cref="RuntimePackageExport"/>（パッケージ用ソースの<b>書き出し</b>）とは別軸。書き出し時は <c>true</c>（固定 infra を全出力）、
    /// 参照モードの通常生成時は <c>false</c>（固定 infra を落として per-entity・DI・DbContext のスキーマ依存物だけを残す）。
    /// スキーマ依存物（Entity/EditModel/Mapper/VO 具象/I{Entity}Repository/エンティティ別実装/DI 登録）は本フラグに依らず常に出力する。
    /// </para>
    /// </remarks>
    public bool EmitSharedInfra { get; init; } = true;

    /// <summary>
    /// パッケージ参照モードで生成ファイルの先頭コメントへ載せる案内テキスト（必要な PackageReference 等）。既定は空。
    /// </summary>
    /// <remarks>
    /// 通常生成（インライン）では空リストを渡し、ヘッダに追加行は出ない（バイト不変）。パッケージ参照モードのみ
    /// <see cref="RuntimePackageReferenceGuidance"/> の出力を渡し、各行を <c>// </c> 接頭辞でヘッダへ差し込む。
    /// </remarks>
    public IReadOnlyList<string> PackageGuidanceLines { get; init; } = [];

    /// <summary>
    /// ブロック名前空間（<see cref="BlockNamespace"/>）の内側へ出力する using（名前空間スコープ using）。既定は空。
    /// </summary>
    /// <remarks>
    /// 非分割マルチ方言のパッケージ参照モードで、方言エンジンパッケージ（<c>QuickER.Runtime.SqlServer</c> /
    /// <c>QuickER.Runtime.Sqlite</c>）を各方言 namespace ブロックの内側へ限定して <c>using</c> する。
    /// ファイル先頭で両方言パッケージを開くと <c>ISqlConnectionFactory</c> 等が方言間で曖昧参照になるため、
    /// 方言別実装のブロック内でだけ自方言のパッケージを開く（インライン多方言が方言別 namespace 内で型を定義していたのと対称）。
    /// 通常生成では空（追加行なし＝バイト不変）。
    /// </remarks>
    public IReadOnlyList<string> BlockUsings { get; init; } = [];
}

/// <summary>生成モデルを Scriban テンプレートで C# ソースコードへレンダリングするレンダラー</summary>
internal sealed class ScribanCSharpRenderer
{
    /// <summary>Entity / EditModel / Mapper / Repository を一括出力する Scriban テンプレート本文</summary>
    /// <remarks>
    /// テンプレート本文はソースに埋め込まず、埋め込みリソース（Templates/CSharpRuntime.scriban）として保持する。
    /// インデントは半角スペース 4 つで統一する（タブは使用しない）。
    /// </remarks>
    private static readonly string TemplateText = LoadTemplate();

    /// <summary>埋め込みリソースから Scriban テンプレート本文を読み込む</summary>
    private static string LoadTemplate()
    {
        const string resourceName = "QuickER.Generator.Templates.CSharpRuntime.scriban";
        var assembly = typeof(ScribanCSharpRenderer).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"埋め込みリソース '{resourceName}' が見つかりません。{Environment.NewLine}"
                    + $"アセンブリ '{assembly.GetName().Name}' に Templates/CSharpRuntime.scriban が "
                    + "EmbeddedResource として含まれているか確認してください。"
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>テンプレートは固定なので一度だけ解析してキャッシュする（分割時は同じテンプレートを範囲を変えて複数回描画する）</summary>
    private static readonly Template ParsedTemplate = ParseTemplate();

    /// <summary>テンプレートを解析し、解析エラーがあれば例外を投げる</summary>
    private static Template ParseTemplate()
    {
        var template = Template.Parse(TemplateText);
        if (template.HasErrors)
        {
            var message = string.Join(
                Environment.NewLine,
                template.Messages.Select(m => m.ToString())
            );
            throw new InvalidOperationException(
                $"C# 生成テンプレートの解析に失敗しました。{Environment.NewLine}{message}"
            );
        }

        return template;
    }

    /// <summary>
    /// 生成モデルとオプションを、指定スコープ（名前空間・using・出力するバケット）でテンプレートへ流し込み、C# ソースコード文字列を生成する
    /// </summary>
    /// <remarks>非分割時は全バケットを 1 回で、分割時はファイルごとにバケットを絞って複数回呼び出す</remarks>
    public string Render(
        CSharpGenerationModel model,
        CodeGenerationOptions options,
        RenderScope scope
    )
    {
        var template = ParsedTemplate;

        // 独自属性 NavigationReference は (1) Entity のナビゲーションプロパティへの付与、
        // (2) 共通契約の EntitySaveMetadata / SqlQuery によるナビゲーション除外・Include 復元（リフレクション走査）で参照される。
        // リレーションが無くても契約（自作 Repository か EF Core のいずれか）を生成する場合は属性定義が必要なため、その条件も含める。
        var emitNavRefAttr =
            (options.GenerateEntityClasses && model.EntityClasses.Any(c => c.Navigations.Count > 0))
            || options.GenerateRepositories
            || options.GenerateEfCore;

        // 自作 Repository の生成方言に応じた原始変数群。sqlserver のときは現行値（識別子クォート [ ]・
        // ADO 型 SqlXxx）そのままで、テンプレートは細粒度置換した箇所でこれらを参照する。方言リテラルを
        // 変数参照へ置き換えてもレンダリング結果は変わらないため、sqlserver 出力はバイト不変を保つ。
        // 塊で異なる領域（FOR JSON プランナ vs マルチクエリ Include・OFFSET/FETCH vs LIMIT/OFFSET・
        // SqlParameter 型付け等）はテンプレート側で {{ if repository_dialect == "sqlserver" }} ／ else により出し分ける。
        // 方言はスコープから受け取る（マルチ方言時は方言実装スペックごとに異なる。単一方言時は実効単一方言）。
        var dialect = new RepositoryDialectVariables(scope.Dialect);

        // SqlColumnType 属性は Entity プロパティに DB 列のメタ情報（SqlDbType・Size・Precision・Scale）を載せる。
        // ランタイムの EntitySaveMetadata が明示 SqlParameter を組み立てるのに使うほか、利用者コードが列メタ情報
        // （最大長・桁数）を参照する用途も兼ねるため、Repository 生成時 または IncludeDataAnnotations 時のいずれか、
        // かつ SqlDbType が判明したプロパティが 1 つでもある場合に属性定義と付与を出力する。
        // [SqlColumnType]（System.Data.SqlDbType）は SQL Server 専用の意味を持つため、sqlserver が生成対象方言に
        // 含まれるときのみ出力する（SQLite は CLR 型から SqliteType を導出でき属性不要。生成物に SqlDbType 依存を出さない）。
        // マルチ方言で図の方言が非 sqlserver でも、sqlserver 実装が属性を要するため「sqlserver がターゲットに含まれるか」で判定する
        // （SqlDbTypeName は sqlserver 辞書からサービス側で共有 Entity へ補完済み）。属性の定義・付与は Entity/Runtime を
        // 出力するスペックで行うため、方言実装スペック（Entity を出さない）ではこの条件が空振りして無害。
        var sqlServerInTargets = options.EffectiveRepositoryDialects.Contains(
            "sqlserver",
            StringComparer.OrdinalIgnoreCase
        );
        var emitSqlColumnTypeAttr =
            sqlServerInTargets
            && (options.GenerateRepositories || options.IncludeDataAnnotations)
            && model.EntityClasses.Any(c => c.Properties.Any(p => p.SqlDbTypeName is not null));

        // DB 定義メタ属性（[DbColumnMeta] / [DbTableMeta]）は、生成 Entity を「DB 定義の自己記述ドキュメント」に
        // するための方言中立メタ（型トークン・説明）を載せる。付与は対象 DB・Repository/EF 設定に依らず、
        // データアノテーション付与（[Table]/[Column] と同列）かつ Entity 生成時のみ。canonical 由来のため
        // 可搬図では方言によらず同一メタになる。刻む中身が 1 つでもある（トークン付き列 または 説明付きテーブル/列）
        // ときだけ属性定義・付与を出力し、実体のない属性クラスは出さない。
        var emitDbMetaAttr =
            options.IncludeDataAnnotations
            && options.GenerateEntityClasses
            && model.EntityClasses.Any(c =>
                !string.IsNullOrEmpty(c.Description)
                || c.Properties.Any(p =>
                    p.CanonicalTypeToken is not null || !string.IsNullOrEmpty(p.Description)
                )
            );

        // using は呼び出し側（GeneratedFileUsings）がバケット単位で解決済み。EF Core など外部依存の
        // 出し分けもそこで完結するため、レンダラーでは受け取った集合をそのまま流し込む
        var scriptObject = new Scriban.Runtime.ScriptObject
        {
            ["namespace_name"] = scope.NamespaceName,
            ["usings"] = scope.Usings,
            // ファイルヘッダ（auto-generated・using）と namespace 形式（file-scoped / block）の出し分け。
            // 単一スペックのファイルは render_header=true・block_namespace=false で従来出力（バイト不変）。
            // 非分割マルチ方言で 1 ファイルへ複数 namespace を連結するときは、先頭スペックのみヘッダを出し
            // 各スペックを block namespace で包む。
            ["render_header"] = scope.RenderHeader,
            ["block_namespace"] = scope.BlockNamespace,
            // マルチ方言レイアウトかどうかと、DI 拡張の方言別サフィックス（SqlServer / Sqlite）
            ["multi_dialect"] = scope.MultiDialect,
            ["dialect_di_suffix"] = GeneratedFilePlanner.DialectNamespaceSuffix(scope.Dialect),
            ["entity_classes"] = model.EntityClasses,
            ["edit_model_classes"] = model.EditModelClasses,
            ["mapper_classes"] = model.MapperClasses,
            ["repository_classes"] = model.RepositoryClasses,
            ["include_data_annotations"] = options.IncludeDataAnnotations,
            ["include_json_ignore_on_parent_navigation"] =
                options.IncludeJsonIgnoreOnParentNavigation,
            ["emit_nav_ref_attr"] = emitNavRefAttr,
            ["emit_sql_column_type_attr"] = emitSqlColumnTypeAttr,
            ["emit_db_meta_attr"] = emitDbMetaAttr,
            ["generate_value_objects"] = options.GenerateValueObjects,
            ["value_object_classes"] = model.ValueObjectClasses,
            ["ef_core"] = model.EfCore,
            // 出力するバケットの絞り込み（分割時はファイルごとに切り替える。非分割時は全 true）
            ["render_runtime"] = scope.Runtime,
            ["render_value_objects"] = scope.ValueObjects,
            ["render_entities"] = scope.Entities,
            ["render_edit_models"] = scope.EditModels,
            ["render_mappers"] = scope.Mappers,
            ["render_ef_core"] = scope.EfCore,
            // 共通契約（インターフェイス・SqlQuery・メタデータ・グラフセーバ・RawSqlMapper 等）を出力するか。
            // 契約は Repository バケットに属し、分割時も Repository バケットのファイルにのみ出力する（EF 側は using で参照）。
            // マルチ方言時は契約スペックのみ true・方言実装スペックは false（契約は 1 回だけ出す）
            ["render_contract"] = scope.RenderContract,
            // 自作 Repository の方言別実装（ADO 依存）を出力するか。Repository バケット内でこのフラグにより契約と実装を出し分ける
            // （EF 単独出力＝false のとき ADO 依存のコードを一切生成しない）
            ["repositories"] = scope.RepositoryImpl,
            // 自作 Repository の生成方言と方言別プリミティブ（識別子クォート・ADO 型名）。
            ["repository_dialect"] = dialect.Dialect,
            ["quote_open"] = dialect.QuoteOpen,
            ["quote_close"] = dialect.QuoteClose,
            ["quote_open_char"] = dialect.QuoteOpenChar,
            ["quote_close_char"] = dialect.QuoteCloseChar,
            ["sql_connection_type"] = dialect.ConnectionType,
            ["sql_command_type"] = dialect.CommandType,
            ["sql_parameter_type"] = dialect.ParameterType,
            ["sql_data_reader_type"] = dialect.DataReaderType,
            ["sql_transaction_type"] = dialect.TransactionType,
            ["connection_factory_impl_type"] = dialect.ConnectionFactoryImplType,
            ["repository_base_class"] = dialect.RepositoryBaseClass,
            ["sql_query_executor_class"] = dialect.SqlQueryExecutorClass,
            // ランタイムのパッケージ書き出しモードと固定 infra の可視性。通常生成では既定（false / "internal"）で
            // 供給し、出力はバイト不変。パッケージ書き出し時のみ RuntimePackageSourceRenderer が true / "public" を渡す。
            // 全既存経路へ必ず供給する（供給漏れがあると scriban が空文字を出しバイト不変が壊れるため）。
            ["runtime_package_export"] = scope.RuntimePackageExport,
            ["infra_visibility"] = scope.InfraVisibility,
            // スキーマ非依存の固定 infra を出力するか。通常生成は true（バイト不変）。パッケージ参照モードの
            // 通常生成のみ false を渡し、固定 infra を落として per-entity・DI・DbContext だけを残す。
            // 全既存経路へ必ず供給する（供給漏れは scriban が空文字を出しバイト不変を壊すため）。
            ["emit_shared_infra"] = scope.EmitSharedInfra,
            // パッケージ参照モードでヘッダへ載せる案内行。通常生成は空リスト（追加行なし＝バイト不変）。
            ["package_guidance_lines"] = scope.PackageGuidanceLines,
            // ブロック名前空間の内側へ出す方言限定 using（非分割マルチ方言のパッケージ参照モードのみ非空）。
            ["block_usings"] = scope.BlockUsings,
        };

        // テンプレートは本ライブラリ内に固定で持つ信頼済みのものであり、ループ回数・出力量は ER 図の規模に
        // 応じて正当に増減する。Scriban 既定の上限のままだと大規模スキーマで出力が無言で打ち切られるため、
        // 関連する上限をすべて無効化（0 = 無制限）して全件を確実に出力する。
        //   - LoopLimit: ループ反復回数の上限（既定 1000）
        //   - LimitToString: レンダリング出力長の上限（既定 1MB = 1048576 文字。超過分は "..." で切り捨て）
        var context = new TemplateContext { LoopLimit = 0, LimitToString = 0 };

        context.PushGlobal(scriptObject);
        var rendered =
            template.Render(context).ReplaceLineEndings(Environment.NewLine).TrimEnd()
            + Environment.NewLine;

        // 条件ブロック（{{ if }}）のスキップ時などに生じる連続空行を 1 行へ正規化する。
        // C# では 2 行以上連続する空行は不要で、CSharpier も 1 行へ畳むため、それに合わせる。
        return Regex.Replace(
            rendered,
            $"(?:{Regex.Escape(Environment.NewLine)}){{3,}}",
            Environment.NewLine + Environment.NewLine
        );
    }
}

/// <summary>
/// 自作 Repository の生成方言ごとに変わるプリミティブ（識別子クォート文字・ADO 型名）を保持する。
/// </summary>
/// <remarks>
/// テンプレートはこれらを細粒度置換した箇所で参照する。sqlserver は現行値（識別子クォート <c>[</c> <c>]</c>・
/// <c>SqlConnection</c> 等）を返し、レンダリング結果を変えない（SQL Server 生成物のバイト不変を保つ）。
/// sqlite は識別子クォート <c>"</c> と Microsoft.Data.Sqlite の <c>SqliteConnection</c> 等を返す。
/// 未知方言は sqlserver 相当へフォールバックする（塊で異なる SQL は
/// テンプレート側の <c>{{ if repository_dialect == "sqlserver" }}</c> ／ <c>else</c> で出し分ける）。
/// </remarks>
internal sealed class RepositoryDialectVariables
{
    public RepositoryDialectVariables(string? dialect)
    {
        Dialect = string.IsNullOrWhiteSpace(dialect) ? "sqlserver" : dialect;

        // 方言ごとに ADO 型名・識別子クォート・基底クラス名を切り替える。方言固有の SQL の塊
        // （FOR JSON プランナ vs マルチクエリ Include 等）はテンプレートの if 分岐が担うため、
        // ここは細粒度プリミティブ（型名・クォート）に留める。未知方言は sqlserver 相当へフォールバックする。
        switch (Dialect)
        {
            // SQLite: 識別子は二重引用符（"）、ADO 型は Microsoft.Data.Sqlite の Sqlite 系。
            // クォートは通常の C# 補間文字列 $"..." 内へ埋め込むため、バックスラッシュでエスケープした \" の形で持つ
            // （$"..." では "" は無効。エスケープは \" が正しい）。文字リテラル 'x' 比較用には素の 1 文字（"）を別変数で持つ。
            case "sqlite":
                QuoteOpen = "\\\"";
                QuoteClose = "\\\"";
                QuoteOpenChar = "\"";
                QuoteCloseChar = "\"";
                ConnectionType = "SqliteConnection";
                CommandType = "SqliteCommand";
                ParameterType = "SqliteParameter";
                DataReaderType = "SqliteDataReader";
                TransactionType = "SqliteTransaction";
                ConnectionFactoryImplType = "SqliteConnectionFactory";
                RepositoryBaseClass = "SqliteRepository";
                SqlQueryExecutorClass = "SqliteSqlQueryExecutor";
                break;

            default:
                QuoteOpen = "[";
                QuoteClose = "]";
                QuoteOpenChar = "[";
                QuoteCloseChar = "]";
                ConnectionType = "SqlConnection";
                CommandType = "SqlCommand";
                ParameterType = "SqlParameter";
                DataReaderType = "SqlDataReader";
                TransactionType = "SqlTransaction";
                ConnectionFactoryImplType = "SqlConnectionFactory";
                RepositoryBaseClass = "SqlServerRepository";
                SqlQueryExecutorClass = "SqlServerSqlQueryExecutor";
                break;
        }
    }

    /// <summary>生成方言（既定 "sqlserver"）</summary>
    public string Dialect { get; }

    /// <summary>識別子クォート開始（C# 文字列リテラル埋め込み用。SQL Server: <c>[</c>、SQLite: <c>""</c>）</summary>
    public string QuoteOpen { get; }

    /// <summary>識別子クォート終了（C# 文字列リテラル埋め込み用。SQL Server: <c>]</c>、SQLite: <c>""</c>）</summary>
    public string QuoteClose { get; }

    /// <summary>識別子クォート開始の 1 文字（C# 文字リテラル <c>'x'</c> 用。SQL Server: <c>[</c>、SQLite: <c>"</c>）</summary>
    public string QuoteOpenChar { get; }

    /// <summary>識別子クォート終了の 1 文字（C# 文字リテラル <c>'x'</c> 用。SQL Server: <c>]</c>、SQLite: <c>"</c>）</summary>
    public string QuoteCloseChar { get; }

    /// <summary>接続型名（SQL Server: <c>SqlConnection</c>）</summary>
    public string ConnectionType { get; }

    /// <summary>コマンド型名（SQL Server: <c>SqlCommand</c>）</summary>
    public string CommandType { get; }

    /// <summary>パラメータ型名（SQL Server: <c>SqlParameter</c>）</summary>
    public string ParameterType { get; }

    /// <summary>データリーダー型名（SQL Server: <c>SqlDataReader</c>）</summary>
    public string DataReaderType { get; }

    /// <summary>トランザクション型名（SQL Server: <c>SqlTransaction</c>）</summary>
    public string TransactionType { get; }

    /// <summary>接続ファクトリ実装クラス名（SQL Server: <c>SqlConnectionFactory</c>）</summary>
    public string ConnectionFactoryImplType { get; }

    /// <summary>Repository 基底クラス名（SQL Server: <c>SqlServerRepository</c>、SQLite: <c>SqliteRepository</c>）</summary>
    public string RepositoryBaseClass { get; }

    /// <summary>SqlQuery の ADO 実行器クラス名（SQL Server: <c>SqlServerSqlQueryExecutor</c>、SQLite: <c>SqliteSqlQueryExecutor</c>）</summary>
    public string SqlQueryExecutorClass { get; }
}
