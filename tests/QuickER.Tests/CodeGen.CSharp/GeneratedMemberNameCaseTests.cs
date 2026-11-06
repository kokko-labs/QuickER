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
/// エンティティのメンバー名の大文字小文字が表をまたいで違うだけの、診断の無い図の生成物が、実際の Kestrel のリモートで
/// 全エンティティを書いて読み戻せることを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// エンティティは JSON で読み書きされ（リモートの転送・SQL Server の Include の読み込み）、どちらの設定もプロパティ名の
/// 大文字小文字を区別しない。同じエンティティの中で大小だけ違うメンバー名は構成に依らず生成時のエラーで止め、EditModel の中だけの
/// 大小違いは止めない。その診断とコンパイルの判定は生成の構成のエンジンの決まった行へ移した（<c>CodegenCaseTests</c> の行
/// <c>diag-member-name-case-only-*</c>・<c>compile-member-name-case-editmodel-only</c>）。
/// </para>
/// <para>
/// 残す理由（P3-6）：実際の Kestrel を立て、生成したクライアントの DI 越しに保存して読み戻す往復は、決まった行の探針
/// （生成物を読み込んで動かす部品）に Kestrel を立てる部品が無く、行で言えない実行の検証。
/// </para>
/// </remarks>
public sealed class GeneratedMemberNameCaseTests
{
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
