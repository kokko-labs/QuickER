using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using QuickER.Model;
using QuickER.Oracle;

namespace QuickER.Tests.Integration;

/// <summary>
/// A: <see cref="OracleDdlGenerator"/> が生成した DDL を実 Oracle に流し、
/// <see cref="OracleSchemaImporter"/> で取り込んだ結果が元の図と一致することを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleDdlRoundTripIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 3 テーブル（複合 PK 1・FK 2 本[Cascade / SetNull]・NULL 混在・日本語名 1 組）の DDL を生成・実行し、
    /// 取込結果がテーブル / 列 / 型 / NULL / PK / FK / 参照アクション / 1対多・1対1 判定まで一致することを検証する。
    /// </summary>
    /// <remarks>Oracle は ON UPDATE をサポートしないため検証対象外とする（OnUpdate は常に NoAction で取り込まれる）</remarks>
    [Fact(
        DisplayName = "[Integration] A: DDL 生成→実行→取込で図が往復一致する（複合PK・FK・日本語・NULL混在）"
    )]
    public async Task DdlToImport_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ---------- 図の定義 ----------
        // 親: 顧客（日本語テーブル名・日本語列名の 1 組）
        var customer = new Entity { TableName = "顧客" };
        var customerId = new Column
        {
            Name = "顧客ID",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        customer.Columns.Add(customerId);
        customer.Columns.Add(
            new Column
            {
                Name = "氏名",
                DataType = "VARCHAR2(50)",
                IsNullable = false,
            }
        );
        customer.Columns.Add(
            new Column
            {
                Name = "備考",
                DataType = "CLOB",
                IsNullable = true,
            }
        );

        // 子1: orders（顧客への FK・ON DELETE CASCADE、単純 PK）
        var order = new Entity { TableName = "orders" };
        var orderId = new Column
        {
            Name = "id",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var orderCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "NUMBER(10)",
            IsNullable = false,
        };
        order.Columns.Add(orderId);
        order.Columns.Add(orderCustomerId);
        order.Columns.Add(
            new Column
            {
                Name = "amount",
                DataType = "NUMBER(10,2)",
                IsNullable = true,
            }
        );

        // 子2: profiles（顧客への FK・ON DELETE SET NULL、customer_id が一意制約 → 1対1）
        var profile = new Entity { TableName = "profiles" };
        var profileId = new Column
        {
            Name = "id",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var profileCustomerId = new Column
        {
            Name = "customer_id",
            DataType = "NUMBER(10)",
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
            new OracleDdlGenerator().Build(diagram)
            + "\nALTER TABLE \"profiles\" ADD CONSTRAINT \"UQ_profiles_customer\" UNIQUE (\"customer_id\");";

        // ---------- 実行 ----------
        await fixture.ExecuteAsync(ddl, Ct);

        // ---------- 取込 ----------
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

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
                    ("顧客ID", "NUMBER(10)", false, true),
                    ("氏名", "VARCHAR2(50)", false, false),
                    ("備考", "CLOB", true, false),
                }
            );

        // orders テーブル: NUMBER(10,2) の再現・FK 列フラグ
        var importedOrder = result.Entities.Single(e => e.TableName == "orders");
        importedOrder.Columns.Single(c => c.Name == "amount").DataType.Should().Be("NUMBER(10,2)");
        importedOrder.Columns.Single(c => c.Name == "customer_id").IsForeignKey.Should().BeTrue();
        importedOrder.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeTrue();

        // ---------- 検証: FK・参照アクション・多重度 ----------
        result.Relationships.Should().HaveCount(2);

        var orderRel = result.Relationships.Single(r => r.ConstraintName == "FK_orders_customer");
        orderRel.SourceEntityId.Should().Be(importedCustomer.Id);
        orderRel.TargetEntityId.Should().Be(importedOrder.Id);
        orderRel.Type.Should().Be(RelationshipType.OneToMany);
        orderRel.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        // Oracle は ON UPDATE をサポートしないため常に NoAction で取り込まれる
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
    /// 型網羅: <see cref="OracleDataTypes"/> の代表型で列を作成→取込し、取り込んだ型文字列が
    /// <see cref="OracleTypeCatalog.TryParse"/> 可能かつ期待表記であることを検証する。
    /// </summary>
    /// <remarks>
    /// TIMESTAMP は Oracle の既定精度 6 が付与されるため <c>TIMESTAMP(6)</c> 等で取り込まれる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] A: 代表型を作成→取込し、型文字列が期待表記かつ TryParse 可能"
    )]
    public async Task TypeCoverage_ImportedTypeStringsAreParseable()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // (列名, 作成する型, 取込で期待される型表記)。作成型と期待表記は正規化により一致しないものがある
        var cases = new (string Column, string CreateType, string Expected)[]
        {
            ("c_number1", "NUMBER(1)", "NUMBER(1)"),
            ("c_number5", "NUMBER(5)", "NUMBER(5)"),
            ("c_number10", "NUMBER(10)", "NUMBER(10)"),
            ("c_number19", "NUMBER(19)", "NUMBER(19)"),
            ("c_numeric", "NUMBER(10,2)", "NUMBER(10,2)"),
            ("c_bfloat", "BINARY_FLOAT", "BINARY_FLOAT"),
            ("c_bdouble", "BINARY_DOUBLE", "BINARY_DOUBLE"),
            ("c_nvarchar2", "NVARCHAR2(50)", "NVARCHAR2(50)"),
            ("c_varchar2", "VARCHAR2(50)", "VARCHAR2(50)"),
            ("c_nchar", "NCHAR(10)", "NCHAR(10)"),
            ("c_char", "CHAR(10)", "CHAR(10)"),
            ("c_nclob", "NCLOB", "NCLOB"),
            ("c_clob", "CLOB", "CLOB"),
            ("c_raw", "RAW(16)", "RAW(16)"),
            ("c_blob", "BLOB", "BLOB"),
            ("c_date", "DATE", "DATE"),
            // TIMESTAMP は Oracle が既定精度 6 を付与するため TIMESTAMP(6) 等で取り込まれる
            ("c_timestamp", "TIMESTAMP", "TIMESTAMP(6)"),
            ("c_timestamptz", "TIMESTAMP WITH TIME ZONE", "TIMESTAMP(6) WITH TIME ZONE"),
            ("c_xml", "XMLTYPE", "XMLTYPE"),
        };

        var cols = string.Join(",\n", cases.Select(c => $"    \"{c.Column}\" {c.CreateType}"));
        await fixture.ExecuteAsync($"CREATE TABLE \"types_all\" (\n{cols}\n);", Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var table = result.Entities.Single(e => e.TableName == "types_all");
        var catalog = new OracleTypeCatalog();

        foreach (var (columnName, _, expected) in cases)
        {
            var col = table.Columns.Single(c => c.Name == columnName);
            col.DataType.Should().Be(expected, $"列 {columnName} の取込型表記");
            catalog
                .TryParse(col.DataType, out _)
                .Should()
                .BeTrue($"取込型 '{col.DataType}'（列 {columnName}）は TryParse 可能であること");
        }
    }
}
