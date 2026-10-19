using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.GeneratedSyncFixture;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 同期の対象の表の主キーが固定長の文字列（nchar(n) / char(n)）なら、生成時の Warning で表と列を名指しすることを検証する。
/// </summary>
/// <remarks>
/// SQL Server は固定長の文字列を宣言の長さまで空白で詰めて返すので、ローカル（SQLite）で作った行のキー
/// （<c>"cd"</c>）は、同期の後にサーバーの空白詰めのキー（<c>"cd        "</c>）へ置き換わり、ローカルに子の行があると
/// 2 つのキーの行が残って収束しない。判定は正規型の固定長の種類で行い、同期を生成しない構成と主キーでない列では出さない。
/// </remarks>
public sealed class SyncFixedLengthKeyWarningTests
{
    private const string KeyColumn = "sync_markers.marker_id";

    [Theory(
        DisplayName = "同期の対象の表の主キーが固定長の文字列なら、生成時の Warning で表と列を名指しする"
    )]
    [InlineData("nchar(10)")]
    [InlineData("char(10)")]
    public void FixedLengthStringPrimaryKey_IsWarned(string dataType)
    {
        var warnings = Warnings(MarkerKeyTyped(dataType), SyncFixtureDefinition.Options);

        warnings.Should().ContainSingle(message => message.Contains(KeyColumn));
    }

    [Theory(DisplayName = "可変長の文字列の主キーでは、固定長の主キーの Warning を出さない")]
    [InlineData("nvarchar(10)")]
    [InlineData("varchar(10)")]
    public void VariableLengthStringPrimaryKey_IsNotWarned(string dataType) =>
        Warnings(MarkerKeyTyped(dataType), SyncFixtureDefinition.Options)
            .Should()
            .NotContain(message => message.Contains(KeyColumn));

    [Fact(DisplayName = "同期を生成しない構成では、固定長の文字列の主キーでも Warning を出さない")]
    public void WithoutSyncSupport_IsNotWarned() =>
        Warnings(
                MarkerKeyTyped("nchar(10)"),
                SyncFixtureDefinition.Options with
                {
                    GenerateSyncSupport = false,
                    GenerateRemoteServices = false,
                }
            )
            .Should()
            .NotContain(message => message.Contains(KeyColumn));

    [Fact(DisplayName = "主キーでない固定長の文字列の列では、Warning を出さない")]
    public void FixedLengthNonKeyColumn_IsNotWarned()
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram
            .Entities.Single(entity => entity.TableName == "sync_markers")
            .Columns.Single(column => column.Name == "code")
            .DataType = "nchar(10)";

        Warnings(diagram, SyncFixtureDefinition.Options)
            .Should()
            .NotContain(message => message.Contains("sync_markers.code"));
    }

    /// <summary>同期のフィクスチャの図で、マーカーの主キーの型だけを差し替える</summary>
    private static ErDiagram MarkerKeyTyped(string dataType)
    {
        var diagram = SyncFixtureDefinition.Build();
        diagram
            .Entities.Single(entity => entity.TableName == "sync_markers")
            .Columns.Single(column => column.IsPrimaryKey)
            .DataType = dataType;
        return diagram;
    }

    /// <summary>生成して Warning の文面を返す（エラーが無いことも確かめる）</summary>
    private static string[] Warnings(ErDiagram diagram, CodeGenerationOptions options)
    {
        // 実際の生成の経路（DiagramCodeGenerator）と同じく、主辞書へ図の方言の型カタログの正規型を付ける
        var (resolved, byDialect) = SyncFixtureDefinition.ResolveColumnTypes(diagram);
        var primary = CanonicalTypeTokenAttacher.Attach(
            resolved,
            diagram,
            new SqlServerTypeCatalog()
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );
        result.HasErrors.Should().BeFalse();

        return result
            .Diagnostics.Where(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
            )
            .Select(diagnostic => diagnostic.Message)
            .ToArray();
    }
}
