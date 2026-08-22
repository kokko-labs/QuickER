using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// rowversion 列 × マルチターゲット（<c>RepositoryDialects=["sqlserver","sqlite"]</c>）の生成を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 同じ <c>rowversion</c> / <c>timestamp</c> の表記を、SQL Server の型マッパは行バージョン（<c>byte[]</c>）へ、
/// SQLite の型マッパは日時（<c>DateTime</c>）へ解決する。統一規則が無いと共有 Entity の型不一致で生成自体が
/// 診断エラーになり、サーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成が組めない。
/// </para>
/// <para>
/// ここで守るのは (1) 行バージョンの解決へ統一されること (2) 非対称を Info 診断で通知すること
/// (3) SQL Server 実装だけが書き込み除外＋版ガードを持ち、SQLite 実装は通常列として書き込むこと
/// (4) 単一方言 sqlite（<c>timestamp</c>＝日時）の解釈が変わらないこと。
/// </para>
/// </remarks>
public sealed class MultiTargetRowVersionGenerationTests
{
    /// <summary>マルチターゲット（sqlserver / sqlite）で rowversion フィクスチャ図を生成する</summary>
    private static CodeGenerationResult GenerateMultiTarget()
    {
        var diagram = MultiTargetRowVersionFixtureDefinition.Build();
        var (primary, byDialect) = MultiTargetRowVersionFixtureDefinition.ResolveColumnTypes(
            diagram
        );

        return new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            MultiTargetRowVersionFixtureDefinition.Options
        );
    }

    /// <summary>
    /// 版列の綴りを非推奨別名の <c>timestamp</c> にした図を返す（フィクスチャは代表表記の <c>rowversion</c>）。
    /// </summary>
    /// <remarks>
    /// 綴りによって SQLite 側の解決が変わる（<c>timestamp</c> → <c>DateTime</c>＝日時の別名 /
    /// <c>rowversion</c> → <c>string</c>＝未知の型）ため、両方の綴りを別々に押さえる。
    /// </remarks>
    private static ErDiagram BuildTimestampSpellingDiagram()
    {
        var diagram = MultiTargetRowVersionFixtureDefinition.Build();
        diagram.Entities[0].Columns[2].DataType = "timestamp";

        return diagram;
    }

    /// <summary>生成物のうち、指定 namespace のブロック（次の namespace 宣言まで）を取り出す</summary>
    private static string DialectBlock(string content, string namespaceName)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line == $"namespace {namespaceName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"{namespaceName} のブロックが出力されるべき");

        var end = Array.FindIndex(
            lines,
            start + 1,
            line => line.StartsWith("namespace ", StringComparison.Ordinal)
        );

        return string.Join("\n", lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact(
        DisplayName = "rowversion 列を持つ図のマルチターゲット生成がエラーにならず共有 Entity は byte[] になる"
    )]
    public void Generate_RowVersionMultiTarget_UnifiesToByteArrayWithoutError()
    {
        var result = GenerateMultiTarget();

        result
            .HasErrors.Should()
            .BeFalse(
                "行バージョン列は統一先が一意に決まるため、方言間の型の食い違いをエラーにしてはならない"
            );

        var content = result.Files.Single().Content;
        content
            .Should()
            .Contain("public byte[] RowVer { get; set; }")
            .And.Contain("[StoreGeneratedColumn]");
        content
            .Should()
            .NotContain("public DateTime RowVer", "sqlite 側の解決（日時）へ倒れてはならない");
    }

    [Fact(DisplayName = "rowversion 列のマルチターゲット統一は Info 診断で通知される")]
    public void Generate_RowVersionMultiTarget_ReportsInfoDiagnostic()
    {
        var result = GenerateMultiTarget();

        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                // 表示言語に依存しないトークン（対象列・行バージョンとして解決した方言名）で確認する
                && d.Message.Contains("sync_items.row_ver")
                && d.Message.Contains("sqlserver")
            );
    }

    [Fact(
        DisplayName = "行バージョン列のない図ではマルチターゲットでも rowversion の Info 診断は出ない"
    )]
    public void Generate_NoRowVersionColumn_NoInfoDiagnostic()
    {
        var diagram =
            Tests.GeneratedMultiTargetFixture.MultiTargetPortableFixtureDefinition.Build();
        var (primary, byDialect) =
            Tests.GeneratedMultiTargetFixture.MultiTargetPortableFixtureDefinition.ResolveColumnTypes(
                diagram
            );

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            Tests.GeneratedMultiTargetFixture.MultiTargetPortableFixtureDefinition.Options
        );

        result
            .Diagnostics.Should()
            .NotContain(d => d.Message.Contains("row_ver"), "統一した列が無ければ何も通知しない");
    }

    [Fact(
        DisplayName = "SQL Server 実装は rowversion 列を書き込みから外し版ガード SQL を持つ／SQLite 実装は通常列として書き込む"
    )]
    public void Generate_RowVersionMultiTarget_DialectAsymmetryInMetadata()
    {
        var content = GenerateMultiTarget().Files.Single().Content;

        var sqlServer = DialectBlock(
            content,
            MultiTargetRowVersionFixtureDefinition.NamespaceName + ".Repositories.SqlServer"
        );
        var sqlite = DialectBlock(
            content,
            MultiTargetRowVersionFixtureDefinition.NamespaceName + ".Repositories.Sqlite"
        );

        // SQL Server 側: store-generated 列を INSERT / UPDATE の対象から外し、版ガード付き SQL を組み立てる
        sqlServer
            .Should()
            .Contain(
                "columns.Where(property => !storeGeneratedColumns.Contains(property)).ToList()"
            )
            .And.Contain("@originalRowVersion")
            .And.Contain("UpdateVersionedSql");

        // SQLite 側: 除外なし（通常列として INSERT / UPDATE が書き込む）・版ガード SQL は存在しない
        sqlite.Should().Contain("var insertProperties = columns;");
        sqlite
            .Should()
            .NotContain(
                "!storeGeneratedColumns.Contains(property)",
                "SQLite では書き込み除外を行わない"
            );
        sqlite.Should().NotContain("@originalRowVersion", "SQLite 側に版ガードは存在しない");
    }

    [Fact(
        DisplayName = "図の方言が sqlite でも sqlserver がターゲットなら timestamp 列は byte[] へ統一される"
    )]
    public void Generate_SqliteDiagramWithSqlServerTarget_UnifiesTimestampToRowVersion()
    {
        // 図の方言＝sqlite（主辞書は timestamp を DateTime として解決する）。
        // 非推奨別名 timestamp は SQL Server では行バージョンだが SQLite では日時（datetime2）の別名で、
        // DB 取込で最も踏みやすい食い違いのため、この綴りで固定する（フィクスチャ側は代表表記 rowversion）
        var diagram = BuildTimestampSpellingDiagram();
        var primary = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);
        var rowVerColumnId = diagram.Entities[0].Columns[2].Id;
        primary[rowVerColumnId]
            .TypeName.Should()
            .Be("DateTime", "前提: sqlite マッパは timestamp を日時として解決する");

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            ["sqlite"] = primary,
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Mixed",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Files.Single()
            .Content.Should()
            .Contain("public byte[] RowVer { get; set; }")
            .And.Contain("[StoreGeneratedColumn]");
    }

    /// <summary>
    /// 実運用の入口（<see cref="DiagramCodeGenerator"/>＝GUI / CLI が通るファサード）でも、SQL Server 図の
    /// <c>timestamp</c> 列 × <c>RepositoryDialects=["sqlserver","sqlite"]</c> がエラーなく通ることを検証する。
    /// </summary>
    /// <remarks>
    /// 報告された再現ケースそのもの。ファサードは主辞書へ中立トークンを付加してから生成へ渡すため、
    /// トークン付加を挟んでも統一と Info 診断が成立することまで一度に押さえる。
    /// </remarks>
    [Fact(
        DisplayName = "実生成経路（DiagramCodeGenerator）でも timestamp × マルチターゲットがエラーなく通り Info 診断が出る"
    )]
    public void DiagramCodeGenerator_TimestampMultiTarget_SucceedsWithInfo()
    {
        var diagram = BuildTimestampSpellingDiagram();
        var sqlServer = new SqlServerProvider();
        var sqlite = new SqliteProvider();

        var result = DiagramCodeGenerator.Generate(
            sqlServer.TypeMapper,
            sqlServer.TypeCatalog,
            new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase)
            {
                ["sqlserver"] = sqlServer.TypeMapper,
                ["sqlite"] = sqlite.TypeMapper,
            },
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Hybrid",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Info
                && d.Message.Contains("sync_items.row_ver")
            );
        result.Files.Single().Content.Should().Contain("public byte[] RowVer { get; set; }");
    }

    [Fact(DisplayName = "単一方言 sqlite 生成では timestamp は日時で store-generated にならない")]
    public void Generate_SqliteOnly_TimestampStaysDateTime()
    {
        var diagram = BuildTimestampSpellingDiagram();
        var primary = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["sqlite"] = primary,
            },
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.LocalOnly",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
            }
        );

        result.HasErrors.Should().BeFalse();

        var content = result.Files.Single().Content.ReplaceLineEndings("\n");
        content.Should().Contain("public DateTime RowVer { get; set; }");
        // 属性の「定義」は常に出力されるため、「付与」（プロパティ直前の行）が無いことで確認する
        content
            .Should()
            .NotContain(
                "[StoreGeneratedColumn]\n    public",
                "単一方言 sqlite の解釈（日時の通常列）は不変"
            );
        result
            .Diagnostics.Should()
            .NotContain(d => d.Message.Contains("row_ver"), "統一していないので通知もしない");
    }
}
