using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>
/// チャットエンジン 4 種（API キー・Codex・Claude Code・Copilot）の共通の契約を、同じ行で全エンジンへ流す網
/// </summary>
/// <remarks>
/// 実エンジン＋既存のテストダブルで組み、中立の台本をアダプタが各バックエンドの入口へ訳す。
/// エンジンごとの違いはアダプタの宣言（性質と出典・期待の文の組み方）が持つ。
/// 揃わない組が出たら、既知の失敗の一覧へ載せずに止める（製品の違いか不具合かもしれない）。
/// </remarks>
public sealed class ChatEngineContractTests
{
    /// <summary>行 × 前提の性質を持つエンジン</summary>
    public static TheoryData<string, string> Cases() => ChatEngineDimensions.Cases();

    [Theory(DisplayName = "契約の行：エンジンごとに同じ入力で同じ契約を守る")]
    [MemberData(nameof(Cases))]
    public async Task Row_Holds(string id, string engine)
    {
        var row = ChatEngineContractRows.Table[id];
        var adapter = ChatEngineAxis.Axis.Get(engine);
        await using var rig = adapter.CreateRig(row.Input.Host);

        await row.Input.Body(
            new ChatContractContext(adapter, rig, TestContext.Current.CancellationToken)
        );
    }

    [Fact(DisplayName = "網羅：エンジン × 契約の行（前提の性質を持たない組だけを理由つきで外す）")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(ChatEngineDimensions.Table);

        foreach (var (id, engine) in ChatEngineDimensions.CaseList())
        {
            ChatEngineDimensions.Observe(counter, id, engine);
        }

        counter.Report().AssertGaps(ChatEngineDimensions.Gaps, ChatEngineDimensions.Scope);
    }

    [Fact(DisplayName = "チャットエンジンの軸：正本の値ごとにアダプタがちょうど 1 つ")]
    public void Axis_CoversAuthoritativeValues()
    {
        ChatEngineAxis
            .Axis.Adapters.Select(adapter => adapter.AxisValue)
            .Should()
            .Equal(Enum.GetNames<ErChatBackendKind>());
        ChatEngineAxis.Axis.Excluded.Should().BeEmpty();
    }

    [Fact(DisplayName = "アダプタの性質の宣言：全性質を出典つきで宣言する")]
    public void Declarations_CoverEveryCapabilityWithEvidence()
    {
        foreach (var adapter in ChatEngineAxis.Axis.Adapters)
        {
            adapter
                .Declarations.Keys.Should()
                .BeEquivalentTo(Enum.GetValues<ChatEngineCapability>(), adapter.AxisValue);
            adapter
                .Declarations.Values.Should()
                .OnlyContain(
                    declaration =>
                        declaration.Evidence.StartsWith("src/", StringComparison.Ordinal),
                    $"{adapter.AxisValue} の出典はリポジトリのファイルから始める"
                );

            // 出典の先頭のパス（最初の空白か全角の括弧まで）が実在する（ファイルの名前が変わったときに出典が嘘になるのを止める）
            foreach (var (capability, declaration) in adapter.Declarations)
            {
                var path = declaration.Evidence.Split([' ', '（'])[0];
                File.Exists(Path.Combine(TestSupport.SourceFiles.RepositoryRoot, path))
                    .Should()
                    .BeTrue($"{adapter.AxisValue} の {capability} の出典 {path} が実在する");
            }
        }
    }

    /// <summary>
    /// アダプタの添付の範囲の期待値が、エンジンの公開する範囲と、合成ルートの規則（エンジンを作る前に使う）の両方と一致する
    /// </summary>
    [Fact(
        DisplayName = "添付の範囲：アダプタの期待値・エンジンの公開値・合成ルートの規則が一致する"
    )]
    public async Task AttachmentSupport_MatchesEngineAndResolver()
    {
        foreach (var adapter in ChatEngineAxis.Axis.Adapters)
        {
            await using var rig = adapter.CreateRig(ChatToolHostKind.None);

            rig.Engine.AttachmentSupport.Should()
                .Be(adapter.ExpectedAttachmentSupport, adapter.AxisValue);

            // 添付の性質の宣言は添付の範囲の期待値と食い違わない（宣言だけを変えて添付の行を黙って外さない）
            var allKinds = Enum.GetValues<ChatAttachmentKind>();
            adapter
                .Has(ChatEngineCapability.AcceptsAttachments)
                .Should()
                .Be(
                    allKinds.Any(kind => adapter.ExpectedAttachmentSupport.Allows(kind)),
                    adapter.AxisValue
                );
            adapter
                .Has(ChatEngineCapability.RefusesSomeAttachmentKind)
                .Should()
                .Be(
                    allKinds.Any(kind => !adapter.ExpectedAttachmentSupport.Allows(kind)),
                    adapter.AxisValue
                );
            AttachmentSupportResolver
                .ForBackend(adapter.Kind, AiProvider.OpenAI)
                .Should()
                .Be(adapter.ExpectedAttachmentSupport, adapter.AxisValue);
        }
    }
}
