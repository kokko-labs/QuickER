using System;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// PostgreSQL の <c>inet</c> / <c>cidr</c> / <c>interval</c> の列を含む図の生成を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 3 型は Npgsql が文字列として読めないため、ドライバが返す CLR 型（<c>System.Net.IPAddress</c> /
/// <c>System.Net.IPNetwork</c> / <c>TimeSpan</c>）で生成する。<c>cidr</c> と <c>interval</c> は値型で、
/// EditModel の既存の解析経路（IParsable）にそのまま載る。
/// </para>
/// <para>
/// <c>inet</c>（参照型の <c>IPAddress</c>）は EditModel が入力文字列をそのまま代入する形になりコンパイルできないため、
/// 配列列と同じ述語（<see cref="CSharpTypeInfo.IsExcludedFromEditModel"/>）で EditModel と値オブジェクトから外し、
/// 生成時 Info で名指しする。NOT NULL の Entity プロパティは <c>= null!</c>、インメモリのシード値は決定的なアドレスで
/// 始める（どちらも無いと null 許容の警告が出る）。EditModel・VO・インメモリの組み合わせの実コンパイル（エラーも警告も出ない）は、
/// 決まった行（<c>CodegenCaseTests</c> の <c>compile-postgresql-network-types-*</c>）が固定する。
/// </para>
/// </remarks>
public class PostgreSqlNetworkTypeGenerationTests
{
    /// <summary>inet / cidr / interval の列（NOT NULL と NULL 許容）を持つ PostgreSQL の図を作る</summary>
    private static ErDiagram BuildDiagram() =>
        new()
        {
            TargetDbms = "postgresql",
            Entities =
            {
                new Entity
                {
                    TableName = "hosts",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "integer",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "addr",
                            DataType = "inet",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "backup_addr",
                            DataType = "inet",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "net",
                            DataType = "cidr",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "backup_net",
                            DataType = "cidr",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Name = "lease",
                            DataType = "interval",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "grace",
                            DataType = "interval day to second(3)",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    private static CodeGenerationResult Generate(CodeGenerationOptions options)
    {
        var diagram = BuildDiagram();
        return new CSharpCodeGenerationService().Generate(
            diagram,
            PostgreSqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            options
        );
    }

    private static CodeGenerationOptions Options(
        bool editModels,
        bool valueObjects,
        bool inMemory
    ) =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = editModels,
            GenerateMappers = editModels,
            GenerateValueObjects = valueObjects,
            GenerateRepositories = false,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = inMemory,
        };

    /// <summary>
    /// inet の列は EditModel に載らず、cidr・interval の列は載ることと、Entity の型・初期化子を検証する。
    /// </summary>
    [Fact(DisplayName = "inet は EditModel から外れ、cidr・interval は通常の型として載る")]
    public void Generate_InetIsExcludedFromEditModel()
    {
        var result = Generate(Options(editModels: true, valueObjects: false, inMemory: false));
        var content = string.Join(Environment.NewLine, result.Files.Select(file => file.Content));

        content.Should().Contain("public System.Net.IPAddress Addr { get; set; } = null!;");
        content.Should().Contain("public System.Net.IPAddress? BackupAddr { get; set; }");
        content.Should().Contain("public System.Net.IPNetwork Net { get; set; }");
        content.Should().Contain("public TimeSpan Lease { get; set; }");

        content.Should().NotContain("BindingAddr");
        content.Should().NotContain("BindingBackupAddr");
        content.Should().Contain("BindingNet");
        content.Should().Contain("BindingLease");
    }
}
