using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>
    /// 同じ（DBMS, 名前）の衝突：書く入口は、対象でも置き換え先でもない同じ（DBMS, 名前）のプロファイルがディスクにあれば、
    /// 何も書かずに <see cref="ConnectionProfileNameConflictException"/> で断る（呼び出し側の一覧が古くても複製を作らない）
    /// </summary>
    private static IEnumerable<ScenarioRow<ProfileScenario>> ConflictRows() =>
        [
            Row(
                "conflict-upsert-new-profile-with-name-on-disk-refuses-and-writes-nothing",
                "Upsert: 新しい Id で、ディスクにある同じ（DBMS, 名前）のプロファイルを書こうとすると、大文字小文字だけの違いでも何も書かずに断る",
                Seed("prod", Prof("Prod", "srv-a", save: true), "pw-a"),
                Probe(o => o.Memo["before"] = o.ConnectionsText),
                Upsert(
                    "other",
                    Prof("PROD", "srv-b", save: true),
                    password: "",
                    throws: true,
                    expect: o =>
                    {
                        o.ShouldHaveThrownNameConflict(o.Profile("prod").Id);
                        o.ConnectionsText.Should().Be((string?)o.Memo["before"]);
                        o.SecretExists("other").Should().BeFalse();
                        System
                            .Text.Encoding.UTF8.GetString(o.SecretBytes("prod"))
                            .Should()
                            .Be("pw-a");
                        o.Profile("other")
                            .SavePassword.Should()
                            .BeTrue("断ったときは渡したインスタンスも正規化で書き換えない");
                    }
                ),
                Upsert(
                    "same",
                    Prof("Prod", "srv-c"),
                    throws: true,
                    expect: o => o.ShouldHaveThrownNameConflict(o.Profile("prod").Id)
                ),
                Read(o => o.All.Should().ContainSingle().Which.Server.Should().Be("srv-a"))
            ),
            Row(
                "conflict-upsert-same-name-other-dbms-is-allowed",
                "Upsert: 方言が違えば同じ名前のプロファイルも書ける（一意なのは DBMS と名前の組）",
                Seed("prod", Prof("Prod", "srv-a", tweak: p => p.Dbms = "postgresql")),
                Upsert("other", Prof("Prod", "srv-b")),
                Read(o =>
                    o.All.Select(p => p.Dbms)
                        .Should()
                        .BeEquivalentTo(new[] { "postgresql", "sqlserver" })
                )
            ),
            Row(
                "conflict-upsert-existing-id-onto-name-of-other-profile-refuses",
                "Upsert: 保存済みの Id のプロファイルを、別のプロファイルの名前で書こうとすると断る（開いた後にほかのプロセスが名前を変えて同名を足した形）",
                Seed("prod", Prof("Prod", "srv-a")),
                Seed("dev", Prof("Dev", "srv-b")),
                Upsert(
                    "dev",
                    Edit("dev", p => p.Name = "prod"),
                    throws: true,
                    expect: o => o.ShouldHaveThrownNameConflict(o.Profile("prod").Id)
                ),
                Read(o =>
                    o.All.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Dev", "Prod" })
                ),
                Upsert(
                    "dev",
                    Edit("dev", p => p.Name = "DEV"),
                    expect: o =>
                        o.Thrown.Should()
                            .BeNull("自分自身の名前の大文字小文字を変えるのは衝突でない")
                )
            ),
            Row(
                "conflict-upsert-replacing-the-conflicting-profile-succeeds",
                "Upsert: 衝突したプロファイルを置き換え先に渡せば、そのプロファイルと暗号ファイルを消して 1 つにする",
                Seed("prod", Prof("Prod", "srv-a", save: true), "pw-a"),
                Upsert(
                    "other",
                    Prof("PROD", "srv-b", save: true),
                    "pw-b",
                    replaced: "prod",
                    expect: o => o.SecretExists("prod").Should().BeFalse()
                ),
                Read(o =>
                    o.All.Should().ContainSingle().Which.Id.Should().Be(o.Profile("other").Id)
                ),
                LoadPassword("other", o => o.Password.Value.Should().Be("pw-b"))
            ),
            Row(
                "conflict-rename-onto-name-on-disk-refuses-and-writes-nothing",
                "Rename: 置き換え先なしで、ディスクにある別のプロファイルの名前へ変えようとすると何も書かずに断る（対象が無いときは衝突より先に false）",
                Seed("prod", Prof("Prod", "srv-a")),
                Seed("dev", Prof("Dev", "srv-b", save: true), "pw-b"),
                Probe(o => o.Memo["before"] = o.ConnectionsText),
                Rename(
                    "dev",
                    "PROD",
                    throws: true,
                    expect: o =>
                    {
                        o.ShouldHaveThrownNameConflict(o.Profile("prod").Id);
                        o.ConnectionsText.Should().Be((string?)o.Memo["before"]);
                        o.SecretExists("dev").Should().BeTrue();
                    }
                ),
                Rename(
                    "missing",
                    "Prod",
                    make: Unregistered(),
                    expect: o => o.Renamed.Should().BeFalse()
                ),
                Rename("dev", "DEV", expect: o => o.Renamed.Should().BeTrue()),
                Read(o =>
                    o.All.Select(p => p.Name).Should().BeEquivalentTo(new[] { "DEV", "Prod" })
                )
            ),
            LegacyDuplicateRow(),
        ];

    /// <summary>手で書いた JSON に同じ（DBMS, 名前）が 2 つある：片方の保存は断り、もう片方を置き換え先に渡すと 1 つになる</summary>
    private static ScenarioRow<ProfileScenario> LegacyDuplicateRow()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        return Row(
            "conflict-legacy-duplicate-names-heal-by-replacing",
            "手で書いた connections.json に同じ（DBMS, 名前）が 2 つあれば、片方の Upsert は断り、もう片方を置き換え先に渡すと 1 つになる",
            WriteJson(
                "{\"profiles\":["
                    + "{\"id\":\""
                    + first
                    + "\",\"name\":\"Prod\",\"dbms\":\"sqlserver\",\"server\":\"srv-a\"},"
                    + "{\"id\":\""
                    + second
                    + "\",\"name\":\"prod\",\"dbms\":\"sqlserver\",\"server\":\"srv-b\"}]}"
            ),
            Known("second", _ => new SqlConnectionProfile { Id = second }),
            Upsert(
                "first",
                _ => new SqlConnectionProfile
                {
                    Id = first,
                    Name = "Prod",
                    Server = "srv-new",
                },
                throws: true,
                expect: o => o.ShouldHaveThrownNameConflict(second)
            ),
            Upsert(
                "first-again",
                _ => new SqlConnectionProfile
                {
                    Id = first,
                    Name = "Prod",
                    Server = "srv-new",
                },
                replaced: "second",
                expect: o => o.Thrown.Should().BeNull()
            ),
            Read(o =>
            {
                var profile = o.All.Should().ContainSingle().Subject;
                profile.Id.Should().Be(first);
                profile.Server.Should().Be("srv-new");
            })
        );
    }
}
