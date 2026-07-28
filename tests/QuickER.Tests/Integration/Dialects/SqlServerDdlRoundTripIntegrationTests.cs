using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// A: <see cref="SqlServerDdlGenerator"/> が生成した DDL を実 SQL Server に流し、
/// <see cref="SqlServerSchemaImporter"/> で取り込んだ結果が元の図と一致することを検証する統合テスト。
/// </summary>
/// <remarks>
/// テーブル・列の説明が拡張プロパティ <c>MS_Description</c> として DDL に乗り、取込で往復一致することを重点的に確認する。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerDdlRoundTripIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 3 テーブル（FK 2 本[Cascade / SetNull]・NULL 混在・日本語名 1 組・テーブル/列の説明つき）の DDL を生成・実行し、
    /// 取込結果がテーブル / 列 / 型 / NULL / PK / FK / 参照アクション / 1対多・1対1 判定・説明まで一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] A: DDL 生成→実行→取込で図が往復一致する（FK・日本語・NULL混在・説明）"
    )]
    public async Task DdlToImport_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ---------- 図の定義 ----------
        // 親: 顧客（日本語テーブル名・日本語列名の 1 組・テーブル/列の説明つき。説明はシングルクォート込みで往復検証）
        var customer = new Entity { TableName = "顧客", Description = "顧客マスタ（It's）" };
        var customerId = new Column
        {
            Name = "顧客ID",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "顧客の識別子",
        };
        customer.Columns.Add(customerId);
        customer.Columns.Add(
            new Column
            {
                Name = "氏名",
                DataType = "nvarchar(50)",
                IsNullable = false,
                Description = "氏名（フルネーム）",
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "備考",
                DataType = "nvarchar(max)",
                IsNullable = true,
            }
        );

        // 子1: orders（顧客への FK・ON DELETE CASCADE、単純 PK）
        var order = new Entity { TableName = "orders" };
        var orderId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsNullable = false,
        };
        order.Columns.Add(orderId);
        order.Columns.Add(orderCustomerId);
        order.Columns.Add(
            new Column
            {
                Name = "amount",
                DataType = "decimal(10,2)",
                IsNullable = true,
            }
        );

        // 子2: profiles（顧客への FK・ON DELETE SET NULL、customer_id が一意制約 → 1対1）
        var profile = new Entity { TableName = "profiles" };
        var profileId = new Column
        {
            Name = "id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var profileCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "int",
            IsNullable = true,
        };
        profile.Columns.Add(profileId);
        profile.Columns.Add(profileCustomerId);

        var relOrder = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customerId.Id,
            TargetColumnId = orderCustomerId.Id,
            ConstraintName = "FK_orders_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var relProfile = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = profile.Id,
            Type = RelationshipType.OneToOne,
            SourceColumnId = customerId.Id,
            TargetColumnId = profileCustomerId.Id,
            ConstraintName = "FK_profiles_customer",
            OnDelete = ForeignKeyReferentialAction.SetNull,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var diagram = new ErDiagram
        {
            Entities = { customer, order, profile },
            Relationships = { relOrder, relProfile },
        };

        // 1対1 判定のため、profiles.customer_id に一意制約を追加する DDL を後付けする
        var ddl =
            new SqlServerDdlGenerator().Build(diagram)
            + "\nALTER TABLE [profiles] ADD CONSTRAINT [UQ_profiles_customer] UNIQUE ([customer_id]);";

        // ---------- 実行 ----------
        await fixture.ExecuteAsync(ddl, Ct);

        // ---------- 取込 ----------
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

        // ---------- 検証: テーブル ----------
        result
            .Entities.Select(e => e.TableName)
            .Should()
            .BeEquivalentTo("顧客", "orders", "profiles");

        // 顧客テーブル: 列・型・NULL・PK
        var importedCustomer = result.Entities.Single(e => e.TableName == "顧客");
        importedCustomer
            .Columns.Select(c => (c.Name, c.DataType, c.IsNullable, c.IsPrimaryKey))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    ("顧客ID", "int", false, true),
                    ("氏名", "nvarchar(50)", false, false),
                    ("備考", "nvarchar(max)", true, false),
                }
            );

        // テーブル・列の説明が DDL → 実行 → 取込で往復一致する（シングルクォート込み）
        importedCustomer.Description.Should().Be("顧客マスタ（It's）");
        importedCustomer
            .Columns.Single(c => c.Name == "顧客ID")
            .Description.Should()
            .Be("顧客の識別子");
        importedCustomer
            .Columns.Single(c => c.Name == "氏名")
            .Description.Should()
            .Be("氏名（フルネーム）");

        // orders テーブル: decimal(10,2) の再現・FK 列フラグ
        var importedOrder = result.Entities.Single(e => e.TableName == "orders");
        importedOrder.Columns.Single(c => c.Name == "amount").DataType.Should().Be("decimal(10,2)");
        importedOrder.Columns.Single(c => c.Name == "customer_id").IsForeignKey.Should().BeTrue();
        importedOrder.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeTrue();

        // ---------- 検証: FK・参照アクション・多重度 ----------
        result.Relationships.Should().HaveCount(2);

        var orderRel = result.Relationships.Single(r => r.ConstraintName == "FK_orders_customer");
        orderRel.SourceEntityId.Should().Be(importedCustomer.Id);
        orderRel.TargetEntityId.Should().Be(importedOrder.Id);
        orderRel.Type.Should().Be(RelationshipType.OneToMany);
        orderRel.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        orderRel.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);

        var importedProfile = result.Entities.Single(e => e.TableName == "profiles");
        var profileRel = result.Relationships.Single(r =>
            r.ConstraintName == "FK_profiles_customer"
        );
        profileRel.SourceEntityId.Should().Be(importedCustomer.Id);
        profileRel.TargetEntityId.Should().Be(importedProfile.Id);
        // customer_id が一意制約を持つため 1 対 1 と判定される
        profileRel.Type.Should().Be(RelationshipType.OneToOne);
        profileRel.OnDelete.Should().Be(ForeignKeyReferentialAction.SetNull);
    }

    /// <summary>
    /// SSMS のダイアグラム機能等が作るツール用テーブル（拡張プロパティ
    /// <c>microsoft_database_tools_support</c> 付き）が取込対象から除外されることを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] A: sysdiagrams 等のツール用テーブルは取込から除外される")]
    public async Task Import_ExcludesDatabaseToolsSupportTables()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ユーザーテーブル 1 つと、実物の sysdiagrams と同じ構造・同じ拡張プロパティを持つテーブルを作る
        // （SSMS はこの拡張プロパティでオブジェクトエクスプローラーの表示からも除外している）
        const string ddl = """
            CREATE TABLE [dbo].[Customer] (
                [Id] int NOT NULL PRIMARY KEY,
                [Name] nvarchar(50) NOT NULL
            );

            CREATE TABLE [dbo].[sysdiagrams] (
                [name] sysname NOT NULL,
                [principal_id] int NOT NULL,
                [diagram_id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [version] int NULL,
                [definition] varbinary(max) NULL
            );

            EXEC sys.sp_addextendedproperty
                @name = N'microsoft_database_tools_support', @value = 1,
                @level0type = N'SCHEMA', @level0name = N'dbo',
                @level1type = N'TABLE',  @level1name = N'sysdiagrams';
            """;

        await fixture.ExecuteAsync(ddl, Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new SqlServerSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("Customer");
    }
}
