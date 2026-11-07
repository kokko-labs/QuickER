using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>正常な保存・読込・削除・名前の変更（connections.json が無いか読める状態からの往復）</summary>
    private static IEnumerable<ScenarioRow<ProfileScenario>> BasicRows() =>
        [
            // 「パスワードを保存」をオンにしたまま空欄で保存すると暗号ファイルは書かれない。
            // フラグだけが立つと「保存済みと表示されるのに復元するものが無い」状態になる
            SaveFlagUpsertRow("save-with-password", true, "secret", true),
            SaveFlagUpsertRow("save-with-empty-password", true, "", false),
            SaveFlagUpsertRow("no-save-with-password", false, "secret", false),
            SaveFlagUpsertRow("no-save-with-empty-password", false, "", false),
            SaveFlagLastUsedRow("save-with-password", true, "secret", true),
            SaveFlagLastUsedRow("save-with-empty-password", true, "", false),
            SaveFlagLastUsedRow("no-save-with-password", false, "secret", false),
            SaveFlagLastUsedRow("no-save-with-empty-password", false, "", false),
            Row(
                "basic-upsert-clearing-password-drops-flag-and-secret",
                "Upsert: 保存済みパスワードを空欄で上書きするとフラグごと落ちる",
                Upsert(
                    "p",
                    Prof("TestDB", "s", "d", save: true),
                    "secret",
                    expect: o => o.SecretExists("p").Should().BeTrue()
                ),
                // チェックはオンのまま、パスワード欄だけを空にして保存し直す
                Upsert(
                    "p",
                    Edit("p", p => p.SavePassword = true),
                    "",
                    expect: o => o.SecretExists("p").Should().BeFalse()
                ),
                Read(o => o.All.Should().ContainSingle().Which.SavePassword.Should().BeFalse()),
                LoadPassword("p", o => o.Password.Value.Should().BeEmpty())
            ),
            Row(
                "basic-upsert-adds-and-load-all-sorted-by-name",
                "Upsert で追加され Read が名前順で返す",
                Upsert("z", Prof("Zeta", "z", "d")),
                Upsert("a", Prof("alpha", "a", "d")),
                Upsert("m", Prof("Mid", "m", "d")),
                Read(o => o.All.Select(p => p.Name).Should().ContainInOrder("alpha", "Mid", "Zeta"))
            ),
            Row(
                "basic-upsert-same-id-overwrites",
                "Upsert で同じ Id は上書きされる",
                Upsert("p", Prof("P1", "old", "d")),
                Upsert("p", Edit("p", p => p.Server = "new")),
                Read(o =>
                {
                    o.All.Should().HaveCount(1);
                    o.All[0].Server.Should().Be("new");
                })
            ),
            Row(
                "basic-delete-removes-profile-and-secret",
                "Delete でプロファイルとパスワード両方が消える",
                Upsert("p", Prof("P", "s", "d", save: true), "secret"),
                LoadPassword("p", o => o.Password.Value.Should().Be("secret")),
                Delete("p"),
                Read(o => o.All.Should().BeEmpty()),
                LoadPassword("p", o => o.Password.Value.Should().BeEmpty())
            ),
            Row(
                "basic-password-round-trip-non-ascii",
                "SavePassword=true なら復号で同じ値を取り出せる",
                Upsert("p", Prof("P", "s", "d", save: true), "P@ssw0rd!日本語"),
                LoadPassword("p", o => o.Password.Value.Should().Be("P@ssw0rd!日本語"))
            ),
            Row(
                "basic-upsert-save-off-deletes-secret",
                "SavePassword=false なら以前保存したパスワードは削除される",
                Upsert("p", Prof("P", "s", "d", save: true), "secret"),
                LoadPassword("p", o => o.Password.Value.Should().Be("secret")),
                Upsert("p", Edit("p", p => p.SavePassword = false), "secret"),
                LoadPassword("p", o => o.Password.Value.Should().BeEmpty())
            ),
            // read-modify-write の相互不干渉（次の行と対）
            Row(
                "basic-upsert-preserves-last-used",
                "Upsert は前回接続情報を消さない",
                SaveLast(Prof("Last", "sql01", "db")),
                Upsert("p", Prof("P", "s", "d")),
                Read(o =>
                {
                    o.Last.Should().NotBeNull();
                    o.Last!.Value.Profile.Server.Should().Be("sql01");
                })
            ),
            Row(
                "basic-save-last-used-preserves-profiles",
                "SaveLastUsed はプロファイル一覧を消さない",
                Upsert("p", Prof("P", "s", "d")),
                SaveLast(Prof("Last", "sql01", "db")),
                Read(o =>
                {
                    o.All.Should().ContainSingle();
                    o.All[0].Name.Should().Be("P");
                })
            ),
            Row(
                "basic-upsert-preserves-dbms-and-port",
                "Dbms・Port を含めて保存・復元される",
                Upsert(
                    "p",
                    Prof(
                        "PG",
                        "pg-host",
                        "app",
                        tweak: p =>
                        {
                            p.Dbms = "postgresql";
                            p.Port = 5432;
                            p.AuthMode = DbAuthMode.UsernamePassword;
                            p.UserId = "postgres";
                        }
                    )
                ),
                Read(o =>
                {
                    o.All.Should().ContainSingle();
                    o.All[0].Dbms.Should().Be("postgresql");
                    o.All[0].Port.Should().Be(5432);
                })
            ),
            // 原子的保存の新規作成経路
            Row(
                "basic-upsert-creates-connections-file",
                "未保存フォルダへの Upsert で connections.json が新規作成される",
                Probe(o => o.ConnectionsExist.Should().BeFalse()),
                Upsert("p", Prof("P", "s", "d"), expect: o => o.ConnectionsExist.Should().BeTrue())
            ),
            // 原子的保存の置換経路
            Row(
                "basic-save-last-used-existing-file-replaces-content",
                "既存の connections.json がある保存は内容を置換する",
                SaveLast(Prof("Old", "old-host", "d")),
                SaveLast(Prof("New", "new-host", "d")),
                Read(o =>
                {
                    o.Last.Should().NotBeNull();
                    o.Last!.Value.Profile.Server.Should().Be("new-host");
                })
            ),
            // 一時ファイルの残りは全行に共通の表明（流す側）が見る。新規作成・上書きの両経路を通す
            Row(
                "basic-save-creates-and-overwrites-without-temp-file",
                "保存後に tmp ファイルが残らない",
                Upsert("p1", Prof("P1", "s1", "d")),
                Upsert("p2", Prof("P2", "s2", "d"))
            ),
            Row(
                "basic-last-used-round-trip-restores-database",
                "前回接続情報はデータベース名を含めて保存・復元される",
                SaveLast(
                    Prof(
                        server: "sql01",
                        database: "SalesDb",
                        save: true,
                        tweak: p =>
                        {
                            p.AuthMode = DbAuthMode.UsernamePassword;
                            p.UserId = "sa";
                            p.TrustServerCertificate = false;
                        }
                    ),
                    "secret"
                ),
                Read(o =>
                {
                    o.Last.Should().NotBeNull();
                    o.Last!.Value.Profile.Server.Should().Be("sql01");
                    o.Last.Value.Profile.Database.Should().Be("SalesDb");
                    o.Last.Value.Profile.UserId.Should().Be("sa");
                    o.Last.Value.Profile.TrustServerCertificate.Should().BeFalse();
                    o.Last.Value.Password.Value.Should().Be("secret");
                })
            ),
            // 「無い」を「読めない」と取り違えると、初回起動でプロファイルを 1 件も保存できなくなる
            Row(
                "basic-missing-file-loads-empty-and-saves",
                "ファイルが無いだけなら空で読め、保存もできる",
                Probe(o => o.ConnectionsExist.Should().BeFalse()),
                Read(o => o.All.Should().BeEmpty()),
                Upsert("p", Prof("初回", "s")),
                Read(o => o.All.Should().ContainSingle().Which.Name.Should().Be("初回"))
            ),
            Row(
                "basic-rename-replaced-profile-is-removed",
                "Rename: 置き換え先は一覧と暗号ファイルから消える",
                Seed("target", Prof("本番DB", "srv", save: true), "secret"),
                Seed("replaced", Prof("本番DB（東京）", "srv", save: true), "tokyo"),
                Rename(
                    "target",
                    "本番DB（東京）",
                    replaced: "replaced",
                    expect: o =>
                    {
                        o.Renamed.Should().BeTrue();
                        o.SecretExists("replaced").Should().BeFalse();
                    }
                ),
                Read(o =>
                    o.All.Should().ContainSingle().Which.Id.Should().Be(o.Profile("target").Id)
                ),
                LoadPassword("target", o => o.Password.Value.Should().Be("secret"))
            ),
            Row(
                "basic-rename-missing-target-returns-false-without-writing",
                "Rename: 対象が無ければ何も書かず false を返す",
                Seed("other", Prof("本番DB", "srv", save: true), "secret"),
                Probe(o => o.Memo["before"] = o.ConnectionsText),
                Rename(
                    "missing",
                    "新しい名前",
                    replaced: "other",
                    make: Unregistered(),
                    expect: o =>
                    {
                        o.Renamed.Should().BeFalse();
                        o.ConnectionsText.Should().Be((string?)o.Memo["before"]);
                        o.SecretExists("other").Should().BeTrue();
                    }
                )
            ),
        ];

    /// <summary>Upsert: SavePassword と暗号ファイルの有無は常に一致する（4 分岐の 1 つ）</summary>
    private static ScenarioRow<ProfileScenario> SaveFlagUpsertRow(
        string shape,
        bool savePassword,
        string password,
        bool expected
    ) =>
        Row(
            $"basic-upsert-flag-matches-secret-{shape}",
            "Upsert: SavePassword と暗号ファイルの有無は常に一致する",
            Upsert(
                "p",
                Prof("TestDB", "s", "d", savePassword),
                password,
                expect: o =>
                {
                    o.SecretExists("p").Should().Be(expected);
                    o.Profile("p")
                        .SavePassword.Should()
                        .Be(expected, "呼び出し側のインスタンスも保存内容へ揃える");
                }
            ),
            Read(o => o.All.Should().ContainSingle().Which.SavePassword.Should().Be(expected))
        );

    /// <summary>SaveLastUsed: SavePassword と暗号ファイルの有無は常に一致する（4 分岐の 1 つ）</summary>
    private static ScenarioRow<ProfileScenario> SaveFlagLastUsedRow(
        string shape,
        bool savePassword,
        string password,
        bool expected
    ) =>
        Row(
            $"basic-save-last-used-flag-matches-secret-{shape}",
            "SaveLastUsed: SavePassword と暗号ファイルの有無は常に一致する",
            SaveLast(
                Prof(server: "s", database: "d", save: savePassword),
                password,
                expect: o => o.SecretExists(ProfileContext.LastKey).Should().Be(expected)
            ),
            Read(o =>
            {
                o.Last.Should().NotBeNull();
                o.Last!.Value.Profile.SavePassword.Should().Be(expected);
                o.Last.Value.Password.Value.Should().Be(expected ? password : string.Empty);
            })
        );
}
