using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>受ける系統のうち、受けない水準の 1 行（複数の水準が 1 つの理由を共有してよい）</summary>
/// <param name="Edges">受けない水準の Id</param>
/// <param name="Shape">作らない形の宣言と同じ行（Id は行の名前・形・種類・直す区切り・理由・受け持つ守り）</param>
public sealed record ValueEdgeNotDrawn(IReadOnlyList<string> Edges, NotGeneratedShape Shape)
{
    /// <summary>製品・DB・docs の事実で受けない水準</summary>
    /// <param name="id">行の名前</param>
    /// <param name="edges">受けない水準の Id</param>
    /// <param name="reason">受けない理由（事実と根拠）</param>
    /// <param name="guard">受け持つ守り（無ければ <see cref="NotGenerated.Unguarded"/>）</param>
    public static ValueEdgeNotDrawn Accepted(
        string id,
        string[] edges,
        string reason,
        string guard
    ) => new(edges, NotGenerated.Accepted(id, ShapeOf(edges), reason, guard));

    /// <summary>水準を足す区切りがある水準</summary>
    /// <param name="id">行の名前</param>
    /// <param name="edges">受けない水準の Id</param>
    /// <param name="fixedIn">水準を足す区切り</param>
    /// <param name="reason">まだ受けない理由</param>
    /// <param name="guard">受け持つ守り（無ければ <see cref="NotGenerated.Unguarded"/>）</param>
    public static ValueEdgeNotDrawn Pending(
        string id,
        string[] edges,
        string fixedIn,
        string reason,
        string guard
    ) => new(edges, NotGenerated.Pending(id, ShapeOf(edges), fixedIn, reason, guard));

    private static string ShapeOf(string[] edges) => "値の水準 " + string.Join("・", edges);
}

/// <summary>
/// 値のプールの受ける水準の宣言の 1 行（守り 1 つ＝影響の地図の名前のエンジンの部分か単体の表）
/// </summary>
/// <param name="Name">守りの名前（エンジンの部分は <c>{エンジンの ID}/{部分}</c>・単体の表は <c>table:{名前}</c>）</param>
/// <param name="Families">受ける系統（空なら値を受けない）</param>
/// <param name="NotReceivingReason">値を受けないときの理由（受けるなら null）</param>
/// <param name="NotDrawn">受ける系統のうち受けない水準の行</param>
public sealed record ValuePoolSubject(
    string Name,
    IReadOnlyList<ValueFamily> Families,
    string? NotReceivingReason,
    IReadOnlyList<ValueEdgeNotDrawn> NotDrawn
)
{
    /// <summary>
    /// 値を受けない宣言を後の区切りで棚卸しし直すときの、その区切り（作らない形の Pending と同じ書き方。棚卸しし直さないなら null）
    /// </summary>
    public string? NotReceivingFixedIn { get; init; }

    /// <summary>値を受ける部分</summary>
    /// <param name="name">守りの名前</param>
    /// <param name="families">受ける系統</param>
    /// <param name="notDrawn">受けない水準の行</param>
    public static ValuePoolSubject Receives(
        string name,
        ValueFamily[] families,
        params ValueEdgeNotDrawn[] notDrawn
    ) => new(name, families, null, notDrawn);

    /// <summary>値を受けない部分</summary>
    /// <param name="name">守りの名前</param>
    /// <param name="reason">値を受けない理由</param>
    public static ValuePoolSubject NotReceiving(string name, string reason) =>
        new(name, [], reason, []);

    /// <summary>値を受けるかを、後の区切りで棚卸しし直す部分（今は値を受けないと扱う）</summary>
    /// <param name="name">守りの名前</param>
    /// <param name="reason">今は棚卸ししない理由（範囲の外であること）</param>
    /// <param name="fixedIn">棚卸しし直す区切り</param>
    public static ValuePoolSubject Deferred(string name, string reason, string fixedIn) =>
        new(name, [], reason, []) { NotReceivingFixedIn = fixedIn };
}

/// <summary>値を受けるのに DB なしで観測できない部分（理由と観測を足す区切り）</summary>
/// <param name="Name">守りの名前</param>
/// <param name="Reason">観測できない理由</param>
/// <param name="FixedIn">観測を足す区切り</param>
public sealed record ValuePoolUnobserved(string Name, string Reason, string FixedIn);

/// <summary>1 つの部分の観測をまとめた行列の材料（観測した水準・例・相のあった水準・分類できなかった数）</summary>
public sealed class ValuePoolObservedMatrix
{
    private ValuePoolObservedMatrix(
        IReadOnlySet<string> observedEdges,
        IReadOnlyDictionary<string, string> examples,
        IReadOnlySet<string> facetEdges,
        IReadOnlySet<ValueFamily> families,
        int count,
        int unclassified
    )
    {
        ObservedEdges = observedEdges;
        Examples = examples;
        FacetEdges = facetEdges;
        Families = families;
        Count = count;
        Unclassified = unclassified;
    }

    /// <summary>観測した水準の Id</summary>
    public IReadOnlySet<string> ObservedEdges { get; }

    /// <summary>観測した水準の例（水準の Id → 最初に当たった観測の出どころ）</summary>
    public IReadOnlyDictionary<string, string> Examples { get; }

    /// <summary>列の相の要る水準のうち、その系統の観測に水準が読む相が 1 つ以上あったもの</summary>
    public IReadOnlySet<string> FacetEdges { get; }

    /// <summary>観測した値の系統</summary>
    public IReadOnlySet<ValueFamily> Families { get; }

    /// <summary>観測の数</summary>
    public int Count { get; }

    /// <summary>名簿のどの系統にも当たらない値を含む観測の数（値オブジェクトなどの型）</summary>
    public int Unclassified { get; }

    /// <summary>観測を分類して行列の材料を作る</summary>
    /// <param name="observations">観測</param>
    public static ValuePoolObservedMatrix From(IEnumerable<ValueObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var examples = new Dictionary<string, string>(StringComparer.Ordinal);
        var facets = new HashSet<string>(StringComparer.Ordinal);
        var families = new HashSet<ValueFamily>();
        var count = 0;
        var unclassified = 0;

        foreach (var observation in observations)
        {
            count++;

            if (ValueEdges.IsUnclassified(observation.Value))
            {
                unclassified++;
            }

            var valueFamilies = ValueEdges.FamiliesOf(observation.Value, observation.Column);
            families.UnionWith(valueFamilies);

            foreach (var id in ValueEdges.Classify(observation.Value, observation.Column))
            {
                observed.Add(id);
                examples.TryAdd(id, observation.Where);
            }

            foreach (
                var edge in ValueEdges.All.Where(edge =>
                    edge.NeedsColumn && valueFamilies.Contains(edge.Family)
                )
            )
            {
                if (ValueEdges.HasFacet(edge.Id, observation.Column))
                {
                    facets.Add(edge.Id);
                }
            }
        }

        return new(observed, examples, facets, families, count, unclassified);
    }
}

/// <summary>
/// 値のプールの受ける水準の宣言の照合：宣言の構造と、部分ごとの観測と宣言の行列（観測した水準と受けない行がちょうど一方）
/// </summary>
/// <remarks>
/// 受けない行の検査は作らない形の宣言の行の検査（<see cref="NotGeneratedGuard.ShapeProblems"/>）を共有する。受け持つ守りの照合の関数は
/// 呼び出し側が渡す（CLAUDE.md の照合と同じ関数。作らない形の宣言と同じく、この名前空間を BCL と Engines だけに保つため）。
/// </remarks>
public static class ValuePoolUseChecks
{
    /// <summary>宣言のファイルの名前（CLAUDE.md の照合が、行 Id の照合の本文から外す）</summary>
    public const string DeclarationFileName = "ValuePoolUseDeclarations.cs";

    /// <summary>ファイルの名前が宣言のファイルか</summary>
    /// <param name="fileName">ファイルの名前（パスを含まない）</param>
    public static bool IsDeclarationFileName(string fileName) =>
        string.Equals(fileName, DeclarationFileName, StringComparison.Ordinal);

    /// <summary>宣言の構造の問題をすべて並べる（無ければ空）</summary>
    /// <param name="subjects">宣言</param>
    public static IReadOnlyList<string> Problems(IReadOnlyList<ValuePoolSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var subject in subjects)
        {
            if (subject is null)
            {
                problems.Add("null の宣言がある");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(subject.Name) ? "(名前なし)" : subject.Name;

            if (string.IsNullOrWhiteSpace(subject.Name))
            {
                problems.Add($"{name}: 守りの名前が空");
            }
            else if (!names.Add(subject.Name))
            {
                problems.Add($"{name}: 同じ守りの宣言が 2 つある");
            }

            CheckReceiving(name, subject, problems);
            CheckNotDrawn(name, subject, problems);
        }

        return problems;
    }

    /// <summary>
    /// 部分の行列の問題（受ける系統の水準ごとに、観測したか受けない行があるかのちょうど一方。観測が宣言の外の系統を持つことも名指しする）
    /// </summary>
    /// <param name="subject">部分の宣言</param>
    /// <param name="matrix">部分の観測</param>
    public static IReadOnlyList<string> MatrixProblems(
        ValuePoolSubject subject,
        ValuePoolObservedMatrix matrix
    )
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(matrix);
        var problems = new List<string>();

        if (subject.Families.Count == 0)
        {
            problems.Add($"{subject.Name}: 値を受けない宣言の部分を観測している");
            return problems;
        }

        if (matrix.Count == 0)
        {
            problems.Add($"{subject.Name}: 観測が 1 つも無い（観測の関数が空振りしている）");
        }

        var declared = NotDrawnByEdge(subject);

        foreach (var edge in ValueEdges.All.Where(edge => subject.Families.Contains(edge.Family)))
        {
            var observed = matrix.ObservedEdges.Contains(edge.Id);
            var row = declared.GetValueOrDefault(edge.Id);

            if (observed && row is not null)
            {
                problems.Add(
                    $"{subject.Name}: 水準 {edge.Id} を観測した（例 {matrix.Examples[edge.Id]}）のに受けない行 {row.Id} がある（古い宣言。行から水準を外す）"
                );
            }
            else if (!observed && row is null)
            {
                problems.Add(
                    edge.NeedsColumn && !matrix.FacetEdges.Contains(edge.Id)
                        ? $"{subject.Name}: 水準 {edge.Id} は列の相が要るのに、相のある観測が 1 つも無い（相の無い観測。観測の関数で列の相を付けるか、受けない理由を宣言する）"
                        : $"{subject.Name}: 水準 {edge.Id} を観測せず、受けない行も無い（欠け。受けない理由を宣言するか、水準をエンジンへ足す）"
                );
            }
        }

        foreach (
            var family in matrix
                .Families.Where(family => !subject.Families.Contains(family))
                .OrderBy(family => family)
        )
        {
            problems.Add(
                $"{subject.Name}: 受ける系統に無い系統 {family} の値を観測した（受ける系統に足す）"
            );
        }

        foreach (var family in subject.Families.Where(family => !matrix.Families.Contains(family)))
        {
            problems.Add(
                $"{subject.Name}: 受ける系統 {family} の値を 1 つも観測していない（受ける系統から外す）"
            );
        }

        return problems;
    }

    /// <summary>部分の行列を書く（水準 × 観測した〔例〕／受けない〔Accepted・Pending と行の名前〕／欠け）</summary>
    /// <param name="subject">部分の宣言</param>
    /// <param name="matrix">部分の観測</param>
    public static string Render(ValuePoolSubject subject, ValuePoolObservedMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(matrix);
        var declared = NotDrawnByEdge(subject);
        var text = new StringBuilder();
        text.Append("### ").Append(subject.Name).Append('\n').Append('\n');
        text.Append("- 観測の数：")
            .Append(matrix.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("（名簿の系統に当たらない値を含む観測 ")
            .Append(matrix.Unclassified.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('）')
            .Append('\n');
        text.Append("- 受ける系統：").Append(string.Join("・", subject.Families)).Append('\n');
        text.Append("- 観測した系統：")
            .Append(string.Join("・", matrix.Families.Order()))
            .Append('\n')
            .Append('\n');
        text.Append("| 水準 | 状態 | 例・行 |\n|---|---|---|\n");

        foreach (var edge in ValueEdges.All.Where(edge => subject.Families.Contains(edge.Family)))
        {
            var observed = matrix.ObservedEdges.Contains(edge.Id);
            var row = declared.GetValueOrDefault(edge.Id);
            var (state, detail) = (observed, row) switch
            {
                (true, null) => ("観測", matrix.Examples[edge.Id]),
                (true, { } shape) => ("観測（古い宣言）", shape.Id),
                (false, { Kind: KnownKind.Pending } shape) => (
                    "受けない（Pending）",
                    $"{shape.Id}（{shape.FixedIn}）"
                ),
                (false, { } shape) => ("受けない（Accepted）", shape.Id),
                _ => (
                    edge.NeedsColumn && !matrix.FacetEdges.Contains(edge.Id)
                        ? "相の無い観測"
                        : "欠け",
                    ""
                ),
            };
            text.Append("| `")
                .Append(edge.Id)
                .Append("` | ")
                .Append(state)
                .Append(" | ")
                .Append(detail.Replace("|", "\\|", StringComparison.Ordinal))
                .Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// 観測の網羅の問題：値を受ける部分はどれも、QuickER.Tests で観測する・Integration で観測する・観測できない理由を持つ、のちょうど 1 つにある
    /// （どの一覧の名前も値を受ける部分の名前）
    /// </summary>
    /// <param name="subjects">宣言</param>
    /// <param name="unitObserved">QuickER.Tests で観測する部分</param>
    /// <param name="integrationObserved">Integration で観測する部分</param>
    /// <param name="unobserved">観測できない部分</param>
    public static IReadOnlyList<string> ObservationCoverageProblems(
        IReadOnlyList<ValuePoolSubject> subjects,
        IReadOnlyList<string> unitObserved,
        IReadOnlyList<string> integrationObserved,
        IReadOnlyList<ValuePoolUnobserved> unobserved
    )
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(unitObserved);
        ArgumentNullException.ThrowIfNull(integrationObserved);
        ArgumentNullException.ThrowIfNull(unobserved);
        var problems = new List<string>();
        var receiving = subjects
            .Where(subject => subject.Families.Count > 0)
            .Select(subject => subject.Name)
            .ToHashSet(StringComparer.Ordinal);
        var listed = unitObserved
            .Select(name => (Name: name, List: "QuickER.Tests で観測する"))
            .Concat(
                integrationObserved.Select(name => (Name: name, List: "Integration で観測する"))
            )
            .Concat(unobserved.Select(entry => (entry.Name, List: "観測できない")))
            .ToList();

        foreach (var group in listed.GroupBy(entry => entry.Name, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                problems.Add(
                    $"{group.Key}: 2 つ以上の一覧にある（{string.Join("・", group.Select(entry => entry.List))}）"
                );
            }

            if (!receiving.Contains(group.Key))
            {
                problems.Add(
                    $"{group.Key}: 値を受ける部分でないのに{group.First().List}一覧にある"
                );
            }
        }

        problems.AddRange(
            receiving
                .Where(name => listed.All(entry => entry.Name != name))
                .Order(StringComparer.Ordinal)
                .Select(name =>
                    $"{name}: 値を受けるのに、どのアセンブリでも観測せず、観測できない理由も無い"
                )
        );

        foreach (var entry in unobserved)
        {
            if (
                string.IsNullOrWhiteSpace(entry.Reason)
                || FactorInventoryGuard.IsVagueReason(entry.Reason.Trim())
            )
            {
                problems.Add($"{entry.Name}: 観測できない理由が具体的でない（{entry.Reason}）");
            }

            if (string.IsNullOrWhiteSpace(entry.FixedIn))
            {
                problems.Add($"{entry.Name}: 観測を足す区切りが空");
            }
        }

        return problems;
    }

    /// <summary>受け持つ守りの照合の問題（<see cref="NotGenerated.Unguarded"/> の行は照合しない）</summary>
    /// <param name="subjects">宣言</param>
    /// <param name="guardProblems">受け持つ守りの本文 → 照合の問題（CLAUDE.md の照合と同じ関数で作る）</param>
    public static IReadOnlyList<string> ReferenceProblems(
        IReadOnlyList<ValuePoolSubject> subjects,
        Func<string, IReadOnlyList<string>> guardProblems
    )
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(guardProblems);

        return
        [
            .. subjects
                .Where(subject => subject?.NotDrawn is not null)
                .SelectMany(subject =>
                    subject
                        .NotDrawn.Where(row =>
                            row?.Shape is not null
                            && !string.IsNullOrWhiteSpace(row.Shape.Guard)
                            && row.Shape.Guard != NotGenerated.Unguarded
                        )
                        .SelectMany(row =>
                            guardProblems(row.Shape.Guard)
                                .Select(problem => $"{subject.Name}/{row.Shape.Id}: {problem}")
                        )
                ),
        ];
    }

    /// <summary>受けない水準の Id → 行（重複は先の行）</summary>
    private static Dictionary<string, NotGeneratedShape> NotDrawnByEdge(ValuePoolSubject subject)
    {
        var map = new Dictionary<string, NotGeneratedShape>(StringComparer.Ordinal);

        foreach (var row in subject.NotDrawn ?? [])
        {
            if (row?.Edges is null || row.Shape is null)
            {
                continue;
            }

            foreach (var edge in row.Edges)
            {
                map.TryAdd(edge, row.Shape);
            }
        }

        return map;
    }

    private static void CheckReceiving(string name, ValuePoolSubject subject, List<string> problems)
    {
        var families = subject.Families ?? [];
        var receives = families.Count > 0;
        var hasReason = !string.IsNullOrWhiteSpace(subject.NotReceivingReason);

        if (receives == hasReason)
        {
            problems.Add(
                receives
                    ? $"{name}: 受ける系統と受けない理由の両方がある"
                    : $"{name}: 受ける系統も受けない理由も無い"
            );
        }

        if (hasReason && FactorInventoryGuard.IsVagueReason(subject.NotReceivingReason!.Trim()))
        {
            problems.Add($"{name}: 受けない理由が具体的でない（{subject.NotReceivingReason}）");
        }

        if (subject.NotReceivingFixedIn is not null)
        {
            if (string.IsNullOrWhiteSpace(subject.NotReceivingFixedIn))
            {
                problems.Add($"{name}: 棚卸しし直す区切りが空");
            }

            if (receives)
            {
                problems.Add($"{name}: 値を受ける部分が棚卸しし直す区切りを持つ");
            }
        }

        if (families.Distinct().Count() != families.Count)
        {
            problems.Add($"{name}: 受ける系統が重複している");
        }

        if (!receives && (subject.NotDrawn?.Count ?? 0) > 0)
        {
            problems.Add($"{name}: 値を受けない部分が受けない水準の行を持つ");
        }
    }

    private static void CheckNotDrawn(string name, ValuePoolSubject subject, List<string> problems)
    {
        var rows = subject.NotDrawn ?? [];
        problems.AddRange(
            NotGeneratedGuard.ShapeProblems(name, [.. rows.Select(row => row?.Shape)])
        );
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var rowName = $"{name}/{row.Shape?.Id}";

            if (row.Edges is null || row.Edges.Count == 0)
            {
                problems.Add($"{rowName}: 受けない水準が無い");
                continue;
            }

            foreach (var id in row.Edges)
            {
                var edge = ValueEdges.Find(id);

                if (edge is null)
                {
                    problems.Add($"{rowName}: 名簿に無い水準 {id}");
                }
                else if (!subject.Families.Contains(edge.Family))
                {
                    problems.Add($"{rowName}: 水準 {id} の系統 {edge.Family} が受ける系統に無い");
                }

                if (!seen.Add(id))
                {
                    problems.Add($"{rowName}: 水準 {id} が 2 つの行にある");
                }
            }
        }
    }
}
