using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using QuickER.Views;
using static QuickER.Tests.Views.WpfApplicationTestSupport;

namespace QuickER.Tests.Views;

/// <summary>
/// <see cref="PasswordBoxBehavior"/> の双方向同期を STA スレッド上で検証する。
/// 特に「バインド先の初期値が空文字（添付プロパティの初期状態と同値）でも
/// PasswordChanged の購読が確立される」ことは回帰防止の要（購読漏れだと入力が VM へ届かない）。
/// </summary>
public class PasswordBoxBehaviorTests
{
    /// <summary>バインディングソースとして使う最小の INotifyPropertyChanged 実装</summary>
    private sealed class Source : INotifyPropertyChanged
    {
        private string _password = string.Empty;

        /// <summary>同期対象のパスワード</summary>
        public string Password
        {
            get => _password;
            set
            {
                if (_password == value)
                {
                    return;
                }

                _password = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Password)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>PasswordBox に <see cref="PasswordBoxBehavior.BoundPasswordProperty"/> のバインドを張る</summary>
    private static void Bind(PasswordBox box, Source source) =>
        BindingOperations.SetBinding(
            box,
            PasswordBoxBehavior.BoundPasswordProperty,
            new Binding(nameof(Source.Password)) { Source = source, Mode = BindingMode.TwoWay }
        );

    [Fact(DisplayName = "VM の初期値が空文字でも PasswordBox への入力が VM へ伝わる")]
    public void EmptyInitialValue_TypedPasswordFlowsToViewModel()
    {
        RunSta(() =>
        {
            var source = new Source();
            var box = new PasswordBox();
            Bind(box, source);

            // ユーザー入力に相当する Password 変更（PasswordChanged が発火する）
            box.Password = "secret";

            source.Password.Should().Be("secret");
        });
    }

    [Fact(DisplayName = "VM 側の初期値と変更が PasswordBox へ反映される")]
    public void ViewModelChange_FlowsToPasswordBox()
    {
        RunSta(() =>
        {
            var source = new Source { Password = "initial" };
            var box = new PasswordBox();
            Bind(box, source);

            box.Password.Should().Be("initial");

            source.Password = "updated";

            box.Password.Should().Be("updated");
        });
    }
}
