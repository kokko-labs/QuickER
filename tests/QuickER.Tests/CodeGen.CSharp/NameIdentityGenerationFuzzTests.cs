using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// C# コード生成（<see cref="DiagramCodeGenerator"/>）へ名前の総当たり（<see cref="NameFuzz"/>）を流し、図の中の重複の
/// 判定が <see cref="NameIdentityRule"/> と同じ答えになることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図は手で編集した JSON・古い版で作った図のように、ツールの検証を通らずに名前の組を持ち得る。規則が「同じ名前」と言う
/// 組を持つ図は生成時エラーで名指しして断り、「別の名前」と言う組は（1 つずつなら生成できる名前なら）2 つでも生成できることを
/// 表明する。生成コードがコンパイルできるかは別のテスト（名前の総当たりの性質 C）が見る。
/// </para>
/// <para>結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。</para>
/// </remarks>
public sealed class NameIdentityGenerationFuzzTests
{
    [Fact(DisplayName = "C# 生成の重複の判定は名前の規則と同じ答えになる")]
    public void Generation_AgreesWithNameRule()
    {
        var ledger = new FuzzFailureLedger(NameFuzzKnownFailures.All);

        NameFuzzRunner.Run(
            ledger,
            "codegen:table",
            NameKind.Table,
            name => Generate(Tables(name)).Accepted,
            pair => Generate(Tables(pair.First, pair.Second)).Outcome,
            EntityClassName
        );
        NameFuzzRunner.Run(
            ledger,
            "codegen:column",
            NameKind.Column,
            name => Generate(Columns(name)).Accepted,
            pair => Generate(Columns(pair.First, pair.Second)).Outcome,
            QueryDefinitionRules.ToMethodName
        );
        NameFuzzRunner.Run(
            ledger,
            "codegen:query_name",
            NameKind.Query,
            name => Generate(WithQueries(q => [q(name)])).Accepted,
            pair => Generate(WithQueries(q => [q(pair.First), q(pair.Second)])).Outcome,
            QueryDefinitionRules.ToMethodName
        );
        NameFuzzRunner.Run(
            ledger,
            "codegen:parameter",
            NameKind.Parameter,
            name => Generate(WithQueries(q => [Parameters(q("Probe"), name)])).Accepted,
            pair =>
                Generate(
                    WithQueries(q => [Parameters(q("Probe"), pair.First, pair.Second)])
                ).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "codegen:field",
            NameKind.ProjectionField,
            name => Generate(WithFields(name)).Accepted,
            pair => Generate(WithFields(pair.First, pair.Second)).Outcome,
            QueryDefinitionRules.ToMethodName
        );

        ledger.AssertMatchesKnown("codegen:");
    }

    /// <summary>テーブル名から生成されるエンティティクラス名（1 つだけの図を生成して読む・同じ名前は覚えておく）</summary>
    private static string? EntityClassName(string tableName)
    {
        lock (EntityClassNames)
        {
            if (!EntityClassNames.TryGetValue(tableName, out var className))
            {
                var result = GenerateResult(Tables(tableName));
                className = result
                    .Files.Select(file => EntityClassPattern.Match(file.Content))
                    .FirstOrDefault(match => match.Success)
                    ?.Groups[1]
                    .Value;
                EntityClassNames[tableName] = className;
            }

            return className;
        }
    }

    private static readonly Dictionary<string, string?> EntityClassNames = new(
        StringComparer.Ordinal
    );

    private static readonly Regex EntityClassPattern = new(
        @"public partial class (\w+Entity)\b",
        RegexOptions.CultureInvariant
    );

    /// <summary>生成時エラーが無いかを返す</summary>
    private static (bool Accepted, PairOutcome Outcome) Generate(ErDiagram diagram)
    {
        var result = GenerateResult(diagram);
        var accepted = !result.HasErrors;
        var detail = string.Join(
            " / ",
            result
                .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error)
                .Select(d => d.Message)
        );

        return (accepted, PairOutcome.Of(accepted, detail));
    }

    private static CodeGenerationResult GenerateResult(ErDiagram diagram)
    {
        var provider = new SqliteProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                GenerateEfCoreRepositories = true,
                GenerateInMemoryRepositories = true,
                GenerateValueObjects = true,
            }
        );
    }

    /// <summary>名前ごとのテーブル（主キー Id だけ）を持つ図</summary>
    private static ErDiagram Tables(params string[] names) =>
        new() { TargetDbms = "sqlite", Entities = names.Select(name => Table(name, [])).ToList() };

    /// <summary>テーブル T に名前ごとの列を持つ図</summary>
    private static ErDiagram Columns(params string[] names) =>
        new()
        {
            TargetDbms = "sqlite",
            Entities =
            [
                Table("T", names.Select(name => new Column { Name = name, DataType = "INTEGER" })),
            ],
        };

    /// <summary>テーブル T(Id, A) と、渡した定義を持つ図</summary>
    private static ErDiagram WithQueries(
        Func<Func<string, QueryDefinition>, IEnumerable<QueryDefinition>> queries
    )
    {
        var entity = Table("T", [new Column { Name = "A", DataType = "INTEGER" }]);

        return new ErDiagram
        {
            TargetDbms = "sqlite",
            Entities = [entity],
            Queries = queries(name => new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = name,
                    Returns = QueryReturnShape.List,
                })
                .ToList(),
        };
    }

    private static QueryDefinition Parameters(QueryDefinition query, params string[] names)
    {
        query.Parameters = names
            .Select(name => new QueryParameter { Name = name, Type = "int32" })
            .ToList();

        return query;
    }

    /// <summary>テーブル T(Id, A) と、名前ごとのフィールドを持つ射影のクエリの図</summary>
    private static ErDiagram WithFields(params string[] names)
    {
        var diagram = WithQueries(q => [q("Probe")]);
        var query = diagram.Queries[0];
        var source = diagram.Entities[0].Columns[1];
        query.Returns = QueryReturnShape.Projection;
        query.ResultTypeName = "ProbeRow";
        query.Fields = names
            .Select(name => new ProjectionField { Name = name, SourceColumnId = source.Id })
            .ToList();

        return diagram;
    }

    private static Entity Table(string name, IEnumerable<Column> columns) =>
        new()
        {
            TableName = name,
            Columns =
            [
                new Column
                {
                    Name = "Id",
                    DataType = "INTEGER",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                .. columns,
            ],
        };
}
