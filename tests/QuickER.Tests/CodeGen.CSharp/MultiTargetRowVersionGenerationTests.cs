using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
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
/// ここで守るのは、SQL Server 実装だけが書き込み除外＋版ガードを持ち、SQLite 実装は通常列として書き込むこと、と、実生成経路で Repository 非生成なら方言辞書が無視されること。
/// 行バージョンの解決への統一・単一方言 sqlite（<c>timestamp</c>＝日時）の解釈・EF Core 単独で方言辞書が出力を変えないことは、決まった行（<c>CodegenCaseTests</c> の <c>code-rowversion-*</c>・<c>relation-rowversion-*</c>）が持つ。
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

    // rowversion 列のマルチターゲット統一が Info 診断で通知されること、行バージョン列のない図では通知されないことは、
    // 決まった行 diag-rowversion-multitarget-info-names-column-and-owner / diag-rowversion-multitarget-no-info-without-rowversion-column

    // 残す理由（P3-4b）：両方言の実装の書き込み除外の式と版ガード SQL のパラメータ名（式本体・SQL の断片）。版ガード付き SQL の持ち主が
    // SQL Server 実装だけであること（UpdateVersionedSql）は行 code-rowversion-multitarget-dialect-asymmetry-in-metadata へ移した
    [Fact(
        DisplayName = "SQL Server 実装は rowversion 列を書き込みから外し版ガード SQL を持ち、SQLite 実装は持たない"
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
            .Contain("storeGeneratedColumns.Concat(dbAssignedColumns).ToList()")
            .And.Contain("@originalRowVersion");

        // SQLite 側: store-generated の除外なし（通常列として INSERT / UPDATE が書き込む）・版ガード SQL は存在しない
        sqlite.Should().Contain("var readOnlyColumns = dbAssignedColumns;");
        sqlite
            .Should()
            .NotContain(
                "storeGeneratedColumns.Concat(dbAssignedColumns)",
                "SQLite では store-generated 列の書き込み除外を行わない"
            );
        sqlite.Should().NotContain("@originalRowVersion", "SQLite 側に版ガードは存在しない");
    }

    /// <summary>
    /// 実生成経路（<see cref="DiagramCodeGenerator"/>）でも、Repository 非生成なら方言マッパ辞書つきの
    /// 呼び出しが単一方言オーバーロードと同一出力になることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "実生成経路でも Repository 非生成なら方言マッパ辞書つき呼び出しは単一方言と同一出力になる"
    )]
    public void DiagramCodeGenerator_EfCoreOnlyWithDialectMappers_MatchesSingleDialect()
    {
        var diagram = BuildTimestampSpellingDiagram();
        var sqlServer = new SqlServerProvider();
        var sqlite = new SqliteProvider();
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.EfOnly2",
            GenerateEfCoreRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
        };

        var multi = DiagramCodeGenerator.Generate(
            sqlite.TypeMapper,
            sqlite.TypeCatalog,
            new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase)
            {
                ["sqlserver"] = sqlServer.TypeMapper,
                ["sqlite"] = sqlite.TypeMapper,
            },
            diagram,
            options
        );
        var single = DiagramCodeGenerator.Generate(
            sqlite.TypeMapper,
            sqlite.TypeCatalog,
            diagram,
            options
        );

        multi.HasErrors.Should().BeFalse();
        multi.Files.Select(f => f.Content).Should().Equal(single.Files.Select(f => f.Content));
    }
}
