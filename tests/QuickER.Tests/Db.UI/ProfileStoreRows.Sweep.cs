using System.IO;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

internal static partial class ProfileStoreRows
{
    /// <summary>掃引の行が使うプロファイルの Id（JSON へ書く文字列と手の対象の Id を一致させるため固定する）</summary>
    private static readonly Guid TargetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ReplacedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid LastId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>手の前の connections.json の作り方（掃引の行）</summary>
    private enum SweepJson
    {
        Missing,
        LegacyArray,
        Corrupt,
        NullElements,
        Healthy,

        /// <summary>Healthy の中身を作って、別のハンドルで開いたままにする</summary>
        Locked,
    }

    private const string CorruptText = "{ broken json";

    private static readonly string LegacyText =
        "[" + ProfileJson(TargetId, "target", "serverA", false) + "]";

    private static readonly string NullElementsText =
        "{\"profiles\":[null,"
        + ProfileJson(TargetId, "target", "serverA", false)
        + ","
        + ProfileJson(ReplacedId, "replaced", "serverC", false)
        + ",null],\"lastUsed\":"
        + ProfileJson(LastId, "last", "serverA", true)
        + "}";

    private static readonly string HealthyText =
        "{\"profiles\":["
        + ProfileJson(TargetId, "target", "serverA", false)
        + ","
        + ProfileJson(ReplacedId, "replaced", "serverC", false)
        + "],\"lastUsed\":"
        + ProfileJson(LastId, "last", "serverA", true)
        + "}";

    private static string ProfileJson(Guid id, string name, string server, bool save) =>
        "{\"id\":\""
        + id.ToString("D")
        + "\",\"name\":\""
        + name
        + "\",\"server\":\""
        + server
        + "\",\"database\":\"db\",\"savePassword\":"
        + (save ? "true" : "false")
        + "}";

    private static string? SweepJsonText(SweepJson json) =>
        json switch
        {
            SweepJson.Missing => null,
            SweepJson.LegacyArray => LegacyText,
            SweepJson.Corrupt => CorruptText,
            SweepJson.NullElements => NullElementsText,
            _ => HealthyText,
        };

    /// <summary>
    /// 手の前の状態（connections.json × 手の対象の暗号ファイル × 暗号フォルダ）を 1 手ごとに作り直して、製品の入口を 1 手流す行
    /// </summary>
    /// <remarks>
    /// 状態の組を網羅するための行（P4-2a）。1 手は「状態の作り直し（ロック・ACL を外し、暗号ファイルと退避を消し、JSON を書く）→
    /// 状態の用意 → 製品の 1 手 → 表明」で、手どうしは前の手の結果を引き継がない（<c>.corrupt</c> の退避が最初の 1 回だけで、
    /// 前の手の結果に依ってしまうのを避けるため、退避も作り直しで消す）。DPAPI の行（復号できない暗号ファイルを作る）。
    /// 暗号ファイルを掴む手（<see cref="ProfileSecretState.Locked"/>）は、書く入口では <see cref="FileShare.Read"/>（読めるが消せない）、
    /// 読む入口では <see cref="FileShare.None"/>（読めない）で掴む。
    /// </remarks>
    private static IEnumerable<ScenarioRow<ProfileScenario>> SweepRows()
    {
        // 書く入口 × connections.json の状態：文書（SqlConnectionProfileStore の XmlDoc と src/QuickER.Db.UI/CLAUDE.md の
        // 「接続プロファイルの暗号ファイルの順序」）で定まる振る舞い
        yield return DpapiRow(
            "legacy-sweep-writes-missing-json",
            "connections.json が無い上での Delete・置き換えつきの Upsert は、空のデータから続けて暗号ファイルを消す",
            [
                .. SweepDefinitions(),
                // 復号できない暗号ファイルでも、プロファイルごと消す（Delete は暗号ファイルの中身を読まない）。作成の拒否とは無関係
                .. Sweep(
                    Delete(
                        "target",
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Should().BeEmpty();
                            o.SecretExists("target")
                                .Should()
                                .BeFalse("復号できない暗号ファイルもプロファイルと一緒に消える");
                            o.BackupExists.Should().BeFalse("無いファイルは壊れたものではない");
                        }
                    ),
                    SweepJson.Missing,
                    ProfileSecretState.Unreadable,
                    denied: true
                ),
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(false),
                        replaced: "replaced",
                        expect: UpsertReplacedSucceeds(backup: null)
                    ),
                    SweepJson.Missing,
                    ProfileSecretState.None
                ),
            ]
        );
        yield return DpapiRow(
            "legacy-sweep-writes-legacy-array",
            "旧配列形式の connections.json は壊れたものとして退避し、空のデータから Delete が書く。掴まれた置き換え先は消せず JSON も変えない",
            [
                .. SweepDefinitions(),
                .. Sweep(
                    Delete(
                        "target",
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Should().BeEmpty();
                            o.SecretExists("target").Should().BeFalse();
                            o.BackupText.Should()
                                .Be(LegacyText, "上書きの前に元の配列形式を退避する");
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Readable
                ),
                // 先頭の削除が失敗したときは何も書かずに例外になる（クラスの XmlDoc）。JSON の置き換えにも退避にも進まない
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(false),
                        replaced: "replaced",
                        throws: true,
                        expect: o =>
                        {
                            ShouldHaveThrownIo(o);
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Locked,
                    denied: true,
                    snapshot: true
                ),
            ]
        );
        yield return DpapiRow(
            "legacy-sweep-writes-corrupt-json",
            "壊れた connections.json は、掴まれた暗号ファイルの削除が失敗したら何も書かず、置き換えつきの Upsert は退避して書く",
            [
                .. SweepDefinitions(),
                .. Sweep(
                    Delete(
                        "target",
                        throws: true,
                        expect: o =>
                        {
                            ShouldHaveThrownIo(o);
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.Corrupt,
                    ProfileSecretState.Locked,
                    snapshot: true
                ),
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(false),
                        replaced: "replaced",
                        expect: UpsertReplacedSucceeds(backup: CorruptText)
                    ),
                    SweepJson.Corrupt,
                    ProfileSecretState.None
                ),
            ]
        );
        yield return DpapiRow(
            "legacy-sweep-writes-null-elements",
            "null の要素を持つ connections.json は、正しい JSON として読み、書き戻すときに null の要素を消す（退避しない）",
            [
                .. SweepDefinitions(),
                .. Sweep(
                    Delete(
                        "target",
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Select(p => p.Id).Should().Equal(ReplacedId);
                            o.SecretExists("target").Should().BeFalse();
                            ShouldHaveNoNullElements(o);
                            o.BackupExists.Should().BeFalse("正しい JSON なので退避しない");
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Readable
                ),
                // 名前だけの変更は暗号ファイルに触れない（暗号フォルダへの作成の拒否も関係ない）
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        expect: o =>
                        {
                            o.Renamed.Should().BeTrue();
                            o.Store.Read()
                                .Profiles.Select(p => p.Name)
                                .Should()
                                .Equal("renamed", "replaced");
                            ShouldHaveNoNullElements(o);
                            o.BackupExists.Should().BeFalse();
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.None,
                    denied: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        replaced: "replaced",
                        throws: true,
                        expect: o =>
                        {
                            ShouldHaveThrownIo(o);
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Locked,
                    snapshot: true,
                    also: [PlantSecret("target", "secret")]
                ),
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(false),
                        replaced: "replaced",
                        expect: UpsertReplacedSucceeds(backup: null)
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Unreadable
                ),
                // 置き換え先の暗号ファイルが復号できなくても、置き換え先のプロファイルごと消える。対象の暗号ファイルは残る
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        replaced: "replaced",
                        expect: o =>
                        {
                            o.Renamed.Should().BeTrue();
                            o.Store.Read().Profiles.Select(p => p.Id).Should().Equal(TargetId);
                            o.SecretExists("replaced").Should().BeFalse();
                            o.SecretExists("target")
                                .Should()
                                .BeTrue("対象の暗号ファイルには触れない");
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Unreadable,
                    also: [PlantSecret("target", "secret")]
                ),
                .. Sweep(
                    Delete(
                        "target",
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Select(p => p.Id).Should().Equal(ReplacedId);
                            o.SecretExists("target")
                                .Should()
                                .BeFalse("復号できない暗号ファイルもプロファイルと一緒に消える");
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Unreadable
                ),
            ]
        );

        // 特性の固定：名前を変える対象が JSON に無いとき（Missing・旧配列形式・破損）の Rename は何も書かない。
        // XmlDoc は「何も書かず false」までで、置き換え先の暗号ファイルと退避ファイルには触れていない
        yield return DpapiRow(
            "legacy-sweep-rename-of-absent-target-writes-nothing",
            "特性の固定（文書に定めが無い今の振る舞い）：対象が JSON に無い Rename は false を返し、"
                + "置き換え先の暗号ファイルも退避ファイルも作らず、何も変えない",
            [
                .. SweepDefinitions(),
                // 根拠：SqlConnectionProfileStore.cs の Rename は対象が無ければ置き換え先の削除と SaveData（退避はここ）より前に false を返す
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                            o.ConnectionsExist.Should().BeFalse("書いていないので作らない");
                        }
                    ),
                    SweepJson.Missing,
                    ProfileSecretState.Locked,
                    snapshot: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        replaced: "replaced",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.Missing,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Unreadable,
                    snapshot: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        replaced: "replaced",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.None,
                    snapshot: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.Corrupt,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Rename(
                        "target",
                        "renamed",
                        replaced: "replaced",
                        expect: o =>
                        {
                            o.Renamed.Should().BeFalse();
                            ShouldBeUnchanged(o);
                        }
                    ),
                    SweepJson.Corrupt,
                    ProfileSecretState.Unreadable,
                    denied: true,
                    snapshot: true
                ),
            ]
        );

        // 読む入口 × connections.json の状態：どれもファイルを書き換えない・作らない（Read の XmlDoc「表示用の唯一の読む入口」）
        yield return DpapiRow(
            "legacy-sweep-reads-by-json-state",
            "読む入口（Read）は、connections.json が壊れていても無くても旧形式でも空の一覧として読んで分類で区別し、"
                + "復号できない・掴まれた暗号ファイルは読めないと返し、どの場合もファイルを書き換えず退避も作らない",
            [
                .. SweepDefinitions(),
                .. Sweep(
                    Read(o =>
                    {
                        o.Status.Should().Be(ConnectionFileStatus.Corrupt);
                        o.All.Should().BeEmpty();
                        ShouldBeUnchanged(o);
                    }),
                    SweepJson.Corrupt,
                    ProfileSecretState.None,
                    denied: true,
                    snapshot: true
                ),
                .. Sweep(
                    Read(ReadsNothing(ConnectionFileStatus.Corrupt)),
                    SweepJson.Corrupt,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Read(ReadsNothing(ConnectionFileStatus.Corrupt)),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Read(ReadsNothing(ConnectionFileStatus.Missing)),
                    SweepJson.Missing,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Read(o =>
                    {
                        // 読めなかったことを分類で返し、ディスクに残った一覧も前回接続も返さない（無いのと区別できる）
                        o.Status.Should().Be(ConnectionFileStatus.Unavailable);
                        o.All.Should().BeEmpty();
                        o.Last.Should().BeNull();
                        ShouldBeUnchanged(o);
                    }),
                    SweepJson.Locked,
                    ProfileSecretState.Readable,
                    snapshot: true
                ),
                .. Sweep(
                    Read(o =>
                    {
                        o.Status.Should().Be(ConnectionFileStatus.Loaded);
                        o.Last.Should().NotBeNull();
                        o.Last!.Value.Profile.Name.Should().Be("last");
                        o.Last!.Value.Password.Should()
                            .Be(new SavedPassword(SavedPasswordStatus.Loaded, "secret"));
                        ShouldBeUnchanged(o);
                    }),
                    SweepJson.NullElements,
                    ProfileSecretState.Readable,
                    denied: true,
                    snapshot: true
                ),
                .. Sweep(
                    Read(o =>
                    {
                        o.Status.Should().Be(ConnectionFileStatus.Loaded);
                        o.Last!.Value.Password.Should().Be(SavedPassword.Unreadable);
                        ShouldBeUnchanged(o);
                    }),
                    SweepJson.Healthy,
                    ProfileSecretState.Unreadable,
                    snapshot: true
                ),
                .. Sweep(
                    Read(o =>
                    {
                        o.Status.Should().Be(ConnectionFileStatus.Loaded);
                        o.Last!.Value.Password.Should().Be(SavedPassword.Unreadable);
                        ShouldBeUnchanged(o);
                    }),
                    SweepJson.Healthy,
                    ProfileSecretState.Locked,
                    lockShare: FileShare.None,
                    snapshot: true
                ),
                UnlockSecret(
                    ProfileContext.LastKey,
                    o =>
                        o.SecretExists(ProfileContext.LastKey)
                            .Should()
                            .BeTrue("読めなかっただけで消さない")
                ),
            ]
        );

        // 特性の固定：LoadPassword は暗号ファイルだけを見る。XmlDoc は「指定 ID のプロファイルに紐づくパスワードを復号して返す」までで、
        // connections.json の状態との関係は書いていない
        yield return DpapiRow(
            "legacy-sweep-load-password-ignores-json-state",
            "特性の固定（文書に定めが無い今の振る舞い）：LoadPassword は connections.json の状態に依らず暗号ファイルだけを読み、何も書き換えない",
            [
                .. SweepDefinitions(),
                .. LoadPasswordSweep(SweepJson.Missing, denied: false),
                .. LoadPasswordSweep(SweepJson.LegacyArray, denied: false),
                .. LoadPasswordSweep(SweepJson.Corrupt, denied: true),
                .. LoadPasswordSweep(SweepJson.NullElements, denied: false),
            ]
        );

        // 保存の入口 × connections.json の状態、と掴まれた connections.json
        yield return DpapiRow(
            "legacy-sweep-saves-and-locked-json",
            "Upsert・SaveLastUsed は旧形式・破損を退避して書き、読めない暗号ファイルを（空のパスワードなら消し、あれば置き換え）、"
                + "掴まれた connections.json の上では何も書かず暗号ファイルにも触れない",
            [
                .. SweepDefinitions(),
                // 根拠：LoadSecret の XmlDoc「読めないものを空として書き戻した保存が暗号ファイルを消す」。Upsert は空のパスワードを
                // 「保存しない」へ正規化して前の暗号ファイルを読めるかを見ずに消す。守りは画面の側（src/QuickER.Db.UI/CLAUDE.md の
                // 「読めない保存済みパスワードの暗号ファイルを、入力し直しなしに失わない」・ConnectionDialogTests）にある
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(false),
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Select(p => p.Id).Should().Equal(TargetId);
                            o.SecretExists("target")
                                .Should()
                                .BeFalse(
                                    "空のパスワードは暗号ファイルを持たない（読めない前のファイルも消える）"
                                );
                            o.BackupText.Should().Be(LegacyText);
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Unreadable
                ),
                // パスワードがあれば、読めなかった暗号ファイルは新しい内容で置き換わる（同じ根拠）
                .. Sweep(
                    SaveLast(
                        LastProf(),
                        "new",
                        expect: o =>
                        {
                            o.Store.Read().Profiles.Should().BeEmpty();
                            o.BackupText.Should().Be(CorruptText);
                            o.Store.Read()
                                .LastUsed!.Value.Password.Should()
                                .Be(new SavedPassword(SavedPasswordStatus.Loaded, "new"));
                        }
                    ),
                    SweepJson.Corrupt,
                    ProfileSecretState.Unreadable
                ),
                .. Sweep(
                    SaveLast(
                        LastProf(),
                        "new",
                        expect: o =>
                        {
                            o.BackupText.Should().Be(LegacyText);
                            o.Store.Read().LastUsed!.Value.Profile.Name.Should().Be("last");
                            o.Store.Read()
                                .LastUsed!.Value.Password.Should()
                                .Be(new SavedPassword(SavedPasswordStatus.Loaded, "new"));
                        }
                    ),
                    SweepJson.LegacyArray,
                    ProfileSecretState.Readable
                ),
                // 前回接続の保存は一覧を消さず、null の要素だけ消す
                .. Sweep(
                    SaveLast(
                        LastProf(),
                        "new",
                        expect: o =>
                        {
                            o.Store.Read()
                                .Profiles.Select(p => p.Name)
                                .Should()
                                .Equal("replaced", "target");
                            ShouldHaveNoNullElements(o);
                            o.BackupExists.Should().BeFalse();
                            o.Store.Read().LastUsed!.Value.Profile.Name.Should().Be("last");
                        }
                    ),
                    SweepJson.NullElements,
                    ProfileSecretState.Readable
                ),
                // 掴まれた connections.json では何も書かない・暗号ファイルにも触れず、渡したインスタンスも変えない
                .. Sweep(
                    SaveLast(
                        LastProf(),
                        "new",
                        throws: true,
                        expect: o =>
                        {
                            o.ShouldHaveThrownUnavailable();
                            o.Profile(ProfileContext.LastKey).SavePassword.Should().BeTrue();
                            o.SecretBytes(ProfileContext.LastKey).Should().Equal(GarbageBytes);
                        }
                    ),
                    SweepJson.Locked,
                    ProfileSecretState.Unreadable,
                    denied: true
                ),
                .. Sweep(
                    Upsert(
                        "target",
                        TargetProf(true),
                        "new",
                        throws: true,
                        expect: o =>
                        {
                            o.ShouldHaveThrownUnavailable();
                            o.SecretExists("target")
                                .Should()
                                .BeTrue("読めないので暗号ファイルにも触れない");
                        }
                    ),
                    SweepJson.Locked,
                    ProfileSecretState.Locked
                ),
            ]
        );
    }

    private static readonly byte[] GarbageBytes = [1, 2, 3, 4];

    /// <summary>LoadPassword を、connections.json の状態を変えて流す（暗号ファイルは読める・何も書き換えない）</summary>
    private static ProfileStep[] LoadPasswordSweep(SweepJson json, bool denied) =>
        Sweep(
            LoadPassword(
                "target",
                o =>
                {
                    o.Password.Should().Be(new SavedPassword(SavedPasswordStatus.Loaded, "secret"));
                    ShouldBeUnchanged(o);
                }
            ),
            json,
            ProfileSecretState.Readable,
            denied: denied,
            snapshot: true
        );

    /// <summary>読む入口の戻り値が指定の分類・「前回接続なし」で、何も書き換えていない</summary>
    private static Action<ProfileObservation> ReadsNothing(ConnectionFileStatus status) =>
        o =>
        {
            o.Status.Should().Be(status);
            o.Last.Should().BeNull();
            ShouldBeUnchanged(o);
        };

    // ---------------- 掃引の手の組み立て ----------------

    /// <summary>掃引の行の対象と置き換え先のプロファイルを、固定の Id で控える</summary>
    private static ProfileStep[] SweepDefinitions() =>
        [
            Define("target", TargetProf(false)),
            Define("replaced", Prof("replaced", "serverC", "db", false, p => p.Id = ReplacedId)),
        ];

    private static Func<ProfileContext, SqlConnectionProfile> TargetProf(bool save) =>
        Prof("target", "serverA", "db", save, p => p.Id = TargetId);

    private static Func<ProfileContext, SqlConnectionProfile> LastProf() =>
        Prof("last", "serverA", "db", true, p => p.Id = LastId);

    /// <summary>
    /// 手の前の状態を作り直して製品の 1 手を流す手の組
    /// </summary>
    /// <param name="op">製品の 1 手</param>
    /// <param name="json">手の前の connections.json</param>
    /// <param name="secret">手の対象の暗号ファイルの状態（<see cref="ProfileStepKind.Read"/> は前回接続の暗号ファイル）</param>
    /// <param name="denied">暗号フォルダへ作成の拒否を掛けるか</param>
    /// <param name="snapshot">手の前の全ファイルの状態を控えるか（<see cref="ShouldBeUnchanged"/> の比べ先）</param>
    /// <param name="lockShare">暗号ファイルを掴むときの共有の指定</param>
    /// <param name="also">対象以外の準備（置き換え先の手では対象の暗号ファイルを置く、など）</param>
    private static ProfileStep[] Sweep(
        ProfileStep op,
        SweepJson json,
        ProfileSecretState secret,
        bool denied = false,
        bool snapshot = false,
        FileShare lockShare = FileShare.Read,
        ProfileStep[]? also = null
    )
    {
        var key = op.Kind switch
        {
            ProfileStepKind.Read or ProfileStepKind.SaveLastUsed => ProfileContext.LastKey,
            ProfileStepKind.UpsertReplacing or ProfileStepKind.RenameReplacing => "replaced",
            _ => "target",
        };
        List<ProfileStep> steps =
        [
            // 状態の作り直し：前の手のロック・拒否・暗号ファイル・退避を残さない
            AllowCreate(),
            UnlockJson(),
            UnlockSecret("target"),
            UnlockSecret("replaced"),
            UnlockSecret(ProfileContext.LastKey),
            RemoveSecret("target"),
            RemoveSecret("replaced"),
            RemoveSecret(ProfileContext.LastKey),
            SetJson(SweepJsonText(json)),
        ];

        if (also is not null)
        {
            steps.AddRange(also);
        }

        if (key is not null)
        {
            ProfileStep[] setup = secret switch
            {
                ProfileSecretState.Readable => [PlantSecret(key, "secret")],
                ProfileSecretState.Unreadable => [GarbageSecret(key, GarbageBytes)],
                ProfileSecretState.Locked =>
                [
                    PlantSecret(key, "secret"),
                    LockSecret(key, lockShare),
                ],
                _ => [],
            };
            steps.AddRange(setup);
        }

        if (denied)
        {
            steps.Add(DenyCreate());
        }

        if (json == SweepJson.Locked)
        {
            steps.Add(LockJson(FileShare.None));
        }

        if (snapshot)
        {
            steps.Add(Probe(o => o.Memo["snap"] = Snapshot(o)));
        }

        steps.Add(op);

        if (json == SweepJson.Locked)
        {
            steps.Add(
                UnlockJsonThen(o =>
                    o.ConnectionsText.Should()
                        .Be(HealthyText, "読めなかったので 1 バイトも書いていない")
                )
            );
        }

        return [.. steps];
    }

    // ---------------- 表明 ----------------

    /// <summary>置き換えつきの Upsert（パスワードなし）が成功し、置き換え先も対象の暗号ファイルも残らない</summary>
    private static Action<ProfileObservation> UpsertReplacedSucceeds(string? backup) =>
        o =>
        {
            o.Store.Read().Profiles.Select(p => p.Id).Should().Equal(TargetId);
            o.SecretExists("target")
                .Should()
                .BeFalse("パスワードなしの保存は暗号ファイルを持たない");
            o.SecretExists("replaced").Should().BeFalse("置き換え先の暗号ファイルは消える");

            if (backup is null)
            {
                o.BackupExists.Should().BeFalse();
            }
            else
            {
                o.BackupText.Should().Be(backup, "上書きの前に元の内容を退避する");
            }
        };

    /// <summary>connections.json の profiles に null の要素が無い</summary>
    private static void ShouldHaveNoNullElements(ProfileObservation o)
    {
        using var document = o.ConnectionsJson();
        document
            .RootElement.GetProperty("profiles")
            .EnumerateArray()
            .Should()
            .OnlyContain(e => e.ValueKind == JsonValueKind.Object, "null の要素は書き戻さない");
    }

    /// <summary>手が暗号ファイルの削除の失敗（共有違反）で止まったことを確かめる</summary>
    private static void ShouldHaveThrownIo(ProfileObservation o) =>
        o.Thrown.Should().BeAssignableTo<IOException>("掴まれた暗号ファイルは消せない");

    /// <summary>手の前に控えた全ファイルの状態（<see cref="Snapshot"/>）から何も変わっていない</summary>
    private static void ShouldBeUnchanged(ProfileObservation o) =>
        Snapshot(o).Should().Be((string)o.Memo["snap"]!, "何も書き換えず・作らず・消さない");

    /// <summary>connections.json・退避・暗号ファイルの有無と中身（掴まれて読めないものは印だけ）</summary>
    private static string Snapshot(ProfileObservation o)
    {
        var text = new StringBuilder();
        text.Append("json=")
            .Append(ReadOrLocked(() => o.ConnectionsExist ? o.ConnectionsText : "<none>"));
        text.Append("\nbackup=")
            .Append(ReadOrLocked(() => o.BackupExists ? o.BackupText : "<none>"));

        if (Directory.Exists(o.Store.SecretsFolder))
        {
            foreach (
                var file in Directory
                    .GetFiles(o.Store.SecretsFolder)
                    .OrderBy(f => f, StringComparer.Ordinal)
            )
            {
                text.Append('\n')
                    .Append(Path.GetFileName(file))
                    .Append('=')
                    .Append(ReadOrLocked(() => Convert.ToHexString(File.ReadAllBytes(file))));
            }
        }

        return text.ToString();
    }

    private static string ReadOrLocked(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (IOException)
        {
            return "<locked>";
        }
    }

    // ---------------- 手の作り方（掃引用） ----------------

    /// <summary>行の中の名前のプロファイルを作って控える（手として数えない）</summary>
    private static ProfileStep Define(
        string key,
        Func<ProfileContext, SqlConnectionProfile> make
    ) => new(ProfileStepKind.Probe) { Key = key, Make = make };

    /// <summary>connections.json を作り直す（null なら消す）</summary>
    private static ProfileStep SetJson(string? text) =>
        new(ProfileStepKind.SetJson) { Text = text };

    /// <summary>読める暗号ファイルを置く</summary>
    private static ProfileStep PlantSecret(string key, string password) =>
        new(ProfileStepKind.PlantSecret) { Key = key, Text = password };

    private static ProfileStep RemoveSecret(string key) =>
        new(ProfileStepKind.RemoveSecret) { Key = key };

    /// <summary>暗号ファイルを掴んだハンドルを閉じる（表明つきにもできる）</summary>
    private static ProfileStep UnlockSecret(
        string key,
        Action<ProfileObservation>? expect = null
    ) => new(ProfileStepKind.UnlockSecret) { Key = key, Expect = expect };

    private static ProfileStep AllowCreate() => new(ProfileStepKind.AllowCreate);
}
