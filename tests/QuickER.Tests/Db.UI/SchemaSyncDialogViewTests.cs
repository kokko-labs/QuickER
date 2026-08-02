using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
using QuickER.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// <see cref="SchemaSyncDialog"/> の BAML 読み込み（InitializeComponent）が成功することを検証する。
/// 共有テーマ辞書（DialogTheme.xaml）のマージが pack URI で解決でき、
/// PrimaryButton / SecondaryButton などの StaticResource 参照が漏れなく解決することを保証する。
/// </summary>
public class SchemaSyncDialogViewTests
{
    /// <summary>SQL Server プロバイダ（同期スクリプト生成に用いる）</summary>
    private static readonly IDatabaseProvider Provider = new SqlServerProvider();

    /// <summary>STA スレッド上でダイアログを構築し、InitializeComponent が例外を投げないことを検証する</summary>
    [Fact(DisplayName = "SchemaSyncDialog の InitializeComponent が例外を投げない")]
    public void InitializeComponent_DoesNotThrow()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            // 差分は空・DB 接続もしない（Window を Show しないため Loaded の Refresh は発火しない）
            var viewModel = new SchemaSyncDialogViewModel(
                Provider,
                new DbConnectionSettings(),
                [],
                []
            );

            // BAML ロードは並列テストと競合しないよう直列化する
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new SchemaSyncDialog(viewModel)
            );

            dialog.ViewModel.Should().BeSameAs(viewModel);
        });
    }
}
