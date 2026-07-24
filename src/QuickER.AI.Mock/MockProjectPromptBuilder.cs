namespace QuickER.AI.Mock;

/// <summary>
/// WPF モックプロジェクト生成（第2ステップ）のシステムプロンプト・初回プロンプトを組み立てる共有ヘルパ。
/// </summary>
/// <remarks>
/// バックエンド（Claude Code / Codex）に依らずプロンプト本文は同一である必要があるため、
/// <see cref="ClaudeCodeMockProjectAgent"/> と <see cref="CodexMockProjectAgent"/> の双方がここを参照する
/// （プロンプト本文の重複コピーを避ける正本）。各バックエンドは、ここで得たシステムプロンプトを
/// それぞれの流儀（Claude Code＝<c>--append-system-prompt</c>／Codex＝developer instructions）で渡す。
/// </remarks>
internal static class MockProjectPromptBuilder
{
    /// <summary>デザイン仕様のモックフォルダの相対パス（スキャフォールドが同梱する）</summary>
    public const string DesignFolderRelativePath = "design/mock";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    /// <summary>
    /// Codex 保険用の自動続行ナッジ（承認待ちで止まったターンを 1 回だけ後押しする固定文）。
    /// </summary>
    /// <remarks>
    /// Codex が計画提示だけで（承認待ちのまま）ターンを終えたと疑われるとき、
    /// <see cref="CodexMockProjectAgent"/> が同一スレッドへ 1 回だけ送る続行指示。
    /// </remarks>
    internal const string CodexContinuationNudge =
        "確認は不要です。承認を求めず、そのまま実装から dotnet build の検証まで完遂してください。";

    /// <summary>ヘッドレス実行のシステムプロンプト（規約・制約）を組み立てる</summary>
    /// <remarks>
    /// 出力は Visual Studio 標準構成（cwd 直下に <c>{ProjectName}.sln</c>、プロジェクト一式は <c>{ProjectName}/</c> 配下）。
    /// パス案内はプロジェクトフォルダ配下を指し、ビルドは cwd で <c>dotnet build</c>（sln を拾う）を指示する。
    /// ターゲット固有の文面（役割・フレームワーク・ビュー種別・進め方）は <paramref name="profile"/> から合成し、
    /// 共有規約（README 参照・読み取り専用・Repository 経由・PK 採番・NuGet.Config 禁止・ビルド検証）はここに残す。
    /// </remarks>
    internal static string BuildSystemPrompt(
        MockProjectTargetProfile profile,
        string projectName
    ) =>
        $@"あなたは {profile.Target.DisplayName} の熟練エンジニアで、既存のプロジェクトに GUI（UI 層）を実装します。
このフォルダは Visual Studio 標準構成で、直下にソリューション {projectName}.sln があり、プロジェクト一式は {projectName}/ フォルダ配下に、QuickER が生成した {profile.SystemScaffoldNoun}が既に用意されています。

これは非対話の自動実行（ヘッドレス）です。応答するユーザーはいません。計画の提示・確認・承認を求める質問は一切せず、このターン内で実装から `dotnet build` の検証まで完遂してください。

# 守るべき規約
- 作業を始める前に、必ず {projectName}/{ReadmeFileName} を読み、その規約に従ってください。
- {projectName}/{DesignFolderRelativePath}/ がデザイン仕様のモックフォルダです。まず {projectName}/{DesignFolderRelativePath}/mock.json を読んで画面一覧（screens）と画面遷移（transitions）を把握し、各画面の *.html（1 ファイル＝1 画面のデザイン仕様）と共有デザインシステム style.css を確認してください。{profile.SystemScreenReproductionRule}
- {projectName}/Generated/ 配下（データ層の自動生成コード）は読み取り専用です。絶対に編集・削除しないでください。UI からは I{{Entity}}Repository を DI 経由で使います。
{profile.SystemUiFrameworkRules}
- 画面のデータ表示・登録・更新・削除は、必ず {projectName}/Generated/ の生成コード（Entity / EditModel / Mapper / I{{Entity}}Repository）を使って実装してください。独自のデータクラスや ViewModel 内のハードコードされたリストで代用してはいけません（一覧・詳細は Repository から取得し、登録・更新・削除は Repository へ保存します）。
- 起動時の DI 登録は AddGeneratedInMemoryRepositories()（サンプルデータ入り）を使ってください（実 DB 接続は不要）。
- 新規作成（Insert）時の主キーは必ずアプリ側で採番してください（QuickER の Repository は DB 自動採番を使いません。未採番のままでは EditModel の検証や保存が失敗します）。主キーが値オブジェクト（GuidKey）の場合は無引数の Create() で新しいキーを生成できます。数値キーの場合は既存データの最大値＋1 等で採番します。
- NuGet.Config 等のパッケージソース設定ファイルを追加・変更しないでください（パッケージ参照は csproj の既存設定のまま復元します）。

# 進め方
{profile.SystemWorkflowSteps(projectName)}
- 実装が一段落したら、このフォルダ（ソリューション直下）で `dotnet build` を実行し、警告なし・エラーなしで通るまで修正を繰り返してください。
- 最後に、ビルドがエラー・警告なしで成功したことを確認した旨を報告してください。";

    /// <summary>初回プロンプト（実装の起点となる具体指示）を組み立てる</summary>
    /// <param name="profile">ターゲット差分（UI 層の呼称・実装手順・完了条件のビュー項目）</param>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="additionalInstructions">実装に対する追加指示（空／null なら付与しない）</param>
    internal static string BuildPrompt(
        MockProjectTargetProfile profile,
        string projectName,
        string? additionalInstructions
    )
    {
        var prompt =
            $@"プロジェクト『{projectName}』の {profile.UiLayerName}を実装してください。

このフォルダは Visual Studio 標準構成です。直下にソリューション {projectName}.sln があり、プロジェクト一式は {projectName}/ フォルダ配下にあります。UI 層のソースは {projectName}/ フォルダ配下（csproj と同じ場所）に追加してください。

手順:
1. まず {projectName}/{ReadmeFileName} を読み、プロジェクト構成と規約を把握する。
2. {projectName}/{DesignFolderRelativePath}/mock.json を読んで画面一覧（screens）と画面遷移（transitions）を把握し、各画面の *.html と共有 style.css を読んで、再現すべき画面構成・項目・遷移を把握する。
3. {projectName}/Generated/ 配下のデータ層（Entity / I{{Entity}}Repository / AddGeneratedInMemoryRepositories 等）を確認し、UI から利用する。
4. {profile.PromptImplementStep}DI には AddGeneratedInMemoryRepositories() を使う。
5. このフォルダ（ソリューション直下）で `dotnet build` を実行し、エラー・警告なしで通るまで自己修正する。
6. ビルドが成功したことを確認して報告する。

完了条件（すべて満たすこと）:
- {profile.PromptViewCriterion}
- 一覧・詳細・登録／編集のデータ操作がすべて I{{Entity}}Repository 経由である（独自データクラスやハードコードのリストで代用していない）。
- 新規登録（Insert）が主キーの採番込みで実際に保存できる。
- mock.json の transitions が画面遷移として動作する。
- `dotnet build` がエラー・警告なしで成功している。

{projectName}/Generated/ 配下は読み取り専用です。編集しないでください。

確認や承認を求めず、この指示だけで最後まで完遂してください。";

        // 追加指示があれば末尾へ「# 追加指示」として連結する（見出しは resx から解決＝表示言語追従）
        if (!string.IsNullOrWhiteSpace(additionalInstructions))
        {
            prompt +=
                "\n\n"
                + Resources.Strings.Mock_PromptUserInstructionsHeading
                + "\n"
                + additionalInstructions.Trim();
        }

        return prompt;
    }

    // ── API キー方式（固定パイプライン・emit_file による提出） ──

    /// <summary>
    /// API キー方式（ChatTurnEngine 系）の system プロンプトを組み立てる。
    /// </summary>
    /// <remarks>
    /// エージェント型（Claude Code / Codex）の <see cref="BuildSystemPrompt"/> を土台に、探索・自己ビルドの前提を外し、
    /// 「dotnet build を回して自己修正せよ」の指示を「与えられた情報だけで完全なファイルを提出せよ」へ置き換えた版。
    /// 実装ファイルの提出は <c>emit_file</c> ツール（<see cref="MockProjectEmitTools"/>）のみで行わせる。
    /// </remarks>
    /// <param name="profile">ターゲット差分（役割・フレームワーク／ビュー種別・画面再現の規約）</param>
    /// <param name="projectName">プロジェクト名</param>
    internal static string BuildApiKeySystemPrompt(
        MockProjectTargetProfile profile,
        string projectName
    ) =>
        $@"あなたは {profile.Target.DisplayName} の熟練エンジニアで、既存プロジェクトの UI 層（画面）を実装します。
データ層（Entity / EditModel / Mapper / I{{Entity}}Repository / インメモリ実装など）は QuickER が {projectName}/{MockProjectScaffoldService.GeneratedFolderName}/ 配下に生成済みで、あなたはそれを DI 経由で使う UI 層だけを書きます。

# 実装の規約
{profile.ApiKeyUiFrameworkRules}
- 起動時の DI 登録は AddGeneratedInMemoryRepositories()（サンプルデータ入り）を使ってください（実 DB 接続は不要）。
- データアクセスは I{{Entity}}Repository を DI 経由で受け取って使い、具象を直接 new しないでください。画面のデータ表示・登録・更新・削除は必ず Repository 経由とし、独自のデータクラスや ViewModel 内のハードコードされたリストで代用してはいけません。
- 新規作成（Insert）時の主キーは必ずアプリ側で採番してください（QuickER の Repository は DB 自動採番を使いません。未採番のままでは EditModel の検証や保存が失敗します）。主キーが値オブジェクト（GuidKey）の場合は無引数の Create() で新しいキーを生成できます。数値キーの場合は既存データの最大値＋1 等で採番します。
- NuGet.Config 等のパッケージソース設定ファイルを提出しないでください（提出しても拒否されます）。
- {profile.ApiKeyScreenReproductionRule}
- {projectName}/{MockProjectScaffoldService.GeneratedFolderName}/ 配下（データ層）・design/ 配下（デザイン仕様）・{ReadmeFileName}・.sln/.csproj は変更しないでください（提出しても拒否されます）。UI 層のソースだけを追加・更新します。

# ファイルの提出方法（重要）
- 実装ファイルを提出する唯一の手段は emit_file ツールです。チャット本文にコードを書いても反映されません。
- emit_file には、そのファイルの完全な内容（差分ではなく全文）を渡してください。同じ path への再提出は上書きです。
- path は出力プロジェクト配下の相対パスです（例 {profile.ApiKeyEmitPathExamples(projectName)}）。
- あなたはファイルの読み取りや dotnet build の実行はできません。与えられた情報だけを根拠に、コンパイルが通る完全なファイルを提出してください（存在が確認できないメンバーを憶測で呼ばない）。
- 各ターンで求められたファイル一式を、そのターン内で emit_file を使って必ず提出してください。";

    /// <summary>固定パイプラインの第 1 リクエスト（共通部＝App/MainWindow/ナビゲーション骨格/DI）のプロンプトを組み立てる</summary>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="schema">元になった ER スキーマ記述テキスト</param>
    /// <param name="screensOverview">画面一覧（mock.json）と遷移の要約</param>
    /// <param name="stylesheet">共有デザインシステム（style.css）の内容（空なら「なし」を案内）</param>
    /// <param name="profile">ターゲット差分（UI 層の呼称・共通部の emit 指示）</param>
    /// <param name="generatedSummary">データ層の契約要約（ファイル一覧＋主要 public シグネチャ）</param>
    internal static string BuildApiKeyCommonPrompt(
        MockProjectTargetProfile profile,
        string projectName,
        string schema,
        string screensOverview,
        string stylesheet,
        string generatedSummary
    ) =>
        $@"プロジェクト『{projectName}』の {profile.UiLayerName}を、共通部から実装します。

このターンでは次を emit_file で提出してください（UI 層のソースは {projectName}/ フォルダ配下へ置きます）:
{profile.ApiKeyCommonEmitInstructions(projectName)}

# データ層（DI で使う契約の要約）
{generatedSummary}

# 画面一覧（mock.json）と遷移
{screensOverview}

# 共有デザインシステム（style.css）
{DescribeStylesheetForPrompt(stylesheet)}

# 元の ER スキーマ
{DescribeOrPlaceholder(schema, "(スキーマ情報はありません)")}";

    /// <summary>固定パイプラインの第 2 以降のリクエスト（1 画面＝1 リクエスト）のプロンプトを組み立てる</summary>
    /// <param name="projectName">プロジェクト名</param>
    /// <param name="screen">対象画面（mock.json の該当エントリ）</param>
    /// <param name="screenHtml">対象画面のデザイン仕様 HTML 全文</param>
    /// <param name="transitions">この画面からの遷移の要約（無ければ空）</param>
    /// <param name="emittedFiles">これまでに提出済みのファイル相対パス一覧</param>
    /// <param name="profile">ターゲット差分（画面リクエストの実装指示）</param>
    internal static string BuildApiKeyScreenPrompt(
        MockProjectTargetProfile profile,
        string projectName,
        MockScreen screen,
        string screenHtml,
        string transitions,
        IReadOnlyList<string> emittedFiles
    ) =>
        $@"画面『{DescribeOrPlaceholder(screen.Name, screen.File)}』（{screen.File}）を実装してください。

{profile.ApiKeyScreenInstruction(projectName)}

# 画面の役割
{DescribeOrPlaceholder(screen.Description, "(説明はありません)")}

# デザイン仕様（{screen.File}）
{DescribeOrPlaceholder(screenHtml, "(この画面の HTML は取得できませんでした。画面名・役割・遷移から実装してください。)")}

# 遷移（この画面から）
{DescribeOrPlaceholder(transitions, "(この画面からの遷移はありません)")}

# これまでに提出済みのファイル（命名やナビゲーション結線の整合を保つため参照）
{DescribeEmittedFiles(emittedFiles)}";

    /// <summary>ビルド失敗時の修正リクエスト（固定 1 回）のプロンプトを組み立てる</summary>
    /// <param name="buildOutput">dotnet build のエラー出力（全文）</param>
    /// <param name="emittedFiles">これまでに提出済みのファイル相対パス一覧</param>
    internal static string BuildApiKeyFixPrompt(
        string buildOutput,
        IReadOnlyList<string> emittedFiles
    ) =>
        $@"提出されたコードで dotnet build がエラーになりました。以下のビルド出力を読み、原因のファイルを修正した完全版を emit_file で再提出してください（差分ではなく全文・同じ path で上書き）。修正の機会はこの 1 回だけです。

# ビルド出力（全文）
{DescribeOrPlaceholder(buildOutput, "(ビルド出力は取得できませんでした)")}

# これまでに提出済みのファイル
{DescribeEmittedFiles(emittedFiles)}";

    /// <summary>提出済みファイル一覧を箇条書きテキストへ整形する（無ければその旨）</summary>
    private static string DescribeEmittedFiles(IReadOnlyList<string> emittedFiles)
    {
        if (emittedFiles is not { Count: > 0 })
        {
            return "(まだありません)";
        }

        return string.Join("\n", emittedFiles.Select(path => "- " + path));
    }

    /// <summary>共有スタイルシートをプロンプト用に案内する（空なら未作成の旨）</summary>
    private static string DescribeStylesheetForPrompt(string stylesheet) =>
        string.IsNullOrWhiteSpace(stylesheet)
            ? "(共有スタイルシートはありません。デザインは各画面 HTML から読み取ってください。)"
            : stylesheet.Trim();

    /// <summary>値が空なら代替テキストを返す（プロンプトの空欄化を避ける小ヘルパ）</summary>
    private static string DescribeOrPlaceholder(string? value, string placeholder) =>
        string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();
}
