using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 図のテーブル・列から決まる生成型名が、ほかのテーブル・列から決まる型名や、生成コードの固定の型名とぶつかる図を、
/// 生成時のエラーで名指しして止めることを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 型名は単数形化と接尾辞（<c>Entity</c>・<c>EditModel</c>・<c>Mapper</c>・<c>Repository</c> など）で作られ、名前が
/// すでに接尾辞で終わっていれば付け足さない。そのため表 <c>order_edit_model</c> は表 <c>order</c> の EditModel と同じ
/// <c>OrderEditModel</c> になり、<c>i_order</c> は <c>order</c> の <c>IOrderRepository</c> を作る。止めないと、診断なしで
/// コンパイルできない出力になる。判定は構成と出力の形に依らない（出し得る名前をすべて見る）ので、非分割・分割・層別の
/// どれでも同じ答えになることを見る。
/// </para>
/// <para>
/// リモートのルート名（エンティティのクラス名から <c>Entity</c> を外したもの）は、ASP.NET のルーティングが大文字小文字を
/// 区別しないので、大小だけ違う 2 つの表も止める。ほかの型名の大小違いは止めずに警告する。
/// </para>
/// </remarks>
public sealed class GeneratedTypeNameCollisionTests
{
    /// <summary>出力の形</summary>
    public enum Output
    {
        /// <summary>1 ファイルにまとめる</summary>
        Single,

        /// <summary>分類ごとのファイルへ分ける</summary>
        Split,

        /// <summary>層ごとのフォルダへ分ける</summary>
        Layered,
    }

    /// <summary>表 <c>order</c> と一緒に置くと型名がぶつかる表と、ぶつかる型名</summary>
    public static TheoryData<string, string> CollidingTables =>
        new()
        {
            { "order_edit_model", "OrderEditModel" },
            { "order_mapper", "OrderMapper" },
            { "order_entity", "OrderEntity" },
            { "orders", "OrderEntity" },
            { "i_order", "IOrderRepository" },
            { "in_memory_order", "InMemoryOrderRepository" },
            { "ef_core_order", "EfCoreOrderRepository" },
            { "journaling_order", "JournalingOrderRepository" },
        };

    [Theory(
        DisplayName = "型名の衝突: 表 order と型名がぶつかる表は、出力の形に依らず両方の表を名指しするエラーになる"
    )]
    [MemberData(nameof(CollidingTables))]
    public void CollidingTable_IsRefusedNamingBothTables(string table, string typeName)
    {
        foreach (var output in Enum.GetValues<Output>())
        {
            var result = Generate(Diagram(Table("order"), Table(table)), output);

            result
                .Diagnostics.Should()
                .Contain(
                    diagnostic =>
                        diagnostic.Severity == GenerationDiagnosticSeverity.Error
                        && diagnostic.Message.Contains(typeName, StringComparison.Ordinal)
                        && diagnostic.Message.Contains("'order'", StringComparison.Ordinal)
                        && diagnostic.Message.Contains($"'{table}'", StringComparison.Ordinal),
                    $"{output}: {Describe(result)}"
                );
            result.Files.Should().BeEmpty(output.ToString());
        }
    }

    [Theory(
        DisplayName = "型名の衝突: 生成コードの固定の型名と同じ名前になる表・列は、出力の形に依らず名指しするエラーになる"
    )]
    [InlineData("raw_sql", null, "RawSqlMapper", "'raw_sql'")]
    [InlineData("orders", "sql_parameter", "SqlParameterValue", "'orders'.'sql_parameter'")]
    [InlineData(
        "orders",
        "media_type_header",
        "MediaTypeHeaderValue",
        "'orders'.'media_type_header'"
    )]
    public void ReservedName_IsRefusedNamingTheSource(
        string table,
        string? column,
        string typeName,
        string source
    )
    {
        foreach (var output in Enum.GetValues<Output>())
        {
            var entity = column is null ? Table(table) : Table(table, Text(column));
            var result = Generate(Diagram(entity), output);

            result
                .Diagnostics.Should()
                .Contain(
                    diagnostic =>
                        diagnostic.Severity == GenerationDiagnosticSeverity.Error
                        && diagnostic.Message.Contains($"'{typeName}'", StringComparison.Ordinal)
                        && diagnostic.Message.Contains(source, StringComparison.Ordinal),
                    $"{output}: {Describe(result)}"
                );
        }
    }

    [Fact(
        DisplayName = "型名の衝突: リモートのルート名が大文字小文字だけ違う 2 つの表は、出力の形に依らずエラーになる"
    )]
    public void RouteNamesDifferingOnlyInCase_AreRefused()
    {
        foreach (var output in Enum.GetValues<Output>())
        {
            var result = Generate(Diagram(Table("order_item"), Table("orderitem")), output);

            result
                .Diagnostics.Should()
                .Contain(
                    diagnostic =>
                        diagnostic.Severity == GenerationDiagnosticSeverity.Error
                        && diagnostic.Message.Contains("'OrderItem'", StringComparison.Ordinal)
                        && diagnostic.Message.Contains("'Orderitem'", StringComparison.Ordinal)
                        && diagnostic.Message.Contains("'order_item'", StringComparison.Ordinal)
                        && diagnostic.Message.Contains("'orderitem'", StringComparison.Ordinal),
                    $"{output}: {Describe(result)}"
                );
        }
    }

    [Theory(
        DisplayName = "型名の衝突: 型名がぶつからない表の組は、どの出力の形でも診断なしで生成してコンパイルできる"
    )]
    [InlineData("customer_address")]
    [InlineData("http_order")]
    [InlineData("order_line")]
    public void DistinctTables_GenerateAndCompile(string table)
    {
        foreach (var output in Enum.GetValues<Output>())
        {
            var result = Generate(Diagram(Table("order", Text("status")), Table(table)), output);

            result.HasErrors.Should().BeFalse($"{output}: {Describe(result)}");
            result
                .Diagnostics.Should()
                .NotContain(
                    diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning,
                    $"{output}: {Describe(result)}"
                );
            var compiled = GeneratedCodeCompiler.Compile(
                result,
                $"QuickER.TypeNameCollision.{output}.{Guid.NewGuid():N}"
            );
            compiled.Success.Should().BeTrue($"{output}: {compiled.DescribeErrors()}");
        }
    }

    [Fact(
        DisplayName = "型名の大小違い: 大文字小文字だけ違う型名は警告になり、両方の型名と元の列を名指しする"
    )]
    public void TypeNamesDifferingOnlyInCase_AreWarnedNamingBoth()
    {
        var result = Generate(
            Diagram(Table("accounts", Int("user_id")), Table("profiles", Int("userid"))),
            Output.Single
        );

        result.HasErrors.Should().BeFalse(Describe(result));
        result
            .Diagnostics.Should()
            .ContainSingle(
                diagnostic =>
                    diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                    && diagnostic.Message.Contains("'UserIdValue'", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("'UseridValue'", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("'accounts'.'user_id'", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("'profiles'.'userid'", StringComparison.Ordinal),
                Describe(result)
            );
    }

    [Theory(DisplayName = "型名の大小違い: 綴りが同じ・別の名前・エラーになる組では警告しない")]
    [InlineData("user_id", "user_id")]
    [InlineData("user_id", "account_id")]
    public void SameOrDistinctTypeNames_AreNotWarned(string first, string second)
    {
        var result = Generate(
            Diagram(Table("accounts", Int(first)), Table("profiles", Int(second))),
            Output.Single
        );

        result.HasErrors.Should().BeFalse(Describe(result));
        result
            .Diagnostics.Should()
            .NotContain(
                diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning,
                Describe(result)
            );
    }

    [Theory(
        DisplayName = "型名の大小違い: エラーになる組（大小だけ違うルート名・固定の型名と大小だけ違う型名）は重ねて警告しない"
    )]
    [InlineData("route")]
    [InlineData("reserved")]
    public void RefusedPair_IsNotAlsoWarned(string shape)
    {
        // reserved: 列 sql_parameter は固定の型名 SqlParameterValue と同じでエラー、別の表の sqlparameter は SqlparameterValue
        var diagram =
            shape == "route"
                ? Diagram(Table("order_item"), Table("orderitem"))
                : Diagram(
                    Table("accounts", Int("sql_parameter")),
                    Table("profiles", Int("sqlparameter"))
                );
        var result = Generate(diagram, Output.Single);

        result.HasErrors.Should().BeTrue(Describe(result));
        result
            .Diagnostics.Should()
            .NotContain(
                diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning,
                Describe(result)
            );
    }

    [Fact(DisplayName = "型名の大小違い: 警告が出る図も、生成物はコンパイルできる")]
    public void WarnedDiagram_Compiles()
    {
        var result = Generate(
            Diagram(Table("accounts", Int("user_id")), Table("profiles", Int("userid"))),
            Output.Single
        );

        result.HasErrors.Should().BeFalse(Describe(result));
        var compiled = GeneratedCodeCompiler.Compile(
            result,
            $"QuickER.TypeNameCase.{Guid.NewGuid():N}"
        );
        compiled.Success.Should().BeTrue(compiled.DescribeErrors());
    }

    /// <summary>生成する（値オブジェクト・EditModel・Mapper・QuickER 版 Repository・インメモリ・EF Core・リモートサービス）</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, Output output) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateValueObjects = true,
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
                GenerateInMemoryRepositories = true,
                GenerateEfCoreRepositories = true,
                GenerateRemoteServices = true,
                SplitFilesByCategory = output == Output.Split,
                LayeredOutput = output == Output.Layered,
            }
        );

    private static ErDiagram Diagram(params Entity[] entities)
    {
        var diagram = new ErDiagram { TargetDbms = "sqlserver" };
        diagram.Entities.AddRange(entities);
        return diagram;
    }

    /// <summary>主キー <c>id</c> と、指定した列を持つ表</summary>
    private static Entity Table(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.AddRange(columns);
        return entity;
    }

    private static Column Text(string name) =>
        new()
        {
            Name = name,
            DataType = "nvarchar(50)",
            IsNullable = true,
        };

    private static Column Int(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsNullable = true,
        };

    private static string Describe(CodeGenerationResult result) =>
        string.Join(
            " / ",
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}")
        );
}
