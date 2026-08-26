using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// インメモリ Repository のサンプルデータ生成が、生成した検証規則（VO の precision / scale / MaxLength）を
/// 自分で満たすことを「生成→実コンパイル→既定設定の DI 登録（シード込み）→読み出し」まで通して検証する。
/// </summary>
/// <remarks>
/// <para>
/// 第 9 次指示書 A-1 の再発防止網。サンプル値の組み立て（<c>BuildSampleValueExpression</c>）は制約の種類ごとに
/// 対応が要り（MaxLength は切り詰め・decimal は桁合わせ）、抜けると <c>AddGeneratedInMemoryRepositories()</c> が
/// **既定設定のまま起動時例外**になる——利用者が最初に踏む形の欠陥なので、Seed まで流す 1 本をここに置く。
/// 図には敵対的な decimal を並べる: scale=1（発見時の形＝<c>100.50m</c> の Scale 2 が違反）・scale=0
/// （<c>100.5m</c> でも違反＝「たまたま通る値」対処の再発形）・precision 余裕なし <c>decimal(3,2)</c>
/// （旧値 <c>301.50</c> は整数部で違反）・整数部 0 桁の <c>decimal(1,1)</c>。
/// </para>
/// <para>
/// コンパイルとロードは <see cref="GuidKeyValueObjectExecutionTests"/> と同型（TPA 参照＋実行まで踏み込むため自前）。
/// DI 拡張を呼ぶため Microsoft.Extensions.DependencyInjection（本体＋Abstractions）を参照へ加える。
/// </para>
/// </remarks>
public sealed class InMemorySampleSeedExecutionTests
{
    /// <summary>プロパティ名 → 宣言 (precision, scale)（図の定義と同期）。</summary>
    private static readonly IReadOnlyDictionary<string, (int Precision, int Scale)> DecimalFacets =
        new Dictionary<string, (int, int)>
        {
            ["Width"] = (9, 1),
            ["Whole"] = (9, 0),
            ["Ratio"] = (3, 2),
            ["Tiny"] = (1, 1),
            ["Depth"] = (9, 1),
        };

    [Fact(
        DisplayName = "既定設定の AddGeneratedInMemoryRepositories() がシードで例外にならず、全行が読め、decimal は宣言桁を満たす"
    )]
    public void 既定シードが検証規則を満たす()
    {
        var assembly = BuildAndLoadGeneratedAssembly();

        // (1) 既定設定（seedSampleData 省略＝true）で DI 登録: シードが VO の Create を通るのでここで落ちない
        var services = new ServiceCollection();
        var extensions = assembly
            .GetTypes()
            .Single(t => t.Name == "GeneratedInMemoryRepositoryServiceCollectionExtensions");
        var register = extensions.GetMethod("AddGeneratedInMemoryRepositories")!;
        register.Invoke(
            null,
            BindingFlags.OptionalParamBinding,
            binder: null,
            parameters: new object?[] { services, Type.Missing },
            culture: null
        );

        using var provider = services.BuildServiceProvider();

        // (2) シードした全行が GetAllAsync で読み出せる
        var repositoryInterface = assembly
            .GetTypes()
            .Single(t => t.IsInterface && t.Name == "IMeasureRepository");
        var repository = provider.GetRequiredService(repositoryInterface);
        var entities = InvokeGetAll(repository, repositoryInterface);

        entities.Should().HaveCount(3, "シーダーは各エンティティ 3 件を投入する");

        // (3) decimal 列の値が宣言 precision / scale の両方を満たす（シード自体が Create を通った時点で
        //     保証されるが、桁の意図を名指しで固定する）。NULL 可列（Depth）は 3 件中 1 件が null
        var depthNullCount = 0;

        foreach (var entity in entities)
        {
            foreach (var (propertyName, (precision, scale)) in DecimalFacets)
            {
                var valueObject = entity.GetType().GetProperty(propertyName)!.GetValue(entity);

                if (valueObject is null)
                {
                    propertyName.Should().Be("Depth", "NULL を取るのは NULL 可列だけ");
                    depthNullCount++;
                    continue;
                }

                var value = (decimal)
                    valueObject.GetType().GetProperty("Value")!.GetValue(valueObject)!;

                value.Scale.Should().BeLessThanOrEqualTo((byte)scale, $"{propertyName} の小数部");
                Math.Abs(decimal.Truncate(value))
                    .Should()
                    .BeLessThan(
                        (decimal)Math.Pow(10, precision - scale),
                        $"{propertyName} の整数部は {precision - scale} 桁以内"
                    );
            }
        }

        depthNullCount.Should().Be(1, "NULL 可列は 3 件中 1 件（index == 3）を null にする");
    }

    [Fact(
        DisplayName = "OnValidate の拒否で seed が落ちるとき、例外にプロパティ・値・元メッセージ・対処（seedSampleData: false）が載る"
    )]
    public void OnValidateの拒否は文脈付きの例外になる()
    {
        // 利用者が書く partial（生成された WidthValue への拡張）＝生成器には知りようがない規則
        const string userValidation = """
            namespace Sample.Domain;

            public sealed partial class WidthValue
            {
                static partial void OnValidate(
                    decimal value,
                    System.Collections.Generic.ICollection<string> errors
                ) => errors.Add("rejected by user rule");
            }
            """;

        var assembly = BuildAndLoadGeneratedAssembly(userValidation);

        var services = new ServiceCollection();
        var extensions = assembly
            .GetTypes()
            .Single(t => t.Name == "GeneratedInMemoryRepositoryServiceCollectionExtensions");
        var register = extensions.GetMethod("AddGeneratedInMemoryRepositories")!;

        var act = () =>
            register.Invoke(
                null,
                BindingFlags.OptionalParamBinding,
                binder: null,
                parameters: new object?[] { services, Type.Missing },
                culture: null
            );

        // リフレクション経由なので TargetInvocationException に包まれる。実体はガードの InvalidOperationException で、
        // 受け入れ条件の 4 要素（エンティティ.プロパティ・渡した値・元の検証メッセージ・対処）を 1 通で運ぶ
        var thrown = act.Should().Throw<TargetInvocationException>().Which;
        var failure = thrown.InnerException.Should().BeOfType<InvalidOperationException>().Which;

        failure.Message.Should().Contain("MeasureEntity.Width", "どのプロパティで落ちたか");
        failure.Message.Should().Contain("\"100.5\"", "何を入れようとしたか（index=1 の値）");
        failure.Message.Should().Contain("rejected by user rule", "元の検証メッセージ");
        failure
            .Message.Should()
            .Contain("seedSampleData: false", "対処へ最短で辿れることが (1) の存在理由");
        failure
            .InnerException.Should()
            .NotBeNull("元の ValueObjectValidationException を inner として保全する")
            .And.Subject.GetType()
            .Name.Should()
            .Be("ValueObjectValidationException");
    }

    /// <summary>リフレクションで GetAllAsync を呼び、結果をリストとして返す。</summary>
    private static List<object> InvokeGetAll(object repository, Type repositoryInterface)
    {
        // GetAllAsync は基底面 IRepository<TEntity, TKey> の宣言＝インターフェイス階層から引く
        var method = repositoryInterface
            .GetInterfaces()
            .Prepend(repositoryInterface)
            .Select(i => i.GetMethod("GetAllAsync"))
            .First(m => m is not null)!;
        var task = (Task)method.Invoke(repository, new object?[] { CancellationToken.None })!;
        task.GetAwaiter().GetResult();

        var result = (IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        return result.Cast<object>().ToList();
    }

    /// <summary>敵対的な decimal 構成の図を VO＋インメモリ単独で生成し、コンパイル・ロードして返す。</summary>
    /// <param name="extraSources">生成物と一緒にコンパイルする追加ソース（利用者の partial 実装を模す）。</param>
    private static Assembly BuildAndLoadGeneratedAssembly(params string[] extraSources)
    {
        static Column Decimal(string name, string dataType, bool nullable = false) =>
            new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                DataType = dataType,
                IsNullable = nullable,
            };

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "measures",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "measure_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        Decimal("width", "decimal(9,1)"),
                        Decimal("whole", "decimal(9)"),
                        Decimal("ratio", "decimal(3,2)"),
                        Decimal("tiny", "decimal(1,1)"),
                        Decimal("depth", "decimal(9,1)", nullable: true),
                    ],
                },
            ],
        };

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateValueObjects = true,
            GenerateInMemoryRepositories = true,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);
        result
            .HasErrors.Should()
            .BeFalse(
                "生成自体が失敗: "
                    + string.Join(
                        " / ",
                        result
                            .Diagnostics.Where(d =>
                                d.Severity == GenerationDiagnosticSeverity.Error
                            )
                            .Select(d => d.Message)
                    )
            );

        return CompileAndLoad(result, extraSources);
    }

    /// <summary>生成結果（＋追加ソース）を TPA＋DI 参照集合でコンパイルし、成功時にロードしたアセンブリを返す。</summary>
    private static Assembly CompileAndLoad(CodeGenerationResult result, string[] extraSources)
    {
        var syntaxTrees = result
            .Files.Select(file =>
                CSharpSyntaxTree.ParseText(
                    file.Content,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    path: file.FileName
                )
            )
            .Concat(
                extraSources.Select(
                    (source, i) =>
                        CSharpSyntaxTree.ParseText(
                            source,
                            new CSharpParseOptions(LanguageVersion.Latest),
                            path: $"UserPartial{i}.cs"
                        )
                )
            )
            .ToArray();

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            // DI 拡張（AddGeneratedInMemoryRepositories）のため本体＋Abstractions を明示追加
            .Append(MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            $"QuickER.InMemorySeed.Tests.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        emitResult
            .Success.Should()
            .BeTrue(
                "生成コードのコンパイルに失敗:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        emitResult
                            .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.ToString())
                    )
            );

        peStream.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(peStream.ToArray());
    }
}
