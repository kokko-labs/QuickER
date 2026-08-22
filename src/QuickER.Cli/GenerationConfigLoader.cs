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

    /// <summary>設定 JSON で有効なキー名の集合（未知キー警告の許可集合）</summary>
    /// <remarks>
    /// 正体は <see cref="GenerationConfigSchema"/> の全キー（＝<see cref="CodeGenerationOptions"/> の設定可能
    /// プロパティと 1:1。<c>GenerationConfigSchemaTests</c> が一致を強制）に、GUI が書き出す正当な別名
    /// <c>OutputPath</c>（<see cref="DeriveOutputFileName"/> が <c>OutputFileName</c> へ橋渡しする）を加えたもの。
    /// 照合は <see cref="OptionsJson"/> の <c>PropertyNameCaseInsensitive</c>（＝デシリアライズ側の規則）に
    /// 合わせて大文字小文字非依存とする。
    /// </remarks>
    private static readonly HashSet<string> KnownConfigKeys = new(
        GenerationConfigSchema.Keys.Select(key => key.Name).Append("OutputPath"),
        StringComparer.OrdinalIgnoreCase
    );

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
    /// （コアは出力ファイル名のみを扱うため）。<c>--output-path</c> フラグ自体の設定 JSON への反映は
    /// 表駆動の <see cref="GenerationOptionSet.ApplyOverrides"/> が担う。
    /// </para>
    /// <para>
    /// QuickER 版 Repository 生成（<c>GenerateRepositories</c>）が要求され、かつ実効方言に未対応方言が含まれる場合は
    /// <see cref="RepositoryDialectUnsupportedException"/> を送出する。<paramref name="config"/> が指定されたのに
    /// 存在しない・JSON オブジェクトとして読めない・値の型が合わない場合は
    /// <see cref="GenerationConfigException"/> を送出する。
    /// </para>
    /// <para>
    /// <paramref name="warnings"/> を渡すと、設定ファイル中の未知キー（許可集合外）を警告として書き出す
    /// （エラーにはせず生成は続行する＝前方互換）。
    /// </para>
    /// </remarks>
    public static CodeGenerationOptions LoadOptions(
        FileInfo? config,
        IDatabaseProvider provider,
        ParseResult parseResult,
        GenerationOptionSet generation,
        TextWriter? warnings = null
    )
    {
        var node = LoadConfigNode(config, warnings);

        // 表駆動: CLI で指定された各フラグ（null でないもの・OutputPath 含む）だけを設定 JSON の該当キーへ上書きする
        generation.ApplyOverrides(parseResult, node);

        return BuildOptions(
            node,
            provider,
            parseResult.GetValue(generation.RepositoryDialects),
            config
        );
    }

    /// <summary>
    /// ParseResult 非依存で、設定ファイル（<paramref name="config"/>）＋既定値のみから生成オプションを構築する。
    /// CLI の <see cref="ParseResult"/> を経由しない経路（MCP の generate_csharp ツール等）が使う。
    /// CLI フラグによる上書きはなく、<c>--repository-dialects</c> は未指定扱い（設定ファイルに無ければ
    /// <paramref name="provider"/> の方言で単一導出する）。
    /// </summary>
    public static CodeGenerationOptions LoadOptions(
        FileInfo? config,
        IDatabaseProvider provider,
        TextWriter? warnings = null
    ) =>
        BuildOptions(
            LoadConfigNode(config, warnings),
            provider,
            repositoryDialectsFlag: null,
            config
        );

    /// <summary>設定ファイル（quicker.json）を JsonObject として読み込む（無指定なら空オブジェクト）</summary>
    /// <remarks>
    /// <para>
    /// 明示されたのに存在しないパスは <see cref="GenerationConfigException"/> で中断する。「未指定」と
    /// 同じ既定枝へ落とすと、パスのタイプミスが「全キー既定値のまま成功（終了コード 0）」として
    /// 黙って通ってしまうため（<c>--schema</c> や MCP の generate_csharp と同じ存在チェックの水準に揃える）。
    /// </para>
    /// <para>
    /// 内容が JSON として不正、JSON オブジェクトでない（配列・スカラー）、JSON リテラル <c>null</c>、
    /// 読み取り自体に失敗した（<see cref="IOException"/> ／<see cref="UnauthorizedAccessException"/>）場合も
    /// 同じ例外へ変換する（生の例外のままだとスタックトレースで落ちるため）。
    /// </para>
    /// </remarks>
    private static JsonObject LoadConfigNode(FileInfo? config, TextWriter? warnings)
    {
        if (config is null)
        {
            return new JsonObject();
        }

        if (!config.Exists)
        {
            throw new GenerationConfigException(
                string.Format(Strings.Cli_ConfigFileNotFound, config.FullName)
            );
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(config.FullName));
        }
        catch (Exception ex)
            when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // JsonException＝JSON として壊れている／IOException・UnauthorizedAccessException＝読み取り不可
            throw new GenerationConfigException(
                string.Format(Strings.Cli_ConfigFileInvalidJson, config.FullName, ex.Message)
            );
        }

        // JSON リテラル null は Parse が例外でなく null を返す。空オブジェクトへ丸めると「全キー既定値のまま
        // 終了コード 0」へ黙って化けるため、非オブジェクトルート（配列・スカラー）と同じ扱いで中断する
        if (parsed is null)
        {
            throw new GenerationConfigException(
                string.Format(
                    Strings.Cli_ConfigFileInvalidJson,
                    config.FullName,
                    Strings.Cli_ConfigRootNull
                )
            );
        }

        JsonObject node;

        try
        {
            node = parsed.AsObject();
        }
        catch (InvalidOperationException ex)
        {
            // AsObject 不可＝ルートが JSON オブジェクトでない（配列・数値・文字列・真偽値）
            throw new GenerationConfigException(
                string.Format(Strings.Cli_ConfigFileInvalidJson, config.FullName, ex.Message)
            );
        }

        WarnUnknownKeys(node, config, warnings);

        return node;
    }

    /// <summary>設定ファイルに未知のキー（許可集合 <see cref="KnownConfigKeys"/> の外）があれば警告する</summary>
    /// <remarks>
    /// エラーにせず警告に留めるのは前方互換のため（新しいツールが書いた設定キーを古い CLI が読んでも止まらない）。
    /// 別名 <c>OutputPath</c> は GUI が書き出す正当なキーなので許可集合に含める（厳格拒否すると GUI → CLI の
    /// 受け渡しが壊れる）。この検査は CLI フラグの反映（<see cref="GenerationOptionSet.ApplyOverrides"/>）より
    /// 前に行う＝ユーザーが実際に書いたキーだけを対象にする。
    /// </remarks>
    private static void WarnUnknownKeys(JsonObject node, FileInfo config, TextWriter? warnings)
    {
        if (warnings is null)
        {
            return;
        }

        var unknown = node.Select(pair => pair.Key)
            .Where(key => !KnownConfigKeys.Contains(key))
            .ToList();

        if (unknown.Count > 0)
        {
            warnings.WriteLine(
                string.Format(
                    Strings.Cli_ConfigUnknownKeyWarning,
                    config.FullName,
                    string.Join(", ", unknown)
                )
            );
        }
    }

    /// <summary>
    /// 設定 JSON ノードから <see cref="CodeGenerationOptions"/> を組み立てる共通後処理。
    /// (1) RepositoryDialects の特例（<paramref name="repositoryDialectsFlag"/> 指定時はそれを採用・
    /// 未指定かつ設定ファイルにも無ければ <paramref name="provider"/> の方言で単一導出）、(2) OutputPath →
    /// OutputFileName の導出、(3) デシリアライズ、(4) 対応方言の検証を行う。
    /// <paramref name="config"/> はエラーメッセージにファイルパスを載せるためだけに使う。
    /// </summary>
    private static CodeGenerationOptions BuildOptions(
        JsonObject node,
        IDatabaseProvider provider,
        string? repositoryDialectsFlag,
        FileInfo? config
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
        else
        {
            var configured = FindProperty(node, "RepositoryDialects");

            if (configured is null or JsonArray { Count: 0 })
            {
                // 未指定（キーなし・JSON null・空配列）なら図の方言（provider.Name）を単一要素で設定する
                // （GUI で選んだ対象 DB がこの経路で CLI に伝わる）。
                SetNodeValue(
                    node,
                    "RepositoryDialects",
                    new JsonArray(JsonValue.Create(provider.Name))
                );
            }
            else if (configured is not JsonArray)
            {
                // 型違い（例: 配列でなく文字列 "sqlserver" と書いた）。黙って破棄して図の方言で上書きすると
                // 意図と違う方言のコードが無警告で出るため、ユーザーの明確な誤りとしてエラーにする
                throw new GenerationConfigException(
                    string.Format(
                        Strings.Cli_ConfigRepositoryDialectsNotArray,
                        config?.FullName ?? string.Empty
                    )
                );
            }

            // 非空の JsonArray は設定ファイルの指定を温存する
        }

        // 後処理2: OutputPath → OutputFileName の導出（コアは出力ファイル名のみを扱うため橋渡しする）。
        // OutputPath 自体は表駆動の ApplyOverrides で設定済みのため、ここではファイル名部分の導出だけを行う
        DeriveOutputFileName(node);

        CodeGenerationOptions options;

        try
        {
            options =
                node.Deserialize<CodeGenerationOptions>(OptionsJson) ?? new CodeGenerationOptions();
        }
        catch (JsonException ex)
        {
            // 値の型違い（例: bool キーへ文字列 "yes"）。捕捉しないと System.CommandLine の既定ハンドラへ
            // 抜けてスタックトレースが露出するため、どのキーで失敗したかを含む整形メッセージへ変換する
            throw new GenerationConfigException(
                string.Format(
                    Strings.Cli_ConfigValueInvalid,
                    config?.FullName ?? string.Empty,
                    ex.Path ?? "?",
                    ex.Message
                )
            );
        }

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

/// <summary>
/// 設定ファイル（<c>--config</c>）が存在しない、JSON オブジェクトとして読めない、
/// または値の型が設定キーと合わないときに送出する例外
/// </summary>
internal sealed class GenerationConfigException(string message) : Exception(message);
