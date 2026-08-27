using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ取得糖衣が展開しない辺（edge-skip）を、<b>子行が実在する状態で</b>実行器ごとに観測する共通基底。
/// </summary>
/// <remarks>
/// <para>
/// 生成側の閉包は、ルートからのパス上に既に現れたテーブルへ向かう辺を展開しない（自己参照は辿ると無限に深くなり、
/// 実行器も有限のツリーしか受け取れない）。生成テキストに「現れない」ことは単体テスト
/// （<c>IncludeGraphGenerationTests</c>）が固定するが、それが実行時にどう見えるか——<b>親を指す子行が実際に
/// あるのに、コレクションは空のまま返る</b>——は実行器を通さないと分からない。シードが空だと「落としている」のか
/// 「そもそも無い」のかを区別できないため、ここでは先に子行の実在を裏取りしてから空であることを表明する。
/// </para>
/// <para>
/// <b>EF Core が居ないのは意図的</b>: 値オブジェクト有効の図では自己参照 FK の CLR 型が参照先の主キー型と一致せず、
/// 生成された <c>DbContext</c> がモデル検証で落ちる（EF Core は FK プロパティの型が主キーの型と互換であることを
/// 要求する）。そのため自己参照は EF Core を生成しないフィクスチャ——<c>MultiTargetPortableFixture</c>
/// （sqlserver / sqlite）と <c>InMemoryFixture</c>（値オブジェクト無効）——にだけ置いている。
/// </para>
/// </remarks>
/// <typeparam name="TNode">自己参照エンティティ型（フィクスチャごとに別型）</typeparam>
[Trait("Category", "Integration")]
public abstract class IncludeGraphSelfReferenceRuntimeTestsBase<TNode>
    where TNode : class
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>保存先を用意し、親 1=("root", 親なし) と子 2=("child", 親=1) を投入する</summary>
    protected abstract Task ResetAndSeedAsync();

    /// <summary><c>Query().IncludeGraph()</c> で 1 件取得する（行なしは null）</summary>
    protected abstract Task<TNode?> FetchNodeWithGraphAsync(int nodeId);

    /// <summary>テーブルの行数を返す（子行が実在することの裏取り）</summary>
    protected abstract Task<int> CountNodesAsync();

    /// <summary>自己参照の子コレクションを取り出す</summary>
    protected abstract IReadOnlyList<TNode> ChildNodesOf(TNode node);

    /// <summary>親キーを取り出す（親なしは null）</summary>
    protected abstract int? ParentNodeIdOf(TNode node);

    /// <summary>自己参照ナビは、子行が実在していても IncludeGraph の後で空のまま</summary>
    [Fact(DisplayName = "[IncludeGraph] 自己参照ナビは子行が実在しても空のまま（edge-skip）")]
    public async Task SelfReference_StaysEmptyEvenWhenChildRowsExist()
    {
        await ResetAndSeedAsync();

        // 前提の裏取り: 子行は実在し、親を指している
        var rowCount = await CountNodesAsync();
        rowCount.Should().Be(2);

        var child = await FetchNodeWithGraphAsync(2);
        child.Should().NotBeNull();
        ParentNodeIdOf(child!).Should().Be(1, "子行は親 1 を指している");

        var parent = await FetchNodeWithGraphAsync(1);
        parent.Should().NotBeNull();
        ParentNodeIdOf(parent!).Should().BeNull();

        ChildNodesOf(parent!)
            .Should()
            .BeEmpty("自己参照辺は閉包に含まれない（IncludeGraph は展開しない）");
    }
}
