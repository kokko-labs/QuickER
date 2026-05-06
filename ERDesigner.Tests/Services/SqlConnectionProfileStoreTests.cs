using System.IO;
using System.Linq;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SqlConnectionProfileStore"/> の保存・読込・削除・パスワード往復を検証するテスト。
/// DPAPI は単体テスト環境差異を避けるため <c>useDpapi:false</c> で平文保存して検証します。
/// </summary>
public class SqlConnectionProfileStoreTests : IDisposable
{
    private readonly string _tempFolder;

    public SqlConnectionProfileStoreTests()
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

    [Fact(DisplayName = "Upsert で追加され LoadAll が名前順で返す")]
    public void Upsert_AddsAndLoadsSorted()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "Zeta",
                Server = "z",
                Database = "d",
            },
            password: ""
        );
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "alpha",
                Server = "a",
                Database = "d",
            },
            password: ""
        );
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "Mid",
                Server = "m",
                Database = "d",
            },
            password: ""
        );

        var list = store.LoadAll();

        list.Select(p => p.Name).Should().ContainInOrder("alpha", "Mid", "Zeta");
    }

    [Fact(DisplayName = "Upsert で同じ Id は上書きされる")]
    public void Upsert_SameId_Overwrites()
    {
        var store = CreateStore();
        var p = new SqlConnectionProfile
        {
            Name = "P1",
            Server = "old",
            Database = "d",
        };

        store.Upsert(p, password: "");
        p.Server = "new";
        store.Upsert(p, password: "");

        var list = store.LoadAll();
        list.Should().HaveCount(1);
        list[0].Server.Should().Be("new");
    }

    [Fact(DisplayName = "Delete でプロファイルとパスワード両方が消える")]
    public void Delete_RemovesProfileAndSecret()
    {
        var store = CreateStore();
        var p = new SqlConnectionProfile
        {
            Name = "P",
            Server = "s",
            Database = "d",
            SavePassword = true,
        };

        store.Upsert(p, password: "secret");
        store.LoadPassword(p.Id).Should().Be("secret");

        store.Delete(p.Id);

        store.LoadAll().Should().BeEmpty();
        store.LoadPassword(p.Id).Should().BeEmpty();
    }

    [Fact(DisplayName = "SavePassword=true なら復号で同じ値を取り出せる")]
    public void Password_RoundTrip()
    {
        var store = CreateStore();
        var p = new SqlConnectionProfile
        {
            Name = "P",
            Server = "s",
            Database = "d",
            SavePassword = true,
        };

        store.Upsert(p, password: "P@ssw0rd!日本語");

        store.LoadPassword(p.Id).Should().Be("P@ssw0rd!日本語");
    }

    [Fact(DisplayName = "SavePassword=false なら以前保存したパスワードは削除される")]
    public void Upsert_SavePasswordFalse_DeletesSecret()
    {
        var store = CreateStore();
        var p = new SqlConnectionProfile
        {
            Name = "P",
            Server = "s",
            Database = "d",
            SavePassword = true,
        };

        store.Upsert(p, password: "secret");
        store.LoadPassword(p.Id).Should().Be("secret");

        p.SavePassword = false;
        store.Upsert(p, password: "secret");

        store.LoadPassword(p.Id).Should().BeEmpty();
    }

    [Fact(DisplayName = "前回接続情報はデータベース名を含めて保存・復元される")]
    public void LastUsed_RoundTrip_RestoresDatabase()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Server = "sql01",
            Database = "SalesDb",
            AuthMode = SqlAuthMode.SqlServer,
            UserId = "sa",
            TrustServerCertificate = false,
            SavePassword = true,
        };

        store.SaveLastUsed(profile, "secret");
        var lastUsed = store.LoadLastUsed();

        lastUsed.Should().NotBeNull();
        lastUsed!.Value.Profile.Server.Should().Be("sql01");
        lastUsed.Value.Profile.Database.Should().Be("SalesDb");
        lastUsed.Value.Profile.UserId.Should().Be("sa");
        lastUsed.Value.Profile.TrustServerCertificate.Should().BeFalse();
        lastUsed.Value.Password.Should().Be("secret");
    }
}
