using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// 共有テーマ辞書 <c>QuickER.Gui.Common/Themes/DialogTheme.xaml</c> が pack URI で読み込め、
/// 各ダイアログが参照する主要キー（パレット・カード・入力欄・ボタン・コンバータ）を提供することを検証する。
/// </summary>
/// <remarks>
/// 4 ダイアログへインラインでコピペされていたスタイルを 1 本化した辞書のため、
/// キー名・型が欠けると載せ替え先の XAML が <c>StaticResource</c> 解決に失敗する。回帰防止として辞書単体を検証する。
/// </remarks>
public class DialogThemeTests
{
    /// <summary>共有テーマ辞書の pack URI</summary>
    private const string ThemeUri =
        "pack://application:,,,/QuickER.Gui.Common;component/Themes/DialogTheme.xaml";

    /// <summary>辞書が pack URI で読み込め、主要キーが期待する型で存在することを検証する</summary>
    [Fact(DisplayName = "DialogTheme 辞書が pack URI で読み込め主要キーを提供する")]
    public void Load_Theme_ProvidesExpectedKeys()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            // pack "application:" スキームの登録は Application 生成時に行われるため先に用意する
            WpfApplicationTestSupport.EnsureApplicationResources();

            var theme = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new ResourceDictionary { Source = new Uri(ThemeUri, UriKind.Absolute) }
            );

            // 汎用コンバータ（3 派閥の統一先）
            theme["BoolToVisibilityConverter"].Should().BeAssignableTo<IValueConverter>();

            // 配色パレット（各ダイアログが共有する SolidColorBrush 群）
            foreach (
                var brushKey in new[]
                {
                    "Accent",
                    "AccentHover",
                    "CardBg",
                    "Divider",
                    "HeaderText",
                    "MutedText",
                    "FieldBorder",
                    "ErrorText",
                    "OkText",
                    "WindowBackground",
                }
            )
            {
                theme[brushKey]
                    .Should()
                    .BeOfType<SolidColorBrush>($"{brushKey} はブラシである前提");
            }

            // アクセント色は既存 4 ダイアログのパレット（#1A73E8）を維持していること
            ((SolidColorBrush)theme["Accent"])
                .Color.Should()
                .Be((Color)ColorConverter.ConvertFromString("#1A73E8"));

            // カード・見出し・ラベル・入力欄・ボタンの各スタイル
            foreach (
                var styleKey in new[]
                {
                    "Card",
                    "SectionHeader",
                    "FieldLabel",
                    "FormInput",
                    "MultilineInput",
                    "PrimaryButton",
                    "SecondaryButton",
                }
            )
            {
                theme[styleKey].Should().BeOfType<Style>($"{styleKey} はスタイルである前提");
            }

            // ボタンスタイルの対象型が Button であること
            ((Style)theme["PrimaryButton"])
                .TargetType.Should()
                .Be(typeof(Button));
            ((Style)theme["SecondaryButton"]).TargetType.Should().Be(typeof(Button));
            ((Style)theme["Card"]).TargetType.Should().Be(typeof(Border));
        });
    }
}
