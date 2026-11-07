using System.Text.Json;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>旧形式・null の要素・壊れた JSON など、手の前に connections.json を直接書いて始める行</summary>
    private static IEnumerable<ScenarioRow<ProfileScenario>> LegacyRows() =>
        [
            // Dbms / Port / ServiceName を持たないプロファイルを新形式（profiles 配列）で直接書き出す
            Row(
                "legacy-profile-without-dbms-defaults-to-sqlserver",
                "Dbms 欠落のプロファイルは sqlserver として読み込まれる",
                WriteJson(
                    "{\"profiles\":[{\"id\":\""
                        + Guid.NewGuid().ToString()
                        + "\",\"name\":\"Legacy\",\"server\":\"srv\",\"database\":\"db\",\"authMode\":0,\"userId\":\"\",\"trustServerCertificate\":true,\"savePassword\":false}]}"
                ),
                Read(o =>
                {
                    o.All.Should().ContainSingle();
                    o.All[0].Dbms.Should().Be("sqlserver");
                    o.All[0].Port.Should().BeNull();
                })
            ),
            // 旧構成のトップレベル配列形式（新形式のオブジェクトではない）を直接書き出す
            Row(
                "legacy-array-format-falls-back-to-empty",
                "旧配列形式の connections.json は空一覧へフォールバックする",
                WriteJson(
                    "[{\"id\":\""
                        + Guid.NewGuid().ToString()
                        + "\",\"name\":\"Legacy\",\"server\":\"srv\",\"database\":\"db\"}]"
                ),
                Read(o => o.All.Should().BeEmpty())
            ),
            // 保存の起点としても同じく読めて、書き戻すと null 要素は消える
            Row(
                "legacy-null-profiles-and-elements-are-normalized",
                "一覧や要素が null の connections.json は揃えて読み、保存もできる",
                WriteJson("{\"profiles\": [null, {\"name\": \"本番DB\"}, null]}"),
                Read(o => o.All.Should().ContainSingle().Which.Name.Should().Be("本番DB")),
                Upsert("dev", Prof("開発")),
                Read(o =>
                {
                    o.All.Select(p => p.Name).Should().BeEquivalentTo(new[] { "本番DB", "開発" });
                    using (var written = o.ConnectionsJson())
                    {
                        written
                            .RootElement.GetProperty("profiles")
                            .EnumerateArray()
                            .Should()
                            .OnlyContain(
                                e => e.ValueKind == JsonValueKind.Object,
                                "null 要素は書き戻さない"
                            );
                    }

                    o.BackupExists.Should().BeFalse("正しい JSON なので壊れたものとして退避しない");
                }),
                WriteJson("{\"profiles\": null}"),
                Read(o => o.All.Should().BeEmpty())
            ),
            // 破損時は空データで続行して保存できる（＝正常化できる）が、上書きの前に元の内容を残す。
            // 残すのは最も古い退避＝最後に正しく読めていた状態にいちばん近いもの
            Row(
                "legacy-corrupt-file-is-backed-up-only-once",
                "壊れた connections.json は最初の 1 回だけ .corrupt へ退避する",
                WriteJson("壊れた 1 回目"),
                Upsert(
                    "r1",
                    Prof("復旧", "s"),
                    expect: o =>
                    {
                        o.BackupExists.Should().BeTrue();
                        o.BackupText.Should().Be("壊れた 1 回目");
                    }
                ),
                Read(o => o.All.Should().ContainSingle()),
                WriteJson("壊れた 2 回目"),
                Upsert(
                    "r2",
                    Prof("復旧 2", "s"),
                    expect: o => o.BackupText.Should().Be("壊れた 1 回目", "最も古い退避を残す")
                )
            ),
        ];
}
