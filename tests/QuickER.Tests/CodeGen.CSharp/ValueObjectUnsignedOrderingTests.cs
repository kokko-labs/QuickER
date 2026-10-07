using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.MySql;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 符号なし整数（MySQL の unsigned 付き整数が生む ushort / uint / ulong）の値オブジェクトが、
/// 順序付き基底（比較演算子・IComparable）へ派生することを検証する。
/// </summary>
/// <remarks>
/// 派生規則と DSL 条件の型整合検査は <c>ValueObjectHasOrderingOperators</c> を共有する。
/// 符号なしが漏れると VO が比較演算子を持たず、DSL の大小比較がクエリごと生成から外れる。
/// </remarks>
public sealed class ValueObjectUnsignedOrderingTests
{
    /// <summary>主キーの型ごとの表（表名・主キー列名・MySQL の型・C# の内包型・VO クラス名）</summary>
    private static readonly (
        string Table,
        string KeyColumn,
        string DataType,
        string ValueType,
        string ValueObject
    )[] Tables =
    [
        ("shorts", "short_id", "smallint unsigned", "ushort", "ShortIdValue"),
        ("ints", "int_id", "int unsigned", "uint", "IntIdValue"),
        ("longs", "long_id", "bigint unsigned", "ulong", "LongIdValue"),
    ];

    /// <summary>3 つの表と、それぞれの主キーを大小比較する DSL クエリを持つ図</summary>
    private static ErDiagram BuildDiagram()
    {
        var diagram = new ErDiagram();

        foreach (var table in Tables)
        {
            var entity = new Entity
            {
                TableName = table.Table,
                Columns =
                [
                    new Column
                    {
                        Name = table.KeyColumn,
                        DataType = table.DataType,
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                ],
            };
            diagram.Entities.Add(entity);
            diagram.Queries.Add(
                new QueryDefinition
                {
                    EntityId = entity.Id,
                    Name = "GetAboveTen",
                    Returns = QueryReturnShape.List,
                    Condition = $"{table.KeyColumn} > 10",
                }
            );
        }

        return diagram;
    }

    private static CodeGenerationResult Generate()
    {
        var diagram = BuildDiagram();

        return new CSharpCodeGenerationService().Generate(
            diagram,
            MySqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                GenerateInMemoryRepositories = true,
                GenerateRepositories = false,
            }
        );
    }

    [Fact(
        DisplayName = "符号なし整数の値オブジェクトは順序付き基底へ派生し、比較演算子が使え、DSL の大小比較も生成される"
    )]
    public void UnsignedValueObjects_HaveOrderingOperators()
    {
        var result = Generate();

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        result
            .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Warning)
            .Should()
            .BeEmpty("大小比較の DSL がクエリごと生成から外れる警告は出ない");

        var content = string.Join("\n", result.Files.Select(file => file.Content));

        foreach (var table in Tables)
        {
            content
                .Should()
                .Contain($": ValueObjectOrderedBase<{table.ValueObject}, {table.ValueType}>");
        }

        content.Split("GetAboveTenAsync(").Length.Should().BeGreaterThan(Tables.Length);

        // 比較演算子と IComparable を静的に使う検証コードごとコンパイルできる
        var probe = $$"""
            namespace Sample.Domain;

            internal static class UnsignedOrderingProbe
            {
                public static bool Compare() =>
                    ShortIdValue.Create(1) < ShortIdValue.Create(2)
                    && IntIdValue.Create(1u) <= IntIdValue.Create(2u)
                    && LongIdValue.Create(3ul) > LongIdValue.Create(2ul)
                    && ((System.IComparable<IntIdValue>)IntIdValue.Create(1u)).CompareTo(IntIdValue.Create(2u)) < 0;
            }
            """;

        var assembly = GeneratedCodeCompiler.CompileAndLoad(result, "UnsignedOrdering", probe);
        var compare = assembly
            .GetType("Sample.Domain.UnsignedOrderingProbe", throwOnError: true)!
            .GetMethod("Compare")!;

        compare.Invoke(null, null).Should().Be(true);
    }
}
