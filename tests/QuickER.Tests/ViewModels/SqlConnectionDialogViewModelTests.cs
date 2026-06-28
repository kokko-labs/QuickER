using System.IO;
using FluentAssertions;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary><see cref="SqlConnectionDialogViewModel"/> の初期表示・プロファイル選択・保存・削除を検証するテストクラス</summary>
public class SqlConnectionDialogViewModelTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _tempFolder;

    /// <summary>一時保存先フォルダを作成する</summary>
    public SqlConnectionDialogViewModelTests()
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

        var vm = new SqlConnectionDialogViewModel(store);

        vm.SelectedProfile.Should().BeNull();
        vm.Server.Should().Be("localhost");
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
                AuthMode = SqlAuthMode.SqlServer,
                UserId = "sa",
                TrustServerCertificate = false,
                SavePassword = true,
            },
            password: "secret"
        );

        var vm = new SqlConnectionDialogViewModel(store)
        {
            Server = "restored-server",
            Database = "restored-db",
            ProfileName = "復元済み",
        };

        vm.SelectedProfile = vm.Profiles[0];

        vm.Server.Should().Be("saved-server");
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
        var vm = new SqlConnectionDialogViewModel(store)
        {
            Server = "restored-server",
            Database = "restored-db",
            AuthMode = SqlAuthMode.SqlServer,
            UserId = "sa",
            Password = "secret",
            SavePassword = true,
        };

        vm.OkCommand.Execute(null);

        var reopened = new SqlConnectionDialogViewModel(store);

        reopened.Server.Should().Be("restored-server");
        reopened.Database.Should().Be("restored-db");
        reopened.UserId.Should().Be("sa");
        reopened.Password.Should().Be("secret");
        reopened.StatusMessage.Should().Be("前回接続情報を復元しました。");
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
        var vm = new SqlConnectionDialogViewModel(store, dialogs);
        vm.SelectedProfile = vm.Profiles[0];

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
        var vm = new SqlConnectionDialogViewModel(store, dialogs);
        vm.SelectedProfile = vm.Profiles[0];

        vm.DeleteProfileCommand.Execute(null);

        vm.Profiles.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("削除しました");
    }
}
