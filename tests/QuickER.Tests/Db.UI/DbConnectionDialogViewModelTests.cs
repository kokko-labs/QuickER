using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Db.UI;

/// <summary><see cref="DbConnectionDialogViewModel"/> の初期表示・プロファイル選択・保存・削除・方言固定を検証するテストクラス</summary>
public class DbConnectionDialogViewModelTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _tempFolder;

    /// <summary>SQL Server のみを登録したレジストリ</summary>
    private static readonly DatabaseProviderRegistry Registry = new(
        new IDatabaseProvider[] { new SqlServerProvider() }
    );

    /// <summary>SQL Server と SQLite を登録したレジストリ（SQLite 分岐の検証用）</summary>
    private static readonly DatabaseProviderRegistry RegistryWithSqlite = new(
        new IDatabaseProvider[] { new SqlServerProvider(), new SqliteProvider() }
    );

    /// <summary>一時保存先フォルダを作成する</summary>
    public DbConnectionDialogViewModelTests()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "QuickERTests_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_tempFolder);
    }

    /// <summary>一時保存先フォルダを削除する</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // テスト後の後始末はベストエフォートとする
        }
    }

    /// <summary>DPAPI を使わず一時フォルダへ保存するテスト用ストアを生成する</summary>
    private SqlConnectionProfileStore CreateStore() => new(_tempFolder, useDpapi: false);

    /// <summary>取込モードの ViewModel を生成する</summary>
    private DbConnectionDialogViewModel CreateVm(
        SqlConnectionProfileStore store,
        IDialogService? dialogs = null
    ) => new(Registry, DbConnectionDialogMode.Import, fixedProvider: null, store, dialogs);

    /// <summary>初期表示で保存済みプロファイルを自動選択せず、入力欄が既定のままであることを検証する</summary>
    [Fact(DisplayName = "初期表示では保存済み接続を自動選択しない")]
    public void Constructor_DoesNotAutoSelectSavedProfile()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "TestDB",
                Server = "saved-server",
                Database = "saved-db",
            },
            password: ""
        );

        var vm = CreateVm(store);

        vm.SelectedProfile.Should().BeNull();
        vm.Host.Should().Be("localhost");
        vm.Database.Should().BeEmpty();
        vm.ProfileName.Should().BeEmpty();
    }

    /// <summary>プロファイル選択時に各入力欄と復号パスワードが反映されることを検証する</summary>
    [Fact(DisplayName = "保存済み接続を選択したときだけ入力欄へ反映する")]
    public void SelectedProfile_UpdatesEditableFields()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "TestDB",
                Server = "saved-server",
                Database = "saved-db",
                AuthMode = DbAuthMode.UsernamePassword,
                UserId = "sa",
                TrustServerCertificate = false,
                SavePassword = true,
            },
            password: "secret"
        );

        var vm = CreateVm(store);
        vm.Host = "restored-server";
        vm.Database = "restored-db";
        vm.ProfileName = "復元済み";

        vm.SelectedProfileItem = vm.Profiles[0];

        vm.Host.Should().Be("saved-server");
        vm.Database.Should().Be("saved-db");
        vm.UserId.Should().Be("sa");
        vm.Password.Should().Be("secret");
        vm.ProfileName.Should().Be("TestDB");
        vm.StatusMessage.Should().Contain("TestDB");
    }

    /// <summary>OK 確定で前回接続が保存され、次回ダイアログで DB 名やパスワードまで復元されることを検証する</summary>
    [Fact(DisplayName = "OK 後に新しいダイアログを開くと前回接続情報のデータベース名も復元される")]
    public void Ok_SavesLastConnection_AndNextDialogRestoresDatabase()
    {
        var store = CreateStore();
        var vm = CreateVm(store);
        vm.Host = "restored-server";
        vm.Database = "restored-db";
        vm.AuthMode = DbAuthMode.UsernamePassword;
        vm.UserId = "sa";
        vm.Password = "secret";
        vm.SavePassword = true;

        vm.OkCommand.Execute(null);

        var reopened = CreateVm(store);

        reopened.Host.Should().Be("restored-server");
        reopened.Database.Should().Be("restored-db");
        reopened.UserId.Should().Be("sa");
        reopened.Password.Should().Be("secret");
        reopened.StatusMessage.Should().Be(Strings.DbConnection_Restored);
    }

    /// <summary>OK 確定で選択されていた方言が結果へ反映されることを検証する</summary>
    [Fact(DisplayName = "OK 確定で選択方言が ResultProvider に反映される")]
    public void Ok_SetsResultAndProvider()
    {
        var store = CreateStore();
        var vm = CreateVm(store);
        vm.Host = "srv";
        vm.Database = "db";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.ResultProvider!.Name.Should().Be("sqlserver");
    }

    /// <summary>保存プロファイルの表示名に DBMS 表示名が含まれることを検証する</summary>
    [Fact(DisplayName = "プロファイル表示名に DBMS 名が含まれる")]
    public void ProfileDisplayName_ContainsDbms()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "本番DB",
                Dbms = "sqlserver",
                Server = "s",
                Database = "d",
            },
            password: ""
        );

        var vm = CreateVm(store);

        vm.Profiles.Should().ContainSingle();
        vm.Profiles[0].Display.Should().Be("[SQL Server] 本番DB");
    }

    /// <summary>同期モードでは DBMS を選択できない（固定）ことを検証する</summary>
    [Fact(DisplayName = "同期モードでは DBMS 選択が無効になる")]
    public void SyncMode_DisablesDbmsSelection()
    {
        var store = CreateStore();
        var vm = new DbConnectionDialogViewModel(
            Registry,
            DbConnectionDialogMode.Sync,
            fixedProvider: new SqlServerProvider(),
            store,
            null
        );

        vm.CanSelectDbms.Should().BeFalse();
    }

    /// <summary>削除確認でキャンセルするとプロファイルが残ることを検証する</summary>
    [Fact(DisplayName = "DeleteProfile: 確認でキャンセルするとプロファイルは削除されない")]
    public void DeleteProfile_ConfirmDeclined_KeepsProfile()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "TestDB",
                Server = "s",
                Database = "d",
            },
            password: ""
        );
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles[0];

        vm.DeleteProfileCommand.Execute(null);

        vm.Profiles.Should().ContainSingle();
        dialogs.ConfirmMessages.Should().ContainSingle().Which.Should().Contain("TestDB");
    }

    /// <summary>削除確認で OK するとプロファイルが削除され、状態メッセージに反映されることを検証する</summary>
    [Fact(DisplayName = "DeleteProfile: 確認で OK するとプロファイルが削除される")]
    public void DeleteProfile_ConfirmAccepted_DeletesProfile()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "TestDB",
                Server = "s",
                Database = "d",
            },
            password: ""
        );
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles[0];

        vm.DeleteProfileCommand.Execute(null);

        vm.Profiles.Should().BeEmpty();
        vm.StatusMessage.Should().Be(string.Format(Strings.DbConnection_ProfileDeleted, "TestDB"));
    }

    // ---------------- SQLite（ファイル型 DB）分岐 ----------------

    /// <summary>SQLite を選択した ViewModel を生成する（既定は取込モード・新規作成不可）</summary>
    private DbConnectionDialogViewModel CreateSqliteVm(
        SqlConnectionProfileStore store,
        IFileDialogService? files = null,
        bool allowFileCreation = false,
        DbConnectionDialogMode mode = DbConnectionDialogMode.Import
    )
    {
        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            mode,
            fixedProvider: mode == DbConnectionDialogMode.Sync
                ? RegistryWithSqlite.Get(SqliteProvider.ProviderName)
                : null,
            store,
            dialogService: null,
            fileDialogService: files ?? new StubFileDialogService(),
            allowSqliteFileCreation: allowFileCreation
        )
        {
            SelectedProvider = RegistryWithSqlite.Get(SqliteProvider.ProviderName),
        };

        return vm;
    }

    /// <summary>SQLite 選択時にファイルパス欄が表示され、サーバー系フィールドが非表示になることを検証する</summary>
    [Fact(DisplayName = "SQLite 選択時はファイルパス欄を表示しサーバー系フィールドを隠す")]
    public void Sqlite_ShowsFilePath_HidesServerFields()
    {
        var vm = CreateSqliteVm(CreateStore());

        vm.ShowFilePath.Should().BeTrue();
        vm.ShowServerFields.Should().BeFalse();
        vm.ShowUserId.Should().BeFalse();
        vm.ShowPassword.Should().BeFalse();
        vm.ShowAuthMode.Should().BeFalse();
        vm.ShowTrustServerCertificate.Should().BeFalse();
    }

    /// <summary>SQLite でファイルパスが空のとき OK が拒否されることを検証する</summary>
    [Fact(DisplayName = "SQLite: ファイルパスが空だと OK は拒否される")]
    public void Sqlite_EmptyFilePath_RejectsOk()
    {
        var vm = CreateSqliteVm(CreateStore());
        vm.FilePath = string.Empty;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_FilePathRequired);
    }

    /// <summary>SQLite で存在しないパスのとき OK が拒否されることを検証する（取込専用・新規作成不可）</summary>
    [Fact(DisplayName = "SQLite: 存在しないファイルパスだと OK は拒否される")]
    public void Sqlite_MissingFile_RejectsOk()
    {
        var vm = CreateSqliteVm(CreateStore());
        vm.FilePath = Path.Combine(_tempFolder, "does-not-exist.db");

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
    }

    /// <summary>SQLite で実在するファイルパスのとき OK が確定し、結果へファイルパスが保持されることを検証する</summary>
    [Fact(DisplayName = "SQLite: 実在するファイルパスだと OK が確定し結果に保持される")]
    public void Sqlite_ExistingFile_ConfirmsOk_AndKeepsFilePath()
    {
        var dbPath = Path.Combine(_tempFolder, "sample.db");
        File.WriteAllText(dbPath, string.Empty);

        var vm = CreateSqliteVm(CreateStore());
        vm.FilePath = dbPath;

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.FilePath.Should().Be(dbPath);
        vm.ResultProvider!.Name.Should().Be(SqliteProvider.ProviderName);
    }

    /// <summary>参照コマンドが選択されたファイルパスを FilePath へ反映することを検証する</summary>
    [Fact(DisplayName = "SQLite: 参照コマンドで選択したパスが FilePath に反映される")]
    public void Sqlite_BrowseFile_SetsFilePath()
    {
        var picked = Path.Combine(_tempFolder, "picked.sqlite");
        var files = new StubFileDialogService { OpenResult = new FileDialogResult(picked, 1) };
        var vm = CreateSqliteVm(CreateStore(), files);

        vm.BrowseFileCommand.Execute(null);

        vm.FilePath.Should().Be(picked);
    }

    /// <summary>SQLite プロファイルを保存し再適用するとファイルパスが往復することを検証する</summary>
    [Fact(DisplayName = "SQLite: プロファイル保存→適用でファイルパスが往復する")]
    public void Sqlite_ProfileRoundTrip_PreservesFilePath()
    {
        var dbPath = Path.Combine(_tempFolder, "roundtrip.db");
        var store = CreateStore();
        var vm = CreateSqliteVm(store);
        vm.FilePath = dbPath;
        vm.ProfileName = "SQLite接続";

        vm.SaveProfileCommand.Execute(null);

        // 再度開き、保存したプロファイルを選択してファイルパスが復元されることを確認する
        var reopened = CreateSqliteVm(store);
        var saved = reopened.Profiles.Single(p =>
            p.Profile.Dbms == SqliteProvider.ProviderName && p.Profile.Name == "SQLite接続"
        );
        reopened.SelectedProfileItem = saved;

        reopened.FilePath.Should().Be(dbPath);
    }

    // ---------------- SQLite 新規作成（DB 同期の文脈のみ） ----------------

    /// <summary>新規作成が許可されていない既定では、新規作成ボタンが実行不可・非表示相当であることを検証する</summary>
    [Fact(DisplayName = "SQLite: 新規作成が不許可なら BrowseNewFile は実行不可・非表示相当")]
    public void Sqlite_CreationDisallowed_HidesAndDisablesCreateNew()
    {
        var vm = CreateSqliteVm(CreateStore(), allowFileCreation: false);

        vm.ShowCreateNewFile.Should().BeFalse();
        vm.BrowseNewFileCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>新規作成が許可され SQLite 選択中なら、新規作成ボタンが表示・実行可であることを検証する</summary>
    [Fact(DisplayName = "SQLite: 新規作成が許可されていれば BrowseNewFile は表示・実行可")]
    public void Sqlite_CreationAllowed_ShowsAndEnablesCreateNew()
    {
        var vm = CreateSqliteVm(
            CreateStore(),
            allowFileCreation: true,
            mode: DbConnectionDialogMode.Sync
        );

        vm.ShowCreateNewFile.Should().BeTrue();
        vm.BrowseNewFileCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>新規作成が許可されていても、手入力の存在しないパスは従来どおり拒否されることを検証する（回帰）</summary>
    [Fact(DisplayName = "SQLite: 新規作成許可でも手入力の存在しないパスは拒否される")]
    public void Sqlite_CreationAllowed_HandTypedMissingPath_RejectsOk()
    {
        var vm = CreateSqliteVm(
            CreateStore(),
            allowFileCreation: true,
            mode: DbConnectionDialogMode.Sync
        );
        // 「新規作成」ボタンを経由せず、存在しないパスを手入力した場合
        vm.FilePath = Path.Combine(_tempFolder, "typo-does-not-exist.db");

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
        File.Exists(vm.FilePath).Should().BeFalse();
    }

    /// <summary>新規作成ボタンで選んだパスは存在チェックを免除され、OK 確定時にファイルが実作成されることを検証する</summary>
    [Fact(DisplayName = "SQLite: 新規作成で選んだパスは OK 確定時に空 DB が作成される")]
    public void Sqlite_CreateNewFile_CreatesFileOnOk()
    {
        var newPath = Path.Combine(_tempFolder, "brand-new.db");
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(newPath, 1) };
        var vm = CreateSqliteVm(
            CreateStore(),
            files,
            allowFileCreation: true,
            mode: DbConnectionDialogMode.Sync
        );

        vm.BrowseNewFileCommand.Execute(null);
        vm.FilePath.Should().Be(newPath);
        File.Exists(newPath).Should().BeFalse("OK 確定前はまだ作成されない");

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.FilePath.Should().Be(newPath);
        File.Exists(newPath).Should().BeTrue("OK 確定時に空 DB が作成される");
    }

    /// <summary>新規作成で選択後に別パスへ手編集すると、存在チェックが復活してエラーになることを検証する</summary>
    [Fact(DisplayName = "SQLite: 新規作成で選択後に別パスへ手編集すると存在チェックが復活する")]
    public void Sqlite_CreateNewFile_ThenEditToDifferentPath_RejectsOk()
    {
        var newPath = Path.Combine(_tempFolder, "chosen.db");
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(newPath, 1) };
        var vm = CreateSqliteVm(
            CreateStore(),
            files,
            allowFileCreation: true,
            mode: DbConnectionDialogMode.Sync
        );

        vm.BrowseNewFileCommand.Execute(null);
        // 新規作成で選んだ後、別の存在しないパスへ手編集する（新規作成の意図ではなくなる）
        vm.FilePath = Path.Combine(_tempFolder, "edited-elsewhere.db");

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_FileNotFound);
    }

    /// <summary>新規作成のファイル作成に失敗したとき、エラー表示のうえダイアログを閉じないことを検証する</summary>
    [Fact(DisplayName = "SQLite: 新規作成の作成失敗ではエラー表示しダイアログを閉じない")]
    public void Sqlite_CreateNewFile_CreationFails_ShowsErrorAndStaysOpen()
    {
        // 親ディレクトリが存在しないパスは ReadWriteCreate でも開けず、作成が失敗する
        var invalidPath = Path.Combine(_tempFolder, "no-such-dir", "x.db");
        var files = new StubFileDialogService { SaveResult = new FileDialogResult(invalidPath, 1) };
        var closed = new List<bool>();
        var vm = CreateSqliteVm(
            CreateStore(),
            files,
            allowFileCreation: true,
            mode: DbConnectionDialogMode.Sync
        );
        vm.CloseAction = closed.Add;

        vm.BrowseNewFileCommand.Execute(null);
        vm.OkCommand.Execute(null);

        // 失敗メッセージのプレフィックス（{0} 手前）で照合し、カルチャに依存しないようにする
        var failurePrefix = Strings.DbConnection_CreateFileFailed.Split('{')[0];
        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().StartWith(failurePrefix);
        closed.Should().BeEmpty("作成失敗時はダイアログを閉じない");
    }

    /// <summary>SQL Server 選択時は従来どおりサーバー系フィールドを表示しファイルパスを隠すことを検証する</summary>
    [Fact(DisplayName = "SQL Server 選択時は従来どおりサーバー系フィールドを表示する")]
    public void SqlServer_ShowsServerFields_HidesFilePath()
    {
        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            DbConnectionDialogMode.Import,
            fixedProvider: null,
            CreateStore(),
            dialogService: null,
            fileDialogService: new StubFileDialogService()
        )
        {
            SelectedProvider = RegistryWithSqlite.Get(SqlServerProvider.ProviderName),
        };

        vm.ShowFilePath.Should().BeFalse();
        vm.ShowServerFields.Should().BeTrue();
    }
}
