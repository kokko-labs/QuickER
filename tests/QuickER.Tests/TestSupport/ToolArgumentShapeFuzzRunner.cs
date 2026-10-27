using System.Text.Json.Nodes;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ツールの入口（MCP の ER 図操作・内蔵チャット・MCP のコード生成）へ、形の崩れた引数を総当たりで流す骨格
/// </summary>
/// <remarks>
/// <para>
/// 流れは入口に依らず同じ：ツールごとの成功する引数（ベースライン）を実行し、成功したものから
/// <see cref="ToolArgumentFuzz.Generate"/> が崩したケースを作り、<see cref="KnownCaseLedger"/> を通して 1 件ずつ
/// 3 つの表明を確かめる。(1) 生の例外にならない、(2) 断るべきケース（<see cref="ToolArgumentFuzz.Case.MustFail"/>）は
/// 失敗で返る、(3) 失敗したら何も変えない。最後に台帳の古い項目・網羅の計数・最低件数を照合する。
/// </para>
/// <para>
/// 入口ごとに違うのは <see cref="Entrance"/> の抽象メンバー（ツールとベースライン・実行のしかた・生の例外の見分け方・
/// 副作用の確かめ方・ケースごとの準備）だけ。
/// </para>
/// </remarks>
internal static class ToolArgumentShapeFuzzRunner
{
    /// <summary>総当たりするツール 1 つ（崩す元の入力スキーマと、成功する引数の並び）</summary>
    /// <param name="Name">ツール名</param>
    /// <param name="InputSchema">入力スキーマ（<see cref="ToolArgumentFuzz.SchemaOf"/> へ渡す）</param>
    /// <param name="Baselines">成功する引数（ラベルと引数）。ケース ID の接頭辞は <c>{ツール名}#{ラベル}</c></param>
    internal sealed record Tool(
        string Name,
        object InputSchema,
        IReadOnlyList<(string Label, JsonObject Args)> Baselines
    );

    /// <summary>ケース 1 件の実行の結果</summary>
    /// <param name="Result">ツールの結果の文</param>
    /// <param name="Success">成功したか</param>
    /// <param name="FindSideEffect">
    /// 失敗したときだけ呼ぶ。何かを変えていればその説明（例「失敗したのに図が変わった」）、変えていなければ null
    /// </param>
    internal sealed record CaseOutcome(string Result, bool Success, Func<string?> FindSideEffect);

    /// <summary>入口ごとに違う部分</summary>
    internal abstract class Entrance
    {
        /// <summary>網羅の計数と網羅しない組の一覧の範囲（<see cref="ToolArgumentFuzzDimensions"/> の Scope）</summary>
        public abstract string Scope { get; }

        /// <summary>既知の失敗の台帳</summary>
        public abstract KnownCaseLedger Ledger { get; }

        /// <summary>組み立てたケースの数の下限（黙った空振りを止める。この数以上を求める）</summary>
        public abstract int MinimumCases { get; }

        /// <summary>総当たりするツール（この並びの順に流す）</summary>
        public abstract IReadOnlyList<Tool> Tools();

        /// <summary>ベースラインを実行する（必要な準備を含む）</summary>
        public abstract (string Result, bool Success) RunBaseline(string toolName, JsonObject args);

        /// <summary>崩したケースの前の準備（台帳の外で行う。既定は何もしない）</summary>
        public virtual void PrepareCase() { }

        /// <summary>崩したケース 1 件を実行する（台帳の中で呼ぶ）</summary>
        public abstract CaseOutcome RunCase(string toolName, JsonObject args);

        /// <summary>結果の文が、入口が例外を受け止めた（＝生の例外）ときのものか</summary>
        public abstract bool IsRawException(string result);
    }

    /// <summary>入口へ全ツールの崩したケースを流し、問題があれば赤にする</summary>
    public static void Run(Entrance entrance)
    {
        var seenIds = new HashSet<string>();
        var problems = new List<string>();
        var caseCount = 0;
        var coverage = new CoverageCounter(ToolArgumentFuzzDimensions.Table);

        foreach (var tool in entrance.Tools())
        {
            var schema = ToolArgumentFuzz.SchemaOf(tool.InputSchema);

            foreach (var (label, baseline) in tool.Baselines)
            {
                var prefix = $"{tool.Name}#{label}";

                // 起点が成功しないと、崩したケースの失敗が崩し方のせいか分からない
                var (baseResult, baseSuccess) = entrance.RunBaseline(tool.Name, baseline);

                if (!baseSuccess)
                {
                    problems.Add($"[{prefix}] ベースラインが失敗した: {baseResult}");
                    continue;
                }

                foreach (var fuzzCase in ToolArgumentFuzz.Generate(prefix, schema, baseline))
                {
                    caseCount++;
                    ToolArgumentFuzzDimensions.Observe(coverage, fuzzCase);
                    entrance.PrepareCase();
                    entrance.Ledger.Run(
                        fuzzCase.Id,
                        () => Check(entrance, tool.Name, fuzzCase, seenIds),
                        problems
                    );
                }
            }
        }

        foreach (var stale in entrance.Ledger.Ids.Except(seenIds))
        {
            problems.Add($"[{stale}] 台帳にあるが、どのケースにも当たらない。台帳から外す。");
        }

        // 網羅の宣言の誤りはケースの結果より先に赤にする
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();
        coverageReport.WriteGapUsage(ToolArgumentFuzzDimensions.Gaps, entrance.Scope);

        CoverageGuard.AtLeast("総当たりで組み立てたケース", caseCount, entrance.MinimumCases);
        KnownCaseLedger.ThrowIfAny(problems);

        // 網羅しない組は、ケースの結果を表明してから照合する（網羅の赤がケースの失敗を隠さない）
        coverageReport.AssertGaps(ToolArgumentFuzzDimensions.Gaps, entrance.Scope);
    }

    /// <summary>崩したケース 1 件を実行して 3 つの表明を確かめる</summary>
    private static void Check(
        Entrance entrance,
        string toolName,
        ToolArgumentFuzz.Case fuzzCase,
        HashSet<string> seenIds
    )
    {
        seenIds.Add(fuzzCase.Id);
        var outcome = entrance.RunCase(toolName, fuzzCase.Arguments);

        if (entrance.IsRawException(outcome.Result))
        {
            throw new Xunit.Sdk.XunitException($"生の例外: {outcome.Result}");
        }

        if (fuzzCase.MustFail && outcome.Success)
        {
            throw new Xunit.Sdk.XunitException($"成功した（断るべき）: {outcome.Result}");
        }

        if (outcome.Success)
        {
            return;
        }

        if (outcome.FindSideEffect() is { } sideEffect)
        {
            throw new Xunit.Sdk.XunitException($"{sideEffect}: {outcome.Result}");
        }
    }
}
