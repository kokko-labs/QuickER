using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成 EditModel の Binding プロパティを、実 WPF のバインディングエンジン越しに検証する（STA）。
/// </summary>
/// <remarks>
/// <para>
/// 第 10 次 A-1（setter 本体の基底集約）の非退行網のうち、既存の EditModel テスト群（POCO）が担当しない
/// 2 層をここで押さえる: (1) **面の網**＝実バインディング越しの最終状態（<c>TextBox.Text</c>・
/// <c>Validation.GetErrors</c>＝ユーザーが見る面）。(2) **機構の網**＝削り込み等値時の再通知イベント。
/// (2) を STA 側で mutation 検出できない理由は該当テストのコメントを参照（in-process の WPF は
/// UpdateSource 後の再読込でも表示が直るため、通知を外しても面の網は緑のまま＝実 GUI のタイピング経路が
/// 頼る通知はイベント発行の事実で固定するしかない）。
/// </para>
/// </remarks>
public sealed class EditModelWpfBindingTests
{
    /// <summary>TextBox を指定プロパティへ双方向（PropertyChanged 即時）でバインドする。</summary>
    private static TextBox BindTextBox(object model, string path)
    {
        var box = new TextBox { DataContext = model };
        box.SetBinding(
            TextBox.TextProperty,
            new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            }
        );
        return box;
    }

    [Fact(
        DisplayName = "[WPF実バインド] 削り込みで等値になった入力は、画面の表示だけが正規化値へ戻る"
    )]
    public void 削り込み等値時に表示が正規化値へ戻る()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var model = new CustomerEditModel();
            model.BindingName = "田中";

            var box = BindTextBox(model, nameof(CustomerEditModel.BindingName));
            box.Text.Should().Be("田中", "初期表示は確定済みの入力文字列");

            // ユーザーが前後空白付きで同じ値を入力する＝正規化すると既存値と等値。
            // プログラム的な Text 代入はバインディングの別経路（ターゲット変更後の再転送）で表示が直って
            // しまい再通知機構を検証できないため、実タイピングと同じ編集経路（TextContainer 経由）で書く
            box.SelectAll();
            box.SelectedText = "  田中  ";

            // モデルは正規化値のまま・画面（ユーザーが見る面）も空白付きのままにならず正規化値へ戻る
            model.BindingName.Should().Be("田中");
            box.Text.Should()
                .Be(
                    "田中",
                    "同値でも表示が空白付きのままでは「入力が受理されていない」ように見える＝再通知でターゲットを書き戻す"
                );
        });
    }

    [Fact(
        DisplayName = "[再通知の機構] 削り込み等値の入力は、値が変わらなくても Binding プロパティの変更通知を発行する"
    )]
    public void 削り込み等値時に再通知イベントが発行される()
    {
        // 機構そのもの（AcceptBindingInput の同値時 OnPropertyChanged）は POCO のイベントで固定する。
        // in-process の WPF バインディングは「UpdateSource 後のソース再読込」でも表示が直ってしまい、
        // 再通知を外しても上の STA テストは緑のままになる＝実 GUI のタイピング経路だけが頼る通知を
        // イベントの発行事実で名指しする（STA 側は「面の最終状態」の網・こちらは「機構」の網）。
        var model = new CustomerEditModel();
        model.BindingName = "田中";

        var notified = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CustomerEditModel.BindingName))
            {
                notified++;
            }
        };

        model.BindingName = "  田中  ";

        model.BindingName.Should().Be("田中", "格納値は正規化済みのまま変わらない");
        notified
            .Should()
            .BeGreaterThan(0, "表示をターゲットへ書き戻させるための再通知が発行される");
    }

    [Fact(DisplayName = "[WPF実バインド] 変換エラーが Validation 面に出て、正しい入力で消える")]
    public void 変換エラーがValidation面に出て解消で消える()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var model = new CustomerEditModel();
            var box = BindTextBox(model, nameof(CustomerEditModel.BindingCustomerId));

            // 解析できない入力 → INotifyDataErrorInfo 経由で Validation 面に出る
            box.Text = "abc";

            Validation.GetHasError(box).Should().BeTrue("解析エラーは画面のエラー表示に乗る");
            var surfaced = Validation.GetErrors(box).Single().ErrorContent;
            var reported = model
                .GetErrors(nameof(CustomerEditModel.BindingCustomerId))
                .Cast<object>()
                .Single();
            surfaced.Should().Be(reported, "画面が示すエラーはモデルが報告したエラーそのもの");

            // 正しい入力 → エラーが消え、確定値が入る
            box.Text = "42";

            Validation.GetHasError(box).Should().BeFalse("解消された変換エラーは画面からも消える");
            model.CustomerId.Should().NotBeNull();
            model.CustomerId!.Value.Should().Be(42);
        });
    }
}
