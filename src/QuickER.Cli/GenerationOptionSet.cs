using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using QuickER.Cli.Resources;

namespace QuickER.Cli;

/// <summary>
/// generate / scaffold が共有する「設定キー＝kebab-case フラグ」の生成系オプション束。
/// </summary>
/// <remarks>
/// quicker.json / codegen-settings.json の各設定キーに 1:1 対応する CLI フラグを定義し、CLI で指定された
/// （＝<c>null</c> でない）値だけを設定 JSON へ上書きする表駆動の橋渡しを担う。bool フラグは三値
/// （未指定＝設定ファイルの値 / <c>--flag</c>＝true / <c>--flag false</c>＝false）で、
/// <c>Option&lt;bool?&gt;</c> と <see cref="ArgumentArity.ZeroOrOne"/> ＋カスタムパーサで表現する。
/// </remarks>
internal sealed class GenerationOptionSet
{
    /// <summary>文字列フラグ（設定キー名 → Option）。指定時は空白でなければ設定 JSON の該当キーを上書きする</summary>
    private readonly List<(string Key, Option<string?> Option)> _stringFlags = new();

    /// <summary>三値 bool フラグ（設定キー名 → Option）。値ありのとき設定 JSON の該当キーを上書きする</summary>
    private readonly List<(string Key, Option<bool?> Option)> _boolFlags = new();

    /// <summary>QuickER 版 Repository の対象方言（カンマ区切り）。未指定時の単一導出は <see cref="GenerationConfigLoader"/> の後処理が担う</summary>
    public Option<string?> RepositoryDialects { get; } =
        new("--repository-dialects") { Description = Strings.Cli_Opt_RepositoryDialects };

    public GenerationOptionSet()
    {
        // 出力モード
        AddBool(
            "SplitFilesByCategory",
            "--split-files-by-category",
            Strings.Cli_Opt_SplitFilesByCategory
        );

        // 名前空間
        AddString("RootNamespace", "--root-namespace", Strings.Cli_Opt_RootNamespace);
        AddString("RuntimeNamespace", "--runtime-namespace", Strings.Cli_Opt_RuntimeNamespace);
        AddString("EntityNamespace", "--entity-namespace", Strings.Cli_Opt_EntityNamespace);
        AddString(
            "EditModelNamespace",
            "--edit-model-namespace",
            Strings.Cli_Opt_EditModelNamespace
        );
        AddString("MapperNamespace", "--mapper-namespace", Strings.Cli_Opt_MapperNamespace);
        AddString(
            "RepositoryNamespace",
            "--repository-namespace",
            Strings.Cli_Opt_RepositoryNamespace
        );
        AddString(
            "ValueObjectNamespace",
            "--value-object-namespace",
            Strings.Cli_Opt_ValueObjectNamespace
        );

        // 生成対象
        AddBool("GenerateEditModels", "--generate-edit-models", Strings.Cli_Opt_GenerateEditModels);
        AddBool("GenerateMappers", "--generate-mappers", Strings.Cli_Opt_GenerateMappers);

        // 値オブジェクト
        AddBool(
            "GenerateValueObjects",
            "--generate-value-objects",
            Strings.Cli_Opt_GenerateValueObjects
        );
        AddBool(
            "UseGuidKeyForStringPrimaryKey",
            "--use-guid-key-for-string-primary-key",
            Strings.Cli_Opt_UseGuidKeyForStringPrimaryKey
        );

        // DB アクセス
        AddBool(
            "GenerateRepositories",
            "--generate-repositories",
            Strings.Cli_Opt_GenerateRepositories
        );
        AddBool(
            "ExcludeUnboundedBinaryColumns",
            "--exclude-unbounded-binary-columns",
            Strings.Cli_Opt_ExcludeUnboundedBinaryColumns
        );
        AddBool("GenerateEfCore", "--generate-ef-core", Strings.Cli_Opt_GenerateEfCore);
        AddBool(
            "GenerateInMemoryRepositories",
            "--generate-in-memory-repositories",
            Strings.Cli_Opt_GenerateInMemoryRepositories
        );

        // リモート対応
        AddBool(
            "GenerateRemoteContracts",
            "--generate-remote-contracts",
            Strings.Cli_Opt_GenerateRemoteContracts
        );
        AddBool(
            "GenerateRemoteServices",
            "--generate-remote-services",
            Strings.Cli_Opt_GenerateRemoteServices
        );

        // ランタイム・ドキュメント
        AddBool("UseRuntimePackages", "--use-runtime-packages", Strings.Cli_Opt_UseRuntimePackages);
        AddBool("GenerateApiDocs", "--generate-api-docs", Strings.Cli_Opt_GenerateApiDocs);
        AddBool("IncludeJapaneseApiDocs", "--api-docs-ja", Strings.Cli_Opt_IncludeJapaneseApiDocs);

        // 属性
        AddBool(
            "IncludeDataAnnotations",
            "--include-data-annotations",
            Strings.Cli_Opt_IncludeDataAnnotations
        );
        AddBool(
            "IncludeJsonIgnoreOnParentNavigation",
            "--include-json-ignore-on-parent-navigation",
            Strings.Cli_Opt_IncludeJsonIgnoreOnParentNavigation
        );

        // 出力先（設定キー OutputPath と同義＝普通の文字列フラグ。CLI はそのファイル名部分のみを出力ファイル名として使う）
        AddString("OutputPath", "--output-path", Strings.Cli_Opt_OutputPath);
    }

    /// <summary>コマンドへ登録すべき全 Option を列挙する（文字列 → bool → RepositoryDialects の順）</summary>
    public IEnumerable<Option> Options
    {
        get
        {
            foreach (var (_, option) in _stringFlags)
            {
                yield return option;
            }

            foreach (var (_, option) in _boolFlags)
            {
                yield return option;
            }

            yield return RepositoryDialects;
        }
    }

    /// <summary>
    /// CLI で指定された各フラグの値だけを設定 JSON へ上書きする（文字列は空白なら無視・bool は値ありのみ）。
    /// RepositoryDialects の特例は <see cref="GenerationConfigLoader"/> の後処理が扱うためここでは触らない。
    /// </summary>
    public void ApplyOverrides(ParseResult parseResult, JsonObject node)
    {
        foreach (var (key, option) in _stringFlags)
        {
            var value = parseResult.GetValue(option);

            if (!string.IsNullOrWhiteSpace(value))
            {
                GenerationConfigLoader.SetNodeValue(node, key, JsonValue.Create(value));
            }
        }

        foreach (var (key, option) in _boolFlags)
        {
            var value = parseResult.GetValue(option);

            if (value.HasValue)
            {
                GenerationConfigLoader.SetNodeValue(node, key, JsonValue.Create(value.Value));
            }
        }
    }

    /// <summary>文字列フラグを追加する</summary>
    private void AddString(string key, string flag, string description) =>
        _stringFlags.Add((key, new Option<string?>(flag) { Description = description }));

    /// <summary>三値 bool フラグを追加する</summary>
    private void AddBool(string key, string flag, string description) =>
        _boolFlags.Add((key, BuildBoolFlag(flag, description)));

    /// <summary>
    /// 三値 bool フラグ（<c>--flag</c> 単独＝true / <c>--flag false</c>＝false / 未指定＝null）の Option を作る。
    /// </summary>
    private static Option<bool?> BuildBoolFlag(string flag, string description) =>
        new(flag)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            CustomParser = ParseNullableBool,
        };

    /// <summary>
    /// 三値 bool フラグの値を解釈する。値トークンが無ければ true（<c>--flag</c> 単独）、あれば <c>true</c>/<c>false</c> を採る。
    /// フラグ自体が未指定のときはこのパーサは呼ばれず、既定 <c>null</c>（＝設定ファイルの値を使う）になる。
    /// </summary>
    private static bool? ParseNullableBool(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return true;
        }

        var token = result.Tokens[0].Value;

        if (bool.TryParse(token, out var value))
        {
            return value;
        }

        result.AddError(string.Format(Strings.Cli_InvalidBooleanFlag, token));
        return null;
    }
}
