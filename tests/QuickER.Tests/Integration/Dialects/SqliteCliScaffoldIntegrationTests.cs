using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Tests.Integration;
using CliStrings = QuickER.Cli.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// D: CLI <c>scaffold</c> が実 DB から取り込むときの警告表示（複合外部キーの列対応喪失）を検証する統合テスト。
/// </summary>
/// <remarks>
/// 複合外部キーは実 DB から取り込まないと再現できないため、Docker 不要な SQLite 実ファイル DB を用いる
/// （CI でも常時実行される）。出力は <see cref="CliApp.InvokeAsync(string[], TextWriter, TextWriter)"/> の
/// TextWriter 注入版で捕捉し、<c>Console</c> は差し替えない。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteCliScaffoldIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>複合外部キーを持つ実 DB を用意する DDL（生成器は複合 FK を出力できないため手書き）</summary>
    private const string CompositeForeignKeyDdl =
        @"
CREATE TABLE ""Parent"" (
    ""A"" INTEGER NOT NULL,
    ""B"" INTEGER NOT NULL,
    PRIMARY KEY (""A"", ""B"")
);
CREATE TABLE ""Child"" (
    ""Id"" INTEGER NOT NULL PRIMARY KEY,
    ""AId"" INTEGER NOT NULL,
    ""BId"" INTEGER NOT NULL,
    CONSTRAINT ""FK_Child_Parent"" FOREIGN KEY (""AId"", ""BId"") REFERENCES ""Parent"" (""A"", ""B"")
);";

    /// <summary>単一列の外部キーだけを持つ実 DB を用意する DDL（警告が出ないことの対照）</summary>
    private const string SingleColumnForeignKeyDdl =
        @"
CREATE TABLE ""Parent"" (
    ""A"" INTEGER NOT NULL PRIMARY KEY
);
CREATE TABLE ""Child"" (
    ""Id"" INTEGER NOT NULL PRIMARY KEY,
    ""AId"" INTEGER NOT NULL,
    CONSTRAINT ""FK_Child_Parent"" FOREIGN KEY (""AId"") REFERENCES ""Parent"" (""A"")
);";

    /// <summary>複合 FK を含む DB の scaffold は、警告を stderr へ出しつつ終了コード 0 で生成を完遂する</summary>
    [Fact(
        DisplayName = "[Integration] D: scaffold は複合 FK 警告を stderr へ出して exit 0 で続行する"
    )]
    public async Task Scaffold_CompositeForeignKey_WarnsOnStderrAndSucceeds()
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(CompositeForeignKeyDdl, Ct);

        var outDir = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "out");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.InvokeAsync(
            [
                "scaffold",
                "--connection",
                db.ReadOnlyConnectionString,
                "--out",
                outDir,
                "--provider",
                "sqlite",
                "--root-namespace",
                "Test.Ns",
            ],
            stdout,
            stderr
        );

        // 警告のみで継続する（生成は通常どおり完了する）
        exit.Should().Be(0);
        Directory.GetFiles(outDir, "*.g.cs").Should().NotBeEmpty();

        // SQLite は制約名を保持しないため、テーブル名・列名の側で警告内容を確認する
        var warnings = stderr.ToString();
        warnings.Should().Contain("Child");
        warnings.Should().Contain("AId, BId");
        warnings.Should().Contain("Parent");
        warnings.Should().Contain("A, B");

        // 文言は resx 由来（書式の骨格が変わったら気付けるよう固定文の一部で照合する）
        var expectedPrefix = CliStrings.Cli_CompositeForeignKeyWarning.Split("{0}")[0];
        warnings.Should().Contain(expectedPrefix);
    }

    /// <summary>単一列 FK だけの DB では警告を出さない（従来と完全に同一の出力）</summary>
    [Fact(DisplayName = "[Integration] D: scaffold は単一列 FK では警告を出さない")]
    public async Task Scaffold_SingleColumnForeignKey_WritesNoWarning()
    {
        using var db = SqliteTempDatabase.Create();
        await db.ApplyDdlAsync(SingleColumnForeignKeyDdl, Ct);

        var outDir = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "out");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.InvokeAsync(
            [
                "scaffold",
                "--connection",
                db.ReadOnlyConnectionString,
                "--out",
                outDir,
                "--provider",
                "sqlite",
                "--root-namespace",
                "Test.Ns",
            ],
            stdout,
            stderr
        );

        exit.Should().Be(0);

        // 複合 FK 警告は 1 件も出ない（他の診断の有無に左右されないよう、当該文言だけを見る）
        var expectedPrefix = CliStrings.Cli_CompositeForeignKeyWarning.Split("{0}")[0];
        stderr.ToString().Should().NotContain(expectedPrefix);
    }
}
