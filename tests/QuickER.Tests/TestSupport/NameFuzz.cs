using System.Globalization;

namespace QuickER.Tests.TestSupport;

/// <summary>名前の総当たりで流す名前の種類（どの要素の名前か）</summary>
public enum NameKind
{
    /// <summary>テーブル名</summary>
    Table,

    /// <summary>同じテーブルの中の列名</summary>
    Column,

    /// <summary>制約名（主キー・一意制約・外部キーで 1 つの名前空間）</summary>
    Constraint,

    /// <summary>同じテーブルの中の名前付きクエリの名前</summary>
    Query,

    /// <summary>同じクエリの中のパラメータ名</summary>
    Parameter,

    /// <summary>同じ射影の中のフィールド名</summary>
    ProjectionField,
}

/// <summary>名前の組がどう違うか（既知の失敗の一覧のキーに使う分類）</summary>
public enum NameDifference
{
    /// <summary>まったく同じ文字列</summary>
    Identical,

    /// <summary>前後の空白だけが違う</summary>
    Whitespace,

    /// <summary>大文字小文字だけが違う（小文字化でも大文字化でも同じになる）</summary>
    Case,

    /// <summary>前後の空白と大文字小文字が違う</summary>
    WhitespaceAndCase,

    /// <summary>
    /// 大文字化（OrdinalIgnoreCase）では同じだが小文字化（ToLowerInvariant）では違う（例: 点の無い i と I）
    /// </summary>
    CaseUpperFoldOnly,

    /// <summary>
    /// 小文字化（ToLowerInvariant）では同じだが大文字化（OrdinalIgnoreCase）では違う（例: ケルビン記号と K）
    /// </summary>
    CaseLowerFoldOnly,

    /// <summary>別の名前</summary>
    Distinct,
}

/// <summary>総当たりで流す名前の組</summary>
/// <param name="First">先に入れる名前</param>
/// <param name="Second">後から入れる名前</param>
public sealed record NamePair(string First, string Second)
{
    /// <summary>組の違いの分類</summary>
    public NameDifference Difference => NameIdentityRule.Classify(First, Second);

    /// <summary>失敗の報告に出す形（空白・非 ASCII を見えるように）</summary>
    public override string ToString() =>
        $"[{NameFuzz.Describe(First)}] / [{NameFuzz.Describe(Second)}] ({Difference})";
}

/// <summary>
/// 名前の同一性の規則（総当たりの期待値の正本・テストの中の 1 つの関数）
/// </summary>
/// <remarks>
/// <para>
/// 図に保存する名前（テーブル名・列名・制約名・クエリ名・パラメータ名・射影のフィールド名）はどれも
/// 「前後の空白を除いて <see cref="StringComparison.OrdinalIgnoreCase"/> で一致すれば同じ名前」。
/// </para>
/// <para>
/// 保存する形は「前後の空白を除いた形」（綴りの大文字小文字は書き換えない）。入口ごとの表明は
/// <see cref="StoredForm"/> と比べる。
/// </para>
/// </remarks>
public static class NameIdentityRule
{
    /// <summary>2 つの名前を同じ名前として扱うべきか</summary>
    /// <param name="kind">名前の種類（いまはどの種類も同じ規則）</param>
    /// <param name="first">先の名前</param>
    /// <param name="second">後の名前</param>
    public static bool AreSame(NameKind kind, string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>名前を図へ保存したときの形（前後の空白を除く・綴りはそのまま）</summary>
    public static string StoredForm(string name) => name.Trim();

    /// <summary>名前の組の違いを分類する</summary>
    public static NameDifference Classify(string first, string second)
    {
        if (first == second)
        {
            return NameDifference.Identical;
        }

        var trimmedFirst = first.Trim();
        var trimmedSecond = second.Trim();
        var whitespace = trimmedFirst != first || trimmedSecond != second;

        if (trimmedFirst == trimmedSecond)
        {
            return NameDifference.Whitespace;
        }

        var upperFold = string.Equals(
            trimmedFirst,
            trimmedSecond,
            StringComparison.OrdinalIgnoreCase
        );
        var lowerFold = trimmedFirst.ToLowerInvariant() == trimmedSecond.ToLowerInvariant();

        if (upperFold && lowerFold)
        {
            return whitespace ? NameDifference.WhitespaceAndCase : NameDifference.Case;
        }

        if (upperFold)
        {
            return NameDifference.CaseUpperFoldOnly;
        }

        return lowerFold ? NameDifference.CaseLowerFoldOnly : NameDifference.Distinct;
    }
}

/// <summary>
/// 名前の総当たりの生成器。前後の空白・大文字小文字だけの違い・ドット・引用符・サロゲート対・SQL と C# の予約語・
/// 生成コードが使う型名・数字始まり・日本語を含む名前を、シードから決定的に作る。
/// </summary>
/// <remarks>
/// ソースに制御文字・行区切りを書かない規約（<c>SourceInvisibleCharacterGuardTests</c>）のため、非 ASCII は
/// すべてエスケープ表記で書く。シードの数は環境変数 <c>QUICKER_FUZZ_SEEDS</c> で増やせる（既定 2）。
/// </remarks>
public static class NameFuzz
{
    /// <summary>基底の名前（それぞれ別の名前）</summary>
    public static readonly IReadOnlyList<string> BaseNames =
    [
        "Orders",
        "order",
        "Group",
        "select",
        "Query",
        "Task",
        "class",
        "1st",
        "a.b",
        "q\"x",
        "q'x",
        "q]x",
        "q`x",
        "\u8868\uD83D\uDE00",
        "\u00C4pfel",
        "\u6CE8\u6587\u660E\u7D30",
        "\u0131d",
        "\u212Aey",
    ];

    /// <summary>
    /// どのシードでも必ず流す組（大文字化と小文字化で答えが割れる組・別の基底の名前どうし）
    /// </summary>
    public static readonly IReadOnlyList<NamePair> FixedPairs =
    [
        new("\u0131d", "ID"),
        new("\u0131d", "Id"),
        new("\u212Aey", "key"),
        new("\u212Aey", "KEY"),
        new("Orders", "order"),
        new("\u00C4pfel", "\u00E4PFEL"),
        new("Orders", " orders\u3000"),
        new("Orders", "Orders"),
        new("\u3000Orders", "\u3000Orders"),
        new("\u00C4pfel ", "\u00E4PFEL"),
    ];

    /// <summary>既定のシードの数</summary>
    public const int DefaultSeedCount = 2;

    /// <summary>総当たりで回すシード（<c>QUICKER_FUZZ_SEEDS</c> で数を増やせる）</summary>
    public static IReadOnlyList<int> Seeds
    {
        get
        {
            var text = Environment.GetEnvironmentVariable("QUICKER_FUZZ_SEEDS");
            var count =
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed > DefaultSeedCount
                    ? parsed
                    : DefaultSeedCount;

            return Enumerable.Range(1, count).ToList();
        }
    }

    /// <summary>基底の名前の変形（空白・大文字小文字）</summary>
    public static IReadOnlyList<string> Variants(string baseName) =>
        [
            baseName,
            " " + baseName,
            baseName + " ",
            "\u3000" + baseName,
            baseName + "\u00A0",
            baseName.ToUpperInvariant(),
            baseName.ToLowerInvariant(),
            SwapFirstLetterCase(baseName),
        ];

    /// <summary>シードから名前の組を作る（固定の組＋同じ基底の変形どうし＋別の基底どうし）</summary>
    /// <param name="seed">シード</param>
    /// <param name="count">ランダムに作る組の数</param>
    public static IReadOnlyList<NamePair> Pairs(int seed, int count)
    {
        var random = new Random(seed);
        var pairs = new List<NamePair>(FixedPairs);

        for (var i = 0; i < count; i++)
        {
            var first = Variants(Pick(random, BaseNames));
            var sameBase = random.Next(4) != 0;
            var second = sameBase ? first : Variants(Pick(random, BaseNames));
            pairs.Add(new NamePair(Pick(random, first), Pick(random, second)));
        }

        return pairs;
    }

    /// <summary>シードから名前を作る（基底の名前のどれかの変形）</summary>
    public static string Name(Random random) => Pick(random, Variants(Pick(random, BaseNames)));

    /// <summary>失敗の報告に出す形（空白と非 ASCII を \uXXXX で見せる）</summary>
    public static string Describe(string name) =>
        string.Concat(name.Select(c => c is >= '!' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));

    private static T Pick<T>(Random random, IReadOnlyList<T> items) =>
        items[random.Next(items.Count)];

    private static string SwapFirstLetterCase(string name)
    {
        var index = name.ToList().FindIndex(char.IsLetter);

        if (index < 0)
        {
            return name;
        }

        var c = name[index];
        var swapped = char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);

        return name[..index] + swapped + name[(index + 1)..];
    }
}

/// <summary>名前の組を 1 つの入口へ流した結果</summary>
/// <param name="SecondAccepted">後から入れた名前を別の要素として受け付けたか</param>
/// <param name="Violations">受け付けた後に破れていた性質（例 <c>lookup-wrong-target</c>）</param>
/// <param name="Detail">失敗の報告に添える入口の応答</param>
public sealed record PairOutcome(
    bool SecondAccepted,
    IReadOnlyList<string> Violations,
    string Detail
)
{
    /// <summary>性質の破れなしの結果</summary>
    public static PairOutcome Of(bool secondAccepted, string detail) =>
        new(secondAccepted, [], detail);
}

/// <summary>
/// 名前の組を入口へ流し、<see cref="NameIdentityRule"/> の答えと照合して失敗を <see cref="FuzzFailureLedger"/> へ記録する
/// </summary>
public static class NameFuzzRunner
{
    /// <summary>ランダムに作る組の数（シードごと）</summary>
    public const int RandomPairsPerSeed = 20;

    /// <summary>
    /// 取り込んだ名前を、書いた名前と照合する（並びは比べない）。書いた名前を空白を除いた形で全部取り込んだら受け付けた、
    /// 取り込んだ名前が空白を除いた形でなければ保存形の破れ（stored-form-differs）とする
    /// </summary>
    /// <param name="imported">取り込んだ名前</param>
    /// <param name="written">書いた名前</param>
    /// <param name="detail">報告に添える文</param>
    public static (bool Accepted, PairOutcome Outcome) JudgeImported(
        IReadOnlyList<string> imported,
        IReadOnlyList<string> written,
        string detail
    )
    {
        var accepted = imported
            .Select(NameIdentityRule.StoredForm)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                written.Select(NameIdentityRule.StoredForm).Order(StringComparer.Ordinal)
            );
        IReadOnlyList<string> violations = imported.Any(name =>
            name != NameIdentityRule.StoredForm(name)
        )
            ? ["stored-form-differs"]
            : [];

        return (accepted, new PairOutcome(accepted, violations, detail));
    }

    /// <summary>入口を総当たりで流す</summary>
    /// <param name="ledger">失敗の記録先</param>
    /// <param name="entrance">入口の名前（既知の失敗の一覧のキー）</param>
    /// <param name="kind">名前の種類（期待値の規則を選ぶ）</param>
    /// <param name="acceptsAlone">
    /// その名前を 1 つだけ入れたときに受け付けるか（受け付けない名前＝その入口の名前の規則で弾かれる名前は、
    /// 重複の判定とは別の理由なので組ごと流さない）
    /// </param>
    /// <param name="run">組を流して結果を返す</param>
    /// <param name="derivedName">
    /// その入口が名前から作る C# の識別子（クラス名・メソッド名・プロパティ名）。規則が「別の名前」と言う組でも、
    /// 作る識別子が大文字小文字を無視して同じなら生成コードが衝突するので、断ってよい（組ごと流さない）。null は識別子を作らない入口
    /// </param>
    public static void Run(
        FuzzFailureLedger ledger,
        string entrance,
        NameKind kind,
        Func<string, bool> acceptsAlone,
        Func<NamePair, PairOutcome> run,
        Func<string, string?>? derivedName = null
    )
    {
        var alone = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool Accepts(string name)
        {
            if (!alone.TryGetValue(name, out var accepted))
            {
                accepted = acceptsAlone(name);
                alone[name] = accepted;
            }

            return accepted;
        }

        foreach (var seed in NameFuzz.Seeds)
        {
            foreach (var pair in NameFuzz.Pairs(seed, RandomPairsPerSeed))
            {
                var same = NameIdentityRule.AreSame(kind, pair.First, pair.Second);

                if (
                    !Accepts(pair.First)
                    || !Accepts(pair.Second)
                    || (
                        !same
                        && derivedName is not null
                        && derivedName(pair.First) is { } firstDerived
                        && string.Equals(
                            firstDerived,
                            derivedName(pair.Second),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    continue;
                }

                var outcome = run(pair);
                var difference = pair.Difference.ToString();
                var example = $"seed {seed}: {pair} \u2192 {outcome.Detail}";

                if (same && outcome.SecondAccepted)
                {
                    ledger.Record(new(entrance, "duplicate-accepted", difference), example);
                }

                if (!same && !outcome.SecondAccepted)
                {
                    ledger.Record(new(entrance, "distinct-rejected", difference), example);
                }

                // 受け付けた後の性質（後の名前で引けるか・保存形）は、組の違いでなく名前が前後に空白を持つかで分ける
                var shape =
                    pair.First.Trim() == pair.First && pair.Second.Trim() == pair.Second
                        ? "Plain"
                        : "WithWhitespace";

                foreach (var violation in outcome.Violations)
                {
                    ledger.Record(new(entrance, violation, shape), example);
                }
            }
        }
    }
}

/// <summary>総当たりで観測した失敗のキー</summary>
/// <param name="Entrance">入口（例 <c>mcp:add_entity</c>）</param>
/// <param name="Property">破れた性質（例 <c>duplicate-accepted</c>）</param>
/// <param name="Difference">名前の組の違い（1 つの名前の性質なら null）</param>
public sealed record FuzzFailureKey(string Entrance, string Property, string? Difference)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Entrance} | {Property} | {Difference ?? "-"}";
}

/// <summary>既知の失敗の一覧の 1 項目</summary>
/// <param name="Key">失敗のキー</param>
/// <param name="Finding">対応する指摘（summary の番号・記録の番号）</param>
/// <param name="FixedIn">空にする区切り</param>
public sealed record KnownFuzzFailure(FuzzFailureKey Key, string Finding, string FixedIn);

/// <summary>
/// 総当たりの失敗を集め、既知の失敗の一覧と厳密に照合する
/// </summary>
/// <remarks>
/// 一覧に無い失敗があれば赤、一覧に載っているのに観測されなかった失敗も赤（修正したら一覧から外す）。
/// 照合は入口ごとに行う（<paramref name="entrancePrefix"/> で始まる一覧の項目だけを見る）ので、
/// 1 つのテストが受け持つ入口の分だけを判定できる。
/// </remarks>
public sealed class FuzzFailureLedger(IReadOnlyList<KnownFuzzFailure> known)
{
    private readonly Dictionary<FuzzFailureKey, string> _observed = [];

    /// <summary>失敗を記録する（同じキーは最初の例だけを残す）</summary>
    /// <param name="key">失敗のキー</param>
    /// <param name="example">再現の例（シード・名前・結果）</param>
    public void Record(FuzzFailureKey key, string example) => _observed.TryAdd(key, example);

    /// <summary>入口の接頭辞に属する観測と一覧を厳密に照合する</summary>
    /// <param name="entrancePrefix">このテストが受け持つ入口の接頭辞</param>
    public void AssertMatchesKnown(params string[] entrancePrefixes)
    {
        bool Owned(FuzzFailureKey key) =>
            entrancePrefixes.Any(prefix =>
                key.Entrance.StartsWith(prefix, StringComparison.Ordinal)
            );

        var knownKeys = known.Where(item => Owned(item.Key)).ToDictionary(item => item.Key);
        var unexpected = _observed
            .Where(pair => Owned(pair.Key) && !knownKeys.ContainsKey(pair.Key))
            .Select(pair => $"  新しい失敗: {pair.Key}{Environment.NewLine}    例: {pair.Value}")
            .ToList();
        var fixedButListed = knownKeys
            .Values.Where(item => !_observed.ContainsKey(item.Key))
            .Select(item =>
                $"  一覧に載っているのに起きなかった（直ったなら一覧から外す）: {item.Key}（{item.Finding}・{item.FixedIn}）"
            )
            .ToList();
        var problems = unexpected.Concat(fixedButListed).ToList();

        if (problems.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                "名前の総当たりの結果が既知の失敗の一覧と一致しない:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
        }
    }
}
