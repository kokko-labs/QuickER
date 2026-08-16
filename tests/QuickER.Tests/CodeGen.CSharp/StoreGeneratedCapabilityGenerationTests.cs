using System;
using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// store-generated 列（<c>rowversion</c>）まわりの出し分けが、方言名の比較ではなく能力フラグ
/// （<c>dialect_assigns_store_generated</c> / <c>store_generated_dialects_differ</c>）で決まることを、
/// 生成テキストの両アームを名指しして固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <c>EntitySaveMetadata</c> は方言エンジン・インメモリ・EF Core の 3 スコープへ出力されるが、
/// 方言を持たない後 2 者では <c>repository_dialect</c> が単に <c>RepositoryDialects[0]</c> になる。
/// 旧実装はその値で <c>RowVersionProperty</c> の XmlDoc を出し分けていたため、
/// <c>RepositoryDialects=["sqlite","sqlserver"]</c> のときだけインメモリ基盤の doc が
/// 「版ガードを行わない」という誤りに転んだ（改善報告書 B-1）。
/// </para>
/// <para>
/// ドリフト検知は「変化に気づく」仕掛けであって「変化が正しい」ことは保証しないため、
/// ここでは再生成に依存しない生成テキストで両アームを固定する（lessons.md の T-5 規約）。
/// </para>
/// </remarks>
public class StoreGeneratedCapabilityGenerationTests
{
    /// <summary>SQL Server 方言エンジンの doc（DB が採番する＝並行トークン）</summary>
    private const string AssigningDoc =
        "/// <summary>Gets the rowversion (concurrency token) property, or <c>null</c> when the table has no such column.</summary>";

    /// <summary>採番しない方言エンジンの doc に現れる断片</summary>
    private const string NonAssigningDoc = "runs no version guard";

    /// <summary>インメモリ基盤の doc に現れる断片</summary>
    private const string InMemoryDoc =
        "The in-memory store stands in for the database that would assign the value";

    /// <summary>EF Core 基盤の doc に現れる断片</summary>
    private const string EfCoreDoc = "EF Core runs the version guard itself";

    /// <summary>マルチターゲットのときだけ契約の <c>mode</c> doc へ足る 1 文</summary>
    private const string MixedEngineNote = "Not every engine behind this contract honours it";

    /// <summary>マルチターゲットのときだけ <c>ConcurrencyMode</c> enum へ足る remarks の断片</summary>
    private const string MixedEngineEnumNote =
        "Not every engine generated from this diagram honours it";

    /// <summary>rowversion 列を 1 本持つ最小の図（SQL Server 表記）</summary>
    private static ErDiagram Diagram()
    {
        var entity = new Entity
        {
            Id = new Guid("c1000000-0000-0000-0000-000000000001"),
            TableName = "sync_items",
            Columns =
            {
                new Column
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000002"),
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = new Guid("c1000000-0000-0000-0000-000000000003"),
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlserver", Entities = { entity } };
    }

    /// <summary>指定オプションで生成し、ファイル名→内容の辞書を返す（主辞書は SQL Server 解決）</summary>
    private static IReadOnlyDictionary<string, string> Generate(CodeGenerationOptions options)
    {
        var diagram = Diagram();
        var primary = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = primary,
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        return result.Files.ToDictionary(file => file.FileName, file => file.Content);
    }

    /// <summary>方言指定を差し替えた分割生成オプション（インメモリ併用）を作る</summary>
    private static CodeGenerationOptions SplitInMemoryOptions(params string[] dialects) =>
        new()
        {
            RootNamespace = "Sample.Hybrid",
            SplitFilesByCategory = true,
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
            RepositoryDialects = dialects,
        };

    /// <summary>
    /// インメモリ基盤の <c>RowVersionProperty</c> doc が、方言の指定順に依らず自分の実挙動
    /// （擬似版の採番＋Optimistic 検証）を述べることを検証する。
    /// </summary>
    /// <remarks>
    /// 旧実装（<c>repository_dialect == "sqlserver"</c> を <c>repositories</c> で包まない出し分け）では、
    /// <c>["sqlite","sqlserver"]</c> の順のときだけ <c>Runtime.InMemory.g.cs</c> が
    /// 「ただの列・版ガードなし」の doc になった。両方の順序を名指しで固定する。
    /// </remarks>
    [Theory(
        DisplayName = "InMemory 基盤の rowversion doc は方言の指定順に依らず擬似版採番＋版ガードを述べる"
    )]
    [InlineData("sqlite", "sqlserver")]
    [InlineData("sqlserver", "sqlite")]
    public void InMemoryRuntime_RowVersionDoc_IsIndependentOfDialectOrder(
        string first,
        string second
    )
    {
        var files = Generate(SplitInMemoryOptions(first, second));

        var inMemory = files["Runtime.InMemory.g.cs"];
        inMemory.Should().Contain(InMemoryDoc);
        inMemory
            .Should()
            .NotContain(
                NonAssigningDoc,
                "インメモリ実装は擬似版を採番し Optimistic 検証も行うため「版ガードなし」は誤り"
            );
        inMemory.Should().NotContain(EfCoreDoc, "インメモリ基盤へ EF Core の説明が出てはならない");
    }

    /// <summary>方言エンジンの固定部は、それぞれ自分の store-generated 意味論を述べる</summary>
    [Fact(
        DisplayName = "方言エンジン固定部の rowversion doc は SQL Server＝並行トークン／SQLite＝通常列のまま"
    )]
    public void DialectRuntimeFiles_RowVersionDoc_DescribeTheirOwnSemantics()
    {
        var files = Generate(SplitInMemoryOptions("sqlite", "sqlserver"));

        var sqlServer = files["Runtime.SqlServer.g.cs"];
        sqlServer.Should().Contain(AssigningDoc);
        sqlServer.Should().NotContain(NonAssigningDoc);
        // 挙動側（書き込み除外）も同じ能力フラグで分岐している
        sqlServer
            .Should()
            .Contain(
                "columns.Where(property => !storeGeneratedColumns.Contains(property)).ToList()"
            );

        var sqlite = files["Runtime.Sqlite.g.cs"];
        sqlite.Should().Contain(NonAssigningDoc);
        sqlite.Should().Contain("var insertProperties = columns;");
    }

    /// <summary>EF Core 基盤の固定部は EF Core 自身が版ガードを行うことを述べる</summary>
    [Fact(DisplayName = "EF Core 基盤の rowversion doc は EF Core 自身の版ガードを述べる")]
    public void EfCoreRuntime_RowVersionDoc_DescribesEfCoreGuard()
    {
        var files = Generate(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.EfOnly",
                SplitFilesByCategory = true,
                GenerateRepositories = false,
                GenerateEfCore = true,
            }
        );

        var efCore = files["Runtime.EntityFrameworkCore.g.cs"];
        efCore.Should().Contain(EfCoreDoc);
        efCore.Should().NotContain(NonAssigningDoc);
        efCore.Should().NotContain(InMemoryDoc);
    }

    /// <summary>
    /// マルチターゲットのときだけ、共有契約の <c>mode</c> doc へ「値を採番しないエンジンは無視する」旨が足ることを検証する。
    /// </summary>
    /// <remarks>
    /// 契約は 1 回しか出力されないため実装ごとの doc では補えない（改善報告書 B-5）。
    /// 単一方言の出力はバイト不変でなければならないので、両アームを名指しで固定する。
    /// </remarks>
    [Fact(DisplayName = "マルチターゲットの契約は mode を無視するエンジンがある旨を doc へ載せる")]
    public void Contract_MultiTarget_DocumentsThatSomeEngineIgnoresMode()
    {
        var content = Generate(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Hybrid",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        )
            .Values.Single();

        // UpdateAsync / SaveAsync（単一・複数）の 3 箇所
        Occurrences(content, MixedEngineNote).Should().Be(3);
        content.Should().Contain(MixedEngineEnumNote);
    }

    /// <summary>単一方言の契約 doc は従来のまま（マルチターゲット向けの 1 文が入らない）</summary>
    [Theory(DisplayName = "単一方言の契約 mode doc はマルチターゲット向けの注記を持たない")]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public void Contract_SingleDialect_KeepsModeDocUnqualified(string dialect)
    {
        var content = Generate(
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Single",
                GenerateRepositories = true,
                RepositoryDialects = [dialect],
            }
        )
            .Values.Single();

        content
            .Should()
            .Contain(
                "How a concurrent modification is handled when the table has a rowversion column (no effect otherwise). An undefined value throws"
            );
        content.Should().NotContain(MixedEngineNote);
        content.Should().NotContain(MixedEngineEnumNote);
    }

    /// <summary>部分文字列の出現回数を数える</summary>
    private static int Occurrences(string content, string value)
    {
        var count = 0;

        for (
            var index = content.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = content.IndexOf(value, index + value.Length, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }
}
