using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Tests.Integration;
using CodeGenStrings = QuickER.CodeGen.CSharp.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// D: CLI <c>scaffold</c> が実 DB の複合外部キーを扱うときの警告表示を検証する統合テスト。
/// </summary>
/// <remarks>
/// 取込は複合外部キーを劣化させないため、取込側の警告は出ない。警告を出すのは、単一列 FK しか
/// 扱えないコード生成側（ナビゲーション生成）で、当該リレーションだけをスキップする。
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

    /// <summary>複合 FK を含む DB の scaffold は、生成側の警告を stderr へ出しつつ exit 0 で完遂する</summary>
    [Fact(
        DisplayName = "[Integration] D: scaffold は複合 FK の生成側警告を stderr へ出して exit 0 で続行する"
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

        // 複合外部キーを扱えないコード生成側の警告が出る（文言は resx 由来）
        var codeGenWarningPrefix =
            CodeGenStrings.CodeGen_Warning_RelationshipCompositeSkipped.Split("{0}")[0];
        stderr.ToString().Should().Contain(codeGenWarningPrefix);
    }

    /// <summary>単一列 FK だけの DB では複合 FK の警告を出さない（誤爆しないことの対照）</summary>
    [Fact(DisplayName = "[Integration] D: scaffold は単一列 FK では複合 FK 警告を出さない")]
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
        var expectedPrefix = CodeGenStrings.CodeGen_Warning_RelationshipCompositeSkipped.Split(
            "{0}"
        )[0];
        stderr.ToString().Should().NotContain(expectedPrefix);
    }
}
