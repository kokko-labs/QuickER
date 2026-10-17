using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using QuickER.Db.UI;

namespace QuickER.Tests.Db.UI;

/// <summary>
/// 接続プロファイルの保存がどの段で失敗しても、接続先と「別の接続先のパスワード」の組を残さないこと、
/// 孤児の暗号ファイルを残さないこと、失敗を呼び出し元へ返すことを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 暗号ファイルのパスは接続先に依らない（前回接続は固定名・プロファイルは Id ごと）ので、JSON と暗号ファイルの
/// 2 つの書き込みのどちらかだけが成功すると、新しい接続先へ古いパスワードが付き得る（そのまま接続すると
/// 前の接続先の資格情報を別のサーバーへ送る）。規則は「暗号ファイルを消すのは JSON の前、書くのは JSON の後」。
/// </para>
/// <para>
/// 失敗は 3 通りで起こす＝暗号ファイルを掴む（削除・置き換えが失敗する）・connections.json を掴む（JSON の
/// 置き換えが失敗する）・暗号ファイルのフォルダにファイルの作成を拒否する（新しい暗号ファイルが書けない）。
/// パスワードは「pw:{サーバー名}」にして、どの接続先のものかを中身で確かめる。
/// </para>
/// </remarks>
public sealed class SqlConnectionProfileSecretPairingTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "QuickERTests",
        "pairing-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>失敗のさせ方</summary>
    public enum Fault
    {
        /// <summary>対象の暗号ファイルを掴む（削除も置き換えも失敗する）</summary>
        LockSecret,

        /// <summary>connections.json を掴む（JSON の置き換えが失敗する）</summary>
        LockConnections,

        /// <summary>暗号ファイルのフォルダにファイルの作成を拒否する（新しい暗号ファイルが書けない）</summary>
        DenyCreateSecret,
    }

    /// <summary>失敗させる操作の入口</summary>
    public enum Entry
    {
        /// <summary>前回接続を別の接続先で保存し直す（パスワードを保存する）</summary>
        SaveLastUsedOtherServer,

        /// <summary>前回接続を別の接続先で保存し直す（「保存しない」へ切り替える）</summary>
        SaveLastUsedOtherServerWithoutPassword,

        /// <summary>同じ Id のプロファイルを別の接続先へ変えて保存する（パスワードを保存する）</summary>
        UpsertOtherServer,

        /// <summary>同じ Id のプロファイルを別の接続先へ変えて保存する（「保存しない」へ切り替える）</summary>
        UpsertOtherServerWithoutPassword,

        /// <summary>プロファイルを削除する</summary>
        Delete,

        /// <summary>別のプロファイルを置き換えて保存する（置き換え先の暗号ファイルを消す）</summary>
        UpsertReplacing,

        /// <summary>名前の変更で別のプロファイルを置き換える（置き換え先の暗号ファイルを消す）</summary>
        RenameReplacing,
    }

    private static string Pw(string server) => "pw:" + server;

    private SqlConnectionProfileStore Store() => new(_folder, useDpapi: false);

    private static SqlConnectionProfile Profile(Guid id, string name, string server, bool save) =>
        new()
        {
            Id = id,
            Name = name,
            Server = server,
            Database = "db",
            SavePassword = save,
        };

    public static TheoryData<Entry, Fault> Cases()
    {
        var data = new TheoryData<Entry, Fault>();
        Entry[] changing =
        [
            Entry.SaveLastUsedOtherServer,
            Entry.SaveLastUsedOtherServerWithoutPassword,
            Entry.UpsertOtherServer,
            Entry.UpsertOtherServerWithoutPassword,
        ];

        foreach (var entry in changing)
        {
            data.Add(entry, Fault.LockSecret);
            data.Add(entry, Fault.LockConnections);

            // 「保存しない」は新しい暗号ファイルを書かないので、ファイルの作成の拒否では失敗しない
            if (entry is Entry.SaveLastUsedOtherServer or Entry.UpsertOtherServer)
            {
                data.Add(entry, Fault.DenyCreateSecret);
            }
        }

        foreach (var entry in new[] { Entry.Delete, Entry.UpsertReplacing, Entry.RenameReplacing })
        {
            data.Add(entry, Fault.LockSecret);
            data.Add(entry, Fault.LockConnections);
        }

        return data;
    }

    [Theory(
        DisplayName = "保存のどの段で失敗しても、接続先と別の接続先のパスワードの組も孤児の暗号ファイルも残さず、失敗を返す"
    )]
    [MemberData(nameof(Cases))]
    public void FailedWrite_NeverPairsAnotherServersPassword(Entry entry, Fault fault)
    {
        var store = Store();
        var target = Guid.NewGuid();
        var replaced = Guid.NewGuid();

        // 初期状態：前回接続とプロファイル 2 件（どれもパスワードを保存している）
        store.SaveLastUsed(Profile(Guid.NewGuid(), "last", "serverA", save: true), Pw("serverA"));
        store.Upsert(Profile(target, "target", "serverA", save: true), Pw("serverA"));
        store.Upsert(Profile(replaced, "other", "serverC", save: true), Pw("serverC"));

        var lockedSecret = entry switch
        {
            Entry.SaveLastUsedOtherServer or Entry.SaveLastUsedOtherServerWithoutPassword =>
                Path.Combine(store.SecretsFolder, "last-connection.dat"),
            Entry.UpsertReplacing or Entry.RenameReplacing => SecretPath(store, replaced),
            _ => SecretPath(store, target),
        };

        var thrown = WithFault(
            store,
            fault,
            lockedSecret,
            () =>
                Record.Exception(() =>
                {
                    switch (entry)
                    {
                        case Entry.SaveLastUsedOtherServer:
                            store.SaveLastUsed(
                                Profile(Guid.NewGuid(), "last", "serverB", save: true),
                                Pw("serverB")
                            );
                            break;
                        case Entry.SaveLastUsedOtherServerWithoutPassword:
                            store.SaveLastUsed(
                                Profile(Guid.NewGuid(), "last", "serverB", save: false),
                                ""
                            );
                            break;
                        case Entry.UpsertOtherServer:
                            store.Upsert(
                                Profile(target, "target", "serverB", save: true),
                                Pw("serverB")
                            );
                            break;
                        case Entry.UpsertOtherServerWithoutPassword:
                            store.Upsert(Profile(target, "target", "serverB", save: false), "");
                            break;
                        case Entry.Delete:
                            store.Delete(target);
                            break;
                        case Entry.UpsertReplacing:
                            store.Upsert(
                                Profile(target, "other", "serverA", save: true),
                                Pw("serverA"),
                                replacedProfileId: replaced
                            );
                            break;
                        case Entry.RenameReplacing:
                            store.Rename(target, "other", replacedProfileId: replaced);
                            break;
                    }
                })
        );

        thrown.Should().NotBeNull("失敗を黙って成功にしない（呼び出し元へ返す）");
        AssertSecretsPairWithTheirProfiles(store);
    }

    [Theory(DisplayName = "失敗の無い保存は、新しい接続先と新しいパスワードの組になる")]
    [InlineData(Entry.SaveLastUsedOtherServer)]
    [InlineData(Entry.UpsertOtherServer)]
    [InlineData(Entry.UpsertOtherServerWithoutPassword)]
    public void SuccessfulWrite_PairsNewServerWithNewPassword(Entry entry)
    {
        var store = Store();
        var target = Guid.NewGuid();
        store.SaveLastUsed(Profile(Guid.NewGuid(), "last", "serverA", save: true), Pw("serverA"));
        store.Upsert(Profile(target, "target", "serverA", save: true), Pw("serverA"));

        switch (entry)
        {
            case Entry.SaveLastUsedOtherServer:
                store.SaveLastUsed(
                    Profile(Guid.NewGuid(), "last", "serverB", save: true),
                    Pw("serverB")
                );
                store.LoadLastUsed()!.Value.Password.Value.Should().Be(Pw("serverB"));
                break;
            case Entry.UpsertOtherServer:
                store.Upsert(Profile(target, "target", "serverB", save: true), Pw("serverB"));
                store.LoadPassword(target).Value.Should().Be(Pw("serverB"));
                break;
            default:
                store.Upsert(Profile(target, "target", "serverB", save: false), "");
                File.Exists(SecretPath(store, target)).Should().BeFalse();
                break;
        }

        AssertSecretsPairWithTheirProfiles(store);
    }

    [Fact(DisplayName = "名前だけの変更は接続先を変えないので、保存済みのパスワードをそのまま残す")]
    public void Rename_KeepsPasswordOfTheSameServer()
    {
        var store = Store();
        var target = Guid.NewGuid();
        store.Upsert(Profile(target, "target", "serverA", save: true), Pw("serverA"));

        store.Rename(target, "renamed").Should().BeTrue();

        store.LoadPassword(target).Value.Should().Be(Pw("serverA"));
        AssertSecretsPairWithTheirProfiles(store);
    }

    private static string SecretPath(SqlConnectionProfileStore store, Guid id) =>
        Path.Combine(store.SecretsFolder, id.ToString("N") + ".dat");

    /// <summary>
    /// 残った暗号ファイルがどれも、いま JSON にあるその位置のプロファイルの接続先のパスワードであること
    /// （孤児の暗号ファイルも無いこと）を確かめる。
    /// </summary>
    private static void AssertSecretsPairWithTheirProfiles(SqlConnectionProfileStore store)
    {
        var owners = store
            .LoadAll()
            .ToDictionary(
                p => p.Id.ToString("N") + ".dat",
                p => p.Server,
                StringComparer.OrdinalIgnoreCase
            );

        if (store.LoadLastUsed() is { } lastUsed)
        {
            owners["last-connection.dat"] = lastUsed.Profile.Server;
        }

        if (!Directory.Exists(store.SecretsFolder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(store.SecretsFolder))
        {
            var name = Path.GetFileName(file);
            owners
                .Should()
                .ContainKey(name, $"暗号ファイル {name} の持ち主のプロファイルが無い（孤児）");
            File.ReadAllText(file)
                .Should()
                .Be(
                    Pw(owners[name]),
                    $"{name} は接続先 {owners[name]} のパスワードでなければならない"
                );
        }
    }

    /// <summary>失敗の仕掛けを付けて本体を走らせ、仕掛けを外してから結果を返す</summary>
    private static Exception? WithFault(
        SqlConnectionProfileStore store,
        Fault fault,
        string lockedSecret,
        Func<Exception?> body
    )
    {
        switch (fault)
        {
            case Fault.LockSecret:
            {
                using var held = new FileStream(
                    lockedSecret,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                return body();
            }
            case Fault.LockConnections:
            {
                using var held = new FileStream(
                    store.ConnectionsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                return body();
            }
            default:
            {
                var info = new DirectoryInfo(store.SecretsFolder);
                var rule = new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User!,
                    FileSystemRights.CreateFiles,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Deny
                );
                var security = info.GetAccessControl();
                security.AddAccessRule(rule);
                info.SetAccessControl(security);

                try
                {
                    return body();
                }
                finally
                {
                    security = info.GetAccessControl();
                    security.RemoveAccessRule(rule);
                    info.SetAccessControl(security);
                }
            }
        }
    }
}
