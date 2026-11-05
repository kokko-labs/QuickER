using QuickER.CodeGen.CSharp;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>網羅の配列の 1 行（次元表の全因子の水準）</summary>
/// <param name="Id">行の Id（<c>m000</c> 普段・<c>n000</c> 夜間）</param>
/// <param name="Levels">因子 → 水準</param>
internal sealed record CodegenRow(string Id, IReadOnlyDictionary<string, string> Levels)
{
    /// <summary>因子の水準</summary>
    public string this[string factor] => Levels[factor];

    /// <summary>真偽の因子が有効か</summary>
    public bool IsOn(string factor) => Levels[factor] == On;

    /// <summary>入口</summary>
    public CodegenEntrance Entrance =>
        CodegenEntrances.Parse(Levels[CodegenConfigDimensions.Entrance]);

    /// <summary>図の形</summary>
    public ShapeLevels Shape =>
        new(
            Levels[Key],
            Levels[Relation],
            Levels[DbGenerated],
            Levels[Binary],
            Levels[Unique],
            Levels[Query],
            Levels[TypeDialect]
        );

    /// <summary>行の生成オプション（入口へ渡す前の、利用者が指定したもの）</summary>
    /// <remarks>
    /// サービスは契約を含意するので、オプションでは両方を立てる。API リファレンスの言語は水準で切り替え、出さない行は既定の英語のまま。
    /// 層別は分割を含意するが、オプションでは <see cref="CodeGenerationOptions.LayeredOutput"/> だけを立てる（含意の解釈は生成側の担当）。
    /// ルート名前空間は固定の値、名前空間の欄・層フォルダ・サブフォルダは既定のまま。
    /// </remarks>
    public CodeGenerationOptions Options()
    {
        var dbAccess = Levels[DbAccess];
        var remote = Levels[Remote];
        var apiDocs = Levels[ApiDocs];

        return new CodeGenerationOptions
        {
            RootNamespace = CodegenRunner.RootNamespace,
            GenerateEditModels = IsOn(EditModels),
            GenerateMappers = IsOn(Mappers),
            GenerateRepositories = dbAccess is DbAccessLevels.Quicker or DbAccessLevels.Both,
            GenerateEfCoreRepositories = dbAccess is DbAccessLevels.EfCore or DbAccessLevels.Both,
            RepositoryDialects = DialectsOf(Levels[CodegenConfigDimensions.Dialects]),
            GenerateInMemoryRepositories = IsOn(InMemory),
            GenerateRemoteContracts = remote != RemoteLevels.None,
            GenerateRemoteServices = remote == RemoteLevels.Services,
            GenerateSyncSupport = IsOn(Sync),
            IncludeDataAnnotations = IsOn(Annotations),
            IncludeJsonIgnoreOnParentNavigation = IsOn(JsonIgnore),
            GenerateValueObjects = IsOn(ValueObjects),
            UseGuidKeyForStringPrimaryKey = IsOn(GuidKey),
            SplitFilesByCategory = Levels[Output] == OutputLevels.Split,
            LayeredOutput = Levels[Output] == OutputLevels.Layered,
            UseRuntimePackages = IsOn(Packages),
            GenerateApiDocs = apiDocs != ApiDocsLevels.Off,
            ApiDocsLanguage = apiDocs switch
            {
                ApiDocsLevels.Japanese => ApiDocsLanguage.Japanese,
                ApiDocsLevels.Both => ApiDocsLanguage.Both,
                _ => ApiDocsLanguage.English,
            },
            ExcludeUnboundedBinaryColumns = IsOn(ExcludeBinary),
        };
    }

    /// <summary>行の水準を 1 行の文字列にする（失敗の文に添える）</summary>
    public string Describe() =>
        $"{Id}: " + string.Join(", ", Levels.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>方言の水準を方言名の並びにする</summary>
    public static IReadOnlyList<string> DialectsOf(string level) =>
        level.Split('+', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>図の形の水準（<see cref="ShapeDiagram"/> の入力）</summary>
/// <param name="Key">主の表のキーの形</param>
/// <param name="Relation">関係の形</param>
/// <param name="DbGenerated">DB が値を作る列</param>
/// <param name="Binary">バイナリ列</param>
/// <param name="Unique">一意制約</param>
/// <param name="Query">名前付きクエリ</param>
/// <param name="TypeDialect">図の方言</param>
internal sealed record ShapeLevels(
    string Key,
    string Relation,
    string DbGenerated,
    string Binary,
    string Unique,
    string Query,
    string TypeDialect
);
