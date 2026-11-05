using QuickER.Model;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>主題をまたいで使う図の組み立てと、行の期待の補助</summary>
internal static partial class FormatShapeRows
{
    /// <summary>customers（主キー id）と orders（bill_customer_id / ship_customer_id）の図に、customers.id → orders の指定列のリレーションを順に足す</summary>
    /// <param name="links">子の列名と制約名（null＝名前なし）の組（図の順）</param>
    internal static ErDiagram CustomersAndOrders(params (string ChildColumn, string? Name)[] links)
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "bill_customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                },
                new Column
                {
                    Name = "ship_customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                },
            },
        };
        var diagram = new ErDiagram { Entities = { customers, orders } };

        foreach (var (childColumn, name) in links)
        {
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = customers.Id,
                    TargetEntityId = orders.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = name,
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(
                            customers.Columns[0].Id,
                            orders.Columns.Single(column => column.Name == childColumn).Id
                        ),
                    ],
                }
            );
        }

        return diagram;
    }

    /// <summary>Customer(CustomerId PK / Email / TenantId / Code) の図（DbmlUniqueConstraintTests・MermaidUniqueConstraintTests の BuildDiagram）</summary>
    private static (ErDiagram Diagram, Entity Customer) CustomerUniqueDiagram()
    {
        var customer = new Entity
        {
            TableName = "Customer",
            Columns =
            {
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Email",
                    DataType = "nvarchar(100)",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "TenantId",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(20)",
                    IsNullable = false,
                },
            },
        };

        return (new ErDiagram { Entities = { customer } }, customer);
    }

    /// <summary>名前・型・説明に i / I / İ / ı を含み、外部キー・一意制約・参照アクションを持つ図（CultureInvariantExportTests と同じ）</summary>
    private static ErDiagram TurkishIDiagram()
    {
        var items = new Entity { TableName = "Items", Description = "İtem ıtem ITEM item" };
        var id = new Column
        {
            Name = "ID",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "InvoiceCode",
            DataType = "nvarchar(40)",
            IsNullable = false,
        };
        var price = new Column
        {
            Name = "UnitPrice",
            DataType = "decimal(18,4)",
            IsNullable = true,
            Description = "Price in INR",
        };
        items.Columns.AddRange([id, code, price]);
        items.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Items_InvoiceCode", ColumnIds = [code.Id] }
        );
        var lines = new Entity { TableName = "ItemLines" };
        var lineId = new Column
        {
            Name = "LineID",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var itemId = new Column
        {
            Name = "ItemID",
            DataType = "int",
            IsNullable = false,
        };
        lines.Columns.AddRange([lineId, itemId]);

        return new ErDiagram
        {
            TargetDbms = "sqlserver",
            Entities = [items, lines],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = items.Id,
                    TargetEntityId = lines.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(id.Id, itemId.Id)],
                    ConstraintName = "FK_ItemLines_Items",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
        };
    }

    /// <summary>前置きで始まる行だけを取り出した並びが期待と一致する（元のテストが並びまで Equal で見ていたとき）</summary>
    internal static Func<IReadOnlyList<string>, bool> InOrder(
        string prefix,
        params string[] expected
    ) =>
        lines =>
            lines
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .SequenceEqual(expected, StringComparer.Ordinal);

    /// <summary>出力のテキストの、断片を含む行を前後の空白を除いて並べたものが期待と一致する</summary>
    private static Func<IReadOnlyList<string>, bool> TrimmedLinesContaining(
        string fragment,
        params string[] expected
    ) =>
        lines =>
            string.Join("\n", lines)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Where(line => line.Contains(fragment, StringComparison.Ordinal))
                .Select(line => line.Trim())
                .SequenceEqual(expected, StringComparer.Ordinal);

    /// <summary>制約名で引いたリレーションがちょうど 1 本あり、その列ペア（親列名&gt;子列名をカンマで連結）が期待と一致する</summary>
    private static (string, Func<ErDiagram, bool>) PairsOfNamed(string name, string expected) =>
        (
            $"制約名 {name} のリレーションがちょうど 1 本で、列ペアが {expected}",
            diagram =>
                diagram.Relationships.Count(r => r.ConstraintName == name) == 1
                && PairsText(diagram, diagram.Relationships.First(r => r.ConstraintName == name))
                    == expected
        );

    /// <summary>制約名で引いたリレーションがちょうど 1 本あり、その列ペアの数が期待と一致する</summary>
    private static (string, Func<ErDiagram, bool>) PairCountOfNamed(string name, int count) =>
        (
            $"制約名 {name} のリレーションがちょうど 1 本で、列ペアが {count} 組",
            diagram =>
                diagram.Relationships.Count(r => r.ConstraintName == name) == 1
                && diagram.Relationships.First(r => r.ConstraintName == name).ColumnPairs.Count
                    == count
        );

    /// <summary>リレーションの列ペアを「親列名&gt;子列名」のカンマ連結にする（引けない列は ?）</summary>
    private static string PairsText(ErDiagram diagram, Relationship relationship)
    {
        var names = diagram
            .Entities.SelectMany(entity => entity.Columns)
            .ToDictionary(column => column.Id, column => column.Name);

        return string.Join(
            ",",
            relationship.ColumnPairs.Select(pair =>
                $"{names.GetValueOrDefault(pair.SourceColumnId, "?")}>{names.GetValueOrDefault(pair.TargetColumnId, "?")}"
            )
        );
    }

    /// <summary>行の Id に使う形（英小文字・数字以外をハイフンへ畳み、前後のハイフンを除く）</summary>
    private static string Slug(string text)
    {
        var chars = text.ToLowerInvariant()
            .Select(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-')
            .ToArray();
        return string.Join(
            "-",
            new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
