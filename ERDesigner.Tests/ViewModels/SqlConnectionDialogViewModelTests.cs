using System.IO;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="SqlConnectionDialogViewModel"/> の初期表示と保存済み接続選択の挙動を検証するテスト。
/// </summary>
public class SqlConnectionDialogViewModelTests : IDisposable
{
    private readonly string _tempFolder;

    public SqlConnectionDialogViewModelTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "ERDesignerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

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
        { /* テスト後のベストエフォート */
        }
    }

    private SqlConnectionProfileStore CreateStore() => new(_tempFolder, useDpapi: false);

    [Fact(DisplayName = "初期表示では保存済み接続を自動選択しない")]
    public void Constructor_DoesNotAutoSelectSavedProfile()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "SampleDB",
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

    [Fact(DisplayName = "保存済み接続を選択したときだけ入力欄へ反映する")]
    public void SelectedProfile_UpdatesEditableFields()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "SampleDB",
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
        vm.ProfileName.Should().Be("SampleDB");
        vm.StatusMessage.Should().Contain("SampleDB");
    }

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
}
