using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.MySql;
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
/// コンパイルとロードは <see cref="GeneratedCodeCompiler.CompileAndLoad"/> で行う。
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
                    ref System.Collections.Generic.List<string>? errors
                ) =>
                    (
                        errors ??= new System.Collections.Generic.List<string>()
                    ).Add("rejected by user rule");
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

    [Theory(
        DisplayName = "シード値は主キー・一意制約を重複させず、外部キーは親に実在する行を指し、全行を保存し直せる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task シード値は制約を守る(bool valueObjects)
    {
        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            GenerateConstraintDiagram(valueObjects),
            $"QuickER.InMemorySeed.Tests.{Guid.NewGuid():N}"
        );

        var (violations, rowCounts) = SeededSampleDataInvariants.Check(assembly, "Sample.Domain");

        violations.Should().BeEmpty();
        rowCounts["CodeEntity"]
            .Should()
            .Be(3, "char(2) の主キーは末尾の行番号を残して 3 行を区別する");
        rowCounts["UnitEntity"].Should().Be(3, "符号なし整数の主キーも行ごとに違う");
        rowCounts["FlagEntity"]
            .Should()
            .Be(2, "NOT NULL の bool の一意制約は 2 種類の値しか作れない");
        rowCounts["FlagItemEntity"].Should().Be(2, "2 行しかない親を参照する子も 2 行に減る");
        rowCounts["ItemEntity"].Should().Be(3);

        // 一意制約を検査するリポジトリ経由で、シードした全行を保存し直せる（重複があると一意制約違反になる）
        var services = new ServiceCollection();
        assembly
            .GetTypes()
            .Single(t => t.Name == "GeneratedInMemoryRepositoryServiceCollectionExtensions")
            .GetMethod("AddGeneratedInMemoryRepositories")!
            .Invoke(
                null,
                BindingFlags.OptionalParamBinding,
                binder: null,
                parameters: new object?[] { services, Type.Missing },
                culture: null
            );

        using var provider = services.BuildServiceProvider();

        foreach (var name in new[] { "Code", "Unit", "Flag", "FlagItem", "Ref", "Item" })
        {
            var repositoryInterface = assembly
                .GetTypes()
                .Single(t => t.IsInterface && t.Name == $"I{name}Repository");
            var repository = provider.GetRequiredService(repositoryInterface);
            var update = repositoryInterface
                .GetInterfaces()
                .Prepend(repositoryInterface)
                .Select(i => i.GetMethod("UpdateAsync"))
                .First(m => m is not null)!;

            foreach (var entity in InvokeGetAll(repository, repositoryInterface))
            {
                entity.GetType().GetMethod("MarkUpdated")!.Invoke(entity, null);
                var task =
                    (Task<bool>)
                        update.Invoke(
                            repository,
                            BindingFlags.OptionalParamBinding,
                            binder: null,
                            parameters: new object?[] { entity, Type.Missing, Type.Missing },
                            culture: null
                        )!;
                (await task).Should().BeTrue($"{name} のシード行を保存し直せる");
            }
        }
    }

    [Fact(DisplayName = "既存の全フィクスチャのシーダーも、主キー・一意制約・外部キーを守る")]
    public void 既存フィクスチャのシード値は制約を守る()
    {
        var assembly = typeof(InMemorySampleSeedExecutionTests).Assembly;
        var namespaces = assembly
            .GetTypes()
            .Where(t => t.Name == "InMemorySampleData")
            .Select(t => t.Namespace!)
            .Distinct()
            .ToList();

        namespaces.Should().NotBeEmpty();

        foreach (var namespaceName in namespaces)
        {
            var (violations, _) = SeededSampleDataInvariants.Check(assembly, namespaceName);
            violations.Should().BeEmpty(namespaceName);
        }
    }

    [Fact(
        DisplayName = "シード行数の不動点は自己参照と相互参照でも止まり、減った行数が循環をたどって伝わる"
    )]
    public void シード行数は循環する参照でも決まる()
    {
        // a は NOT NULL の bool の一意制約で 2 行に減り、a と b は互いを参照し、nodes は自分自身を参照する。
        // c は b を参照し、図の中で a・b より前に並ぶ＝b の行数が減ってから c へ伝わるには 2 周目が要る
        var aId = Column("a_id", "int", key: true);
        var aFlag = Column("flag", "tinyint(1)");
        var aToB = Column("b_id", "int");
        var bId = Column("b_id", "int", key: true);
        var bToA = Column("a_id", "int");
        var nodeId = Column("node_id", "int", key: true);
        var parentId = Column("parent_id", "int", nullable: true);
        var cId = Column("c_id", "int", key: true);
        var cToB = Column("b_id", "int");

        var a = new Entity { TableName = "a", Columns = [aId, aFlag, aToB] };
        a.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { aFlag.Id } });
        var b = new Entity { TableName = "b", Columns = [bId, bToA] };
        var nodes = new Entity { TableName = "nodes", Columns = [nodeId, parentId] };
        var c = new Entity { TableName = "c", Columns = [cId, cToB] };

        var diagram = new ErDiagram
        {
            TargetDbms = "mysql",
            Entities = [c, a, b, nodes],
            Relationships =
            [
                Reference(b, bId, a, aToB),
                Reference(a, aId, b, bToA),
                Reference(nodes, nodeId, nodes, parentId),
                Reference(b, bId, c, cToB),
            ],
        };

        var content = GenerateInMemory(diagram, valueObjects: false).Files[0].Content;

        SeedLoopBound(content, "SeedAEntity").Should().Be("2");
        SeedLoopBound(content, "SeedBEntity").Should().Be("2");
        SeedLoopBound(content, "SeedCEntity").Should().Be("2");
        SeedLoopBound(content, "SeedNodeEntity").Should().Be("RowsPerEntity");
    }

    /// <summary>シーダーのメソッドのループ上限（<c>index &lt;= …</c> の右辺）を取り出す</summary>
    private static string SeedLoopBound(string content, string methodName)
    {
        var start = content.IndexOf($"private static void {methodName}(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, methodName);
        var bound =
            content.IndexOf("index <= ", start, StringComparison.Ordinal) + "index <= ".Length;
        return content[bound..content.IndexOf(';', bound)];
    }

    /// <summary>列を組み立てる</summary>
    private static Column Column(
        string name,
        string dataType,
        bool key = false,
        bool nullable = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            IsPrimaryKey = key,
            IsNullable = nullable,
        };

    /// <summary>親の列を子の列が参照する 1 対多のリレーションを組み立てる</summary>
    private static Relationship Reference(
        Entity parent,
        Column parentColumn,
        Entity child,
        Column childColumn
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new RelationshipColumnPair(parentColumn.Id, childColumn.Id)],
        };

    /// <summary>インメモリ単独の生成物を作る（生成エラーが無いことも確かめる）</summary>
    private static CodeGenerationResult GenerateInMemory(ErDiagram diagram, bool valueObjects)
    {
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            MySqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = valueObjects,
                GenerateInMemoryRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
                GenerateRepositories = false,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join(
                    " / ",
                    result
                        .Diagnostics.Where(d => d.Severity == GenerationDiagnosticSeverity.Error)
                        .Select(d => d.Message)
                )
            );
        return result;
    }

    /// <summary>
    /// シード値の作り方を試す図（MySQL）。char(2) の主キー・符号なし整数の主キーと char(2) の一意制約・
    /// NOT NULL の bool の一意制約とそれを参照する子・主キーでない一意制約の列を参照される表と、
    /// それらを文字列・符号なし整数・一意制約の列で参照する表。
    /// </summary>
    private static CodeGenerationResult GenerateConstraintDiagram(bool valueObjects)
    {
        var code = Column("code", "char(2)", key: true);
        var codeLabel = Column("label", "varchar(50)");
        var codes = new Entity { TableName = "codes", Columns = [code, codeLabel] };

        var unitId = Column("unit_id", "int unsigned", key: true);
        var unitShort = Column("short_name", "char(2)");
        var units = new Entity { TableName = "units", Columns = [unitId, unitShort] };
        units.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { unitShort.Id } });

        var flagId = Column("flag_id", "int", key: true);
        var flag = Column("flag", "tinyint(1)");
        var flags = new Entity { TableName = "flags", Columns = [flagId, flag] };
        flags.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { flag.Id } });

        var flagChildId = Column("flag_child_id", "int", key: true);
        var flagRef = Column("flag_id", "int");
        var flagChildren = new Entity
        {
            TableName = "flag_items",
            Columns = [flagChildId, flagRef],
        };

        var refId = Column("ref_id", "int", key: true);
        var refNo = Column("ref_no", "int");
        var refs = new Entity { TableName = "refs", Columns = [refId, refNo] };
        refs.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = { refNo.Id } });

        var itemId = Column("item_id", "int", key: true);
        var itemCode = Column("code_value", "char(2)");
        var itemUnit = Column("unit_no", "int unsigned");
        var itemRef = Column("ref_value", "int", nullable: true);
        var items = new Entity
        {
            TableName = "items",
            Columns = [itemId, itemCode, itemUnit, itemRef],
        };

        var diagram = new ErDiagram
        {
            TargetDbms = "mysql",
            Entities = [codes, units, flags, flagChildren, refs, items],
            Relationships =
            [
                Reference(flags, flagId, flagChildren, flagRef),
                Reference(codes, code, items, itemCode),
                Reference(units, unitId, items, itemUnit),
                Reference(refs, refNo, items, itemRef),
            ],
        };

        return GenerateInMemory(diagram, valueObjects);
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

        return GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.InMemorySeed.Tests.{Guid.NewGuid():N}",
            extraSources
        );
    }
}
