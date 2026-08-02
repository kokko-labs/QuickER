using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成ランタイムの <c>SqlExpressionTranslator</c>（SQLite 方言）の <b>bool 列短縮分岐</b>
/// （bool メンバ真＝<c>"col" = 1</c>・bool メンバの NOT＝<c>"col" = 0</c>）を、実 SQLite
/// （一時ファイル DB・Docker 不要）への往復で意味論検証するスイート。
/// <see cref="SqlServerBoolColumnTranslatorRuntimeTests"/> と対称構造。
/// </summary>
/// <remarks>
/// この分岐はラムダパラメータ直下の<b>素の bool プロパティ</b>を要求するため、VO 化フィクスチャ
/// （GeneratedFixture / SqlitePortableFixture）では構造的に到達できない（<c>.Value</c> 経由は 2 段の
/// メンバアクセスとなり翻訳器が <c>NotSupportedException</c> を投げる）。raw 型で生成される
/// バイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>）の <c>is_published</c>（bit・非 nullable）が
/// 素の bool 列の唯一の担い手で、この列は本分岐の検証用に追加されたもの。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteBoolColumnTranslatorRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider? _provider;

    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();

    /// <summary>スキーマを初期化し、公開フラグ true/false 混在の文書 3 件をシードしたリポジトリを返す</summary>
    /// <remarks>documents: 1="alpha"（公開）・2="beta"（非公開）・3="gamma"（公開）。</remarks>
    private async Task<IDocumentRepository> ResetAndSeedAsync()
    {
        await using (var conn = new SqliteConnection(_db.ReadWriteCreateConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"document_notes\"; DROP TABLE IF EXISTS \"documents\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        var documents = Provider().GetRequiredService<IDocumentRepository>();
        await documents.InsertAsync(NewDocument(1, "alpha", isPublished: true), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", isPublished: false), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", isPublished: true), Ct);
        return documents;
    }

    /// <summary>文書エンティティを組み立てる（非 nullable の thumb には最小のダミーを与える）</summary>
    private static DocumentEntity NewDocument(int id, string title, bool isPublished) =>
        new()
        {
            DocumentId = id,
            Title = title,
            IsPublished = isPublished,
            Thumb = [1],
        };

    /// <summary>bool メンバ真: <c>d =&gt; d.IsPublished</c> が <c>"is_published" = 1</c> へ翻訳され、公開行のみ返す</summary>
    [Fact(DisplayName = "[SQLite演算子] bool 列メンバ真（col = 1）が公開行のみ返す")]
    public async Task BoolMember_TranslatesToEqualsOne()
    {
        var documents = await ResetAndSeedAsync();

        var published = await documents.Query().Where(d => d.IsPublished).ToListAsync(Ct);
        published.Select(d => d.DocumentId).Should().BeEquivalentTo([1, 3]);
    }

    /// <summary>bool メンバの NOT: <c>d =&gt; !d.IsPublished</c> が <c>"is_published" = 0</c> へ翻訳され、非公開行のみ返す</summary>
    [Fact(DisplayName = "[SQLite演算子] bool 列メンバの NOT（col = 0）が非公開行のみ返す")]
    public async Task NegatedBoolMember_TranslatesToEqualsZero()
    {
        var documents = await ResetAndSeedAsync();

        var unpublished = await documents.Query().Where(d => !d.IsPublished).ToListAsync(Ct);
        unpublished.Select(d => d.DocumentId).Should().BeEquivalentTo([2]);
    }

    /// <summary>bool メンバを AND と組み合わせても正しく合成される（<c>d.IsPublished &amp;&amp; d.Title == ...</c>）</summary>
    [Fact(DisplayName = "[SQLite演算子] bool 列メンバと AND の組み合わせが正しく絞り込む")]
    public async Task BoolMember_CombinedWithAnd()
    {
        var documents = await ResetAndSeedAsync();

        var result = await documents
            .Query()
            .Where(d => d.IsPublished && d.Title == "alpha")
            .ToListAsync(Ct);
        result.Select(d => d.DocumentId).Should().BeEquivalentTo([1]);
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _db.Dispose();
    }
}
