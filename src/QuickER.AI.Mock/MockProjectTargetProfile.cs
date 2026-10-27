namespace QuickER.AI.Mock;

/// <summary>
/// 第2ステップ（モックプロジェクト生成）の「ターゲット（プラットフォーム）差分」を一箇所に束ねる抽象。
/// </summary>
/// <remarks>
/// <para>
/// スキャフォールド（csproj／README）と、決定的ではない UI 層生成のプロンプト（エージェント型・API キー型の双方）には
/// ターゲット固有の文面（フレームワーク・ビュー種別・エントリポイント・語彙など）が混ざる。それらの差分だけを
/// この型のフラグメントとして切り出し、<see cref="MockProjectScaffoldService"/> と <see cref="MockProjectPromptBuilder"/>
/// は「共有部＋フラグメント」を合成する形にする。共有規約（PK 採番・NuGet.Config 禁止・非対話・Repository 経由・
/// 追加指示連結・ビルド検証・emit の提出方法など）はターゲットに依らないため、呼び出し側に残す。
/// </para>
/// <para>
/// 実装は WPF（<see cref="WpfMockProjectTargetProfile"/>）と Blazor Web App（<see cref="BlazorMockProjectTargetProfile"/>）の
/// 2 種。<see cref="MockProjectTarget"/> と一対一で対応し、<see cref="Resolve"/> で解決する。型自体は公開
/// <see cref="MockProjectAgentRequest"/> に載るため <c>public</c> だが、ターゲット差分の各メンバーは <c>internal</c> で
/// アセンブリ内（本機能と単体テスト）に閉じる。
/// </para>
/// </remarks>
public abstract class MockProjectTargetProfile
{
    /// <summary>WPF ターゲットのプロファイル</summary>
    internal static MockProjectTargetProfile Wpf { get; } = new WpfMockProjectTargetProfile();

    /// <summary>Blazor Web App ターゲットのプロファイル</summary>
    internal static MockProjectTargetProfile Blazor { get; } = new BlazorMockProjectTargetProfile();

    /// <summary>ターゲット（プラットフォーム）識別子</summary>
    internal abstract MockProjectTarget Target { get; }

    /// <summary>UI 層の成果物ファイルを検出する検索パターン（成果物検証・承認待ち検知に使う。WPF は <c>*.xaml</c>）</summary>
    internal abstract string UiFileSearchPattern { get; }

    /// <summary>
    /// プロジェクトフォルダの中の、画面として数える UI 成果物の数を返す（成果物検証で設計の画面の数と比べる）。
    /// </summary>
    /// <remarks>走査は <see cref="MockProjectFiles"/>（obj / bin を数えない・範囲はプロジェクトフォルダ配下）。</remarks>
    internal abstract int CountScreenArtifacts(string projectDirectory);

    /// <summary>
    /// 画面として数えた成果物が設計の画面の数に届かないとき、実行全体を失敗にするか（false なら警告の行だけを出す）。
    /// </summary>
    /// <remarks>
    /// 数え方が画面の作り方に左右されないターゲットだけが true を宣言する。ランナーはターゲットの名前で分岐せず、この値だけを見る。
    /// </remarks>
    internal abstract bool ScreenShortfallFailsRun { get; }

    /// <summary>
    /// <c>emit_file</c> での提出を許可するファイル拡張子（先頭ドット付き・大文字小文字を無視して照合する集合）。
    /// </summary>
    /// <remarks>
    /// このターゲットの UI 層ソースだけを列挙する。提出可否はこのホワイトリストで決まり、実効の許可集合は
    /// <see cref="MockProjectEmitTools.SupportedEmitExtensions"/>（中央の上限集合）との積になる。
    /// </remarks>
    internal abstract IReadOnlySet<string> AllowedEmitExtensions { get; }

    /// <summary>
    /// このターゲットの共有フレームワークが <c>System.Security.Cryptography.Pkcs</c> を刈り込むか。
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL Server の csproj に Pkcs の明示参照を書くかは、この値だけで決める。<c>Microsoft.Data.SqlClient</c> が
    /// 推移的に持ち込む Pkcs の版には既知の脆弱性があり、刈り込まないターゲットでは restore が NU1902 / NU1903 を出すので、
    /// 修正済みの版を明示参照で上書きする。刈り込むターゲットでは推移的な版は解決されず警告も出ない一方、明示参照を書くと
    /// restore が NU1510（参照は刈り込まれず不要）を出すので書かない。
    /// </para>
    /// <para>
    /// 判定の基準は「そのターゲットの共有フレームワークの刈り込みの一覧に Pkcs が載っているか」で、DLL が
    /// 共有フレームワークのフォルダにあるかではない（<c>Microsoft.AspNetCore.App</c> は DLL を持つが刈り込まない）。
    /// ターゲットを足すときは、SqlClient を参照する最小の csproj を restore して、<c>obj/project.assets.json</c> の
    /// <c>packagesToPrune</c> に Pkcs が載るか（あるいは NU1902 / NU1903 が出るか）で決める。WPF
    /// （<c>Microsoft.WindowsDesktop.App</c>）は刈り込み、Blazor（<c>Microsoft.NET.Sdk.Web</c>）は刈り込まない。
    /// </para>
    /// </remarks>
    internal abstract bool SharedFrameworkPrunesCryptographyPkcs { get; }

    // ── スキャフォールド差分（決定的な土台） ──

    /// <summary>csproj スケルトンを組み立てる（ターゲットの SDK・TFM・PackageReference 差分を含む）</summary>
    internal abstract string BuildCsproj(string rootNamespace, string? repositoryDialect);

    /// <summary>規約ドキュメント（README）を組み立てる（ターゲット固有の実装規約・起動手順を含む）</summary>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="namespaces">生成されるデータ層の名前空間（生成の計画から求めた値）</param>
    /// <param name="repositoryDialect">QuickER 版 Repository の方言（出さない図は null）</param>
    internal abstract string BuildReadme(
        string projectName,
        MockProjectNamespaces namespaces,
        string? repositoryDialect
    );

    // ── エージェント型 system プロンプトのターゲット差分 ──

    /// <summary>雛形の説明語（「〜プロジェクトの雛形とデータ層のコード」）</summary>
    internal abstract string SystemScaffoldNoun { get; }

    /// <summary>デザイン仕様の再現規約（「各画面を〜で忠実に再現し…」の一文）</summary>
    internal abstract string SystemScreenReproductionRule { get; }

    /// <summary>UI フレームワーク／ビュー種別の規約（2 行の箇条書き）</summary>
    internal abstract string SystemUiFrameworkRules { get; }

    /// <summary>「# 進め方」のターゲット固有ステップ（先頭 3 項目・ビルド検証と報告は共有側）</summary>
    internal abstract string SystemWorkflowSteps(string projectName);

    // ── エージェント型 初回プロンプトのターゲット差分 ──

    /// <summary>UI 層の呼称（「WPF UI 層」等）</summary>
    internal abstract string UiLayerName { get; }

    /// <summary>初回プロンプト手順 4 のターゲット固有本文（末尾の「DI には…」は共有側）</summary>
    internal abstract string PromptImplementStep { get; }

    /// <summary>初回プロンプト完了条件のビュー項目（「各画面が〜として存在し…」の一項目）</summary>
    internal abstract string PromptViewCriterion { get; }

    // ── API キー型プロンプトのターゲット差分 ──

    /// <summary>API キー型 system プロンプトの UI フレームワーク／ビュー種別の規約（2 行の箇条書き）</summary>
    internal abstract string ApiKeyUiFrameworkRules { get; }

    /// <summary>API キー型 system プロンプトのデザイン仕様の再現規約（1 項目）</summary>
    internal abstract string ApiKeyScreenReproductionRule { get; }

    /// <summary>API キー型 共通部リクエストの emit 指示（提出ファイル一覧＋土台作成の案内）</summary>
    internal abstract string ApiKeyCommonEmitInstructions(string projectName);

    /// <summary>API キー型 画面リクエストの実装指示（「〜で忠実に再現し、View と ViewModel を提出…」の一文）</summary>
    internal abstract string ApiKeyScreenInstruction(string projectName);

    /// <summary>API キー型 system プロンプトの emit_file パス例（英語の "and" で連ねたターゲット代表例 2 つ）</summary>
    internal abstract string ApiKeyEmitPathExamples(string projectName);

    /// <summary>
    /// API キー型 system プロンプトの「データ層の名前空間を using する」規約（1 項目）。
    /// </summary>
    /// <remarks>
    /// データ層は分割出力で型ごとに別の名前空間へ出るので、どのファイルにどの using を書くかをターゲットの流儀で示す。
    /// 名前空間そのものは共通部のリクエストに添えるデータ層の要約が名指しする。
    /// </remarks>
    internal abstract string ApiKeyNamespaceRule { get; }

    // ── ターゲット共通のスキャフォールド部品 ──

    /// <summary>
    /// 方言の ADO.NET パッケージの PackageReference 行を返す（対応方言でなければ空）。版は
    /// <see cref="MockProjectPackageVersions"/>（正本は <c>Directory.Packages.props</c>）から取る。
    /// </summary>
    /// <remarks>
    /// SQLite は、QuickER 本体・サンプルと同じく、<c>Microsoft.Data.Sqlite</c> が推移的に持ち込む SQLitePCLRaw（2.1 系）でなく
    /// 3.0 系の版を明示参照して、同梱の SQLite を QuickER が確かめた版にそろえる。
    /// SQL Server は、共有フレームワークが Pkcs を刈り込まないターゲット（<see cref="SharedFrameworkPrunesCryptographyPkcs"/>）
    /// でだけ、修正済みの System.Security.Cryptography.Pkcs を明示参照で上書きする。
    /// </remarks>
    private protected string BuildAdoPackageReferences(string? repositoryDialect) =>
        repositoryDialect switch
        {
            "sqlserver" => PackageReferenceLine(MockProjectPackageVersions.SqlClient)
                + (
                    SharedFrameworkPrunesCryptographyPkcs
                        ? string.Empty
                        : "    <!-- Microsoft.Data.SqlClient brings in a System.Security.Cryptography.Pkcs version with known "
                            + "vulnerabilities (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7), so the fixed version is referenced "
                            + "explicitly to override it -->\n"
                            + PackageReferenceLine(MockProjectPackageVersions.CryptographyPkcs)
                ),
            "sqlite" => PackageReferenceLine(MockProjectPackageVersions.Sqlite)
                + "    <!-- SQLitePCLRaw 3.0 is referenced explicitly in place of the 2.1 version Microsoft.Data.Sqlite "
                + "brings in, so the bundled SQLite is the version QuickER is tested with. SQLitePCLRaw 2.1.11 and earlier "
                + "also have a known vulnerability (GHSA-2m69-gcr7-jv3q) -->\n"
                + PackageReferenceLine(MockProjectPackageVersions.SqliteNativeBundle),
            _ => string.Empty,
        };

    /// <summary>
    /// README の「データ層の名前空間」の節を返す（生成するファイルごとの名前空間の一覧。末尾改行なし）。
    /// </summary>
    private protected static string BuildNamespaceSection(MockProjectNamespaces namespaces) =>
        "## Namespaces of the data layer\n\n"
        + "Each file under `Generated/` declares its own namespace, and the parent namespace "
        + $"`{ParentNamespace(namespaces.Entities)}` declares no types, so add a `using` for every namespace you use.\n\n"
        + string.Join(
            "\n",
            namespaces.Files.Select(file => $"- `Generated/{file.FileName}`: `{file.Namespace}`")
        );

    /// <summary>名前空間の親（最後の <c>.</c> より前）を返す</summary>
    private static string ParentNamespace(string name) =>
        name.LastIndexOf('.') is var dot and > 0 ? name[..dot] : name;

    /// <summary>csproj の PackageReference 1 行（字下げ 4・末尾改行つき）を返す</summary>
    private protected static string PackageReferenceLine(string packageId) =>
        $"    <PackageReference Include=\"{packageId}\" Version=\"{MockProjectPackageVersions.Get(packageId)}\" />\n";

    /// <summary>README の「実 DB への切り替え」手順 3 を返す（DI 登録は方言別の拡張メソッド名と、その名前空間で案内する）</summary>
    private protected static string BuildDialectSwitchGuide(
        string? repositoryDialect,
        MockProjectNamespaces namespaces
    ) =>
        repositoryDialect switch
        {
            "sqlserver" =>
                "3. To switch to a real database (SQL Server), replace `AddGeneratedInMemoryRepositories()` with "
                    + $"`AddGeneratedSqlServerRepositories(connectionString)`, which is declared in the "
                    + $"`{namespaces.DatabaseRegistration}` namespace (add `using {namespaces.DatabaseRegistration};`).",
            "sqlite" =>
                "3. To switch to a real database (SQLite), replace `AddGeneratedInMemoryRepositories()` with "
                    + $"`AddGeneratedSqliteRepositories(connectionString)`, which is declared in the "
                    + $"`{namespaces.DatabaseRegistration}` namespace (add `using {namespaces.DatabaseRegistration};`).",
            _ =>
                "3. To switch to a real database, regenerate the QuickER repositories for a supported dialect "
                    + "(SQL Server / SQLite) with QuickER, then replace `AddGeneratedInMemoryRepositories()` with "
                    + "the matching DI registration.",
        };

    /// <summary>ターゲットから対応するプロファイルを解決する（未知のターゲットは例外）</summary>
    /// <exception cref="ArgumentException">未対応のターゲットが渡された場合</exception>
    internal static MockProjectTargetProfile Resolve(MockProjectTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (string.Equals(target.Id, MockProjectTarget.Wpf.Id, StringComparison.Ordinal))
        {
            return Wpf;
        }

        if (string.Equals(target.Id, MockProjectTarget.Blazor.Id, StringComparison.Ordinal))
        {
            return Blazor;
        }

        throw new ArgumentException(
            $"Unsupported mock project target: {target.Id}",
            nameof(target)
        );
    }
}
