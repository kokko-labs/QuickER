using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Tests.Integration;
using CliStrings = QuickER.Cli.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// CLI <c>scaffold</c> が取込警告を stderr へ出しつつ生成を完遂することを検証する統合テスト。
/// </summary>
/// <remarks>
/// GUI 側（<c>DbImportCommandService</c>）はモーダルの内訳で同じ警告を提示する。両者は同じ
/// 言語中立の <c>SchemaImportWarning</c> を、それぞれ自前の resx で文言化する。
/// 取込警告を実際に生む構成（ドメイン型）は PostgreSQL でしか作れないため、ここはコンテナを使う。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlCliScaffoldIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>ドメイン型の列を含む DB の scaffold は、取込警告を stderr へ出しつつ exit 0 で完遂する</summary>
    [Fact(DisplayName = "[Integration] D: scaffold は取込警告を stderr へ出して exit 0 で続行する")]
    public async Task Scaffold_ImportWarning_WritesToStderrAndSucceeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE DOMAIN email_dom AS varchar(320);
            CREATE TABLE person (id integer PRIMARY KEY, mail email_dom);
            """,
            Ct
        );

        var outDir = Path.Combine(Path.GetTempPath(), $"quicker-scaffold-{Guid.NewGuid():N}");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "scaffold",
                    "--connection",
                    fixture.ConnectionString,
                    "--out",
                    outDir,
                    "--provider",
                    "postgresql",
                    "--root-namespace",
                    "Test.Ns",
                ],
                stdout,
                stderr
            );

            // 警告は取込の成否と無関係（取込自体は成功している）ため、生成はそのまま完遂する
            exit.Should().Be(0);
            Directory.GetFiles(outDir, "*.g.cs").Should().NotBeEmpty();

            var warningPrefix = CliStrings.Cli_ImportWarningDomainTypeFlattened.Split("{0}")[0];
            var text = stderr.ToString();
            text.Should().Contain(warningPrefix);
            text.Should().Contain("email_dom");
            text.Should().Contain("person");
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    /// <summary>警告を生まない DB では取込警告を 1 件も出さない（誤爆しないことの対照）</summary>
    [Fact(DisplayName = "[Integration] D: scaffold は警告の無い取込では何も stderr へ出さない")]
    public async Task Scaffold_NoImportWarning_WritesNothing()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE person (id integer PRIMARY KEY, mail varchar(320));",
            Ct
        );

        var outDir = Path.Combine(Path.GetTempPath(), $"quicker-scaffold-{Guid.NewGuid():N}");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                [
                    "scaffold",
                    "--connection",
                    fixture.ConnectionString,
                    "--out",
                    outDir,
                    "--provider",
                    "postgresql",
                    "--root-namespace",
                    "Test.Ns",
                ],
                stdout,
                stderr
            );

            exit.Should().Be(0);

            var warningPrefix = CliStrings.Cli_ImportWarningDomainTypeFlattened.Split("{0}")[0];
            stderr.ToString().Should().NotContain(warningPrefix);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }
}
