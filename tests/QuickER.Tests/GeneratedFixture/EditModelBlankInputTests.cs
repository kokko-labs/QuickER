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
/// <para>
/// 空欄で NULL 許容列が null になる・非 NULL 列が必須エラーになる・解析できない入力が変換エラーになる、の決まった形の表明は <see cref="EditModelScenarioTests"/> の em-blank-null・em-blank-required・em-blank-parse の表にある。ここに残すのは、手順や準備が行ごとに違うテスト。
/// </para>
/// </remarks>
public sealed class EditModelBlankInputTests
{
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
