using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// 内蔵チャットのツール実行（<see cref="ErDiagramHostChatAdapter"/> 経由で GUI の図を操作する 15 ツール）へ、
/// 形の崩れた引数を総当たりで流す
/// </summary>
/// <remarks>
/// <para>
/// 入口は全チャットエンジンが共有する <see cref="QuickER.AI.IErDiagramToolHost.Execute"/>。名前付きクエリの 3 ツールは
/// アダプタが共有コアで処理し、それ以外は GUI の <see cref="ErDiagramDynamicTools"/> へ渡る。ケースは
/// <see cref="ErDiagramToolCatalog"/> の入力スキーマから <see cref="ToolArgumentFuzz"/> が作る。
/// </para>
/// <para>
/// 表明は MCP 側（<c>ToolArgumentShapeFuzzTests</c>）と同じ 3 つ：生の例外にならない（GUI の例外の文で返らない・
/// アダプタから例外が漏れない）、断るべきケースは失敗で返る、失敗したら図が変わらない。
/// </para>
/// </remarks>
public sealed class ChatToolArgumentShapeFuzzTests
{
    /// <summary>既知の失敗（直す前の現状）。直したら外す</summary>
    private static readonly KnownCaseLedger Ledger = new(
        "ToolArgumentShapeFuzz.chat",
        ToolArgumentShapeFuzzKnownFailures.Chat
    );

    /// <summary>この実行で通ったケース ID（台帳の古い項目を見つけるため）</summary>
    private readonly HashSet<string> _seenIds = [];

    [Fact(
        DisplayName = "内蔵チャットのツールは、形の崩れた引数で生の例外にならず、断るべきものは断り、失敗で図を変えない"
    )]
    public void MalformedArguments_AreRefusedWithoutSideEffects()
    {
        var baselines = ToolArgumentFuzzSeed.CatalogBaselines();
        var tools = ErDiagramToolCatalog.GetDefinitions();

        tools
            .Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo(baselines.Keys, "公開しているツールすべてにベースラインを持つ");

        var problems = new List<string>();
        var caseCount = 0;
        var coverage = new CoverageCounter(ToolArgumentFuzzDimensions.Table);

        foreach (var tool in tools)
        {
            var schema = ToolArgumentFuzz.SchemaOf(tool.InputSchema);

            foreach (var (label, baseline) in baselines[tool.Name])
            {
                var prefix = $"{tool.Name}#{label}";
                var (baseResult, baseSuccess) = Run(NewHost(out _), tool.Name, baseline);

                if (!baseSuccess)
                {
                    problems.Add($"[{prefix}] ベースラインが失敗した: {baseResult}");
                    continue;
                }

                foreach (var fuzzCase in ToolArgumentFuzz.Generate(prefix, schema, baseline))
                {
                    caseCount++;
                    ToolArgumentFuzzDimensions.Observe(coverage, fuzzCase);
                    Ledger.Run(fuzzCase.Id, () => Check(tool.Name, fuzzCase), problems);
                }
            }
        }

        foreach (var stale in Ledger.Ids.Except(_seenIds))
        {
            problems.Add($"[{stale}] 台帳にあるが、どのケースにも当たらない。台帳から外す。");
        }

        // 網羅の宣言の誤りはケースの結果より先に赤にする
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();
        coverageReport.WriteGapUsage(
            ToolArgumentFuzzDimensions.Gaps,
            ToolArgumentFuzzDimensions.ChatScope
        );

        CoverageGuard.AtLeast("総当たりで組み立てたケース", caseCount, 301);
        KnownCaseLedger.ThrowIfAny(problems);

        // 網羅しない組は、ケースの結果を表明してから照合する（網羅の赤がケースの失敗を隠さない）
        coverageReport.AssertGaps(
            ToolArgumentFuzzDimensions.Gaps,
            ToolArgumentFuzzDimensions.ChatScope
        );
    }

    /// <summary>崩したケース 1 件を新しい図で実行して 3 つの表明を確かめる</summary>
    private void Check(string toolName, ToolArgumentFuzz.Case fuzzCase)
    {
        _seenIds.Add(fuzzCase.Id);
        var host = NewHost(out var viewModel);
        var before = Snapshot(viewModel);
        var (result, success) = Run(host, toolName, fuzzCase.Arguments);
        var rawPrefix = string.Format(GuiStrings.Tool_Error, string.Empty);

        if (result.StartsWith(rawPrefix, StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException($"生の例外: {result}");
        }

        if (fuzzCase.MustFail && success)
        {
            throw new Xunit.Sdk.XunitException($"成功した（断るべき）: {result}");
        }

        if (!success && Snapshot(viewModel) != before)
        {
            throw new Xunit.Sdk.XunitException($"失敗したのに図が変わった: {result}");
        }
    }

    /// <summary>種の図を読み込んだ GUI の図と、それを操作するチャットのツール実行口を作る</summary>
    private static QuickER.AI.IErDiagramToolHost NewHost(out MainViewModel viewModel)
    {
        viewModel = new MainViewModel();
        viewModel.ReplaceDiagramFromModule(ToolArgumentFuzzSeed.BuildDiagram());

        return new ErDiagramHostChatAdapter(new MainViewModelErDiagramHost(viewModel)).ToolHost;
    }

    /// <summary>チャットエンジンと同じ入口でツールを実行する（漏れた例外は生の例外として返す）</summary>
    private static (string Result, bool Success) Run(
        QuickER.AI.IErDiagramToolHost host,
        string toolName,
        JsonObject arguments
    )
    {
        try
        {
            return host.Execute(toolName, arguments.ToJsonString());
        }
        catch (Exception ex)
        {
            return (
                string.Format(
                    GuiStrings.Tool_Error,
                    $"（入口から漏れた {ex.GetType().Name}）{ex.Message}"
                ),
                false
            );
        }
    }

    /// <summary>図の状態（意味モデル＋名前付きクエリ）を比較用の文字列にする</summary>
    private static string Snapshot(MainViewModel viewModel) =>
        JsonSerializer.Serialize(viewModel.ToDiagramModel());
}
