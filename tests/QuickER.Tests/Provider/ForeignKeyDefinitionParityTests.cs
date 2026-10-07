using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 名前の決め方が使う「外部キーを作る関係か」の判定（<see cref="ConstraintNames.DefinesForeignKey"/>・Model）が、
/// DDL・差分・同期計画の判定（多対多でなく <see cref="ForeignKeyColumnPairResolver.Resolve"/> が列を解決できる）と
/// 一致することを検証するテストクラス
/// </summary>
/// <remarks>
/// Model は Provider を参照できないので判定は写しになる。食い違うと、DDL が作らない外部キーが既定名を取り、
/// 同じ組の本物の外部キーの名前がずれる（またはその逆）。
/// </remarks>
public class ForeignKeyDefinitionParityTests
{
    /// <summary>判定の分かれ目になる形のリレーションを、親子とともに列挙する</summary>
    public static TheoryData<string> Shapes =>
        [
            "OneToMany",
            "OneToOne",
            "Composite",
            "ManyToMany",
            "ManyToManyWithPairs",
            "NoPairs",
            "UnresolvedParentColumn",
            "UnresolvedChildColumn",
            "PartiallyUnresolved",
        ];

    /// <summary>形の名前から親・子・リレーションを組み立てる</summary>
    private static (Entity Parent, Entity Child, Relationship Relationship) Build(string shape)
    {
        var parent = new Entity
        {
            TableName = "customers",
            Columns =
            {
                new Column { Name = "tenant_id", IsPrimaryKey = true },
                new Column { Name = "id", IsPrimaryKey = true },
            },
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column { Name = "id", IsPrimaryKey = true },
                new Column { Name = "tenant_id" },
                new Column { Name = "customer_id" },
            },
        };
        var single = new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[2].Id);
        var tenant = new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id);
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
        };

        switch (shape)
        {
            case "OneToMany":
                relationship.ColumnPairs = [single];
                break;
            case "OneToOne":
                relationship.Type = RelationshipType.OneToOne;
                relationship.ColumnPairs = [single];
                break;
            case "Composite":
                relationship.ColumnPairs = [tenant, single];
                break;
            case "ManyToMany":
                relationship.Type = RelationshipType.ManyToMany;
                break;
            case "ManyToManyWithPairs":
                relationship.Type = RelationshipType.ManyToMany;
                relationship.ColumnPairs = [single];
                break;
            case "NoPairs":
                break;
            case "UnresolvedParentColumn":
                relationship.ColumnPairs = [new(Guid.NewGuid(), child.Columns[2].Id)];
                break;
            case "UnresolvedChildColumn":
                relationship.ColumnPairs = [new(parent.Columns[1].Id, Guid.NewGuid())];
                break;
            case "PartiallyUnresolved":
                relationship.ColumnPairs = [tenant, new(parent.Columns[1].Id, Guid.NewGuid())];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return (parent, child, relationship);
    }

    [Theory(DisplayName = "外部キーを作る関係かの判定は、Model と DDL・差分で一致する")]
    [MemberData(nameof(Shapes))]
    public void DefinesForeignKey_AgreesWithProviderResolution(string shape)
    {
        var (parent, child, relationship) = Build(shape);
        var providerDefines =
            relationship.Type != RelationshipType.ManyToMany
            && ForeignKeyColumnPairResolver.Resolve(relationship, parent, child) is not null;

        ConstraintNames
            .DefinesForeignKey(relationship, parent, child)
            .Should()
            .Be(providerDefines, shape);
    }

    [Theory(
        DisplayName = "外部キーを作らない関係は DDL に外部キーとして現れない（判定の両側を DDL で確かめる）"
    )]
    [MemberData(nameof(Shapes))]
    public void DefinesForeignKey_MatchesWhetherDdlEmitsConstraint(string shape)
    {
        var (parent, child, relationship) = Build(shape);
        relationship.ConstraintName = "FK_probe";
        var diagram = new ErDiagram
        {
            Entities = { parent, child },
            Relationships = { relationship },
        };

        var ddl = new QuickER.Provider.SqlServer.SqlServerProvider().DdlGenerator.Build(diagram);

        ddl.Contains("FK_probe", StringComparison.Ordinal)
            .Should()
            .Be(ConstraintNames.DefinesForeignKey(relationship, parent, child), shape);
    }
}
