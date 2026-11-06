using QuickER.Cli;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Tests.Cli;
using QuickER.Tests.CodeGen.UI;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：CodeGen（生成オプションのキーと、設定カタログ・CLI のフラグ表・GUI の設定）</summary>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> CodeGen() =>
        [
            new(
                "config-schema-keys-match-options",
                "get_generation_config_schema のカタログ（src/QuickER.Cli/GenerationConfigSchema.cs）は CodeGenerationOptions の"
                    + "設定キーと 1:1 でなければならない。差分がある場合はカタログを更新する",
                ParityCompare.Set,
                OptionNames,
                () => [.. GenerationConfigSchema.Keys.Select(key => key.Name)]
            ),
            new(
                "config-schema-enum-allowed-values",
                "列挙型のキーの allowedValues は列挙体のメンバー名と順まで一致する（候補の書き漏れを検知する。項目はキー: 値）",
                ParityCompare.Sequence,
                () =>
                    [
                        .. EnumKeys()
                            .SelectMany(key =>
                                Enum.GetNames(key.Type).Select(name => $"{key.Name}: {name}")
                            ),
                    ],
                () =>
                    [
                        .. EnumKeys()
                            .SelectMany(key =>
                                (key.AllowedValues ?? []).Select(value => $"{key.Name}: {value}")
                            ),
                    ]
            ),
            new(
                "cli-config-keys-match-options",
                "CLI フラグ表（src/QuickER.Cli/GenerationOptionSet.cs）の設定キーは CodeGenerationOptions の設定可能プロパティと"
                    + "1:1 でなければならない。綴り違いはフラグを無警告で無効化し、欠落は「GUI/MCP では設定できるのに CLI からは"
                    + "設定できない」非対称になる（例外は別名 "
                    + GenerationOptionSetParityTests.OutputPathKey
                    + " → "
                    + GenerationOptionSetParityTests.OutputFileNameKey
                    + " の 1 件だけで、写しの読み取りで読み替える）",
                ParityCompare.Set,
                OptionNames,
                () => [.. GenerationOptionSetParityTests.CliConfigKeys()]
            ),
            new(
                "cli-config-keys-match-schema",
                "CLI フラグ表と get_generation_config_schema のカタログは同じ設定キー集合を指す（別名 OutputPath は読み替える）",
                ParityCompare.Set,
                () => [.. GenerationConfigSchema.Keys.Select(key => key.Name)],
                () => [.. GenerationOptionSetParityTests.CliConfigKeys()]
            ),
            new(
                "generation-settings-keys-match-options",
                "GUI の設定（CSharpGenerationSettings）のキーと生成オプション（CodeGenerationOptions）のキーは 1:1 に対応する"
                    + "（出力先の 1 組＝設定の "
                    + GenerationSettingsParityTests.SettingsOnly
                    + " とオプションの "
                    + GenerationSettingsParityTests.OptionsOnly
                    + " は意味が違うので両側の読み取りで外す）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. OptionKeys
                            .Writable(typeof(CodeGenerationOptions))
                            .Select(property => property.Name)
                            .Where(name => name != GenerationSettingsParityTests.OptionsOnly),
                    ],
                () =>
                    [
                        .. OptionKeys
                            .Writable(typeof(CSharpGenerationSettings))
                            .Select(property => property.Name)
                            .Where(name => name != GenerationSettingsParityTests.SettingsOnly),
                    ]
            ),
        ];

    /// <summary>設定 JSON の正となるキー集合＝CodeGenerationOptions の init 設定可能なインスタンスプロパティ</summary>
    private static IReadOnlyList<string> OptionNames() =>
        [.. OptionKeys.Writable(typeof(CodeGenerationOptions)).Select(property => property.Name)];

    /// <summary>カタログの列挙型のキー（カタログの順・プロパティの型つき）</summary>
    private static IReadOnlyList<(
        string Name,
        Type Type,
        IReadOnlyList<string>? AllowedValues
    )> EnumKeys()
    {
        var byName = OptionKeys
            .Writable(typeof(CodeGenerationOptions))
            .ToDictionary(property => property.Name);

        return
        [
            .. GenerationConfigSchema
                .Keys.Where(key => byName[key.Name].PropertyType.IsEnum)
                .Select(key =>
                    (
                        key.Name,
                        byName[key.Name].PropertyType,
                        (IReadOnlyList<string>?)key.AllowedValues
                    )
                ),
        ];
    }
}
