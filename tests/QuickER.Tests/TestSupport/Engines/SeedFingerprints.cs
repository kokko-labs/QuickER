using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>種ごとの生成器の入力の要約から、短い指紋を作る</summary>
/// <remarks>
/// 指紋は検証ではなく変化の検出に使う（乱数の引き方の順序が 1 回でも変わったことを知らせる）。
/// 先頭 16 桁（64 ビット）なので衝突は実質ないが、一致を「同じ入力である証明」とは扱わない。
/// </remarks>
public static class SeedFingerprint
{
    /// <summary>指紋の桁数（16 進）</summary>
    public const int Length = 16;

    /// <summary>要約の指紋（改行を LF へそろえた UTF-8 の SHA-256 の先頭 16 桁・小文字の 16 進）</summary>
    /// <param name="summary">生成器が作った入力の要約</param>
    public static string Of(string summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var normalized = summary
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash)[..Length];
    }

    /// <summary>種ごとの要約の指紋（要約は <see cref="SeedSummary.UnderInvariantCulture"/> の中で作る）</summary>
    /// <param name="seeds">種</param>
    /// <param name="summary">種から要約を作る処理</param>
    public static Dictionary<int, string> Compute(IEnumerable<int> seeds, Func<int, string> summary)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(summary);
        return seeds.ToDictionary(
            seed => seed,
            seed => Of(SeedSummary.UnderInvariantCulture(() => summary(seed)))
        );
    }
}

/// <summary>指紋の照合でどこまでを見るか</summary>
public enum FingerprintCoverage
{
    /// <summary>記録と計算の種がちょうど一致し、すべて同じ指紋（普段の既定の種の数）</summary>
    All,

    /// <summary>記録にある種はすべて計算されて同じ指紋。記録に無い種は見ない（夜間の既定の種の数＝普段の種を含む）</summary>
    RecordedSeeds,

    /// <summary>計算した種のうち記録にあるものだけを照合する（単独の種・個別の数の指定）</summary>
    Intersection,
}

/// <summary>指紋の照合の差</summary>
/// <param name="Changed">記録と指紋が違う種</param>
/// <param name="NotRecorded">計算したが記録に無い種</param>
/// <param name="NotComputed">記録にあるが計算していない種</param>
public sealed record FingerprintDiff(
    IReadOnlyList<int> Changed,
    IReadOnlyList<int> NotRecorded,
    IReadOnlyList<int> NotComputed
)
{
    /// <summary>差が無いか</summary>
    public bool IsEmpty => Changed.Count == 0 && NotRecorded.Count == 0 && NotComputed.Count == 0;
}

/// <summary>
/// エンジンごとの種の指紋の記録（<c>engine-fingerprints.json</c>）
/// </summary>
/// <remarks>
/// <para>
/// 記録はエンジン ID の序数順・種の数値順・インデント・LF・BOM なしの UTF-8 で書く（更新の差分を目で読めるように）。
/// 記録の更新は承認であって検証ではない（差分を見て、乱数の引き方を意図して変えたことを確かめてから更新する）。
/// </para>
/// <para>
/// 照合の範囲は流した種で決まる（<see cref="CoverageFor"/>）：普段の既定の種の数なら全数、夜間の既定の種の数なら記録にある種の全部、
/// 単独の種・個別の数の指定なら計算した種と記録の重なりだけ。記録は普段の既定の種の数で流したときだけ更新できる
/// （夜間や一部の種で更新すると、次の普段の実行で記録と計算の種がずれる）。
/// </para>
/// </remarks>
public sealed class FingerprintFile
{
    private readonly SortedDictionary<string, SortedDictionary<int, string>> _engines;

    private FingerprintFile(SortedDictionary<string, SortedDictionary<int, string>> engines) =>
        _engines = engines;

    /// <summary>空の記録</summary>
    public static FingerprintFile Empty() => new(new(StringComparer.Ordinal));

    /// <summary>記録したエンジンの ID</summary>
    public IReadOnlyCollection<string> Engines => _engines.Keys;

    /// <summary>エンジンの記録（無ければ空）</summary>
    /// <param name="engineId">エンジンの ID</param>
    public IReadOnlyDictionary<int, string> For(string engineId) =>
        _engines.TryGetValue(engineId, out var seeds) ? seeds : new Dictionary<int, string>();

    /// <summary>流した種に合う照合の範囲</summary>
    /// <param name="plan">流した種</param>
    public static FingerprintCoverage CoverageFor(SeedPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsDefaultCount)
        {
            return FingerprintCoverage.Intersection;
        }

        return plan.Profile == EngineProfile.Regular
            ? FingerprintCoverage.All
            : FingerprintCoverage.RecordedSeeds;
    }

    /// <summary>記録を読む（ファイルが無ければ空）</summary>
    /// <param name="path">ファイルのパス</param>
    public static FingerprintFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return Empty();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var engines = new SortedDictionary<string, SortedDictionary<int, string>>(
            StringComparer.Ordinal
        );

        foreach (var engine in document.RootElement.GetProperty("engines").EnumerateObject())
        {
            var seeds = new SortedDictionary<int, string>();

            foreach (var seed in engine.Value.EnumerateObject())
            {
                seeds.Add(
                    int.Parse(seed.Name, NumberStyles.None, CultureInfo.InvariantCulture),
                    seed.Value.GetString()
                        ?? throw new InvalidDataException(
                            $"{path}: {engine.Name} の種 {seed.Name} の指紋が null"
                        )
                );
            }

            engines.Add(engine.Name, seeds);
        }

        return new FingerprintFile(engines);
    }

    /// <summary>エンジンの記録を計算した指紋で置き換えた記録</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="actual">計算した指紋（種 → 指紋）</param>
    public FingerprintFile WithEngine(string engineId, IReadOnlyDictionary<int, string> actual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentNullException.ThrowIfNull(actual);

        var engines = new SortedDictionary<string, SortedDictionary<int, string>>(
            StringComparer.Ordinal
        );

        foreach (var (id, seeds) in _engines)
        {
            engines.Add(id, new SortedDictionary<int, string>(seeds));
        }

        engines[engineId] = new SortedDictionary<int, string>(
            actual.ToDictionary(pair => pair.Key, pair => pair.Value)
        );
        return new FingerprintFile(engines);
    }

    /// <summary>記録と計算した指紋を照合する</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="actual">計算した指紋（種 → 指紋）</param>
    /// <param name="coverage">照合の範囲</param>
    public FingerprintDiff Compare(
        string engineId,
        IReadOnlyDictionary<int, string> actual,
        FingerprintCoverage coverage
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentNullException.ThrowIfNull(actual);

        var recorded = For(engineId);
        var changed = actual
            .Where(pair =>
                recorded.TryGetValue(pair.Key, out var expected)
                && !string.Equals(expected, pair.Value, StringComparison.Ordinal)
            )
            .Select(pair => pair.Key)
            .Order()
            .ToList();
        List<int> notRecorded =
            coverage == FingerprintCoverage.All
                ? actual.Keys.Where(seed => !recorded.ContainsKey(seed)).Order().ToList()
                : [];
        List<int> notComputed =
            coverage == FingerprintCoverage.Intersection
                ? []
                : recorded.Keys.Where(seed => !actual.ContainsKey(seed)).Order().ToList();
        return new FingerprintDiff(changed, notRecorded, notComputed);
    }

    /// <summary>記録を書く（エンジン ID の序数順・種の数値順・LF・BOM なし）</summary>
    /// <param name="path">ファイルのパス</param>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, Serialize(), new UTF8Encoding(false));
    }

    /// <summary>記録の文字列（<see cref="Save"/> が書く内容）</summary>
    public string Serialize()
    {
        using var stream = new MemoryStream();

        using (
            var writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions { Indented = true, NewLine = "\n" }
            )
        )
        {
            writer.WriteStartObject();
            writer.WriteStartObject("engines");

            foreach (var (engineId, seeds) in _engines)
            {
                writer.WriteStartObject(engineId);

                foreach (var (seed, fingerprint) in seeds)
                {
                    writer.WriteString(seed.ToString(CultureInfo.InvariantCulture), fingerprint);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>
    /// 計算した指紋を記録と照合する（regenerate なら記録を書き直す）
    /// </summary>
    /// <param name="path">記録のファイルのパス</param>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="actual">計算した指紋（種 → 指紋）</param>
    /// <param name="plan">流した種（照合の範囲と、書き直してよいかを決める）</param>
    /// <param name="regenerate">記録を書き直すか（承認であって検証ではない）</param>
    public static void Verify(
        string path,
        string engineId,
        IReadOnlyDictionary<int, string> actual,
        SeedPlan plan,
        bool regenerate
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        var file = Load(path);

        if (regenerate)
        {
            if (plan.Profile != EngineProfile.Regular || !plan.IsDefaultCount)
            {
                throw new InvalidOperationException(
                    $"{engineId} の指紋は普段の既定の種の数で流したときだけ書き直せる（いまは {plan.Profile}・既定の数={plan.IsDefaultCount}）"
                );
            }

            file.WithEngine(engineId, actual).Save(path);
            return;
        }

        var diff = file.Compare(engineId, actual, CoverageFor(plan));

        if (diff.IsEmpty)
        {
            return;
        }

        throw new XunitException(
            $"{engineId} の種の指紋が記録（{path}）と一致しない（生成器の乱数の引き方・入力が変わった）。"
                + "意図した変更なら差分を見て記録を書き直す:"
                + Line("指紋が変わった種", diff.Changed)
                + Line("記録に無い種", diff.NotRecorded)
                + Line("計算していない種", diff.NotComputed)
        );
    }

    private static string Line(string label, IReadOnlyList<int> seeds) =>
        seeds.Count == 0
            ? ""
            : string.Create(
                CultureInfo.InvariantCulture,
                $"\n  {label} {seeds.Count} 件: {string.Join(", ", seeds.Take(20))}{(seeds.Count > 20 ? " …" : "")}"
            );
}
