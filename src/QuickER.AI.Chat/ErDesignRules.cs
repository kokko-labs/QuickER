using QuickER.AI;
using QuickER.AI.Chat.Resources;

namespace QuickER.AI.Chat;

/// <summary>AI スキーマ生成と Codex チャットで共用する ER 設計ルール文を集約するクラス</summary>
/// <remarks>
/// 出力形式に依存しない設計原則（命名・PK/FK・NULL 許容・データ型）をここで一元管理し、
/// OpenAI 系のシステムプロンプトと Codex の developerInstructions の双方から組み立てる。
/// 文言はすべて resx（中立＝英語 / <c>ja</c> サテライト＝日本語）から解決するため、アプリの表示言語に追従する。
/// </remarks>
public static class ErDesignRules
{
    /// <summary>出力形式に依存しない共通の設計原則（箇条書き）</summary>
    internal static string CommonDesignPrinciples => Strings.ErDesign_CommonDesignPrinciples;

    /// <summary>複合主キー禁止の 1 行ルール（ツール説明文などから単独参照する短文）</summary>
    internal static string SinglePrimaryKeyRule => Strings.ErDesign_SinglePrimaryKeyRule;

    /// <summary>複合外部キー禁止の 1 行ルール（ツール説明文などから単独参照する短文）</summary>
    internal static string SingleColumnForeignKeyRule =>
        Strings.ErDesign_SingleColumnForeignKeyRule;

    /// <summary>識別子（テーブル名・カラム名）の命名規則の指示行を返す</summary>
    internal static string BuildNamingInstruction(AiIdentifierNamingStyle style) =>
        style switch
        {
            AiIdentifierNamingStyle.SnakeCase => Strings.ErDesign_NamingSnakeCase,
            _ => Strings.ErDesign_NamingPascalCase,
        };

    /// <summary>テーブル名の単数形・複数形の指示行を返す</summary>
    internal static string BuildTableNameNumberInstruction(AiTableNameNumberStyle style) =>
        style switch
        {
            AiTableNameNumberStyle.Plural => Strings.ErDesign_TableNamePlural,
            _ => Strings.ErDesign_TableNameSingular,
        };

    /// <summary>Codex スレッド開始時に渡す developerInstructions（共通設計原則＋ツール運用手順）を組み立てる</summary>
    internal static string BuildCodexDeveloperInstructions() =>
        BuildChatToolInstructions("dynamicTools");

    /// <summary>API キー接続チャット（Function/Tool 呼び出し）用の system プロンプト（共通設計原則＋ツール運用手順）を組み立てる</summary>
    internal static string BuildChatSystemPrompt() =>
        BuildChatToolInstructions(Strings.ErDesign_FunctionToolLabel);

    /// <summary>ツール駆動チャット（Codex / OpenAI 共通）の指示文を組み立てる</summary>
    /// <param name="toolMechanismLabel">ツール呼び出し機構の呼称（プロンプト内での表現を切り替える）</param>
    private static string BuildChatToolInstructions(string toolMechanismLabel) =>
        string.Format(
            Strings.ErDesign_ChatInstructionsTemplate,
            toolMechanismLabel,
            CommonDesignPrinciples,
            SinglePrimaryKeyRule,
            SingleColumnForeignKeyRule
        );
}
