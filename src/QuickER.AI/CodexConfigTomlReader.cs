using System.IO;

namespace QuickER.AI;

/// <summary>Codex の config.toml から読み込んだ設定</summary>
public sealed class CodexConfigToml
{
    /// <summary>config.toml の model フィールド（既定モデル名）</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>config.toml の model_provider フィールド（既定プロバイダー名）</summary>
    public string ModelProvider { get; init; } = string.Empty;

    /// <summary>config.toml に定義されたプロバイダー名の一覧（[model_providers.xxx] セクションから収集）</summary>
    public IReadOnlyList<string> ProviderNames { get; init; } = [];
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
                var sectionName = ParseSectionName(line);
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

        return new CodexConfigToml
        {
            Model = model,
            ModelProvider = modelProvider,
            ProviderNames = providerNames.AsReadOnly(),
        };
    }

    /// <summary>セクション見出しの行から名前を取り出す（<c>[[array]]</c> の括弧と末尾コメントを除く）</summary>
    /// <remarks>
    /// 名前は先頭の <c>[</c> の並びの後ろから最初の <c>]</c> の手前まで。末尾から括弧を剥がす形にすると、
    /// <c>[model_providers.ollama] # local</c> の名前が <c>ollama] # local</c> になる
    /// </remarks>
    private static string ParseSectionName(string line)
    {
        var body = line.TrimStart('[');
        var closeIndex = body.IndexOf(']');

        return (closeIndex >= 0 ? body[..closeIndex] : body).Trim();
    }

    /// <summary>TOML の値文字列からクォートと末尾コメントを除去しプレーン文字列を返す</summary>
    /// <remarks>
    /// 末尾コメントの <c>#</c> は引用の外だけで探す（<c>"a#b"</c> の <c>#</c> は値の一部）。
    /// 基本文字列（<c>"…"</c>）は <c>\"</c> のエスケープを読み飛ばして閉じ引用符を探し、
    /// リテラル文字列（<c>'…'</c>）は次の <c>'</c> で閉じる。エスケープは解かずにそのまま返す
    /// </remarks>
    private static string ParseTomlValue(string raw)
    {
        if (raw.Length > 0 && (raw[0] == '"' || raw[0] == '\''))
        {
            var closeIndex = FindClosingQuote(raw);

            if (closeIndex > 0)
            {
                return raw[1..closeIndex];
            }

            // 閉じ引用符が無い不正な値は加工せずに返す
            return raw;
        }

        // 引用されていない値（数値・真偽値など）は最初の # 以降を末尾コメントとして除去する
        var commentIndex = raw.IndexOf('#');

        return commentIndex >= 0 ? raw[..commentIndex].Trim() : raw;
    }

    /// <summary>先頭の引用符に対応する閉じ引用符の位置を返す（見つからなければ -1）</summary>
    private static int FindClosingQuote(string raw)
    {
        var quote = raw[0];

        for (var i = 1; i < raw.Length; i++)
        {
            // 基本文字列のエスケープ（\" や \\）は次の 1 文字ごと読み飛ばす
            if (quote == '"' && raw[i] == '\\')
            {
                i++;
                continue;
            }

            if (raw[i] == quote)
            {
                return i;
            }
        }

        return -1;
    }
}
