using QuickER.Mcp;

namespace QuickER.AI.Mock;

/// <summary>
/// モックフォルダ方式の Web モック生成チャットで公開するツール定義。
/// 画面（HTML）と共有スタイルシート（style.css）を「モックフォルダ」（画面ごとの HTML＋共有 CSS）として
/// 保存・取得・削除するための 4 ツールを提供する。
/// </summary>
/// <remarks>
/// ツール定義は中立言語（英語）を正本とする（<see cref="ErDiagramToolCatalog"/> と同流儀・ハードコード）。
/// 実行は <see cref="MockFolderDesignSession"/> が担い、<see cref="MockFolderStore"/> へ委譲する。
/// </remarks>
public static class MockFolderDesignTools
{
    /// <summary>1 画面の完全な HTML を保存（upsert）するツール名</summary>
    public const string SaveScreenToolName = "save_screen";

    /// <summary>画面を削除するツール名</summary>
    public const string RemoveScreenToolName = "remove_screen";

    /// <summary>共有スタイルシート（style.css）を保存するツール名</summary>
    public const string SaveStylesheetToolName = "save_stylesheet";

    /// <summary>画面の現在の HTML を取得するツール名</summary>
    public const string GetScreenToolName = "get_screen";

    /// <summary>モックフォルダ方式のチャットで公開するツール定義一覧を返す</summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = SaveScreenToolName,
                Description =
                    "Saves the complete HTML of a single screen into the mock folder (upsert). "
                    + "This is the only way to show a screen to the user - never paste HTML into the chat body. "
                    + "Always submit the whole HTML of the screen (not a diff). "
                    + "Before modifying an existing screen, call get_screen to fetch its current HTML first. "
                    + "Reference the shared stylesheet with <link rel=\"stylesheet\" href=\"style.css\">, "
                    + "link between screens with relative hrefs (e.g. href=\"Other.html\"), and never reference external resources. "
                    + "Use the optional entities argument to declare which entities (tables) this screen uses and how (its CRUD footprint): "
                    + "omitting entities keeps the screen's existing declarations, an empty array clears them, and a non-empty array replaces them.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        file = new
                        {
                            type = "string",
                            description = "Screen file name directly under the mock folder (PascalCase recommended, must end with .html; no path separators or '..').",
                        },
                        name = new { type = "string", description = "Display name of the screen." },
                        description = new
                        {
                            type = "string",
                            description = "Description of the screen's role (recommended).",
                        },
                        html = new
                        {
                            type = "string",
                            description = "The complete single HTML document for this screen (must include <html>). "
                                + "Reference the shared CSS via <link rel=\"stylesheet\" href=\"style.css\">; "
                                + "put only screen-specific tweaks in an inline <style>. Link between screens with relative href=\"Other.html\". External references are forbidden (offline-only).",
                        },
                        transitions = new
                        {
                            type = "array",
                            description = "Navigation declarations that start from this screen (each element is a transition from this screen to another).",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    to = new
                                    {
                                        type = "string",
                                        description = "Destination screen file name.",
                                    },
                                    trigger = new
                                    {
                                        type = "string",
                                        description = "Description of what triggers the transition (e.g. row click).",
                                    },
                                },
                                required = new[] { "to" },
                            },
                        },
                        entities = new
                        {
                            type = "array",
                            description = "Optional. Declares which entities (tables) this screen uses and how (its CRUD footprint). "
                                + "Upsert semantics: omitting entities keeps the screen's existing declarations, "
                                + "an empty array clears them, and a non-empty array replaces them (transitions, by contrast, are always fully replaced). "
                                + "Only C/R/U/D operations are meaningful; declarations with no valid operations are dropped with a warning.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new
                                    {
                                        type = "string",
                                        description = "Entity (table) name as defined in the ER diagram (matched case-insensitively).",
                                    },
                                    operations = new
                                    {
                                        type = "string",
                                        description = "The CRUD operations this screen performs on the entity, as a subset of \"CRUD\" (e.g. \"CRU\"). "
                                            + "Case-insensitive; characters outside C/R/U/D are ignored, duplicates removed, and the result ordered C, R, U, D.",
                                    },
                                },
                                required = new[] { "name" },
                            },
                        },
                        revision_note = new
                        {
                            type = "string",
                            description = "One-line note describing what changed in this revision (optional).",
                        },
                    },
                    required = new[] { "file", "name", "html" },
                },
            },
            new ToolDefinition
            {
                Name = RemoveScreenToolName,
                Description = "Removes a screen from the mock folder.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        file = new
                        {
                            type = "string",
                            description = "File name of the screen to remove.",
                        },
                    },
                    required = new[] { "file" },
                },
            },
            new ToolDefinition
            {
                Name = SaveStylesheetToolName,
                Description =
                    "Saves the shared stylesheet (style.css) for the mock folder. "
                    + "This is the common design system for all screens - create it before the screens.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        css = new { type = "string", description = "The entire CSS content." },
                        revision_note = new
                        {
                            type = "string",
                            description = "One-line note describing what changed in this revision (optional).",
                        },
                    },
                    required = new[] { "css" },
                },
            },
            new ToolDefinition
            {
                Name = GetScreenToolName,
                Description = "Returns the current HTML of a screen in the mock folder.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        file = new
                        {
                            type = "string",
                            description = "File name of the screen to fetch.",
                        },
                    },
                    required = new[] { "file" },
                },
            },
        ];
    }
}
