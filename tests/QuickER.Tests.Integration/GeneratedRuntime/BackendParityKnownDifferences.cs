using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>受け入れた手の差を持つ手の種類（<see cref="DeviatingOp"/> が印を付ける）</summary>
public enum ParityDeviation
{
    /// <summary>
    /// 子を持つ親（子に参照されている行）の削除＝<c>DeleteAsync(id)</c> か、子を子孫として持たないグラフ保存の削除（DB は ON DELETE の
    /// 参照動作どおりに断るか子を消す・SET NULL / SET DEFAULT なら子の外部キーを空ける。外部キーを検査しない実装先は親だけを消し、
    /// 子をそのまま残す）
    /// </summary>
    ParentDeleteWithChildren,

    /// <summary>親の無い行の挿入（DB は外部キーで断る・外部キーを検査しない実装先は挿入する）</summary>
    OrphanInsert,

    /// <summary>
    /// 子に参照されている親のキー（一意制約を参照する外部キーの代替キー）の更新（DB は ON UPDATE NO ACTION で断る・外部キーを
    /// 検査しない実装先は更新し、存在しない値を指す子を残す）
    /// </summary>
    ReferencedKeyUpdate,

    /// <summary>
    /// 子に参照されている親を、編集した依存行を同じ保存に持ったままグラフ保存で消す（DB は NO ACTION なら断り、SET NULL /
    /// SET DEFAULT なら空ける。EF Core は追跡中の依存行にクライアント側の削除の振る舞いを当てる＝H5・P2-5-N1。外部キーを検査
    /// しない実装先は親だけを消す）
    /// </summary>
    DependentHeldParentDelete,

    /// <summary>参照されていない親のキー（一意制約を参照する外部キーの代替キー）の更新（EF Core は代替キーを書かない＝H1）</summary>
    AlternateKeyUpdate,

    /// <summary>Before が値を書き換えてから false を返す規則に当たる保存（EF Core＝H3・リモート＝P2-5-R1）</summary>
    HookEditsSkippedRow,

    /// <summary>Before が値を書き換えて続ける規則に当たる削除（リモート＝P2-5-R1）</summary>
    HookEditsDeletedRow,
}

/// <summary>直す区切りのある手の差の 1 項目（実装先・手の種類・指摘・直す区切り）</summary>
public sealed record PendingDeviation(
    ParityBackendKind Backend,
    ParityDeviation Deviation,
    string Finding,
    string FixedIn
);

/// <summary>受け入れた手の差の 1 項目（実装先・手の種類・指摘・受け入れた理由）</summary>
public sealed record AcceptedDeviation(
    ParityBackendKind Backend,
    ParityDeviation Deviation,
    string Finding,
    string Reason
);

/// <summary>
/// 今わかっている実装先の違い（基準の QuickER 版と食い違うシナリオ・実装先・観点）。
/// </summary>
/// <remarks>
/// <para>
/// 一覧は厳格に扱う。載っていない違いが出たら失敗し、載っている項目が違いを出さなくなっても失敗する（直したら一覧から
/// 外す）。項目は範囲＝シナリオの名前・署名＝<see cref="SignatureOf"/>（実装先と観点）で当たり、期待・実際の中身は見ない。
/// 直す区切りのある違いは <see cref="Pending"/>、直さないと決めた違いは <see cref="Accepted"/>（理由つき）で作る。
/// 照合の規則は <see cref="KnownFailureSet"/> が持つ。
/// </para>
/// </remarks>
public static class BackendParityKnownDifferences
{
    /// <summary>乱数の操作の列の違いの範囲</summary>
    public const string RandomScope = "random";

    private const ParityBackendKind EfCore = ParityBackendKind.EfCore;
    private const ParityBackendKind InMemory = ParityBackendKind.InMemory;
    private const ParityBackendKind Remote = ParityBackendKind.Remote;
    private const ParityBackendKind RemoteEfCoreOnly = ParityBackendKind.RemoteEfCoreOnly;

    /// <summary>既知の違いの一覧（決まった形のシナリオ）</summary>
    public static IReadOnlyList<KnownEntry> All { get; } =
    [
        // F4 の決定で残した差（受容・直さない）: 外した子への参照を持ったまま、その子を直接もう一度保存すると、QuickER 版・
        // インメモリ・リモートは 0 行の削除を黙認し、EF Core は行なしとして NotFound にする（「行なし削除」の既知の非対称）
        Accepted("save-removed-child-again", EfCore, "outcome", "F4", RowlessDeleteReason),
        Accepted("save-removed-child-again", EfCore, "hooks-after", "F4", RowlessDeleteReason),
        // 同じ差を、EF Core 単独で生成したサーバーのリモートも持つ（サーバーの EF Core 版 Repository の振る舞い）
        Accepted(
            "save-removed-child-again",
            RemoteEfCoreOnly,
            "outcome",
            "F4",
            RowlessDeleteReason
        ),
        Accepted(
            "save-removed-child-again",
            RemoteEfCoreOnly,
            "hooks-after",
            "F4",
            RowlessDeleteReason
        ),
        // 区切り 39 の決定（受容・直さない）: DB にある行を消し、同じキーの新しい行を足す保存は、どの実装先も同じく
        // 置き換わる（保存先・フックは一致）。EF Core は同じキーの削除と挿入を 1 つの UPDATE に畳むので、戻り値の行数だけが 1 になる
        Accepted(
            "graph-remove-and-add-same-key-roots-added-first",
            EfCore,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        Accepted(
            "graph-remove-and-add-same-key-roots-removed-first",
            EfCore,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        Accepted(
            "graph-remove-and-add-same-key-roots-added-first",
            RemoteEfCoreOnly,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        Accepted(
            "graph-remove-and-add-same-key-roots-removed-first",
            RemoteEfCoreOnly,
            "outcome",
            "区切り 39",
            SameKeyReplaceReason
        ),
        // H1（修正 D）: 一意制約を参照する外部キーの親の列は、EF Core では HasPrincipalKey の代替キーになり、State=Modified でも
        // 書かれない。参照されていない代替キーでも書かない（保存先が古いまま）。参照されているとき、QuickER 版は ON UPDATE NO ACTION
        // で断られ、EF Core は書かずに成功を返す
        Pending(
            "h1-alternate-key-referenced-update",
            EfCore,
            "outcome",
            "H1",
            FixD,
            "updated=True"
        ),
        Pending("h1-alternate-key-referenced-update", EfCore, "state", "H1", FixD),
        Pending("h1-alternate-key-referenced-update", EfCore, "slots", "H1", FixD),
        Pending("h1-alternate-key-referenced-save", EfCore, "outcome", "H1", FixD, "rows=1"),
        Pending("h1-alternate-key-referenced-save", EfCore, "state", "H1", FixD),
        Pending("h1-alternate-key-referenced-save", EfCore, "slots", "H1", FixD),
        Pending("h1-alternate-key-referenced-save", EfCore, "hooks-after", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-update", EfCore, "state", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-update", EfCore, "slots", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-save", EfCore, "state", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-save", EfCore, "slots", "H1", FixD),
        // H5（修正 D）: 任意 × NO ACTION の外部キーで参照されている親を、依存行を編集して同じ保存に持ったまま消すと、EF Core は
        // 追跡中の依存行の外部キーを null にして保存を通す（QuickER 版は外部キーで断る）。依存行を編集しない形は割れない（実測）
        Pending("h5-referrer-delete-dependent-modified", EfCore, "outcome", "H5", FixD, "rows=2"),
        Pending("h5-referrer-delete-dependent-modified", EfCore, "state", "H5", FixD),
        Pending("h5-referrer-delete-dependent-modified", EfCore, "slots", "H5", FixD),
        Pending("h5-referrer-delete-dependent-modified", EfCore, "hooks-after", "H5", FixD),
        Pending(
            "h5-contact-watcher-delete-dependent-modified",
            EfCore,
            "outcome",
            "H5",
            FixD,
            "rows=2"
        ),
        Pending("h5-contact-watcher-delete-dependent-modified", EfCore, "state", "H5", FixD),
        Pending("h5-contact-watcher-delete-dependent-modified", EfCore, "slots", "H5", FixD),
        Pending("h5-contact-watcher-delete-dependent-modified", EfCore, "hooks-after", "H5", FixD),
        Pending(
            "h5-contact-code-delete-dependent-modified",
            EfCore,
            "outcome",
            "H5",
            FixD,
            "rows=2"
        ),
        Pending("h5-contact-code-delete-dependent-modified", EfCore, "state", "H5", FixD),
        Pending("h5-contact-code-delete-dependent-modified", EfCore, "slots", "H5", FixD),
        Pending("h5-contact-code-delete-dependent-modified", EfCore, "hooks-after", "H5", FixD),
        // H3（修正 D）: Before が値を書き換えてから false を返すと、EF Core は飛ばした更新のメモリの書き換えを戻し、飛ばした削除を
        // UPDATE として流す
        Pending("h3-edit-then-skip-update", EfCore, "slots", "H3", FixD),
        Pending("h3-edit-then-skip-delete", EfCore, "outcome", "H3", FixD, "rows=2"),
        Pending("h3-edit-then-skip-delete", EfCore, "state", "H3", FixD),
        Pending("h3-edit-then-skip-delete", EfCore, "slots", "H3", FixD),
        // P2-5-N1（修正 D）: H5 と同じ根（追跡中の依存行へのクライアント側の削除の振る舞い）。参照されている親を、依存行を編集して
        // 同じ保存に持ったまま消すと、EF Core は保存先は QuickER 版と同じにしたまま、依存行の外部キーをメモリで書き換え（必須 × NO
        // ACTION＝断られる保存でも null・SET NULL＝null）、消す親のコレクションへ依存行を足す
        Pending("contact-owner-delete-dependent-modified", EfCore, "slots", "P2-5-N1", FixD),
        Pending("contact-backup-delete-dependent-modified", EfCore, "slots", "P2-5-N1", FixD),
        Pending("contact-fallback-delete-dependent-modified", EfCore, "slots", "P2-5-N1", FixD),
        // P2-5-N2（修正 D）: ナビゲーションと外部キーの値の食い違い（どちらを正とするかは修正 D で決める）。親のコレクションに持った
        // 子の外部キーを別の親へ書き換えて保存すると、QuickER 版は外部キーの値を、EF Core はナビゲーション（持っている親）を書く
        Pending("n2-navigation-overrides-foreign-key", EfCore, "state", "P2-5-N2", FixD),
        Pending("n2-navigation-overrides-foreign-key", EfCore, "slots", "P2-5-N2", FixD),
        // P2-5-N3（修正 D）: 同じ行の別インスタンス。Include が関係ごとに別のインスタンスにした同じ連絡先を含むグラフを保存すると、
        // QuickER 版は両方の編集を書き、EF Core は同じキーの 2 つめのインスタンスを追跡できずに InvalidOperationException
        Pending(
            "contacts-include-and-save",
            EfCore,
            "outcome",
            "P2-5-N3",
            FixD,
            "InvalidOperationException"
        ),
        Pending("contacts-include-and-save", EfCore, "state", "P2-5-N3", FixD),
        Pending("contacts-include-and-save", EfCore, "slots", "P2-5-N3", FixD),
        Pending("contacts-include-and-save", EfCore, "hooks-before", "P2-5-N3", FixD),
        Pending("contacts-include-and-save", EfCore, "hooks-after", "P2-5-N3", FixD),
        // P2-5-R1（修正 D）: リモートのフックはサーバーのインスタンスに走り、クライアントへ戻るのは保存した行の値だけなので、
        // 飛ばした行・削除した行へ Before が書き換えた値はクライアントのグラフに戻らない（戻すか、docs に「戻らない」と書いて受け入れるかは
        // 修正 D でユーザーが決める）
        Pending("h3-edit-then-skip-update", Remote, "slots", "P2-5-R1", FixD),
        Pending("h3-edit-then-skip-delete", Remote, "slots", "P2-5-R1", FixD),
        Pending("hook-edit-then-continue-delete", Remote, "slots", "P2-5-R1", FixD),
        // H3 の挿入の形：EF Core は書き換えてから飛ばした挿入を NotFound の競合にして保存全体を戻す（同じ保存のほかの行の更新も
        // 書かない）。RemoteEfCoreOnly の slots は H3（戻った更新の RowState）と P2-5-R1（挿入の書き換えが戻らない）が重なる
        Pending("h3-edit-then-skip-insert", EfCore, "outcome", "H3", FixD, "NotFound"),
        Pending("h3-edit-then-skip-insert", EfCore, "state", "H3", FixD),
        Pending("h3-edit-then-skip-insert", EfCore, "slots", "H3", FixD),
        Pending("h3-edit-then-skip-insert", EfCore, "hooks-after", "H3", FixD),
        Pending("h3-edit-then-skip-insert", Remote, "slots", "P2-5-R1", FixD),
        Pending("h3-edit-then-skip-insert", RemoteEfCoreOnly, "outcome", "H3", FixD, "NotFound"),
        Pending("h3-edit-then-skip-insert", RemoteEfCoreOnly, "state", "H3", FixD),
        Pending("h3-edit-then-skip-insert", RemoteEfCoreOnly, "slots", "H3", FixD),
        Pending("h3-edit-then-skip-insert", RemoteEfCoreOnly, "hooks-after", "H3", FixD),
        // EF Core 単独で生成したサーバーのリモート（RemoteEfCoreOnly）も、サーバーの EF Core 版 Repository が同じ違いを持つ（H1・H3・H5・
        // P2-5-N1・P2-5-N2）。クライアントのグラフはサーバーのインスタンスでなく応答の値で書き戻されるので、フックの書き換えが戻らない
        // 違いは P2-5-R1（Remote と同じ）、EF Core のメモリの fix-up が見えるのは応答の値に載る SET NULL だけ（P2-5-N1）
        Pending(
            "h1-alternate-key-referenced-update",
            RemoteEfCoreOnly,
            "outcome",
            "H1",
            FixD,
            "updated=True"
        ),
        Pending("h1-alternate-key-referenced-update", RemoteEfCoreOnly, "state", "H1", FixD),
        Pending("h1-alternate-key-referenced-update", RemoteEfCoreOnly, "slots", "H1", FixD),
        Pending(
            "h1-alternate-key-referenced-save",
            RemoteEfCoreOnly,
            "outcome",
            "H1",
            FixD,
            "rows=1"
        ),
        Pending("h1-alternate-key-referenced-save", RemoteEfCoreOnly, "state", "H1", FixD),
        Pending("h1-alternate-key-referenced-save", RemoteEfCoreOnly, "slots", "H1", FixD),
        Pending("h1-alternate-key-referenced-save", RemoteEfCoreOnly, "hooks-after", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-update", RemoteEfCoreOnly, "state", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-update", RemoteEfCoreOnly, "slots", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-save", RemoteEfCoreOnly, "state", "H1", FixD),
        Pending("h1-alternate-key-unreferenced-save", RemoteEfCoreOnly, "slots", "H1", FixD),
        Pending(
            "h5-referrer-delete-dependent-modified",
            RemoteEfCoreOnly,
            "outcome",
            "H5",
            FixD,
            "rows=2"
        ),
        Pending("h5-referrer-delete-dependent-modified", RemoteEfCoreOnly, "state", "H5", FixD),
        Pending("h5-referrer-delete-dependent-modified", RemoteEfCoreOnly, "slots", "H5", FixD),
        Pending(
            "h5-referrer-delete-dependent-modified",
            RemoteEfCoreOnly,
            "hooks-after",
            "H5",
            FixD
        ),
        Pending(
            "h5-contact-watcher-delete-dependent-modified",
            RemoteEfCoreOnly,
            "outcome",
            "H5",
            FixD,
            "rows=2"
        ),
        Pending(
            "h5-contact-watcher-delete-dependent-modified",
            RemoteEfCoreOnly,
            "state",
            "H5",
            FixD
        ),
        Pending(
            "h5-contact-watcher-delete-dependent-modified",
            RemoteEfCoreOnly,
            "slots",
            "H5",
            FixD
        ),
        Pending(
            "h5-contact-watcher-delete-dependent-modified",
            RemoteEfCoreOnly,
            "hooks-after",
            "H5",
            FixD
        ),
        Pending(
            "h5-contact-code-delete-dependent-modified",
            RemoteEfCoreOnly,
            "outcome",
            "H5",
            FixD,
            "rows=2"
        ),
        Pending("h5-contact-code-delete-dependent-modified", RemoteEfCoreOnly, "state", "H5", FixD),
        Pending("h5-contact-code-delete-dependent-modified", RemoteEfCoreOnly, "slots", "H5", FixD),
        Pending(
            "h5-contact-code-delete-dependent-modified",
            RemoteEfCoreOnly,
            "hooks-after",
            "H5",
            FixD
        ),
        Pending("h3-edit-then-skip-update", RemoteEfCoreOnly, "slots", "P2-5-R1", FixD),
        Pending("h3-edit-then-skip-delete", RemoteEfCoreOnly, "outcome", "H3", FixD, "rows=2"),
        Pending("h3-edit-then-skip-delete", RemoteEfCoreOnly, "state", "H3", FixD),
        Pending("h3-edit-then-skip-delete", RemoteEfCoreOnly, "slots", "P2-5-R1", FixD),
        Pending("hook-edit-then-continue-delete", RemoteEfCoreOnly, "slots", "P2-5-R1", FixD),
        Pending(
            "contact-backup-delete-dependent-modified",
            RemoteEfCoreOnly,
            "slots",
            "P2-5-N1",
            FixD
        ),
        Pending("n2-navigation-overrides-foreign-key", RemoteEfCoreOnly, "state", "P2-5-N2", FixD),
        Pending("n2-navigation-overrides-foreign-key", RemoteEfCoreOnly, "slots", "P2-5-N2", FixD),
    ];

    /// <summary>直す区切り（H1・H3・H5 と P2-5 で見つけた EF Core・リモートの違い）</summary>
    private const string FixD = "修正 D";

    /// <summary>
    /// 受け入れた手の差（実装先×手の種類）。宣言した手を流すと、その実装先だけを、その手から後の比較から外す
    /// （<see cref="ParityScenario.RunAsync"/>。外した位置と手の数は <see cref="ParityRunResult.Cuts"/> に残る）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 手を全実装先から作らないと、その手でほかの実装先を見る機会まで消える（H5：インメモリの P1・P2 のために子を持つ親の
    /// <c>DeleteAsync(id)</c> を作らなかったので、EF Core の削除の違いが網の外にあった）。宣言は実装先を名指しし、手は
    /// 生成器・シナリオが <see cref="DeviatingOp"/> で印を付ける（実装先の結果からは推測しない）。
    /// </para>
    /// <para>
    /// 外した後を比べないのは、外部キーを検査しない実装先の保存先がその手の後ずっと食い違い、後の手を比べても同じ差を
    /// 繰り返すだけになるため。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AcceptedDeviation> AcceptedDeviations { get; } =
    [
        // P1・P2（受容・直さない）: インメモリは外部キーを検査せず、参照動作（ON DELETE CASCADE など）も再現しない。
        // DeleteAsync(id) で親を消しても子は残り（P1）、親の無い明細・無い顧客を指す注文も挿入する（P2）。
        // インメモリの振る舞いは変えず、docs の「実 DB との既知の違い」に書く（決定 2026-10-13・docs は区切り 25）
        new(InMemory, ParityDeviation.ParentDeleteWithChildren, "P1", InMemoryForeignKeyReason),
        new(InMemory, ParityDeviation.OrphanInsert, "P2", InMemoryForeignKeyReason),
        new(InMemory, ParityDeviation.ReferencedKeyUpdate, "P2", InMemoryForeignKeyReason),
        new(InMemory, ParityDeviation.DependentHeldParentDelete, "P1", InMemoryForeignKeyReason),
    ];

    /// <summary>その実装先で、その手の差を受け入れたか</summary>
    public static bool Accepts(ParityBackendKind backend, ParityDeviation deviation) =>
        AcceptedDeviations.Any(d => d.Backend == backend && d.Deviation == deviation);

    /// <summary>
    /// 直す区切りのある手の差（実装先×手の種類）。乱数の操作の列（<see cref="ParityScenario.CutPendingDeviations"/>）でだけ、
    /// 宣言した手から後のその実装先の比較を外す（決まった形のシナリオは打ち切らず、<see cref="All"/> の Pending の項目として実際に
    /// 赤を照合する）。直したら行を消すと比較が戻る。
    /// </summary>
    /// <remarks>
    /// 引き金の手は生成器が列の末尾にだけ置く（列の途中で EF Core が割れる手を作ると、その種の EF Core の比較がほぼ消える）。
    /// </remarks>
    public static IReadOnlyList<PendingDeviation> PendingDeviations { get; } =
    [
        new(EfCore, ParityDeviation.DependentHeldParentDelete, "H5", FixD),
        new(EfCore, ParityDeviation.ReferencedKeyUpdate, "H1", FixD),
        new(EfCore, ParityDeviation.AlternateKeyUpdate, "H1", FixD),
        new(EfCore, ParityDeviation.HookEditsSkippedRow, "H3", FixD),
        new(Remote, ParityDeviation.HookEditsSkippedRow, "P2-5-R1", FixD),
        new(Remote, ParityDeviation.HookEditsDeletedRow, "P2-5-R1", FixD),
        new(RemoteEfCoreOnly, ParityDeviation.DependentHeldParentDelete, "H5", FixD),
        new(RemoteEfCoreOnly, ParityDeviation.ReferencedKeyUpdate, "H1", FixD),
        new(RemoteEfCoreOnly, ParityDeviation.AlternateKeyUpdate, "H1", FixD),
        new(RemoteEfCoreOnly, ParityDeviation.HookEditsSkippedRow, "H3", FixD),
        new(RemoteEfCoreOnly, ParityDeviation.HookEditsDeletedRow, "P2-5-R1", FixD),
    ];

    /// <summary>その実装先で、その手の差を直す区切りつきで見逃しているか</summary>
    public static bool Defers(ParityBackendKind backend, ParityDeviation deviation) =>
        PendingDeviations.Any(d => d.Backend == backend && d.Deviation == deviation);

    /// <summary>乱数の操作の列で一時的に見逃す違い（<see cref="Tolerance"/> で作る）</summary>
    public static IReadOnlyList<KnownEntry> RandomTolerances { get; } = [];

    /// <summary>一覧（決まった形の違いと、乱数の列の見逃し）</summary>
    public static KnownFailureSet Set { get; } =
        new("BackendParity", [.. All, .. RandomTolerances]);

    private const string RowlessDeleteReason = "行なし削除の非対称（F4 の決定で直さない）";
    private const string SameKeyReplaceReason =
        "同じキーの削除と挿入の行数（区切り 39 の決定で直さない）";
    private const string InMemoryForeignKeyReason =
        "インメモリは外部キーと参照動作を再現しない（決定 2026-10-13・docs の実 DB との既知の違い）";

    /// <summary>違いの署名（実装先と観点）</summary>
    public static string SignatureOf(ParityBackendKind backend, string aspect) =>
        $"{backend}:{aspect}";

    /// <summary>署名を実装先と観点へ戻す</summary>
    public static (ParityBackendKind Backend, string Aspect) Split(string signature)
    {
        var index = signature.IndexOf(':', StringComparison.Ordinal);
        return (Enum.Parse<ParityBackendKind>(signature[..index]), signature[(index + 1)..]);
    }

    /// <summary>直さないと決めた違いの項目を作る</summary>
    public static KnownEntry Accepted(
        string scenario,
        ParityBackendKind backend,
        string aspect,
        string finding,
        string reason
    ) =>
        new(
            finding,
            KnownKind.Accepted,
            null,
            reason,
            scenario,
            KnownMatch.ExactSignature,
            SignatureOf(backend, aspect)
        );

    /// <summary>直す区切りのある違いの項目を作る</summary>
    /// <param name="reasonContains">違いの詳細（期待と実際）に含まれるべき断片（無ければ見ない）</param>
    public static KnownEntry Pending(
        string scenario,
        ParityBackendKind backend,
        string aspect,
        string finding,
        string fixedIn,
        string? reasonContains = null
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            scenario,
            KnownMatch.ExactSignature,
            SignatureOf(backend, aspect),
            ReasonContains: reasonContains
        );

    /// <summary>
    /// 乱数の操作の列で、決まった形の一覧に残っている指摘の違いを一時的に見逃す項目を作る
    /// （同じ指摘・同じ区切りの項目が <see cref="All"/> に残っている間だけ有効＝整合テストが強制する）
    /// </summary>
    /// <param name="finding">指摘</param>
    /// <param name="fixedIn">直す区切り</param>
    /// <param name="covers">その違いがこの指摘のものか</param>
    public static KnownEntry Tolerance(
        string finding,
        string fixedIn,
        Func<ParityViolation, bool> covers
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            RandomScope,
            KnownMatch.Predicate,
            Predicate: failure => failure.Source is ParityViolation violation && covers(violation),
            RandomSeeds: true,
            Reproduction: KnownReproduction.NotGuaranteed
        );

    /// <summary>違いを照合に渡す失敗にする（元の違いを持つ）</summary>
    /// <param name="scope">範囲（シナリオの名前か <see cref="RandomScope"/>）</param>
    /// <param name="violation">違い</param>
    /// <param name="seed">乱数の種（決まった形のシナリオなら null）</param>
    public static EngineFailure ToFailure(
        string scope,
        ParityViolation violation,
        int? seed = null
    ) =>
        new(
            scope,
            SignatureOf(violation.Backend, violation.Aspect),
            violation.Describe(),
            seed,
            violation
        );
}
