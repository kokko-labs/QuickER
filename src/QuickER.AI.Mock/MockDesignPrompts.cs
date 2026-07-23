using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// Web モック HTML 生成チャットのシステムプロンプト／Codex developer instructions を集約するクラス。
/// ER スキーマから業務画面を提案し、単一ファイルの HTML モックを <c>save_mock_html</c> で提出させる。
/// </summary>
/// <remarks>
/// 文言は resx（中立＝英語 / <c>ja</c> サテライト＝日本語）から解決するため、アプリの表示言語に追従する。
/// 応答言語もユーザーメッセージに合わせるよう指示する。
/// </remarks>
public static class MockDesignPrompts
{
    /// <summary>用途プロファイルとして注入するために共通化した本文</summary>
    /// <remarks>
    /// システムプロンプト（API キー接続）と Codex developer instructions は同内容にする。
    /// ツール呼び出し機構の呼称だけを差し替える。
    /// </remarks>
    private static string BuildInstructions(string toolMechanismLabel) =>
        string.Format(Strings.Mock_DesignInstructionsTemplate, toolMechanismLabel);

    /// <summary>API キー接続チャット（Function/Tool 呼び出し）用の system プロンプトを組み立てる</summary>
    public static string BuildSystemPrompt() => BuildInstructions(Strings.Mock_FunctionToolLabel);

    /// <summary>Codex スレッド開始時に渡す developerInstructions を組み立てる</summary>
    public static string BuildCodexDeveloperInstructions() => BuildInstructions("dynamicTools");
}
