using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using QuickER.ViewModels;
using QuickER.Views;

namespace QuickER.Tests.Views;

/// <summary>
/// <see cref="SqlConnectionDialog"/> の BAML 読み込み（InitializeComponent）が成功することを検証する。
/// XAML の <c>ObjectDataProvider</c> が <c>QuickER.SqlServer.SqlAuthMode</c> を正しい名前空間／アセンブリで
/// 解決できることを保証する（型の移動に伴う XAML 参照漏れの回帰防止）。
/// </summary>
public class SqlConnectionDialogTests
{
    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "SqlConnectionDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                // BAML 内の {StaticResource BoolToVisibilityConverter} は App レベル定義のため、
                // 実アプリと同様にアプリケーションリソースとして供給する（本検証の対象外の依存）。
                if (Application.Current is null)
                {
                    _ = new Application();
                }

                if (!Application.Current!.Resources.Contains("BoolToVisibilityConverter"))
                {
                    Application.Current.Resources.Add(
                        "BoolToVisibilityConverter",
                        new BooleanToVisibilityConverter()
                    );
                }

                var viewModel = new SqlConnectionDialogViewModel();

                // InitializeComponent がここで実行される。SqlAuthMode の型解決に失敗すると
                // XamlParseException が送出される。
                _ = new SqlConnectionDialog(viewModel);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        captured.Should().BeNull();
    }
}
