using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState などが他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedConcurrencyFixture;

/// <summary>
/// リモートのクライアントが、保存の応答に載った「保存した行の列の値」を書き戻すことを、応答を差し替えた HTTP で検証する
/// （ConcurrencyFixture の生成クライアント・DB とサーバーは使わない）。
/// </summary>
/// <remarks>
/// 値は位置で照合し、キーで照合する版の表と食い違っても値が勝つ。値の欄を持たない応答（古いサーバー）では、従来どおり
/// 版の表と飛ばした行の一覧だけで確定する。値の数が行の数と合わない応答も、値を使わずに従来どおり確定する。
/// </remarks>
public sealed class RemoteSaveResponseClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>版の表が運ぶ版（"AQI=" = 01 02）</summary>
    private const string TableVersion = "AQI=";

    /// <summary>値が運ぶ版（"CQo=" = 09 0A）</summary>
    private const string ValuesVersion = "CQo=";

    private static HttpGadgetRemoteRepository Client(string responseBody) =>
        new(
            new HttpClient(new StubHandler(responseBody))
            {
                BaseAddress = new Uri("http://localhost/quicker/"),
            }
        );

    private static GadgetEntity NewGadget(int id)
    {
        var gadget = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(id),
            Name = NameValue.Create("local"),
        };
        gadget.MarkAdded();
        return gadget;
    }

    [Fact(DisplayName = "リモートの保存: 応答の値が書き戻され、キーで照合する版の表より値が勝つ")]
    public async Task Save_WritesBackValues_AndValuesWinOverVersionTable()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$"""
            {"Affected":1,
             "RowVersions":[{"EntityType":"GadgetEntity","Key":"5","RowVersion":"{{TableVersion}}"}],
             "Skipped":null,
             "Saved":[{"GadgetId":5,"Name":"server","RowVer":"{{ValuesVersion}}"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.GadgetId.Value.Should().Be(5, "サーバーのフックが決めた主キー");
        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
        gadget.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(
        DisplayName = "リモートの保存: 値の欄が無い応答（古いサーバー）は、従来どおり版の表で確定する"
    )]
    public async Task Save_WithoutValues_FallsBackToVersionTable()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$"""
            {"Affected":1,
             "RowVersions":[{"EntityType":"GadgetEntity","Key":"1","RowVersion":"{{TableVersion}}"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.Name.Value.Should().Be("local");
        gadget.RowVer.Value.Should().Equal([0x01, 0x02]);
        gadget.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(DisplayName = "リモートの保存: 値の欄が無い応答でも、飛ばした行の一覧の行は状態を残す")]
    public async Task Save_WithoutValues_KeepsSkippedRows()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":0,"RowVersions":[],
             "Skipped":[{"EntityType":"GadgetEntity","Key":"1"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.RowState.Should().Be(RowState.Added);
    }

    [Fact(DisplayName = "リモートの保存: 値が null の位置の行は飛ばした行として状態を残す")]
    public async Task Save_NullValues_KeepsTheRowAsSkipped()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":0,"RowVersions":[],"Skipped":[],"Saved":[null]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.RowState.Should().Be(RowState.Added);
        gadget.Name.Value.Should().Be("local");
    }

    [Fact(
        DisplayName = "リモートの保存: 値の数が行の数と合わない応答は、値を使わずに従来どおり確定する"
    )]
    public async Task Save_MismatchedValueCount_IgnoresValues()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$"""
            {"Affected":1,
             "RowVersions":[{"EntityType":"GadgetEntity","Key":"1","RowVersion":"{{TableVersion}}"}],
             "Saved":[{"Name":"a"},{"Name":"b"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.Name.Value.Should().Be("local");
        gadget.RowVer.Value.Should().Equal([0x01, 0x02]);
        gadget.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(DisplayName = "リモートの挿入: 応答の値が書き戻され、版より値が勝つ")]
    public async Task Insert_WritesBackValues()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$$"""
            {"RowVersion":"{{{TableVersion}}}","Values":{"Name":"server","RowVer":"{{{ValuesVersion}}}"}}
            """
        );

        await repository.InsertAsync(gadget, Ct);

        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
    }

    [Fact(DisplayName = "リモートの更新: 応答の値が書き戻され、版より値が勝つ")]
    public async Task Update_WritesBackValues()
    {
        var gadget = NewGadget(1);
        gadget.RowVer = RowVerValue.Create([0x00]);
        gadget.MarkUpdated();
        var repository = Client(
            $$$"""
            {"Updated":true,"RowVersion":"{{{TableVersion}}}","Values":{"Name":"server","RowVer":"{{{ValuesVersion}}}"}}
            """
        );

        (await repository.UpdateAsync(gadget, cancellationToken: Ct)).Should().BeTrue();

        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
    }

    /// <summary>どの要求にも同じ本文で 200 を返す</summary>
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }
}
