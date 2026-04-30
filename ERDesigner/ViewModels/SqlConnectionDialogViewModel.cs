using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;
using Microsoft.Data.SqlClient;

namespace ERDesigner.ViewModels;

/// <summary>
/// SQL Server 接続ダイアログ用 ViewModel。
/// 接続プロファイルの保存・選択・削除に対応します。
/// </summary>
public partial class SqlConnectionDialogViewModel : ObservableObject
{
    private readonly SqlConnectionProfileStore _store;

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
    /// <summary>パスワードも DPAPI で暗号化保存するか (SQL/Azure AD 認証のときのみ意味あり)。</summary>
    [ObservableProperty] private bool _savePassword;
    /// <summary>テスト結果や状態メッセージ。</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;
    /// <summary>接続テスト中かどうか。</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>保存済みプロファイル一覧。</summary>
    public ObservableCollection<SqlConnectionProfile> Profiles { get; } = new();

    /// <summary>選択中のプロファイル。</summary>
    [ObservableProperty] private SqlConnectionProfile? _selectedProfile;

    /// <summary>名前を付けて保存するときの名前 (ComboBox の編集テキストと同期)。</summary>
    [ObservableProperty] private string _profileName = string.Empty;

    /// <summary>OK ボタン押下時の確定設定。null なら未確定。</summary>
    public SqlConnectionSettings? Result { get; private set; }

    /// <summary>ダイアログを閉じるためのアクション (View が注入)。</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>SQL 認証 / Azure AD のときに UserId/Password 入力欄を表示するか。</summary>
    public bool ShowCredentials => AuthMode != SqlAuthMode.Windows;

    /// <summary>新しい ViewModel を生成します。</summary>
    /// <param name="store">プロファイル保存ストア (省略時は既定パスを使用)。</param>
    public SqlConnectionDialogViewModel(SqlConnectionProfileStore? store = null)
    {
        _store = store ?? new SqlConnectionProfileStore();
        ReloadProfiles(selectFirst: true);
    }

    private void ReloadProfiles(bool selectFirst)
    {
        Profiles.Clear();
        foreach (var p in _store.LoadAll())
            Profiles.Add(p);
        if (selectFirst && Profiles.Count > 0)
            SelectedProfile = Profiles[0];
    }

    partial void OnAuthModeChanged(SqlAuthMode value) => OnPropertyChanged(nameof(ShowCredentials));

    /// <summary>プロファイル選択時、入力欄に値を反映します。</summary>
    partial void OnSelectedProfileChanged(SqlConnectionProfile? value)
    {
        if (value is null) return;
        Server = value.Server;
        Database = value.Database;
        AuthMode = value.AuthMode;
        UserId = value.UserId;
        TrustServerCertificate = value.TrustServerCertificate;
        SavePassword = value.SavePassword;
        Password = value.SavePassword ? _store.LoadPassword(value.Id) : string.Empty;
        ProfileName = value.Name;
        StatusMessage = $"プロファイル '{value.Name}' を読み込みました。";
    }

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

    /// <summary>現在の入力内容をプロファイルとして保存します。</summary>
    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            StatusMessage = "保存名を入力してください。";
            return;
        }

        // 既存名と一致するなら同じ Id を使う (上書き)
        var existing = Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, ProfileName, StringComparison.OrdinalIgnoreCase));

        var profile = new SqlConnectionProfile
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = ProfileName.Trim(),
            Server = Server,
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            TrustServerCertificate = TrustServerCertificate,
            SavePassword = SavePassword
        };

        _store.Upsert(profile, Password);
        ReloadProfiles(selectFirst: false);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = $"プロファイル '{profile.Name}' を保存しました。";
    }

    /// <summary>選択中プロファイルを削除します。</summary>
    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "削除するプロファイルを選択してください。";
            return;
        }

        var ans = System.Windows.MessageBox.Show(
            $"プロファイル '{SelectedProfile.Name}' を削除します。よろしいですか？",
            "確認",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (ans != System.Windows.MessageBoxResult.OK) return;

        var name = SelectedProfile.Name;
        _store.Delete(SelectedProfile.Id);
        ReloadProfiles(selectFirst: true);
        StatusMessage = $"プロファイル '{name}' を削除しました。";
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
