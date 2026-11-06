using System.IO;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>TLS 要求水準の保存と読み（名前・整数・キー欠落）</summary>
    private static IEnumerable<ScenarioRow<ProfileScenario>> TlsRows() =>
        [
            Row(
                "tls-ssl-mode-round-trips",
                "TLS 要求水準はプロファイルの往復で保たれる",
                Upsert(
                    "p",
                    Prof(
                        "PG",
                        "pg01",
                        "shop",
                        tweak: p =>
                        {
                            p.Dbms = "postgresql";
                            p.SslMode = DbSslMode.VerifyFull;
                        }
                    )
                ),
                SaveLast(context => context.Profiles["p"]),
                LoadAll(o => o.All[0].SslMode.Should().Be(DbSslMode.VerifyFull)),
                LoadLast(o =>
                {
                    o.Last!.Value.Profile.SslMode.Should().Be(DbSslMode.VerifyFull);
                    o.Profile("p").ToSettings("").SslMode.Should().Be(DbSslMode.VerifyFull);
                })
            ),
            // 整数で保存すると、将来 DbSslMode の途中へメンバーを挿入した瞬間、保存済みの設定が無言で別の水準として
            // 読み直される。名前で保存していればその事故が起きない（元の表明の文字列照合を、元の強さのまま残す）
            Row(
                "tls-ssl-mode-is-persisted-by-name",
                "TLS 要求水準は JSON へ名前で保存される",
                Upsert(
                    "p",
                    Prof(
                        "PG",
                        tweak: p =>
                        {
                            p.Dbms = "postgresql";
                            p.SslMode = DbSslMode.VerifyFull;
                        }
                    ),
                    expect: o =>
                    {
                        var json = o.ConnectionsText;
                        json.Should().Contain("\"sslMode\": \"VerifyFull\"");
                        json.Should().NotContain("\"sslMode\": 5");
                    }
                )
            ),
            // System.Text.Json の既定（allowIntegerValues: true）に依存する後方互換
            Row(
                "tls-ssl-mode-integer-spelling-is-readable",
                "整数で書かれた旧 TLS 要求水準も読める",
                WriteJson(
                    """
                    {
                      "profiles": [
                        { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "sslMode": 5 }
                      ]
                    }
                    """
                ),
                LoadAll(o => o.All[0].SslMode.Should().Be(DbSslMode.VerifyFull))
            ),
            // この 1 点が「既存プロファイルの接続が黙って変わらない」ことの保証にあたる
            Row(
                "tls-ssl-mode-key-missing-is-unspecified",
                "TLS 要求水準を持たない旧プロファイル JSON は未指定として読み込まれる",
                WriteJson(
                    """
                    {
                      "profiles": [
                        { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "database": "shop" }
                      ],
                      "lastUsed": { "name": "Legacy", "dbms": "postgresql", "server": "pg01", "database": "shop" }
                    }
                    """
                ),
                LoadAll(o => o.All[0].SslMode.Should().Be(DbSslMode.Unspecified)),
                LoadLast(o => o.Last!.Value.Profile.SslMode.Should().Be(DbSslMode.Unspecified))
            ),
        ];
}
