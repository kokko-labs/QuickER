using QuickER.Model;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>写しの 1 行（面・鍵・値）</summary>
/// <param name="Facet">面</param>
/// <param name="Key">要素の鍵（元の図の名前で書く）</param>
/// <param name="Value">値（存在の面は空）</param>
internal sealed record FacetLine(Facet Facet, string Key, string Value)
{
    /// <inheritdoc/>
    public override string ToString() =>
        Value.Length == 0 ? $"{Facet} {Key}" : $"{Facet} {Key} = {Value}";
}

/// <summary>元の図と取り込んだ図の要素の組（取り込んだ側の要素に、組んだ元の要素の鍵を与える）</summary>
/// <remarks>
/// 表は名前の完全一致で組み、残りを並びで組む。列も同じ。名前を畳む形式でも面が混ざらず「表名だけが違う」と言える。
/// リレーションは端の表の組が同じものの中で、制約名 → 列ペア → 並びの順に組む。
/// 組めずに余った要素は <c>+</c> で始まる鍵になる（存在の面の余りとして数える）。
/// </remarks>
internal sealed class FormatPairing
{
    private readonly Dictionary<Guid, string> _tables = [];
    private readonly Dictionary<Guid, (string Table, string Column)> _columns = [];
    private readonly Dictionary<Guid, string> _relationships = [];

    private FormatPairing() { }

    /// <summary>元の図だけの鍵（自分の名前）</summary>
    public static FormatPairing Identity(ErDiagram diagram)
    {
        var pairing = new FormatPairing();
        var keys = TableKeys(diagram);

        foreach (var entity in diagram.Entities)
        {
            pairing._tables[entity.Id] = keys[entity.Id];

            foreach (var column in entity.Columns)
            {
                pairing._columns[column.Id] = (keys[entity.Id], column.Name);
            }
        }

        foreach (var (id, key) in RelationshipKeys(diagram, pairing))
        {
            pairing._relationships[id] = key;
        }

        return pairing;
    }

    /// <summary>元の図と取り込んだ図を組み、取り込んだ側の鍵を返す（元の側の鍵は <see cref="Identity"/>）</summary>
    public static FormatPairing Pair(ErDiagram original, ErDiagram imported)
    {
        var pairing = new FormatPairing();
        var originalKeys = TableKeys(original);
        var pairs = PairByNameThenOrder(
            original.Entities,
            imported.Entities,
            entity => entity.TableName
        );

        foreach (var entity in imported.Entities)
        {
            if (pairs.TryGetValue(entity, out var source))
            {
                var tableKey = originalKeys[source.Id];
                pairing._tables[entity.Id] = tableKey;
                var columnPairs = PairByNameThenOrder(
                    source.Columns,
                    entity.Columns,
                    column => column.Name
                );

                foreach (var column in entity.Columns)
                {
                    pairing._columns[column.Id] = columnPairs.TryGetValue(column, out var c)
                        ? (tableKey, c.Name)
                        : (tableKey, "+" + column.Name);
                }
            }
            else
            {
                pairing._tables[entity.Id] = "+" + entity.TableName;

                foreach (var column in entity.Columns)
                {
                    pairing._columns[column.Id] = ("+" + entity.TableName, column.Name);
                }
            }
        }

        var originalKeys2 = Identity(original);
        var originalRelationships = RelationshipKeys(original, originalKeys2);

        foreach (var group in imported.Relationships.GroupBy(r => EndsKey(pairing, r)))
        {
            var candidates = original
                .Relationships.Where(r => EndsKey(originalKeys2, r) == group.Key)
                .ToList();
            var remaining = new List<Relationship>(candidates);
            var assigned = new Dictionary<Relationship, Relationship>();

            foreach (
                var match in new Func<Relationship, Relationship, bool>[]
                {
                    (o, i) => o.ConstraintName is { } name && name == i.ConstraintName,
                    (o, i) =>
                        PairsText(originalKeys2, o) == PairsText(pairing, i)
                        && o.ColumnPairs.Count > 0,
                    (_, _) => true,
                }
            )
            {
                foreach (var relationship in group.Where(r => !assigned.ContainsKey(r)))
                {
                    var hit = remaining.FirstOrDefault(o => match(o, relationship));

                    if (hit is not null)
                    {
                        assigned[relationship] = hit;
                        remaining.Remove(hit);
                    }
                }
            }

            var extra = 0;

            foreach (var relationship in group)
            {
                pairing._relationships[relationship.Id] = assigned.TryGetValue(
                    relationship,
                    out var source
                )
                    ? originalRelationships[source.Id]
                    : $"{group.Key}#+{extra++}";
            }
        }

        return pairing;
    }

    /// <summary>表の鍵（名前。同じ名前が重なれば 2 つ目から <c>#n</c> を付ける）</summary>
    private static Dictionary<Guid, string> TableKeys(ErDiagram diagram)
    {
        var keys = new Dictionary<Guid, string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entity in diagram.Entities)
        {
            var count = seen.GetValueOrDefault(entity.TableName);
            seen[entity.TableName] = count + 1;
            keys[entity.Id] = count == 0 ? entity.TableName : $"{entity.TableName}#{count}";
        }

        return keys;
    }

    private static Dictionary<Guid, string> RelationshipKeys(
        ErDiagram diagram,
        FormatPairing pairing
    )
    {
        var keys = new Dictionary<Guid, string>();

        foreach (var group in diagram.Relationships.GroupBy(r => EndsKey(pairing, r)))
        {
            var index = 0;

            foreach (var relationship in group)
            {
                keys[relationship.Id] = $"{group.Key}#{index++}";
            }
        }

        return keys;
    }

    private static string EndsKey(FormatPairing pairing, Relationship relationship) =>
        $"{pairing.Table(relationship.SourceEntityId)}->{pairing.Table(relationship.TargetEntityId)}";

    private static string PairsText(FormatPairing pairing, Relationship relationship) =>
        string.Join(
            ",",
            relationship.ColumnPairs.Select(pair =>
                $"{pairing.Column(pair.SourceColumnId)}>{pairing.Column(pair.TargetColumnId)}"
            )
        );

    /// <summary>名前の完全一致で組み、残りを並びで組む（取り込んだ側の要素 → 元の要素）</summary>
    private static Dictionary<T, T> PairByNameThenOrder<T>(
        IReadOnlyList<T> original,
        IReadOnlyList<T> imported,
        Func<T, string> name
    )
        where T : class
    {
        var result = new Dictionary<T, T>(ReferenceEqualityComparer.Instance);
        var remaining = new List<T>(original);

        foreach (var item in imported)
        {
            var hit = remaining.FirstOrDefault(o =>
                string.Equals(name(o), name(item), StringComparison.Ordinal)
            );

            if (hit is not null)
            {
                result[item] = hit;
                remaining.Remove(hit);
            }
        }

        var left = imported.Where(item => !result.ContainsKey(item)).ToList();

        for (var i = 0; i < left.Count && i < remaining.Count; i++)
        {
            result[left[i]] = remaining[i];
        }

        return result;
    }

    /// <summary>表の鍵（引けなければ <c>?</c>）</summary>
    public string Table(Guid id) => _tables.GetValueOrDefault(id, "?");

    /// <summary>列の鍵（引けなければ <c>?</c>）</summary>
    public string Column(Guid id) =>
        _columns.TryGetValue(id, out var key) ? $"{key.Table}.{key.Column}" : "?";

    /// <summary>列の鍵の列の部分（同じ表の中の並び・構成列に使う。引けなければ <c>?</c>）</summary>
    public string ColumnShort(Guid id) => _columns.TryGetValue(id, out var key) ? key.Column : "?";

    /// <summary>リレーションの鍵（引けなければ <c>?</c>）</summary>
    public string Relationship(Guid id) => _relationships.GetValueOrDefault(id, "?");
}

/// <summary>図を面の行へ写す（鍵は <see cref="FormatPairing"/> が与える元の図の名前）</summary>
internal static class FormatSchemaProjection
{
    /// <summary>元の図の写し</summary>
    public static IReadOnlyList<FacetLine> Lines(ErDiagram diagram) =>
        Lines(diagram, FormatPairing.Identity(diagram));

    /// <summary>図の写し（鍵は組みが与える）</summary>
    public static IReadOnlyList<FacetLine> Lines(ErDiagram diagram, FormatPairing keys)
    {
        var lines = new List<FacetLine>();
        var tableNames = new Dictionary<Guid, string>();

        void Add(Facet facet, string key, string value = "") =>
            lines.Add(new FacetLine(facet, key, value));

        foreach (var entity in diagram.Entities)
        {
            var table = keys.Table(entity.Id);
            tableNames[entity.Id] = table;
            Add(Facet.Structure, "table " + table);
            Add(Facet.TableName, table, entity.TableName);
            Add(Facet.TableDescription, table, Quote(entity.Description));
            Add(Facet.Memo, table, Quote(entity.Memo));
            Add(
                Facet.ColumnOrder,
                table,
                string.Join(",", entity.Columns.Select(column => keys.ColumnShort(column.Id)))
            );

            foreach (var column in entity.Columns)
            {
                var key = keys.Column(column.Id);
                Add(Facet.Structure, "column " + key);
                Add(Facet.ColumnName, key, column.Name);
                Add(Facet.Type, key, column.DataType);
                Add(Facet.Nullability, key, column.IsNullable ? "null" : "notnull");
                Add(Facet.ColumnDescription, key, Quote(column.Description));

                if (column.IsPrimaryKey)
                {
                    Add(Facet.PrimaryKey, key);
                }

                if (column.IsForeignKey)
                {
                    Add(Facet.ForeignKeyMarker, key);
                }

                if (column.IsComputed)
                {
                    Add(Facet.Computed, key);
                }

                if (column.IsIdentity)
                {
                    Add(column.IsPrimaryKey ? Facet.IdentityKey : Facet.IdentityNonKey, key);
                }
            }

            if (entity.Columns.Any(column => column.IsPrimaryKey))
            {
                Add(
                    Facet.PrimaryKeyOrder,
                    table,
                    string.Join(
                        ",",
                        entity.GetPrimaryKeyColumnsInOrder().Select(c => keys.ColumnShort(c.Id))
                    )
                );
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var unique in entity.UniqueConstraints)
            {
                var members = unique.ColumnIds.Select(id => keys.ColumnShort(id)).ToList();
                var set = $"{table}({string.Join(",", members.Order(StringComparer.Ordinal))})";
                var count = seen.GetValueOrDefault(set);
                seen[set] = count + 1;
                var key = count == 0 ? set : $"{set}#{count}";
                Add(Facet.UniqueSet, key);
                Add(Facet.UniqueOrder, key, string.Join(",", members));
                Add(Facet.UniqueName, key, NameOrDash(unique.Name));
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            var key = keys.Relationship(relationship.Id);
            Add(
                relationship.Type == RelationshipType.ManyToMany
                    ? Facet.ManyToMany
                    : Facet.Relationship,
                key
            );
            Add(Facet.RelationshipType, key, relationship.Type.ToString());
            Add(
                Facet.ColumnPairs,
                key,
                string.Join(
                    ",",
                    relationship.ColumnPairs.Select(pair =>
                        $"{keys.ColumnShort(pair.SourceColumnId)}>{keys.ColumnShort(pair.TargetColumnId)}"
                    )
                )
            );
            Add(Facet.ConstraintName, key, NameOrDash(relationship.ConstraintName));
            Add(
                Facet.ReferentialAction,
                key,
                $"del={relationship.OnDelete} upd={relationship.OnUpdate}"
            );
        }

        foreach (var query in diagram.Queries)
        {
            Add(Facet.Query, $"{tableNames.GetValueOrDefault(query.EntityId, "?")}.{query.Name}");
        }

        return lines;
    }

    /// <summary>名前（null と空文字は名前なしの <c>-</c>。モデルは代入した名前の前後の空白を除くので、空白だけの名前は空文字になる）</summary>
    private static string NameOrDash(string? name) => string.IsNullOrEmpty(name) ? "-" : name;

    private static string Quote(string? text) =>
        "\"" + (text ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
