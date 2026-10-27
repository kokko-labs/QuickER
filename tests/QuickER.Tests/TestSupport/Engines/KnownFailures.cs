using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>エンジンが観測した失敗 1 件</summary>
/// <param name="Scope">実行の範囲（シナリオの Id・方言・入口など。序数で照合する）</param>
/// <param name="Signature">エンジンが正規化した失敗の署名</param>
/// <param name="Detail">失敗の詳細（理由の断片の照合と報告に使う）</param>
/// <param name="Seed">乱数の種（決定的なシナリオなら null）</param>
public sealed record EngineFailure(string Scope, string Signature, string Detail, int? Seed = null);

/// <summary>既知の失敗の項目が、観測した失敗に当たるかの決め方</summary>
public enum KnownMatch
{
    /// <summary>署名の全文が序数で一致する</summary>
    ExactSignature,

    /// <summary>エンジンが渡す正規化（<see cref="KnownFailureSet"/> の family）を通した署名が一致する</summary>
    Family,

    /// <summary>項目の述語が真を返す</summary>
    Predicate,

    /// <summary>範囲だけが一致すればよい（署名は見ない）</summary>
    KeyOnly,
}

/// <summary>既知の失敗の項目の種類</summary>
public enum KnownKind
{
    /// <summary>直す区切りがある一時的な項目（<see cref="KnownEntry.FixedIn"/> が必須）</summary>
    Pending,

    /// <summary>受け入れた差（<see cref="KnownEntry.Reason"/> が必須・直す区切りは持たない）</summary>
    Accepted,
}

/// <summary>既知の失敗の一覧の 1 項目</summary>
/// <param name="Finding">指摘の番号</param>
/// <param name="Kind">一時的な項目か、受け入れた差か</param>
/// <param name="FixedIn">一覧から外す区切り（Pending のとき必須）</param>
/// <param name="Reason">受け入れた理由（Accepted のとき必須）</param>
/// <param name="Scope">当たる範囲（<see cref="EngineFailure.Scope"/> と序数で一致）</param>
/// <param name="Match">当たるかの決め方</param>
/// <param name="Signature">署名（ExactSignature・Family のとき必須）</param>
/// <param name="Predicate">述語（Predicate のとき必須）</param>
/// <param name="ReasonContains">失敗の詳細に含まれるべき理由の断片（無ければ見ない）</param>
/// <param name="RandomSeeds">乱数の種の失敗に当たる項目か（偽なら決定的なシナリオの失敗にだけ当たる）</param>
/// <param name="ReproducibleAtDefaultSeeds">
/// 既定の種の数で流せば必ず起きるか（乱数の項目だけが偽にできる。偽なら「起きない」を数えない）
/// </param>
public sealed record KnownEntry(
    string Finding,
    KnownKind Kind,
    string? FixedIn,
    string? Reason,
    string Scope,
    KnownMatch Match,
    string? Signature = null,
    Func<EngineFailure, bool>? Predicate = null,
    string? ReasonContains = null,
    bool RandomSeeds = false,
    bool ReproducibleAtDefaultSeeds = true
)
{
    /// <summary>報告に出す短い表記</summary>
    public string Describe()
    {
        var where = Kind == KnownKind.Pending ? $"直す区切り {FixedIn}" : $"受容: {Reason}";
        var signature = Signature is null ? "" : $" | {Signature}";
        var random = RandomSeeds ? "（乱数の種）" : "";
        return $"{Finding}（{where}）: {Scope}{random}{signature}";
    }
}

/// <summary>観測した失敗と、それに当たった項目</summary>
/// <param name="Failure">観測した失敗</param>
/// <param name="Entry">当たった項目</param>
public sealed record KnownMatchResult(EngineFailure Failure, KnownEntry Entry);

/// <summary>既知の失敗の一覧との照合の結果</summary>
/// <param name="Unknown">どの項目にも当たらない失敗（新しい失敗）</param>
/// <param name="NotObserved">一覧に載っているのに起きなかった項目</param>
/// <param name="ReasonMismatch">項目に当たったが、期待した理由の断片を含まない失敗</param>
/// <param name="Matched">既知として数えた失敗</param>
public sealed record KnownReport(
    IReadOnlyList<EngineFailure> Unknown,
    IReadOnlyList<KnownEntry> NotObserved,
    IReadOnlyList<KnownMatchResult> ReasonMismatch,
    IReadOnlyList<KnownMatchResult> Matched
)
{
    /// <summary>赤にする問題が無いか</summary>
    public bool IsClean =>
        Unknown.Count == 0 && NotObserved.Count == 0 && ReasonMismatch.Count == 0;

    /// <summary>問題があれば、件数と次の手を添えて 1 つの失敗にする（文面はここだけで組み立てる）</summary>
    /// <param name="title">何の照合か（エンジン・テストの名前）</param>
    /// <param name="report">新しい失敗を打ち切りと再現の手順つきで書く報告（無ければ失敗をそのまま並べる）</param>
    public void AssertClean(string title, EngineReport? report = null)
    {
        if (IsClean)
        {
            return;
        }

        var text = new StringBuilder();
        text.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{title}: 既知の失敗の一覧と一致しない（新しい失敗 {Unknown.Count} 件・起きなかった項目 {NotObserved.Count} 件・理由の食い違い {ReasonMismatch.Count} 件）"
            )
        );

        if (Unknown.Count > 0)
        {
            text.Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"新しい失敗 {Unknown.Count} 件: 直すか、直す区切りを決めて一覧に載せる"
                    )
                );
            text.Append('\n').Append(report?.Render(Unknown) ?? EngineReport.RenderPlain(Unknown));
        }

        if (NotObserved.Count > 0)
        {
            text.Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"起きなかった項目 {NotObserved.Count} 件: 直ったなら一覧から消す"
                    )
                );

            foreach (var entry in NotObserved)
            {
                text.Append("\n  ").Append(entry.Describe());
            }
        }

        if (ReasonMismatch.Count > 0)
        {
            text.Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"理由の食い違い {ReasonMismatch.Count} 件: 別の原因が混ざっていないか確かめ、直すか項目の理由を直す"
                    )
                );

            foreach (var mismatch in ReasonMismatch)
            {
                text.Append("\n  ")
                    .Append(mismatch.Entry.Describe())
                    .Append("\n    期待した理由: ")
                    .Append(mismatch.Entry.ReasonContains)
                    .Append("\n    実際: ")
                    .Append(mismatch.Failure.Detail);
            }
        }

        throw new XunitException(text.ToString());
    }
}

/// <summary>
/// 既知の失敗の一覧（厳格: 一覧に無い失敗・一覧にあるのに起きない項目・期待と違う理由の失敗は、いずれも赤）
/// </summary>
/// <remarks>
/// <para>
/// 項目は範囲（序数）と決め方（<see cref="KnownMatch"/>）で失敗に当たる。乱数の項目（<see cref="KnownEntry.RandomSeeds"/>）は
/// 種のある失敗にだけ、そうでない項目は種の無い失敗にだけ当たる。複数の項目に当たるときは一覧の先の項目を採る。
/// 鍵だけ当たって理由の断片を含まない失敗は「理由の食い違い」とし、その項目は起きたものとして数える（二重に赤にしない）。
/// </para>
/// <para>
/// 「起きなかった」の規則：決定的なシナリオの項目は常に数える。乱数の項目は、既定の種の数で必ず起きる項目
/// （<see cref="KnownEntry.ReproducibleAtDefaultSeeds"/>）を既定の種の数で流したとき（<see cref="SeedPlan.IsDefaultCount"/>）だけ数える
/// （種を絞ると、その形が出ないことがある）。
/// </para>
/// <para>
/// 一覧の整合は構築時に検査し、問題をすべて並べて <see cref="ArgumentException"/> にする。
/// </para>
/// </remarks>
public sealed class KnownFailureSet
{
    private readonly IReadOnlyList<KnownEntry> _entries;
    private readonly Func<string, string>? _family;

    /// <summary>一覧を作る（整合を検査する）</summary>
    /// <param name="engineId">エンジンの ID（問題の報告に使う）</param>
    /// <param name="entries">項目</param>
    /// <param name="family">Family の項目の照合に使う署名の正規化（エンジンが持つ）</param>
    public KnownFailureSet(
        string engineId,
        IReadOnlyList<KnownEntry> entries,
        Func<string, string>? family = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentNullException.ThrowIfNull(entries);

        var problems = Validate(entries, family is not null);

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"{engineId} の既知の失敗の一覧が整っていない:\n  " + string.Join("\n  ", problems),
                nameof(entries)
            );
        }

        EngineId = engineId;
        _entries = entries;
        _family = family;
    }

    /// <summary>エンジンの ID</summary>
    public string EngineId { get; }

    /// <summary>項目</summary>
    public IReadOnlyList<KnownEntry> Entries => _entries;

    /// <summary>すべての範囲を受け持つものとして照合する</summary>
    /// <param name="observed">観測した失敗</param>
    /// <param name="plan">流した種</param>
    public KnownReport Classify(IEnumerable<EngineFailure> observed, SeedPlan plan) =>
        Classify(observed, plan, _ => true);

    /// <summary>このテストが受け持つ範囲の項目だけで照合する</summary>
    /// <param name="observed">観測した失敗（受け持つ範囲の外の失敗も新しい失敗として数える）</param>
    /// <param name="plan">流した種</param>
    /// <param name="ownsScope">このテストが受け持つ範囲か（受け持たない項目は照合も「起きなかった」の判定もしない）</param>
    public KnownReport Classify(
        IEnumerable<EngineFailure> observed,
        SeedPlan plan,
        Func<string, bool> ownsScope
    )
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownsScope);

        var owned = _entries.Where(entry => ownsScope(entry.Scope)).ToList();
        var seen = new HashSet<KnownEntry>(ReferenceEqualityComparer.Instance);
        var unknown = new List<EngineFailure>();
        var mismatch = new List<KnownMatchResult>();
        var matched = new List<KnownMatchResult>();

        foreach (var failure in observed)
        {
            var candidates = owned.Where(entry => KeyMatches(entry, failure)).ToList();

            if (candidates.Count == 0)
            {
                unknown.Add(failure);
                continue;
            }

            var hit = candidates.FirstOrDefault(entry => ReasonMatches(entry, failure));

            if (hit is not null)
            {
                matched.Add(new KnownMatchResult(failure, hit));
                seen.Add(hit);
                continue;
            }

            mismatch.Add(new KnownMatchResult(failure, candidates[0]));
            seen.Add(candidates[0]);
        }

        var notObserved = owned
            .Where(entry => !seen.Contains(entry) && ChecksNotObserved(entry, plan))
            .ToList();
        return new KnownReport(unknown, notObserved, mismatch, matched);
    }

    /// <summary>「起きなかった」をこの実行で数える項目か</summary>
    private static bool ChecksNotObserved(KnownEntry entry, SeedPlan plan) =>
        !entry.RandomSeeds || (entry.ReproducibleAtDefaultSeeds && plan.IsDefaultCount);

    /// <summary>項目の鍵（範囲・種の有無・署名）が失敗に当たるか</summary>
    private bool KeyMatches(KnownEntry entry, EngineFailure failure)
    {
        if (
            !string.Equals(entry.Scope, failure.Scope, StringComparison.Ordinal)
            || entry.RandomSeeds != failure.Seed.HasValue
        )
        {
            return false;
        }

        return entry.Match switch
        {
            KnownMatch.ExactSignature => string.Equals(
                entry.Signature,
                failure.Signature,
                StringComparison.Ordinal
            ),
            KnownMatch.Family => string.Equals(
                entry.Signature,
                _family!(failure.Signature),
                StringComparison.Ordinal
            ),
            KnownMatch.Predicate => entry.Predicate!(failure),
            KnownMatch.KeyOnly => true,
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Match, null),
        };
    }

    /// <summary>失敗が項目の期待した理由で起きたか</summary>
    private static bool ReasonMatches(KnownEntry entry, EngineFailure failure) =>
        entry.ReasonContains is null
        || failure.Detail.Contains(entry.ReasonContains, StringComparison.Ordinal);

    private static List<string> Validate(IReadOnlyList<KnownEntry> entries, bool hasFamily)
    {
        var problems = new List<string>();
        var keys = new HashSet<(string, bool, KnownMatch, string?)>();

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                problems.Add("null の項目がある");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Finding) ? "(指摘なし)" : entry.Finding;

            if (string.IsNullOrWhiteSpace(entry.Finding))
            {
                problems.Add($"{name}: 指摘の番号が空");
            }

            if (string.IsNullOrWhiteSpace(entry.Scope))
            {
                problems.Add($"{name}: 範囲が空");
            }

            switch (entry.Kind)
            {
                case KnownKind.Pending when string.IsNullOrWhiteSpace(entry.FixedIn):
                    problems.Add($"{name}: Pending の項目に直す区切り（FixedIn）が無い");
                    break;
                case KnownKind.Accepted when string.IsNullOrWhiteSpace(entry.Reason):
                    problems.Add($"{name}: Accepted の項目に受け入れた理由（Reason）が無い");
                    break;
                case KnownKind.Accepted when entry.FixedIn is not null:
                    problems.Add($"{name}: Accepted の項目は直す区切り（FixedIn）を持たない");
                    break;
                case KnownKind.Pending:
                case KnownKind.Accepted:
                    break;
                default:
                    problems.Add($"{name}: 知らない種類 {entry.Kind}");
                    break;
            }

            switch (entry.Match)
            {
                case KnownMatch.ExactSignature:
                case KnownMatch.Family:
                    if (string.IsNullOrEmpty(entry.Signature))
                    {
                        problems.Add($"{name}: {entry.Match} の項目に署名が無い");
                    }

                    if (entry.Predicate is not null)
                    {
                        problems.Add($"{name}: {entry.Match} の項目は述語を持たない");
                    }

                    if (entry.Match == KnownMatch.Family && !hasFamily)
                    {
                        problems.Add(
                            $"{name}: Family の項目があるのに署名の正規化（family）が渡されていない"
                        );
                    }

                    break;
                case KnownMatch.Predicate:
                    if (entry.Predicate is null)
                    {
                        problems.Add($"{name}: Predicate の項目に述語が無い");
                    }

                    if (entry.Signature is not null)
                    {
                        problems.Add($"{name}: Predicate の項目は署名を持たない");
                    }

                    break;
                case KnownMatch.KeyOnly:
                    if (entry.Signature is not null || entry.Predicate is not null)
                    {
                        problems.Add($"{name}: KeyOnly の項目は署名も述語も持たない");
                    }

                    break;
                default:
                    problems.Add($"{name}: 知らない決め方 {entry.Match}");
                    break;
            }

            if (!entry.ReproducibleAtDefaultSeeds && !entry.RandomSeeds)
            {
                problems.Add(
                    $"{name}: 決定的なシナリオの項目は ReproducibleAtDefaultSeeds を偽にできない（必ず確かめる）"
                );
            }

            if (
                entry.Match != KnownMatch.Predicate
                && !keys.Add((entry.Scope, entry.RandomSeeds, entry.Match, entry.Signature))
            )
            {
                problems.Add($"{name}: 同じ鍵の項目が重複している（{entry.Describe()}）");
            }
        }

        return problems;
    }
}
