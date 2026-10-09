using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SchemaDiffService"/> のテーブル・列・外部キー・説明の差分計算を検証するテストクラス</summary>
public class SchemaDiffServiceTests
{
    /// <summary>名前と (列名, 型, 主キー) の組からテスト用エンティティを生成する</summary>
    private static Entity Tbl(string name, params (string Name, string Type, bool Pk)[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c.Name,
                    DataType = c.Type,
                    IsPrimaryKey = c.Pk,
                    IsNullable = !c.Pk,
                }
            );
        }

        return e;
    }

    /// <summary>DB に存在しないテーブルが AddTable として検出されることを検証する</summary>
    [Fact(DisplayName = "DB 側に無いテーブルは AddTable になる")]
    public void NewTable_AddTable()
    {
        var live = new List<Entity>();
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "Customer");
    }

    /// <summary>DB に存在しない列が AddColumn として検出されることを検証する</summary>
    [Fact(DisplayName = "DB 側に無い列は AddColumn になる")]
    public void NewColumn_AddColumn()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "Name");
    }

    /// <summary>列の型変更が AlterColumn として検出され、既定では未選択であることを検証する</summary>
    [Fact(DisplayName = "型が変われば AlterColumn になり、既定では未選択")]
    public void TypeChange_AlterColumn_NotSelected()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(100)", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which;
        alter.IsSelected.Should().BeFalse();
    }

    /// <summary>NULL 許容の変更が AlterColumn として検出され、説明に NULL 許容と記載されることを検証する</summary>
    [Fact(DisplayName = "NULL 許容が変われば AlterColumn になる")]
    public void NullabilityChange_AlterColumn_NotSelected()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        live[0].Columns[1].IsNullable = true;
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        target[0].Columns[1].IsNullable = false;

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which;
        alter.ColumnName.Should().Be("Name");
        alter.IsSelected.Should().BeFalse();

        // 製品コードと同じ resx キーから期待値を組み立て、カルチャに依らず完全一致で検証する
        // （型は変わらず NULL 許容のみ変化するため、変更点は NullableChange の 1 件のみ）
        var expectedNullableChange = string.Format(
            ProviderStrings.Diff_NullableChange,
            ProviderStrings.Diff_Nullable_Allow,
            ProviderStrings.Diff_Nullable_Deny
        );
        var expectedDescription =
            string.Format(ProviderStrings.Diff_ColumnChangePrefix, "Customer", "Name")
            + expectedNullableChange;
        alter.Description.Should().Be(expectedDescription);
    }

    /// <summary>
    /// 計算列の NULL 許容差が AlterColumn を生まないことを、5 方言の同期ケーパビリティすべてで検証する。
    /// </summary>
    /// <remarks>
    /// 計算列の NULL 許容は式が決める性質で、図が DB へ強制できない。しかも「NOT NULL の計算列は
    /// マルチターゲットのミラー側へ書けない」問題の逃げ道が<b>図で NULL 許容にする</b>ことなので、
    /// 比較すると逃げ道を採った瞬間に取込元の DB へ偽の <c>ALTER COLUMN</c> が出る。
    /// 判定は方言中立だが、ケーパビリティで再び開かないことも 5 方言分まとめて押さえる。
    /// </remarks>
    [Fact(DisplayName = "計算列の NULL 許容差は AlterColumn にならない（5 方言）")]
    public void ComputedColumn_NullabilityChange_IsNotDiffed()
    {
        foreach (var (dialect, capabilities) in DialectCapabilities)
        {
            var live = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false)),
            };
            live[0].Columns[1].IsNullable = false;
            live[0].Columns[1].IsComputed = true;

            var target = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false)),
            };
            target[0].Columns[1].IsNullable = true;
            target[0].Columns[1].IsComputed = true;

            var diff = new SchemaDiffService().Compute(
                live,
                new List<Relationship>(),
                target,
                new List<Relationship>(),
                capabilities
            );

            diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterColumn, $"{dialect}");
        }
    }

    /// <summary>
    /// 計算列の型差は AlterColumn として検出するが、NULL 許容は運ばず（項目の列は DB の NULL 許容）、式を保って型を
    /// 変えられる方言（PostgreSQL）でだけ選べ、ほかの方言では選択できない案内項目になることを検証する（5 方言）。
    /// </summary>
    /// <remarks>
    /// 項目の列が図の NULL 許容を運ぶと、選んだ計画が説明に出ないまま NOT NULL を外した。式を書けない方言で選べると、
    /// 実行が失敗する（SQL Server・MySQL）か式が消える（SQLite の再構築）。
    /// </remarks>
    [Fact(DisplayName = "計算列の型差: NULL 許容は運ばず、PostgreSQL だけ選べる（5 方言）")]
    public void ComputedColumn_TypeChange_IsSelectableOnlyWhereSupported()
    {
        foreach (var (dialect, capabilities) in DialectCapabilities)
        {
            var live = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Total", "decimal(18,2)", false)),
            };
            live[0].Columns[1].IsNullable = false;
            live[0].Columns[1].IsComputed = true;

            // 型だけでなく NULL 許容も変えておく＝それでも説明に載るのは型の変更だけ
            var target = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false)),
            };
            target[0].Columns[1].IsNullable = true;
            target[0].Columns[1].IsComputed = true;

            var diff = new SchemaDiffService().Compute(
                live,
                new List<Relationship>(),
                target,
                new List<Relationship>(),
                capabilities
            );

            var alter = diff
                .Items.Should()
                .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn, $"{dialect}")
                .Which;
            alter.ColumnName.Should().Be("Total", $"{dialect}");
            alter.Column!.IsNullable.Should().BeFalse($"{dialect}: nullability is not carried");
            alter.IsSelected.Should().BeFalse($"{dialect}");

            var selectable = capabilities.SupportsComputedColumnTypeChange;
            alter.IsSelectable.Should().Be(selectable, $"{dialect}");
            var expectedDescription = selectable
                ? string.Format(ProviderStrings.Diff_ColumnChangePrefix, "Item", "Total")
                    + string.Format(
                        ProviderStrings.Diff_TypeChange,
                        "decimal(18,2)",
                        "decimal(21,2)"
                    )
                : string.Format(
                    ProviderStrings.Diff_ComputedColumnTypeNotSynced,
                    "Item",
                    "Total",
                    "decimal(18,2)",
                    "decimal(21,2)"
                );
            alter.Description.Should().Be(expectedDescription, $"{dialect}");
        }

        DialectCapabilities
            .Where(d => d.Capabilities.SupportsComputedColumnTypeChange)
            .Select(d => d.Dialect)
            .Should()
            .Equal("PostgreSql");
    }

    /// <summary>
    /// DB 側だけが計算列（図では通常の列）でも、NULL 許容の差は出さず、型差は計算列と同じ扱いになることを検証する。
    /// </summary>
    [Fact(DisplayName = "計算列の型差: DB 側だけが計算列でも NULL 許容は出さず型差は案内項目")]
    public void LiveOnlyComputedColumn_IsTreatedAsComputed()
    {
        var live = new List<Entity>
        {
            Tbl("Item", ("Id", "int", true), ("Total", "decimal(18,2)", false)),
        };
        live[0].Columns[1].IsNullable = false;
        live[0].Columns[1].IsComputed = true;
        var target = new List<Entity>
        {
            Tbl("Item", ("Id", "int", true), ("Total", "decimal(18,2)", false)),
        };
        target[0].Columns[1].IsNullable = true;

        var diff = new SchemaDiffService().Compute(
            live,
            [],
            target,
            [],
            new SqlServerProvider().SyncCapabilities
        );

        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterColumn);

        target[0].Columns[1].DataType = "decimal(21,2)";
        diff = new SchemaDiffService().Compute(
            live,
            [],
            target,
            [],
            new SqlServerProvider().SyncCapabilities
        );

        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn)
            .Which.IsSelectable.Should()
            .BeFalse();
    }

    /// <summary>
    /// 図の主キーと同じ列集合の一意制約の追加は、それを別の制約として持てない方言（SQLite・Oracle）では選択できない
    /// 案内項目になり、ほかの方言では従来どおり既定で選ばれることを検証する（5 方言）。主キーと違う列集合は全方言で選べる。
    /// </summary>
    [Fact(DisplayName = "主キーと同じ列集合の一意制約: SQLite・Oracle は案内項目（5 方言）")]
    public void UniqueConstraintOnPrimaryKey_IsGuideOnlyWhereUnsupported()
    {
        foreach (var (dialect, capabilities) in DialectCapabilities)
        {
            var live = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Code", "int", false)),
            };
            var target = new List<Entity>
            {
                Tbl("Item", ("Id", "int", true), ("Code", "int", false)),
            };
            target[0]
                .UniqueConstraints.Add(
                    new UniqueConstraint { ColumnIds = [target[0].Columns[0].Id] }
                );
            target[0]
                .UniqueConstraints.Add(
                    new UniqueConstraint { ColumnIds = [target[0].Columns[1].Id] }
                );

            var diff = new SchemaDiffService().Compute(live, [], target, [], capabilities);

            var adds = diff.Items.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint).ToList();
            adds.Should().HaveCount(2, $"{dialect}");
            var onKey = adds.Single(i => i.UniqueConstraintColumns.SequenceEqual(["Id"]));
            var other = adds.Single(i => i.UniqueConstraintColumns.SequenceEqual(["Code"]));
            var supported = capabilities.SupportsUniqueConstraintOnPrimaryKey;
            onKey.IsSelectable.Should().Be(supported, $"{dialect}");
            onKey.IsSelected.Should().Be(supported, $"{dialect}");
            other.IsSelectable.Should().BeTrue($"{dialect}");
            other.IsSelected.Should().BeTrue($"{dialect}");

            if (!supported)
            {
                onKey
                    .Description.Should()
                    .Be(
                        string.Format(
                            ProviderStrings.Diff_UniqueConstraintOnPrimaryKeyNotSynced,
                            "Item",
                            "Id"
                        ),
                        $"{dialect}"
                    );
            }
        }

        DialectCapabilities
            .Where(d => !d.Capabilities.SupportsUniqueConstraintOnPrimaryKey)
            .Select(d => d.Dialect)
            .Should()
            .Equal("Oracle", "Sqlite");
    }

    /// <summary>
    /// 複合外部キーの列ペアの並びだけが違うとき、並びを問う方言（SQL Server・MySQL）では作り直しの差分になり、
    /// 問わない方言（PostgreSQL・Oracle・SQLite）では差分にならないことを検証する（5 方言）。
    /// Oracle のカタログは外部キーの列を参照先のキーの並びで返すので、並びまで比べると取り込み直すたびに差分が出た。
    /// </summary>
    [Fact(
        DisplayName = "複合外部キーの列ペアの並び: 並びを問わない方言では差分にならない（5 方言）"
    )]
    public void CompositeForeignKeyPairOrder_IsComparedOnlyWhereItMatters()
    {
        (List<Entity>, List<Relationship>) Build(bool reversed)
        {
            var parent = Tbl("P", ("A", "int", true), ("B", "int", true));
            var child = Tbl("C", ("Id", "int", true), ("X", "int", false), ("Y", "int", false));
            List<RelationshipColumnPair> pairs =
            [
                new(parent.Columns[0].Id, child.Columns[1].Id),
                new(parent.Columns[1].Id, child.Columns[2].Id),
            ];

            if (reversed)
            {
                pairs.Reverse();
            }

            return (
                [parent, child],
                [
                    new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = child.Id,
                        ColumnPairs = pairs,
                        ConstraintName = "FK_C_P",
                    },
                ]
            );
        }

        foreach (var (dialect, capabilities) in DialectCapabilities)
        {
            var (live, liveRelationships) = Build(reversed: true);
            var (target, targetRelationships) = Build(reversed: false);

            var items = new SchemaDiffService()
                .Compute(live, liveRelationships, target, targetRelationships, capabilities)
                .Items;

            if (capabilities.ReferencedKeyRequiresSameColumnOrder)
            {
                items
                    .Should()
                    .ContainSingle($"{dialect}")
                    .Which.Kind.Should()
                    .Be(SchemaDiffKind.AlterForeignKey, $"{dialect}");
            }
            else
            {
                items.Should().BeEmpty($"{dialect}");
            }
        }

        DialectCapabilities
            .Where(d => d.Capabilities.ReferencedKeyRequiresSameColumnOrder)
            .Select(d => d.Dialect)
            .Should()
            .Equal("SqlServer", "MySql");
    }

    /// <summary>計算列でない列の NULL 許容差は従来どおり検出されることを検証する（スキップの範囲の下限）</summary>
    [Fact(DisplayName = "計算列でなければ NULL 許容差は従来どおり AlterColumn になる")]
    public void NonComputedColumn_NullabilityChange_IsStillDiffed()
    {
        var live = new List<Entity>
        {
            Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false)),
        };
        live[0].Columns[1].IsNullable = false;

        var target = new List<Entity>
        {
            Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false)),
        };
        target[0].Columns[1].IsNullable = true;

        new SchemaDiffService()
            .Compute(live, new List<Relationship>(), target, new List<Relationship>())
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn);
    }

    /// <summary>5 方言の同期ケーパビリティ（差分計算の入力になる唯一の方言差）</summary>
    private static readonly (
        string Dialect,
        SyncDialectCapabilities Capabilities
    )[] DialectCapabilities =
    [
        ("SqlServer", new SqlServerProvider().SyncCapabilities),
        ("PostgreSql", new PostgreSqlProvider().SyncCapabilities),
        ("MySql", new MySqlProvider().SyncCapabilities),
        ("Oracle", new OracleProvider().SyncCapabilities),
        ("Sqlite", new SqliteProvider().SyncCapabilities),
    ];

    /// <summary>ER 図に存在しない列が DropColumn として検出され、既定では未選択であることを検証する</summary>
    [Fact(DisplayName = "ER 図側に無い列は DropColumn になり、既定では未選択")]
    public void RemovedColumn_DropColumn_NotSelected()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true), ("Old", "int", false)) };
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        var drop = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropColumn)
            .Which;
        drop.ColumnName.Should().Be("Old");
        drop.IsSelected.Should().BeFalse();
    }

    /// <summary>ER 図に存在しないテーブルが DropTable として検出されることを検証する</summary>
    [Fact(DisplayName = "ER 図側に無いテーブルは DropTable になる")]
    public void RemovedTable_DropTable()
    {
        var live = new List<Entity> { Tbl("Old", ("Id", "int", true)) };
        var target = new List<Entity>();
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropTable && i.TableName == "Old");
    }

    /// <summary>ER 図のみに存在するリレーションが AddForeignKey として検出され、FK 列が解決されることを検証する</summary>
    [Fact(DisplayName = "新しい外部キーは AddForeignKey になる")]
    public void NewRelationship_AddForeignKey()
    {
        var customer = Tbl("Customer", ("Id", "int", true));
        var order = Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
        var target = new List<Entity> { customer, order };
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            // 構成列は列ペアで明示する（命名規約による推測フォールバックは廃止された）
            ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
        };

        var liveCustomer = Tbl("Customer", ("Id", "int", true));
        var liveOrder = Tbl("Order", ("Id", "int", true), ("Customer_Id", "int", false));
        var live = new List<Entity> { liveCustomer, liveOrder };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship> { rel }
        );

        var fk = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey)
            .Which;
        fk.ColumnName.Should().Be("Customer_Id");
    }

    /// <summary>同一テーブル間で FK 列が変わると DropForeignKey と AddForeignKey が両方出力されることを検証する</summary>
    [Fact(DisplayName = "同一テーブル間で FK 列が変わると DropForeignKey と AddForeignKey になる")]
    public void ForeignKeyColumnChanged_EmitsDropAndAdd()
    {
        var customerLive = Tbl("Customer", ("Id", "int", true));
        var orderLive = Tbl(
            "Order",
            ("Id", "int", true),
            ("CustomerId1", "int", false),
            ("CustomerId2", "int", false)
        );
        var liveRel = new Relationship
        {
            SourceEntityId = customerLive.Id,
            TargetEntityId = orderLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customerLive.Columns[0].Id, orderLive.Columns[1].Id)],
            ConstraintName = "FK_Order_CustomerId1",
        };

        var customerTarget = Tbl("Customer", ("Id", "int", true));
        var orderTarget = Tbl(
            "Order",
            ("Id", "int", true),
            ("CustomerId1", "int", false),
            ("CustomerId2", "int", false)
        );
        var targetRel = new Relationship
        {
            SourceEntityId = customerTarget.Id,
            TargetEntityId = orderTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(customerTarget.Columns[0].Id, orderTarget.Columns[2].Id)],
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { customerLive, orderLive },
            new List<Relationship> { liveRel },
            new List<Entity> { customerTarget, orderTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.DropForeignKey
                && i.ForeignKeyName == "FK_Order_CustomerId1"
            );
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AddForeignKey && i.ColumnName == "CustomerId2"
            );
    }

    /// <summary>
    /// 参照アクション（ON DELETE/UPDATE）だけが変わった外部キーは、削除と追加の 2 項目でなく
    /// 1 つの外部キー変更（AlterForeignKey）になることを検証する。
    /// </summary>
    /// <remarks>
    /// 削除と追加は同じ名前を持つため、2 項目のままだと既定どおり追加だけを選んで実行したときに
    /// 同じ名前の外部キーを足そうとして失敗する。外部キー変更は既定未選択・破壊的で、外す側（DB の定義）と
    /// 付ける側（図の定義）の両方を運ぶ。
    /// </remarks>
    [Fact(DisplayName = "参照アクションだけが変わると 1 つの AlterForeignKey になる")]
    public void ForeignKeyReferentialActionChanged_EmitsSingleAlter()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
            ConstraintName = "FK_Child_Parent",
            OnDelete = ForeignKeyReferentialAction.NoAction,
            OnUpdate = ForeignKeyReferentialAction.NoAction,
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
            ConstraintName = "FK_Child_Parent",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddForeignKey || i.Kind == SchemaDiffKind.DropForeignKey
            );
        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterForeignKey)
            .Which;
        alter.IsSelected.Should().BeFalse();
        alter.IsDestructive.Should().BeTrue();
        alter.Relationship!.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        alter.Relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        alter.ReplacedForeignKey!.Kind.Should().Be(SchemaDiffKind.DropForeignKey);
        alter.ReplacedForeignKey.ForeignKeyName.Should().Be("FK_Child_Parent");
        alter
            .ReplacedForeignKey.Relationship!.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.NoAction);
    }

    /// <summary>
    /// 名前の無いリレーションと、DDL が既定名 <c>FK_{子}_{親}</c> で作った live の外部キーは同じものとみなし、
    /// 外部キーの差分を出さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "名前の無いリレーションは既定名の live 外部キーと一致し差分を出さない")]
    public void UnnamedRelationship_MatchesDefaultNamedLiveForeignKey()
    {
        var (live, liveRels, target, targetRels) = ForeignKeyPair(
            liveName: "FK_Child_Parent",
            targetName: null
        );

        var diff = new SchemaDiffService().Compute(live, liveRels, target, targetRels);

        diff.Items.Should().NotContain(i => IsForeignKeyDiff(i));
    }

    /// <summary>
    /// 外部キー名を保存しない方言（SQLite）でも、同じ子列に張る外部キーの参照アクションだけが変われば
    /// AlterForeignKey になることを検証する（2 項目のままだと再構築で旧と新が同じ列に並ぶ）。
    /// </summary>
    [Fact(
        DisplayName = "名前を保存しない方言でも同じ子列の外部キーの変更は AlterForeignKey になる"
    )]
    public void ForeignKeyChange_WithoutPersistedNames_EmitsSingleAlter()
    {
        var (live, liveRels, target, targetRels) = ForeignKeyPair(
            liveName: "FK_Child_Parent_0",
            targetName: null
        );
        targetRels[0].OnDelete = ForeignKeyReferentialAction.Cascade;

        var diff = new SchemaDiffService().Compute(
            live,
            liveRels,
            target,
            targetRels,
            new SqliteProvider().SyncCapabilities
        );

        diff.Items.Should()
            .ContainSingle(i => IsForeignKeyDiff(i))
            .Which.Kind.Should()
            .Be(SchemaDiffKind.AlterForeignKey);
    }

    /// <summary>名前の違う外部キー同士は作り直しとみなさず、削除と追加のまま残すことを検証する</summary>
    [Fact(DisplayName = "名前の違う外部キーは AlterForeignKey に畳まない")]
    public void ForeignKeysWithDifferentNames_StayDropAndAdd()
    {
        var (live, liveRels, target, targetRels) = ForeignKeyPair(
            liveName: "fk_old",
            targetName: "fk_new"
        );

        var diff = new SchemaDiffService().Compute(live, liveRels, target, targetRels);

        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.DropForeignKey);
        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddForeignKey);
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterForeignKey);
    }

    /// <summary>親 Parent(Id)・子 Child(Id, ParentId) と、ParentId → Id の外部キーを live と図の両方に作る</summary>
    private static (
        List<Entity> Live,
        List<Relationship> LiveRels,
        List<Entity> Target,
        List<Relationship> TargetRels
    ) ForeignKeyPair(string? liveName, string? targetName)
    {
        static List<Relationship> Build(List<Entity> entities, string? name) =>
            [
                new Relationship
                {
                    SourceEntityId = entities[0].Id,
                    TargetEntityId = entities[1].Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(entities[0].Columns[0].Id, entities[1].Columns[1].Id)],
                    ConstraintName = name,
                },
            ];

        var live = new List<Entity>
        {
            Tbl("Parent", ("Id", "int", true)),
            Tbl("Child", ("Id", "int", true), ("ParentId", "int", false)),
        };
        var target = new List<Entity>
        {
            Tbl("Parent", ("Id", "int", true)),
            Tbl("Child", ("Id", "int", true), ("ParentId", "int", false)),
        };

        return (live, Build(live, liveName), target, Build(target, targetName));
    }

    /// <summary>DB と ER 図が同一なら差分項目が空になることを検証する</summary>
    [Fact(DisplayName = "差分が無ければ Items は空")]
    public void Identical_NoDiff()
    {
        var live = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var target = new List<Entity> { Tbl("A", ("Id", "int", true)) };
        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should().BeEmpty();
    }

    /// <summary>テーブル説明の変更が SetTableDescription として検出され、既定で選択されることを検証する</summary>
    [Fact(DisplayName = "テーブル説明が変わると SetTableDescription になる")]
    public void TableDescriptionChanged_SetTableDescription()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "古い説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "新しい顧客テーブル";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.SetTableDescription)
            .Which;
        item.NewDescription.Should().Be("新しい顧客テーブル");
        item.OldDescription.Should().Be("古い説明");
        item.IsSelected.Should().BeTrue();
    }

    /// <summary>列説明の変更が SetColumnDescription として検出されることを検証する</summary>
    [Fact(DisplayName = "列の説明が変わると SetColumnDescription になる")]
    public void ColumnDescriptionChanged_SetColumnDescription()
    {
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        live[0].Columns[1].Description = "旧";
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.SetColumnDescription)
            .Which;
        item.ColumnName.Should().Be("Name");
        item.NewDescription.Should().Be("顧客名");
        item.OldDescription.Should().Be("旧");
    }

    /// <summary>説明が同一なら差分項目を生成しないことを検証する</summary>
    [Fact(DisplayName = "説明が同じなら差分にならない")]
    public void SameDescription_NoDiff()
    {
        var live = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        live[0].Description = "同じ説明";
        var target = new List<Entity> { Tbl("Customer", ("Id", "int", true)) };
        target[0].Description = "同じ説明";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diff.Items.Should().BeEmpty();
    }

    /// <summary>新規テーブルに説明があれば AddTable と併せて説明設定差分も同時出力されることを検証する</summary>
    [Fact(DisplayName = "新規テーブル + 列の説明があれば SetTable/Column も同時に出力される")]
    public void NewTableWithDescriptions_EmitsAllSetDescriptions()
    {
        var live = new List<Entity>();
        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };

        target[0].Description = "顧客マスタ";
        target[0].Columns[1].Description = "顧客名";

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AddTable);
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.SetTableDescription && i.NewDescription == "顧客マスタ"
            );
        diff.Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.SetColumnDescription
                && i.ColumnName == "Name"
                && i.NewDescription == "顧客名"
            );
    }

    // ---------------- ケーパビリティによる方言調整 ----------------

    /// <summary>SupportsDescriptions=false の方言（SQLite）では説明差分を一切生成しないことを検証する</summary>
    [Fact(DisplayName = "説明非対応方言では説明差分を生成しない")]
    public void CapabilitiesWithoutDescriptions_SuppressesDescriptionDiffs()
    {
        // 既存テーブルの説明変更・列説明変更・新規テーブルの説明の 3 経路をまとめて確認する
        var live = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
        };
        live[0].Description = "旧テーブル説明";
        live[0].Columns[1].Description = "旧列説明";

        var target = new List<Entity>
        {
            Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)),
            Tbl("Order", ("Id", "int", true)),
        };
        target[0].Description = "新テーブル説明";
        target[0].Columns[1].Description = "新列説明";
        target[1].Description = "新規テーブルの説明";

        var capabilities = new SyncDialectCapabilities { SupportsDescriptions = false };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            capabilities
        );

        // 説明差分はゼロ・構造差分（新規テーブル Order）は生成される
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.SetTableDescription);
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.SetColumnDescription);
        diff.Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == "Order");
    }

    /// <summary>
    /// PersistsForeignKeyConstraintNames=false の方言（SQLite）では、制約名だけが異なる FK（合成名 live vs 無名 target）を
    /// 同一とみなし、Drop+Add の誤検出を出さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "FK 制約名を永続化しない方言では名前差だけの FK 差分は出ない")]
    public void CapabilitiesWithoutFkConstraintNames_SuppressesNameOnlyForeignKeyDiff()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        // live 側は取込時の合成制約名を持つ
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
            ConstraintName = "FK_Child_Parent_0",
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        // target 側は無名（手動作成のリレーション）
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
            ConstraintName = null,
        };

        var capabilities = new SyncDialectCapabilities
        {
            PersistsForeignKeyConstraintNames = false,
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel },
            capabilities
        );

        // 制約名以外は同一のため、名前差だけでは FK 差分を出さない
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AddForeignKey);
        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.DropForeignKey);
    }

    /// <summary>
    /// 既定（capabilities なし）では制約名を含めて比較するため、名前差だけの FK でも Drop+Add が出る
    /// （上のケーパビリティ抑止との対照）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "既定では FK 制約名の差で Drop+Add が出る")]
    public void DefaultCapabilities_EmitsFkDiffOnConstraintNameChange()
    {
        var parentLive = Tbl("Parent", ("Id", "int", true));
        var childLive = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var liveRel = new Relationship
        {
            SourceEntityId = parentLive.Id,
            TargetEntityId = childLive.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentLive.Columns[0].Id, childLive.Columns[1].Id)],
            ConstraintName = "FK_Old",
        };

        var parentTarget = Tbl("Parent", ("Id", "int", true));
        var childTarget = Tbl("Child", ("Id", "int", true), ("ParentId", "int", false));
        var targetRel = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parentTarget.Columns[0].Id, childTarget.Columns[1].Id)],
            ConstraintName = "FK_New",
        };

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { parentLive, childLive },
            new List<Relationship> { liveRel },
            new List<Entity> { parentTarget, childTarget },
            new List<Relationship> { targetRel }
        );

        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropForeignKey);
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddForeignKey);
    }

    // ---------------- 列順変更（ReorderColumns）の生成 ----------------

    /// <summary>列順のみ入れ替えた図（Native/Rebuild 方言）</summary>
    private static (List<Entity> Live, List<Entity> Target) ReorderScenario()
    {
        var live = new List<Entity>
        {
            Tbl(
                "Customer",
                ("Id", "int", true),
                ("Name", "nvarchar(50)", false),
                ("Email", "nvarchar(50)", false)
            ),
        };
        var target = new List<Entity>
        {
            Tbl(
                "Customer",
                ("Id", "int", true),
                ("Email", "nvarchar(50)", false),
                ("Name", "nvarchar(50)", false)
            ),
        };
        return (live, target);
    }

    /// <summary>Native 方言では列順変更が選択可能な ReorderColumns 項目として生成され、既定で未選択であることを検証する</summary>
    [Fact(DisplayName = "Native 方言では ReorderColumns が生成され既定で未選択")]
    public void Reorder_NativeDialect_GeneratesUnselectedReorderItem()
    {
        var (live, target) = ReorderScenario();

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native }
        );

        var reorder = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.ReorderColumns)
            .Which;
        reorder.TableName.Should().Be("Customer");
        reorder.Entity.Should().BeSameAs(target[0]);
        reorder.IsSelected.Should().BeFalse();
        reorder.IsSelectable.Should().BeTrue();
        reorder.IsDestructive.Should().BeFalse();
    }

    /// <summary>Rebuild 方言でも ReorderColumns 項目が生成されることを検証する</summary>
    [Fact(DisplayName = "Rebuild 方言でも ReorderColumns が生成される")]
    public void Reorder_RebuildDialect_GeneratesReorderItem()
    {
        var (live, target) = ReorderScenario();

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities
            {
                SupportsAlterColumn = false,
                SupportsForeignKeyAlter = false,
                SupportsDescriptions = false,
                PersistsForeignKeyConstraintNames = false,
                ColumnReorder = ColumnReorderMode.Rebuild,
            }
        );

        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.ReorderColumns);
    }

    /// <summary>非対応方言（None）および capabilities 省略時は ReorderColumns を生成しないことを検証する</summary>
    [Fact(DisplayName = "None 方言・capabilities 省略時は ReorderColumns を生成しない")]
    public void Reorder_NoneDialectOrNoCapabilities_DoesNotGenerate()
    {
        var (live, target) = ReorderScenario();
        var service = new SchemaDiffService();

        // capabilities 省略（既定）
        var diffDefault = service.Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );
        diffDefault.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);

        // 明示的な None
        var diffNone = service.Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>(),
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.None }
        );
        diffNone.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.ReorderColumns);
    }

    // ---------------- 主キー変更（AlterPrimaryKey）の検出 ----------------

    /// <summary>
    /// 主キー指定だけを差し替えたテーブルを組み立てる（型・NULL 許容は固定＝主キー以外の差分を起こさない）。
    /// </summary>
    /// <param name="columns">列名（この順序がそのまま列定義順＝主キーの順序判定にも使われる）</param>
    /// <param name="pkColumns">主キーにする列名</param>
    private static Entity PkTbl(string name, string[] columns, params string[] pkColumns)
    {
        var e = new Entity { TableName = name };

        foreach (var c in columns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c,
                    DataType = "int",
                    IsNullable = false,
                    IsPrimaryKey = pkColumns.Contains(c, StringComparer.OrdinalIgnoreCase),
                }
            );
        }

        return e;
    }

    /// <summary>主キーが無かったテーブルへ主キーを付ける変更が AlterPrimaryKey になることを検証する</summary>
    [Fact(DisplayName = "主キーの追加は AlterPrimaryKey になり、既定では未選択かつ破壊的")]
    public void PrimaryKeyAdded_AlterPrimaryKey_NotSelected()
    {
        var live = new List<Entity> { PkTbl("Customer", ["Id", "Name"]) };
        var target = new List<Entity> { PkTbl("Customer", ["Id", "Name"], "Id") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        // 主キー以外は同一のため、差分は AlterPrimaryKey の 1 件だけになる
        var item = diff.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        item.TableName.Should().Be("Customer");
        item.Entity.Should().BeSameAs(target[0]);
        item.IsSelected.Should().BeFalse();
        item.IsSelectable.Should().BeTrue();
        item.IsDestructive.Should().BeTrue();

        // 製品コードと同じ resx キーから期待値を組み立て、カルチャに依らず完全一致で検証する
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "Customer",
                    ProviderStrings.Diff_PrimaryKey_None,
                    "Id"
                )
            );
    }

    /// <summary>主キーの解除が AlterPrimaryKey になり、変更後表記が「なし」になることを検証する</summary>
    [Fact(DisplayName = "主キーの解除は AlterPrimaryKey になる")]
    public void PrimaryKeyRemoved_AlterPrimaryKey()
    {
        var live = new List<Entity> { PkTbl("Customer", ["Id", "Name"], "Id") };
        var target = new List<Entity> { PkTbl("Customer", ["Id", "Name"]) };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "Customer",
                    "Id",
                    ProviderStrings.Diff_PrimaryKey_None
                )
            );
    }

    /// <summary>単一主キーから複合主キーへの構成変更が 1 件の AlterPrimaryKey になることを検証する</summary>
    [Fact(DisplayName = "単一主キー → 複合主キーの構成変更は 1 件の AlterPrimaryKey になる")]
    public void PrimaryKeyCompositionChanged_AlterPrimaryKey()
    {
        var live = new List<Entity> { PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId") };
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "OrderLine",
                    "OrderId",
                    "OrderId, LineNo"
                )
            );
    }

    /// <summary>複合主キーの順序変更（構成列は同じ）も AlterPrimaryKey として検出されることを検証する</summary>
    [Fact(DisplayName = "複合主キーの順序変更も AlterPrimaryKey になる")]
    public void PrimaryKeyOrderChanged_AlterPrimaryKey()
    {
        // 列定義順が主キーの順序になる（live: OrderId, LineNo → target: LineNo, OrderId）
        var live = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["LineNo", "OrderId"], "OrderId", "LineNo"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterPrimaryKey,
                    "OrderLine",
                    "OrderId, LineNo",
                    "LineNo, OrderId"
                )
            );
    }

    /// <summary>主キー構成が同一なら（列名の大文字小文字差を含めて）AlterPrimaryKey を生成しないことを検証する</summary>
    [Fact(DisplayName = "主キー構成が同じなら AlterPrimaryKey は出ない")]
    public void SamePrimaryKey_NoAlterPrimaryKey()
    {
        var live = new List<Entity>
        {
            PkTbl("OrderLine", ["OrderId", "LineNo"], "OrderId", "LineNo"),
        };
        // 列名の大文字小文字だけが異なる（列差分と同じ規則で同一とみなす）
        var target = new List<Entity>
        {
            PkTbl("OrderLine", ["orderid", "lineno"], "orderid", "lineno"),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterPrimaryKey);
    }

    // ---------------- 一意制約（UNIQUE）の差分 ----------------

    /// <summary>エンティティへ一意制約を足す（構成列は列名で引き当てる）</summary>
    private static Entity WithUnique(Entity entity, string? name, params string[] columnNames)
    {
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = name,
                ColumnIds = columnNames
                    .Select(n => entity.Columns.Single(c => c.Name == n).Id)
                    .ToList(),
            }
        );
        return entity;
    }

    /// <summary>Code / Kind 列を持つ Customer テーブルを生成する（一意制約テストの共通土台）</summary>
    private static Entity CustomerTable() =>
        Tbl(
            "Customer",
            ("Id", "int", true),
            ("Code", "nvarchar(20)", false),
            ("Kind", "nvarchar(10)", false)
        );

    /// <summary>図にだけ在る一意制約が AddUniqueConstraint として検出され、既定で選択されることを検証する</summary>
    [Fact(DisplayName = "図にだけ在る一意制約は AddUniqueConstraint になり既定で選択される")]
    public void UniqueConstraintOnlyInTarget_AddUniqueConstraint_SelectedByDefault()
    {
        var live = new List<Entity> { CustomerTable() };
        var target = new List<Entity> { WithUnique(CustomerTable(), name: null, "Code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddUniqueConstraint)
            .Which;
        item.TableName.Should().Be("Customer");
        item.UniqueConstraintColumns.Should().Equal("Code");
        item.IsSelected.Should().BeTrue();
        item.IsDestructive.Should().BeFalse();
        item.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_AddUniqueConstraint, "Customer", "Code"));
    }

    /// <summary>DB にだけ在る一意制約が DropUniqueConstraint（既定未選択・破壊的）になることを検証する</summary>
    [Fact(DisplayName = "DB にだけ在る一意制約は DropUniqueConstraint になり既定で未選択")]
    public void UniqueConstraintOnlyInLive_DropUniqueConstraint_NotSelected()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_Legacy", "Code", "Kind") };
        var target = new List<Entity> { CustomerTable() };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Which;
        // DROP には DB 側の実名が要る（レンダラーはこの名前をそのまま使う）
        item.UniqueConstraintName.Should().Be("UQ_Legacy");
        item.UniqueConstraintColumns.Should().Equal("Code", "Kind");
        item.IsSelected.Should().BeFalse();
        item.IsDestructive.Should().BeTrue();
        item.Description.Should()
            .Be(string.Format(ProviderStrings.Diff_DropUniqueConstraint, "Customer", "Code, Kind"));
    }

    /// <summary>一意制約だけの差分を計算する（関係なし）</summary>
    private static List<SchemaDiffItem> UniqueDiff(
        Entity live,
        Entity target,
        SyncDialectCapabilities? capabilities = null
    ) =>
        new SchemaDiffService()
            .Compute(
                [live],
                new List<Relationship>(),
                [target],
                new List<Relationship>(),
                capabilities
            )
            .Items;

    /// <summary>
    /// 同じ名前の一意制約の列を変えると、追加と削除が 1 つの AlterUniqueConstraint（既定で未選択・破壊的）に
    /// まとまることを検証する（名前の照合は前後の空白を除き大文字小文字を区別しない）。2 項目のままだと、
    /// 既定どおり追加だけを選んで実行したときに同じ名前の制約を足そうとして失敗した。
    /// </summary>
    [Fact(
        DisplayName = "一意制約の変更: 同じ名前で列を変えると AlterUniqueConstraint 1 項目にまとまり既定で未選択"
    )]
    public void UniqueConstraintColumnsChanged_SameName_AlterUniqueConstraint()
    {
        var items = UniqueDiff(
            WithUnique(CustomerTable(), "UQ_Code", "Code"),
            WithUnique(CustomerTable(), " uq_code ", "Code", "Kind")
        );

        var item = items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterUniqueConstraint);
        item.IsSelected.Should().BeFalse();
        item.IsDestructive.Should().BeTrue();
        item.UniqueConstraintName.Should().Be(" uq_code ");
        item.UniqueConstraintColumns.Should().Equal("Code", "Kind");
        item.ReplacedUniqueConstraint!.UniqueConstraintName.Should().Be("UQ_Code");
        item.ReplacedUniqueConstraint.UniqueConstraintColumns.Should().Equal("Code");
        item.Description.Should()
            .Be(
                string.Format(
                    ProviderStrings.Diff_AlterUniqueConstraint,
                    "Customer",
                    " uq_code ",
                    "Code",
                    "Code, Kind"
                )
            );
    }

    /// <summary>図の名前が未設定でも、合成名（DDL が付ける名前）が DB の名前と同じなら組むことを検証する</summary>
    [Fact(DisplayName = "一意制約の変更: 図の名前が未設定でも合成名が DB の名前と同じなら組む")]
    public void UniqueConstraintColumnsChanged_SynthesizedNameMatches_AlterUniqueConstraint()
    {
        var items = UniqueDiff(
            WithUnique(CustomerTable(), "UQ_Customer_Code", "Code", "Kind"),
            WithUnique(CustomerTable(), name: null, "Code")
        );

        items.Should().ContainSingle().Which.Kind.Should().Be(SchemaDiffKind.AlterUniqueConstraint);
    }

    /// <summary>
    /// 名前が違う・DB 側に名前が無い（SQLite の自動名）・図の主キーと同じ列集合を持てない方言の案内項目、の
    /// いずれも組まず、追加と削除のままにすることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "一意制約の変更: 名前違い・DB 側の名前なし・持てない方言の案内項目は組まない"
    )]
    public void UniqueConstraintColumnsChanged_NotPaired()
    {
        static IEnumerable<SchemaDiffKind> Kinds(List<SchemaDiffItem> items) =>
            items.Select(i => i.Kind);

        Kinds(
                UniqueDiff(
                    WithUnique(CustomerTable(), "UQ_A", "Code"),
                    WithUnique(CustomerTable(), "UQ_B", "Kind")
                )
            )
            .Should()
            .Equal(SchemaDiffKind.AddUniqueConstraint, SchemaDiffKind.DropUniqueConstraint);
        Kinds(
                UniqueDiff(
                    WithUnique(CustomerTable(), name: null, "Code"),
                    WithUnique(CustomerTable(), name: null, "Kind")
                )
            )
            .Should()
            .Equal(SchemaDiffKind.AddUniqueConstraint, SchemaDiffKind.DropUniqueConstraint);

        var onPrimaryKey = UniqueDiff(
            WithUnique(CustomerTable(), "UQ_X", "Code"),
            WithUnique(CustomerTable(), "UQ_X", "Id"),
            new OracleProvider().SyncCapabilities
        );
        Kinds(onPrimaryKey)
            .Should()
            .Equal(SchemaDiffKind.AddUniqueConstraint, SchemaDiffKind.DropUniqueConstraint);
        onPrimaryKey[0].IsSelectable.Should().BeFalse();
    }

    /// <summary>制約名だけが違う（構成列は同じ）一意制約では差分が出ないことを検証する</summary>
    /// <remarks>
    /// 図側の制約名は未設定（null＝合成名）が普通で、SQLite に至っては実名を持たない。
    /// 名前を比較に含めると恒常的な Drop＋Add の誤検出になるため、照合は列集合だけで行う。
    /// </remarks>
    [Fact(DisplayName = "制約名の差だけでは一意制約の差分にならない")]
    public void UniqueConstraintNameDiffersOnly_NoDiff()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_From_Db", "Code") };
        var target = new List<Entity> { WithUnique(CustomerTable(), name: null, "Code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                || i.Kind == SchemaDiffKind.DropUniqueConstraint
            );
    }

    /// <summary>構成列の並び順・大文字小文字の差だけでは一意制約の差分にならないことを検証する</summary>
    [Fact(DisplayName = "構成列の順序・大文字小文字の差だけでは一意制約の差分にならない")]
    public void UniqueConstraintColumnOrderOrCaseDiffersOnly_NoDiff()
    {
        var live = new List<Entity> { WithUnique(CustomerTable(), "UQ_A", "Code", "Kind") };
        var targetTable = Tbl(
            "Customer",
            ("Id", "int", true),
            ("code", "nvarchar(20)", false),
            ("kind", "nvarchar(10)", false)
        );
        var target = new List<Entity> { WithUnique(targetTable, name: null, "kind", "code") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                || i.Kind == SchemaDiffKind.DropUniqueConstraint
            );
    }

    /// <summary>構成列が空・解決不能な一意制約は差分対象から外れることを検証する</summary>
    [Fact(DisplayName = "構成列が解決できない一意制約は差分にならない")]
    public void UniqueConstraintWithUnresolvableColumns_NoDiff()
    {
        var targetTable = CustomerTable();
        // 空の制約と、このエンティティに存在しない列を指す制約（いずれも DDL 生成側も無視する）
        targetTable.UniqueConstraints.Add(new UniqueConstraint());
        targetTable.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [Guid.NewGuid()] });

        var diff = new SchemaDiffService().Compute(
            new List<Entity> { CustomerTable() },
            new List<Relationship>(),
            new List<Entity> { targetTable },
            new List<Relationship>()
        );

        diff.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AddUniqueConstraint);
    }

    // ---------------- 主キーの順序（PrimaryKeyColumnIds）の差分 ----------------

    /// <summary>
    /// <see cref="PkTbl"/> と同じテーブルに、主キーの順序上書き（<see cref="Entity.PrimaryKeyColumnIds"/>）を与えて組み立てる。
    /// </summary>
    /// <param name="columns">列名（この順序がそのまま列定義順）</param>
    /// <param name="pkOrder">主キーにする列名（この順序が実効順になる）</param>
    private static Entity PkTblOrdered(string name, string[] columns, params string[] pkOrder)
    {
        var e = PkTbl(name, columns, pkOrder);

        e.PrimaryKeyColumnIds = pkOrder
            .Select(pk =>
                e.Columns.First(c =>
                    string.Equals(c.Name, pk, StringComparison.OrdinalIgnoreCase)
                ).Id
            )
            .ToList();

        return e;
    }

    /// <summary>列定義順が同一でも主キーの順序だけが違えば AlterPrimaryKey になることを検証する</summary>
    [Fact(DisplayName = "主キーの順序だけが違えば AlterPrimaryKey になる")]
    public void PrimaryKeyOrderOnlyDifference_AlterPrimaryKey()
    {
        // 列定義順はどちらも (a, b)。主キーの実効順だけが (b, a) と (a, b) で食い違う
        var live = new List<Entity> { PkTblOrdered("Pair", ["a", "b"], "b", "a") };
        var target = new List<Entity> { PkTblOrdered("Pair", ["a", "b"], "a", "b") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        var item = diff.Items.Should().ContainSingle().Which;
        item.Kind.Should().Be(SchemaDiffKind.AlterPrimaryKey);
        item.TableName.Should().Be("Pair");
    }

    /// <summary>双方が同じ主キー順序を持つなら差分が出ないことを検証する</summary>
    [Fact(DisplayName = "主キーの順序が双方同一なら差分は出ない")]
    public void SamePrimaryKeyOrder_NoDiff()
    {
        var live = new List<Entity> { PkTblOrdered("Pair", ["a", "b"], "b", "a") };
        var target = new List<Entity> { PkTblOrdered("Pair", ["a", "b"], "b", "a") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().BeEmpty();
    }

    /// <summary>順序リストが空の側は列宣言順へフォールバックし、同順なら差分が出ないことを検証する</summary>
    [Fact(DisplayName = "順序リスト空 vs 同順の明示指定では差分が出ない")]
    public void EmptyListVersusMatchingExplicitOrder_NoDiff()
    {
        // live は順序リストなし（＝列宣言順 (a, b)）、target は同じ並びを明示指定
        var live = new List<Entity> { PkTbl("Pair", ["a", "b"], "a", "b") };
        var target = new List<Entity> { PkTblOrdered("Pair", ["a", "b"], "a", "b") };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().BeEmpty();
    }

    /// <summary>外部キーの差分（追加・削除・作り直し）か</summary>
    private static bool IsForeignKeyDiff(SchemaDiffItem item) =>
        item.Kind
            is SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AlterForeignKey;

    /// <summary>大文字小文字だけ違う列が DB 側に共存しても差分が例外にならず、序数一致で組むことを検証する</summary>
    /// <remarks>
    /// PostgreSQL の引用識別子は <c>"Dup"</c> と <c>dup</c> を共存させ、取込は両方を持ち帰る。大文字小文字を無視した
    /// 辞書で索引を作ると、同期画面がその DB を一切扱えなくなっていた（キー重複の例外）
    /// </remarks>
    [Fact(
        DisplayName = "大文字小文字だけ違う列が共存しても例外にならず、組めなかった側だけが差分になる"
    )]
    public void CaseOnlyDifferentColumns_DoNotThrow_AndExactMatchWins()
    {
        var live = new List<Entity>
        {
            Tbl("t", ("id", "int", true), ("Dup", "int", false), ("dup", "int", false)),
        };
        var target = new List<Entity> { Tbl("t", ("id", "int", true), ("Dup", "int", false)) };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should()
            .ContainSingle()
            .Which.Should()
            .Match<SchemaDiffItem>(i =>
                i.Kind == SchemaDiffKind.DropColumn && i.ColumnName == "dup"
            );
    }

    /// <summary>大文字小文字だけ違うテーブルが DB 側に共存しても差分が例外にならないことを検証する</summary>
    [Fact(
        DisplayName = "大文字小文字だけ違うテーブルが共存しても例外にならず、曖昧な名前は組まない"
    )]
    public void CaseOnlyDifferentTables_DoNotThrow_AndAmbiguousIsNotMatched()
    {
        var live = new List<Entity>
        {
            Tbl("Dup", ("id", "int", true)),
            Tbl("dup", ("id", "int", true)),
        };
        var target = new List<Entity> { Tbl("DUP", ("id", "int", true)) };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Select(i => (i.Kind, i.TableName))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    (SchemaDiffKind.AddTable, "DUP"),
                    (SchemaDiffKind.DropTable, "Dup"),
                    (SchemaDiffKind.DropTable, "dup"),
                }
            );
    }

    /// <summary>大文字小文字だけ違う名前でも、双方で一意なら同じテーブル・列として比べることを検証する</summary>
    [Fact(DisplayName = "大文字小文字だけ違う名前は双方で一意なら同じテーブル・列として比べる")]
    public void CaseOnlyDifference_UniqueOnBothSides_IsMatched()
    {
        var live = new List<Entity> { Tbl("orders", ("id", "int", true), ("name", "int", false)) };
        var target = new List<Entity>
        {
            Tbl("Orders", ("Id", "int", true), ("Name", "int", false)),
        };

        var diff = new SchemaDiffService().Compute(
            live,
            new List<Relationship>(),
            target,
            new List<Relationship>()
        );

        diff.Items.Should().BeEmpty();
    }

    /// <summary>
    /// 大文字小文字だけ違う綴りで組んだ表・列の項目は DB 側の綴りを書く（スクリプトが実在する名前を指す）。
    /// 図にしか無い表・列は図の綴りのまま作り、図そのものは書き換えない。外部キーの両端も揃えた表を指す。
    /// </summary>
    [Fact(
        DisplayName = "大文字小文字だけ違う綴りで組んだ表・列は DB の綴りで、図にしか無いものは図の綴りで項目を作る"
    )]
    public void CaseOnlyDifference_ItemsUseLiveSpellingForMatchedNamesOnly()
    {
        var liveOrders = Tbl("ORDERS", ("ID", "NUMBER(10)", true), ("NAME", "VARCHAR2(50)", false));
        var liveCustomers = Tbl("CUSTOMERS", ("ID", "NUMBER(10)", true));
        var targetOrders = Tbl(
            "Orders",
            ("Id", "NUMBER(10)", true),
            ("Name", "VARCHAR2(100)", false),
            ("CustomerId", "NUMBER(10)", false),
            ("NewCol", "NUMBER(10)", false)
        );
        var targetCustomers = Tbl("Customers", ("Id", "NUMBER(10)", true));
        var targetItems = Tbl("Items", ("Id", "NUMBER(10)", true));
        targetOrders.Description = "orders";
        var relationship = new Relationship
        {
            SourceEntityId = targetCustomers.Id,
            TargetEntityId = targetOrders.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(targetCustomers.Columns[0].Id, targetOrders.Columns[2].Id)],
            ConstraintName = "FK_Orders_Customers",
        };

        var diff = new SchemaDiffService().Compute(
            [liveOrders, liveCustomers],
            [],
            [targetOrders, targetCustomers, targetItems],
            [relationship]
        );

        var alter = diff.Items.Single(i => i.Kind == SchemaDiffKind.AlterColumn);
        alter.TableName.Should().Be("ORDERS");
        alter.ColumnName.Should().Be("NAME");
        alter.Column!.Name.Should().Be("NAME");
        alter.Column.DataType.Should().Be("VARCHAR2(100)", "型は図の定義のまま");
        diff.Items.Single(i => i.Kind == SchemaDiffKind.SetTableDescription)
            .TableName.Should()
            .Be("ORDERS");

        // 図にしか無い列・表は図の綴りで作る（揃えるのは組になったものだけ）
        diff.Items.Where(i => i.Kind == SchemaDiffKind.AddColumn)
            .Select(i => (i.TableName, i.ColumnName))
            .Should()
            .BeEquivalentTo([("ORDERS", "CustomerId"), ("ORDERS", "NewCol")]);
        diff.Items.Single(i => i.Kind == SchemaDiffKind.AddTable).TableName.Should().Be("Items");

        // 外部キーの両端は揃えた表を指す
        var addFk = diff.Items.Single(i => i.Kind == SchemaDiffKind.AddForeignKey);
        SchemaDiffService.NormalizeTable(addFk.ChildEntity!).Should().Be("ORDERS");
        SchemaDiffService.NormalizeTable(addFk.ParentEntity!).Should().Be("CUSTOMERS");

        // 図そのものは書き換えない
        targetOrders.TableName.Should().Be("Orders");
        targetOrders.Columns[1].Name.Should().Be("Name");
    }

    /// <summary>綴りが揃っている（揃える組が無い）表は、元の図のオブジェクトのまま項目に載る</summary>
    [Fact(DisplayName = "揃える組が無い表は元の図のオブジェクトのまま項目に載る")]
    public void SameSpelling_ItemsReferenceOriginalTargetObjects()
    {
        var live = Tbl("orders", ("id", "int", true), ("name", "varchar(50)", false));
        var target = Tbl("orders", ("id", "int", true), ("name", "varchar(100)", false));

        var diff = new SchemaDiffService().Compute([live], [], [target], []);

        var alter = diff.Items.Single(i => i.Kind == SchemaDiffKind.AlterColumn);
        alter.Entity.Should().BeSameAs(target);
        alter.Column.Should().BeSameAs(target.Columns[1]);
    }

    // ---------------- 28-6: 署名・NULL 許容・切り詰め・SQLite の作り直しの組み合わせ ----------------

    /// <summary>親表・子表と、1 本の外部キーを持つ図を作る（表名・制約名に区切り文字を含められる）</summary>
    private static (List<Entity> Entities, List<Relationship> Relationships) ForeignKeyDiagram(
        string parentName,
        string childName,
        string constraintName
    )
    {
        var parent = Tbl(parentName, ("Id", "int", true));
        var child = Tbl(childName, ("Id", "int", true), ("Ref", "int", false));

        return (
            [parent, child],
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    ConstraintName = constraintName,
                },
            ]
        );
    }

    /// <summary>
    /// 表名に区切り文字を含むとき、親子が違う外部キーが同じ署名にならず、差分に出ることを検証する
    /// （署名を素の区切り文字でつなぐと、親 "P|Q"・子 "C" と、親 "P"・子 "Q|C" が同じ署名になり、差分が消えた）。
    /// </summary>
    [Fact(
        DisplayName = "外部キーの署名: 名前に区切り文字があっても、親子の違う外部キーを取り違えない"
    )]
    public void ForeignKeySignature_NamesWithSeparators_AreNotConfused()
    {
        var (liveA, liveARels) = ForeignKeyDiagram("P|Q", "C", "FK_X");
        var (liveB, _) = ForeignKeyDiagram("P", "Q|C", "FK_X");
        var (targetA, _) = ForeignKeyDiagram("P|Q", "C", "FK_X");
        var (targetB, targetBRels) = ForeignKeyDiagram("P", "Q|C", "FK_X");

        var items = new SchemaDiffService()
            .Compute([.. liveA, .. liveB], liveARels, [.. targetA, .. targetB], targetBRels)
            .Items;

        items
            .Select(i => (i.Kind, i.ChildEntity?.TableName))
            .Should()
            .BeEquivalentTo([
                (SchemaDiffKind.AddForeignKey, "Q|C"),
                (SchemaDiffKind.DropForeignKey, "C"),
            ]);
    }

    /// <summary>
    /// 主キーの列の NULL 許容は実効の値（NOT NULL）で比べ、図がその値を直していなくても NOT NULL → NULL の
    /// 列の変更を出さないことを検証する（生の値で比べると、実行できない偽の変更が出た）。
    /// </summary>
    [Fact(DisplayName = "NULL 許容: 主キーの列は実効の値（NOT NULL）で比べる")]
    public void PrimaryKeyColumn_NullabilityComparedAsNotNull()
    {
        var live = Tbl("Item", ("Id", "int", true));
        var target = Tbl("Item", ("Id", "int", true));
        target.Columns[0].IsNullable = true;

        new SchemaDiffService()
            .Compute([live], new List<Relationship>(), [target], new List<Relationship>())
            .Items.Should()
            .BeEmpty();
    }

    /// <summary>説明の切り詰めが、切る位置のサロゲートペアを割らないことを検証する</summary>
    [Fact(DisplayName = "説明の切り詰め: サロゲートペアを割らない")]
    public void DescriptionTruncation_DoesNotSplitSurrogatePair()
    {
        var live = Tbl("Item", ("Id", "int", true));
        var target = Tbl("Item", ("Id", "int", true));
        // 30 文字目と 31 文字目がサロゲートペア（𠮷）になる説明
        target.Description = new string('a', 29) + "𠮷" + "b";

        var description = new SchemaDiffService()
            .Compute([live], new List<Relationship>(), [target], new List<Relationship>())
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.SetTableDescription)
            .Which.Description;

        for (var i = 0; i < description.Length; i++)
        {
            if (char.IsHighSurrogate(description[i]))
            {
                (i + 1 < description.Length && char.IsLowSurrogate(description[i + 1]))
                    .Should()
                    .BeTrue(description);
            }
        }

        description.Should().Contain("…");
    }

    /// <summary>
    /// 外部キー名を保存しない方言（SQLite）で、参照先だけを変えた外部キーが、削除と追加でなく 1 つの作り直しに
    /// まとまることを検証する（組み合わせのキーに親表が入っていて、まとまらなかった＝既定で追加だけが選ばれると、
    /// 再構築後の表に旧と新の外部キーが同じ列へ 2 本並んだ）。
    /// </summary>
    [Fact(
        DisplayName = "外部キー変更: SQLite で参照先だけを変えた外部キーは 1 つの作り直しにまとまる"
    )]
    public void RetargetedForeignKey_Sqlite_IsPairedIntoAlter()
    {
        var a = Tbl("A", ("Id", "int", true));
        var b = Tbl("B", ("Id", "int", true));
        var child = Tbl("C", ("Id", "int", true), ("Ref", "int", false));
        var live = new List<Entity> { a, b, child };
        var targetEntities = live.Select(e => e.Clone(preserveId: true)).ToList();

        Relationship To(Entity parent) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
            };

        new SchemaDiffService()
            .Compute(live, [To(a)], targetEntities, [To(b)], new SqliteProvider().SyncCapabilities)
            .Items.Should()
            .ContainSingle()
            .Which.Kind.Should()
            .Be(SchemaDiffKind.AlterForeignKey);
    }
}
