namespace QuickER.AI.Mock;

/// <summary>
/// WPF モックプロジェクト生成（第2ステップ）のシステムプロンプト・初回プロンプトを組み立てる共有ヘルパ。
/// </summary>
/// <remarks>
/// バックエンド（Claude Code / Codex / Copilot）に依らずプロンプト本文は同一である必要があるため、
/// <see cref="ClaudeCodeMockProjectAgent"/> ・ <see cref="CodexMockProjectAgent"/> ・
/// <see cref="CopilotMockProjectAgent"/> のいずれもここを参照する（プロンプト本文の重複コピーを避ける正本）。
/// 各バックエンドは、ここで得たシステムプロンプトをそれぞれの流儀（Claude Code＝<c>--append-system-prompt</c>／
/// Codex＝developer instructions／Copilot＝システムメッセージへの追記）で渡す。
/// 本文はすべて英語固定（ヘッドレス実行の機械向け指示は UI 言語に追従させない＝回答言語が意図せず
/// 引きずられるのを避ける方針。CJK 混入は英語ガードテストが検知する）。
/// </remarks>
internal static class MockProjectPromptBuilder
{
    /// <summary>デザイン仕様のモックフォルダの相対パス（スキャフォールドが同梱する）</summary>
    public const string DesignFolderRelativePath = "design/mock";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    /// <summary>追加指示を連結するときの見出し（英語固定＝機械向け指示のため UI 言語に追従させない）</summary>
    internal const string AdditionalInstructionsHeading = "# Additional instructions";

    /// <summary>
    /// エージェント型バックエンド保険用の自動続行ナッジ（承認待ちで止まったターンを 1 回だけ後押しする固定文）。
    /// </summary>
    /// <remarks>
    /// 計画提示だけで（承認待ちのまま）ターンを終えたと疑われるとき、<see cref="CodexMockProjectAgent"/> と
    /// <see cref="CopilotMockProjectAgent"/> が同一セッションへ 1 回だけ送る続行指示。
    /// </remarks>
    internal const string ContinuationNudge =
        "No confirmation is needed. Do not ask for approval: continue on your own and finish the work, from the implementation through verification with dotnet build.";

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
        $@"You are an expert {profile.Target.DisplayName} engineer, and you implement the GUI (the UI layer) of an existing project.
This folder uses the standard Visual Studio layout: the solution {projectName}.sln sits at the root and the project itself lives under the {projectName}/ folder, where QuickER has already generated {profile.SystemScaffoldNoun}.

This is a non-interactive, headless run. There is no user to answer you. Never present a plan, ask a clarifying question, or wait for approval: complete everything within this turn, from the implementation through verification with `dotnet build`.

# Rules you must follow
- Before you start working, read {projectName}/{ReadmeFileName} and follow the rules it states.
- {projectName}/{DesignFolderRelativePath}/ is the mock folder that holds the design specification. Start by reading {projectName}/{DesignFolderRelativePath}/mock.json to learn the screen list (screens) and the screen transitions (transitions), then review every screen's *.html (one file is the design specification of one screen) and the shared design system style.css. {profile.SystemScreenReproductionRule}
- Everything under {projectName}/Generated/ (the auto-generated data-layer code) is read-only. Never edit or delete it. From the UI, use I{{Entity}}Repository through dependency injection.
{profile.SystemUiFrameworkRules}
- Implement every screen's data display, insert, update and delete with the generated code under {projectName}/Generated/ (Entity / EditModel / Mapper / I{{Entity}}Repository). You must not substitute your own data classes or a hard-coded list inside a ViewModel (read lists and details from the repository, and save inserts, updates and deletes through the repository).
- Register the dependencies at startup with AddGeneratedInMemoryRepositories() (it seeds sample data), so no real database connection is needed.
- When inserting a new record, always assign the primary key in the application (QuickER repositories do not rely on database-generated keys; if the key is left unassigned, EditModel validation or saving fails). When the primary key is a value object (GuidKey), the parameterless Create() generates a new key. For a numeric key, assign for example the largest existing value plus one.
- Do not add or modify NuGet.Config or any other package source configuration file (packages are restored with the settings already present in the csproj).

# How to proceed
{profile.SystemWorkflowSteps(projectName)}
- Once the implementation is in place, run `dotnet build` in this folder (the solution root) and keep fixing until it succeeds with no errors and no warnings.
- Finally, report that you confirmed the build succeeded with no errors and no warnings.";

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
            $@"Implement the {profile.UiLayerName} of the project '{projectName}'.

This folder uses the standard Visual Studio layout: the solution {projectName}.sln sits at the root and the project itself lives under the {projectName}/ folder. Add the UI-layer sources under the {projectName}/ folder (the same place as the csproj).

Steps:
1. First read {projectName}/{ReadmeFileName} to learn the project layout and the rules.
2. Read {projectName}/{DesignFolderRelativePath}/mock.json to learn the screen list (screens) and the screen transitions (transitions), then read every screen's *.html and the shared style.css to learn the screen structure, the fields and the transitions you must reproduce.
3. Review the data layer under {projectName}/Generated/ (Entity / I{{Entity}}Repository / AddGeneratedInMemoryRepositories and so on) and use it from the UI.
4. {profile.PromptImplementStep}Use AddGeneratedInMemoryRepositories() for dependency injection.
5. Run `dotnet build` in this folder (the solution root) and fix your own code until it succeeds with no errors and no warnings.
6. Confirm that the build succeeded and report it.

Completion criteria (all of them must hold):
- {profile.PromptViewCriterion}
- Every data operation for lists, details and insert/edit goes through I{{Entity}}Repository (no substitution with your own data classes or hard-coded lists).
- A new record (insert) can actually be saved, including the assignment of its primary key.
- The transitions declared in mock.json work as screen navigation.
- `dotnet build` succeeds with no errors and no warnings.

Everything under {projectName}/Generated/ is read-only. Do not edit it.

Do not ask for confirmation or approval: finish the whole task from these instructions alone.";

        // 追加指示があれば末尾へ「# Additional instructions」として連結する（見出しも英語固定）
        if (!string.IsNullOrWhiteSpace(additionalInstructions))
        {
            prompt += "\n\n" + AdditionalInstructionsHeading + "\n" + additionalInstructions.Trim();
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
        $@"You are an expert {profile.Target.DisplayName} engineer, and you implement the UI layer (the screens) of an existing project.
QuickER has already generated the data layer (Entity / EditModel / Mapper / I{{Entity}}Repository / the in-memory implementation and so on) under {projectName}/{MockProjectScaffoldService.GeneratedFolderName}/, and you write only the UI layer that consumes it through dependency injection.

# Implementation rules
{profile.ApiKeyUiFrameworkRules}
- Register the dependencies at startup with AddGeneratedInMemoryRepositories() (it seeds sample data), so no real database connection is needed.
- Receive I{{Entity}}Repository through dependency injection instead of newing up a concrete implementation directly. Every screen's data display, insert, update and delete must go through the repository; you must not substitute your own data classes or a hard-coded list inside a ViewModel.
- When inserting a new record, always assign the primary key in the application (QuickER repositories do not rely on database-generated keys; if the key is left unassigned, EditModel validation or saving fails). When the primary key is a value object (GuidKey), the parameterless Create() generates a new key. For a numeric key, assign for example the largest existing value plus one.
- Do not submit NuGet.Config or any other package source configuration file (such a submission is rejected).
- {profile.ApiKeyScreenReproductionRule}
- Do not change anything under {projectName}/{MockProjectScaffoldService.GeneratedFolderName}/ (the data layer), anything under design/ (the design specification), {ReadmeFileName}, or the .sln/.csproj files (such a submission is rejected). You only add and update UI-layer sources.

# How to submit files (important)
- The emit_file tool is the only way to submit an implementation file. Code written in the chat body has no effect.
- Pass the complete content of the file to emit_file (the whole file, not a diff). Submitting the same path again overwrites it.
- path is a relative path under the output project (for example {profile.ApiKeyEmitPathExamples(projectName)}).
- You cannot read files or run dotnet build. Using only the information you are given, submit complete files that compile (never guess at a member whose existence you cannot confirm).
- In every turn, submit the whole set of files requested for that turn, using emit_file within that same turn.";

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
        $@"Implement the {profile.UiLayerName} of the project '{projectName}', starting with the shared parts.

In this turn, submit the following with emit_file (UI-layer sources go under the {projectName}/ folder):
{profile.ApiKeyCommonEmitInstructions(projectName)}

# Data layer (summary of the contracts you consume through dependency injection)
{generatedSummary}

# Screen list (mock.json) and transitions
{screensOverview}

# Shared design system (style.css)
{DescribeStylesheetForPrompt(stylesheet)}

# Source ER schema
{DescribeOrPlaceholder(schema, "(no schema information is available)")}";

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
        $@"Implement the screen '{DescribeOrPlaceholder(screen.Name, screen.File)}' ({screen.File}).

{profile.ApiKeyScreenInstruction(projectName)}

# Role of this screen
{DescribeOrPlaceholder(screen.Description, "(no description is available)")}

# Design specification ({screen.File})
{DescribeOrPlaceholder(screenHtml, "(the HTML of this screen could not be read; implement it from the screen name, its role and its transitions)")}

# Transitions (from this screen)
{DescribeOrPlaceholder(transitions, "(there is no transition from this screen)")}

# Files submitted so far (refer to them so that naming and navigation wiring stay consistent)
{DescribeEmittedFiles(emittedFiles)}";

    /// <summary>ビルド失敗時の修正リクエスト（固定 1 回）のプロンプトを組み立てる</summary>
    /// <param name="buildOutput">dotnet build のエラー出力（全文）</param>
    /// <param name="emittedFiles">これまでに提出済みのファイル相対パス一覧</param>
    internal static string BuildApiKeyFixPrompt(
        string buildOutput,
        IReadOnlyList<string> emittedFiles
    ) =>
        $@"dotnet build failed on the code you submitted. Read the build output below, then resubmit the complete corrected version of the offending files with emit_file (the whole file, not a diff, under the same path so that it overwrites). This is your only chance to correct them.

# Build output (full)
{DescribeOrPlaceholder(buildOutput, "(the build output could not be read)")}

# Files submitted so far
{DescribeEmittedFiles(emittedFiles)}";

    /// <summary>提出済みファイル一覧を箇条書きテキストへ整形する（無ければその旨）</summary>
    private static string DescribeEmittedFiles(IReadOnlyList<string> emittedFiles)
    {
        if (emittedFiles is not { Count: > 0 })
        {
            return "(none yet)";
        }

        return string.Join("\n", emittedFiles.Select(path => "- " + path));
    }

    /// <summary>共有スタイルシートをプロンプト用に案内する（空なら未作成の旨）</summary>
    private static string DescribeStylesheetForPrompt(string stylesheet) =>
        string.IsNullOrWhiteSpace(stylesheet)
            ? "(there is no shared stylesheet; read the design from each screen's HTML)"
            : stylesheet.Trim();

    /// <summary>値が空なら代替テキストを返す（プロンプトの空欄化を避ける小ヘルパ）</summary>
    private static string DescribeOrPlaceholder(string? value, string placeholder) =>
        string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();
}
