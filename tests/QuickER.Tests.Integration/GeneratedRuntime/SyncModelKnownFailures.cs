using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>既知の失敗 1 件（直すまでモデルベーステストから一時的に外す）</summary>
/// <param name="Finding">指摘の番号（三巡目レビューのまとめの番号。新しく見つけたものはその番号の案）</param>
/// <param name="Section">直す区切り（30 または 31）</param>
/// <param name="Scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
/// <param name="Signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
internal sealed record KnownSyncModelFailure(
    string Finding,
    int Section,
    string Scenario,
    string Signature
);

/// <summary>
/// 同期のモデルベーステストの既知の失敗の一覧（厳格）。
/// </summary>
/// <remarks>
/// <para>
/// 載っているシナリオがその署名の組で失敗しなければ赤（直ったか、別の形で失敗した）。載っていない失敗も赤。
/// 乱数の種の失敗は、ここに載ったどれかの署名に当たれば既知として数える。
/// </para>
/// <para>
/// 区切り 30 の終わりに Section=30 の行を、区切り 31 で Section=31 の行を消し、この一覧を空にする。
/// </para>
/// </remarks>
internal static class SyncModelKnownFailures
{
    public static IReadOnlyList<KnownSyncModelFailure> All { get; } =
    [
        new(
            "#23 系（要判断）",
            31,
            "23-insert-after-discarded-blob-record",
            "2/diverge:model_items:payload:value"
        ),
        new(
            "#23 系（要判断）",
            31,
            "23-insert-after-discarded-blob-record",
            "3/blob-empty:model_items"
        ),
        new(
            "#22",
            31,
            "22-forced-overwrite-hides-server-blob",
            "2/diverge:model_items:payload:value"
        ),
        new(
            "#14",
            31,
            "14-owed-blob-source-gone",
            "1/throw:DownloadAsync~SyncTableBase.CopyBinaryColumnAsync>UnboundedBinaryColumnEngine.WriteAsync:SQLite19:NOT NULL constraint failed: model_items.payload"
        ),
        new(
            "#14（静止化）",
            31,
            "14-owed-blob-source-gone",
            "1/quiesce-throw:DownloadAsync~SyncTableBase.CopyBinaryColumnAsync>UnboundedBinaryColumnEngine.WriteAsync:SQLite19:NOT NULL constraint failed: model_items.payload"
        ),
        new(
            "#27（N5 の決着の後に見える）",
            31,
            "n5-versionless-delete-refused-after-stub",
            "2/diverge:model_items:missing-on-device"
        ),
        new(
            "#27（N5 の決着の後に見える）",
            31,
            "n5-versionless-delete-refused-after-stub",
            "2/diverge:model_items:payload:missing-on-device"
        ),
        new(
            "#27（N5 の決着の後に見える）",
            31,
            "n5-versionless-delete-refused-after-stub",
            "3/lost:model_items:deleted-but-present"
        ),
        new("#16", 31, "16-failed-write-record-overwrites-blob", "3/blob-empty:model_items"),
        new("#16", 31, "16-failed-update-record-reverts-row", "3/lost:model_folders:value"),
        new("#16", 31, "16-failed-blob-write-record-reverts-blob", "3/blob-lost:model_items"),
        new(
            "#16",
            31,
            "16-failed-blob-write-record-spurious-conflict",
            "3/spurious:model_items:ModifiedOnServer"
        ),
        new(
            "#23",
            31,
            "23-localwins-resurrect-without-blob",
            "2/diverge:model_items:payload:value"
        ),
        new("#23", 31, "23-localwins-resurrect-without-blob", "3/blob-empty:model_items"),
        new("#26", 31, "26-external-table-overwrites-pending-edit", "5/expect-local:model_items"),
        new("#27", 31, "27-key-only-stub-delete", "2/diverge:model_items:missing-on-device"),
        new(
            "#27",
            31,
            "27-key-only-stub-delete",
            "2/diverge:model_items:payload:missing-on-device"
        ),
        new("#27", 31, "27-key-only-stub-delete", "3/lost:model_items:deleted-but-present"),
        new("#27", 31, "27-held-object-delete", "3/lost:model_items:deleted-but-present"),
        new("#28", 31, "28-hook-assigns-key", "3/lost:model_folders:present-but-absent"),
        new(
            "3r rs #6",
            31,
            "rs6-offline-delete-reinsert",
            "3/spurious:model_folders:DuplicateOnServer"
        ),
    ];
}
