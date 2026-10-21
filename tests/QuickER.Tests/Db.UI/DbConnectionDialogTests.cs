using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// <see cref="DbConnectionDialog"/> の BAML 読み込み（InitializeComponent）が成功することと、
/// 認証方式のコンボが表示名つきの選択肢を出すことを検証する（XAML の参照の漏れ・束縛の書き損じの回帰防止）。
/// </summary>
public class DbConnectionDialogTests
{
    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "DbConnectionDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                // BAML 内の {StaticResource BoolToVisibilityConverter} は App レベル定義のため、
                // 実アプリと同様にアプリケーションリソースとして供給する（本検証の対象外の依存）。
                // 生成は共有ヘルパーで直列化し、並列テストとの Application 二重生成競合を防ぐ
                WpfApplicationTestSupport.EnsureApplicationResources();

                var registry = new DatabaseProviderRegistry(
                    new IDatabaseProvider[] { new SqlServerProvider() }
                );
                var viewModel = new DbConnectionDialogViewModel(registry);

                // InitializeComponent がここで実行される。XAML が参照する型・リソースの解決に失敗すると
                // XamlParseException が送出される。
                // BAML ロードは並列テストと競合しないよう直列化する
                _ = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new DbConnectionDialog(viewModel)
                );
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

    /// <summary>
    /// 認証方式のコンボが VM の表示名つきの選択肢（<see cref="DbConnectionDialogViewModel.AuthModes"/>）を出し、
    /// 選んだ値が <see cref="DbConnectionDialogViewModel.AuthMode"/> へ戻ることを検証する
    /// </summary>
    [Fact(DisplayName = "認証方式のコンボは表示名を出し、選んだ値を AuthMode へ戻す")]
    public void AuthModeCombo_ShowsDisplayNamesAndWritesBackTheMode()
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                WpfApplicationTestSupport.EnsureApplicationResources();

                var registry = new DatabaseProviderRegistry(
                    new IDatabaseProvider[] { new SqlServerProvider() }
                );
                var viewModel = new DbConnectionDialogViewModel(registry);
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new DbConnectionDialog(viewModel)
                );
                // 束縛の解決はディスパッチャへ後回しになるので、流し切ってから読む
                WpfApplicationTestSupport.DoEvents();
                var combo = Descendants(dialog)
                    .OfType<ComboBox>()
                    .Single(c => ReferenceEquals(c.ItemsSource, viewModel.AuthModes));

                combo
                    .DisplayMemberPath.Should()
                    .Be(nameof(DbConnectionDialogViewModel.AuthModeListItem.Display));
                combo
                    .SelectedValuePath.Should()
                    .Be(nameof(DbConnectionDialogViewModel.AuthModeListItem.Mode));
                combo.SelectedValue.Should().Be(viewModel.AuthMode);

                combo.SelectedValue = DbAuthMode.AzureAd;
                WpfApplicationTestSupport.DoEvents();

                viewModel.AuthMode.Should().Be(DbAuthMode.AzureAd);
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

    /// <summary>論理ツリーの子孫を列挙する</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
