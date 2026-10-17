using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using static QuickER.Tests.CodeGen.CSharp.GeneratedRuntimeProbe;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// エンティティのメンバー名が大文字小文字だけ違う図を、構成に依らず生成時のエラーで名指しして止めることを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// エンティティは JSON で読み書きされ（リモートの転送・SQL Server の Include の読み込み）、どちらの設定もプロパティ名の
/// 大文字小文字を区別しない。そのため同じエンティティに <c>UserId</c> と <c>Userid</c> があると、コンパイルは通っても
/// 実行時に直列化の設定を組み立てる時点で例外になる。比べるのは JSON に出るメンバー（列・ナビゲーション・基底の公開
/// プロパティ）で、EditModel は JSON に出ないので大文字小文字を区別して比べたままにする。
/// </para>
/// <para>
/// 判定は構成に依らない（リモートを生成しない構成でも止める）ので、リモートありとなしの 2 つの構成で同じ答えになることを見る。
/// </para>
/// </remarks>
public sealed class GeneratedMemberNameCaseTests
{
    /// <summary>生成の構成</summary>
    public enum Config
    {
        /// <summary>値オブジェクト・EditModel・Mapper・QuickER 版 Repository・インメモリ・EF Core・リモートサービス</summary>
        Full,

        /// <summary>エンティティと EditModel だけ（Repository・リモートなし）</summary>
        Plain,
    }

    /// <summary>大文字小文字だけ違うメンバー名を作る図と、診断が名指しするはずの名前</summary>
    public static TheoryData<string, string[]> CaseOnlyMembers =>
        new()
        {
            { "columns", ["'UserId'", "'Userid'", "'user_id'", "'userid'"] },
            { "column-navigation", ["'OrderLines'", "'Orderlines'", "'orderlines'"] },
            { "row-state", ["'RowState'", "'Rowstate'", "'rowstate'"] },
            { "is-added", ["'IsAdded'", "'Isadded'", "'isadded'"] },
            { "display-name", ["'DisplayName'", "'Displayname'", "'displayname'"] },
        };

    [Theory(
        DisplayName = "メンバー名の大小違い: エンティティの JSON に出るメンバー名が大文字小文字だけ違う図は、構成に依らず表と名前を名指しするエラーになる"
    )]
    [MemberData(nameof(CaseOnlyMembers))]
    public void CaseOnlyMemberNames_AreRefused(string shape, string[] names)
    {
        foreach (var config in Enum.GetValues<Config>())
        {
            var result = Generate(DiagramFor(shape), config);

            result
                .Diagnostics.Should()
                .Contain(
                    diagnostic =>
                        diagnostic.Severity == GenerationDiagnosticSeverity.Error
                        && diagnostic.Message.Contains("'orders'", StringComparison.Ordinal)
                        && names.All(name =>
                            diagnostic.Message.Contains(name, StringComparison.Ordinal)
                        ),
                    $"{config}: {Describe(result)}"
                );
            result.Files.Should().BeEmpty(config.ToString());
        }
    }

    [Fact(
        DisplayName = "メンバー名の大小違い: 同じ表の大小だけ違う列はエラーだけを出し、値オブジェクトの型名の大小違いの警告を重ねない"
    )]
    public void SameTableCaseOnlyColumns_AreNotAlsoWarned()
    {
        var result = Generate(DiagramFor("columns"), Config.Full);

        result.HasErrors.Should().BeTrue(Describe(result));
        result
            .Diagnostics.Should()
            .NotContain(
                diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning,
                Describe(result)
            );
    }

    [Fact(
        DisplayName = "メンバー名の大小違い: EditModel の中だけで大小が違う名前（JSON に出ない）は止めず、診断なしで生成してコンパイルできる"
    )]
    public void EditModelOnlyCaseDifference_GeneratesAndCompiles()
    {
        // 列 id の EditModel は BindingId を、列 bindingid は Bindingid を作る（エンティティのメンバーは Id と Bindingid）
        var result = Generate(Diagram(Table("orders", Int("bindingid"))), Config.Full);

        result.HasErrors.Should().BeFalse(Describe(result));
        result
            .Diagnostics.Should()
            .NotContain(
                diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning,
                Describe(result)
            );
        var compiled = GeneratedCodeCompiler.Compile(
            result,
            $"QuickER.MemberNameCase.EditModel.{Guid.NewGuid():N}"
        );
        compiled.Success.Should().BeTrue(compiled.DescribeErrors());
    }

    [Fact(
        DisplayName = "メンバー名の大小違い: 診断の無い図（表をまたぐ大小違いの列・子へのナビゲーション）は、実際の Kestrel のリモートで全エンティティを書いて読み戻せる"
    )]
    public async Task DiagnosticFreeDiagram_RoundTripsOverRemoteHttp()
    {
        // 表をまたぐ user_id / userid は警告だけで止めない組。orders は子 order_lines へのナビゲーションを持つ
        var orders = Table("orders", Int("status"));
        var lines = Table("order_lines", Int("order_id"));
        var diagram = Diagram(
            Table("accounts", Int("user_id")),
            Table("profiles", Int("userid")),
            orders,
            lines
        );
        diagram.Relationships.Add(Reference(orders, lines, "order_id"));

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                GenerateInMemoryRepositories = true,
                GenerateRemoteServices = true,
            }
        );
        result.HasErrors.Should().BeFalse(Describe(result));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.MemberNameCase.Remote.{Guid.NewGuid():N}"
        );

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        InvokeExtension(
            assembly,
            "AddGeneratedInMemoryRepositories",
            [typeof(IServiceCollection), typeof(bool)],
            builder.Services,
            false
        );
        await using var app = builder.Build();
        var access = Enum.Parse(TypeNamed(assembly, "RemoteAccess"), "AllowAnonymous");
        InvokeExtension(
            assembly,
            "MapGeneratedRemoteEndpoints",
            null,
            app,
            access,
            "/quicker",
            false
        );
        await app.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var services = new ServiceCollection();
            InvokeExtension(
                assembly,
                "AddGeneratedHttpRemoteRepositories",
                [typeof(IServiceCollection), typeof(string)],
                services,
                app.Urls.Single() + "/quicker"
            );
            await using var provider = services.BuildServiceProvider();

            foreach (
                var (name, column) in new[]
                {
                    ("Account", "UserId"),
                    ("Profile", "Userid"),
                    ("Order", "Status"),
                    ("OrderLine", "OrderId"),
                }
            )
            {
                var entityType = TypeNamed(assembly, $"{name}Entity");
                var entity = Activator.CreateInstance(entityType)!;
                SetValue(entity, "Id", 1);
                SetValue(entity, column, 7);

                var repository = provider.GetRequiredService(
                    TypeNamed(assembly, $"I{name}RemoteRepository")
                );
                await InvokeAsync(
                    repository,
                    "InsertAsync",
                    entity,
                    TestContext.Current.CancellationToken
                );
                var read = await InvokeAsync(
                    repository,
                    "GetByIdAsync",
                    ToStored(entityType.GetProperty("Id")!.PropertyType, 1),
                    TestContext.Current.CancellationToken
                );

                read.Should().NotBeNull(name);
                ReadValue(read!, column).Should().Be(7, $"{name}.{column}");
            }
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>形ごとの図（どれも表 orders に大小だけ違う名前を置く）</summary>
    private static ErDiagram DiagramFor(string shape)
    {
        switch (shape)
        {
            case "columns":
                return Diagram(Table("orders", Int("user_id"), Int("userid")));

            case "column-navigation":
            {
                var orders = Table("orders", Int("orderlines"));
                var lines = Table("order_lines", Int("order_id"));
                var diagram = Diagram(orders, lines);
                diagram.Relationships.Add(Reference(orders, lines, "order_id"));
                return diagram;
            }

            case "row-state":
                return Diagram(Table("orders", Int("rowstate")));

            case "is-added":
                return Diagram(Table("orders", Int("isadded")));

            case "display-name":
                return Diagram(Table("orders", Int("displayname")));

            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
    }

    private static CodeGenerationResult Generate(ErDiagram diagram, Config config) =>
        new CSharpCodeGenerationService().Generate(
            diagram,
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            config == Config.Full
                ? new CodeGenerationOptions
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
                }
                : new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    GenerateEditModels = true,
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

    private static Column Int(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsNullable = true,
        };

    /// <summary>子の列から親の主キーへの 1 対多のリレーション</summary>
    private static Relationship Reference(Entity parent, Entity child, string childColumn) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    parent.Columns.Single(column => column.IsPrimaryKey).Id,
                    child.Columns.Single(column => column.Name == childColumn).Id
                ),
            ],
        };

    /// <summary>リポジトリの非同期メソッドを呼び、結果を返す（結果を持たない Task なら null）</summary>
    private static async Task<object?> InvokeAsync(
        object repository,
        string name,
        params object[] arguments
    )
    {
        var method = repository
            .GetType()
            .GetMethods()
            .Single(method =>
                method.Name == name && method.GetParameters().Length == arguments.Length
            );
        var task = (Task)method.Invoke(repository, arguments)!;
        await task;

        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>プロパティへ int を入れる（値オブジェクトなら Create で包む）</summary>
    private static void SetValue(object entity, string property, int value)
    {
        var info = entity.GetType().GetProperty(property)!;
        info.SetValue(entity, ToStored(info.PropertyType, value));
    }

    /// <summary>int をプロパティの型の値にする（値オブジェクトなら Create で包む）</summary>
    private static object ToStored(Type propertyType, int value)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return type == typeof(int)
            ? value
            : type.GetMethod(
                        "Create",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                        [typeof(int)]
                    )!
                .Invoke(null, [value])!;
    }

    /// <summary>プロパティの int の値を読む（値オブジェクトなら Value を開く）</summary>
    private static object? ReadValue(object entity, string property)
    {
        var stored = entity.GetType().GetProperty(property)!.GetValue(entity);

        return stored is null or int
            ? stored
            : stored.GetType().GetProperty("Value")!.GetValue(stored);
    }

    private static string Describe(CodeGenerationResult result) =>
        string.Join(
            " / ",
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}")
        );
}
