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
/// 流れと表明は <see cref="ToolArgumentShapeFuzzRunner"/> が持ち、ここはこの入口に固有の部分だけを渡す。
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

        ToolArgumentShapeFuzzRunner.Run(
            new Entrance(
                tools
                    .Select(tool => new ToolArgumentShapeFuzzRunner.Tool(
                        tool.Name,
                        tool.InputSchema,
                        baselines[tool.Name]
                    ))
                    .ToList()
            )
        );
    }

    /// <summary>内蔵チャットの入口（ケースごとに種の図を読み込んだ新しい GUI の図で実行する）</summary>
    private sealed class Entrance(IReadOnlyList<ToolArgumentShapeFuzzRunner.Tool> tools)
        : ToolArgumentShapeFuzzRunner.Entrance
    {
        public override string Scope => ToolArgumentFuzzDimensions.ChatScope;

        public override KnownCaseLedger Ledger => ChatToolArgumentShapeFuzzTests.Ledger;

        public override int MinimumCases => 301;

        public override IReadOnlyList<ToolArgumentShapeFuzzRunner.Tool> Tools() => tools;

        public override (string Result, bool Success) RunBaseline(
            string toolName,
            JsonObject args
        ) => Run(NewHost(out _), toolName, args);

        public override ToolArgumentShapeFuzzRunner.CaseOutcome RunCase(
            string toolName,
            JsonObject args
        )
        {
            var host = NewHost(out var viewModel);
            var before = Snapshot(viewModel);
            var (result, success) = Run(host, toolName, args);

            return new(
                result,
                success,
                () => Snapshot(viewModel) != before ? "失敗したのに図が変わった" : null
            );
        }

        public override bool IsRawException(string result) =>
            result.StartsWith(
                string.Format(GuiStrings.Tool_Error, string.Empty),
                StringComparison.Ordinal
            );
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
