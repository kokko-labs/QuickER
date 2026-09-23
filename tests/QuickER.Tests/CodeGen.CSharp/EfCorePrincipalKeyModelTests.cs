using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 親列が主キーでない FK（UNIQUE 列参照・複合主キーの一部参照）で、生成 <c>QuickErDbContext</c> の
/// FK が <b>親の主キーでなくその親列へ結合する</b>ことを実際にモデルを組み立てて確認するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// EF Core の既定は「FK は親の主キーへ結合」で、<c>HasPrincipalKey</c> を出さないと親列が主キーでない
/// リレーションは主キーへ黙って結合される（CLR 型が同じなら Include の結果が誤ったまま緑・違えば
/// モデル検証例外で EF Core 経路が丸ごと使用不能）。ADO 側は列ペアどおりに結合するため、
/// 出さないままだと実装先で結果が食い違う。
/// </para>
/// <para>
/// 生成テキストの表明ではモデルの結合先まで届かないため、生成→Roslyn コンパイル→アセンブリロード→
/// <c>DbContext.Model</c> の実アクセスで <c>fk.PrincipalKey.Properties</c> を確認する
/// （<see cref="ValueObjectForeignKeyEfCoreModelTests"/> と同じ流儀。実 DB は不要）。
/// </para>
/// </remarks>
public class EfCorePrincipalKeyModelTests
{
    /// <summary>
    /// UNIQUE 列参照（1 対多・1 対 1）と複合主キーの一部参照を含む図。
    /// customers.code（UNIQUE・非 PK）を orders / profiles が参照し、
    /// parents(part_a, part_b) の複合主キーの一部 part_a を children が参照する
    /// </summary>
    private static ErDiagram BuildDiagram()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customerCode = new Column
        {
            Name = "code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var customers = new Entity
        {
            TableName = "customers",
            Columns = { customerId, customerCode },
        };
        customers.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [customerCode.Id] });

        var orderId = new Column
        {
            Name = "order_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerCode = new Column
        {
            Name = "customer_code",
            DataType = "nvarchar(20)",
            IsForeignKey = true,
            IsNullable = true,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerCode } };

        var profileId = new Column
        {
            Name = "profile_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var profileCustomerCode = new Column
        {
            Name = "customer_code",
            DataType = "nvarchar(20)",
            IsForeignKey = true,
            IsNullable = true,
        };
        var profiles = new Entity
        {
            TableName = "profiles",
            Columns = { profileId, profileCustomerCode },
        };

        var partA = new Column
        {
            Name = "part_a",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var partB = new Column
        {
            Name = "part_b",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parents = new Entity { TableName = "parents", Columns = { partA, partB } };

        var childId = new Column
        {
            Name = "child_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childPartA = new Column
        {
            Name = "part_a_ref",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };
        var children = new Entity { TableName = "children", Columns = { childId, childPartA } };

        return new ErDiagram
        {
            Entities = { customers, orders, profiles, parents, children },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerCode.Id, orderCustomerCode.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = customers.Id,
                    TargetEntityId = profiles.Id,
                    ColumnPairs = { new(customerCode.Id, profileCustomerCode.Id) },
                },
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = parents.Id,
                    TargetEntityId = children.Id,
                    ColumnPairs = { new(partA.Id, childPartA.Id) },
                },
            },
        };
    }

    /// <summary>EF Core 単独出力のオプション（モデル構築の検証対象を EF Core に絞る）</summary>
    private static CodeGenerationOptions CreateOptions() =>
        new()
        {
            RootNamespace = "Test.EfCorePk",
            GenerateEfCoreRepositories = true,
            GenerateRepositories = false,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

    [Fact(DisplayName = "親列が主キーでない FK は HasPrincipalKey でその親列へ結合する")]
    public void 親列が主キーでないFKはその親列へ結合する()
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            BuildDiagram(),
            CreateOptions()
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.EfCorePrincipalKey.Tests.{Guid.NewGuid():N}"
        );

        using var context = CreateContext(assembly);

        // Model へのアクセスでモデル構築＋検証が走る
        var model = context.Model;

        // 1 対多: orders.CustomerCode → customers.Code（主キー Id ではない）
        var orderFk = model.FindEntityType("Test.EfCorePk.OrderEntity")!.GetForeignKeys().Single();
        orderFk.Properties.Select(p => p.Name).Should().Equal("CustomerCode");
        orderFk.PrincipalKey.Properties.Select(p => p.Name).Should().Equal("Code");
        orderFk.PrincipalKey.IsPrimaryKey().Should().BeFalse();

        // 1 対 1: profiles.CustomerCode → customers.Code
        var profileFk = model
            .FindEntityType("Test.EfCorePk.ProfileEntity")!
            .GetForeignKeys()
            .Single();
        profileFk.PrincipalKey.Properties.Select(p => p.Name).Should().Equal("Code");
        profileFk.PrincipalKey.IsPrimaryKey().Should().BeFalse();

        // 複合主キーの一部参照: children.PartARef → parents.PartA（複合 PK (PartA, PartB) ではない）
        // （簡易単数形化は "children" を変換しないためクラス名は ChildrenEntity）
        var childFk = model
            .FindEntityType("Test.EfCorePk.ChildrenEntity")!
            .GetForeignKeys()
            .Single();
        childFk.PrincipalKey.Properties.Select(p => p.Name).Should().Equal("PartA");
        childFk.PrincipalKey.IsPrimaryKey().Should().BeFalse();
    }

    /// <summary>親列が単一主キーそのものの FK には HasPrincipalKey を出さない（従来出力の不変）</summary>
    [Fact(DisplayName = "親列が主キーそのものの FK には HasPrincipalKey を出さない")]
    public void 親列が主キーのFKにはHasPrincipalKeyを出さない()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };
        var orderId = new Column
        {
            Name = "order_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerId } };
        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    ColumnPairs = { new(customerId.Id, orderCustomerId.Id) },
                },
            },
        };

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            CreateOptions()
        );

        result.HasErrors.Should().BeFalse();
        string.Join("\n", result.Files.Select(f => f.Content))
            .Should()
            .NotContain("HasPrincipalKey");
    }

    /// <summary>
    /// 図の参照アクション（ON DELETE）が EF Core の DeleteBehavior へ実挙動に基づいて写像されることを
    /// 実モデルで検証する。従来は Cascade 以外がすべて Restrict に畳まれ、SET DEFAULT の図では EF が
    /// 追跡中の子の FK を null へ更新して DB の既定値設定を横取りしていた（ClientNoAction なら EF は
    /// 子へ触れず DELETE だけを送り、DB 側の宣言がそのまま効く＝実測）
    /// </summary>
    [Fact(DisplayName = "参照アクションは DeleteBehavior へ実挙動どおり写像される")]
    public void 参照アクションはDeleteBehaviorへ写像される()
    {
        var hubId = new Column
        {
            Name = "hub_id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var hub = new Entity { TableName = "hub", Columns = { hubId } };

        var diagram = new ErDiagram { Entities = { hub } };
        var expectations = new (
            string Table,
            ForeignKeyReferentialAction Action,
            DeleteBehavior Expected
        )[]
        {
            ("cascade_child", ForeignKeyReferentialAction.Cascade, DeleteBehavior.Cascade),
            ("setnull_child", ForeignKeyReferentialAction.SetNull, DeleteBehavior.SetNull),
            (
                "setdefault_child",
                ForeignKeyReferentialAction.SetDefault,
                DeleteBehavior.ClientNoAction
            ),
            ("noaction_child", ForeignKeyReferentialAction.NoAction, DeleteBehavior.NoAction),
        };

        foreach (var (table, action, _) in expectations)
        {
            var childId = new Column
            {
                Name = "row_id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
            var fkColumn = new Column
            {
                Name = "hub_id",
                DataType = "int",
                IsForeignKey = true,
                IsNullable = true,
            };
            var child = new Entity { TableName = table, Columns = { childId, fkColumn } };
            diagram.Entities.Add(child);
            diagram.Relationships.Add(
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = hub.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = { new(hubId.Id, fkColumn.Id) },
                    OnDelete = action,
                }
            );
        }

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            CreateOptions()
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var assembly = GeneratedCodeCompiler.CompileAndLoad(
            result,
            $"QuickER.EfCoreDeleteBehavior.Tests.{Guid.NewGuid():N}"
        );

        using var context = CreateContext(assembly);
        var model = context.Model;

        foreach (var (table, _, expected) in expectations)
        {
            var className = "Test.EfCorePk." + ToPascal(table) + "Entity";
            var fk = model.FindEntityType(className)!.GetForeignKeys().Single();
            fk.DeleteBehavior.Should().Be(expected, $"{table} の参照アクションの写像");
        }

        static string ToPascal(string tableName) =>
            string.Concat(
                tableName.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            );
    }

    /// <summary>生成アセンブリから QuickErDbContext を Sqlite 構成で生成する（実接続はしない）</summary>
    private static DbContext CreateContext(Assembly assembly)
    {
        var contextType =
            assembly.GetTypes().SingleOrDefault(type => type.Name == "QuickErDbContext")
            ?? throw new InvalidOperationException("QuickErDbContext が生成されていない");

        var builder = (DbContextOptionsBuilder)
            Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType)
            )!;
        builder.UseSqlite("Data Source=:memory:");

        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }
}
