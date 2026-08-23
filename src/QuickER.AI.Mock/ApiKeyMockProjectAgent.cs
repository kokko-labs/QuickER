using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuickER.AI;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// API キー方式（OpenAI / Claude / ローカル LLM＝<see cref="ChatTurnEngine"/> 系）で WPF の UI 層を書かせる
/// <see cref="IMockProjectAgent"/> の実装。
/// </summary>
/// <remarks>
/// <para>
/// エージェント型（Claude Code / Codex）と異なり、探索や自己修正ループはしない。<b>固定パイプライン</b>で
/// 「共通部（App / MainWindow / ナビゲーション骨格 / DI）→ 各画面（1 画面 1 リクエスト）」を決定的に進め、
/// 実装ファイルは <c>emit_file</c> ツール（<see cref="MockProjectEmitTools"/>）で丸ごと提出させる
/// （読み取り・ビルド実行のツールは与えない＝探索させない）。一貫性は「既提出ファイルの相対パス一覧」を
/// 次リクエストへ引き継いで確保する。
/// </para>
/// <para>
/// パイプライン完了後に <see cref="IBuildRunner"/> で中間ビルドし、失敗ならエラー全文を添えて修正版の
/// 再提出を <b>1 回だけ</b>求める。修正ターン後に実行器内で再ビルドし、その成否を <see cref="MockProjectAgentOutcome"/> へ
/// 反映する（Outcome を自己申告として正直にするため。最終判定は共有オーケストレーターの独立ビルド）。
/// </para>
/// <para>
/// 本エージェント自身が <see cref="IErDiagramToolHost"/> として <c>emit_file</c> を処理する（ツールホスト）。
/// エンジンはコンストラクタ注入のファクトリで生成し、モデル・キー・エンドポイントはファクトリの閉包に閉じ込める。
/// </para>
/// </remarks>
public sealed class ApiKeyMockProjectAgent : IMockProjectAgent, IErDiagramToolHost
{
    /// <summary>MCP サーバー名相当（ツール名の名前空間・API キー方式では表示以外に影響しない）</summary>
    private const string ProfileServerName = "erdesigner_mockproject";

    /// <summary>生成された契約要約の最大文字数（トークン浪費を避けるための上限）</summary>
    private const int GeneratedSummaryMaxChars = 8000;

    private readonly Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> _engineFactory;
    private readonly IBuildRunner _buildRunner;

    // ── 1 実行分の状態（RunAsync の間だけ有効） ──

    /// <summary>出力フォルダ（相対パスの基点＝ソリューション直下）</summary>
    private string _workingDirectory = string.Empty;

    /// <summary>生成ターゲットのプロファイル（emit_file の許可拡張子の宣言元）</summary>
    private MockProjectTargetProfile? _targetProfile;

    /// <summary>進捗（emit のパス等）の転送先</summary>
    private Action<string>? _onProgress;

    /// <summary>これまでに提出済みのファイル相対パス（提出順・重複はしない）</summary>
    private readonly List<string> _emittedFiles = new();

    /// <summary>現在のターンで emit_file が受理された回数（0 なら emit なしターン）</summary>
    private int _emitCountThisTurn;

    /// <summary>実行中のエンジン（中断で参照する）</summary>
    private IErChatEngine? _engine;

    /// <summary>直近ターンの完了結果（ターン失敗検出用）</summary>
    private ErChatTurnResult _lastTurnResult = new(true, null);

    /// <summary>エンジンファクトリとビルド検証器を注入して生成する</summary>
    /// <param name="engineFactory">プロファイル・ツールホストを受け取り API キーエンジンを生成するファクトリ（VM の apiKeyEngineFactory と同型）</param>
    /// <param name="buildRunner">中間ビルド検証器</param>
    public ApiKeyMockProjectAgent(
        Func<ErChatProfile, IErDiagramToolHost, IErChatEngine> engineFactory,
        IBuildRunner buildRunner
    )
    {
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _buildRunner = buildRunner ?? throw new ArgumentNullException(nameof(buildRunner));
    }

    /// <inheritdoc />
    /// <remarks>API キーの ready 判定は VM 側（IsBackendReady）が担うため、ここでは常に true を返す。</remarks>
    public bool IsAvailable() => true;

    /// <inheritdoc />
    public async Task<MockProjectAgentOutcome> RunAsync(
        MockProjectAgentRequest request,
        Action<string> onProgress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onProgress);

        _workingDirectory = request.WorkingDirectory;
        _onProgress = onProgress;
        _emittedFiles.Clear();
        _lastTurnResult = new ErChatTurnResult(true, null);

        // スキャフォールド済みフォルダから素材（README/mock.json/画面 HTML/style.css/契約要約）を読む
        var materials = ReadMaterials(request);

        // 生成ターゲットのプロファイル（プロンプト文面のターゲット差分と emit_file の許可拡張子の正本）
        var targetProfile = request.Profile;
        _targetProfile = targetProfile;

        var profile = new ErChatProfile(
            () =>
                MockProjectPromptBuilder.BuildApiKeySystemPrompt(
                    targetProfile,
                    request.ProjectName
                ),
            () =>
                MockProjectPromptBuilder.BuildApiKeySystemPrompt(
                    targetProfile,
                    request.ProjectName
                ),
            MockProjectEmitTools.GetDefinitions(),
            ProfileServerName
        );

        // ツールホストは本エージェント自身（emit_file を処理する）
        var engine = _engineFactory(profile, this);
        _engine = engine;
        engine.AssistantDeltaReceived += OnAssistantDelta;
        engine.TurnCompleted += OnTurnCompleted;

        try
        {
            await engine.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await engine.StartConversationAsync(cancellationToken).ConfigureAwait(false);

            var total = 1 + materials.Screens.Count;

            // リクエスト 1: 共通部（App / MainWindow / ナビゲーション骨格 / DI）
            EmitLine(string.Format(Strings.Mock_ApiRun_CommonPart, 1, total));
            var commonFailure = await SendTurnAsync(
                    MockProjectPromptBuilder.BuildApiKeyCommonPrompt(
                        targetProfile,
                        request.ProjectName,
                        materials.Schema,
                        materials.ScreensOverview,
                        materials.Stylesheet,
                        materials.GeneratedSummary
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (commonFailure is not null)
            {
                return commonFailure;
            }

            // リクエスト 2〜N: 各画面（1 画面 1 リクエスト）
            var index = 1;

            foreach (var screen in materials.Screens)
            {
                index++;
                EmitLine(
                    string.Format(
                        Strings.Mock_ApiRun_Screen,
                        index,
                        total,
                        string.IsNullOrWhiteSpace(screen.Name) ? screen.File : screen.Name
                    )
                );

                var screenHtml = ReadScreenHtml(materials.DesignFolder, screen.File);
                var transitions = DescribeTransitionsFrom(materials.Transitions, screen.File);

                var screenFailure = await SendTurnAsync(
                        MockProjectPromptBuilder.BuildApiKeyScreenPrompt(
                            targetProfile,
                            request.ProjectName,
                            screen,
                            screenHtml,
                            transitions,
                            _emittedFiles.ToArray()
                        ),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (screenFailure is not null)
                {
                    return screenFailure;
                }
            }

            // 中間ビルド（IBuildRunner）。成功→成功、失敗→修正ターン 1 回だけ→再ビルドして成否を反映
            EmitLine(Strings.Mock_ApiRun_Build);
            var build = await _buildRunner
                .BuildAsync(_workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (build.Success)
            {
                EmitLine(Strings.Mock_ApiRun_BuildOk);
                return new MockProjectAgentOutcome(true, null, false);
            }

            // 修正ターン（固定 1 回）: ビルドエラー全文を添えて修正版の再提出を求める
            EmitLine(Strings.Mock_ApiRun_BuildFailedFixing);
            var fixFailure = await SendTurnAsync(
                    MockProjectPromptBuilder.BuildApiKeyFixPrompt(
                        build.Output,
                        _emittedFiles.ToArray()
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (fixFailure is not null)
            {
                return fixFailure;
            }

            // 修正ターン後に再ビルドし、その成否を Outcome へ反映する（自己申告を正直にする）
            EmitLine(Strings.Mock_ApiRun_Rebuild);
            var rebuild = await _buildRunner
                .BuildAsync(_workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (rebuild.Success)
            {
                EmitLine(Strings.Mock_ApiRun_BuildOk);
                return new MockProjectAgentOutcome(true, null, false);
            }

            EmitLine(Strings.Mock_ApiRun_BuildFail);
            return new MockProjectAgentOutcome(false, Strings.Mock_ApiRun_ErrorBuildFailed, false);
        }
        finally
        {
            engine.AssistantDeltaReceived -= OnAssistantDelta;
            engine.TurnCompleted -= OnTurnCompleted;
            _engine = null;
            _targetProfile = null;

            // エンジン（＝1 実行分）は使い捨てにする
            await engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task InterruptAsync() => _engine?.InterruptAsync() ?? Task.CompletedTask;

    /// <summary>
    /// 1 ターンを送信し、キャンセル・ターン失敗を検査する。失敗なら失敗 Outcome を、続行可能なら null を返す。
    /// emit なしターンはログのみで続行する（最終判定は中間ビルドに委ねる）。
    /// </summary>
    private async Task<MockProjectAgentOutcome?> SendTurnAsync(
        string prompt,
        CancellationToken cancellationToken
    )
    {
        _emitCountThisTurn = 0;
        _lastTurnResult = new ErChatTurnResult(true, null);

        // キャンセル（タイムアウト・中断）は OperationCanceledException として呼び出し側へ伝播させる。
        // ChatTurnEngine は OCE を内部で握って TurnCompleted(false) に変えるため、送信後にトークンを検査する。
        await _engine!
            .SendAsync(prompt, Array.Empty<ChatAttachment>(), cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // ターンがエンジン側のエラー（API 失敗等）で失敗したら、そこで打ち切る
        if (!_lastTurnResult.Success && !string.IsNullOrWhiteSpace(_lastTurnResult.Error))
        {
            return new MockProjectAgentOutcome(
                false,
                string.Format(Strings.Mock_ApiRun_ErrorTurnFailedFormat, _lastTurnResult.Error),
                false
            );
        }

        // このターンで 1 度も emit されなかった場合はログのみ（失敗扱いにせず次へ進む）
        if (_emitCountThisTurn == 0)
        {
            EmitLine(Strings.Mock_ApiRun_NoEmit);
        }

        return null;
    }

    /// <summary>アシスタントのストリーミング断片を進捗として転送する</summary>
    private void OnAssistantDelta(object? sender, string delta) => _onProgress?.Invoke(delta);

    /// <summary>ターン完了を記録する（ターン失敗検出用）</summary>
    private void OnTurnCompleted(object? sender, ErChatTurnResult result) =>
        _lastTurnResult = result;

    /// <summary>emit_file ツールを処理する（本エージェントがツールホスト）</summary>
    public (string Result, bool Success) Execute(string toolName, string argumentsJson)
    {
        if (
            !string.Equals(
                toolName,
                MockProjectEmitTools.EmitFileToolName,
                StringComparison.Ordinal
            )
        )
        {
            return ($"Unknown tool: {toolName}", false);
        }

        string path;
        string content;

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
            );
            var root = document.RootElement;
            path = GetString(root, "path");
            content = GetString(root, "content");
        }
        catch (JsonException ex)
        {
            return ($"Could not parse the tool arguments (JSON): {ex.Message}", false);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return ("content is empty. Submit the complete file content.", false);
        }

        if (_targetProfile is null)
        {
            return ("emit_file is only available while a generation run is in progress.", false);
        }

        var resolved = MockProjectEmitTools.ResolveEmitPath(
            _workingDirectory,
            _targetProfile,
            path
        );

        if (!resolved.Ok)
        {
            // 拒否は失敗結果として返し、AI に別パスでの再提出を促す（探索はさせない）
            EmitLine(string.Format(Strings.Mock_ApiRun_EmitRejectedFormat, resolved.Error));
            return (resolved.Error, false);
        }

        try
        {
            var directory = Path.GetDirectoryName(resolved.FullPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                resolved.FullPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ($"Failed to write the file: {ex.Message}", false);
        }

        _emitCountThisTurn++;

        // 相対パスを提出済み一覧へ upsert（同一 path の再提出は重複させない）
        if (!_emittedFiles.Contains(resolved.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            _emittedFiles.Add(resolved.RelativePath);
        }

        EmitLine(string.Format(Strings.Mock_ApiRun_Emit, resolved.RelativePath));

        return ($"Saved '{resolved.RelativePath}'.", true);
    }

    /// <summary>進捗として 1 行（改行付き）を転送する</summary>
    private void EmitLine(string line) => _onProgress?.Invoke(line + "\n");

    // ── 素材の読み取り ──

    /// <summary>スキャフォールド済みフォルダから読み取った素材一式</summary>
    private sealed record Materials(
        string DesignFolder,
        string Schema,
        string Stylesheet,
        string ScreensOverview,
        string GeneratedSummary,
        IReadOnlyList<MockScreen> Screens,
        IReadOnlyList<MockTransition> Transitions
    );

    /// <summary>スキャフォールド済みフォルダから素材（mock.json・style.css・契約要約）を読む</summary>
    private static Materials ReadMaterials(MockProjectAgentRequest request)
    {
        var projectDir = Path.Combine(request.WorkingDirectory, request.ProjectName);
        var designFolder = Path.Combine(
            projectDir,
            MockProjectScaffoldService.DesignFolderRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar
            )
        );
        var generatedDir = Path.Combine(projectDir, MockProjectScaffoldService.GeneratedFolderName);

        var manifest = ReadManifest(designFolder);
        var stylesheet = ReadTextOrEmpty(
            Path.Combine(designFolder, MockManifest.StylesheetFileName)
        );

        return new Materials(
            DesignFolder: designFolder,
            Schema: manifest.SourceSchema ?? string.Empty,
            Stylesheet: stylesheet,
            ScreensOverview: DescribeScreensOverview(manifest),
            GeneratedSummary: SummarizeGeneratedContracts(generatedDir),
            Screens: manifest.Screens ?? new List<MockScreen>(),
            Transitions: manifest.Transitions ?? new List<MockTransition>()
        );
    }

    /// <summary>design/mock/mock.json を読む（不在・破損は空マニフェストにフォールバック）</summary>
    private static MockManifest ReadManifest(string designFolder)
    {
        var manifestPath = Path.Combine(designFolder, MockManifest.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return new MockManifest();
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<MockManifest>(
                json,
                MockManifest.SerializerOptions
            );

            if (manifest is null)
            {
                return new MockManifest();
            }

            manifest.Screens ??= new List<MockScreen>();
            manifest.Transitions ??= new List<MockTransition>();

            return manifest;
        }
        catch (JsonException)
        {
            return new MockManifest();
        }
    }

    /// <summary>画面ファイルの HTML を読む（不在は空文字）</summary>
    private static string ReadScreenHtml(string designFolder, string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return string.Empty;
        }

        return ReadTextOrEmpty(Path.Combine(designFolder, file));
    }

    /// <summary>ファイルを読む（不在・失敗は空文字）</summary>
    private static string ReadTextOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>mock.json の画面一覧と遷移を、共通部プロンプト用の概要テキストへ整形する</summary>
    private static string DescribeScreensOverview(MockManifest manifest)
    {
        var builder = new StringBuilder();

        var screens = manifest.Screens ?? new List<MockScreen>();

        if (screens.Count == 0)
        {
            builder.AppendLine("(no screens yet)");
        }
        else
        {
            foreach (var screen in screens)
            {
                builder.Append("- ").Append(screen.File);

                if (!string.IsNullOrWhiteSpace(screen.Name))
                {
                    builder.Append(" : ").Append(screen.Name);
                }

                if (!string.IsNullOrWhiteSpace(screen.Description))
                {
                    builder.Append(" — ").Append(screen.Description);
                }

                builder.AppendLine();
            }
        }

        var transitions = manifest.Transitions ?? new List<MockTransition>();

        if (transitions.Count > 0)
        {
            builder.AppendLine("Transitions:");

            foreach (var transition in transitions)
            {
                builder.Append("- ").Append(transition.From).Append(" -> ").Append(transition.To);

                if (!string.IsNullOrWhiteSpace(transition.Trigger))
                {
                    builder.Append(" (").Append(transition.Trigger).Append(')');
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>指定画面を起点とする遷移を、画面プロンプト用の箇条書きへ整形する</summary>
    private static string DescribeTransitionsFrom(
        IReadOnlyList<MockTransition> transitions,
        string file
    )
    {
        var relevant = transitions
            .Where(t => string.Equals(t.From, file, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevant.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var transition in relevant)
        {
            builder.Append("- ").Append(transition.To);

            if (!string.IsNullOrWhiteSpace(transition.Trigger))
            {
                builder.Append(" (").Append(transition.Trigger).Append(')');
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    // ── データ層（Generated/）の契約要約 ──

    /// <summary>public 型宣言（interface/class/record/enum/struct）を拾う正規表現</summary>
    private static readonly Regex PublicTypeRegex = new(
        @"\bpublic\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(interface|class|record|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// <c>Generated/</c> 配下のデータ層コードを、トークンを浪費しない範囲で要約する
    /// （ファイル名一覧＋公開型名＋リポジトリ契約 <c>I{Entity}Repository</c> のメンバーシグネチャ）。
    /// </summary>
    /// <remarks>詳細な規約は README に書かれている前提で、UI から使う「名前とシグネチャ」だけを渡す。</remarks>
    private static string SummarizeGeneratedContracts(string generatedDir)
    {
        if (!Directory.Exists(generatedDir))
        {
            return "(the data layer could not be found)";
        }

        var files = Directory
            .EnumerateFiles(generatedDir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return "(no data-layer files could be found)";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Generated files:");

        foreach (var file in files)
        {
            builder
                .Append("- ")
                .AppendLine(Path.GetRelativePath(generatedDir, file).Replace('\\', '/'));
        }

        var publicTypes = new List<string>();
        var repositoryContracts = new List<string>();

        foreach (var file in files)
        {
            string text;

            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match match in PublicTypeRegex.Matches(text))
            {
                var name = match.Groups[2].Value;

                if (!publicTypes.Contains(name))
                {
                    publicTypes.Add(name);
                }

                // I{Entity}Repository はメンバーシグネチャも抽出して DI 利用の助けにする
                if (
                    match.Groups[1].Value == "interface"
                    && name.StartsWith('I')
                    && name.EndsWith("Repository", StringComparison.Ordinal)
                )
                {
                    var members = ExtractInterfaceMemberSignatures(
                        text,
                        match.Index + match.Length
                    );

                    if (members.Count > 0)
                    {
                        repositoryContracts.Add(
                            $"{name}\n{string.Join("\n", members.Select(m => "  " + m))}"
                        );
                    }
                }
            }
        }

        if (publicTypes.Count > 0)
        {
            builder.AppendLine();
            builder.Append("Public types: ").AppendLine(string.Join(", ", publicTypes));
        }

        if (repositoryContracts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Repository contracts (consumed through dependency injection):");
            builder.AppendLine(string.Join("\n", repositoryContracts));
        }

        var summary = builder.ToString().TrimEnd();

        // 上限を超える場合は切り詰める（トークン浪費防止）
        if (summary.Length > GeneratedSummaryMaxChars)
        {
            summary =
                summary[..GeneratedSummaryMaxChars]
                + "\n... (the summary was truncated at its size limit; see the files under Generated/ for the details)";
        }

        return summary;
    }

    /// <summary>インターフェイス宣言直後の <c>{ }</c> ブロックから、メンバーシグネチャ行を抽出する</summary>
    private static IReadOnlyList<string> ExtractInterfaceMemberSignatures(
        string text,
        int declEndIndex
    )
    {
        var open = text.IndexOf('{', declEndIndex);

        if (open < 0)
        {
            return Array.Empty<string>();
        }

        // 波括弧の対応を数えてブロック終端を探す
        var depth = 0;
        var end = -1;

        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end < 0)
        {
            return Array.Empty<string>();
        }

        var body = text[(open + 1)..end];
        var members = new List<string>();

        // 「;」で終わるシグネチャ行のみを拾う（XML doc コメント・属性は除外）
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();

            if (
                line.Length == 0
                || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal)
                || line.StartsWith('*')
                || line.StartsWith('[')
            )
            {
                continue;
            }

            if (line.EndsWith(';') && line.Contains('('))
            {
                members.Add(line);
            }
        }

        return members;
    }

    /// <summary>JSON オブジェクトから文字列プロパティを取り出す（未設定・非文字列は空文字）</summary>
    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
