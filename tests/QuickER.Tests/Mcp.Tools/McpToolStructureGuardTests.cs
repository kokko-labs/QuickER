using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Dialects;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP ツールの「同じ知識を 1 か所に置く」ことを機械で見張る（方言の一覧・図の読み込みの決まり文句。
/// ツール引数の読み取りとリレーションの種別の解釈は <see cref="QuickER.Tests.Mcp.ToolArgumentParsingGuardTests"/>）
/// </summary>
public sealed class McpToolStructureGuardTests
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownCaseLedger Ledger = new("McpToolStructureGuardTests", []);

    /// <summary>リポジトリのルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    [Fact(
        DisplayName = "ツールの説明に載る方言の一覧（create_diagram・generate_csharp・generate_ddl の enum と set_query の sql の説明）は、正本の一覧と一致する"
    )]
    public void DialectListsInToolSchemas_MatchTheCanonicalList()
    {
        var canonical = DialectNames.All.ToList();

        EnumOf(DocumentErDiagramToolSet.CreateDiagramDefinition, "target_dbms")
            .Should()
            .Equal(canonical);
        EnumOf(CodeGenToolSet.GenerateCSharpDefinition, "provider").Should().Equal(canonical);
        EnumOf(CodeGenToolSet.GenerateDdlDefinition, "provider").Should().Equal(canonical);

        var setQuery = ErDiagramToolCatalog
            .GetDefinitions()
            .Single(tool => tool.Name == "set_query");
        var sqlDescription = ToolArgumentFuzz.SchemaOf(setQuery.InputSchema)["properties"]!["sql"]![
            "description"
        ]!.GetValue<string>();

        foreach (var dialect in canonical)
        {
            sqlDescription.Should().Contain(dialect);
        }
    }

    [Fact(DisplayName = "方言の一覧の正本は、外から書き換えられない")]
    public void CanonicalDialectList_IsNotAWritableArray() =>
        Ledger.Run(
            "CanonicalDialectList_IsNotAWritableArray",
            CanonicalDialectList_IsNotAWritableArrayCore
        );

    private void CanonicalDialectList_IsNotAWritableArrayCore()
    {
        DialectNames
            .All.Should()
            .NotBeAssignableTo<string[]>(
                "配列を公開すると、どこかの書き換えで受け付ける方言がプロセス全体で黙って変わる"
            );
        ((ICollection<string>)DialectNames.All).IsReadOnly.Should().BeTrue();
    }

    [Fact(DisplayName = "方言の一覧の正本は、各プロバイダの名前と一致する")]
    public void CanonicalDialectList_MatchesProviderNames() =>
        DialectNames
            .All.Should()
            .BeEquivalentTo([
                QuickER.Provider.SqlServer.SqlServerProvider.ProviderName,
                QuickER.Provider.PostgreSql.PostgreSqlProvider.ProviderName,
                QuickER.Provider.MySql.MySqlProvider.ProviderName,
                QuickER.Provider.Oracle.OracleProvider.ProviderName,
                QuickER.Provider.Sqlite.SqliteProvider.ProviderName,
            ]);

    [Fact(
        DisplayName = "図を読むツールの「新しい版で保存された」の警告文は、ソースに 1 か所だけ書く（ER 図操作ツールとコード生成ツールで共有）"
    )]
    public void NewerFormatWarning_IsWrittenOnce() =>
        Ledger.Run("NewerFormatWarning_IsWrittenOnce", NewerFormatWarning_IsWrittenOnceCore);

    private void NewerFormatWarning_IsWrittenOnceCore()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !TestSupport.SourceFiles.IsBuildOutput(
                    Path.GetRelativePath(Path.Combine(Root, "src"), path)
                )
            );
        var hits = sources
            .SelectMany(path =>
                Regex
                    .Matches(
                        File.ReadAllText(path),
                        Regex.Escape("Warning: this diagram was saved in a newer format")
                    )
                    .Select(_ => Path.GetFileName(path))
            )
            .ToList();

        hits.Should().ContainSingle("文言を直すとき 1 か所で済み、ツールどうしで食い違わない");
    }

    /// <summary>ツール定義の引数の enum を取り出す</summary>
    private static List<string> EnumOf(ToolDefinition definition, string property) =>
        ToolArgumentFuzz.SchemaOf(definition.InputSchema)["properties"]![property]!["enum"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
}
