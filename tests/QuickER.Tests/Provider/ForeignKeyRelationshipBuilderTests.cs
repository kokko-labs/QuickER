using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="ForeignKeyRelationshipBuilder"/> の外部キー集約・1 対 1 / 1 対多 判定・列解決を検証するテストクラス
/// （通常は Docker 依存の統合テスト経由でのみ通る純ロジックを直接検証する）
/// </summary>
public class ForeignKeyRelationshipBuilderTests
{
    /// <summary>列名・主キー指定からテスト用カラムを生成する</summary>
    private static Column Col(string name, bool pk = false) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = pk,
        };

    /// <summary>テーブルキー・テーブル名・カラムから取込用エントリを生成する（ColumnsByName も同時に索引化する）</summary>
    private static SchemaTableEntry Table(string key, string tableName, params Column[] columns)
    {
        var entry = new SchemaTableEntry
        {
            Key = key,
            Entity = new Entity { TableName = tableName },
        };

        foreach (var c in columns)
        {
            entry.Entity.Columns.Add(c);
            entry.ColumnsByName[c.Name] = c;
        }

        return entry;
    }

    /// <summary>空の一意制約集合を返す</summary>
    private static IReadOnlyDictionary<string, List<string[]>> NoUniqueSets() =>
        new Dictionary<string, List<string[]>>();

    /// <summary>単一列 FK が 1 対多として参照先起点で構築され、両側の列 ID が解決されることを検証する</summary>
    [Fact(DisplayName = "単一列 FK は 1 対多になり参照先を起点とする")]
    public void SingleColumnForeignKey_BuildsOneToMany()
    {
        var customerId = Col("Id", pk: true);
        var customer = Table("Customer", "Customer", customerId);

        var orderFk = Col("CustomerId");
        var order = Table("Order", "Order", Col("Id", pk: true), orderFk);

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Customer"] = customer,
            ["Order"] = order,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Order_Customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        var rel = rels.Should().ContainSingle().Which;
        rel.Type.Should().Be(RelationshipType.OneToMany);
        // 参照先 (PK 側) が起点、FK 保有テーブルが終点
        rel.SourceEntityId.Should().Be(customer.Entity.Id);
        rel.TargetEntityId.Should().Be(order.Entity.Id);
        rel.SourceColumnId.Should().Be(customerId.Id);
        rel.TargetColumnId.Should().Be(orderFk.Id);
        rel.ConstraintName.Should().Be("FK_Order_Customer");
        // FK 保有列に IsForeignKey フラグが立つ
        orderFk.IsForeignKey.Should().BeTrue();
    }

    /// <summary>同一制約名の複数行が複合 FK として集約され、単一列でないため列 ID が解決されないことを検証する</summary>
    [Fact(DisplayName = "同一制約名の複数行は複合 FK として集約される")]
    public void CompositeForeignKey_AggregatesByConstraintName()
    {
        var child = Table("Child", "Child", Col("Id", pk: true), Col("AId"), Col("BId"));
        var parent = Table("Parent", "Parent", Col("A", pk: true), Col("B", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Child"] = child,
            ["Parent"] = parent,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "AId",
            "Parent",
            "A",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "BId",
            "Parent",
            "B",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        var rel = rels.Should().ContainSingle().Which;
        // FK 列集合 (AId,BId) は Child の PK (Id) と一致しないため 1 対多
        rel.Type.Should().Be(RelationshipType.OneToMany);
        // 複数列 FK では代表列 ID を持たない
        rel.SourceColumnId.Should().BeNull();
        rel.TargetColumnId.Should().BeNull();
        rel.ConstraintName.Should().Be("FK_Child_Parent");
        child.ColumnsByName["AId"].IsForeignKey.Should().BeTrue();
        child.ColumnsByName["BId"].IsForeignKey.Should().BeTrue();
    }

    /// <summary>複合 FK の取込で、制約名・子テーブル・列ペアを備えた劣化警告が生成されることを検証する</summary>
    [Fact(DisplayName = "複合 FK は列対応喪失の警告を生成する")]
    public void CompositeForeignKey_ProducesWarning()
    {
        var child = Table("Child", "ChildTable", Col("Id", pk: true), Col("AId"), Col("BId"));
        var parent = Table("Parent", "ParentTable", Col("A", pk: true), Col("B", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Child"] = child,
            ["Parent"] = parent,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "AId",
            "Parent",
            "A",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "BId",
            "Parent",
            "B",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        builder.Build(tables, NoUniqueSets());

        var warning = builder.CompositeForeignKeyWarnings.Should().ContainSingle().Which;
        warning.ConstraintName.Should().Be("FK_Child_Parent");
        // テーブルはキーではなくエンティティのテーブル名で報告する
        warning.ChildTable.Should().Be("ChildTable");
        warning.ParentTable.Should().Be("ParentTable");
        // 列は投入順（＝序数順）で子側・親側が対応する
        warning.ChildColumns.Should().Equal("AId", "BId");
        warning.ParentColumns.Should().Equal("A", "B");
    }

    /// <summary>単一列 FK だけの取込では劣化警告が生成されないことを検証する</summary>
    [Fact(DisplayName = "単一列 FK では警告を生成しない")]
    public void SingleColumnForeignKey_ProducesNoWarning()
    {
        var customer = Table("Customer", "Customer", Col("Id", pk: true));
        var order = Table("Order", "Order", Col("Id", pk: true), Col("CustomerId"));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Customer"] = customer,
            ["Order"] = order,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Order_Customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        builder.Build(tables, NoUniqueSets());

        builder.CompositeForeignKeyWarnings.Should().BeEmpty();
    }

    /// <summary>FK 列が FK 保有テーブルの主キーと一致する場合に 1 対 1 と判定されることを検証する</summary>
    [Fact(DisplayName = "FK 列が主キーと一致すれば 1 対 1")]
    public void ForeignKeyMatchingPrimaryKey_IsOneToOne()
    {
        // UserProfile の PK 兼 FK 列 UserId が User.Id を参照する共有主キー 1 対 1
        var profile = Table("UserProfile", "UserProfile", Col("UserId", pk: true));
        var user = Table("User", "User", Col("Id", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["UserProfile"] = profile,
            ["User"] = user,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_UserProfile_User",
            "UserProfile",
            "UserId",
            "User",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        rels.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.OneToOne);
    }

    /// <summary>FK 列が一意制約と一致する場合に 1 対 1 と判定されることを検証する</summary>
    [Fact(DisplayName = "FK 列が一意制約と一致すれば 1 対 1")]
    public void ForeignKeyMatchingUniqueConstraint_IsOneToOne()
    {
        // FK 列 ProfileId は PK ではないが一意制約が張られている
        var owner = Table("Owner", "Owner", Col("Id", pk: true), Col("ProfileId"));
        var profile = Table("Profile", "Profile", Col("Id", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Owner"] = owner,
            ["Profile"] = profile,
        };
        var uniqueSets = new Dictionary<string, List<string[]>>
        {
            ["Owner"] = new List<string[]> { new[] { "ProfileId" } },
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Owner_Profile",
            "Owner",
            "ProfileId",
            "Profile",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, uniqueSets);

        rels.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.OneToOne);
    }

    /// <summary>FK 列が主キーでも一意でもない場合に 1 対多と判定されることを検証する</summary>
    [Fact(DisplayName = "FK 列が主キーでも一意でもなければ 1 対多")]
    public void ForeignKeyNotUnique_IsOneToMany()
    {
        var order = Table("Order", "Order", Col("Id", pk: true), Col("CustomerId"));
        var customer = Table("Customer", "Customer", Col("Id", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Order"] = order,
            ["Customer"] = customer,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Order_Customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        rels.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.OneToMany);
    }

    /// <summary>別々の制約名は独立したリレーションへ分離され、投入順が保持されることを検証する</summary>
    [Fact(DisplayName = "複数の制約は投入順を保って分離される")]
    public void MultipleConstraints_AreSeparatedInInsertionOrder()
    {
        var order = Table(
            "Order",
            "Order",
            Col("Id", pk: true),
            Col("CustomerId"),
            Col("ShipperId")
        );
        var customer = Table("Customer", "Customer", Col("Id", pk: true));
        var shipper = Table("Shipper", "Shipper", Col("Id", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Order"] = order,
            ["Customer"] = customer,
            ["Shipper"] = shipper,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Order_Customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "FK_Order_Shipper",
            "Order",
            "ShipperId",
            "Shipper",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        rels.Should().HaveCount(2);
        rels.Select(r => r.ConstraintName).Should().Equal("FK_Order_Customer", "FK_Order_Shipper");
    }

    /// <summary>異なる子テーブルが同名の制約を持つ場合でも別々のリレーションとして構築され、各々の列 ID が解決されることを検証する</summary>
    [Fact(DisplayName = "異なる子テーブルの同名制約は別々のリレーションになる")]
    public void SameConstraintName_OnDifferentChildTables_AreSeparated()
    {
        // PostgreSQL 等では制約名の一意性がテーブル単位のため、別テーブルが同名 FK 制約 "fk_customer" を持ちうる
        var customer = Table("Customer", "Customer", Col("Id", pk: true));
        var order = Table("Order", "Order", Col("Id", pk: true), Col("CustomerId"));
        var invoice = Table("Invoice", "Invoice", Col("Id", pk: true), Col("CustomerId"));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Customer"] = customer,
            ["Order"] = order,
            ["Invoice"] = invoice,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "fk_customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "fk_customer",
            "Invoice",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        // 制約名が同じでも子テーブルが異なるため、2 件のリレーションへ分離される
        rels.Should().HaveCount(2);
        rels.Should().OnlyContain(r => r.ConstraintName == "fk_customer");

        var orderRel = rels.Should().ContainSingle(r => r.TargetEntityId == order.Entity.Id).Which;
        orderRel.SourceEntityId.Should().Be(customer.Entity.Id);
        orderRel.Type.Should().Be(RelationshipType.OneToMany);
        orderRel.SourceColumnId.Should().Be(customer.ColumnsByName["Id"].Id);
        orderRel.TargetColumnId.Should().Be(order.ColumnsByName["CustomerId"].Id);

        var invoiceRel = rels.Should()
            .ContainSingle(r => r.TargetEntityId == invoice.Entity.Id)
            .Which;
        invoiceRel.SourceEntityId.Should().Be(customer.Entity.Id);
        invoiceRel.Type.Should().Be(RelationshipType.OneToMany);
        invoiceRel.SourceColumnId.Should().Be(customer.ColumnsByName["Id"].Id);
        invoiceRel.TargetColumnId.Should().Be(invoice.ColumnsByName["CustomerId"].Id);

        // 双方の子テーブルの FK 列にフラグが立つ（片方だけに混線していない）
        order.ColumnsByName["CustomerId"].IsForeignKey.Should().BeTrue();
        invoice.ColumnsByName["CustomerId"].IsForeignKey.Should().BeTrue();
    }

    /// <summary>参照先または FK 保有テーブルが解決できない FK はスキップされることを検証する</summary>
    [Fact(DisplayName = "解決できないテーブル参照の FK はスキップされる")]
    public void UnresolvableTable_IsSkipped()
    {
        var order = Table("Order", "Order", Col("Id", pk: true), Col("CustomerId"));
        // Customer テーブルを tables に登録しない → 参照解決に失敗する
        var tables = new Dictionary<string, SchemaTableEntry> { ["Order"] = order };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Order_Customer",
            "Order",
            "CustomerId",
            "Customer",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables, NoUniqueSets());

        rels.Should().BeEmpty();
    }

    /// <summary>同一 FK では最初の行の参照アクションが採用され、後続行の値は無視されることを検証する</summary>
    [Fact(DisplayName = "参照アクションは同一 FK の初回行が採用される")]
    public void ReferentialAction_UsesFirstRow()
    {
        var child = Table("Child", "Child", Col("Id", pk: true), Col("AId"), Col("BId"));
        var parent = Table("Parent", "Parent", Col("A", pk: true), Col("B", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Child"] = child,
            ["Parent"] = parent,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        // 初回行に Cascade/Cascade を投入
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "AId",
            "Parent",
            "A",
            ForeignKeyReferentialAction.Cascade,
            ForeignKeyReferentialAction.Cascade
        );
        // 後続行の SetNull は無視されるべき
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "BId",
            "Parent",
            "B",
            ForeignKeyReferentialAction.SetNull,
            ForeignKeyReferentialAction.SetNull
        );

        var rel = builder.Build(tables, NoUniqueSets()).Should().ContainSingle().Which;

        rel.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        rel.OnUpdate.Should().Be(ForeignKeyReferentialAction.Cascade);
    }

    /// <summary>行を 1 件も投入しなければ空のリレーション一覧になることを検証する</summary>
    [Fact(DisplayName = "投入なしなら空のリレーション一覧")]
    public void NoInput_ReturnsEmpty()
    {
        var builder = new ForeignKeyRelationshipBuilder();

        builder
            .Build(new Dictionary<string, SchemaTableEntry>(), NoUniqueSets())
            .Should()
            .BeEmpty();
    }

    /// <summary>複合 FK の列順序が異なっても大文字小文字無視の集合一致で 1 対 1 判定されることを検証する</summary>
    [Fact(DisplayName = "複合 FK の列順が主キーと逆でも集合一致で 1 対 1")]
    public void CompositeForeignKey_OrderInsensitiveSetMatch()
    {
        // Child の PK は (A,B)。FK 列は逆順 (B,A) かつ大小混在で投入する
        var child = Table("Child", "Child", Col("A", pk: true), Col("B", pk: true));
        var parent = Table("Parent", "Parent", Col("PA", pk: true), Col("PB", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Child"] = child,
            ["Parent"] = parent,
        };

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "b",
            "Parent",
            "PB",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "FK_Child_Parent",
            "Child",
            "a",
            "Parent",
            "PA",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rel = builder.Build(tables, NoUniqueSets()).Should().ContainSingle().Which;

        rel.Type.Should().Be(RelationshipType.OneToOne);
    }

    /// <summary><see cref="ForeignKeyRelationshipBuilder.SameSet"/> の集合一致（空・大小無視・長さ差）を検証する</summary>
    [Fact(DisplayName = "SameSet: 空集合は不一致・大小無視で一致・長さ差は不一致")]
    public void SameSet_ComparesCaseInsensitiveNonEmptySets()
    {
        // 空集合同士は不一致
        ForeignKeyRelationshipBuilder.SameSet([], []).Should().BeFalse();
        // 大文字小文字を無視して一致
        ForeignKeyRelationshipBuilder.SameSet(["a", "b"], ["A", "B"]).Should().BeTrue();
        // 長さが異なれば不一致
        ForeignKeyRelationshipBuilder.SameSet(["a"], ["a", "b"]).Should().BeFalse();
        // 同じ長さでも要素が違えば不一致
        ForeignKeyRelationshipBuilder.SameSet(["a", "b"], ["a", "c"]).Should().BeFalse();
    }
}

/// <summary><see cref="UniqueColumnSetBuilder"/> の一意制約列集合の集約を検証するテストクラス（FK の 1 対 1 判定へ供給する材料）</summary>
public class UniqueColumnSetBuilderTests
{
    /// <summary>単一制約の複数列が 1 つの列配列（大小無視の昇順）へ集約されることを検証する</summary>
    [Fact(DisplayName = "単一制約の複数列は昇順の配列へ集約される")]
    public void SingleConstraint_AggregatesColumnsSorted()
    {
        var builder = new UniqueColumnSetBuilder();
        builder.Add("T", "UQ_T", "b");
        builder.Add("T", "UQ_T", "A");

        var result = builder.Build();

        result.Should().ContainKey("T");
        result["T"].Should().ContainSingle().Which.Should().Equal("A", "b");
    }

    /// <summary>同一テーブルの複数制約がそれぞれ独立した配列として保持されることを検証する</summary>
    [Fact(DisplayName = "同一テーブルの複数制約は別々の配列になる")]
    public void MultipleConstraints_KeptSeparate()
    {
        var builder = new UniqueColumnSetBuilder();
        builder.Add("T", "UQ_1", "X");
        builder.Add("T", "UQ_2", "Y");

        var result = builder.Build();

        result["T"].Should().HaveCount(2);
        result["T"].Should().ContainEquivalentOf(new[] { "X" });
        result["T"].Should().ContainEquivalentOf(new[] { "Y" });
    }

    /// <summary>投入がなければ空辞書を返すことを検証する</summary>
    [Fact(DisplayName = "投入なしなら空辞書")]
    public void NoInput_ReturnsEmpty()
    {
        new UniqueColumnSetBuilder().Build().Should().BeEmpty();
    }
}
