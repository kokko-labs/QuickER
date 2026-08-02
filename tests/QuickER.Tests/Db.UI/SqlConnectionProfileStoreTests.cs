using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;

namespace QuickER.Tests.Db.UI;

/// <summary><see cref="SqlConnectionProfileStore"/> の保存・読込・削除・パスワード往復を検証するテストクラス</summary>
/// <remarks>環境差異を避けるため <c>useDpapi:false</c> で平文保存して検証する</remarks>
public class SqlConnectionProfileStoreTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _tempFolder;

    /// <summary>一時保存先フォルダを作成する</summary>
    public SqlConnectionProfileStoreTests()
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

    /// <summary>Upsert で追加したプロファイルを LoadAll が名前順で返すことを検証する</summary>
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

    /// <summary>同一 Id の Upsert が既存プロファイルを上書きすることを検証する</summary>
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

    /// <summary>Delete がプロファイルと暗号化パスワードの両方を削除することを検証する</summary>
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

    /// <summary>SavePassword=true で保存したパスワードが復号で同値復元できることを検証する</summary>
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

    /// <summary>SavePassword を false にして Upsert すると既存パスワードが削除されることを検証する</summary>
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

    /// <summary>Dbms フィールドを欠くプロファイルを読み込むと sqlserver とみなされることを検証する</summary>
    [Fact(DisplayName = "Dbms 欠落のプロファイルは sqlserver として読み込まれる")]
    public void LoadAll_ProfileWithoutDbms_DefaultsToSqlServer()
    {
        var store = CreateStore();
        // Dbms / Port / ServiceName を持たないプロファイルを新形式（profiles 配列）で直接書き出す
        var json =
            "{\"profiles\":[{\"id\":\""
            + Guid.NewGuid().ToString()
            + "\",\"name\":\"Legacy\",\"server\":\"srv\",\"database\":\"db\",\"authMode\":0,\"userId\":\"\",\"trustServerCertificate\":true,\"savePassword\":false}]}";
        File.WriteAllText(store.ConnectionsPath, json);

        var list = store.LoadAll();

        list.Should().ContainSingle();
        list[0].Dbms.Should().Be("sqlserver");
        list[0].Port.Should().BeNull();
    }

    /// <summary>旧配列形式の connections.json は新形式として読めず空一覧へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "旧配列形式の connections.json は空一覧へフォールバックする")]
    public void LoadAll_LegacyArrayFormat_FallsBackToEmpty()
    {
        var store = CreateStore();
        // 旧構成のトップレベル配列形式（新形式のオブジェクトではない）を直接書き出す
        var legacyArrayJson =
            "[{\"id\":\""
            + Guid.NewGuid().ToString()
            + "\",\"name\":\"Legacy\",\"server\":\"srv\",\"database\":\"db\"}]";
        File.WriteAllText(store.ConnectionsPath, legacyArrayJson);

        store.LoadAll().Should().BeEmpty();
    }

    /// <summary>SaveAll が前回接続情報（LastUsed）を消さないことを検証する（read-modify-write の相互不干渉）</summary>
    [Fact(DisplayName = "SaveAll は前回接続情報を消さない")]
    public void SaveAll_PreservesLastUsed()
    {
        var store = CreateStore();
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Name = "Last",
                Server = "sql01",
                Database = "db",
            },
            password: ""
        );

        // プロファイル一覧を保存しても前回接続情報は温存されるべき
        store.SaveAll(
            new[]
            {
                new SqlConnectionProfile
                {
                    Name = "P",
                    Server = "s",
                    Database = "d",
                },
            }
        );

        var lastUsed = store.LoadLastUsed();
        lastUsed.Should().NotBeNull();
        lastUsed!.Value.Profile.Server.Should().Be("sql01");
    }

    /// <summary>SaveLastUsed がプロファイル一覧（Profiles）を消さないことを検証する（read-modify-write の相互不干渉）</summary>
    [Fact(DisplayName = "SaveLastUsed はプロファイル一覧を消さない")]
    public void SaveLastUsed_PreservesProfiles()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "P",
                Server = "s",
                Database = "d",
            },
            password: ""
        );

        // 前回接続情報を保存してもプロファイル一覧は温存されるべき
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Name = "Last",
                Server = "sql01",
                Database = "db",
            },
            password: ""
        );

        var list = store.LoadAll();
        list.Should().ContainSingle();
        list[0].Name.Should().Be("P");
    }

    /// <summary>Dbms / Port / ServiceName を含めて往復保存・復元されることを検証する</summary>
    [Fact(DisplayName = "Dbms・Port を含めて保存・復元される")]
    public void Upsert_PreservesDbmsAndPort()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Name = "PG",
            Dbms = "postgresql",
            Server = "pg-host",
            Port = 5432,
            Database = "app",
            AuthMode = DbAuthMode.UsernamePassword,
            UserId = "postgres",
        };

        store.Upsert(profile, password: "");
        var list = store.LoadAll();

        list.Should().ContainSingle();
        list[0].Dbms.Should().Be("postgresql");
        list[0].Port.Should().Be(5432);
    }

    /// <summary>前回接続情報がデータベース名・認証情報・パスワードを含めて往復保存・復元されることを検証する</summary>
    [Fact(DisplayName = "前回接続情報はデータベース名を含めて保存・復元される")]
    public void LastUsed_RoundTrip_RestoresDatabase()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Server = "sql01",
            Database = "SalesDb",
            AuthMode = DbAuthMode.UsernamePassword,
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
