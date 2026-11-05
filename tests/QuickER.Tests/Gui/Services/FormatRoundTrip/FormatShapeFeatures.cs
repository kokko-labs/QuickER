using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>図の特徴（次元表の水準）と、図の中身から特徴を決める分類</summary>
/// <remarks>
/// 特徴は「主題:水準」の名前で、1 つの図は当てはまる特徴をすべて持つ（いちばん複雑な 1 つに絞らない）。
/// 判定は図の中身だけを読む（行の宣言を読まない＝行を足せば自動で数えられ、宣言の付け忘れが起きない）。
/// </remarks>
internal static class FormatShapeFeatures
{
    /// <summary>特徴（名前と判定・製品の軸の項目）</summary>
    /// <param name="Name">名前</param>
    /// <param name="Applies">図がこの特徴を持つか</param>
    /// <param name="Source">製品の軸の項目（無ければ null）</param>
    internal sealed record FeatureDefinition(
        string Name,
        Func<ErDiagram, bool> Applies,
        AxisMember? Source = null
    );

    /// <summary>特徴の一覧（宣言の順）</summary>
    public static IReadOnlyList<FeatureDefinition> All { get; } =
    [
        new("table:none", d => d.Entities.Count == 0),
        new("table:without-columns", d => d.Entities.Any(e => e.Columns.Count == 0)),
        new(
            "pk:none",
            d => d.Entities.Any(e => e.Columns.Count > 0 && !e.Columns.Any(c => c.IsPrimaryKey))
        ),
        new("pk:single", d => d.Entities.Any(e => e.Columns.Count(c => c.IsPrimaryKey) == 1)),
        new(
            "pk:composite-in-column-order",
            d =>
                d.Entities.Any(e =>
                    e.Columns.Count(c => c.IsPrimaryKey) > 1
                    && e.GetReorderedPrimaryKeyColumnNames() is null
                )
        ),
        new(
            "pk:composite-reordered",
            d => d.Entities.Any(e => e.GetReorderedPrimaryKeyColumnNames() is not null)
        ),
        new(
            "uq:single-unnamed",
            d => Uniques(d).Any(u => u.U.ColumnIds.Count == 1 && u.U.Name is null)
        ),
        new(
            "uq:single-named",
            d => Uniques(d).Any(u => u.U.ColumnIds.Count == 1 && u.U.Name is not null)
        ),
        new(
            "uq:composite-unnamed",
            d => Uniques(d).Any(u => u.U.ColumnIds.Count > 1 && u.U.Name is null)
        ),
        new(
            "uq:composite-named",
            d => Uniques(d).Any(u => u.U.ColumnIds.Count > 1 && u.U.Name is not null)
        ),
        new(
            "uq:on-foreign-key-column",
            d =>
                Uniques(d)
                    .Any(u =>
                        u.U.ColumnIds.Count == 1
                        && u.E.Columns.Any(c => c.Id == u.U.ColumnIds[0] && c.IsForeignKey)
                    )
        ),
        new(
            "rel:one-to-many",
            d => d.Relationships.Any(r => r.Type == RelationshipType.OneToMany),
            AxisMember.Of(RelationshipType.OneToMany)
        ),
        new(
            "rel:one-to-one",
            d => d.Relationships.Any(r => r.Type == RelationshipType.OneToOne),
            AxisMember.Of(RelationshipType.OneToOne)
        ),
        new(
            "rel:many-to-many",
            d => d.Relationships.Any(r => r.Type == RelationshipType.ManyToMany),
            AxisMember.Of(RelationshipType.ManyToMany)
        ),
        new(
            "rel:self-reference",
            d => d.Relationships.Any(r => r.SourceEntityId == r.TargetEntityId)
        ),
        new(
            "rel:parallel",
            d =>
                d.Relationships.GroupBy(r => (r.SourceEntityId, r.TargetEntityId))
                    .Any(g => g.Count() > 1)
        ),
        new(
            "rel:without-pairs",
            d =>
                d.Relationships.Any(r =>
                    r.Type != RelationshipType.ManyToMany && r.ColumnPairs.Count == 0
                )
        ),
        new("rel:composite-pairs", d => d.Relationships.Any(r => r.ColumnPairs.Count > 1)),
        new("rel:named", d => d.Relationships.Any(r => r.ConstraintName is not null)),
        new("rel:unnamed", d => d.Relationships.Any(r => r.ConstraintName is null)),
        new(
            "rel:referential-action",
            d =>
                d.Relationships.Any(r =>
                    r.OnDelete != ForeignKeyReferentialAction.NoAction
                    || r.OnUpdate != ForeignKeyReferentialAction.NoAction
                )
        ),
        new("fk:marker-outside-relationship", HasMarkerOutsideRelationship),
        new("name:needs-quoting", d => Names(d).Any(NeedsQuoting)),
        new("name:non-ascii", d => Names(d).Any(name => name.Any(ch => ch > 0x7F))),
        new(
            "name:line-break",
            d => Names(d).Any(name => name.AsSpan().IndexOfAny('\r', '\n') >= 0)
        ),
        new("desc:table", d => d.Entities.Any(e => e.Description.Length > 0)),
        new(
            "desc:column",
            d => d.Entities.SelectMany(e => e.Columns).Any(c => c.Description.Length > 0)
        ),
        new(
            "desc:special-characters",
            d => Descriptions(d).Any(text => text.AsSpan().IndexOfAny("\r\n'\"\\<>&") >= 0)
        ),
        new("memo", d => d.Entities.Any(e => e.Memo.Length > 0)),
        new("column:computed", d => d.Entities.SelectMany(e => e.Columns).Any(c => c.IsComputed)),
        new(
            "column:identity-non-key",
            d => d.Entities.SelectMany(e => e.Columns).Any(c => c.IsIdentity && !c.IsPrimaryKey)
        ),
        new(
            "column:identity-key",
            d => d.Entities.SelectMany(e => e.Columns).Any(c => c.IsIdentity && c.IsPrimaryKey)
        ),
        new("column:not-null", d => d.Entities.SelectMany(e => e.Columns).Any(c => !c.IsNullable)),
        new("query", d => d.Queries.Count > 0),
    ];

    /// <summary>図が持つ特徴の名前</summary>
    public static IEnumerable<string> Of(ErDiagram diagram) =>
        All.Where(feature => feature.Applies(diagram)).Select(feature => feature.Name);

    /// <summary>形の行の網羅しない組（元のテストに無い形。フェーズ 2・3 の移し元に該当する図が無い）</summary>
    public static IReadOnlyList<CoverageGapEntry> Gaps { get; } =
    [
        .. new[] { "table:none", "rel:self-reference" }.Select(feature =>
            CoverageGap.Matching(
                FormatRoundTripDimensions.Scope,
                "P3-8",
                "P3 の後（元のテストに無い形を足すかの判断）",
                "元のテストに無い形（フェーズ 2・3 の移し元に該当する図が無い）",
                FormatRoundTripDimensions.Format,
                FormatRoundTripDimensions.Feature,
                (_, actual) => actual == feature
            )
        ),
    ];

    /// <summary>告知の網羅しない組</summary>
    public static IReadOnlyList<CoverageGapEntry> OmissionGaps { get; } = [];

    /// <summary>マージの網羅しない組</summary>
    public static IReadOnlyList<CoverageGapEntry> MergeGaps { get; } = [];

    /// <summary>規則の網羅しない組</summary>
    public static IReadOnlyList<CoverageGapEntry> RuleGaps { get; } = [];

    private static IEnumerable<(Entity E, UniqueConstraint U)> Uniques(ErDiagram diagram) =>
        diagram.Entities.SelectMany(e => e.UniqueConstraints.Select(u => (e, u)));

    private static bool HasMarkerOutsideRelationship(ErDiagram diagram)
    {
        var children = diagram
            .Relationships.SelectMany(r => r.ColumnPairs.Select(p => p.TargetColumnId))
            .ToHashSet();

        return diagram
            .Entities.SelectMany(e => e.Columns)
            .Any(c => c.IsForeignKey && !children.Contains(c.Id));
    }

    private static IEnumerable<string> Names(ErDiagram diagram) =>
        diagram
            .Entities.SelectMany(e => e.Columns.Select(c => c.Name).Prepend(e.TableName))
            .Concat(diagram.Relationships.Select(r => r.ConstraintName).OfType<string>())
            .Concat(
                diagram
                    .Entities.SelectMany(e => e.UniqueConstraints)
                    .Select(u => u.Name)
                    .OfType<string>()
            );

    private static IEnumerable<string> Descriptions(ErDiagram diagram) =>
        diagram.Entities.SelectMany(e =>
            e.Columns.Select(c => c.Description).Append(e.Description).Append(e.Memo)
        );

    /// <summary>ASCII の英数字とアンダースコアだけでない名前（DBML・Mermaid が引用・書き換えの対象にする）</summary>
    private static bool NeedsQuoting(string name) =>
        name.Length == 0 || name.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '_'));
}
