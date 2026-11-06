namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表の 1 行：正本の一覧と写しの一覧を、決めた比べ方で照合する</summary>
/// <remarks>
/// 正本と写しはどちらも関数で返す（C# の定数・反射・resx・docs・ワークフロー・スクリプト・csproj を読む）。読み取りの失敗
/// （目印の行が無い・区切りが無いなど）は関数が例外を投げ、行の赤として出る。比べ方は <see cref="ParityCompare"/>。
/// </remarks>
/// <param name="Id">行の Id（一意）</param>
/// <param name="Because">守ること（食い違いの報告に添える）</param>
/// <param name="Compare">比べ方</param>
/// <param name="Canon">正本の一覧</param>
/// <param name="Copy">写しの一覧</param>
internal sealed record ParityRow(
    string Id,
    string Because,
    ParityCompare Compare,
    Func<IReadOnlyList<string>> Canon,
    Func<IReadOnlyList<string>> Copy
)
{
    /// <summary>空振りの検知：正本の項目の最低数（正本を読めずに空になったのを止める）</summary>
    public int MinimumCanon { get; init; } = 1;
}

/// <summary>照合の表の比べ方</summary>
internal enum ParityCompare
{
    /// <summary>集合として一致する（重複は 1 つとみなす）</summary>
    Set,

    /// <summary>多重集合として一致する（重複の数も比べる・順は比べない）</summary>
    Multiset,

    /// <summary>順まで一致する</summary>
    Sequence,

    /// <summary>写しの項目はどれも正本にある（正本の一部を写す）</summary>
    CopyWithinCanon,

    /// <summary>正本の項目はどれも写しにある（写しが正本を全部載せる）</summary>
    CanonWithinCopy,
}

/// <summary>照合の表（領域ごとの行は partial のファイルに置く）</summary>
internal static partial class ParityRows
{
    private static readonly Lazy<IReadOnlyList<ParityRow>> LazyAll = new(() =>
        [.. Docs(), .. Workflow(), .. Resources(), .. CodeGen(), .. AiMock()]
    );

    /// <summary>すべての行（宣言の順）</summary>
    public static IReadOnlyList<ParityRow> All => LazyAll.Value;

    /// <summary>Id から行を引く</summary>
    public static ParityRow ById(string id) =>
        All.SingleOrDefault(row => row.Id == id)
        ?? throw new ArgumentException($"照合の行が無い: {id}", nameof(id));
}
