using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Services;
using Microsoft.Data.SqlClient;

namespace ERDesigner.ViewModels;

/// <summary>SQL Server 接続ダイアログの ViewModel</summary>
/// <remarks>接続プロファイルの保存・選択・削除に対応する</remarks>
public partial class SqlConnectionDialogViewModel : ObservableObject
{
    /// <summary>接続プロファイルの保存ストア</summary>
    private readonly SqlConnectionProfileStore _store;

    /// <summary>確認ダイアログの表示先（テストではスタブへ差し替える）</summary>
    private readonly IDialogService _dialogs;

    /// <summary>サーバー名</summary>
    [ObservableProperty]
    private string _server = "localhost";

    /// <summary>データベース名</summary>
    [ObservableProperty]
    private string _database = string.Empty;

    /// <summary>選択中の認証方式</summary>
    [ObservableProperty]
    private SqlAuthMode _authMode = SqlAuthMode.Windows;

    /// <summary>SQL / Azure AD のユーザー名</summary>
    [ObservableProperty]
    private string _userId = string.Empty;

    /// <summary>SQL / Azure AD のパスワード</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>サーバー証明書を信頼するかどうか</summary>
    [ObservableProperty]
    private bool _trustServerCertificate = true;

    /// <summary>パスワードも DPAPI で暗号化保存するかどうか（SQL / Azure AD 認証時のみ有効）</summary>
    [ObservableProperty]
    private bool _savePassword;

    /// <summary>接続テスト結果や状態メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>接続テスト中かどうか</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>保存済みプロファイル一覧</summary>
    public ObservableCollection<SqlConnectionProfile> Profiles { get; } = new();

    /// <summary>選択中のプロファイル</summary>
    [ObservableProperty]
    private SqlConnectionProfile? _selectedProfile;

    /// <summary>名前を付けて保存する際の名前（ComboBox の編集テキストと同期する）</summary>
    [ObservableProperty]
    private string _profileName = string.Empty;

    /// <summary>OK 押下時の確定設定（null なら未確定）</summary>
    public SqlConnectionSettings? Result { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（View が注入する）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>ユーザー名入力欄を表示するかどうか（Windows 認証以外で表示する）</summary>
    public bool ShowUserId => AuthMode != SqlAuthMode.Windows;

    /// <summary>パスワード入力欄を表示するかどうか（SQL 認証時のみ表示する）</summary>
    public bool ShowPassword => AuthMode == SqlAuthMode.SqlServer;

    /// <summary>ストアとダイアログサービスを指定して ViewModel を生成し、前回接続を復元する</summary>
    /// <param name="store">プロファイル保存ストア（省略時は既定パスを使用）</param>
    /// <param name="dialogService">確認ダイアログの表示先（省略時は MessageBox、テストではスタブを注入）</param>
    public SqlConnectionDialogViewModel(SqlConnectionProfileStore? store = null, IDialogService? dialogService = null)
    {
        _store = store ?? new SqlConnectionProfileStore();
        _dialogs = dialogService ?? new MessageBoxDialogService();
        // 先頭プロファイルの自動選択は、直後に復元する前回接続情報を上書きするため初期選択は行わない
        ReloadProfiles(selectFirst: false);
        RestoreLastConnection();
    }

    /// <summary>ストアからプロファイル一覧を再読込する</summary>
    /// <param name="selectFirst">true の場合は先頭プロファイルを選択する</param>
    private void ReloadProfiles(bool selectFirst)
    {
        Profiles.Clear();

        foreach (var p in _store.LoadAll())
        {
            Profiles.Add(p);
        }

        if (selectFirst && Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
            return;
        }
    }

    partial void OnAuthModeChanged(SqlAuthMode value)
    {
        OnPropertyChanged(nameof(ShowUserId));
        OnPropertyChanged(nameof(ShowPassword));
    }

    /// <summary>前回接続情報があれば入力欄へ復元する</summary>
    private void RestoreLastConnection()
    {
        var lastUsed = _store.LoadLastUsed();

        if (lastUsed is null)
        {
            return;
        }

        ApplyConnection(lastUsed.Value.Profile, lastUsed.Value.Password, updateProfileName: false);
        StatusMessage = "前回接続情報を復元しました。";
    }

    /// <summary>プロファイルまたは前回接続情報の内容を入力欄へ反映する</summary>
    /// <param name="updateProfileName">true の場合はプロファイル名入力欄も更新する</param>
    private void ApplyConnection(SqlConnectionProfile profile, string password, bool updateProfileName)
    {
        Server = profile.Server;
        Database = profile.Database;
        AuthMode = profile.AuthMode;
        UserId = profile.UserId;
        TrustServerCertificate = profile.TrustServerCertificate;
        SavePassword = profile.SavePassword;
        Password = password;

        if (updateProfileName)
        {
            ProfileName = profile.Name;
        }
    }

    /// <summary>現在の入力内容から接続プロファイルを生成する</summary>
    private SqlConnectionProfile CreateCurrentProfile(Guid? id = null, string? name = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? string.Empty,
            Server = Server,
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            TrustServerCertificate = TrustServerCertificate,
            SavePassword = SavePassword,
        };

    /// <summary>プロファイル選択時に、その内容（必要なら復号パスワード）を入力欄へ反映する</summary>
    partial void OnSelectedProfileChanged(SqlConnectionProfile? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyConnection(value, value.SavePassword ? _store.LoadPassword(value.Id) : string.Empty, updateProfileName: true);
        StatusMessage = $"プロファイル '{value.Name}' を読み込みました。";
    }

    /// <summary>現在の入力から <see cref="SqlConnectionSettings"/> を構築する</summary>
    public SqlConnectionSettings ToSettings() =>
        new()
        {
            Server = Server,
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            Password = Password,
            TrustServerCertificate = TrustServerCertificate,
        };

    /// <summary>現在の入力で接続テストを行い、結果をステータスへ表示する</summary>
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

    /// <summary>現在の入力内容を名前付きプロファイルとして保存する（同名があれば上書きする）</summary>
    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            StatusMessage = "保存名を入力してください。";
            return;
        }

        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name, ProfileName, StringComparison.OrdinalIgnoreCase));

        var profile = CreateCurrentProfile(existing?.Id, ProfileName.Trim());

        _store.Upsert(profile, Password);
        ReloadProfiles(selectFirst: false);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        StatusMessage = $"プロファイル '{profile.Name}' を保存しました。";
    }

    /// <summary>選択中プロファイルを確認のうえ削除する</summary>
    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = "削除するプロファイルを選択してください。";
            return;
        }

        if (!_dialogs.Confirm($"プロファイル '{SelectedProfile.Name}' を削除します。よろしいですか？", "確認"))
        {
            return;
        }

        var name = SelectedProfile.Name;
        _store.Delete(SelectedProfile.Id);
        ReloadProfiles(selectFirst: true);
        StatusMessage = $"プロファイル '{name}' を削除しました。";
    }

    /// <summary>入力を検証して確定し、前回接続として保存したうえでダイアログを閉じる</summary>
    [RelayCommand]
    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Database))
        {
            StatusMessage = "サーバー名とデータベース名を入力してください。";
            return;
        }

        // 確定内容を前回接続として記録し、次回起動時に復元できるようにする
        var currentProfile = CreateCurrentProfile();
        _store.SaveLastUsed(currentProfile, Password);
        Result = currentProfile.ToSettings(Password);
        CloseAction?.Invoke(true);
    }

    /// <summary>確定せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke(false);
    }
}
