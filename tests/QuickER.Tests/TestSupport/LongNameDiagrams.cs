using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER が付ける既定の制約名が方言の上限を超える図（表の名前が長い図）を組み立てる
/// </summary>
/// <remarks>単体（<c>LongDefaultConstraintNameTests</c>）と Integration（<c>LongDefaultConstraintNameIntegrationTests</c>）が共有する。</remarks>
public static class LongNameDiagrams
{
    /// <summary>
    /// 表の名前が方言の上限ちょうど（ASCII）の親子と、子から親への名前の無い外部キー 2 本・名前の無い一意制約の図
    /// </summary>
    public static ErDiagram Build(IdentifierLengthLimit limit, string dbms)
    {
        var length = limit.IsUnlimited ? 200 : limit.Max;
        var parent = new Entity
        {
            TableName = new string('p', length),
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = IntType(dbms),
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = new string('c', length),
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = IntType(dbms),
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "bill_id",
                    DataType = IntType(dbms),
                    IsNullable = false,
                },
                new Column
                {
                    Name = "ship_id",
                    DataType = IntType(dbms),
                    IsNullable = false,
                },
            },
        };
        child.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = { child.Columns[1].Id, child.Columns[2].Id } }
        );

        Relationship Link(int childColumn) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[childColumn].Id)],
            };

        return new ErDiagram
        {
            TargetDbms = dbms,
            Entities = { parent, child },
            Relationships = { Link(1), Link(2) },
        };
    }

    /// <summary>
    /// 親子それぞれ <paramref name="tableLength"/> 文字（ASCII）の表と、名前の無い外部キー 1 本の図
    /// （旧版の QuickER が作った DB を再現するための最小の形。外部キーの既定名だけが長い）
    /// </summary>
    public static ErDiagram BuildSingleForeignKey(int tableLength, string dbms)
    {
        var parent = new Entity
        {
            TableName = new string('p', tableLength),
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = IntType(dbms),
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = new string('c', tableLength),
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = IntType(dbms),
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "parent_id",
                    DataType = IntType(dbms),
                    IsNullable = false,
                },
            },
        };

        return new ErDiagram
        {
            TargetDbms = dbms,
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            },
        };
    }

    /// <summary>方言が取り込み直したときと同じ綴りの整数型（PostgreSQL は <c>integer</c> で返す）</summary>
    private static string IntType(string dbms) => dbms == "postgresql" ? "integer" : "int";
}
