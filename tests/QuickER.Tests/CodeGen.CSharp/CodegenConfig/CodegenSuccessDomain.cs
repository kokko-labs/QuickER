using System.Collections.Concurrent;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// Success の表の領域の述語：生成が通る（診断の規則が生成時エラーを予測しない）構成だけを表の領域にする
/// </summary>
/// <remarks>
/// <para>
/// これは次元表の <c>isValid</c> の原則（原理的に起こり得ない組だけを断る）とは別の仕組みで、isValid の意味は変えない。
/// 領域の外の組（生成時エラーになる組）は Errors の表（<see cref="CodegenConfigDimensions.Table"/>）が網羅し、
/// 診断の予測をそこで照合する。Success の表はこの述語と原理的な規則の論理積を isValid として受け取る。
/// </para>
/// <para>
/// 述語は正本の規則（テスト側の予測の規則 <see cref="DiagnosticRules.For"/> のエラーの規則）を参照し、写さない。
/// 入口が実際に生成へ渡すオプション（GUI は画面に出ていないオプションを落とす＝<see cref="GuiOptionRules"/>）で評価する
/// （<see cref="CodegenRunner.EffectiveOptions"/>）。製品の診断のコードからは作らない。
/// </para>
/// <para>
/// 一部の因子だけの割り当て（網羅すべき組・組み立て途中の行）には、「割り当てを含む完全な行で、領域に入るものがある」ときだけ真を返す。
/// 判定は、エラーの規則が読む因子（<see cref="RuleFactors"/>）の全組み合わせを一度だけ評価した表で引く（ほかの因子は先頭の水準に固定する）。
/// この前提（エラーの規則がほかの因子を読まない）は、<c>CodegenSuccessMatrixTests.DomainPredicate_ReadsOnlyRuleFactors</c> が
/// 全部の行で「そのまま評価した結果＝読む因子だけに絞って評価した結果」を表明して固定する。
/// </para>
/// </remarks>
internal static class CodegenSuccessDomain
{
    /// <summary>エラーの規則が読む因子（GUI の入口はオプションを落とすので入口も読む。同期の対象の表の有無はキーの形が決める）</summary>
    public static IReadOnlyList<string> RuleFactors { get; } =
    [
        EditModels,
        Mappers,
        DbAccess,
        CodegenConfigDimensions.Dialects,
        InMemory,
        Remote,
        Sync,
        Annotations,
        CodegenConfigDimensions.Entrance,
        // 同期の「対象の表（主キーが 1 列の表）が 1 つ以上」のエラーは図の形のキーを読む
        Key,
    ];

    /// <summary>読む因子の割り当てのうち、領域に入るもの</summary>
    private static readonly Lazy<IReadOnlyList<Dictionary<string, string>>> InDomain = new(
        BuildInDomain
    );

    /// <summary>一部の因子だけの割り当ての答え（読む因子の部分の鍵 → 真偽）</summary>
    private static readonly ConcurrentDictionary<string, bool> PartialCache = new(
        StringComparer.Ordinal
    );

    /// <summary>行が領域に入るか（規則が生成時エラーを予測しないか）</summary>
    /// <param name="row">完全な行</param>
    public static bool PredictsSuccess(CodegenRow row) =>
        !DiagnosticRules
            .For(new ShapeDiagram(row.Shape), CodegenRunner.EffectiveOptions(row))
            .ExpectsError;

    /// <summary>行を読む因子だけに絞り、ほかの因子を先頭の水準にした行</summary>
    /// <param name="row">完全な行</param>
    public static CodegenRow Project(CodegenRow row) =>
        Complete(
            RuleFactors.ToDictionary(
                factor => factor,
                factor => row[factor],
                StringComparer.Ordinal
            )
        );

    /// <summary>割り当て（一部の因子でもよい）を含む完全な行で、領域に入るものがあるか</summary>
    /// <param name="assignment">因子から水準への割り当て</param>
    public static bool Contains(IReadOnlyDictionary<string, string> assignment)
    {
        if (assignment.Count == Table.Factors.Count)
        {
            return PredictsSuccess(new CodegenRow("domain", assignment));
        }

        var present = RuleFactors.Where(assignment.ContainsKey).ToList();
        var key = string.Join("|", present.Select(factor => $"{factor}={assignment[factor]}"));

        return PartialCache.GetOrAdd(
            key,
            _ =>
                InDomain.Value.Any(candidate =>
                    present.All(factor => candidate[factor] == assignment[factor])
                )
        );
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildInDomain()
    {
        var result = new List<Dictionary<string, string>>();
        var partial = new Dictionary<string, string>(StringComparer.Ordinal);

        void Enumerate(int depth)
        {
            if (depth == RuleFactors.Count)
            {
                var row = Complete(partial);

                if (CodegenConfigDimensions.IsValid(row.Levels) && PredictsSuccess(row))
                {
                    result.Add(new Dictionary<string, string>(partial, StringComparer.Ordinal));
                }

                return;
            }

            var factor = RuleFactors[depth];

            foreach (var level in Table.Factor(factor).Levels)
            {
                partial[factor] = level.Name;
                Enumerate(depth + 1);
            }
        }

        Enumerate(0);
        return result;
    }

    /// <summary>読む因子の割り当てに、ほかの因子の先頭の水準を足して完全な行にする</summary>
    private static CodegenRow Complete(IReadOnlyDictionary<string, string> ruleLevels)
    {
        var levels = Table.Factors.ToDictionary(
            factor => factor.Name,
            factor =>
                ruleLevels.TryGetValue(factor.Name, out var level) ? level : factor.Levels[0].Name,
            StringComparer.Ordinal
        );
        return new CodegenRow("domain", levels);
    }
}
