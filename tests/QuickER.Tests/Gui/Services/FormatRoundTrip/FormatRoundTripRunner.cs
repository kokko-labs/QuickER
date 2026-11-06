using System.Text.RegularExpressions;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Services;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>面の違い（元の図と取り込んだ図で食い違った面と行）</summary>
/// <param name="Facet">面</param>
/// <param name="Detail">食い違いの説明</param>
internal sealed record FacetDifference(Facet Facet, string Detail);

/// <summary>形式の往復の共通の表明（1 つのケース＝形の行 × 形式）</summary>
/// <remarks>
/// 表明は 6 つ（計画の 1.4）。
/// (1) 往復：面ごとに一致するか、違うなら告知されたか、運ばない面として宣言してある。
/// (2) 告知と出力の食い違いが無い：告知した種類はその形式が告知しうる種類で、対応する面が実際に違う。
/// (3) 注入が無い：表・列が増減しない（<see cref="Facet.Structure"/> の違いは常に失敗）・
/// 形式が素で書けない文字を含む名前・説明が出力にそのまま出ない。
/// (4) カルチャ不変：出力のテキストと取り込んだ図の写しが、どのカルチャでもインバリアントと同じ（<see cref="CheckCulture"/>・カルチャの代表の形の行だけ）。
/// (5) マージで戻る：マージ取込の経路がある形式は、運ばない面のうちマージが戻す面も元と一致し、表・列の Id が元のまま。
/// (6) 規則の行の不動点：<see cref="FixedPoint"/>（規則の行の入力を取り込んだ図にも (1) を当てる）。
/// </remarks>
internal static class FormatRoundTripRunner
{
    /// <summary>形の行 × 形式の 1 ケースを流し、問題を返す（空なら合格）</summary>
    public static List<string> Check(FormatAdapter adapter, FormatShape shape)
    {
        var problems = new List<string>();
        var original = shape.Build();
        shape.Expectations.TryGetValue(adapter.AxisValue, out var expectation);

        var omissions = adapter.DetectOmissions(original);
        CheckOmissionExpectations(omissions, expectation, problems);

        using var artifact = adapter.Export(original, shape.Settings);
        CheckVerbatim(adapter, original, artifact.Text, problems);

        if (expectation is not null)
        {
            problems.AddRange(expectation.Output.Problems([artifact.Text], "出力"));
            problems.AddRange(
                expectation.OutputLines.Problems(SplitLines(artifact.Text), "出力の行")
            );
        }

        if (!adapter.CanImport)
        {
            if (
                expectation is not null
                && (
                    !expectation.Imported.IsEmpty
                    || expectation.ImportedDiagram.Count > 0
                    || expectation.ImportedResult.Count > 0
                )
            )
            {
                problems.Add("取込の無い形式に取込の期待がある");
            }

            return problems;
        }

        var imported = adapter.Import(artifact, original.TargetDbms, shape.Settings);
        var differences = Compare(original, imported.Diagram);
        CheckExcused(adapter, differences, omissions, "(1) 往復", problems);
        CheckNotices(adapter, original, omissions, differences, problems);

        if (shape.DistinctDdlConstraintNames)
        {
            CheckDistinctDdlConstraintNames(imported.Diagram, problems);
        }

        if (expectation is not null)
        {
            problems.AddRange(expectation.Imported.Problems(Describe(imported.Diagram), "取込"));
            problems.AddRange(expectation.Warnings.Problems(imported.Warnings, "警告"));

            foreach (var (description, predicate) in expectation.ImportedResult)
            {
                if (!predicate(imported))
                {
                    problems.Add($"取込の結果: {description} が成り立たない");
                }
            }

            foreach (var (description, predicate) in expectation.ImportedDiagram)
            {
                if (!predicate(imported.Diagram))
                {
                    problems.Add($"取込: {description} が成り立たない");
                }
            }
        }

        if (adapter.Merge is { } merge)
        {
            // 往復の取込を使い回す（マージは取込の図の Id をその場で書き換えるので、ほかの表明の後に置く）
            CheckMerge(shape, merge, imported, adapter, problems);
        }

        return problems;
    }

    /// <summary>(4) カルチャ不変：出力のテキストと取り込んだ図の写しが、どのカルチャでもインバリアントと同じ</summary>
    /// <remarks>
    /// 書式のカルチャが効くのは数値・日時・大文字小文字の書き方で、図の形（制約・リレーションの組み方）には依らない。
    /// そのため全形では流さず、カルチャの代表の形の行（<see cref="FormatShape.CultureProbe"/>）だけで流す。
    /// </remarks>
    public static List<string> CheckCulture(FormatAdapter adapter, FormatShape shape)
    {
        var problems = new List<string>();
        var original = shape.Build();
        problems.AddRange(
            CultureScope
                .FindDifferences(() =>
                {
                    using var again = adapter.Export(original, shape.Settings);
                    return again.Text;
                })
                .Select(d => $"(4) 出力のカルチャ: {d}")
        );

        if (!adapter.CanImport)
        {
            return problems;
        }

        using var artifact = adapter.Export(original, shape.Settings);
        problems.AddRange(
            CultureScope
                .FindDifferences(() =>
                    string.Join(
                        "\n",
                        Describe(
                            adapter.Import(artifact, original.TargetDbms, shape.Settings).Diagram
                        )
                    )
                )
                .Select(d => $"(4) 取込のカルチャ: {d}")
        );

        return problems;
    }

    /// <summary>(6) 規則の行の不動点：取り込んだ図を出力して取り込み直しても、運ぶ面は変わらない</summary>
    /// <param name="adapter">形式</param>
    /// <param name="first">規則の行の入力を取り込んだ図</param>
    /// <param name="settings">設定</param>
    public static List<string> FixedPoint(
        FormatAdapter adapter,
        ErDiagram first,
        FormatSettings settings
    )
    {
        var problems = new List<string>();
        var omissions = adapter.DetectOmissions(first);
        using var artifact = adapter.Export(first, settings);
        var second = adapter.Import(artifact, first.TargetDbms, settings);
        CheckExcused(adapter, Compare(first, second.Diagram), omissions, "(6) 不動点", problems);
        return problems;
    }

    /// <summary>元の図と取り込んだ図の面の違い</summary>
    public static List<FacetDifference> Compare(ErDiagram original, ErDiagram imported)
    {
        var left = FormatSchemaProjection.Lines(original);
        var right = FormatSchemaProjection.Lines(imported, FormatPairing.Pair(original, imported));
        var differences = new List<FacetDifference>();
        var keys = FormatPairing.Identity(original);
        // 外部キーの子の列（多対多の列ペアは外部キーでない：docs/import-export.ja.md「多対多のリレーションは列対応を持ちません」）
        var children = original
            .Relationships.Where(r => r.Type != RelationshipType.ManyToMany)
            .SelectMany(r => r.ColumnPairs.Select(p => keys.Column(p.TargetColumnId)))
            .ToHashSet(StringComparer.Ordinal);
        var emptyTables = original
            .Entities.Where(e => e.Columns.Count == 0)
            .Select(e => keys.Table(e.Id))
            .ToHashSet(StringComparer.Ordinal);
        var composites = original
            .Relationships.Where(r =>
                r.Type != RelationshipType.ManyToMany && r.ColumnPairs.Count > 1
            )
            .ToList();
        var pairlessKeys = original
            .Relationships.Where(r =>
                r.Type != RelationshipType.ManyToMany && r.ColumnPairs.Count == 0
            )
            .Select(r => keys.Relationship(r.Id))
            .ToHashSet(StringComparer.Ordinal);
        var compositeKeys = composites
            .Select(r => keys.Relationship(r.Id))
            .ToHashSet(StringComparer.Ordinal);
        // 元の図で列ペアの無かったリレーションに、取込が推論して作った列ペアの子の列（その印は列ペアの違いの一部）
        var importedKeys = FormatPairing.Pair(original, imported);
        var inferredChildren = imported
            .Relationships.Where(r => pairlessKeys.Contains(importedKeys.Relationship(r.Id)))
            .SelectMany(r => r.ColumnPairs.Select(p => importedKeys.Column(p.TargetColumnId)))
            .ToHashSet(StringComparer.Ordinal);
        var compositeChildren = composites
            .SelectMany(r => r.ColumnPairs.Select(p => keys.Column(p.TargetColumnId)))
            .ToHashSet(StringComparer.Ordinal);

        var manyToManyKeys = original
            .Relationships.Where(r => r.Type == RelationshipType.ManyToMany)
            .Select(r => keys.Relationship(r.Id))
            .ToHashSet(StringComparer.Ordinal);
        var originalColumnIds = original
            .Entities.SelectMany(e => e.Columns)
            .Select(c => c.Id)
            .ToHashSet();
        var broken = original
            .Relationships.Where(r =>
                r.ColumnPairs.Any(p =>
                    !originalColumnIds.Contains(p.SourceColumnId)
                    || !originalColumnIds.Contains(p.TargetColumnId)
                )
            )
            .ToList();
        var brokenKeys = broken
            .Select(r => keys.Relationship(r.Id))
            .ToHashSet(StringComparer.Ordinal);
        var brokenChildren = broken
            .SelectMany(r => r.ColumnPairs.Select(p => keys.Column(p.TargetColumnId)))
            .ToHashSet(StringComparer.Ordinal);
        var brokenChildTables = broken
            .Select(r => keys.Table(r.TargetEntityId))
            .ToHashSet(StringComparer.Ordinal);
        var primaryKeys = original
            .Entities.SelectMany(e =>
                e.Columns.Where(c => c.IsPrimaryKey).Select(c => keys.Column(c.Id))
            )
            .ToHashSet(StringComparer.Ordinal);
        var leftRaw = RawTexts(original, keys);
        var rightRaw = RawTexts(imported, FormatPairing.Pair(original, imported));

        // 改行・制御文字を空白へ畳んだ違いだけか（生の値で見る。写しの値は改行を見える形にしてある）
        bool OnlySanitized(Facet facet, string key) =>
            leftRaw.TryGetValue((facet, key), out var before)
            && rightRaw.TryGetValue((facet, key), out var after)
            && !string.Equals(before, after, StringComparison.Ordinal)
            && string.Equals(ExportTextSanitizer.Sanitize(before), after, StringComparison.Ordinal);

        // 型の表記の大文字小文字（と前後の空白）だけの違いか（同期が同じ型とみなす規則＝DbTypeText.AreEquivalent）
        bool OnlyTypeCase(string key) =>
            leftRaw.TryGetValue((Facet.Type, key), out var before)
            && rightRaw.TryGetValue((Facet.Type, key), out var after)
            && !string.Equals(before, after, StringComparison.Ordinal)
            && DbTypeText.AreEquivalent(before, after);

        // 元の図で列の無かった表の列・並び（取込が補った列の違い）
        bool OnEmptyTable(string key) =>
            emptyTables.Contains(key)
            || emptyTables.Any(table =>
                key.StartsWith($"column {table}.", StringComparison.Ordinal)
                || key.StartsWith($"{table}.", StringComparison.Ordinal)
            );

        // 存在の面の余りのうち、取込が手がかりから補ったものを別の面に振り分ける
        Facet ClassifyExtra(Facet facet, string key) =>
            facet switch
            {
                _ when OnEmptyTable(key) => Facet.PlaceholderColumn,
                Facet.ForeignKeyMarker
                    when brokenChildTables.Any(table =>
                        key.StartsWith($"{table}.", StringComparison.Ordinal)
                    ) => Facet.BrokenColumnPairs,
                Facet.ForeignKeyMarker when children.Contains(key) => Facet.ForeignKeyMarkerDerived,
                Facet.ForeignKeyMarker when inferredChildren.Contains(key) => Facet.ColumnPairs,
                _ => facet,
            };

        // 存在の面の欠けのうち、元の図の複合外部キーごと消えたものを別の面に振り分ける
        Facet ClassifyMissing(Facet facet, string key) =>
            facet switch
            {
                Facet.Relationship when compositeKeys.Contains(key) => Facet.CompositeRelationship,
                Facet.Relationship when pairlessKeys.Contains(key) => Facet.PairlessRelationship,
                Facet.Relationship or Facet.ManyToMany when brokenKeys.Contains(key) =>
                    Facet.BrokenColumnPairs,
                Facet.ForeignKeyMarker when brokenChildren.Contains(key) => Facet.BrokenColumnPairs,
                Facet.ForeignKeyMarker when compositeChildren.Contains(key) =>
                    Facet.CompositeRelationship,
                Facet.ForeignKeyMarker when !children.Contains(key) =>
                    Facet.ForeignKeyMarkerOutsideRelationship,
                _ => facet,
            };

        foreach (var facet in Enum.GetValues<Facet>())
        {
            var l = left.Where(line => line.Facet == facet).ToList();
            var r = right.Where(line => line.Facet == facet).ToList();

            if (FacetKinds.IsExistence(facet))
            {
                foreach (
                    var missing in l.Select(x => x.Key)
                        .Except(r.Select(x => x.Key), StringComparer.Ordinal)
                )
                {
                    var actual = ClassifyMissing(facet, missing);
                    differences.Add(
                        new FacetDifference(actual, $"{actual} {missing} が取込で消えた")
                    );
                }

                foreach (
                    var extra in r.Select(x => x.Key)
                        .Except(l.Select(x => x.Key), StringComparer.Ordinal)
                )
                {
                    var actual = ClassifyExtra(facet, extra);
                    differences.Add(
                        new FacetDifference(actual, $"{actual} {extra} が取込で増えた")
                    );
                }

                continue;
            }

            var rightByKey = r.GroupBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

            foreach (var line in l)
            {
                if (
                    rightByKey.TryGetValue(line.Key, out var value)
                    && !string.Equals(value, line.Value, StringComparison.Ordinal)
                )
                {
                    // 名前の面は、元の図で名前が無かった要素に名前が付いた違いを別の面に振り分ける。
                    // 列の無かった表の列の並びの違いは、取込が補った列の違い
                    var actual =
                        facet == Facet.ColumnOrder && OnEmptyTable(line.Key)
                            ? Facet.PlaceholderColumn
                        : facet == Facet.ColumnPairs && brokenKeys.Contains(line.Key)
                            ? Facet.BrokenColumnPairs
                        : facet == Facet.ColumnPairs && manyToManyKeys.Contains(line.Key)
                            ? Facet.ManyToManyPairs
                        : facet == Facet.Nullability
                        && primaryKeys.Contains(line.Key)
                        && line.Value == "null"
                        && value == "notnull"
                            ? Facet.PrimaryKeyNullability
                        : facet == Facet.Type && OnlyTypeCase(line.Key) ? Facet.TypeCase
                        : facet
                            is Facet.TableName
                                or Facet.ColumnName
                                or Facet.Type
                                or Facet.ConstraintName
                        && OnlySanitized(facet, line.Key)
                            ? Facet.NameSanitized
                        : facet is Facet.TableDescription or Facet.ColumnDescription or Facet.Memo
                        && OnlySanitized(facet, line.Key)
                            ? Facet.DescriptionLineBreaks
                        : line.Value == "-"
                        && FacetKinds.FilledFacets.TryGetValue(facet, out var filled)
                            ? filled
                        : facet;
                    differences.Add(
                        new FacetDifference(actual, $"{actual} {line.Key}: {line.Value} → {value}")
                    );
                }
            }
        }

        return differences;
    }

    /// <summary>面の生の値（名前・型・制約名・説明・メモ。鍵は組みが与える）</summary>
    private static Dictionary<(Facet, string), string> RawTexts(
        ErDiagram diagram,
        FormatPairing keys
    )
    {
        var raw = new Dictionary<(Facet, string), string>();

        foreach (var entity in diagram.Entities)
        {
            var table = keys.Table(entity.Id);
            raw[(Facet.TableName, table)] = entity.TableName;
            raw[(Facet.TableDescription, table)] = entity.Description;
            raw[(Facet.Memo, table)] = entity.Memo;

            foreach (var column in entity.Columns)
            {
                var key = keys.Column(column.Id);
                raw[(Facet.ColumnName, key)] = column.Name;
                raw[(Facet.Type, key)] = column.DataType;
                raw[(Facet.ColumnDescription, key)] = column.Description;
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            raw[(Facet.ConstraintName, keys.Relationship(relationship.Id))] =
                relationship.ConstraintName ?? string.Empty;
        }

        return raw;
    }

    /// <summary>取り込んだ図の写し（取り込んだ図の自分の名前を鍵にした行の文字列。行の期待が照合する）</summary>
    public static List<string> Describe(ErDiagram diagram) =>
        FormatSchemaProjection.Lines(diagram).Select(line => line.ToString()).ToList();

    /// <summary>違う面が、告知か宣言で説明できるか（説明できない違いを問題にする）</summary>
    private static void CheckExcused(
        FormatAdapter adapter,
        IReadOnlyList<FacetDifference> differences,
        IReadOnlyList<ExportOmissionKind>? omissions,
        string what,
        List<string> problems
    )
    {
        var notified = (omissions ?? [])
            .SelectMany(kind => FacetKinds.OmissionFacets[kind])
            .ToHashSet();
        var notCarried = adapter.NotCarriedFacets;

        foreach (var difference in differences)
        {
            if (difference.Facet == Facet.Structure)
            {
                problems.Add(
                    $"{what}: 表・列が増減した（告知も宣言も理由にならない）: {difference.Detail}"
                );
            }
            else if (!notified.Contains(difference.Facet) && !notCarried.Contains(difference.Facet))
            {
                problems.Add($"{what}: 告知も運ばない宣言も無い面が違う: {difference.Detail}");
            }
        }
    }

    /// <summary>(2) 告知した種類は告知しうる種類で、対応する面が実際に違う</summary>
    private static void CheckNotices(
        FormatAdapter adapter,
        ErDiagram original,
        IReadOnlyList<ExportOmissionKind>? omissions,
        IReadOnlyList<FacetDifference> differences,
        List<string> problems
    )
    {
        if (omissions is null)
        {
            return;
        }

        var differing = differences.Select(d => d.Facet).ToHashSet();
        var reinferred = adapter.Reinferred.Select(item => item.Kind).ToHashSet();
        var originalLines = FormatSchemaProjection.Lines(original);

        foreach (var kind in omissions)
        {
            if (!adapter.ReportableOmissions.Contains(kind))
            {
                problems.Add($"(2) 告知: {kind} はこの形式が告知しうる種類に無い");
            }

            // 属性の告知は、持ち主ごと消えた要素でも成り立つ（複合の一意制約が消えれば、その名前も運ばれていない）
            if (
                !FacetKinds
                    .OmissionFacets[kind]
                    .Any(facet =>
                        differing.Contains(facet)
                        || FacetKinds.Owners(facet).Any(differing.Contains)
                    )
            )
            {
                // 取込が推論し直す種類は、元の図にその面の中身があれば、同じ値に戻っても告知してよい（宣言した種類だけ）
                var hasContent = originalLines.Any(line =>
                    FacetKinds.OmissionFacets[kind].Contains(line.Facet)
                    && !FacetKinds.IsDefault(line.Value)
                );

                if (!(reinferred.Contains(kind) && hasContent))
                {
                    problems.Add(
                        $"(2) 告知: {kind} を告知したが、対応する面（{string.Join(",", FacetKinds.OmissionFacets[kind])}）は往復で違わない"
                    );
                }
            }
        }
    }

    /// <summary>(3) 形式が素で書けない文字を含む名前・説明が、出力にそのまま出ない</summary>
    private static void CheckVerbatim(
        FormatAdapter adapter,
        ErDiagram diagram,
        string text,
        List<string> problems
    )
    {
        var unsafeCharacters = adapter.MustEscape;

        if (unsafeCharacters.Length == 0)
        {
            return;
        }

        foreach (var (what, value) in UserTexts(diagram, includeDescriptions: true))
        {
            if (
                value.AsSpan().IndexOfAny(unsafeCharacters) >= 0
                && text.Contains(value, StringComparison.Ordinal)
            )
            {
                problems.Add($"(3) 注入: {what} が素で書けない文字を含んだまま出力に出る");
            }
        }
    }

    /// <summary>図の利用者の文字列（表名・列名・制約名・説明・メモ）</summary>
    private static IEnumerable<(string What, string Text)> UserTexts(
        ErDiagram diagram,
        bool includeDescriptions
    )
    {
        foreach (var entity in diagram.Entities)
        {
            yield return ($"表名 {entity.TableName}", entity.TableName);

            if (includeDescriptions)
            {
                yield return ($"表 {entity.TableName} の説明", entity.Description);
                yield return ($"表 {entity.TableName} のメモ", entity.Memo);
            }

            foreach (var column in entity.Columns)
            {
                yield return ($"列名 {entity.TableName}.{column.Name}", column.Name);

                if (includeDescriptions)
                {
                    yield return (
                        $"列 {entity.TableName}.{column.Name} の説明",
                        column.Description
                    );
                }
            }

            foreach (var unique in entity.UniqueConstraints.Where(u => u.Name is not null))
            {
                yield return ($"一意制約名 {unique.Name}", unique.Name!);
            }
        }

        foreach (var relationship in diagram.Relationships.Where(r => r.ConstraintName is not null))
        {
            yield return ($"制約名 {relationship.ConstraintName}", relationship.ConstraintName!);
        }
    }

    /// <summary>行の告知の期待</summary>
    private static void CheckOmissionExpectations(
        IReadOnlyList<ExportOmissionKind>? omissions,
        FormatExpectation? expectation,
        List<string> problems
    )
    {
        if (expectation is null)
        {
            return;
        }

        var hasExpectation =
            expectation.OmissionsExact is not null
            || expectation.OmissionsInclude.Count > 0
            || expectation.OmissionsExclude.Count > 0;

        if (omissions is null)
        {
            if (hasExpectation)
            {
                problems.Add("告知しない形式に告知の期待がある");
            }

            return;
        }

        if (expectation.OmissionsExact is { } exact && !exact.SequenceEqual(omissions))
        {
            problems.Add(
                $"告知: [{string.Join(", ", omissions)}]（期待 [{string.Join(", ", exact)}]）"
            );
        }

        foreach (var kind in expectation.OmissionsInclude.Where(kind => !omissions.Contains(kind)))
        {
            problems.Add($"告知: {kind} が無い（実際 [{string.Join(", ", omissions)}]）");
        }

        foreach (var kind in expectation.OmissionsExclude.Where(omissions.Contains))
        {
            problems.Add($"告知: {kind} がある");
        }
    }

    /// <summary>(5) マージ取込で、運ぶ面とマージが戻す面が元と一致し、表・列の Id が元のまま</summary>
    private static void CheckMerge(
        FormatShape shape,
        Func<ErDiagram, FormatImport, FormatMergeOutcome> merge,
        FormatImport imported,
        FormatAdapter adapter,
        List<string> problems
    )
    {
        var current = shape.Build();
        var merged = merge(current, imported).Merged;

        var tableIds = current.Entities.Select(e => e.Id).ToHashSet();
        var columnIds = current.Entities.SelectMany(e => e.Columns).Select(c => c.Id).ToHashSet();

        foreach (var entity in merged.Entities.Where(e => !tableIds.Contains(e.Id)))
        {
            problems.Add($"(5) マージ: 表 {entity.TableName} の Id が元の図に無い");
        }

        foreach (
            var column in merged
                .Entities.SelectMany(e => e.Columns)
                .Where(c => !columnIds.Contains(c.Id))
        )
        {
            problems.Add($"(5) マージ: 列 {column.Name} の Id が元の図に無い");
        }

        var allowed = adapter.NotCarriedFacets.Except(adapter.MergeRestores).ToHashSet();

        foreach (var difference in Compare(current, merged))
        {
            if (!allowed.Contains(difference.Facet))
            {
                problems.Add($"(5) マージ: 面が戻らない: {difference.Detail}");
            }
        }
    }

    /// <summary>取り込んだ図の SQL Server の DDL の制約名がすべて違う（大文字小文字を無視）</summary>
    private static void CheckDistinctDdlConstraintNames(ErDiagram diagram, List<string> problems)
    {
        var ddl = new SqlServerDdlGenerator().Build(diagram);
        var duplicated = Regex
            .Matches(ddl, @"CONSTRAINT \[([^\]]+)\]")
            .Select(match => match.Groups[1].Value.ToUpperInvariant())
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicated.Count > 0)
        {
            problems.Add($"DDL の制約名が重複している: {string.Join(", ", duplicated)}");
        }
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
}
