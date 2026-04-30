using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;
using Microsoft.Data.SqlClient;

namespace ERDesigner.ViewModels;

/// <summary>
/// SQL Server 接続ダイアログ用 ViewModel。
/// </summary>
public partial class SqlConnectionDialogViewModel : ObservableObject
{
    /// <summary>サーバ名。</summary>
    [ObservableProperty] private string _server = "localhost";
    /// <summary>データベース名。</summary>
    [ObservableProperty] private string _database = string.Empty;
    /// <summary>選択中の認証方式。</summary>
    [ObservableProperty] private SqlAuthMode _authMode = SqlAuthMode.Windows;
    /// <summary>SQL/Azure AD のユーザー名。</summary>
    [ObservableProperty] private string _userId = string.Empty;
    /// <summary>SQL/Azure AD のパスワード。</summary>
    [ObservableProperty] private string _password = string.Empty;
    /// <summary>サーバ証明書を信頼するか。</summary>
    [ObservableProperty] private bool _trustServerCertificate = true;
    /// <summary>テスト結果や状態メッセージ。</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;
    /// <summary>接続テスト中かどうか。</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>OK ボタン押下時の確定設定。null なら未確定。</summary>
    public SqlConnectionSettings? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクション (View が注入)。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>SQL 認証 / Azure AD のときに UserId/Password 入力欄を表示するか。</summary>
    public bool ShowCredentials => AuthMode != SqlAuthMode.Windows;

    partial void OnAuthModeChanged(SqlAuthMode value) => OnPropertyChanged(nameof(ShowCredentials));

    /// <summary>現在の入力から <see cref="SqlConnectionSettings"/> を構築します。</summary>
    public SqlConnectionSettings ToSettings() => new()
    {
        Server = Server,
        Database = Database,
        AuthMode = AuthMode,
        UserId = UserId,
        Password = Password,
        TrustServerCertificate = TrustServerCertificate
    };

    /// <summary>接続テストを行います。</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = "接続中...";
        try
        {
            var s = ToSettings();
            await using var conn = new SqlConnection(s.Build());
            await conn.OpenAsync().ConfigureAwait(true);
            StatusMessage = $"接続成功: {conn.ServerVersion}";
        }
        catch (Exception ex)
        {
            StatusMessage = "接続失敗: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>OK ボタン: 入力を確定してダイアログを閉じます。</summary>
    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Database))
        {
            StatusMessage = "サーバ名とデータベース名を入力してください。";
            return;
        }
        Result = ToSettings();
        CloseAction?.Invoke(true);
    }

    /// <summary>キャンセルボタン。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke(false);
    }
}
