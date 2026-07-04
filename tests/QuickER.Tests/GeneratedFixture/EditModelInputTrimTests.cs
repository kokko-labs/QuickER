using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using Xunit;
using static QuickER.Tests.Views.WpfApplicationTestSupport;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成 EditModel の「バインディング入力文字列の前後空白トリム」挙動を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 対象はコミット済みフィクスチャ（<c>GeneratedFixture.g.cs</c>）の生成型。全バインディング
/// プロパティのセッター入口で入力を <see cref="string.Trim()"/> で正規化する（常時 ON）。
/// ロード・復元中（IsLoading / IsReverting）は素通しし、元 Entity の鏡像を保つ。
/// </para>
/// <para>
/// この図の文字列列はすべて値オブジェクト（VO）対象のため、空白のみ入力は
/// <c>string.IsNullOrEmpty</c> 分岐で確定値 null になる。VO を通さない「非 null 文字列列→空文字」
/// 経路はこの図には存在しない（VO 有効フィクスチャのため）。
/// </para>
/// </remarks>
public sealed class EditModelInputTrimTests
{
    [Theory(
        DisplayName = "前後の空白・タブ・改行・全角スペースはトリムされ、中間の空白・改行は保持される"
    )]
    [InlineData("  abc  ", "abc")]
    [InlineData("\tabc\t", "abc")]
    [InlineData("\r\nabc\r\n", "abc")]
    [InlineData("　abc　", "abc")] // 全角スペース
    [InlineData("  a b\nc  ", "a b\nc")] // 中間の空白・改行は保持
    public void 前後空白はトリムされ中間は保持される(string input, string expected)
    {
        var model = new CustomerEditModel { BindingName = input };

        model.BindingName.Should().Be(expected);
        model.Name!.Value.Should().Be(expected);
    }

    [Fact(
        DisplayName = "ロード時は素通し: 前後空白付きの Entity 値がバインディング・確定値とも保持され RowState は Unchanged のまま"
    )]
    public void ロード時は正規化されず空白が保持される()
    {
        var entity = new CustomerEntity
        {
            CustomerId = CustomerIdValue.Create(1),
            Name = NameValue.Create("  spaced name  "),
            IsActive = IsActiveValue.Create(true),
        };
        entity.RowState = RowState.Unchanged;

        var model = new CustomerMapper().CreateEditModel(entity);

        // ロード（IsLoading=true）中はトリムされないため空白が残る
        model.BindingName.Should().Be("  spaced name  ");
        model.Name!.Value.Should().Be("  spaced name  ");

        // ロードは確定値変更を Updated へ昇格させない（元 Entity の鏡像）
        model.RowState.Should().Be(RowState.Unchanged);
    }

    [Fact(DisplayName = "空白のみ入力: nullable 文字列列（VO）は確定値 null になる")]
    public void 空白のみ入力は確定値nullになる()
    {
        var model = new CustomerEditModel { BindingName = "abc" };
        model.Name.Should().NotBeNull();

        model.BindingName = "   \t 　  ";

        model.BindingName.Should().BeEmpty();
        model.Name.Should().BeNull();
        model.HasErrors.Should().BeFalse();
    }

    [Fact(
        DisplayName = "null 入力は落ちず未入力扱いになる（ComboBox 等が null を書き込むケースとの互換）"
    )]
    public void null入力は未入力扱いになる()
    {
        var model = new CustomerEditModel { BindingName = "abc" };

        model.BindingName = null!;

        model.BindingName.Should().BeNull();
        model.Name.Should().BeNull();
        model.HasErrors.Should().BeFalse();
    }

    [Fact(
        DisplayName = "数値列に前後空白付き \" 12 \" を入力するとパース成功し確定値 12・エラーなし"
    )]
    public void 前後空白付き数値はパース成功する()
    {
        var model = new CustomerEditModel { BindingBalance = " 12 " };

        model.BindingBalance.Should().Be("12");
        model.Balance!.Value.Should().Be(12m);
        model.HasErrors.Should().BeFalse();
    }

    [Fact(
        DisplayName = "スナップバック: 確定値と同値になるトリム入力で PropertyChanged が発火しバインディング文字列は正規化値のまま"
    )]
    public void 表示スナップバックでPropertyChangedが発火する()
    {
        var model = new CustomerEditModel { BindingName = "abc" };

        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // "abc " はトリムで "abc" となり既存値と一致 → SetProperty は false を返すが表示は戻す
        model.BindingName = "abc ";

        raised.Should().Contain(nameof(CustomerEditModel.BindingName));
        model.BindingName.Should().Be("abc");
    }

    [Fact(DisplayName = "CustomizeInputNormalization で除外した列はトリムされない")]
    public void 除外した列はトリムされない()
    {
        // CustomerProfileEditModel は BindingBio の正規化を無効化している（下部の partial 実装）
        var model = new CustomerProfileEditModel { BindingBio = "  keep spaces  " };

        // 除外列は生の値が残る
        model.BindingBio.Should().Be("  keep spaces  ");
        model.Bio!.Value.Should().Be("  keep spaces  ");

        // 同クラスの別列（除外していない）は通常どおりトリムされる
        var trimmed = new CustomerProfileEditModel { BindingCustomerId = " 7 " };
        trimmed.BindingCustomerId.Should().Be("7");
        trimmed.CustomerId!.Value.Should().Be(7);
    }

    [Fact(
        DisplayName = "WPF Binding 検証: TwoWay/LostFocus の TextBox は UpdateSource 後に正規化値へ表示が戻る（STA）"
    )]
    public void WpfBinding_UpdateSource後にTextBoxが正規化値へ戻る()
    {
        RunSta(() =>
        {
            var model = new CustomerEditModel();

            var box = new TextBox();
            var binding = new Binding(nameof(CustomerEditModel.BindingName))
            {
                Source = model,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            };
            BindingOperations.SetBinding(box, TextBox.TextProperty, binding);

            var expression = box.GetBindingExpression(TextBox.TextProperty)!;

            // 画面入力に相当: 空白付きテキストを設定 → LostFocus 相当で UpdateSource
            box.Text = "  hello  ";
            expression.UpdateSource();

            // ソース側は正規化済み
            model.BindingName.Should().Be("hello");

            // TextBox.Text が正規化値へ戻っていること（スナップバックが表示へ反映される）
            box.Text.Should().Be("hello");
        });
    }
}

/// <summary>
/// <see cref="CustomerProfileEditModel"/> の入力正規化を列単位で調整する partial 実装（テスト用）。
/// BindingBio のみトリムを無効化する。
/// </summary>
public partial class CustomerProfileEditModel
{
    /// <summary>BindingBio だけ正規化（トリム）を無効化し、生の入力値を保持する</summary>
    partial void CustomizeInputNormalization(
        string propertyName,
        string rawValue,
        ref string normalizedValue
    )
    {
        if (propertyName == nameof(BindingBio))
        {
            normalizedValue = rawValue;
        }
    }
}
