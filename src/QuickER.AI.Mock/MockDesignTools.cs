using QuickER.Mcp;

namespace QuickER.AI.Mock;

/// <summary>
/// Web モック生成チャット用のツール定義。ツールは <c>save_mock_html</c> の 1 つのみ。
/// AI に完成した単一 HTML モックを提出させるための唯一の手段として公開する。
/// </summary>
public static class MockDesignTools
{
    /// <summary>モック HTML 提出ツールの名前</summary>
    public const string SaveMockHtmlToolName = "save_mock_html";

    /// <summary>モック生成チャットで公開するツール定義一覧を返す</summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = SaveMockHtmlToolName,
                Description =
                    "完成した単一 HTML モックを提出します。ユーザーへ画面を見せる唯一の手段です。"
                    + "チャット本文へ HTML を貼らず、必ずこのツールで完全な HTML 全体（部分ではなく全画面分）を提出してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        html = new
                        {
                            type = "string",
                            description = "完成した単一ファイルの HTML 全体（CSS/JS インライン・外部参照禁止）",
                        },
                        revision_note = new
                        {
                            type = "string",
                            description = "この版での変更点（1 行・省略可）",
                        },
                    },
                    required = new[] { "html" },
                },
            },
        ];
    }
}
