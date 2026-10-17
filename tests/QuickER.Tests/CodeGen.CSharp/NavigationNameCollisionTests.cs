using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 2 つのテーブルの間に両方向のリレーションがある図のナビゲーション名と、同じ列ペアの外部キーが 2 本ある図の生成時のエラーを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// ナビゲーション名は通常、相手のテーブル名から作る（親側＝子の複数形〔1 対 1 は単数形〕・子側＝親の単数形）。
/// 表 customers と profiles の間に、customers を親とする 1 対 1 と、profiles を親とする 1 対 1（または 1 対多）があると、
/// customers には子側の <c>Profile</c> と親側の <c>Profile</c> が両方できて、同じ名前のメンバーが 2 つになる。
/// このときだけ、2 つのテーブルの間の全リレーションを外部キー列由来の役割名で区別する。両方向とも 1 対多の組は
/// 名前がぶつからないので、従来の名前のまま（生成物を変えない）。
/// </para>
/// <para>
/// 同じ親・子・列ペアの外部キーが 2 本ある図は、同じ定義の重複で、役割名でも区別できない。両方の制約名を名指しする
/// 専用のエラーで止める（片方を消せば直る）。
/// </para>
/// </remarks>
public sealed class NavigationNameCollisionTests
{
    /// <summary>両方向のリレーションの形と、生成後の各エンティティのナビゲーション名</summary>
    public static TheoryData<string, string[], string[]> Shapes =>
        new()
        {
            // 両方向とも 1 対 1: 名前がぶつかるので役割名へ切り替える
            {
                "one-to-one-both",
                ["Profile", "ProfileByCustomer"],
                ["Customer", "CustomerByProfile"]
            },
            // 片方 1 対 1・片方 1 対多: customers の Profile がぶつかるので役割名へ切り替える
            {
                "one-to-one-and-many",
                ["Profile", "ProfileByCustomer"],
                ["Customer", "CustomersByProfile"]
            },
            // 両方向とも 1 対多: ぶつからないので従来の名前のまま
            { "one-to-many-both", ["Profile", "Profiles"], ["Customer", "Customers"] },
            // 同じ向きの 2 本は役割名（従来の規則）・逆向きの 1 本は従来の名前のまま
            {
                "two-and-reverse",
                ["Profile", "ProfilesByBackupCustomer", "ProfilesByCustomer"],
                ["BackupCustomer", "Customer", "Customers"]
            },
        };

    [Theory(
        DisplayName = "ナビゲーション名: 両方向のリレーションは、既定の名前がぶつかるときだけ役割名になり、生成・コンパイル・EF Core のモデル構築が通る"
    )]
    [MemberData(nameof(Shapes))]
    public void BidirectionalRelationships_GenerateWithDistinctNavigations(
        string shape,
        string[] customerNavigations,
        string[] profileNavigations
    )
    {
        var result = Generate(DiagramFor(shape));

        result.HasErrors.Should().BeFalse(Describe(result));
        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.NavigationNames.{Guid.NewGuid():N}"
        );

        NavigationNames(assembly, "CustomerEntity").Should().Equal(customerNavigations);
        NavigationNames(assembly, "ProfileEntity").Should().Equal(profileNavigations);

        // Model へのアクセスで EF Core のモデル構築と検証が走る（Fluent の HasOne / WithOne の組が成り立つこと）
        using var context = CreateContext(assembly);
        context.Model.FindEntityType("Sample.Domain.CustomerEntity").Should().NotBeNull();
    }

    [Theory(
        DisplayName = "外部キーの重複: 同じ親・子・列ペアの外部キーが 2 本ある図は、両方の制約名と列を名指しする専用のエラーで止まる"
    )]
    [InlineData("FK_first", "FK_second", "FK_first", "FK_second")]
    [InlineData(null, null, "FK_profiles_customers", "FK_profiles_customers_customer_id")]
    public void DuplicateForeignKeys_AreRefusedNamingBoth(
        string? firstName,
        string? secondName,
        string firstExpected,
        string secondExpected
    )
    {
        var (customers, profiles) = Tables();
        var diagram = Diagram(customers, profiles);
        diagram.Relationships.Add(
            Reference(customers, profiles, "customer_id", RelationshipType.OneToMany, firstName)
        );
        diagram.Relationships.Add(
            Reference(customers, profiles, "customer_id", RelationshipType.OneToMany, secondName)
        );

        var result = Generate(diagram);

        result
            .Diagnostics.Where(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
            )
            .Should()
            .ContainSingle(Describe(result))
            .Which.Message.Should()
            .Contain($"'{firstExpected}'")
            .And.Contain($"'{secondExpected}'")
            .And.Contain("'profiles'.'customer_id'")
            .And.Contain("'customers'.'id'");
        result.Files.Should().BeEmpty();
    }

    /// <summary>生成する（QuickER 版 Repository・EF Core・インメモリ・値オブジェクト・EditModel・Mapper・リモートサービス）</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram) =>
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
            }
        );

    /// <summary>形ごとの図（表 customers と profiles の間のリレーション）</summary>
    private static ErDiagram DiagramFor(string shape)
    {
        var (customers, profiles) = Tables();
        var diagram = Diagram(customers, profiles);
        var relationships = shape switch
        {
            "one-to-one-both" => new[]
            {
                Reference(customers, profiles, "customer_id", RelationshipType.OneToOne),
                Reference(profiles, customers, "profile_id", RelationshipType.OneToOne),
            },
            "one-to-one-and-many" =>
            [
                Reference(customers, profiles, "customer_id", RelationshipType.OneToOne),
                Reference(profiles, customers, "profile_id", RelationshipType.OneToMany),
            ],
            "one-to-many-both" =>
            [
                Reference(customers, profiles, "customer_id", RelationshipType.OneToMany),
                Reference(profiles, customers, "profile_id", RelationshipType.OneToMany),
            ],
            "two-and-reverse" =>
            [
                Reference(customers, profiles, "customer_id", RelationshipType.OneToMany),
                Reference(customers, profiles, "backup_customer_id", RelationshipType.OneToMany),
                Reference(profiles, customers, "profile_id", RelationshipType.OneToMany),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
        diagram.Relationships.AddRange(relationships);
        return diagram;
    }

    /// <summary>表 customers（列 profile_id）と profiles（列 customer_id・backup_customer_id）</summary>
    private static (Entity Customers, Entity Profiles) Tables() =>
        (Table("customers", "profile_id"), Table("profiles", "customer_id", "backup_customer_id"));

    private static ErDiagram Diagram(params Entity[] entities)
    {
        var diagram = new ErDiagram { TargetDbms = "sqlserver" };
        diagram.Entities.AddRange(entities);
        return diagram;
    }

    /// <summary>主キー <c>id</c> と、指定した int の列を持つ表</summary>
    private static Entity Table(string name, params string[] columns)
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

        foreach (var column in columns)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = column,
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                }
            );
        }

        return entity;
    }

    /// <summary>子の列から親の主キーへのリレーション</summary>
    private static Relationship Reference(
        Entity parent,
        Entity child,
        string childColumn,
        RelationshipType type,
        string? constraintName = null
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = type,
            ConstraintName = constraintName,
            ColumnPairs =
            [
                new RelationshipColumnPair(
                    parent.Columns.Single(column => column.IsPrimaryKey).Id,
                    child.Columns.Single(column => column.Name == childColumn).Id
                ),
            ],
        };

    /// <summary>生成したエンティティのナビゲーションプロパティの名前（名前順）</summary>
    private static string[] NavigationNames(Assembly assembly, string entityName) =>
        assembly
            .GetTypes()
            .Single(type => type.Name == entityName)
            .GetProperties()
            .Where(property =>
                property
                    .GetCustomAttributesData()
                    .Any(attribute =>
                        attribute.AttributeType.Name == "NavigationReferenceAttribute"
                    )
            )
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>生成アセンブリから QuickErDbContext を Sqlite 構成で作る（実接続はしない）</summary>
    private static DbContext CreateContext(Assembly assembly)
    {
        var contextType = assembly.GetTypes().Single(type => type.Name == "QuickErDbContext");
        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:").IgnoreServiceProviderCacheLimit();

        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static string Describe(CodeGenerationResult result) =>
        string.Join(
            " / ",
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}")
        );
}
