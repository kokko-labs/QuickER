using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Db.UI.Resources;
using QuickER.Gui.Abstractions;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
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

    /// <summary>全方言を登録したレジストリ（方言ごとの条件表示の検証用）</summary>
    private static readonly DatabaseProviderRegistry RegistryWithAllDialects = new(
        new IDatabaseProvider[]
        {
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new MySqlProvider(),
            new OracleProvider(),
            new SqliteProvider(),
        }
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

    /// <summary>
    /// 同期モードでは、図と違う方言の前回接続を復元しないことを検証する（DU6）。
    /// </summary>
    /// <remarks>
    /// 方言が違えば入力欄の意味も変わるため、固定した方言の欄へ別方言の値が入ると
    /// 「復元しました」と言いながら接続できない入力になる。
    /// </remarks>
    [Fact(DisplayName = "同期モード: 方言が違う前回接続は復元しない")]
    public void SyncMode_LastConnectionOfOtherDialect_IsNotRestored()
    {
        var store = CreateStore();
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Dbms = SqlServerProvider.ProviderName,
                Server = "prod-sqlserver",
                Database = "Sales",
            },
            password: "p@ss"
        );

        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            DbConnectionDialogMode.Sync,
            fixedProvider: new SqliteProvider(),
            store,
            null
        );

        vm.Host.Should().Be("localhost", "別方言のサーバー名は入れず既定のまま");
        vm.Database.Should().BeEmpty();
        vm.Password.Should().BeEmpty();
        vm.StatusMessage.Should().NotBe(Strings.DbConnection_Restored);
    }

    /// <summary>同期モードでも、方言が同じ前回接続は従来どおり復元することを検証する（DU6 の対照）</summary>
    [Fact(DisplayName = "同期モード: 方言が同じ前回接続は復元する")]
    public void SyncMode_LastConnectionOfSameDialect_IsRestored()
    {
        var store = CreateStore();
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Dbms = SqlServerProvider.ProviderName,
                Server = "prod-sqlserver",
                Database = "Sales",
            },
            password: "p@ss"
        );

        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            DbConnectionDialogMode.Sync,
            fixedProvider: new SqlServerProvider(),
            store,
            null
        );

        vm.Host.Should().Be("prod-sqlserver");
        vm.Database.Should().Be("Sales");
        vm.StatusMessage.Should().Be(Strings.DbConnection_Restored);
    }

    /// <summary>取込モードは方言ごと復元する（DU6 の制限は同期モード限定）ことを検証する</summary>
    [Fact(DisplayName = "取込モード: 前回接続は方言ごと復元する")]
    public void ImportMode_LastConnection_RestoresDialectToo()
    {
        var store = CreateStore();
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Dbms = SqliteProvider.ProviderName,
                FilePath = @"C:\data\app.db",
            },
            password: string.Empty
        );

        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            DbConnectionDialogMode.Import,
            fixedProvider: null,
            store,
            null
        );

        vm.SelectedProvider.Name.Should().Be(SqliteProvider.ProviderName);
        vm.FilePath.Should().Be(@"C:\data\app.db");
    }

    /// <summary>
    /// 接続テスト中は OK で確定できないことを検証する（DU4）。
    /// </summary>
    /// <remarks>
    /// テストは取り消せないため、確定して閉じるとテストだけが接続を掴んだまま走り続け、
    /// 呼び出し側の取込・同期と同時に同じ DB を叩くことになる。
    /// </remarks>
    [Fact(DisplayName = "接続テスト中は OK を実行できない")]
    public async Task Ok_WhileTestingConnection_IsDisabled()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var provider = new GatedTestProvider(started, release);
        var vm = new DbConnectionDialogViewModel(
            new DatabaseProviderRegistry(new IDatabaseProvider[] { provider }),
            DbConnectionDialogMode.Import,
            fixedProvider: provider,
            CreateStore(),
            null
        );

        vm.OkCommand.CanExecute(null).Should().BeTrue("テスト前は確定できる");

        var testing = vm.TestConnectionCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        vm.OkCommand.CanExecute(null).Should().BeFalse("接続テスト中は確定できない");

        release.SetResult();
        await testing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        vm.OkCommand.CanExecute(null).Should().BeTrue("テストが終われば確定できる");
    }

    /// <summary>接続テストが始まったことを知らせ、解放されるまで待つスキーマインポーター</summary>
    private sealed class GatedSchemaImporter(
        TaskCompletionSource started,
        TaskCompletionSource release
    ) : ISchemaImporter
    {
        public async Task<SchemaImportResult> ImportAsync(
            string connectionString,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken = default
        )
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new SchemaImportResult();
        }
    }

    /// <summary>接続テストを任意のタイミングまで止められるプロバイダ</summary>
    private sealed class GatedTestProvider(
        TaskCompletionSource started,
        TaskCompletionSource release
    ) : IDatabaseProvider
    {
        public string Name => "gated";

        public string DisplayName => "Gated";

        public int? DefaultPort => null;

        public ISchemaImporter SchemaImporter { get; } = new GatedSchemaImporter(started, release);

        public IColumnTypeMapper TypeMapper => null!;

        public ITypeCatalog TypeCatalog => null!;

        public ISyncScriptBuilder SyncScriptBuilder => null!;

        public SyncDialectCapabilities SyncCapabilities => null!;

        public ISchemaSyncExecutor SyncExecutor => null!;

        public IDdlGenerator DdlGenerator => null!;

        public string BuildConnectionString(DbConnectionSettings settings) => "gated";
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

    /// <summary>新規作成が許可されていても、手入力の存在しないパスは拒否されることを検証する（回帰）</summary>
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

    /// <summary>SQL Server 選択時はサーバー系フィールドを表示しファイルパスを隠すことを検証する</summary>
    [Fact(DisplayName = "SQL Server 選択時はサーバー系フィールドを表示する")]
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

    // ---------------- コマンドタイムアウト ----------------

    /// <summary>新規入力の既定値が共通ヘルパーの既定（＝従来のハードコード値）であることを検証する</summary>
    [Fact(DisplayName = "コマンドタイムアウトの既定は DbCommands の既定値")]
    public void CommandTimeout_DefaultsToHelperDefault()
    {
        var vm = CreateVm(CreateStore());

        vm.CommandTimeout.Should().Be(DbCommands.DefaultTimeoutSeconds.ToString());
    }

    /// <summary>キーを持たない旧プロファイルを読んでも既定値へ落ちる（JSON 後方互換）ことを検証する</summary>
    [Fact(DisplayName = "コマンドタイムアウトを持たない旧プロファイルは既定値で読み込まれる")]
    public void CommandTimeout_MissingInLegacyProfile_FallsBackToDefault()
    {
        var store = CreateStore();
        // キーが無い旧 JSON を模して、既定値のまま保存されたプロファイルを読み戻す
        store.Upsert(
            new SqlConnectionProfile { Name = "旧プロファイル", Server = "legacy" },
            password: ""
        );

        var vm = CreateVm(store);
        vm.SelectedProfileItem = vm.Profiles[0];

        vm.CommandTimeout.Should().Be(DbCommands.DefaultTimeoutSeconds.ToString());
    }

    /// <summary>プロファイルへ保存した値が復元され、確定結果（接続設定）へも載ることを検証する</summary>
    [Fact(DisplayName = "コマンドタイムアウトはプロファイルへ保存され接続設定へ流れる")]
    public void CommandTimeout_RoundTripsThroughProfileAndSettings()
    {
        var store = CreateStore();
        var vm = CreateVm(store);
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = "180";
        vm.ProfileName = "長時間取込";
        vm.SaveProfileCommand.Execute(null);

        var reopened = CreateVm(store);
        reopened.SelectedProfileItem = reopened.Profiles.Single(p =>
            p.Profile.Name == "長時間取込"
        );
        reopened.OkCommand.Execute(null);

        reopened.CommandTimeout.Should().Be("180");
        reopened.Result.Should().NotBeNull();
        reopened.Result!.CommandTimeoutSeconds.Should().Be(180);
    }

    /// <summary>0（無制限）は ADO.NET の規約どおり通ることを検証する</summary>
    [Fact(DisplayName = "コマンドタイムアウト 0（無制限）は確定できる")]
    public void CommandTimeout_ZeroIsAccepted()
    {
        var vm = CreateVm(CreateStore());
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = "0";

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.CommandTimeoutSeconds.Should().Be(0);
    }

    /// <summary>空欄は確定時に弾く（直前の有効値で黙って動かない）ことを検証する</summary>
    /// <remarks>
    /// <c>int</c> へ直接バインドしていた頃は、空欄が型変換の失敗でソースへ届かず
    /// 直前の有効値が残るため、この検証を素通りして「画面と違う値」で確定できていた。
    /// </remarks>
    [Fact(DisplayName = "空欄のコマンドタイムアウトは OK を拒否する")]
    public void CommandTimeout_BlankRejectsOk()
    {
        var vm = CreateVm(CreateStore());
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = string.Empty;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
    }

    /// <summary>数値として読めない入力を確定時に弾くことを検証する</summary>
    [Theory(DisplayName = "数値でないコマンドタイムアウトは OK を拒否する")]
    [InlineData("abc")]
    [InlineData("   ")]
    [InlineData("1.5")]
    public void CommandTimeout_NonNumericRejectsOk(string input)
    {
        var vm = CreateVm(CreateStore());
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = input;

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
    }

    /// <summary>確定と同じ検証を接続テストも通す（不正なまま接続を試みない）ことを検証する</summary>
    [Fact(DisplayName = "空欄のコマンドタイムアウトは接続テストを拒否する")]
    public async Task CommandTimeout_BlankRejectsTestConnection()
    {
        var vm = CreateVm(CreateStore());
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = string.Empty;

        await vm.TestConnectionCommand.ExecuteAsync(null);

        // 接続を試みる前に返るため、実行中フラグも立たない
        vm.IsBusy.Should().BeFalse();
        vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
    }

    /// <summary>負値は確定時に弾く（黙って既定へ丸めない）ことを検証する</summary>
    [Fact(DisplayName = "負のコマンドタイムアウトは OK を拒否する")]
    public void CommandTimeout_NegativeRejectsOk()
    {
        var vm = CreateVm(CreateStore());
        vm.Host = "server";
        vm.Database = "db";
        vm.CommandTimeout = "-1";

        vm.OkCommand.Execute(null);

        vm.Result.Should().BeNull();
        vm.StatusMessage.Should().Be(Strings.DbConnection_CommandTimeoutInvalid);
    }

    /// <summary>TLS 要求水準の選択欄は、対応する接続文字列キーワードを持つ 2 方言でのみ表示することを検証する</summary>
    [Theory(DisplayName = "TLS 要求水準の欄は PostgreSQL / MySQL でのみ表示する")]
    [InlineData("postgresql", true)]
    [InlineData("mysql", true)]
    [InlineData("sqlserver", false)]
    [InlineData("sqlite", false)]
    public void ShowSslMode_OnlyForDialectsWithKeyword(string dbms, bool expected)
    {
        var vm = CreateAllDialectVm(CreateStore());
        vm.SelectedProvider = RegistryWithAllDialects.Get(dbms);

        vm.ShowSslMode.Should().Be(expected);
    }

    /// <summary>TLS 要求水準の初期値が「未指定」＝ドライバ既定であることを検証する（既定挙動不変の入口）</summary>
    [Fact(DisplayName = "TLS 要求水準の初期値は未指定")]
    public void SslMode_DefaultsToUnspecified()
    {
        var vm = CreateAllDialectVm(CreateStore());

        vm.SslMode.Should().Be(DbSslMode.Unspecified);
        vm.ToSettings().SslMode.Should().Be(DbSslMode.Unspecified);
    }

    /// <summary>TLS 要求水準がプロファイル選択で入力欄へ反映され、確定内容にも載ることを検証する</summary>
    [Fact(DisplayName = "TLS 要求水準は保存済み接続から復元され確定内容へ載る")]
    public void SslMode_RestoredFromProfile_AndFlowsIntoResult()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "PG",
                Dbms = "postgresql",
                Server = "pg01",
                Database = "shop",
                SslMode = DbSslMode.VerifyFull,
            },
            password: ""
        );

        var vm = CreateAllDialectVm(store);
        vm.SelectedProfileItem = vm.Profiles[0];

        vm.SslMode.Should().Be(DbSslMode.VerifyFull);

        vm.OkCommand.Execute(null);

        vm.Result.Should().NotBeNull();
        vm.Result!.SslMode.Should().Be(DbSslMode.VerifyFull);
        // 前回接続としても記録され、次回ダイアログで復元される
        new DbConnectionDialogViewModel(
            RegistryWithAllDialects,
            DbConnectionDialogMode.Import,
            fixedProvider: null,
            store
        )
            .SslMode.Should()
            .Be(DbSslMode.VerifyFull);
    }

    /// <summary>
    /// TLS 要求水準の選択肢が、全 6 値をローカライズ済みの表示名付きで並べることを検証する。
    /// </summary>
    /// <remarks>
    /// 表示名が列挙名そのままだと <c>Require</c>（証明書を検証しない）と <c>VerifyCa</c> の差が
    /// 画面から読み取れない——この設定の存在理由が伝わらないため、resx 由来であることを固定する。
    /// </remarks>
    [Fact(DisplayName = "TLS 要求水準の選択肢は全 6 値をローカライズ済み表示名で並べる")]
    public void SslModes_ExposeLocalizedDisplayNames()
    {
        var vm = CreateAllDialectVm(CreateStore());

        vm.SslModes.Select(i => i.Mode).Should().Equal(Enum.GetValues<DbSslMode>());
        vm.SslModes.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Display));
        vm.SslModes.Single(i => i.Mode == DbSslMode.VerifyFull)
            .Display.Should()
            .Be(Strings.DbConnection_SslMode_VerifyFull);
        // 生の列挙名そのままではない＝意味が読める表示になっている
        vm.SslModes.Single(i => i.Mode == DbSslMode.Require)
            .Display.Should()
            .NotBe(nameof(DbSslMode.Require));
    }

    /// <summary>Oracle 選択時だけ「既定は平文接続」の注記を出すことを検証する</summary>
    [Theory(DisplayName = "Oracle の平文接続の注記は Oracle でのみ表示する")]
    [InlineData("oracle", true)]
    [InlineData("postgresql", false)]
    [InlineData("sqlserver", false)]
    [InlineData("sqlite", false)]
    public void ShowOracleEncryptionNote_OnlyForOracle(string dbms, bool expected)
    {
        var vm = CreateAllDialectVm(CreateStore());
        vm.SelectedProvider = RegistryWithAllDialects.Get(dbms);

        vm.ShowOracleEncryptionNote.Should().Be(expected);
    }

    // ---------------- プロファイル保存の上書き確認（DU1） ----------------

    /// <summary>「本番DB」相当の既存プロファイル 1 件をストアへ用意する</summary>
    private static Guid SeedProfile(SqlConnectionProfileStore store, string name) =>
        SeedProfile(store, name, "prod.example.com", "prod-secret");

    /// <summary>名前・サーバー・パスワードを指定して既存プロファイルを 1 件用意する</summary>
    private static Guid SeedProfile(
        SqlConnectionProfileStore store,
        string name,
        string server,
        string password
    )
    {
        var profile = new SqlConnectionProfile
        {
            Name = name,
            Dbms = SqlServerProvider.ProviderName,
            Server = server,
            Database = "SalesDb",
            SavePassword = true,
        };
        store.Upsert(profile, password);
        return profile.Id;
    }

    /// <summary>
    /// 未選択のまま既存と同じ保存名で保存すると、上書き前に確認が出る（DU1）。
    /// </summary>
    /// <remarks>
    /// 保存名を打ち間違えた・使い回しただけで、本番の接続先と保存済みパスワードが警告なく失われていた。
    /// </remarks>
    [Fact(DisplayName = "保存: 選択していない同名プロファイルの上書きは確認を出す")]
    public void SaveProfile_OverwritingUnselectedProfile_AsksForConfirmation()
    {
        var store = CreateStore();
        var existingId = SeedProfile(store, "本番DB");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.Host = "test01";
        vm.Password = "dev-secret";
        vm.ProfileName = "本番DB";

        vm.SaveProfileCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));

        // キャンセルしたので既存の接続先もパスワードも変わらない
        var stored = store.LoadAll().Should().ContainSingle().Subject;
        stored.Id.Should().Be(existingId);
        stored.Server.Should().Be("prod.example.com");
        store.LoadPassword(existingId).Should().Be("prod-secret");
    }

    /// <summary>確認で続行を選べば、従来どおり同名プロファイルを上書きする（DU1）。</summary>
    [Fact(DisplayName = "保存: 確認で続行すれば同名プロファイルを上書きする")]
    public void SaveProfile_ConfirmedOverwrite_ReplacesExistingProfile()
    {
        var store = CreateStore();
        var existingId = SeedProfile(store, "本番DB");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateVm(store, dialogs);
        vm.Host = "test01";
        vm.ProfileName = "本番DB";

        vm.SaveProfileCommand.Execute(null);

        var stored = store.LoadAll().Should().ContainSingle().Subject;
        stored.Id.Should().Be(existingId);
        stored.Server.Should().Be("test01");
    }

    /// <summary>
    /// 選択中のプロファイル自身の上書き（読み込んで直して保存）は従来どおり確認しない（DU1）。
    /// </summary>
    [Fact(DisplayName = "保存: 選択中のプロファイル自身の上書きは確認を出さない")]
    public void SaveProfile_OverwritingSelectedProfile_DoesNotAsk()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.Host = "prod2.example.com";

        vm.SaveProfileCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        store.LoadAll().Should().ContainSingle().Which.Server.Should().Be("prod2.example.com");
    }

    /// <summary>
    /// 選択中のプロファイルを読み込んで別名で保存すると、確認なしで別のプロファイルになる（DU1・案 2）。
    /// </summary>
    /// <remarks>
    /// 「現在の入力内容を保存名で保存する」という保存ボタンの意味どおり、
    /// 既存のどれも上書きしないため確認は出さない（何も失われない）。
    /// </remarks>
    [Fact(DisplayName = "保存: 別名で保存すると確認なしで新しいプロファイルになる")]
    public void SaveProfile_NewName_CreatesAnotherProfileWithoutAsking()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.Host = "staging01";
        vm.ProfileName = "検証DB";

        vm.SaveProfileCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        store
            .LoadAll()
            .Select(profile => (profile.Name, profile.Server))
            .Should()
            .BeEquivalentTo([("本番DB", "prod.example.com"), ("検証DB", "staging01")]);
    }

    /// <summary>
    /// 別のプロファイルを選んだまま既存の保存名を打ち替えた場合も確認が出る（DU1・報告書の例 2）。
    /// </summary>
    /// <remarks>
    /// 選んでいるのは「本番DB」なのに、壊れるのは触っていないつもりの「検証DB」という形。
    /// </remarks>
    [Fact(DisplayName = "保存: 選択中と違うプロファイルの名前を打つと確認を出す")]
    public void SaveProfile_RenamingOntoAnotherProfile_AsksForConfirmation()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var stagingId = SeedProfile(store, "検証DB", "staging.example.com", "staging-secret");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles.Single(p => p.Profile.Name == "本番DB");
        vm.Host = "staging01";
        vm.ProfileName = "検証DB";

        vm.SaveProfileCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "検証DB"));
        store
            .LoadAll()
            .Single(profile => profile.Id == stagingId)
            .Server.Should()
            .Be("staging.example.com");
        store.LoadPassword(stagingId).Should().Be("staging-secret");
    }

    /// <summary>同名でも DB 種別が違えば別のプロファイルなので確認は出ない（DU1）。</summary>
    [Fact(DisplayName = "保存: 同名でも DB 種別が違えば確認を出さない")]
    public void SaveProfile_SameNameDifferentDbms_DoesNotAsk()
    {
        var store = CreateStore();
        SeedProfile(store, "共通名");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new DbConnectionDialogViewModel(
            RegistryWithSqlite,
            DbConnectionDialogMode.Import,
            fixedProvider: null,
            store,
            dialogs
        )
        {
            SelectedProvider = RegistryWithSqlite.Get(SqliteProvider.ProviderName),
        };
        vm.FilePath = Path.Combine(_tempFolder, "local.db");
        vm.ProfileName = "共通名";

        vm.SaveProfileCommand.Execute(null);

        dialogs.WarningConfirmMessages.Should().BeEmpty();
        store.LoadAll().Should().HaveCount(2);
    }

    // ---------------- プロファイルの名前の変更（DU3） ----------------

    /// <summary>
    /// 名前の変更は、選択中のプロファイルの名前だけを変え、複製を作らないことを検証する（DU3）。
    /// </summary>
    /// <remarks>
    /// 保存名がキーのため、名前を打ち替えて保存すると名前の変更ではなく複製になる。
    /// Id が変わらないのでパスワードの暗号ファイルもそのまま引き継がれる。
    /// </remarks>
    [Fact(DisplayName = "名前の変更はプロファイルを複製せず名前だけを変える")]
    public void RenameProfile_ChangesNameInPlace()
    {
        var store = CreateStore();
        var id = SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.ProfileName = "本番DB（東京）";

        vm.RenameProfileCommand.Execute(null);

        var stored = store.LoadAll().Should().ContainSingle().Subject;
        stored.Id.Should().Be(id);
        stored.Name.Should().Be("本番DB（東京）");
        // 接続の設定値は変わらない
        stored.Server.Should().Be("prod.example.com");
        // 保存済みのパスワードは引き継がれる（古い暗号ファイルも残らない＝Id が変わらないため）
        store.LoadPassword(id).Should().Be("prod-secret");
    }

    /// <summary>入力欄の編集内容は名前の変更で保存されないことを検証する（DU3）</summary>
    [Fact(DisplayName = "名前の変更は入力欄の編集内容を保存しない")]
    public void RenameProfile_DoesNotSaveEditedFields()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.Host = "typo-host";
        vm.ProfileName = "本番DB2";

        vm.RenameProfileCommand.Execute(null);

        store.LoadAll().Single().Server.Should().Be("prod.example.com");
    }

    /// <summary>名前を変えていない・未選択のときは名前の変更を実行できないことを検証する（DU3）</summary>
    [Fact(DisplayName = "未選択または同名では名前の変更は実行できない")]
    public void RenameProfile_RequiresSelectionAndDifferentName()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());

        // 未選択
        vm.ProfileName = "別名";
        vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();

        // 選択したが名前は同じ
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();

        // 名前を変えると実行できる
        vm.ProfileName = "本番DB（東京）";
        vm.RenameProfileCommand.CanExecute(null).Should().BeTrue();

        // 空欄は実行できない
        vm.ProfileName = "   ";
        vm.RenameProfileCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>
    /// 変更後の名前が別のプロファイルと衝突するときは確認を出し、続行するとそちらを置き換えることを検証する（DU3）。
    /// </summary>
    /// <remarks>
    /// 同じ名前のプロファイルが 2 つ並ぶと、名前で上書き先を決める保存がどちらを指すか決まらなくなる。
    /// </remarks>
    [Fact(DisplayName = "名前の変更が別のプロファイルと衝突すると確認のうえ置き換える")]
    public void RenameProfile_NameCollision_AsksThenReplaces()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var stagingId = SeedProfile(store, "検証DB", "staging.example.com", "staging-secret");
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles.Single(p => p.Profile.Name == "検証DB");
        vm.ProfileName = "本番DB";

        vm.RenameProfileCommand.Execute(null);

        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(string.Format(Strings.DbConnection_OverwriteProfileConfirm, "本番DB"));

        var stored = store.LoadAll().Should().ContainSingle().Subject;
        stored.Id.Should().Be(stagingId);
        stored.Name.Should().Be("本番DB");
        stored.Server.Should().Be("staging.example.com");
    }

    /// <summary>衝突の確認をキャンセルすると何も変わらないことを検証する（DU3）</summary>
    [Fact(DisplayName = "名前の変更の確認をキャンセルすると何も変わらない")]
    public void RenameProfile_CancelledCollision_ChangesNothing()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        SeedProfile(store, "検証DB", "staging.example.com", "staging-secret");
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = CreateVm(store, dialogs);
        vm.SelectedProfileItem = vm.Profiles.Single(p => p.Profile.Name == "検証DB");
        vm.ProfileName = "本番DB";

        vm.RenameProfileCommand.Execute(null);

        store
            .LoadAll()
            .Select(profile => profile.Name)
            .Should()
            .BeEquivalentTo(["本番DB", "検証DB"]);
    }

    /// <summary>全方言を登録した取込モードの ViewModel を生成する</summary>
    private DbConnectionDialogViewModel CreateAllDialectVm(SqlConnectionProfileStore store) =>
        new(RegistryWithAllDialects, DbConnectionDialogMode.Import, fixedProvider: null, store);

    // ---------------- 接続情報ファイルを読み取れないとき（MS1） ----------------

    /// <summary>接続情報ファイルを排他で掴んだまま操作させるためのハンドルを開く</summary>
    /// <remarks>
    /// 読み取り失敗の再現は <see cref="FileShare.None"/> で開いておくだけで決定的に作れる
    /// （人工的な並行・時間依存に頼らない）。開くのはファイルが既にある状態でだけ。
    /// </remarks>
    private static FileStream LockConnectionsFile(SqlConnectionProfileStore store) =>
        new(store.ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None);

    /// <summary>保存が中止されたときに、状態メッセージで知らせて何も変えないことを検証する（MS1）。</summary>
    /// <remarks>
    /// 受け止めないと <see cref="ConnectionProfileStoreUnavailableException"/> がそのまま
    /// WPF の未処理例外になってアプリが落ちる。利用者が明示的に頼んだ操作なので、
    /// このダイアログの他の失敗と同じくステータス行で伝える。
    /// </remarks>
    [Fact(DisplayName = "SaveProfile: 接続情報ファイルを読み取れないときはステータスで知らせる")]
    public void SaveProfile_WhenStoreUnavailable_ReportsStatusWithoutLosingProfiles()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());
        vm.Host = "new-server";
        vm.Database = "new-db";
        vm.ProfileName = "新しい接続";

        using (LockConnectionsFile(store))
        {
            vm.SaveProfileCommand.Execute(null);

            vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
            vm.Profiles.Should()
                .ContainSingle("一覧は空にしない")
                .Which.Profile.Name.Should()
                .Be("本番DB");
        }

        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("本番DB");
    }

    /// <summary>削除が中止されたときに、状態メッセージで知らせて選択も一覧も保つことを検証する（MS1）。</summary>
    [Fact(DisplayName = "DeleteProfile: 接続情報ファイルを読み取れないときはステータスで知らせる")]
    public void DeleteProfile_WhenStoreUnavailable_ReportsStatusAndKeepsSelection()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService { ConfirmResult = true });
        vm.SelectedProfileItem = vm.Profiles.Single();

        using (LockConnectionsFile(store))
        {
            vm.DeleteProfileCommand.Execute(null);

            vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
            vm.SelectedProfile.Should().NotBeNull("削除していないので選択も外さない");
            vm.Profiles.Should().ContainSingle();
        }

        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("本番DB");
    }

    /// <summary>名前の変更が中止されたときに、名前を書き換えずステータスで知らせることを検証する（MS1）。</summary>
    /// <remarks>
    /// 名前の書き換えは保存の前に行われるため、受け止めるだけでは「保存されていない名前が画面にだけ残る」。
    /// 中止したら表示も元へ戻す。
    /// </remarks>
    [Fact(DisplayName = "RenameProfile: 接続情報ファイルを読み取れないときは名前も書き換えない")]
    public void RenameProfile_WhenStoreUnavailable_RollsBackDisplayedName()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());
        vm.SelectedProfileItem = vm.Profiles.Single();
        vm.ProfileName = "本番DB（東京）";

        using (LockConnectionsFile(store))
        {
            vm.RenameProfileCommand.Execute(null);

            vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
            vm.Profiles.Single()
                .Profile.Name.Should()
                .Be("本番DB", "保存できていない名前を画面に残さない");
        }

        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("本番DB");
    }

    /// <summary>
    /// 名前の衝突を置き換える経路でも、読み取れないときは<b>どちらのプロファイルも消えない</b>ことを
    /// 検証する（MS1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// この経路は同じファイルへ 2 回書く（衝突相手の削除 → 置き換え）。実装では 1 つの <c>try</c> に
    /// まとめて 1 つ目で止まったら 2 つ目へ進まないようにしてある——進むと、同じ名前のプロファイルが
    /// 2 つ並んで「保存がどちらを指すか決まらない」状態になるため。
    /// </para>
    /// <para>
    /// <b>ここで固定できるのは「どちらも消えない」まで。</b>「削除だけ成功して置き換えが失敗する」
    /// 中間状態は、読み取りが 1 回目だけ失敗して 2 回目は成功するという一過性の並びでしか起きず、
    /// ストアに差し替え口が無い現状では決定的に再現できない（人工的な時間依存のテストは書かない）。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "RenameProfile: 読み取れないときは衝突相手も対象も消えない")]
    public void RenameProfile_WhenStoreUnavailableWithConflict_KeepsBothProfiles()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        SeedProfile(store, "旧本番DB");
        var vm = CreateVm(store, new StubDialogService { ConfirmResult = true });
        vm.SelectedProfileItem = vm.Profiles.Single(item => item.Profile.Name == "旧本番DB");
        vm.ProfileName = "本番DB";

        using (LockConnectionsFile(store))
        {
            vm.RenameProfileCommand.Execute(null);

            vm.StatusMessage.Should().Be(Strings.DbConnection_ProfileStoreUnavailable);
        }

        store
            .LoadAll()
            .Select(profile => profile.Name)
            .Should()
            .BeEquivalentTo(new[] { "本番DB", "旧本番DB" }, "どちらも消えていない");
    }

    /// <summary>
    /// OK 確定は、前回接続を記録できなくても黙って続行することを検証する（MS1）。
    /// </summary>
    /// <remarks>
    /// ここでの保存は確定の「ついで」の記録で、書けなくてもファイルの中身は前のまま＝何も失われない。
    /// 記録できないことを理由に接続そのものを止めるのは釣り合わないため、確定は通す。
    /// </remarks>
    [Fact(DisplayName = "Ok: 前回接続を記録できなくても確定は続行する")]
    public void Ok_WhenStoreUnavailable_StillConfirms()
    {
        var store = CreateStore();
        SeedProfile(store, "本番DB");
        var vm = CreateVm(store, new StubDialogService());
        vm.Host = "srv";
        vm.Database = "db";
        bool? closed = null;
        vm.CloseAction = result => closed = result;

        using (LockConnectionsFile(store))
        {
            vm.OkCommand.Execute(null);
        }

        closed.Should().BeTrue("記録できないだけで確定は妨げない");
        vm.Result.Should().NotBeNull();
        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("本番DB");
    }
}
