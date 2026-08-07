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

    /// <summary>エンティティへ一意制約（構成列は列名で指定）を追加する</summary>
    private static void AddUnique(SchemaTableEntry entry, string? name, params string[] columns)
    {
        entry.Entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = name,
                ColumnIds = columns.Select(c => entry.ColumnsByName[c].Id).ToList(),
            }
        );
    }

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

        var rels = builder.Build(tables);

        var rel = rels.Should().ContainSingle().Which;
        rel.Type.Should().Be(RelationshipType.OneToMany);
        // 参照先 (PK 側) が起点、FK 保有テーブルが終点
        rel.SourceEntityId.Should().Be(customer.Entity.Id);
        rel.TargetEntityId.Should().Be(order.Entity.Id);
        var pair = rel.ColumnPairs.Should().ContainSingle().Which;
        pair.SourceColumnId.Should().Be(customerId.Id);
        pair.TargetColumnId.Should().Be(orderFk.Id);
        rel.ConstraintName.Should().Be("FK_Order_Customer");
        // FK 保有列に IsForeignKey フラグが立つ
        orderFk.IsForeignKey.Should().BeTrue();
    }

    /// <summary>同一制約名の複数行が複合 FK として集約され、全構成列が列ペアへ載ることを検証する</summary>
    /// <remarks>
    /// 意味モデルが複合外部キーを表現できるようになったため、かつての「単一列でないので列 ID を持たない」
    /// 劣化はもう起きない（列ペアが外部キー定義の正本）。
    /// </remarks>
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

        var rels = builder.Build(tables);

        var rel = rels.Should().ContainSingle().Which;
        // FK 列集合 (AId,BId) は Child の PK (Id) と一致しないため 1 対多
        rel.Type.Should().Be(RelationshipType.OneToMany);
        // 構成列は投入順（＝序数順）で親側・子側が対応する列ペアとして全て載る
        rel.ColumnPairs.Should().HaveCount(2);
        rel.ColumnPairs[0].SourceColumnId.Should().Be(parent.ColumnsByName["A"].Id);
        rel.ColumnPairs[0].TargetColumnId.Should().Be(child.ColumnsByName["AId"].Id);
        rel.ColumnPairs[1].SourceColumnId.Should().Be(parent.ColumnsByName["B"].Id);
        rel.ColumnPairs[1].TargetColumnId.Should().Be(child.ColumnsByName["BId"].Id);
        rel.ConstraintName.Should().Be("FK_Child_Parent");
        child.ColumnsByName["AId"].IsForeignKey.Should().BeTrue();
        child.ColumnsByName["BId"].IsForeignKey.Should().BeTrue();
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

        var rels = builder.Build(tables);

        rels.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.OneToOne);
    }

    /// <summary>FK 列がモデルの一意制約と一致する場合に 1 対 1 と判定されることを検証する</summary>
    [Fact(DisplayName = "FK 列がモデルの一意制約と一致すれば 1 対 1")]
    public void ForeignKeyMatchingUniqueConstraint_IsOneToOne()
    {
        // FK 列 ProfileId は PK ではないが一意制約が張られている
        var owner = Table("Owner", "Owner", Col("Id", pk: true), Col("ProfileId"));
        var profile = Table("Profile", "Profile", Col("Id", pk: true));

        // 判定材料はエンティティに載った UniqueConstraints（＝モデル正本）
        AddUnique(owner, "UQ_Owner_ProfileId", "ProfileId");

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["Owner"] = owner,
            ["Profile"] = profile,
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

        var rels = builder.Build(tables);

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

        var rels = builder.Build(tables);

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

        var rels = builder.Build(tables);

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

        var rels = builder.Build(tables);

        // 制約名が同じでも子テーブルが異なるため、2 件のリレーションへ分離される
        rels.Should().HaveCount(2);
        rels.Should().OnlyContain(r => r.ConstraintName == "fk_customer");

        var orderRel = rels.Should().ContainSingle(r => r.TargetEntityId == order.Entity.Id).Which;
        orderRel.SourceEntityId.Should().Be(customer.Entity.Id);
        orderRel.Type.Should().Be(RelationshipType.OneToMany);
        var orderPair = orderRel.ColumnPairs.Should().ContainSingle().Which;
        orderPair.SourceColumnId.Should().Be(customer.ColumnsByName["Id"].Id);
        orderPair.TargetColumnId.Should().Be(order.ColumnsByName["CustomerId"].Id);

        var invoiceRel = rels.Should()
            .ContainSingle(r => r.TargetEntityId == invoice.Entity.Id)
            .Which;
        invoiceRel.SourceEntityId.Should().Be(customer.Entity.Id);
        invoiceRel.Type.Should().Be(RelationshipType.OneToMany);
        var invoicePair = invoiceRel.ColumnPairs.Should().ContainSingle().Which;
        invoicePair.SourceColumnId.Should().Be(customer.ColumnsByName["Id"].Id);
        invoicePair.TargetColumnId.Should().Be(invoice.ColumnsByName["CustomerId"].Id);

        // 双方の子テーブルの FK 列にフラグが立つ（片方だけに混線していない）
        order.ColumnsByName["CustomerId"].IsForeignKey.Should().BeTrue();
        invoice.ColumnsByName["CustomerId"].IsForeignKey.Should().BeTrue();
    }

    /// <summary>テーブルキーに区切り文字「::」を含んでも一意制約集合が正しいテーブルへ紐付くことを検証する</summary>
    /// <remarks>
    /// 文字列連結キーを分解する実装では最初の「::」で誤切断され、一意制約集合が別キーへ紐付いて
    /// 1 対 1 判定が 1 対多へ劣化する（タプルキー化による構造的解消の回帰テスト）。
    /// </remarks>
    [Fact(DisplayName = "テーブルキーに :: を含んでも一意制約による 1 対 1 判定が効く")]
    public void TableKeyContainingSeparator_KeepsUniqueSetAssociation()
    {
        var owner = Table("db::Owner", "Owner", Col("Id", pk: true), Col("ProfileId"));
        var profile = Table("db::Profile", "Profile", Col("Id", pk: true));

        var tables = new Dictionary<string, SchemaTableEntry>
        {
            ["db::Owner"] = owner,
            ["db::Profile"] = profile,
        };

        var uniqueBuilder = new UniqueConstraintImportBuilder();
        uniqueBuilder.Add("db::Owner", "UQ_Owner_ProfileId", "ProfileId", "UQ_Owner_ProfileId");
        UniqueConstraintImportBuilder.Attach(tables, uniqueBuilder.Build());

        // 一意制約が「db」ではなく元のテーブルキーのエンティティへ載る
        owner
            .Entity.UniqueConstraints.Should()
            .ContainSingle()
            .Which.ColumnIds.Should()
            .Equal(owner.ColumnsByName["ProfileId"].Id);
        profile.Entity.UniqueConstraints.Should().BeEmpty();

        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "FK_Owner_Profile",
            "db::Owner",
            "ProfileId",
            "db::Profile",
            "Id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var rels = builder.Build(tables);

        rels.Should().ContainSingle().Which.Type.Should().Be(RelationshipType.OneToOne);
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

        var rels = builder.Build(tables);

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

        var rel = builder.Build(tables).Should().ContainSingle().Which;

        rel.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        rel.OnUpdate.Should().Be(ForeignKeyReferentialAction.Cascade);
    }

    /// <summary>行を 1 件も投入しなければ空のリレーション一覧になることを検証する</summary>
    [Fact(DisplayName = "投入なしなら空のリレーション一覧")]
    public void NoInput_ReturnsEmpty()
    {
        var builder = new ForeignKeyRelationshipBuilder();

        builder.Build(new Dictionary<string, SchemaTableEntry>()).Should().BeEmpty();
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

        var rel = builder.Build(tables).Should().ContainSingle().Which;

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

/// <summary>
/// <see cref="UniqueConstraintImportBuilder"/> の一意制約集約とモデルへの反映を検証するテストクラス
/// （5 方言のインポーターが共有する取込→モデル変換の正本）
/// </summary>
public class UniqueConstraintImportBuilderTests
{
    /// <summary>テーブルキー・テーブル名・列名から取込用エントリを生成する</summary>
    private static SchemaTableEntry Table(string key, params string[] columnNames)
    {
        var entry = new SchemaTableEntry
        {
            Key = key,
            Entity = new Entity { TableName = key },
        };

        foreach (var name in columnNames)
        {
            var column = new Column { Name = name, DataType = "int" };
            entry.Entity.Columns.Add(column);
            entry.ColumnsByName[name] = column;
        }

        return entry;
    }

    /// <summary>単一制約の複数列が宣言順（＝投入順）のまま保持されることを検証する</summary>
    /// <remarks>旧実装は列名をアルファベット順にソートしていたが、DDL へ書き戻すため宣言順が正本になった</remarks>
    [Fact(DisplayName = "単一制約の複数列は宣言順のまま保持される")]
    public void SingleConstraint_KeepsDeclarationOrder()
    {
        var builder = new UniqueConstraintImportBuilder();
        builder.Add("T", "UQ_T", "b", "UQ_T");
        builder.Add("T", "UQ_T", "A", "UQ_T");

        var result = builder.Build();

        var constraint = result["T"].Should().ContainSingle().Which;
        constraint.Name.Should().Be("UQ_T");
        constraint.ColumnNames.Should().Equal("b", "A");
    }

    /// <summary>同一テーブルの複数制約がそれぞれ独立した制約として投入順に保持されることを検証する</summary>
    [Fact(DisplayName = "同一テーブルの複数制約は投入順に分離される")]
    public void MultipleConstraints_KeptSeparateInInsertionOrder()
    {
        var builder = new UniqueConstraintImportBuilder();
        builder.Add("T", "UQ_1", "X", "UQ_1");
        builder.Add("T", "UQ_2", "Y", "UQ_2");

        var result = builder.Build();

        result["T"].Select(c => c.Name).Should().Equal("UQ_1", "UQ_2");
        result["T"][0].ColumnNames.Should().Equal("X");
        result["T"][1].ColumnNames.Should().Equal("Y");
    }

    /// <summary>集約キーと別に保存名を指定でき、null を渡すと制約名なしとして保持されることを検証する（SQLite の自動名対策）</summary>
    [Fact(DisplayName = "保存名に null を渡すと制約名なしとして保持される")]
    public void NullPersistedName_IsKept()
    {
        var builder = new UniqueConstraintImportBuilder();
        builder.Add("T", "sqlite_autoindex_T_1", "X", persistedName: null);

        builder.Build()["T"].Should().ContainSingle().Which.Name.Should().BeNull();
    }

    /// <summary>投入がなければ空辞書を返すことを検証する</summary>
    [Fact(DisplayName = "投入なしなら空辞書")]
    public void NoInput_ReturnsEmpty()
    {
        new UniqueConstraintImportBuilder().Build().Should().BeEmpty();
    }

    /// <summary>Attach が列名をカラム ID へ解決してエンティティへ制約を載せることを検証する</summary>
    [Fact(DisplayName = "Attach: 列名を解決してエンティティへ一意制約を載せる")]
    public void Attach_ResolvesColumnIds()
    {
        var entry = Table("T", "A", "B", "C");
        var tables = new Dictionary<string, SchemaTableEntry> { ["T"] = entry };

        var builder = new UniqueConstraintImportBuilder();
        builder.Add("T", "UQ_T", "C", "UQ_T");
        builder.Add("T", "UQ_T", "A", "UQ_T");
        UniqueConstraintImportBuilder.Attach(tables, builder.Build());

        var constraint = entry.Entity.UniqueConstraints.Should().ContainSingle().Which;
        constraint.Name.Should().Be("UQ_T");
        // 宣言順（C→A）のまま ID へ解決される
        constraint
            .ColumnIds.Should()
            .Equal(entry.ColumnsByName["C"].Id, entry.ColumnsByName["A"].Id);
    }

    /// <summary>解決できない列を含む制約は Attach でスキップされることを検証する</summary>
    [Fact(DisplayName = "Attach: 解決できない列を含む制約はスキップされる")]
    public void Attach_SkipsUnresolvableColumns()
    {
        var entry = Table("T", "A");
        var tables = new Dictionary<string, SchemaTableEntry> { ["T"] = entry };

        var builder = new UniqueConstraintImportBuilder();
        builder.Add("T", "UQ_T", "A", "UQ_T");
        builder.Add("T", "UQ_T", "Missing", "UQ_T");
        UniqueConstraintImportBuilder.Attach(tables, builder.Build());

        entry.Entity.UniqueConstraints.Should().BeEmpty();
    }

    /// <summary>取込対象に無いテーブルの制約は Attach で無視されることを検証する</summary>
    [Fact(DisplayName = "Attach: 未知のテーブルの制約は無視される")]
    public void Attach_IgnoresUnknownTable()
    {
        var entry = Table("T", "A");
        var tables = new Dictionary<string, SchemaTableEntry> { ["T"] = entry };

        var builder = new UniqueConstraintImportBuilder();
        builder.Add("Other", "UQ_Other", "A", "UQ_Other");
        UniqueConstraintImportBuilder.Attach(tables, builder.Build());

        entry.Entity.UniqueConstraints.Should().BeEmpty();
    }
}
