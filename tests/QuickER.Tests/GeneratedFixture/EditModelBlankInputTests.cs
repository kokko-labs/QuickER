using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝RowState / EditModelMessages が他フィクスチャの同名型と
// 曖昧にならないようにする（InMemoryFixtureDefinition.cs と同じ流儀）
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// VO 無効の EditModel における空欄入力の意味論を、コミット済みフィクスチャ
/// （<c>InMemoryFixture.g.cs</c> の <c>blank_probes</c>）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則は「空欄は変換エラーにせず確定値を null にし、null を許すかは基底の必須チェックが決める」＝
/// VO 有効時の <c>ConvertParsedValueObjectInput</c> / <c>ConvertBinaryValueObjectInput</c> と同じ規則が、
/// VO 無効の parse 系列（bool・DateTime・decimal）とバイナリ列にも適用されること。
/// これが破れると、NULL 許容列を画面から NULL へ戻せない（旧: 空欄が <c>TryParseInput("")</c> の失敗＝
/// 変換エラーに化け、バイナリは空配列＝非 null になって必須検証まで素通りした）。
/// </remarks>
public sealed class EditModelBlankInputTests
{
    /// <summary>全列に値が入った Unchanged な BlankProbeEntity を作る。</summary>
    private static BlankProbeEntity BuildEntity() =>
        new()
        {
            ProbeId = 1,
            Flag = true,
            SeenAt = new DateTime(2026, 1, 2, 3, 4, 5),
            Chunk = [0x01, 0x02],
            Rate = 12.5m,
            LoggedAt = new DateTime(2026, 2, 3),
            Stamp = [0x0A, 0x0B],
        };

    /// <summary>ロード済み（Unchanged）の EditModel を作る。</summary>
    private static BlankProbeEditModel BuildLoadedModel()
    {
        var entity = BuildEntity();
        entity.MarkUnchanged();
        return new BlankProbeMapper().CreateEditModel(entity);
    }

    [Fact]
    public void 空欄入力はNULL許容のbool列の確定値をnullへ戻す()
    {
        var model = BuildLoadedModel();

        model.BindingFlag = string.Empty;

        model.Flag.Should().BeNull();
        model.GetErrors(nameof(model.BindingFlag)).Cast<object>().Should().BeEmpty();
        model.Validate().Should().BeTrue();
        model.RowState.Should().Be(RowState.Updated);
    }

    [Fact]
    public void 空欄入力はNULL許容の日時列の確定値をnullへ戻す()
    {
        var model = BuildLoadedModel();

        model.BindingSeenAt = string.Empty;

        model.SeenAt.Should().BeNull();
        model.GetErrors(nameof(model.BindingSeenAt)).Cast<object>().Should().BeEmpty();
        model.Validate().Should().BeTrue();
    }

    [Fact]
    public void 空欄入力はNULL許容のdecimal列の確定値をnullへ戻す()
    {
        var model = BuildLoadedModel();

        model.BindingRate = string.Empty;

        model.Rate.Should().BeNull();
        model.GetErrors(nameof(model.BindingRate)).Cast<object>().Should().BeEmpty();
        model.Validate().Should().BeTrue();
    }

    [Fact]
    public void 空欄入力はNULL許容のバイナリ列の確定値をnullへ戻す()
    {
        var model = BuildLoadedModel();

        model.BindingChunk = string.Empty;

        model.Chunk.Should().BeNull();
        model.GetErrors(nameof(model.BindingChunk)).Cast<object>().Should().BeEmpty();
        model.Validate().Should().BeTrue();
    }

    [Fact]
    public void 非NULLの日時列の空欄は必須エラーになり変換エラーにはならない()
    {
        var model = BuildLoadedModel();

        model.BindingLoggedAt = string.Empty;

        model.LoggedAt.Should().BeNull();
        model.Validate().Should().BeFalse();
        model
            .GetErrors(nameof(model.BindingLoggedAt))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(nameof(model.LoggedAt), nameof(model.LoggedAt)));

        // 値を入れ直せば必須エラーは基底が自分の分だけ取り下げる
        model.BindingLoggedAt = "2026-02-03";
        model.Validate().Should().BeTrue();
        model.GetErrors(nameof(model.BindingLoggedAt)).Cast<object>().Should().BeEmpty();
    }

    [Fact]
    public void 非NULLのバイナリ列の空欄は必須エラーになり空配列で素通りしない()
    {
        var model = BuildLoadedModel();

        model.BindingStamp = string.Empty;

        // 旧挙動は空欄→空配列（非 null）で必須検証を素通りしていた
        model.Stamp.Should().BeNull();
        model.Validate().Should().BeFalse();
        model
            .GetErrors(nameof(model.BindingStamp))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(nameof(model.Stamp), nameof(model.Stamp)));
    }

    [Fact]
    public void 解析不能な入力は従来どおり変換エラーになり空欄で取り下がる()
    {
        var model = BuildLoadedModel();

        model.BindingRate = "abc";

        model.Rate.Should().Be(12.5m, "変換に失敗した入力は確定値を動かさない");
        model
            .GetErrors(nameof(model.BindingRate))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                EditModelMessages.ParseFailed(
                    nameof(model.Rate),
                    nameof(model.Rate),
                    "abc",
                    "decimal"
                )
            );

        // 空欄は変換エラーを取り下げて null を確定する
        model.BindingRate = string.Empty;
        model.Rate.Should().BeNull();
        model.GetErrors(nameof(model.BindingRate)).Cast<object>().Should().BeEmpty();
    }

    [Fact]
    public void 不正なBase64は従来どおり変換エラーになる()
    {
        var model = BuildLoadedModel();

        model.BindingChunk = "@@not-base64@@";

        model.Chunk.Should().Equal([0x01, 0x02], "変換に失敗した入力は確定値を動かさない");
        model
            .GetErrors(nameof(model.BindingChunk))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                EditModelMessages.ParseFailed(
                    nameof(model.Chunk),
                    nameof(model.Chunk),
                    "@@not-base64@@",
                    "byte[]"
                )
            );
    }

    [Fact]
    public void 新規行でも空欄はNULL許容列の確定値をnullのままにする()
    {
        var model = new BlankProbeEditModel();

        model.BindingFlag = string.Empty;
        model.BindingSeenAt = string.Empty;
        model.BindingChunk = string.Empty;
        model.BindingRate = string.Empty;

        model.Flag.Should().BeNull();
        model.SeenAt.Should().BeNull();
        model.Chunk.Should().BeNull();
        model.Rate.Should().BeNull();
        model.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void 新しい行のNOTNULLの文字列は空欄として読み込み必須エラーになる()
    {
        var model = new BlankCodeMapper().CreateEditModel();

        // Entity の初期化子の空文字列は未入力として読む（主キーも同じ）
        model.Code.Should().BeNull();
        model.Label.Should().BeNull();
        model.Validate().Should().BeFalse();
        model
            .GetErrors(nameof(model.BindingCode))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(nameof(model.Code), nameof(model.Code)));
        model
            .GetErrors(nameof(model.BindingLabel))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(nameof(model.Label), nameof(model.Label)));

        model.BindingCode = "A";
        model.BindingLabel = "alpha";
        model.Validate().Should().BeTrue();
    }

    [Fact]
    public void 新しい行の除外していないバイナリ列の空配列は未入力として読み必須エラーになる()
    {
        var model = new BlankProbeMapper().CreateEditModel();

        model.Stamp.Should().BeNull();
        model.Validate().Should().BeFalse();
        model
            .GetErrors(nameof(model.BindingStamp))
            .Cast<string>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(EditModelMessages.Required(nameof(model.Stamp), nameof(model.Stamp)));
    }

    [Fact]
    public void 既存の行の空文字列は値として読み込む()
    {
        var entity = new BlankCodeEntity
        {
            Code = "A",
            Label = string.Empty,
            Note = string.Empty,
        };
        entity.MarkUnchanged();

        var model = new BlankCodeMapper().CreateEditModel(entity);

        model.Label.Should().Be(string.Empty);
        model.Note.Should().Be(string.Empty);
        model.Validate().Should().BeTrue();
    }

    [Fact]
    public void 新しい行の初期値の空文字列はNULL許容の列でも未入力として読みNULLで保存する()
    {
        var mapper = new BlankCodeMapper();
        var entity = mapper.CreateEntity();

        // OnEntityCreated のフックが入れた空文字列と同じ形
        entity.Note = string.Empty;

        var model = mapper.CreateEditModel(entity);
        model.Note.Should().BeNull();

        model.BindingCode = "A";
        model.BindingLabel = "alpha";
        mapper.CreateEntity(model, includeRemoved: true).Note.Should().BeNull();
    }

    [Fact]
    public void 空欄の新しい行どうしは一意な列の重複として報告しない()
    {
        var mapper = new BlankCodeMapper();
        var models = new EditModelCollection<BlankCodeEditModel>([
            mapper.CreateEditModel(),
            mapper.CreateEditModel(),
        ]);

        models.Validate().Should().BeFalse();

        foreach (var model in models)
        {
            model
                .GetErrors(nameof(model.BindingLabel))
                .Cast<string>()
                .Should()
                .ContainSingle("空欄は必須エラーだけで、重複とは数えない")
                .Which.Should()
                .Be(EditModelMessages.Required(nameof(model.Label), nameof(model.Label)));
        }
    }

    [Fact]
    public async Task 空欄の新しい行のValidateUniqueAsyncは空文字列の行と照合しない()
    {
        var store = new InMemoryDataStore();
        var repository = new InMemoryBlankCodeRepository(store);
        await repository.InsertAsync(
            new BlankCodeEntity
            {
                Code = "A",
                Label = string.Empty,
                RowState = RowState.Added,
            },
            TestContext.Current.CancellationToken
        );

        var model = new BlankCodeMapper().CreateEditModel();

        (await model.ValidateUniqueAsync(repository, TestContext.Current.CancellationToken))
            .Should()
            .BeTrue("入力の無い列は照合しない");
        model.GetErrors(nameof(model.BindingLabel)).Cast<object>().Should().BeEmpty();
    }
}
