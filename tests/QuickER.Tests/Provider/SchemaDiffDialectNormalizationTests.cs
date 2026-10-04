using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SchemaDiffService"/> が方言のケーパビリティ（型表記の正規化・表せない参照アクションの畳み込み）を
/// 通して比べることを検証するテストクラス
/// </summary>
/// <remarks>
/// どちらも「同期しても消えない差分」を防ぐためのもの。DB が宣言の綴り・参照アクションをそのまま保存しない
/// 方言で、図と取込を素のまま比べると、適用しても取込の結果が変わらないので差分が出続ける。
/// </remarks>
public class SchemaDiffDialectNormalizationTests
{
    /// <summary>1 列だけを持つテスト用エンティティを生成する</summary>
    private static Entity Table(string name, string columnType) =>
        new()
        {
            TableName = name,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column
                {
                    Name = "value",
                    DataType = columnType,
                    IsNullable = true,
                },
            },
        };

    /// <summary>
    /// 綴りだけ違う同じ型（取込が返す <c>numeric(18)</c> と図の <c>numeric(18,0)</c>）は、方言の正規化を通して
    /// 比べるので AlterColumn にならないことを検証する。capabilities を渡さない経路は従来どおり素の比較（恒等）。
    /// </summary>
    [Theory(DisplayName = "型の比較は方言の取込表記へ寄せてから行う")]
    [InlineData("numeric(18)", "numeric(18,0)")]
    [InlineData("datetime2", "datetime2(7)")]
    [InlineData("decimal(18)", "decimal")]
    [InlineData("timestamp", "rowversion")]
    public void ColumnType_NormalizedPerDialect(string live, string target)
    {
        var withDialect = new SchemaDiffService().Compute(
            [Table("t", live)],
            [],
            [Table("t", target)],
            [],
            new SqlServerProvider().SyncCapabilities
        );
        var withoutDialect = new SchemaDiffService().Compute(
            [Table("t", live)],
            [],
            [Table("t", target)],
            []
        );

        withDialect.Items.Should().NotContain(i => i.Kind == SchemaDiffKind.AlterColumn);
        withoutDialect.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn);
    }

    /// <summary>正規化しても違う型（精度が違う）は従来どおり AlterColumn になることを検証する</summary>
    [Theory(DisplayName = "正規化しても違う型は AlterColumn になる")]
    [InlineData("datetime2", "datetime2(3)")]
    [InlineData("numeric(18)", "numeric(18,2)")]
    public void ColumnType_DifferentAfterNormalization_IsAltered(string live, string target)
    {
        var diff = new SchemaDiffService().Compute(
            [Table("t", live)],
            [],
            [Table("t", target)],
            [],
            new SqlServerProvider().SyncCapabilities
        );

        diff.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn);
    }

    /// <summary>親 Parent(Id) ← 子 Child(ParentId) の外部キーを、指定の参照アクションで live / target の両方に作る</summary>
    private static (
        List<Entity> Live,
        List<Relationship> LiveRels,
        List<Entity> Target,
        List<Relationship> TargetRels
    ) ForeignKey(
        ForeignKeyReferentialAction liveOnDelete,
        ForeignKeyReferentialAction liveOnUpdate,
        ForeignKeyReferentialAction targetOnDelete,
        ForeignKeyReferentialAction targetOnUpdate
    )
    {
        (List<Entity>, List<Relationship>) Build(
            ForeignKeyReferentialAction onDelete,
            ForeignKeyReferentialAction onUpdate
        )
        {
            var parent = Table("Parent", "int");
            var child = Table("Child", "int");
            var rel = new Relationship
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                ConstraintName = "FK_Child_Parent",
                OnDelete = onDelete,
                OnUpdate = onUpdate,
            };

            return ([parent, child], [rel]);
        }

        var (live, liveRels) = Build(liveOnDelete, liveOnUpdate);
        var (target, targetRels) = Build(targetOnDelete, targetOnUpdate);
        return (live, liveRels, target, targetRels);
    }

    /// <summary>ON UPDATE と ON DELETE SET DEFAULT を持たない方言（Oracle）の宣言を模した capabilities</summary>
    private static readonly SyncDialectCapabilities NoOnUpdateNoSetDefault = new()
    {
        SupportsOnUpdateAction = false,
        SupportsSetDefaultOnDelete = false,
    };

    /// <summary>
    /// ON UPDATE と ON DELETE SET DEFAULT を持たない方言では、図のそれらを実効値（NO ACTION）として比べるので、
    /// DB に作れない参照アクションの差分が出ないことを検証する。両方を持つ方言（既定）では差分になる（対照）。
    /// </summary>
    [Theory(DisplayName = "表せない参照アクションは方言の実効値へ畳んで比べる")]
    [InlineData(ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade)]
    [InlineData(ForeignKeyReferentialAction.SetDefault, ForeignKeyReferentialAction.NoAction)]
    [InlineData(ForeignKeyReferentialAction.SetDefault, ForeignKeyReferentialAction.SetNull)]
    public void UnsupportedReferentialAction_IsFolded(
        ForeignKeyReferentialAction targetOnDelete,
        ForeignKeyReferentialAction targetOnUpdate
    )
    {
        var (live, liveRels, target, targetRels) = ForeignKey(
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction,
            targetOnDelete,
            targetOnUpdate
        );

        var folded = new SchemaDiffService().Compute(
            live,
            liveRels,
            target,
            targetRels,
            NoOnUpdateNoSetDefault
        );
        var unfolded = new SchemaDiffService().Compute(
            live,
            liveRels,
            target,
            targetRels,
            new SyncDialectCapabilities()
        );

        folded.Items.Should().NotContain(i => IsForeignKeyDiff(i));
        unfolded.Items.Should().ContainSingle(i => i.Kind == SchemaDiffKind.AlterForeignKey);
    }

    /// <summary>
    /// 方言が表せる参照アクションの変更（ON DELETE CASCADE）は従来どおり外部キー変更になり、変更内容の文言には
    /// 表せない ON UPDATE を載せない（実効値で比べる）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "外部キー変更の文言は参照アクションを実効値で書く")]
    public void AlterForeignKey_DescribesEffectiveActionsOnly()
    {
        var (live, liveRels, target, targetRels) = ForeignKey(
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.Cascade,
            ForeignKeyReferentialAction.Cascade
        );

        var diff = new SchemaDiffService().Compute(
            live,
            liveRels,
            target,
            targetRels,
            NoOnUpdateNoSetDefault
        );

        var alter = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterForeignKey)
            .Which;
        alter.Description.Should().Contain("ON DELETE");
        alter.Description.Should().NotContain("ON UPDATE");
    }

    private static bool IsForeignKeyDiff(SchemaDiffItem item) =>
        item.Kind
            is SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AlterForeignKey;
}
