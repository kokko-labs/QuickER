using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// A: <see cref="OracleDdlGenerator"/> が生成した DDL を実 Oracle に流し、
/// <see cref="OracleSchemaImporter"/> で取り込んだ結果が元の図と一致することを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
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
        // 親: 顧客（日本語テーブル名・日本語列名の 1 組・テーブル/列の説明つき。説明はシングルクォート込みで往復検証）
        var customer = new Entity { TableName = "顧客", Description = "顧客マスタ（It's）" };
        var customerId = new Column
        {
            Name = "顧客ID",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "顧客の識別子",
        };
        customer.Columns.Add(customerId);
        customer.Columns.Add(
            new Column
            {
                Name = "氏名",
                DataType = "VARCHAR2(50)",
                IsNullable = false,
                Description = "氏名（フルネーム）",
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
            ColumnPairs = [new(customerId.Id, orderCustomerId.Id)],
            ConstraintName = "FK_orders_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var relProfile = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = profile.Id,
            Type = RelationshipType.OneToOne,
            ColumnPairs = [new(customerId.Id, profileCustomerId.Id)],
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

    /// <summary>
    /// DROP したテーブルがごみ箱（リサイクルビン）に残す <c>BIN$...</c> テーブルが
    /// 取込対象から除外されることを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] A: ごみ箱（BIN$）のテーブルは取込から除外される")]
    public async Task Import_ExcludesRecycleBinTables()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // ユーザーテーブル 1 つと、DROP でごみ箱行きになるテーブルを作る
        // （PURGE なしの DROP は BIN$ 名（dropped = 'YES'）で user_tables に残る）
        await fixture.ExecuteAsync(
            """
            CREATE TABLE "CUSTOMER" ("ID" NUMBER(10) NOT NULL PRIMARY KEY);
            CREATE TABLE "TRASH" ("ID" NUMBER(10));
            DROP TABLE "TRASH";
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("CUSTOMER");
    }

    /// <summary>
    /// 名前付き単一列 UNIQUE と名前なし複合 UNIQUE を持つテーブルの DDL を生成・実行し、
    /// 取込んだ <see cref="Entity.UniqueConstraints"/> が制約名・構成列・宣言順まで一致することを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] A: UNIQUE 制約が DDL 生成→実行→取込で往復一致する")]
    public async Task UniqueConstraints_RoundTrip()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var inventory = new Entity { TableName = "inventory" };
        var id = new Column
        {
            Name = "id",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var sku = new Column
        {
            Name = "sku",
            DataType = "VARCHAR2(30)",
            IsNullable = false,
        };
        var warehouse = new Column
        {
            Name = "warehouse",
            DataType = "VARCHAR2(10)",
            IsNullable = false,
        };
        var slot = new Column
        {
            Name = "slot",
            DataType = "NUMBER(10)",
            IsNullable = false,
        };
        inventory.Columns.AddRange([id, sku, warehouse, slot]);

        // 名前付き単一列 UNIQUE
        inventory.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_inventory_sku", ColumnIds = [sku.Id] }
        );
        // 名前なし複合 UNIQUE（合成名 UQ_inventory_warehouse_slot で出力される）
        inventory.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [warehouse.Id, slot.Id] }
        );

        var diagram = new ErDiagram { Entities = { inventory } };

        await fixture.ExecuteAsync(new OracleDdlGenerator().Build(diagram), Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var imported = result.Entities.Single(e => e.TableName == "inventory");
        var columnNamesById = imported.Columns.ToDictionary(c => c.Id, c => c.Name);

        imported.UniqueConstraints.Should().HaveCount(2);

        var named = imported.UniqueConstraints.Single(u => u.Name == "UQ_inventory_sku");
        named.ColumnIds.Select(cid => columnNamesById[cid]).Should().Equal("sku");

        var composite = imported.UniqueConstraints.Single(u =>
            u.Name == "UQ_inventory_warehouse_slot"
        );
        // 宣言順（warehouse → slot）が取込でも保たれる
        composite
            .ColumnIds.Select(cid => columnNamesById[cid])
            .Should()
            .Equal("warehouse", "slot");
    }

    /// <summary>
    /// 複合主キーの親と複合外部キーの子を実 DB へ流し、構成列ペアが宣言順のまま往復することを検証する。
    /// </summary>
    /// <remarks>
    /// 意味モデルが列ペアの一覧で外部キーを表現するようになったため、複合外部キーも劣化せず往復する。
    /// </remarks>
    [Fact(DisplayName = "[Integration] A: 複合 FK が構成列ペアごと往復一致する")]
    public async Task CompositeForeignKey_RoundTrips()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        // 親: 複合主キー (a, b)
        var parent = new Entity { TableName = "composite_parent" };
        var parentA = new Column
        {
            Name = "a",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentB = new Column
        {
            Name = "b",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        parent.Columns.AddRange([parentA, parentB]);

        // 子: 複合外部キー (a_ref, b_ref) → 親 (a, b)
        var child = new Entity { TableName = "composite_child" };
        var childId = new Column
        {
            Name = "id",
            DataType = "NUMBER(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childA = new Column
        {
            Name = "a_ref",
            DataType = "NUMBER(10)",
            IsNullable = false,
        };
        var childB = new Column
        {
            Name = "b_ref",
            DataType = "NUMBER(10)",
            IsNullable = false,
        };
        child.Columns.AddRange([childId, childA, childB]);

        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentA.Id, childA.Id), new(parentB.Id, childB.Id)],
            ConstraintName = "FK_composite_child_composite_parent",
        };

        var diagram = new ErDiagram { Entities = { parent, child }, Relationships = { rel } };
        var ddl = new OracleDdlGenerator().Build(diagram);

        await fixture.ExecuteAsync(ddl, Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);
        var importedParent = result.Entities.Single(e => e.TableName == "composite_parent");
        var importedChild = result.Entities.Single(e => e.TableName == "composite_child");
        var parentColumnNames = importedParent.Columns.ToDictionary(c => c.Id, c => c.Name);
        var childColumnNames = importedChild.Columns.ToDictionary(c => c.Id, c => c.Name);

        // 複合外部キーは 1 本のリレーションとして取り込まれる（構成列ごとに分裂しない）
        var importedRel = result.Relationships.Should().ContainSingle().Which;
        importedRel.SourceEntityId.Should().Be(importedParent.Id);
        importedRel.TargetEntityId.Should().Be(importedChild.Id);
        // FK 列集合 (a_ref, b_ref) は子の主キー (id) と一致しないため 1 対多
        importedRel.Type.Should().Be(RelationshipType.OneToMany);

        // 構成列ペアは宣言順（a→a_ref, b→b_ref）のまま復元される
        importedRel
            .ColumnPairs.Select(p =>
                (parentColumnNames[p.SourceColumnId], childColumnNames[p.TargetColumnId])
            )
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));

        // 構成列すべてに外部キーフラグが立つ
        importedChild.Columns.Single(c => c.Name == "a_ref").IsForeignKey.Should().BeTrue();
        importedChild.Columns.Single(c => c.Name == "b_ref").IsForeignKey.Should().BeTrue();
    }
}
