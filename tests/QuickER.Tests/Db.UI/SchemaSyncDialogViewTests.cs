using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
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

    /// <summary>空の取込結果をすぐ返すフェイクインポーター（表示時の再計算で DB へ接続しない）</summary>
    private sealed class EmptySchemaImporter : ISchemaImporter
    {
        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new SchemaImportResult());
    }

    /// <summary>取込だけをフェイクへ差し替えた SQL Server プロバイダ</summary>
    private sealed class OfflineProvider : IDatabaseProvider
    {
        private readonly SqlServerProvider _inner = new();

        public string Name => _inner.Name;

        public string DisplayName => _inner.DisplayName;

        public int? DefaultPort => _inner.DefaultPort;

        public ISchemaImporter SchemaImporter { get; } = new EmptySchemaImporter();

        public IColumnTypeMapper TypeMapper => _inner.TypeMapper;

        public ITypeCatalog TypeCatalog => _inner.TypeCatalog;

        public ISyncScriptBuilder SyncScriptBuilder => _inner.SyncScriptBuilder;

        public SyncDialectCapabilities SyncCapabilities => _inner.SyncCapabilities;

        public ISchemaSyncExecutor SyncExecutor => _inner.SyncExecutor;

        public IDdlGenerator DdlGenerator => _inner.DdlGenerator;

        public string BuildConnectionString(DbConnectionSettings settings) => "Offline";
    }

    /// <summary>
    /// 同期の実行中は × ボタン（Window.Close）でも閉じない（1-9 #9）。
    /// </summary>
    /// <remarks>閉じると呼び出し側へ制御が戻り、DDL の実行がダイアログの外で走り続ける</remarks>
    [Fact(DisplayName = "SchemaSyncDialog は同期の実行中は閉じない")]
    public void Close_WhileExecuting_IsCancelled()
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var viewModel = new SchemaSyncDialogViewModel(
                new OfflineProvider(),
                new DbConnectionSettings(),
                [],
                []
            );
            var dialog = WpfApplicationTestSupport.LoadXamlComponent(() =>
                new SchemaSyncDialog(viewModel)
            );
            dialog.ShowActivated = false;
            dialog.Show();

            try
            {
                viewModel.IsExecuting = true;
                dialog.Close();
                dialog.IsVisible.Should().BeTrue("実行中は閉じない");

                viewModel.IsExecuting = false;
                dialog.Close();
                dialog.IsVisible.Should().BeFalse("実行が終われば閉じられる");
            }
            finally
            {
                viewModel.IsExecuting = false;

                if (dialog.IsVisible)
                {
                    dialog.Close();
                }
            }
        });
    }
}
