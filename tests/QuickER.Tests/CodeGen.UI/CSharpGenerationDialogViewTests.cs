using System.IO;
using System.Windows;
using System.Windows.Data;
using AwesomeAssertions;
using QuickER.CodeGen.UI;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="CSharpGenerationDialog" /> の BAML 読み込み（InitializeComponent）が例外を投げず、
/// 層別フォルダ出力の入力欄が ViewModel の表示フラグどおりに現れ・畳まれることを検証する
/// （XAML の StaticResource 参照漏れ・Visibility 配線の回帰防止）。
/// </summary>
public class CSharpGenerationDialogViewTests
{
    /// <summary>一時フォルダのストアで ViewModel を生成する（実 %APPDATA% を汚さない）</summary>
    private static CSharpGenerationDialogViewModel CreateViewModel(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return new CSharpGenerationDialogViewModel(new CSharpGenerationSettingsStore(folder));
    }

    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "CSharpGenerationDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var viewModel = CreateViewModel(out var folder);

            try
            {
                // BAML ロードは並列テストと競合しないよう直列化する
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new CSharpGenerationDialog(viewModel)
                );

                dialog.ViewModel.Should().BeSameAs(viewModel);
            }
            finally
            {
                DeleteFolder(folder);
            }
        });
    }

    /// <summary>
    /// 層フォルダ欄は層別出力 ON のときだけ表示され、サーバー層の欄はさらに
    /// リモートサービス生成 ON のときだけ表示されることを検証する
    /// </summary>
    [Fact(DisplayName = "層フォルダ欄は層別出力 ON・サーバー層はリモートサービス ON で表示される")]
    public void LayerDirectoryFields_FollowViewModelVisibilityFlags()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var viewModel = CreateViewModel(out var folder);

            try
            {
                // BAML ロードは並列テストと競合しないよう直列化する
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new CSharpGenerationDialog(viewModel)
                );

                // Window を Show せずに検証するため論理ツリーから Visibility バインド先で探す
                var layerPanel = FindByVisibilityBinding(
                    dialog,
                    nameof(CSharpGenerationDialogViewModel.ShowLayerDirectories)
                );
                var serverField = FindByVisibilityBinding(
                    dialog,
                    nameof(CSharpGenerationDialogViewModel.ShowServerLayerDirectory)
                );

                layerPanel.Should().NotBeNull("層フォルダ欄のブロックが XAML に存在する前提");
                serverField.Should().NotBeNull("サーバー層フォルダ欄が XAML に存在する前提");

                // 未表示のウィンドウでは初期バインドがまだ適用されていないため、まず反映を待つ
                PumpBindings();

                layerPanel!
                    .Visibility.Should()
                    .Be(Visibility.Collapsed, "既定（層別出力 OFF）では畳まれている");

                viewModel.LayeredOutput = true;
                PumpBindings();

                layerPanel.Visibility.Should().Be(Visibility.Visible, "層別出力 ON で表示される");
                serverField!
                    .Visibility.Should()
                    .Be(Visibility.Collapsed, "リモートサービスを生成しない構成では隠す");

                viewModel.GenerateRemoteServices = true;
                PumpBindings();

                serverField
                    .Visibility.Should()
                    .Be(Visibility.Visible, "リモートサービス生成 ON で表示される");
            }
            finally
            {
                DeleteFolder(folder);
            }
        });
    }

    /// <summary>
    /// 生成ファイルプレビューがスクロール可能な欄（横=Auto・縦=Auto＋高さ上限）に包まれていることを検証する
    /// （層フォルダ＋名前空間で行が長くなり、右端で見切れていた回帰の防止）
    /// </summary>
    [Fact(DisplayName = "生成ファイルプレビューは横スクロール可能な欄に包まれている")]
    public void PreviewFilesList_IsWrappedInScrollViewer()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var viewModel = CreateViewModel(out var folder);

            try
            {
                // BAML ロードは並列テストと競合しないよう直列化する
                var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                    new CSharpGenerationDialog(viewModel)
                );

                var previewList = FindByItemsSourceBinding(
                    dialog,
                    nameof(CSharpGenerationDialogViewModel.PreviewFiles)
                );
                previewList.Should().NotBeNull("プレビューの ItemsControl が XAML に存在する前提");

                var scrollViewer = FindAncestor<System.Windows.Controls.ScrollViewer>(previewList!);
                scrollViewer.Should().NotBeNull("プレビューは ScrollViewer に包まれている前提");
                scrollViewer!
                    .HorizontalScrollBarVisibility.Should()
                    .Be(
                        System.Windows.Controls.ScrollBarVisibility.Auto,
                        "長い行（層フォルダ＋名前空間）を横スクロールで読めるようにする"
                    );
                scrollViewer
                    .VerticalScrollBarVisibility.Should()
                    .Be(System.Windows.Controls.ScrollBarVisibility.Auto);
                scrollViewer
                    .MaxHeight.Should()
                    .NotBe(double.PositiveInfinity, "ファイル数が多くても欄内スクロールに収める");
            }
            finally
            {
                DeleteFolder(folder);
            }
        });
    }

    /// <summary>ItemsSource を指定パスへバインドしている ItemsControl を論理ツリーから探す</summary>
    private static System.Windows.Controls.ItemsControl? FindByItemsSourceBinding(
        DependencyObject root,
        string path
    )
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependency)
            {
                continue;
            }

            if (
                dependency is System.Windows.Controls.ItemsControl items
                && BindingOperations.GetBinding(
                    items,
                    System.Windows.Controls.ItemsControl.ItemsSourceProperty
                )
                    is { } binding
                && binding.Path?.Path == path
            )
            {
                return items;
            }

            if (FindByItemsSourceBinding(dependency, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>論理ツリーを親方向へ辿り、指定型の祖先を探す</summary>
    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        var current = LogicalTreeHelper.GetParent(element);

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>バインディングの反映（DataBind 優先度のディスパッチ）を待つ</summary>
    private static void PumpBindings() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.DataBind
        );

    /// <summary>Visibility を指定パスへバインドしている要素を論理ツリーから探す</summary>
    private static FrameworkElement? FindByVisibilityBinding(DependencyObject root, string path)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependency)
            {
                continue;
            }

            if (
                dependency is FrameworkElement element
                && BindingOperations.GetBinding(element, UIElement.VisibilityProperty)
                    is { } binding
                && binding.Path?.Path == path
            )
            {
                return element;
            }

            if (FindByVisibilityBinding(dependency, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>一時設定フォルダを後始末する</summary>
    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
