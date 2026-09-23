using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

// 生成フィクスチャ自身の namespace に置く＝他フィクスチャの同名型と曖昧にならないようにする
namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// <c>HasErrors</c> の PropertyChanged 通知を、コミット済みフィクスチャ（<c>InMemoryFixture.g.cs</c> の
/// BlankProbeEditModel）の実型で検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 固定する規則: エラーの増減（<c>ErrorsChanged</c> が上がる全箇所）と対で <c>HasErrors</c> の
/// PropertyChanged も上がる。これが破れると、保存ボタンの IsEnabled のような <c>HasErrors</c> への
/// バインディングがエラーの発生・解消に追従しない（<c>IsAdded</c> / <c>HasChanges</c> は通知するのに
/// <c>HasErrors</c> だけ通知しない非対称だった）。
/// </remarks>
public sealed class EditModelHasErrorsNotificationTests
{
    [Fact]
    public void エラーの発生と解消でHasErrorsのPropertyChangedが上がる()
    {
        var model = new BlankProbeEditModel();
        var notified = new List<string?>();
        model.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // 変換エラーの発生
        model.BindingRate = "abc";

        model.HasErrors.Should().BeTrue();
        notified.Should().Contain(nameof(model.HasErrors), "エラーの発生で通知される");

        // 空欄で変換エラーが取り下がる
        notified.Clear();
        model.BindingRate = string.Empty;

        model.HasErrors.Should().BeFalse();
        notified.Should().Contain(nameof(model.HasErrors), "エラーの解消でも通知される");
    }

    [Fact]
    public void 重複エラーの登録でもHasErrorsのPropertyChangedが上がる()
    {
        var model = new BlankProbeEditModel();
        var notified = new List<string?>();
        model.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        model.SetDuplicateError(
            nameof(model.BindingRate),
            "duplicate",
            DuplicateErrorSource.Siblings
        );

        model.HasErrors.Should().BeTrue();
        notified
            .Should()
            .Contain(nameof(model.HasErrors), "重複エラーのストアも HasErrors の合成対象");
    }
}
