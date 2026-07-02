using System.IO;
using FluentAssertions;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary><see cref="DbConnectionDialogViewModel"/> の初期表示・プロファイル選択・保存・削除・方言固定を検証するテストクラス</summary>
public class DbConnectionDialogViewModelTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _tempFolder;

    /// <summary>SQL Server のみを登録したレジストリ</summary>
    private static readonly DatabaseProviderRegistry Registry = new(
        new IDatabaseProvider[] { new SqlServerProvider() }
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
        reopened.StatusMessage.Should().Be("前回接続情報を復元しました。");
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
        vm.StatusMessage.Should().Contain("削除しました");
    }
}
