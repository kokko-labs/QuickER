using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>読めないファイル（connections.json の排他・暗号ファイルの排他・復号できない暗号ファイル）</summary>
    /// <remarks>
    /// 読めない状態は、別のハンドルで <see cref="FileShare.None"/> のまま開いておくだけで決定的に作れる（人工的な並行・時間依存に頼らない）。
    /// 共有違反は Windows の挙動で、このテストプロジェクトは Windows 専用。
    /// 復号できない暗号ファイルは、DPAPI で復号できないバイト列で作る（別ユーザー・別端末で暗号化されたものの代役）ので、
    /// 実 DPAPI（CurrentUser）を使う行になる。
    /// </remarks>
    private static IEnumerable<ScenarioRow<ProfileScenario>> UnreadableRows() =>
        [
            // 保存はどれも「読み込み → 変更 → 書き込み」。読み取り失敗を空データへ落とすと、
            // その空が書き戻って登録済みプロファイルが全件消える
            Row(
                "unreadable-writes-abort-and-profiles-survive",
                "読み取れないときの書き込みは中止され、プロファイルは消えない",
                Seed("kept", Prof("本番", "prod", "shop")),
                SeedLast(Prof("本番", "prod", "shop")),
                Probe(o => o.Memo["original"] = o.ConnectionsText),
                // OS の仕掛け：connections.json を FileShare.None のハンドルで開いたままにして、読み取りを失敗させる
                LockJson(FileShare.None),
                Upsert(
                    "added",
                    Prof("新規", "s", "d"),
                    throws: true,
                    expect: o => o.ShouldHaveThrownUnavailable()
                ),
                Delete("kept", throws: true, expect: o => o.ShouldHaveThrownUnavailable()),
                Rename("kept", "別名", throws: true, expect: o => o.ShouldHaveThrownUnavailable()),
                SaveLast(
                    Prof("別", "x"),
                    throws: true,
                    expect: o => o.ShouldHaveThrownUnavailable()
                ),
                UnlockJsonThen(o =>
                    o.ConnectionsText.Should()
                        .Be((string?)o.Memo["original"], "読めなかったので 1 バイトも書いていない")
                ),
                LoadAll(o => o.All.Should().ContainSingle().Which.Name.Should().Be("本番")),
                LoadLast(o => o.Last.Should().NotBeNull())
            ),
            // Upsert は SavePassword を正規化して呼び出し側のインスタンスを書き換えるが、
            // 保存を中止したのに書き換えだけ残ると「保存していないのに画面の表示だけ変わる」
            Row(
                "unreadable-upsert-abort-does-not-mutate-argument",
                "読み取れないときの中止は呼び出し側のインスタンスも変えない",
                Seed("kept", Prof("本番", "prod")),
                LockJson(FileShare.None),
                Upsert(
                    "added",
                    Prof("新規", "s", save: true),
                    throws: true,
                    expect: o =>
                    {
                        o.ShouldHaveThrownUnavailable();
                        o.Profile("added")
                            .SavePassword.Should()
                            .BeTrue("保存を中止した以上、正規化もしない");
                    }
                )
            ),
            // 表示は「並べるものが無い」で続行してよい（何も失われない）。止めるのは書き込みだけ、という非対称
            Row(
                "unreadable-reads-fall-back-to-empty-without-throwing",
                "読み取れないときも読み取り API は空で返る（UI を止めない）",
                Seed("p", Prof("本番", "prod")),
                LockJson(FileShare.None),
                LoadAll(o => o.All.Should().BeEmpty()),
                LoadLast(o => o.Last.Should().BeNull()),
                LoadPassword("p", o => o.Password.Value.Should().BeEmpty())
            ),
            // 「読めない」を「無い」と同じ空文字へ畳むと、その空値を書き戻した保存が暗号ファイルを消す
            Row(
                "unreadable-locked-secret-is-classified-unreadable",
                "LoadPassword: ロックされた暗号ファイルは読めないに分類する",
                Seed("p", Prof("本番DB", "srv", save: true), "secret"),
                // OS の仕掛け：暗号ファイルを FileShare.None のハンドルで開いたままにする（一時的なロックの代表）
                LockSecret("p", FileShare.None),
                LoadPassword("p", o => o.Password.Should().Be(SavedPassword.Unreadable))
            ),
            DpapiRow(
                "unreadable-password-distinguishes-loaded-missing-unreadable",
                "LoadPassword: 読めた・無い・読めないを区別して返す",
                Seed("loaded", Prof("読める", "srv", save: true), "secret"),
                Seed("unreadable", Prof("読めない", "srv", save: true), "secret"),
                // DPAPI で復号できないバイト列で差し替える（別ユーザー・別端末で暗号化されたものの代役）
                GarbageSecret("unreadable", [1, 2, 3, 4]),
                LoadPassword(
                    "loaded",
                    o =>
                        o.Password.Should()
                            .Be(new SavedPassword(SavedPasswordStatus.Loaded, "secret"))
                ),
                LoadPassword(
                    "unknown",
                    o => o.Password.Should().Be(SavedPassword.Missing),
                    Unregistered()
                ),
                LoadPassword(
                    "unreadable",
                    o =>
                    {
                        o.Password.Should().Be(SavedPassword.Unreadable);
                        o.SecretExists("unreadable").Should().BeTrue("読めないだけで消さない");
                    }
                )
            ),
            DpapiRow(
                "unreadable-rename-leaves-secret-bytes-untouched",
                "Rename: 名前だけを変え、対象の暗号ファイルには触れない",
                Seed("p", Prof("本番DB", "srv", save: true), "secret"),
                GarbageSecret("p", [5, 6, 7, 8]),
                Rename("p", "本番DB（新）", expect: o => o.Renamed.Should().BeTrue()),
                LoadAll(o =>
                {
                    var stored = o.All.Should().ContainSingle().Subject;
                    stored.Name.Should().Be("本番DB（新）");
                    stored.SavePassword.Should().BeTrue();
                    o.SecretBytes("p").Should().Equal(new byte[] { 5, 6, 7, 8 });
                })
            ),
        ];

    /// <summary>connections.json のロックを外してから、いまの状態を確かめる</summary>
    private static ProfileStep UnlockJsonThen(Action<ProfileObservation> expect) =>
        UnlockJson() with
        {
            Expect = expect,
        };
}
