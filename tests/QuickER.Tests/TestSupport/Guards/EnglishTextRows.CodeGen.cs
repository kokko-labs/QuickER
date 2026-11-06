using QuickER.Cli;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：CodeGen（生成の設定のカタログ）</summary>
internal static partial class EnglishTextRows
{
    private static IEnumerable<EnglishTextRow> CodeGen() =>
        [
            // get_generation_config_schema（MCP のツール）が返すカタログの全文（説明・rules・キーの説明）。外部 AI エージェント向けの英語正本。
            // キーの集合・型・既定値の照合は ParityTests の行 config-schema-* と Cli/GenerationConfigSchemaTests
            new(
                "config-schema-catalog",
                "get_generation_config_schema のカタログは英語で統一する（src/QuickER.Cli/GenerationConfigSchema.cs を確認する。"
                    + "日本語 UI で作った出力を見る）",
                () =>
                    [
                        new(
                            "GenerationConfigSchema.BuildJson",
                            UnderUiCulture(JapaneseUi, GenerationConfigSchema.BuildJson)
                        ),
                    ]
            ),
        ];
}
