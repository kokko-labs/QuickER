using System;
using System.Linq;
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
}
