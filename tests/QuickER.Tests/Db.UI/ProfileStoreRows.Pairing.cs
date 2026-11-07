using System.IO;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>失敗させる操作の入口（元のテストの Entry）</summary>
    private enum PairingEntry
    {
        SaveLastUsedOtherServer,
        SaveLastUsedOtherServerWithoutPassword,
        UpsertOtherServer,
        UpsertOtherServerWithoutPassword,
        Delete,
        UpsertReplacing,
        RenameReplacing,
    }

    /// <summary>失敗のさせ方（元のテストの Fault）</summary>
    private enum PairingFault
    {
        LockSecret,
        LockConnections,
        DenyCreateSecret,
    }

    /// <summary>
    /// 保存がどの段で失敗しても、接続先と別の接続先のパスワードの組も孤児の暗号ファイルも残さず、失敗を返す（印付きの行）
    /// </summary>
    /// <remarks>
    /// 暗号ファイルのパスは接続先に依らない（前回接続は固定名・プロファイルは Id ごと）ので、JSON と暗号ファイルの 2 つの書き込みの
    /// どちらかだけが成功すると、新しい接続先へ古いパスワードが付き得る。規則は「暗号ファイルを消すのは JSON の前、書くのは JSON の後」
    /// （→ src/QuickER.Db.UI/CLAUDE.md「接続プロファイルの暗号ファイルの順序」）。
    /// パスワードは「pw:{サーバー名}」にして、どの接続先のものかを中身で確かめる（流す側の共通の表明）。
    /// </remarks>
    private static IEnumerable<ScenarioRow<ProfileScenario>> PairingRows()
    {
        // 「保存しない」は新しい暗号ファイルを書かないので、ファイルの作成の拒否では失敗しない（組から外す）
        PairingEntry[] changing =
        [
            PairingEntry.SaveLastUsedOtherServer,
            PairingEntry.SaveLastUsedOtherServerWithoutPassword,
            PairingEntry.UpsertOtherServer,
            PairingEntry.UpsertOtherServerWithoutPassword,
        ];

        foreach (var entry in changing)
        {
            yield return FailedRow(entry, PairingFault.LockSecret);
            yield return FailedRow(entry, PairingFault.LockConnections);

            if (entry is PairingEntry.SaveLastUsedOtherServer or PairingEntry.UpsertOtherServer)
            {
                yield return FailedRow(entry, PairingFault.DenyCreateSecret);
            }
        }

        foreach (
            var entry in new[]
            {
                PairingEntry.Delete,
                PairingEntry.UpsertReplacing,
                PairingEntry.RenameReplacing,
            }
        )
        {
            yield return FailedRow(entry, PairingFault.LockSecret);
            yield return FailedRow(entry, PairingFault.LockConnections);

            // 置き換えつきの保存も、パスワードを書く段（暗号ファイルの作成）で失敗しても、順序の不変条件は同じ
            if (entry is PairingEntry.UpsertReplacing)
            {
                yield return FailedRow(entry, PairingFault.DenyCreateSecret);
            }
        }

        yield return PairedRow(
            "pairing-success-save-last-used-other-server",
            "失敗の無い保存は、新しい接続先と新しいパスワードの組になる",
            [
                .. SeedAB(),
                SaveLast(Pair("last", "serverB", true), Pw("serverB")),
                Read(o => o.Last!.Value.Password.Value.Should().Be(Pw("serverB"))),
            ]
        );
        yield return PairedRow(
            "pairing-success-upsert-other-server",
            "失敗の無い保存は、新しい接続先と新しいパスワードの組になる",
            [
                .. SeedAB(),
                Upsert(
                    "target",
                    Pair("target", "serverB", true, sameIdAs: "target"),
                    Pw("serverB")
                ),
                LoadPassword("target", o => o.Password.Value.Should().Be(Pw("serverB"))),
            ]
        );
        yield return PairedRow(
            "pairing-success-upsert-other-server-without-password",
            "失敗の無い保存は、新しい接続先と新しいパスワードの組になる",
            [
                .. SeedAB(),
                Upsert(
                    "target",
                    Pair("target", "serverB", false, sameIdAs: "target"),
                    "",
                    expect: o => o.SecretExists("target").Should().BeFalse()
                ),
            ]
        );
        yield return PairedRow(
            "pairing-rename-keeps-password-of-same-server",
            "名前だけの変更は接続先を変えないので、保存済みのパスワードをそのまま残す",
            Seed("target", Pair("target", "serverA", true), Pw("serverA")),
            Rename("target", "renamed", expect: o => o.Renamed.Should().BeTrue()),
            LoadPassword("target", o => o.Password.Value.Should().Be(Pw("serverA")))
        );
    }

    /// <summary>失敗する保存の行（入口 × 失敗のさせ方）</summary>
    private static ScenarioRow<ProfileScenario> FailedRow(PairingEntry entry, PairingFault fault)
    {
        // 掴む暗号ファイル（入口で決まる）。前回接続は固定名、置き換え系は置き換え先、ほかは対象
        var lockedSecret = entry switch
        {
            PairingEntry.SaveLastUsedOtherServer
            or PairingEntry.SaveLastUsedOtherServerWithoutPassword => ProfileContext.LastKey,
            PairingEntry.UpsertReplacing or PairingEntry.RenameReplacing => "replaced",
            _ => "target",
        };
        var faultStep = fault switch
        {
            // OS の仕掛け：暗号ファイルを FileShare.Read で掴む（読めるが、削除も置き換えも失敗する）
            PairingFault.LockSecret => LockSecret(lockedSecret, FileShare.Read),
            // OS の仕掛け：connections.json を FileShare.Read で掴む（読めるが、JSON の置き換えが失敗する）
            PairingFault.LockConnections => LockJson(FileShare.Read),
            // OS の仕掛け：暗号フォルダへファイルの作成を拒否する ACL を掛ける（新しい暗号ファイルが書けない）
            _ => DenyCreate(),
        };

        return PairedRow(
            $"pairing-failed-{Kebab(entry.ToString())}-{Kebab(fault.ToString())}",
            "保存のどの段で失敗しても、接続先と別の接続先のパスワードの組も孤児の暗号ファイルも残さず、失敗を返す",
            [.. SeedAC(), faultStep, FailingStep(entry)]
        );
    }

    /// <summary>失敗を期待する保存の手（失敗を黙って成功にしない）</summary>
    private static ProfileStep FailingStep(PairingEntry entry)
    {
        Action<ProfileObservation> failed = o =>
            o.Thrown.Should().NotBeNull("失敗を黙って成功にしない（呼び出し元へ返す）");

        return entry switch
        {
            PairingEntry.SaveLastUsedOtherServer => SaveLast(
                Pair("last", "serverB", true),
                Pw("serverB"),
                throws: true,
                expect: failed
            ),
            PairingEntry.SaveLastUsedOtherServerWithoutPassword => SaveLast(
                Pair("last", "serverB", false),
                "",
                throws: true,
                expect: failed
            ),
            PairingEntry.UpsertOtherServer => Upsert(
                "target",
                Pair("target", "serverB", true, sameIdAs: "target"),
                Pw("serverB"),
                throws: true,
                expect: failed
            ),
            PairingEntry.UpsertOtherServerWithoutPassword => Upsert(
                "target",
                Pair("target", "serverB", false, sameIdAs: "target"),
                "",
                throws: true,
                expect: failed
            ),
            PairingEntry.Delete => Delete("target", throws: true, expect: failed),
            PairingEntry.UpsertReplacing => Upsert(
                "target",
                Pair("other", "serverA", true, sameIdAs: "target"),
                Pw("serverA"),
                replaced: "replaced",
                throws: true,
                expect: failed
            ),
            _ => Rename("target", "other", replaced: "replaced", throws: true, expect: failed),
        };
    }

    /// <summary>初期状態：前回接続とプロファイル 2 件（どれもパスワードを保存している）</summary>
    private static ProfileStep[] SeedAC() =>
        [
            SeedLast(Pair("last", "serverA", true), Pw("serverA")),
            Seed("target", Pair("target", "serverA", true), Pw("serverA")),
            Seed("replaced", Pair("other", "serverC", true), Pw("serverC")),
        ];

    /// <summary>初期状態：前回接続とプロファイル 1 件</summary>
    private static ProfileStep[] SeedAB() =>
        [
            SeedLast(Pair("last", "serverA", true), Pw("serverA")),
            Seed("target", Pair("target", "serverA", true), Pw("serverA")),
        ];

    /// <summary>パスワードを「pw:{接続先}」で作る行のプロファイル（Id は新規か、既存と同じ）</summary>
    private static Func<ProfileContext, SqlConnectionProfile> Pair(
        string name,
        string server,
        bool save,
        string? sameIdAs = null
    ) =>
        context =>
        {
            var profile = new SqlConnectionProfile
            {
                Name = name,
                Server = server,
                Database = "db",
                SavePassword = save,
            };

            if (sameIdAs is not null)
            {
                profile.Id = context.Profiles[sameIdAs].Id;
            }

            return profile;
        };

    /// <summary>列挙値の名前を行の Id 用の英小文字とハイフンにする</summary>
    private static string Kebab(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + c : c.ToString()))
            .ToLowerInvariant();
}
