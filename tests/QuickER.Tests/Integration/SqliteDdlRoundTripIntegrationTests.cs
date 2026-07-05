using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.Sqlite;

namespace QuickER.Tests.Integration;

/// <summary>
/// A: <see cref="SqliteDdlGenerator"/> が生成した DDL を一時ファイル SQLite DB に流し、
/// <see cref="SqliteSchemaImporter"/> で取り込んだ結果が元の図と一致することを検証する統合テスト。
/// </summary>
/// <remarks>
/// SQLite はインプロセス（Microsoft.Data.Sqlite）のため Docker / Testcontainers を使わず、
/// CI（windows-latest・Docker なし）でも常時実行される。他方言のような
/// <c>Assert.SkipUnless</c>（Docker 不在時スキップ）は入れない。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteDdlRoundTripIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 3 テーブル（複合 PK 1・FK 2 本[Cascade / SetNull]・NULL 混在・日本語名 1 組）の DDL を生成・実行し、
    /// 取込結果がテーブル / 列 / 宣言型 / NULL / PK / FK / 参照アクション / 1対多・1対1 判定まで一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] A: DDL 生成→実行→取込で図が往復一致する（複合PK・FK・日本語・NULL混在）"
    )]
    public async Task DdlToImport_RoundTrips()
    {
        using var db = SqliteTempDatabase.Create();

        // ---------- 図の定義 ----------
        // 親: 顧客（日本語テーブル名・日本語列名の 1 組）
        var customer = new Entity { TableName = "顧客" };
        var customerId = new Column
        {
            Name = "顧客ID",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        customer.Columns.Add(customerId);
        customer.Columns.Add(
            new Column
            {
                Name = "氏名",
                DataType = "NVARCHAR(50)",
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "備考",
                DataType = "NVARCHAR(MAX)",
                IsNullable = true,
            }
        );

        // 子1: orders（顧客への FK・ON DELETE CASCADE、単純 PK）
        var order = new Entity { TableName = "orders" };
        var orderId = new Column
        {
            Name = "id",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "INT",
            IsNullable = false,
        };
        order.Columns.Add(orderId);
        order.Columns.Add(orderCustomerId);
        order.Columns.Add(
            new Column
            {
                Name = "amount",
                DataType = "DECIMAL(10,2)",
                IsNullable = true,
            }
        );

        // 子2: profiles（顧客への FK・ON DELETE SET NULL、customer_id が一意制約 → 1対1）
        var profile = new Entity { TableName = "profiles" };
        var profileId = new Column
        {
            Name = "id",
            DataType = "INT",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var profileCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "INT",
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

        // 1対1 判定のため、profiles.customer_id に一意インデックスを追加する DDL を後付けする
        // （SQLite は ALTER ADD CONSTRAINT UNIQUE 非対応のため CREATE UNIQUE INDEX を使う）
        var ddl =
            new SqliteDdlGenerator().Build(diagram)
            + "\nCREATE UNIQUE INDEX \"UQ_profiles_customer\" ON \"profiles\" (\"customer_id\");";

        // ---------- 実行 ----------
        await db.ApplyDdlAsync(ddl, Ct);

        // ---------- 取込 ----------
        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        // ---------- 検証: テーブル ----------
        result
            .Entities.Select(e => e.TableName)
            .Should()
            .BeEquivalentTo("顧客", "orders", "profiles");

        // 顧客テーブル: 列・宣言型（verbatim）・NULL・PK
        var importedCustomer = result.Entities.Single(e => e.TableName == "顧客");
        importedCustomer
            .Columns.Select(c => (c.Name, c.DataType, c.IsNullable, c.IsPrimaryKey))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    ("顧客ID", "INT", false, true),
                    ("氏名", "NVARCHAR(50)", false, false),
                    ("備考", "NVARCHAR(MAX)", true, false),
                }
            );

        // orders テーブル: DECIMAL(10,2) の verbatim 再現・FK 列フラグ・PK
        var importedOrder = result.Entities.Single(e => e.TableName == "orders");
        importedOrder.Columns.Single(c => c.Name == "amount").DataType.Should().Be("DECIMAL(10,2)");
        importedOrder.Columns.Single(c => c.Name == "customer_id").IsForeignKey.Should().BeTrue();
        importedOrder.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeTrue();

        // ---------- 検証: FK・参照アクション・多重度 ----------
        result.Relationships.Should().HaveCount(2);

        // SQLite の FK は制約名を保持しないため、参照先ではなく親子エンティティで対応付ける
        var orderRel = result.Relationships.Single(r => r.TargetEntityId == importedOrder.Id);
        orderRel.SourceEntityId.Should().Be(importedCustomer.Id);
        orderRel.Type.Should().Be(RelationshipType.OneToMany);
        orderRel.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        orderRel.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);

        var importedProfile = result.Entities.Single(e => e.TableName == "profiles");
        var profileRel = result.Relationships.Single(r => r.TargetEntityId == importedProfile.Id);
        profileRel.SourceEntityId.Should().Be(importedCustomer.Id);
        // customer_id が一意インデックスを持つため 1 対 1 と判定される
        profileRel.Type.Should().Be(RelationshipType.OneToOne);
        profileRel.OnDelete.Should().Be(ForeignKeyReferentialAction.SetNull);
    }

    /// <summary>
    /// 往復無損失: SQL Server の代表的な宣言型を持つ図 → SQLite DDL → 実 DB 適用 → 取込で
    /// 宣言型が元の表記どおり読み戻せることを検証する。SQLite は宣言型を verbatim に保存するため
    /// 表記が一切変化しないことが期待値。加えて取込型が <see cref="SqliteTypeCatalog.TryParse"/> 可能であることも確認する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] A: SQL Server 代表宣言型が SQLite 経由で verbatim に往復する（TryParse 可能）"
    )]
    public async Task TypeCoverage_DeclaredTypesRoundTripVerbatim()
    {
        using var db = SqliteTempDatabase.Create();

        // SQL Server の代表宣言型を verbatim に維持できることを検証する（表記が一切変わらないのが期待値）
        var declaredTypes = new[]
        {
            "INT",
            "BIGINT",
            "SMALLINT",
            "TINYINT",
            "BIT",
            "DECIMAL(18,2)",
            "MONEY",
            "NVARCHAR(50)",
            "NVARCHAR(MAX)",
            "VARCHAR(100)",
            "NCHAR(10)",
            "CHAR(5)",
            "VARBINARY(MAX)",
            "BINARY(16)",
            "DATE",
            "TIME(3)",
            "DATETIME2(7)",
            "DATETIMEOFFSET",
            "UNIQUEIDENTIFIER",
            "XML",
            "FLOAT",
            "REAL",
        };

        // 各代表型を 1 列ずつ持つテーブルの図を組み立てる（列名は c0, c1, ... で決定的）
        var entity = new Entity { TableName = "types_all" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "INT",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        for (var i = 0; i < declaredTypes.Length; i++)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = $"c{i}",
                    DataType = declaredTypes[i],
                    IsNullable = true,
                }
            );
        }

        var diagram = new ErDiagram { Entities = { entity } };

        // ---------- DDL 生成→適用→取込 ----------
        var ddl = new SqliteDdlGenerator().Build(diagram);
        await db.ApplyDdlAsync(ddl, Ct);

        await using var conn = await db.OpenReadOnlyConnectionAsync(Ct);
        var result = await new SqliteSchemaImporter().ImportAsync(conn, Ct);

        var imported = result.Entities.Single(e => e.TableName == "types_all");
        var catalog = new SqliteTypeCatalog();

        for (var i = 0; i < declaredTypes.Length; i++)
        {
            var col = imported.Columns.Single(c => c.Name == $"c{i}");

            // 宣言型は SQLite が verbatim 保存するため元の表記どおり読み戻せる
            col.DataType.Should().Be(declaredTypes[i], $"列 c{i} の宣言型が verbatim に往復する");

            // 取込型は型カタログで解析可能であること（生成・型変換経路の健全性）
            catalog
                .TryParse(col.DataType, out _)
                .Should()
                .BeTrue($"取込型 '{col.DataType}'（列 c{i}）は TryParse 可能であること");
        }
    }
}
