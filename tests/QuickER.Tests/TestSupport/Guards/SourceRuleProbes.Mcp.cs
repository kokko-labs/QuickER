namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：MCP とツール・AI のチャットエンジン</summary>
internal static partial class SourceRuleProbes
{
    private static IEnumerable<SourceRuleProbe> Mcp() =>
        [
            // シンボルで判定し、JSON の第 1 引数の宣言（NULL 許容・拡張メソッド・ローカル関数も）・種別の列挙型の Parse / TryParse /
            // GetValues（型引数・typeof・別名）・写しの名前の宣言を拾う。JSON でない第 1 引数・違う名前・GetNames・ほかの列挙型・
            // IsDefined は拾わない
            new SourceRuleProbe(
                "tool-argument-parsing-copies-forms",
                "tool-argument-parsing-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Text.Json;
                        using System.Text.Json.Nodes;
                        using QuickER.Model;
                        using E = System.Enum;

                        internal static class Hits
                        {
                            public static string? GetString(JsonElement element, string name) => null;

                            public static bool? GetBool(JsonObject element, string name) => null;

                            public static bool? GetBool(JsonElement? element, string name) => null;

                            public static string? GetString(this JsonNode node) => null;

                            private static RelationshipType ParseRelationshipType(string text) => default;

                            private static bool HasSameColumnPairs(object a, object b) => false;

                            public static void Calls(string text)
                            {
                                _ = Enum.Parse<RelationshipType>(text);
                                _ = Enum.TryParse(typeof(RelationshipType), text, out _);
                                _ = Enum.TryParse<RelationshipType>(text, true, out _);
                                _ = Enum.GetValues<RelationshipType>();
                                _ = E.Parse(typeof(RelationshipType), text);
                                _ = Enum.GetValues(typeof(RelationshipType));

                                static string? GetString(JsonElement element) => null;
                            }
                        }

                        internal static class Misses
                        {
                            public static string? GetString(string text, string name) => null;

                            public static bool GetBool(int value) => false;

                            public static string? GetStringValue(JsonElement element) => null;

                            public static void Calls(string text)
                            {
                                _ = Enum.GetNames<RelationshipType>();
                                _ = Enum.Parse<DayOfWeek>(text);
                                _ = Enum.TryParse(typeof(DayOfWeek), text, out _);
                                _ = Enum.GetValues<DayOfWeek>();
                                _ = Enum.IsDefined(RelationshipType.OneToOne);
                            }
                        }
                        """,
                        "GetString(JsonElement) の宣言",
                        "GetBool(JsonObject) の宣言",
                        "GetBool(JsonElement) の宣言",
                        "GetString(JsonNode) の宣言",
                        "ParseRelationshipType の宣言",
                        "HasSameColumnPairs の宣言",
                        "Enum.Parse<RelationshipType>",
                        "Enum.TryParse<RelationshipType>",
                        "Enum.TryParse<RelationshipType>",
                        "Enum.GetValues<RelationshipType>",
                        "Enum.Parse<RelationshipType>",
                        "Enum.GetValues<RelationshipType>",
                        "GetString(JsonElement) の宣言"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐（変種のコンパイル）の中の宣言も拾う
            new SourceRuleProbe(
                "tool-argument-parsing-copies-inactive-branch",
                "tool-argument-parsing-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Text.Json;

                        internal static class Probe
                        {
                        #if QUICKER_PROBE_BRANCH
                            public static string? GetString(JsonElement element, string name) => null;
                        #endif
                        }
                        """,
                        "GetString(JsonElement) の宣言"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 正本の書き方（所有の判定で外す形）を、所有の判定を通さずに拾う
            new SourceRuleProbe(
                "tool-argument-parsing-owners-forms",
                "tool-argument-parsing-owners",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Text.Json;
                        using QuickER.Model;

                        internal static class Probe
                        {
                            public static string? GetString(JsonElement element, string name) => null;

                            public static bool? GetBool(JsonElement element, string name) => null;

                            public static Array Values() => Enum.GetValues<RelationshipType>();
                        }
                        """,
                        "GetString(JsonElement) の宣言",
                        "GetBool(JsonElement) の宣言",
                        "Enum.GetValues<RelationshipType>"
                    ),
                ]
            ),
            // 警告文の表記を 1 出現 1 件拾い、大文字小文字の違う表記は拾わない
            new SourceRuleProbe(
                "newer-format-warning-text-forms",
                "newer-format-warning-text",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string A(int v) => $"Warning: this diagram was saved in a newer format (version {v})";
                            public static string B() => "warning: this diagram was saved in a newer format";
                        }
                        """,
                        "Warning: this diagram was saved in a newer format"
                    ),
                ]
            ),
            // ヘルパーの 2 つの呼び出しを本文の順に拾い、ヘルパーの別のメンバーは拾わない
            new SourceRuleProbe(
                "chat-engine-tool-invocation-uses-forms",
                "chat-engine-tool-invocation-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        var result = ChatToolInvocation.Execute(host, dispatcher, name, () => args);
                        ChatToolInvocation.Notify(ToolActivityReceived, this, activity, status);
                        ChatToolInvocation.Describe(result);
                        """,
                        "ChatToolInvocation.Execute(",
                        "ChatToolInvocation.Notify("
                    ),
                ]
            ),
            // 生の失敗の文言と裸の通知を拾い、ヘルパーへ渡す通知の参照は拾わない
            new SourceRuleProbe(
                "chat-engine-tool-invocation-copies-forms",
                "chat-engine-tool-invocation-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        return ($"Tool '{name}' threw an exception: {ex.Message}", false);
                        ToolActivityReceived?.Invoke(this, activity);
                        ChatToolInvocation.Notify(ToolActivityReceived, this, activity, status);
                        """,
                        "threw an exception:",
                        "ToolActivityReceived?.Invoke("
                    ),
                ]
            ),
        ];
}
