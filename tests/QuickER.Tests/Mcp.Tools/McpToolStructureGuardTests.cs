using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Dialects;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// MCP ツールの「同じ知識を 1 か所に置く」ことを機械で見張る（方言の一覧）
/// </summary>
/// <remarks>
/// ソースの走査は規則の表へ移した：図の読み込みの警告文は <c>SourceRuleTests</c> の規則 <c>newer-format-warning-text</c>、
/// ツール引数の読み取りとリレーションの種別の解釈は規則 <c>tool-argument-parsing-copies</c>・<c>tool-argument-parsing-owners</c>。
/// ここに残すのは、実行時のツール定義・型・値の構造の検査。
/// </remarks>
public sealed class McpToolStructureGuardTests
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownCaseLedger Ledger = new("McpToolStructureGuardTests", []);

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

    /// <summary>ツール定義の引数の enum を取り出す</summary>
    private static List<string> EnumOf(ToolDefinition definition, string property) =>
        ToolArgumentFuzz.SchemaOf(definition.InputSchema)["properties"]![property]!["enum"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
}
