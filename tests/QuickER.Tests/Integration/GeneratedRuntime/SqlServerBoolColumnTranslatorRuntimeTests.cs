using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c>（SQL Server 方言）の <b>bool 列短縮分岐</b>
/// （bool メンバ真＝<c>[col] = 1</c>・bool メンバの NOT＝<c>[col] = 0</c>）を、実 SQL Server
/// （Testcontainers）への往復で意味論検証するスイート。
/// <see cref="SqliteBoolColumnTranslatorRuntimeTests"/> と対称構造。
/// </summary>
/// <remarks>
/// この分岐はラムダパラメータ直下の<b>素の bool プロパティ</b>を要求するため、VO 化フィクスチャでは
/// 構造的に到達できない。raw 型で生成される SQL Server バイナリフィクスチャ
/// （<see cref="SqlServerBinaryFixtureDefinition"/>＝図は <see cref="BinaryFixtureDefinition"/> と同一）の
/// <c>is_published</c>（bit・非 nullable）が素の bool 列の唯一の担い手で、この列は本分岐の検証用に追加されたもの。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerBoolColumnTranslatorRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>スキーマを作成し、公開フラグ true/false 混在の文書 3 件をシードする</summary>
    /// <remarks>documents: 1="alpha"（公開）・2="beta"（非公開）・3="gamma"（公開）。</remarks>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(
            new SqlServerDdlGenerator().Build(SqlServerBinaryFixtureDefinition.Build()),
            Ct
        );

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        var documents = Documents();
        await documents.InsertAsync(NewDocument(1, "alpha", isPublished: true), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", isPublished: false), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", isPublished: true), Ct);
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>文書リポジトリを解決する</summary>
    private IDocumentRepository Documents() => _provider.GetRequiredService<IDocumentRepository>();

    /// <summary>文書エンティティを組み立てる（非 nullable の thumb には最小のダミーを与える）</summary>
    private static DocumentEntity NewDocument(int id, string title, bool isPublished) =>
        new()
        {
            DocumentId = id,
            Title = title,
            IsPublished = isPublished,
            Thumb = [1],
        };

    /// <summary>bool メンバ真: <c>d =&gt; d.IsPublished</c> が <c>[is_published] = 1</c> へ翻訳され、公開行のみ返す</summary>
    [Fact(DisplayName = "[SqlServer演算子] bool 列メンバ真（col = 1）が公開行のみ返す")]
    public async Task BoolMember_TranslatesToEqualsOne()
    {
        var published = await Documents().Query().Where(d => d.IsPublished).ToListAsync(Ct);
        published.Select(d => d.DocumentId).Should().BeEquivalentTo([1, 3]);
    }

    /// <summary>bool メンバの NOT: <c>d =&gt; !d.IsPublished</c> が <c>[is_published] = 0</c> へ翻訳され、非公開行のみ返す</summary>
    [Fact(DisplayName = "[SqlServer演算子] bool 列メンバの NOT（col = 0）が非公開行のみ返す")]
    public async Task NegatedBoolMember_TranslatesToEqualsZero()
    {
        var unpublished = await Documents().Query().Where(d => !d.IsPublished).ToListAsync(Ct);
        unpublished.Select(d => d.DocumentId).Should().BeEquivalentTo([2]);
    }

    /// <summary>bool メンバを AND と組み合わせても正しく合成される（<c>d.IsPublished &amp;&amp; d.Title == ...</c>）</summary>
    [Fact(DisplayName = "[SqlServer演算子] bool 列メンバと AND の組み合わせが正しく絞り込む")]
    public async Task BoolMember_CombinedWithAnd()
    {
        var result = await Documents()
            .Query()
            .Where(d => d.IsPublished && d.Title == "alpha")
            .ToListAsync(Ct);
        result.Select(d => d.DocumentId).Should().BeEquivalentTo([1]);
    }
}
