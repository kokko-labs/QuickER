using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
/// 値は位置で照合し、版も値が運ぶ。値の欄を持たない応答と、値の数が行の数と合わない応答（別の版の QuickER で生成した
/// サーバー）は、名指しの <see cref="InvalidOperationException"/> で断る。
/// </remarks>
public sealed class RemoteSaveResponseClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

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

    [Fact(
        DisplayName = "リモートの保存: 応答の値（版を含む）が位置で書き戻され、サーバーのフックが主キーを変えた行にも届く"
    )]
    public async Task Save_WritesBackValues_IncludingVersionOfRowWithChangedKey()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$"""
            {"Affected":1,
             "Skipped":null,
             "Saved":[{"GadgetId":5,"Name":"server","RowVer":"{{ValuesVersion}}"}],
             "Unsaved":[null]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.GadgetId.Value.Should().Be(5, "サーバーのフックが決めた主キー");
        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
        gadget.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(
        DisplayName = "リモートの保存・挿入・更新: 値の欄が無い応答（別の版の QuickER で生成したサーバー）は、生成し直す案内の例外で断り、手元の行を変えない"
    )]
    public async Task Responses_WithoutValues_AreRefused()
    {
        var saved = NewGadget(1);
        var save = () => Client("""{"Affected":1}""").SaveAsync(saved, cancellationToken: Ct);
        (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*'Saved'*different versions of QuickER*"
        );
        saved.RowState.Should().Be(RowState.Added);

        var inserted = NewGadget(1);
        var insert = () => Client("{}").InsertAsync(inserted, Ct);
        (await insert.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*'Values'*different versions of QuickER*"
        );
        inserted.Name.Value.Should().Be("local");

        var updated = NewGadget(1);
        updated.RowVer = RowVerValue.Create([0x00]);
        updated.MarkUpdated();
        var update = () =>
            Client("""{"Updated":true}""").UpdateAsync(updated, cancellationToken: Ct);
        (await update.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*'Values'*different versions of QuickER*"
        );
        updated.RowVer.Value.Should().Equal([0x00]);
    }

    [Fact(DisplayName = "リモートの保存: 値が null の位置の行は飛ばした行として状態を残す")]
    public async Task Save_NullValues_KeepsTheRowAsSkipped()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":0,"Skipped":[],"Saved":[null],"Unsaved":[null]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.RowState.Should().Be(RowState.Added);
        gadget.Name.Value.Should().Be("local");
    }

    [Fact(
        DisplayName = "リモートの保存: 値の数が行の数と合わない応答は、数を名指しした例外で断り、手元の行を変えない"
    )]
    public async Task Save_MismatchedValueCount_IsRefused()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":1,"Saved":[{"Name":"a"},{"Name":"b"}],"Unsaved":[null]}
            """
        );

        var save = () => repository.SaveAsync(gadget, cancellationToken: Ct);

        (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*2 'Saved' values for 1 rows*"
        );
        gadget.Name.Value.Should().Be("local");
        gadget.RowState.Should().Be(RowState.Added);
    }

    [Fact(DisplayName = "リモートの保存: 飛ばした行は、Unsaved の値が書き戻され、状態を残す")]
    public async Task Save_SkippedRow_WritesBackUnsavedValuesAndKeepsState()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":0,"Skipped":[],"Saved":[null],
             "Unsaved":[{"GadgetId":1,"Name":"hook"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.Name.Value.Should().Be("hook", "サーバーのフックが飛ばす前に書いた値");
        gadget.RowState.Should().Be(RowState.Added);
    }

    [Fact(DisplayName = "リモートの保存: 削除した行は、Unsaved の値が書き戻され、Removed のまま")]
    public async Task Save_RemovedRow_WritesBackUnsavedValuesAndStaysRemoved()
    {
        var gadget = NewGadget(1);
        gadget.MarkRemoved();
        var repository = Client(
            """
            {"Affected":1,"Skipped":[],"Saved":[],
             "Unsaved":[{"GadgetId":1,"Name":"hook-delete"}]}
            """
        );

        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.Name.Value.Should().Be("hook-delete", "サーバーのフックが削除の前に書いた値");
        gadget.RowState.Should().Be(RowState.Removed);
    }

    [Fact(
        DisplayName = "リモートの保存: Unsaved の数が行の数と合わない応答は、数を名指しした例外で断り、手元の行を変えない"
    )]
    public async Task Save_MismatchedUnsavedCount_IsRefused()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            """
            {"Affected":0,"Skipped":[],"Saved":[null],
             "Unsaved":[{"Name":"a"},{"Name":"b"}]}
            """
        );

        var save = () => repository.SaveAsync(gadget, cancellationToken: Ct);

        (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*2 'Unsaved' values for 1 rows*"
        );
        gadget.Name.Value.Should().Be("local");
        gadget.RowState.Should().Be(RowState.Added);
    }

    [Fact(
        DisplayName = "リモートの保存の応答: Unsaved は位置でない init プロパティのまま JSON を往復し、知らない欄は読み捨てる"
    )]
    public void SaveResult_UnsavedRoundTripsAndUnknownFieldsAreIgnored()
    {
        var result = new RemoteSaveResult(1, null, [null])
        {
            Unsaved = [new() { ["Name"] = JsonSerializer.SerializeToElement("x") }],
        };

        var json = JsonSerializer.Serialize(result, RemoteJson.Options);
        var read = JsonSerializer.Deserialize<RemoteSaveResult>(json, RemoteJson.Options)!;

        read.Unsaved.Should().ContainSingle().Which!["Name"].GetString().Should().Be("x");

        // 知らない欄は読み捨て、欄の無い Unsaved は null で読む（クライアントは null の Unsaved を断る）
        var withUnknown = JsonSerializer.Deserialize<RemoteSaveResult>(
            """{"Affected":2,"FieldFromTheFuture":[1]}""",
            RemoteJson.Options
        )!;
        withUnknown.Affected.Should().Be(2);
        withUnknown.Unsaved.Should().BeNull();
    }

    [Fact(DisplayName = "リモートの挿入: 応答の値（版を含む）が書き戻される")]
    public async Task Insert_WritesBackValues()
    {
        var gadget = NewGadget(1);
        var repository = Client(
            $$$"""
            {"Values":{"Name":"server","RowVer":"{{{ValuesVersion}}}"}}
            """
        );

        await repository.InsertAsync(gadget, Ct);

        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
    }

    [Fact(DisplayName = "リモートの更新: 応答の値（版を含む）が書き戻される")]
    public async Task Update_WritesBackValues()
    {
        var gadget = NewGadget(1);
        gadget.RowVer = RowVerValue.Create([0x00]);
        gadget.MarkUpdated();
        var repository = Client(
            $$$"""
            {"Updated":true,"Values":{"Name":"server","RowVer":"{{{ValuesVersion}}}"}}
            """
        );

        (await repository.UpdateAsync(gadget, cancellationToken: Ct)).Should().BeTrue();

        gadget.Name.Value.Should().Be("server");
        gadget.RowVer.Value.Should().Equal([0x09, 0x0A]);
    }

    [Fact(
        DisplayName = "リモートの保存: 外部キーと持っている親が食い違うグラフは、送る前に直結と同じ例外で断る"
    )]
    public async Task Save_InconsistentGraph_IsRejectedBeforeSending()
    {
        var handler = new StubHandler("{}");
        var repository = new HttpGadgetRemoteRepository(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/quicker/") }
        );
        var gadget = NewGadget(1);
        var note = new GadgetNoteEntity
        {
            NoteId = NoteIdValue.Create(10),
            GadgetId = GadgetIdValue.Create(2),
            Note = NoteValue.Create("n"),
        };
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        var single = () => repository.SaveAsync(gadget, cancellationToken: Ct);
        var many = () => repository.SaveAsync([gadget], cancellationToken: Ct);

        (await single.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*GadgetNoteEntity*GadgetNotes*"
        );
        await many.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().Be(0);
    }

    /// <summary>どの要求にも同じ本文で 200 を返す（受けた要求の数を数える）</summary>
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        /// <summary>受けた要求の数</summary>
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
