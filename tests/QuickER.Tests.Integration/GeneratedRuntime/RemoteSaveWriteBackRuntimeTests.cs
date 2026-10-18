using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートの保存で、サーバーが保存した行の列の値（サーバー側の Before フックが書いた値・決めた主キー）がクライアントへ
/// 戻ることを、BinaryFixture のリモートサービス（サーバーの実体は SQLite の QuickER 版 Repository）で検証する。
/// </summary>
/// <remarks>
/// 値は削除されていない行をカスケードの順にたどったときの位置で照合する（キーでは照合しない＝フックが主キーを変えても
/// 外れない）。同じ親の下に飛ばした子・削除した子・変更のない子が混ざる保存、cascadeSave: false の保存、null 要素を含む
/// 複数の根の保存でも位置がそろうこと、除外した無制限バイナリ列が応答に載らないことも見る。
/// </remarks>
public sealed class RemoteSaveWriteBackRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private readonly ResponseCapture _capture = new();
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    public async ValueTask InitializeAsync() =>
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }

    /// <summary>サーバー側に保存フックを登録したリモートを立て、クライアントの Repository を返す</summary>
    private async Task<IDocumentRemoteRepository> RemoteAsync(params object[] hooks)
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString);

                foreach (var hook in hooks)
                {
                    services.AddSaveHook(hook);
                }
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        _client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_ =>
            {
                _capture.InnerHandler = new HttpClientHandler();
                return new HttpClient(_capture)
                {
                    // 生成クライアントのベースアドレスは末尾の / を要する
                    BaseAddress = new(
                        $"{_server.BaseAddress(RemotePaths.DefaultPrefix).TrimEnd('/')}/"
                    ),
                };
            })
            .BuildServiceProvider();
        return _client.GetRequiredService<IDocumentRemoteRepository>();
    }

    private static DocumentEntity NewDocument(int id, string title)
    {
        var document = new DocumentEntity { DocumentId = id, Title = title };
        document.MarkAdded();
        return document;
    }

    private static DocumentNoteEntity NewNote(int id, int documentId, string note)
    {
        var entity = new DocumentNoteEntity
        {
            NoteId = id,
            DocumentId = documentId,
            Note = note,
        };
        entity.MarkAdded();
        return entity;
    }

    /// <summary>挿入のときだけ IsPublished を立てる（監査列の例）</summary>
    private static RecordingHook<DocumentEntity> PublishOnInsert() =>
        new(new List<string>(), e => e.DocumentId)
        {
            BeforePredicate = (e, op) =>
            {
                if (op == SaveOperation.Insert)
                {
                    e.IsPublished = true;
                }

                return true;
            },
        };

    [Fact(
        DisplayName = "[Remote/書き戻し] サーバーの Before フックが書いた値はクライアントへ戻り、次の保存で消えない"
    )]
    public async Task HookStamp_IsWrittenBack_AndSurvivesNextSave()
    {
        var repository = await RemoteAsync(PublishOnInsert());
        var document = NewDocument(1, "alpha");

        await repository.SaveAsync(document, cancellationToken: Ct);
        document.IsPublished.Should().BeTrue("サーバーのフックが刻んだ値が戻る");

        // 保存した同じインスタンスを編集して保存し直す（読み直さない）
        document.Title = "beta";
        document.MarkUpdated();
        await repository.SaveAsync(document, cancellationToken: Ct);

        (await repository.GetByIdAsync(1, Ct))!.IsPublished.Should().BeTrue();
    }

    [Fact(
        DisplayName = "[Remote/書き戻し] サーバーの Before フックが決めた主キーがクライアントへ戻る"
    )]
    public async Task HookAssignedKey_IsWrittenBack()
    {
        var hook = new RecordingHook<DocumentEntity>(new List<string>(), e => e.DocumentId)
        {
            BeforePredicate = (e, op) =>
            {
                if (op == SaveOperation.Insert && e.DocumentId == 0)
                {
                    e.DocumentId = 42;
                }

                return true;
            },
        };
        var repository = await RemoteAsync(hook);
        var document = NewDocument(0, "alpha");

        await repository.SaveAsync(document, cancellationToken: Ct);

        document.DocumentId.Should().Be(42);
        document.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(
        DisplayName = "[Remote/書き戻し] 飛ばした子・削除した子・削除を飛ばした子・変更のない子が混ざっても、値は位置で正しい子へ戻る"
    )]
    public async Task MixedChildren_ValuesReachTheRightChild()
    {
        // 子の挿入では note の末尾に "!" を足し、"skip" の子と "pinned!" の子の削除は飛ばす
        var noteHook = new RecordingHook<DocumentNoteEntity>(new List<string>(), e => e.NoteId)
        {
            BeforePredicate = (e, op) =>
            {
                if (e.Note == "skip" || (op == SaveOperation.Delete && e.Note == "pinned!"))
                {
                    return false;
                }

                if (op == SaveOperation.Insert)
                {
                    e.Note += "!";
                }

                return true;
            },
        };
        var repository = await RemoteAsync(noteHook);

        var document = NewDocument(1, "alpha");
        document.DocumentNotes.Add(NewNote(10, 1, "kept"));
        document.DocumentNotes.Add(NewNote(11, 1, "gone"));
        document.DocumentNotes.Add(NewNote(14, 1, "pinned"));
        await repository.SaveAsync(document, cancellationToken: Ct);

        var kept = document.DocumentNotes.Single(n => n.NoteId == 10);
        var gone = document.DocumentNotes.Single(n => n.NoteId == 11);
        var pinned = document.DocumentNotes.Single(n => n.NoteId == 14);
        kept.Note.Should().Be("kept!");

        // 削除する子・削除を飛ばされる子・変更のない子・飛ばされる新しい子・入る新しい子を同じ親の下に並べる。
        // 削除を飛ばされた子は、サーバーでもクライアントでも Removed のままコレクションに残る
        gone.MarkRemoved();
        pinned.MarkRemoved();
        var skipped = NewNote(12, 1, "skip");
        var added = NewNote(13, 1, "added");
        document.DocumentNotes.Add(skipped);
        document.DocumentNotes.Add(added);

        await repository.SaveAsync(document, cancellationToken: Ct);

        document.DocumentNotes.Should().NotContain(gone, "削除した子はコレクションから外れる");
        document.DocumentNotes.Should().Contain(pinned, "削除を飛ばした子はコレクションに残る");
        pinned.RowState.Should().Be(RowState.Removed);
        kept.Note.Should().Be("kept!");
        kept.RowState.Should().Be(RowState.Unchanged);
        skipped.Note.Should().Be("skip", "飛ばした子には値を書き戻さない");
        skipped.RowState.Should().Be(RowState.Added, "飛ばした子は新しい行のまま残る");
        added.Note.Should().Be("added!", "値は位置で照合される");
        added.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(
        DisplayName = "[Remote/書き戻し] cascadeSave: false の保存は根だけに値を戻し、子には触れない"
    )]
    public async Task WithoutCascade_OnlyTheRootIsWrittenBack()
    {
        var noteHook = new RecordingHook<DocumentNoteEntity>(new List<string>(), e => e.NoteId)
        {
            BeforePredicate = (e, _) =>
            {
                e.Note += "!";
                return true;
            },
        };
        var repository = await RemoteAsync(PublishOnInsert(), noteHook);
        var document = NewDocument(1, "alpha");
        var note = NewNote(10, 1, "child");
        document.DocumentNotes.Add(note);

        await repository.SaveAsync(document, cascadeSave: false, cancellationToken: Ct);

        document.IsPublished.Should().BeTrue();
        note.Note.Should().Be("child");
        note.RowState.Should().Be(RowState.Added);
    }

    [Fact(
        DisplayName = "[Remote/書き戻し] null 要素を含む複数の根の保存でも、値はそれぞれの根へ戻る"
    )]
    public async Task SaveMany_WithNullElements_EachRootGetsItsOwnValues()
    {
        var hook = new RecordingHook<DocumentEntity>(new List<string>(), e => e.DocumentId)
        {
            BeforePredicate = (e, _) =>
            {
                e.Title = $"{e.Title}#{e.DocumentId}";
                return true;
            },
        };
        var repository = await RemoteAsync(hook);
        var first = NewDocument(1, "a");
        var second = NewDocument(2, "b");

        await repository.SaveAsync([null!, first, null!, second], cancellationToken: Ct);

        first.Title.Should().Be("a#1");
        second.Title.Should().Be("b#2");
    }

    [Fact(
        DisplayName = "[Remote/書き戻し] 除外した無制限バイナリ列は応答に載らず、クライアントの値も書き換えない"
    )]
    public async Task ExcludedBinaryColumns_AreNotCarriedBack()
    {
        var hook = new RecordingHook<DocumentEntity>(new List<string>(), e => e.DocumentId)
        {
            BeforePredicate = (e, _) =>
            {
                e.Payload = [9, 9, 9];
                return true;
            },
        };
        var repository = await RemoteAsync(hook);
        var document = NewDocument(1, "alpha");
        document.Thumb = [1];

        await repository.SaveAsync(document, cancellationToken: Ct);

        document.Payload.Should().BeNull("除外列はフックが書いても書き戻さない");
        document.Thumb.Should().Equal([1]);

        using var body = JsonDocument.Parse(_capture.LastResponseBody!);
        var saved = body.RootElement.GetProperty("Saved");
        saved.GetArrayLength().Should().Be(1);
        var columns = saved[0].EnumerateObject().Select(p => p.Name).ToList();
        columns.Should().Contain("Title");
        columns.Should().NotContain("Payload").And.NotContain("Thumb");
    }

    /// <summary>応答の本文を控える（応答に載る列を確かめるため）</summary>
    private sealed class ResponseCapture : DelegatingHandler
    {
        public string? LastResponseBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response.Content is { } content)
            {
                LastResponseBody = await content.ReadAsStringAsync(cancellationToken);
            }

            return response;
        }
    }
}
