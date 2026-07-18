using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Gui.Abstractions;
using QuickER.Gui.Common;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Sqlite;

namespace QuickER.ViewModels;

/// <summary>接続ダイアログの用途（DBMS 選択の可否が変わる）</summary>
public enum DbConnectionDialogMode
{
    /// <summary>データベースから取込（DBMS を選択できる）</summary>
    Import,

    /// <summary>データベースと同期（DBMS は図の方言に固定する）</summary>
    Sync,
}

/// <summary>DB 接続ダイアログの ViewModel（多 DBMS 共通・条件表示）</summary>
/// <remarks>接続プロファイルの保存・選択・削除に対応する。方言固有フィールドは Visibility で切り替える</remarks>
public partial class DbConnectionDialogViewModel : ObservableObject
{
    /// <summary>登録済みプロバイダのレジストリ（DBMS 選択肢・接続文字列組立に用いる）</summary>
    private readonly DatabaseProviderRegistry _providers;

    /// <summary>接続プロファイルの保存ストア</summary>
    private readonly SqlConnectionProfileStore _store;

    /// <summary>確認ダイアログの表示先（テストではスタブへ差し替える）</summary>
    private readonly IDialogService _dialogs;

    /// <summary>ファイル選択ダイアログの表示先（SQLite のファイルパス参照に用いる。テストではスタブへ差し替える）</summary>
    private readonly IFileDialogService _files;

    /// <summary>DBMS 選択肢（登録済み全プロバイダ）</summary>
    public ObservableCollection<IDatabaseProvider> Providers { get; } = new();

    /// <summary>選択中のプロバイダ（現在の方言）</summary>
    [ObservableProperty]
    private IDatabaseProvider _selectedProvider = null!;

    /// <summary>ダイアログの用途（Sync のとき DBMS 選択を無効化する）</summary>
    [ObservableProperty]
    private DbConnectionDialogMode _mode = DbConnectionDialogMode.Import;

    /// <summary>ホスト（サーバー名）</summary>
    [ObservableProperty]
    private string _host = "localhost";

    /// <summary>ポート（空欄は方言既定）。入力テキストと同期する</summary>
    [ObservableProperty]
    private string _port = string.Empty;

    /// <summary>データベース名</summary>
    [ObservableProperty]
    private string _database = string.Empty;

    /// <summary>選択中の認証方式</summary>
    [ObservableProperty]
    private DbAuthMode _authMode = DbAuthMode.Windows;

    /// <summary>ユーザー名</summary>
    [ObservableProperty]
    private string _userId = string.Empty;

    /// <summary>パスワード</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>サーバー証明書を信頼するかどうか（SQL Server 固有）</summary>
    [ObservableProperty]
    private bool _trustServerCertificate = true;

    /// <summary>接続タイムアウト（秒）</summary>
    [ObservableProperty]
    private int _connectTimeoutSeconds = 15;

    /// <summary>サービス名（Oracle 固有・将来使用）</summary>
    [ObservableProperty]
    private string _serviceName = string.Empty;

    /// <summary>データベースファイルのパス（SQLite 固有。サーバー系フィールドの代わりに用いる）</summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>パスワードも DPAPI で暗号化保存するかどうか</summary>
    [ObservableProperty]
    private bool _savePassword;

    /// <summary>接続テスト結果や状態メッセージ</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>接続テスト中かどうか</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>保存済みプロファイル一覧（全 DBMS 分・表示名に DBMS を含む）</summary>
    public ObservableCollection<ProfileListItem> Profiles { get; } = new();

    /// <summary>選択中のプロファイル項目</summary>
    [ObservableProperty]
    private ProfileListItem? _selectedProfileItem;

    /// <summary>選択中のプロファイル（本体）</summary>
    public SqlConnectionProfile? SelectedProfile => SelectedProfileItem?.Profile;

    /// <summary>プロファイルを一覧表示用に DBMS 名付き表示名でラップした項目</summary>
    /// <param name="Profile">プロファイル本体</param>
    /// <param name="Display">一覧に表示する文字列（例: [SQL Server] 本番DB）</param>
    public sealed record ProfileListItem(SqlConnectionProfile Profile, string Display);

    /// <summary>名前を付けて保存する際の名前（ComboBox の編集テキストと同期する）</summary>
    [ObservableProperty]
    private string _profileName = string.Empty;

    /// <summary>OK 押下時の確定設定（null なら未確定）</summary>
    public DbConnectionSettings? Result { get; private set; }

    /// <summary>確定時に選択されていたプロバイダ（取込時に図の TargetDbms へ反映する）</summary>
    public IDatabaseProvider? ResultProvider { get; private set; }

    /// <summary>ダイアログを閉じる際に呼ぶアクション（View が注入する）</summary>
    public Action<bool>? CloseAction { get; set; }

    /// <summary>DBMS を選択できるか（同期モードでは固定＝無効）</summary>
    public bool CanSelectDbms => Mode == DbConnectionDialogMode.Import;

    /// <summary>認証方式を選択できるか（SQL Server のみ複数の認証方式を持つ）</summary>
    public bool ShowAuthMode =>
        SelectedProvider?.Name == QuickER.SqlServer.SqlServerProvider.ProviderName;

    /// <summary>ユーザー名入力欄を表示するか（Windows 認証以外・非 SQL Server は常に表示。SQLite では非表示）</summary>
    public bool ShowUserId => ShowServerFields && (!ShowAuthMode || AuthMode != DbAuthMode.Windows);

    /// <summary>パスワード入力欄を表示するか（ユーザー名/パスワード認証時。非 SQL Server は常に表示。SQLite では非表示）</summary>
    public bool ShowPassword =>
        ShowServerFields && (!ShowAuthMode || AuthMode == DbAuthMode.UsernamePassword);

    /// <summary>サーバー証明書信頼チェックを表示するか（SQL Server 固有）</summary>
    public bool ShowTrustServerCertificate => ShowAuthMode;

    /// <summary>サービス名入力欄を表示するか（Oracle 固有・現状は常に非表示）</summary>
    public bool ShowServiceName => SelectedProvider?.Name == "oracle";

    /// <summary>ファイルパス入力欄を表示するか（SQLite 固有。ファイル型 DB の接続に用いる）</summary>
    public bool ShowFilePath => SelectedProvider?.Name == SqliteProvider.ProviderName;

    /// <summary>サーバー系フィールド（ホスト・ポート・DB 名・認証・証明書）を表示するか（SQLite では非表示）</summary>
    public bool ShowServerFields => !ShowFilePath;

    /// <summary>依存を注入して ViewModel を生成し、前回接続を復元する</summary>
    /// <param name="providers">プロバイダレジストリ</param>
    /// <param name="mode">ダイアログの用途</param>
    /// <param name="fixedProvider">同期モードで固定する方言（Import では初期選択に用いる）</param>
    /// <param name="store">プロファイル保存ストア（省略時は既定パスを使用）</param>
    /// <param name="dialogService">確認ダイアログの表示先（省略時は MessageBox、テストではスタブを注入）</param>
    /// <param name="fileDialogService">ファイル選択ダイアログの表示先（SQLite の参照ボタン用。省略時は WPF 実装、テストではスタブを注入）</param>
    public DbConnectionDialogViewModel(
        DatabaseProviderRegistry providers,
        DbConnectionDialogMode mode = DbConnectionDialogMode.Import,
        IDatabaseProvider? fixedProvider = null,
        SqlConnectionProfileStore? store = null,
        IDialogService? dialogService = null,
        IFileDialogService? fileDialogService = null
    )
    {
        _providers = providers;
        _store = store ?? new SqlConnectionProfileStore();
        _dialogs = dialogService ?? new MessageBoxDialogService();
        _files = fileDialogService ?? new WpfFileDialogService();
        Mode = mode;

        foreach (var provider in _providers.All)
        {
            Providers.Add(provider);
        }

        _selectedProvider =
            fixedProvider ?? Providers.FirstOrDefault() ?? _providers.Get("sqlserver");

        // 先頭プロファイルの自動選択は、直後に復元する前回接続情報を上書きするため初期選択は行わない
        ReloadProfiles();
        RestoreLastConnection();
    }

    /// <summary>ストアからプロファイル一覧を再読込する（全 DBMS 分）</summary>
    private void ReloadProfiles()
    {
        Profiles.Clear();

        foreach (var p in _store.LoadAll())
        {
            Profiles.Add(new ProfileListItem(p, GetProfileDisplayName(p)));
        }
    }

    partial void OnSelectedProviderChanged(IDatabaseProvider value)
    {
        // 方言に応じた条件表示を更新する
        OnPropertyChanged(nameof(ShowAuthMode));
        OnPropertyChanged(nameof(ShowUserId));
        OnPropertyChanged(nameof(ShowPassword));
        OnPropertyChanged(nameof(ShowTrustServerCertificate));
        OnPropertyChanged(nameof(ShowServiceName));
        OnPropertyChanged(nameof(ShowFilePath));
        OnPropertyChanged(nameof(ShowServerFields));
    }

    partial void OnModeChanged(DbConnectionDialogMode value)
    {
        OnPropertyChanged(nameof(CanSelectDbms));
    }

    partial void OnAuthModeChanged(DbAuthMode value)
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

        // 同期モードで方言固定中は、異なる方言の前回接続は方言のみ復元しない
        ApplyProfile(lastUsed.Value.Profile, lastUsed.Value.Password, updateProfileName: false);
        StatusMessage = Strings.DbConnection_Restored;
    }

    /// <summary>プロファイルまたは前回接続情報の内容を入力欄へ反映する</summary>
    private void ApplyProfile(SqlConnectionProfile profile, string password, bool updateProfileName)
    {
        // 同期モードでは方言は固定のまま、それ以外のフィールドのみ反映する
        if (
            Mode == DbConnectionDialogMode.Import
            && _providers.TryGet(profile.Dbms, out var provider)
        )
        {
            SelectedProvider = provider;
        }

        Host = profile.Server;
        Port = profile.Port?.ToString() ?? string.Empty;
        Database = profile.Database;
        AuthMode = profile.AuthMode;
        UserId = profile.UserId;
        TrustServerCertificate = profile.TrustServerCertificate;
        ServiceName = profile.ServiceName;
        FilePath = profile.FilePath;
        ConnectTimeoutSeconds = profile.ConnectTimeoutSeconds;
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
            Dbms = SelectedProvider.Name,
            Server = Host,
            Port = ParsePort(),
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            TrustServerCertificate = TrustServerCertificate,
            ServiceName = ServiceName,
            FilePath = FilePath,
            ConnectTimeoutSeconds = ConnectTimeoutSeconds,
            SavePassword = SavePassword,
        };

    /// <summary>ポート入力を数値へ解析する（空欄・不正値は null＝方言既定）</summary>
    private int? ParsePort() => int.TryParse(Port, out var value) && value > 0 ? value : null;

    /// <summary>一覧での表示名（DBMS を含める。例: [SQL Server] 本番DB）</summary>
    public string GetProfileDisplayName(SqlConnectionProfile profile)
    {
        var dbmsLabel = _providers.TryGet(profile.Dbms, out var provider)
            ? provider.DisplayName
            : profile.Dbms;
        return $"[{dbmsLabel}] {profile.Name}";
    }

    /// <summary>プロファイル選択時に、その内容（必要なら復号パスワード）を入力欄へ反映する</summary>
    partial void OnSelectedProfileItemChanged(ProfileListItem? value)
    {
        OnPropertyChanged(nameof(SelectedProfile));

        if (value is null)
        {
            return;
        }

        var profile = value.Profile;

        // 同期モードでは異なる方言のプロファイルは適用しない（方言固定を優先）
        if (Mode == DbConnectionDialogMode.Sync && !IsSameDbms(profile.Dbms))
        {
            StatusMessage = string.Format(
                Strings.DbConnection_ProfileDialectMismatch,
                profile.Name
            );
            return;
        }

        ApplyProfile(
            profile,
            profile.SavePassword ? _store.LoadPassword(profile.Id) : string.Empty,
            updateProfileName: true
        );
        StatusMessage = string.Format(Strings.DbConnection_ProfileLoaded, profile.Name);
    }

    /// <summary>指定 DBMS 名が現在の選択方言と一致するか（大文字小文字無視）</summary>
    private bool IsSameDbms(string dbms) =>
        string.Equals(dbms, SelectedProvider.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>現在の入力から <see cref="DbConnectionSettings"/> を構築する</summary>
    public DbConnectionSettings ToSettings() =>
        new()
        {
            Host = Host,
            Port = ParsePort(),
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            Password = Password,
            TrustServerCertificate = TrustServerCertificate,
            ServiceName = ServiceName,
            FilePath = FilePath,
            ConnectTimeoutSeconds = ConnectTimeoutSeconds,
        };

    /// <summary>現在の入力で接続テストを行い、結果をステータスへ表示する</summary>
    /// <remarks>プロバイダの接続文字列でスキーマ取込を試み、接続可否とテーブル数を確認する</remarks>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = Strings.DbConnection_Connecting;

        try
        {
            var connectionString = SelectedProvider.BuildConnectionString(ToSettings());
            var result = await SelectedProvider
                .SchemaImporter.ImportAsync(connectionString)
                .ConfigureAwait(true);
            StatusMessage = string.Format(
                Strings.DbConnection_ConnectSucceeded,
                result.Entities.Count
            );
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.DbConnection_ConnectFailed, ex.Message);
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
            StatusMessage = Strings.DbConnection_ProfileNameRequired;
            return;
        }

        // 同名かつ同一方言のプロファイルのみ上書き対象とする
        var existing = Profiles.FirstOrDefault(p =>
            string.Equals(p.Profile.Name, ProfileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                p.Profile.Dbms,
                SelectedProvider.Name,
                StringComparison.OrdinalIgnoreCase
            )
        );

        var profile = CreateCurrentProfile(existing?.Profile.Id, ProfileName.Trim());

        _store.Upsert(profile, Password);
        ReloadProfiles();
        SelectedProfileItem = Profiles.FirstOrDefault(p => p.Profile.Id == profile.Id);
        StatusMessage = string.Format(Strings.DbConnection_ProfileSaved, profile.Name);
    }

    /// <summary>選択中プロファイルを確認のうえ削除する</summary>
    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            StatusMessage = Strings.DbConnection_SelectProfileToDelete;
            return;
        }

        if (
            !_dialogs.Confirm(
                string.Format(Strings.DbConnection_DeleteProfileConfirm, SelectedProfile.Name),
                Strings.Common_Confirm
            )
        )
        {
            return;
        }

        var name = SelectedProfile.Name;
        _store.Delete(SelectedProfile.Id);
        ReloadProfiles();
        SelectedProfileItem = null;
        StatusMessage = string.Format(Strings.DbConnection_ProfileDeleted, name);
    }

    /// <summary>ファイル選択ダイアログで SQLite のデータベースファイルを選ぶ（取込専用のため既存ファイルのみ）</summary>
    [RelayCommand]
    private void BrowseFile()
    {
        var picked = _files.PickOpenFile(Strings.DbConnection_SqliteFileFilter);

        if (picked is null)
        {
            return;
        }

        FilePath = picked.Path;
    }

    /// <summary>入力を検証して確定し、前回接続として保存したうえでダイアログを閉じる</summary>
    [RelayCommand]
    private void Ok()
    {
        // SQLite はファイル型 DB のためファイルパスのみを検証する（ホスト・DB 名検証はスキップ）
        if (ShowFilePath)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                StatusMessage = Strings.DbConnection_FilePathRequired;
                return;
            }

            // 取込専用のため新規作成は許可せず、存在しないファイルは確定を拒否する
            if (!File.Exists(FilePath))
            {
                StatusMessage = Strings.DbConnection_FileNotFound;
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Database))
        {
            StatusMessage = Strings.DbConnection_HostAndDatabaseRequired;
            return;
        }

        // 確定内容を前回接続として記録し、次回起動時に復元できるようにする
        var currentProfile = CreateCurrentProfile();
        _store.SaveLastUsed(currentProfile, Password);
        Result = ToSettings();
        ResultProvider = SelectedProvider;
        CloseAction?.Invoke(true);
    }

    /// <summary>確定せずダイアログを閉じる</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        ResultProvider = null;
        CloseAction?.Invoke(false);
    }
}
