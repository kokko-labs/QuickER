namespace QuickER.Tests.TestSupport;

/// <summary>
/// ガードのテストが、修正より前に入る段階で使う既知の失敗の台帳（ケース ID → 期待する失敗の理由の断片）。
/// </summary>
/// <remarks>
/// 台帳にない失敗、台帳にあるのに起きない失敗、台帳にあるが期待した理由と別の理由で起きた失敗は、いずれも赤にする
/// （直した修正が台帳を更新し忘れても、新しい失敗が台帳に紛れ込んでも、修正の途中で別の原因が混ざっても検出できる）。
/// 修正が入るたびに該当の行を消し、区切りの最後に空にする。
/// </remarks>
/// <param name="known">既知の失敗（ケース ID → 期待する失敗の理由の断片）</param>
public sealed class KnownFailureLedger(IReadOnlyDictionary<string, string> known)
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    public IReadOnlyDictionary<string, string> Known { get; } = known;

    /// <summary>ケース ID の失敗を台帳と突き合わせる</summary>
    /// <param name="caseId">ケース ID</param>
    /// <param name="body">検証本体</param>
    /// <param name="problems">問題の集め先。null なら最初の問題をその場で例外にする</param>
    public void Run(string caseId, Action body, List<string>? problems = null)
    {
        Exception? failure = null;

        try
        {
            body();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        var problem = (failure, Known.TryGetValue(caseId, out var expected)) switch
        {
            (not null, false) => $"[{caseId}] {failure.Message}",
            (null, true) =>
                $"[{caseId}] 既知の失敗として台帳に載っているが、起きなくなった。台帳から外す。",
            (not null, true) when !failure.Message.Contains(expected!, StringComparison.Ordinal) =>
                $"[{caseId}] 期待した理由「{expected}」と別の理由で失敗した: {failure.Message}",
            _ => null,
        };

        if (problem is null)
        {
            return;
        }

        if (problems is null)
        {
            throw new Xunit.Sdk.XunitException(problem);
        }

        problems.Add(problem);
    }

    /// <summary>集めた問題があれば、全件を 1 つの失敗として報告する</summary>
    public static void ThrowIfAny(List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"{problems.Count} 件: {Environment.NewLine}{string.Join(Environment.NewLine, problems)}"
            );
        }
    }
}
