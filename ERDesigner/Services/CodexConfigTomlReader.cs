using System.IO;

namespace ERDesigner.Services;

/// <summary>Codex の config.toml から読み込んだ設定</summary>
public sealed class CodexConfigToml
{
    /// <summary>config.toml の model フィールド（既定モデル名）</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>config.toml の model_provider フィールド（既定プロバイダー名）</summary>
    public string ModelProvider { get; init; } = string.Empty;

    /// <summary>config.toml に定義されたプロバイダー名の一覧（[model_providers.xxx] セクションから収集）</summary>
    public IReadOnlyList<string> ProviderNames { get; init; } = [];

    /// <summary>プロバイダー名ごとのモデル候補辞書</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ProviderModels { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>Codex の config.toml を読み込むリーダー</summary>
/// <remarks>外部ライブラリに依存せず、必要なフィールドのみ最低限のパースを行う</remarks>
public static class CodexConfigTomlReader
{
    /// <summary>既定の config.toml パス（~/.codex/config.toml）</summary>
    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml"
        );

    /// <summary>既定パスから config.toml を読み込む（ファイルが無ければ空の設定を返す）</summary>
    public static CodexConfigToml Read() => Read(DefaultConfigPath);

    /// <summary>指定パスから config.toml を読み込む（ファイルが無い・解析失敗時は空の設定を返す）</summary>
    public static CodexConfigToml Read(string path)
    {
        if (!File.Exists(path))
        {
            return new CodexConfigToml();
        }

        try
        {
            var lines = File.ReadAllLines(path);
            return Parse(lines);
        }
        catch
        {
            // 読み取り・解析失敗時は起動を妨げないよう空の設定へフォールバックする
            return new CodexConfigToml();
        }
    }

    /// <summary>TOML 行配列から model / model_provider / プロバイダー名を抽出する</summary>
    internal static CodexConfigToml Parse(IEnumerable<string> lines)
    {
        var model = string.Empty;
        var modelProvider = string.Empty;
        var providerNames = new List<string>();
        var currentSection = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // コメント・空行をスキップする
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            // セクションヘッダー（[model_providers.xxx] 形式に対応）
            if (line.StartsWith('['))
            {
                // [[array]] 形式も考慮してトリム
                var sectionName = line.TrimStart('[').TrimEnd(']').Trim();
                currentSection = sectionName;

                // [model_providers.<name>] セクションのプロバイダー名を収集する
                if (sectionName.StartsWith("model_providers.", StringComparison.OrdinalIgnoreCase))
                {
                    var providerName = sectionName["model_providers.".Length..].Trim();

                    if (
                        !string.IsNullOrEmpty(providerName)
                        && !providerNames.Contains(providerName, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        providerNames.Add(providerName);
                    }
                }

                continue;
            }

            // key = value 形式のみ処理する
            var eqIndex = line.IndexOf('=');

            if (eqIndex < 0)
            {
                continue;
            }

            var key = line[..eqIndex].Trim();
            var value = ParseTomlValue(line[(eqIndex + 1)..].Trim());

            // トップレベルの model / model_provider を取得する
            if (string.IsNullOrEmpty(currentSection))
            {
                if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
                {
                    model = value;
                }
                else if (key.Equals("model_provider", StringComparison.OrdinalIgnoreCase))
                {
                    modelProvider = value;
                }
            }
        }

        // トップレベルの model_provider と model をプロバイダー別モデル候補辞書に登録する
        var providerModels = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );

        if (!string.IsNullOrEmpty(modelProvider) && !string.IsNullOrEmpty(model))
        {
            providerModels[modelProvider] = [model];
        }

        return new CodexConfigToml
        {
            Model = model,
            ModelProvider = modelProvider,
            ProviderNames = providerNames.AsReadOnly(),
            ProviderModels = providerModels,
        };
    }

    /// <summary>TOML の値文字列からクォートと末尾コメントを除去しプレーン文字列を返す</summary>
    private static string ParseTomlValue(string raw)
    {
        // 末尾コメントを除去する
        var commentIndex = raw.IndexOf('#');

        if (commentIndex > 0)
        {
            raw = raw[..commentIndex].Trim();
        }

        // 引用符を除去する（" か ' で囲まれた文字列）
        if (
            raw.Length >= 2
            && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''))
        )
        {
            return raw[1..^1];
        }

        return raw;
    }
}
