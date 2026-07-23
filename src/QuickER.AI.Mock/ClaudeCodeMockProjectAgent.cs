using QuickER.AI;

namespace QuickER.AI.Mock;

/// <summary>
/// Claude Code CLI をヘッドレス実行して WPF の UI 層を書かせる <see cref="IMockProjectAgent"/> の実装。
/// </summary>
/// <remarks>
/// <para>
/// データ層（Entity/EditModel/Mapper/InMemory 等）はスキャフォールドが決定的に生成済みで、AI には書かせない。
/// AI には design/mock/ のモックフォルダをデザイン仕様として、README-QuickER.md の規約に従い
/// CommunityToolkit.Mvvm の MVVM で UI 層を作らせる。MCP サーバー（ER ツール）は使わせず、
/// ファイル編集・コマンド実行のみを許可する。
/// </para>
/// <para>
/// 進捗テキストは <c>onProgress</c> で逐次転送する。全体タイムアウト・成果物検証・独立ビルド・ログ保全は
/// 呼び出し側の <see cref="MockProjectAgentRunner"/> が担う。キャンセルは <see cref="OperationCanceledException"/>
/// をそのまま伝播させ、タイムアウトと中断の区別は呼び出し側へ委ねる。
/// </para>
/// </remarks>
public sealed class ClaudeCodeMockProjectAgent : IMockProjectAgent
{
    /// <summary>ヘッドレスで許可する追加ツール（ファイル編集・コマンド実行）</summary>
    private static readonly string[] HeadlessAllowedTools = ["Edit", "Write", "MultiEdit", "Bash"];

    private readonly IClaudeCodeClient _client;

    /// <summary>Claude Code クライアントを注入して生成する</summary>
    /// <param name="client">Claude Code クライアント</param>
    public ClaudeCodeMockProjectAgent(IClaudeCodeClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public bool IsAvailable() => _client.IsAvailable();

    /// <inheritdoc />
    public async Task<MockProjectAgentOutcome> RunAsync(
        MockProjectAgentRequest request,
        Action<string> onProgress,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onProgress);

        var options = BuildLaunchOptions(
            request.Model,
            request.WorkingDirectory,
            request.ProjectName
        );
        var prompt = MockProjectPromptBuilder.BuildPrompt(
            request.ProjectName,
            request.AdditionalInstructions
        );

        // キャンセル（タイムアウト・中断）は OperationCanceledException として呼び出し側へ伝播させる
        var outcome = await _client
            .RunTurnAsync(prompt, resumeSessionId: null, options, onProgress, cancellationToken)
            .ConfigureAwait(false);

        return new MockProjectAgentOutcome(outcome.Success, outcome.Error, outcome.NotLoggedIn);
    }

    /// <inheritdoc />
    public Task InterruptAsync()
    {
        _client.Interrupt();
        return Task.CompletedTask;
    }

    /// <summary>ヘッドレス（ファイル編集・コマンド実行許可）の起動オプションを組み立てる</summary>
    /// <remarks>
    /// MCP は使わない（ER ツール不要）。<c>--permission-mode acceptEdits</c> と Edit/Write/Bash の許可で、
    /// 作業フォルダ内のファイル作成・編集と dotnet build 等のコマンド実行をヘッドレスで通す。
    /// </remarks>
    internal static ClaudeCodeLaunchOptions BuildLaunchOptions(
        string model,
        string workingDirectory,
        string projectName
    ) =>
        new(
            Model: model ?? string.Empty,
            SystemPrompt: MockProjectPromptBuilder.BuildSystemPrompt(projectName),
            McpConfigPath: string.Empty,
            AllowedTool: string.Empty,
            WorkingDirectory: workingDirectory
        )
        {
            PermissionMode = "acceptEdits",
            AdditionalAllowedTools = HeadlessAllowedTools,
        };
}
