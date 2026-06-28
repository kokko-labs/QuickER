namespace QuickER.Services;

/// <summary>AI スキーマ生成と Codex チャットで共用する ER 設計ルール文を集約するクラス</summary>
/// <remarks>
/// 出力形式に依存しない設計原則（命名・PK/FK・NULL 許容・データ型）をここで一元管理し、
/// OpenAI 系のシステムプロンプトと Codex の developerInstructions の双方から組み立てる
/// </remarks>
public static class ErDesignRules
{
    /// <summary>出力形式に依存しない共通の設計原則（箇条書き）</summary>
    internal const string CommonDesignPrinciples =
        @"- 第3正規形を意識したテーブル設計を行う。
- テーブル名・カラム名は英数字とアンダースコアのみ。
- 各テーブルに説明 (description)、各カラムに説明 (description) を必ず付ける。
- 主キーは各テーブルに必ず『ちょうど 1 列』設ける。複合主キー（複数列の主キー）は禁止。多対多の中間テーブルでも単独のサロゲートキー（例: OrderItemId int）を主キーとし、親テーブルへの参照列は外部キー（主キーではない）として定義する。
- 外部キーは 1 つのリレーション（外部キー制約）につき必ず 1 列の参照で構成する。複数列の組で 1 つの参照を表す複合外部キーは禁止。
- 外部キー列を同時に主キーにしてはならない。参照元テーブルの主キーを引き継ぐ列は外部キー（非主キー・NOT NULL）にする。
- 役割が異なる複数の外部キー（例: ShippingAddressId と BillingAddressId が同じテーブルを参照）は正当であり、それぞれ別のリレーションとして定義する。
- NULL 許容は必ず設定する。主キーは NOT NULL、必須項目や通常の外部キーも NOT NULL、任意入力の項目だけ NULL 許容にする。
- データ型は SQL Server の型 (例: int, bigint, nvarchar(50), datetime2, decimal(10,2), bit) を使用する。";

    /// <summary>複合主キー禁止の 1 行ルール（ツール説明文などから単独参照する短文）</summary>
    internal const string SinglePrimaryKeyRule = "主キーは各テーブル 1 列のみ（複合主キー禁止）。";

    /// <summary>複合外部キー禁止の 1 行ルール（ツール説明文などから単独参照する短文）</summary>
    internal const string SingleColumnForeignKeyRule =
        "リレーションは 1 列対 1 列の参照のみ（複合外部キー禁止）。";

    /// <summary>識別子（テーブル名・カラム名）の命名規則の指示行を返す</summary>
    internal static string BuildNamingInstruction(AiIdentifierNamingStyle style) =>
        style switch
        {
            AiIdentifierNamingStyle.SnakeCase =>
                "- テーブル名・カラム名は必ずスネークケース (例: customer_order, customer_id) にする。",
            _ =>
                "- テーブル名・カラム名は必ずパスカルケース (例: CustomerOrder, CustomerId) にする。",
        };

    /// <summary>テーブル名の単数形・複数形の指示行を返す</summary>
    internal static string BuildTableNameNumberInstruction(AiTableNameNumberStyle style) =>
        style switch
        {
            AiTableNameNumberStyle.Plural =>
                "- テーブル名は必ず複数形 (例: Customers, Orders) にする。",
            _ => "- テーブル名は必ず単数形 (例: Customer, Order) にする。",
        };

    /// <summary>Codex スレッド開始時に渡す developerInstructions（共通設計原則＋ツール運用手順）を組み立てる</summary>
    internal static string BuildCodexDeveloperInstructions() =>
        BuildChatToolInstructions("dynamicTools");

    /// <summary>API キー接続チャット（Function/Tool 呼び出し）用の system プロンプト（共通設計原則＋ツール運用手順）を組み立てる</summary>
    internal static string BuildChatSystemPrompt() => BuildChatToolInstructions("関数ツール");

    /// <summary>ツール駆動チャット（Codex / OpenAI 共通）の指示文を組み立てる</summary>
    /// <param name="toolMechanismLabel">ツール呼び出し機構の呼称（プロンプト内での表現を切り替える）</param>
    private static string BuildChatToolInstructions(string toolMechanismLabel) =>
        $@"あなたは ER 図デザイナーアプリに組み込まれた DB 設計アシスタントです。
ER 図の作成・変更は必ず提供されたツール（{toolMechanismLabel}）で行ってください。ファイルやシェルは使用しません。

# 設計原則
{CommonDesignPrinciples}

# ツール運用手順
- 既存の ER 図に触れる前に、必ず get_diagram_summary で現在の状態を確認する。
- テーブル作成は add_entity → add_column の順で行い、最初に主キー列を 1 列だけ is_primary_key=true で追加する。{SinglePrimaryKeyRule}
- 外部キー列は add_column で is_primary_key=false（通常は is_nullable=false）として追加し、その後 add_relationship で参照を定義する。
- add_relationship では source_column（親の主キー列）と target_column（子の外部キー列）を必ず明示する。{SingleColumnForeignKeyRule}
- ツールがエラーを返した場合は、エラーメッセージの指示に従って修正し再実行する。

# 命名規則
- 既存の ER 図にテーブルがある場合は、その命名規則（パスカルケース/スネークケース）と単数形・複数形の方針に合わせる。
- 新規の ER 図ではパスカルケース・単数形（例: Customer, OrderItem）を既定とする。";
}
