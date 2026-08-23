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
    /// <c>emit_file</c> での提出を許可するファイル拡張子（先頭ドット付き・大文字小文字を無視して照合する集合）。
    /// </summary>
    /// <remarks>
    /// このターゲットの UI 層ソースだけを列挙する。提出可否はこのホワイトリストで決まり、実効の許可集合は
    /// <see cref="MockProjectEmitTools.SupportedEmitExtensions"/>（中央の上限集合）との積になる。
    /// </remarks>
    internal abstract IReadOnlySet<string> AllowedEmitExtensions { get; }

    // ── スキャフォールド差分（決定的な土台） ──

    /// <summary>csproj スケルトンを組み立てる（ターゲットの SDK・TFM・PackageReference 差分を含む）</summary>
    internal abstract string BuildCsproj(string rootNamespace, string? repositoryDialect);

    /// <summary>規約ドキュメント（README）を組み立てる（ターゲット固有の実装規約・起動手順を含む）</summary>
    internal abstract string BuildReadme(
        string projectName,
        string rootNamespace,
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
            $"未対応のモックプロジェクトターゲットです: {target.Id}",
            nameof(target)
        );
    }
}
