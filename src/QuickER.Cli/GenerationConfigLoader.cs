using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Cli.Resources;
using QuickER.CodeGen.CSharp;
using QuickER.Provider;

namespace QuickER.Cli;

/// <summary>
/// 設定ファイル（quicker.json / codegen-settings.json）と CLI フラグから
/// <see cref="CodeGenerationOptions"/> を組み立てる設定読解の担当。
/// </summary>
internal static class GenerationConfigLoader
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 設定ファイル（quicker.json）を読み、CLI フラグ（設定キーと 1:1 対応する kebab-case フラグ群）で
    /// 上書きして生成オプションを構築する。優先順位は全キー一律「CLI フラグ ＞ 設定ファイル ＞ 既定値」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="generation"/> の各フラグは、指定された（＝<c>null</c> でない）値だけを設定 JSON の
    /// 該当キーへ上書きする（表駆動）。bool フラグは三値（未指定＝設定ファイルの値 / <c>--flag</c>＝true /
    /// <c>--flag false</c>＝false）、文字列フラグ（<c>OutputPath</c> を含む）は空白でなければ上書きする。
    /// </para>
    /// <para>
    /// 表適用後に 2 つの後処理を行う。(1) <c>--repository-dialects</c> の特例＝フラグ・設定ファイルとも
    /// <see cref="CodeGenerationOptions.RepositoryDialects"/> 未指定なら <paramref name="provider"/> の名前
    /// （図の TargetDbms から導出）を単一要素で設定する。(2) 出力先の橋渡し＝設定 JSON に <c>OutputPath</c> があり
    /// <c>OutputFileName</c> が無ければ、<c>Path.GetFileName(OutputPath)</c>（非空のとき）を <c>OutputFileName</c> へ導出する
    /// （コアは従来どおり出力ファイル名のみを扱うため）。<c>--output-path</c> フラグ自体の設定 JSON への反映は
    /// 表駆動の <see cref="GenerationOptionSet.ApplyOverrides"/> が担う。
    /// </para>
    /// <para>
    /// QuickER 版 Repository 生成（<c>GenerateRepositories</c>）が要求され、かつ実効方言に未対応方言が含まれる場合は
    /// <see cref="RepositoryDialectUnsupportedException"/> を送出する。
    /// </para>
    /// </remarks>
    public static CodeGenerationOptions LoadOptions(
        FileInfo? config,
        IDatabaseProvider provider,
        ParseResult parseResult,
        GenerationOptionSet generation
    )
    {
        var node = LoadConfigNode(config);

        // 表駆動: CLI で指定された各フラグ（null でないもの・OutputPath 含む）だけを設定 JSON の該当キーへ上書きする
        generation.ApplyOverrides(parseResult, node);

        return BuildOptions(node, provider, parseResult.GetValue(generation.RepositoryDialects));
    }

    /// <summary>
    /// ParseResult 非依存で、設定ファイル（<paramref name="config"/>）＋既定値のみから生成オプションを構築する。
    /// CLI の <see cref="ParseResult"/> を経由しない経路（MCP の generate_csharp ツール等）が使う。
    /// CLI フラグによる上書きはなく、<c>--repository-dialects</c> は未指定扱い（設定ファイルに無ければ
    /// <paramref name="provider"/> の方言で単一導出する）。
    /// </summary>
    public static CodeGenerationOptions LoadOptions(FileInfo? config, IDatabaseProvider provider) =>
        BuildOptions(LoadConfigNode(config), provider, repositoryDialectsFlag: null);

    /// <summary>設定ファイル（quicker.json）を JsonObject として読み込む（無指定・不在なら空オブジェクト）</summary>
    private static JsonObject LoadConfigNode(FileInfo? config) =>
        config is { Exists: true }
            ? JsonNode.Parse(File.ReadAllText(config.FullName))?.AsObject() ?? new JsonObject()
            : new JsonObject();

    /// <summary>
    /// 設定 JSON ノードから <see cref="CodeGenerationOptions"/> を組み立てる共通後処理。
    /// (1) RepositoryDialects の特例（<paramref name="repositoryDialectsFlag"/> 指定時はそれを採用・
    /// 未指定かつ設定ファイルにも無ければ <paramref name="provider"/> の方言で単一導出）、(2) OutputPath →
    /// OutputFileName の導出、(3) デシリアライズ、(4) 対応方言の検証を行う。
    /// </summary>
    private static CodeGenerationOptions BuildOptions(
        JsonObject node,
        IDatabaseProvider provider,
        string? repositoryDialectsFlag
    )
    {
        // 後処理1: RepositoryDialects の特例（フラグ・設定ファイルとも未指定なら図の方言で単一導出する）
        if (!string.IsNullOrWhiteSpace(repositoryDialectsFlag))
        {
            var dialects = repositoryDialectsFlag
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            SetNodeValue(
                node,
                "RepositoryDialects",
                new JsonArray(dialects.Select(dialect => JsonValue.Create(dialect)).ToArray())
            );
        }
        else if (FindProperty(node, "RepositoryDialects") is not JsonArray { Count: > 0 })
        {
            // 設定ファイルに RepositoryDialects（非空）があればそれを温存し、無ければ図の方言（provider.Name）を
            // 単一要素で設定する（GUI で選んだ対象 DB がこの経路で CLI に伝わる）。
            SetNodeValue(
                node,
                "RepositoryDialects",
                new JsonArray(JsonValue.Create(provider.Name))
            );
        }

        // 後処理2: OutputPath → OutputFileName の導出（コアは出力ファイル名のみを扱うため橋渡しする）。
        // OutputPath 自体は表駆動の ApplyOverrides で設定済みのため、ここではファイル名部分の導出だけを行う
        DeriveOutputFileName(node);

        var options =
            node.Deserialize<CodeGenerationOptions>(OptionsJson) ?? new CodeGenerationOptions();

        if (options.GenerateRepositories)
        {
            IReadOnlyList<string> effectiveDialects;
            try
            {
                effectiveDialects = options.EffectiveRepositoryDialects;
            }
            catch (ArgumentException ex)
            {
                throw new RepositoryDialectUnsupportedException(ex.Message);
            }

            var unsupported = effectiveDialects
                .Where(dialect =>
                    !CodeGenerationOptions.SupportedRepositoryDialects.Contains(
                        dialect,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                .ToList();

            if (unsupported.Count > 0)
            {
                throw new RepositoryDialectUnsupportedException(
                    string.Format(
                        Strings.Cli_RepositoryDialectUnsupported,
                        string.Join(", ", unsupported),
                        string.Join(", ", CodeGenerationOptions.SupportedRepositoryDialects)
                    )
                );
            }
        }

        return options;
    }

    /// <summary>
    /// 設定 JSON に <c>OutputPath</c> があり <c>OutputFileName</c> が無ければ、そのファイル名部分を
    /// <c>OutputFileName</c> へ導出する（大文字小文字非依存でキーを探す＝GUI は camelCase で書き出すため）。
    /// </summary>
    private static void DeriveOutputFileName(JsonObject node)
    {
        // 既に OutputFileName があれば尊重する（手書き設定の明示指定を上書きしない）
        if (FindProperty(node, "OutputFileName") is not null)
        {
            return;
        }

        if (
            FindProperty(node, "OutputPath") is JsonValue outputPathValue
            && outputPathValue.TryGetValue(out string? outputPath)
            && !string.IsNullOrWhiteSpace(outputPath)
        )
        {
            var fileName = Path.GetFileName(outputPath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                node["OutputFileName"] = fileName;
            }
        }
    }

    /// <summary>
    /// 設定 JSON の指定キーを大文字小文字非依存で上書きする。既存の別綴りキー（例 camelCase の GUI 出力）を
    /// 取り除いてから正準キー（PascalCase）で設定する＝綴り違いの二重キーが残らないようにする。
    /// </summary>
    internal static void SetNodeValue(JsonObject node, string key, JsonNode? value)
    {
        var duplicates = node.Where(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
            )
            .Select(pair => pair.Key)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            node.Remove(duplicate);
        }

        node[key] = value;
    }

    /// <summary>設定 JSON から指定キーの値を大文字小文字非依存で探す（無ければ null）</summary>
    private static JsonNode? FindProperty(JsonObject node, string key)
    {
        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}

/// <summary>QuickER 版 Repository の生成が要求されたが、指定プロバイダの方言が未対応のときに送出する例外</summary>
internal sealed class RepositoryDialectUnsupportedException(string message) : Exception(message);
