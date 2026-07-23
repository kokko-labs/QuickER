using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// モックフォルダ方式の Web モック生成チャットのシステムプロンプト／Codex developer instructions を集約するクラス。
/// ER スキーマから業務画面を提案し、画面ごとの HTML＋共有 style.css を専用ツール（save_screen /
/// save_stylesheet / get_screen / remove_screen）で「モックフォルダ」として作成させる。
/// </summary>
/// <remarks>
/// 文言は resx（中立＝英語 / <c>ja</c> サテライト＝日本語）から解決するため、アプリの表示言語に追従する。
/// 応答言語もユーザーメッセージに合わせるよう指示する。用途プロファイル本文は resx テンプレートで、
/// ツール呼び出し機構の呼称（<c>{0}</c>）だけを差し替える。
/// </remarks>
public static class MockFolderDesignPrompts
{
    /// <summary>用途プロファイルとして注入するために共通化した本文</summary>
    /// <remarks>
    /// システムプロンプト（API キー接続）と Codex developer instructions は同内容にする。
    /// ツール呼び出し機構の呼称だけを差し替える。
    /// </remarks>
    private static string BuildInstructions(string toolMechanismLabel) =>
        string.Format(Strings.Mock_FolderDesignInstructionsTemplate, toolMechanismLabel);

    /// <summary>API キー接続チャット（Function/Tool 呼び出し）用の system プロンプトを組み立てる</summary>
    public static string BuildSystemPrompt() => BuildInstructions(Strings.Mock_FunctionToolLabel);

    /// <summary>Codex スレッド開始時に渡す developerInstructions を組み立てる</summary>
    public static string BuildCodexDeveloperInstructions() => BuildInstructions("dynamicTools");
}
