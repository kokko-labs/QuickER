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

    /// <summary>暗号ファイルが実在するか（フラグとの整合を確かめるために直接見る）</summary>
    private bool SecretExists(SqlConnectionProfileStore store, Guid id) =>
        File.Exists(Path.Combine(store.SecretsFolder, id.ToString("N") + ".dat"));

    /// <summary>
    /// 保存した <c>SavePassword</c> と暗号ファイルの有無が常に一致することを検証する（DU7・4 分岐）。
    /// </summary>
    /// <remarks>
    /// 「パスワードを保存」をオンにしたまま空欄で保存すると暗号ファイルは書かれないため、
    /// フラグだけが立つと「保存済みと表示されるのに復元するものが無い」状態になる。
    /// </remarks>
    [Theory(DisplayName = "Upsert: SavePassword と暗号ファイルの有無は常に一致する")]
    [InlineData(true, "secret", true)]
    [InlineData(true, "", false)]
    [InlineData(false, "secret", false)]
    [InlineData(false, "", false)]
    public void Upsert_SavePasswordFlag_MatchesSecretFile(
        bool savePassword,
        string password,
        bool expected
    )
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Name = "TestDB",
            Server = "s",
            Database = "d",
            SavePassword = savePassword,
        };

        store.Upsert(profile, password);

        SecretExists(store, profile.Id).Should().Be(expected);
        store.LoadAll().Should().ContainSingle().Which.SavePassword.Should().Be(expected);
        profile.SavePassword.Should().Be(expected, "呼び出し側のインスタンスも保存内容へ揃える");
    }

    [Theory(DisplayName = "SaveLastUsed: SavePassword と暗号ファイルの有無は常に一致する")]
    [InlineData(true, "secret", true)]
    [InlineData(true, "", false)]
    [InlineData(false, "secret", false)]
    [InlineData(false, "", false)]
    public void SaveLastUsed_SavePasswordFlag_MatchesSecretFile(
        bool savePassword,
        string password,
        bool expected
    )
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Server = "s",
            Database = "d",
            SavePassword = savePassword,
        };

        store.SaveLastUsed(profile, password);

        File.Exists(Path.Combine(store.SecretsFolder, "last-connection.dat")).Should().Be(expected);

        var restored = store.LoadLastUsed();
        restored.Should().NotBeNull();
        restored!.Value.Profile.SavePassword.Should().Be(expected);
        restored.Value.Password.Should().Be(expected ? password : string.Empty);
    }

    /// <summary>
    /// 保存済みパスワードを空欄で上書きしたら、フラグも暗号ファイルも落ちることを検証する（DU7）。
    /// </summary>
    [Fact(DisplayName = "Upsert: 保存済みパスワードを空欄で上書きするとフラグごと落ちる")]
    public void Upsert_ClearingPassword_DropsFlagAndSecret()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Name = "TestDB",
            Server = "s",
            Database = "d",
            SavePassword = true,
        };
        store.Upsert(profile, "secret");
        SecretExists(store, profile.Id).Should().BeTrue();

        // チェックはオンのまま、パスワード欄だけを空にして保存し直す
        profile.SavePassword = true;
        store.Upsert(profile, string.Empty);

        SecretExists(store, profile.Id).Should().BeFalse();
        store.LoadAll().Should().ContainSingle().Which.SavePassword.Should().BeFalse();
        store.LoadPassword(profile.Id).Should().BeEmpty();
    }

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

    /// <summary>ファイル未作成の状態から Upsert（SaveData 経由）で connections.json が新規作成されることを検証する（原子的保存の新規作成経路）</summary>
    [Fact(DisplayName = "未保存フォルダへの Upsert で connections.json が新規作成される")]
    public void Upsert_WhenFileMissing_CreatesConnectionsFile()
    {
        var store = CreateStore();

        File.Exists(store.ConnectionsPath).Should().BeFalse();

        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "P",
                Server = "s",
                Database = "d",
            },
            password: ""
        );

        File.Exists(store.ConnectionsPath).Should().BeTrue();
    }

    /// <summary>既存の connections.json がある状態での保存が内容を正しく置換することを検証する（原子的保存の置換経路）</summary>
    [Fact(DisplayName = "既存の connections.json がある保存は内容を置換する")]
    public void SaveLastUsed_WhenFileExists_ReplacesContent()
    {
        var store = CreateStore();
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Name = "Old",
                Server = "old-host",
                Database = "d",
            },
            password: ""
        );

        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Name = "New",
                Server = "new-host",
                Database = "d",
            },
            password: ""
        );

        var lastUsed = store.LoadLastUsed();
        lastUsed.Should().NotBeNull();
        lastUsed!.Value.Profile.Server.Should().Be("new-host");
    }

    /// <summary>connections.json への保存後に一時ファイル（原子的保存の中間生成物）が残らないことを検証する</summary>
    [Fact(DisplayName = "保存後に tmp ファイルが残らない")]
    public void SaveData_DoesNotLeaveTempFile()
    {
        var store = CreateStore();

        // 新規作成・上書きの両経路を確認する
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "P1",
                Server = "s1",
                Database = "d",
            },
            password: ""
        );
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "P2",
                Server = "s2",
                Database = "d",
            },
            password: ""
        );

        Directory.GetFiles(_tempFolder, "*.tmp").Should().BeEmpty();
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

    /// <summary>TLS 要求水準がプロファイルの保存・読込で保たれることを検証する</summary>
    [Fact(DisplayName = "TLS 要求水準はプロファイルの往復で保たれる")]
    public void SslMode_RoundTrip_IsPreserved()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile
        {
            Name = "PG",
            Dbms = "postgresql",
            Server = "pg01",
            Database = "shop",
            SslMode = DbSslMode.VerifyFull,
        };

        store.Upsert(profile, password: "");
        store.SaveLastUsed(profile, password: "");

        store.LoadAll()[0].SslMode.Should().Be(DbSslMode.VerifyFull);
        store.LoadLastUsed()!.Value.Profile.SslMode.Should().Be(DbSslMode.VerifyFull);
        profile.ToSettings("").SslMode.Should().Be(DbSslMode.VerifyFull);
    }

    /// <summary>TLS 要求水準が JSON へ<b>名前で</b>書かれることを検証する</summary>
    /// <remarks>
    /// 整数で保存すると、将来 <see cref="DbSslMode"/> の途中へメンバーを挿入した瞬間、保存済みの
    /// 設定が無言で別の水準として読み直される（<c>5</c> が VerifyFull から VerifyCa へずれる）。
    /// 名前で保存していればその事故が起きない。
    /// </remarks>
    [Fact(DisplayName = "TLS 要求水準は JSON へ名前で保存される")]
    public void SslMode_IsPersistedByName()
    {
        var store = CreateStore();
        store.Upsert(
            new SqlConnectionProfile
            {
                Name = "PG",
                Dbms = "postgresql",
                SslMode = DbSslMode.VerifyFull,
            },
            password: ""
        );

        var json = File.ReadAllText(store.ConnectionsPath);

        json.Should().Contain("\"sslMode\": \"VerifyFull\"");
        json.Should().NotContain("\"sslMode\": 5");
    }

    /// <summary>
    /// 整数で書かれた TLS 要求水準（名前保存へ切り替える前のファイル）も、従来どおり読めることを検証する。
    /// </summary>
    /// <remarks>
    /// System.Text.Json の既定（<c>allowIntegerValues: true</c>）に依存する後方互換。
    /// ここが壊れると、既に保存済みの検証要求が読み込み失敗で既定へ落ちる。
    /// </remarks>
    [Fact(DisplayName = "整数で書かれた旧 TLS 要求水準も読める")]
    public void SslMode_WrittenAsInteger_IsStillReadable()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(
            store.ConnectionsPath,
            """
            {
              "profiles": [
                { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "sslMode": 5 }
              ]
            }
            """
        );

        store.LoadAll()[0].SslMode.Should().Be(DbSslMode.VerifyFull);
    }

    /// <summary>
    /// TLS 要求水準のキーを持たない旧 JSON が、既定（未指定＝ドライバ既定）で読み込まれることを検証する。
    /// </summary>
    /// <remarks>
    /// この 1 点が「既存プロファイルの接続が黙って変わらない」ことの保証にあたる（既定が
    /// <see cref="DbSslMode.Unspecified"/> ＝接続文字列へキーワードを載せない）。
    /// </remarks>
    [Fact(DisplayName = "TLS 要求水準を持たない旧プロファイル JSON は未指定として読み込まれる")]
    public void LegacyJsonWithoutSslMode_LoadsAsUnspecified()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(
            store.ConnectionsPath,
            """
            {
              "profiles": [
                { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "database": "shop" }
              ],
              "lastUsed": { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "database": "shop" }
            }
            """
        );

        store.LoadAll()[0].SslMode.Should().Be(DbSslMode.Unspecified);
        store.LoadLastUsed()!.Value.Profile.SslMode.Should().Be(DbSslMode.Unspecified);
    }

    /// <summary>
    /// 接続情報ファイルを読み取れないときは、書き込みを中止して専用の例外を投げることを検証する（MS1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 保存はどれも「読み込み → 変更 → 書き込み」なので、読み取り失敗を空データへ落とすと、その空が
    /// 保存先へ書き戻って<b>登録済みプロファイルが全件消える</b>。読み取り失敗はウイルス対策ソフトの
    /// 一瞬のロックや別プロセスの保存中に実際に起こる。
    /// </para>
    /// <para>
    /// 再現は別のハンドルで <see cref="FileShare.None"/> のまま開いておくだけで決定的に作れる
    /// （人工的な並行・時間依存に頼らない）。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "読み取れないときの書き込みは中止され、プロファイルは消えない")]
    public void Writes_WhenFileUnreadable_AreAbortedAndProfilesSurvive()
    {
        var store = CreateStore();
        var kept = new SqlConnectionProfile
        {
            Name = "本番",
            Server = "prod",
            Database = "shop",
        };
        store.Upsert(kept, password: "");
        store.SaveLastUsed(
            new SqlConnectionProfile
            {
                Name = "本番",
                Server = "prod",
                Database = "shop",
            },
            password: ""
        );

        var original = File.ReadAllText(store.ConnectionsPath);

        using (
            new FileStream(store.ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            var added = new SqlConnectionProfile
            {
                Name = "新規",
                Server = "s",
                Database = "d",
            };

            FluentActions
                .Invoking(() => store.Upsert(added, password: ""))
                .Should()
                .Throw<ConnectionProfileStoreUnavailableException>();
            FluentActions
                .Invoking(() => store.Delete(kept.Id))
                .Should()
                .Throw<ConnectionProfileStoreUnavailableException>();
            FluentActions
                .Invoking(() => store.SaveAll(new List<SqlConnectionProfile>()))
                .Should()
                .Throw<ConnectionProfileStoreUnavailableException>();
            FluentActions
                .Invoking(() =>
                    store.SaveLastUsed(
                        new SqlConnectionProfile { Name = "別", Server = "x" },
                        password: ""
                    )
                )
                .Should()
                .Throw<ConnectionProfileStoreUnavailableException>();
        }

        File.ReadAllText(store.ConnectionsPath)
            .Should()
            .Be(original, "読めなかったので 1 バイトも書いていない");
        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("本番");
        store.LoadLastUsed().Should().NotBeNull();
    }

    /// <summary>読み取れないときの書き込み中止は、渡したインスタンスも書き換えないことを検証する（MS1）。</summary>
    /// <remarks>
    /// <see cref="SqlConnectionProfileStore.Upsert"/> は <c>SavePassword</c> を正規化して
    /// 呼び出し側のインスタンスを書き換えるが、保存を中止したのに書き換えだけ残ると
    /// 「保存していないのに画面の表示だけ変わる」ことになる。
    /// </remarks>
    [Fact(DisplayName = "読み取れないときの中止は呼び出し側のインスタンスも変えない")]
    public void Upsert_WhenFileUnreadable_DoesNotMutateArgument()
    {
        var store = CreateStore();
        store.Upsert(new SqlConnectionProfile { Name = "本番", Server = "prod" }, password: "");

        using (
            new FileStream(store.ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            var profile = new SqlConnectionProfile
            {
                Name = "新規",
                Server = "s",
                SavePassword = true,
            };

            FluentActions
                .Invoking(() => store.Upsert(profile, password: ""))
                .Should()
                .Throw<ConnectionProfileStoreUnavailableException>();

            profile.SavePassword.Should().BeTrue("保存を中止した以上、正規化もしない");
        }
    }

    /// <summary>読み取りの公開 API は読めなくても例外を投げず、UI を止めないことを検証する（MS1）。</summary>
    /// <remarks>
    /// 表示は「並べるものが無い」で続行してよい（何も失われない）。止めるのは書き込みだけ、という非対称を固定する。
    /// </remarks>
    [Fact(DisplayName = "読み取れないときも読み取り API は空で返る（UI を止めない）")]
    public void Reads_WhenFileUnreadable_FallBackToEmptyWithoutThrowing()
    {
        var store = CreateStore();
        var profile = new SqlConnectionProfile { Name = "本番", Server = "prod" };
        store.Upsert(profile, password: "");

        using (
            new FileStream(store.ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            store.LoadAll().Should().BeEmpty();
            store.LoadLastUsed().Should().BeNull();
            store.LoadPassword(profile.Id).Should().BeEmpty();
        }
    }

    /// <summary>ファイルが無いだけなら従来どおり空で読め、保存も普通にできることを検証する（MS1 の反対側）。</summary>
    /// <remarks>
    /// 「無い」を「読めない」と取り違えると、初回起動でプロファイルを 1 件も保存できなくなる。
    /// </remarks>
    [Fact(DisplayName = "ファイルが無いだけなら空で読め、保存もできる")]
    public void Writes_WhenFileMissing_StillSucceed()
    {
        var store = CreateStore();

        File.Exists(store.ConnectionsPath).Should().BeFalse();
        store.LoadAll().Should().BeEmpty();

        store.Upsert(new SqlConnectionProfile { Name = "初回", Server = "s" }, password: "");

        store.LoadAll().Should().ContainSingle().Which.Name.Should().Be("初回");
    }

    /// <summary>壊れた JSON は最初の保存で 1 回だけ退避されることを検証する（MS1）。</summary>
    /// <remarks>
    /// 破損時は従来どおり空データで続行して保存できる（＝正常化できる）が、上書きの前に
    /// 元の内容を残す。残すのは最も古い退避＝最後に正しく読めていた状態にいちばん近いもの。
    /// </remarks>
    [Fact(DisplayName = "壊れた connections.json は最初の 1 回だけ .corrupt へ退避する")]
    public void Save_CorruptConnectionsFile_BacksUpOnlyOnce()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(store.ConnectionsPath, "壊れた 1 回目");

        store.Upsert(new SqlConnectionProfile { Name = "復旧", Server = "s" }, password: "");

        File.Exists(store.CorruptBackupPath).Should().BeTrue();
        File.ReadAllText(store.CorruptBackupPath).Should().Be("壊れた 1 回目");
        store.LoadAll().Should().ContainSingle();

        File.WriteAllText(store.ConnectionsPath, "壊れた 2 回目");
        store.Upsert(new SqlConnectionProfile { Name = "復旧 2", Server = "s" }, password: "");

        File.ReadAllText(store.CorruptBackupPath)
            .Should()
            .Be("壊れた 1 回目", "最も古い退避を残す");
    }
}
