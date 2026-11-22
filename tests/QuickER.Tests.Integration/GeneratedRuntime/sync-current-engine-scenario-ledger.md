<!-- 手で直さない生成物（SyncSupportAdoSqliteCurrentEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord が、今のエンジンの端末で決定的なシナリオを流した結果から作る）。書き直し：$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj --filter "FullyQualifiedName~ScenarioLedgerDrift"; $env:QUICKER_REGEN_FIXTURES=$null -->

# 今のエンジンの端末の決定的なシナリオの控え

green: 484 / known: 10 / unsupported: 0 / differs: 8

| シナリオ | 結果 | 詳細 | 今のエンジンだけ（新しいエンジンの端末での結果） |
|---|---|---|---|
| 12-selfref-propagation | green |  |  |
| 12-selfref-propagation-completes-in-one-run | green |  |  |
| 13-propagation-pending-label-parent | green |  |  |
| 13-propagation-pending-row-parent | green |  |  |
| 13-propagation-truncated-child | green |  |  |
| 14-owed-blob-source-gone | green |  |  |
| 15-propagation-excluded-child | green |  |  |
| 15-propagation-versionless-child | green |  |  |
| 16-failed-blob-write-record-reverts-blob | green |  |  |
| 16-failed-blob-write-record-spurious-conflict | green |  |  |
| 16-failed-update-record-reverts-row | green |  |  |
| 16-failed-write-record-overwrites-blob | green |  |  |
| 17-serverwins-deleted-parent | green |  |  |
| 18-serverwins-restore-subtree | green |  |  |
| 19-varchar-key-paging-skips-rows | green |  |  |
| 20-download-selfref-order | green |  |  |
| 20-refresh-selfref-order | green |  |  |
| 21-versionless-held-row-referenced | green |  |  |
| 22-forced-overwrite-hides-server-blob | green |  |  |
| 23-insert-after-discarded-blob-record | green |  |  |
| 23-inserted-row-blob-retry | green |  |  |
| 23-localwins-resurrect-without-blob | green |  |  |
| 24-deferred-upsert-carries-children | green |  |  |
| 24-unique-delete-then-insert | green |  |  |
| 25-datetime-key-local | green |  |  |
| 25-datetime-key-unspecified | green |  |  |
| 25-datetime-key-utc | green |  |  |
| 25-legacy-ack-key-text | green |  | unsupported |
| 25-legacy-journal-key-text | green |  | unsupported |
| 26-external-table-overwrites-pending-edit | green |  | unsupported |
| 27-held-object-delete | green |  |  |
| 27-key-only-stub-delete | green |  |  |
| 27-stub-delete-after-blob-write | green |  |  |
| 28-hook-assigns-key | green |  |  |
| 42-refresh-refused-by-rows-outside | green |  |  |
| ack-failed-download-then-edit-uploads-and-still-detects-echo | green |  |  |
| ack-propagation-drops-ack-so-reinsert-of-same-key-is-clean | green |  |  |
| ack-serverwins-applied-without-download-then-edit-is-not-a-false-modified-conflict | green |  |  |
| ack-serverwins-restored-row-then-edit-is-not-a-false-duplicate | green |  |  |
| ack-truncated-echo-of-offline-insert-then-edit-is-not-a-false-duplicate | green |  |  |
| ack-truncated-echo-then-delete-is-not-a-false-delete-conflict | green |  |  |
| ack-truncated-echo-then-edit-is-not-a-false-modified-conflict | differs | 5/expect-result |  |
| apply-skip-stops-table | green |  | unsupported |
| blob-after-hook-write-matching-no-row-is-not-recorded | green |  |  |
| blob-after-hook-write-on-another-row-is-uploaded | green |  |  |
| blob-after-hook-write-on-update-is-uploaded | green |  |  |
| blob-after-hook-write-rolled-back-with-failed-save | green |  |  |
| blob-after-hook-write-with-other-key-type-uses-row-key | green |  |  |
| blob-default-does-not-download | green |  |  |
| blob-default-does-not-upload | green |  |  |
| blob-default-preserves-existing-local-blobs-new-rows-arrive-empty | green |  |  |
| blob-edit-without-local-blob-keeps-server-blob | green |  |  |
| blob-failed-download-copy-is-retried | green |  |  |
| blob-failed-refresh-copy-is-retried | green |  |  |
| blob-failed-upload-no-false-conflict | green |  |  |
| blob-include-downloads-blobs | green |  |  |
| blob-include-uploads-local-blob | green |  |  |
| blob-inserted-row-blob-is-recorded-and-uploaded | green |  |  |
| blob-only-edit-after-serverwins-discarded-unknown-insert-uploads-row-and-blob | green |  |  |
| blob-only-edit-is-journaled-and-uploaded | green |  |  |
| blob-owed-copy-keeps-unsent-local-blob | green |  |  |
| blob-owed-copy-made-for-ordinary-edit | green |  |  |
| blob-refresh-discard-drops-local-blobs | green |  |  |
| blob-refresh-discard-empties-blob-of-row-written-after-retry | green |  |  |
| blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling | green |  |  |
| blob-refresh-discard-then-sync-does-not-send-empty-blob-back | green |  |  |
| blob-refresh-include-reloads-blobs | green |  |  |
| blob-refresh-refuses-when-blobs-would-be-lost | green |  |  |
| blob-thrown-serverwins-then-collect-overwrites-newer-server-blob | green |  |  |
| blob-uploaded-blob-does-not-echo | green |  |  |
| c2-serverwins-out-of-scope-delete | green |  |  |
| chunk-cross-table-cycle-both-new | known | 1/no-quiesce:conflicts-or-truncations, 5/expect-result, 5/expect-rows:model_folders, 5/expect-rows:model_items |  |
| chunk-delete-stage-parent-gone-after-next-sync | green |  |  |
| chunk-selfref-cycle-across-chunks | known | 1/no-quiesce:conflicts-or-truncations, 5/expect-result, 5/expect-rows:model_folders |  |
| chunk-selfref-parent-in-later-chunk | green |  |  |
| chunk-taker-before-releaser | green |  |  |
| chunk-unique-swap-across-chunks | known | 1/no-quiesce:unique-swap:model_items, 5/expect-result, 5/expect-rows:model_items |  |
| conc-local-edit-during-download-is-not-overwritten-by-batch | differs | 5/expect-result |  |
| conc-local-insert-during-download-is-not-deleted-by-propagation | green |  |  |
| conflict-collected-by-default-with-both-rows | green |  |  |
| conflict-collected-row-keeps-local-content | green |  |  |
| conflict-delete-is-not-resurrected-by-same-run-download | differs | 5/expect-result |  |
| conflict-delete-of-row-updated-on-server | green |  |  |
| conflict-discarded-lets-next-run-catch-up | green |  | unsupported |
| conflict-duplicate-key-on-server-is-reported | green |  |  |
| conflict-duplicate-key-then-local-delete-brings-server-row-down | green |  |  |
| conflict-truncation-continues-following-tables-and-reads-parents | differs | 5/expect-result |  |
| cycle-pin-to-new-item-uploads-in-rounds | green |  |  |
| cycle-pinned-item-is-read-back-before-folder | green |  |  |
| cycle-serverwins-restores-folder-pinned-to-new-item | green |  |  |
| d1-folder-recreate-delete | green |  |  |
| d2-delete-check-fails-lww-serverwins | green |  |  |
| d2-delete-refused-by-referencing-row | green |  |  |
| d2-delete-refused-by-referencing-row-after-fault | green |  |  |
| dl-batch-larger-than-one-in-clause | green |  |  |
| dl-continues-across-batches | green |  |  |
| dl-downloaded-rows-are-not-journaled | green |  |  |
| dl-empty-server-syncs-to-zero | green |  |  |
| dl-incremental-downloads-only-newer-rows | green |  |  |
| dl-initial-downloads-everything | green |  |  |
| dl-interrupted-download-resumes-from-applied-maximum | green |  |  |
| dl-non-positive-batch-size-throws-before-upload-changes-anything | green |  |  |
| dl-propagation-can-be-turned-off | green |  |  |
| dl-propagation-removes-rows-missing-on-server | green |  |  |
| dl-repeated-sync-downloads-nothing | green |  |  |
| dl-row-limit-equal-to-fetched-rows-is-not-refused | green |  |  |
| dl-row-limit-exceeded-after-upload-applies-everything | green |  |  |
| dl-row-limit-exceeded-with-unsent-change-refuses-and-applies-nothing | green |  |  |
| dl-server-update-after-upload-is-still-downloaded | green |  |  |
| dl-table-emptied-outside-sync | green |  |  |
| e2-leftover-unset-key-entry | green |  | unsupported |
| fk-child-of-excluded-parent-table-stops-and-names-parent-table | green |  |  |
| fk-delete-refused-on-server-holds-back-child-collect | green |  |  |
| fk-delete-refused-on-server-holds-back-child-localwins | green |  |  |
| fk-delete-refused-on-server-is-settled-by-serverwins | green |  |  |
| fk-lww-serverwins-label-delete-refused-while-item-reparent-is-held | green |  |  |
| fk-lww-serverwins-settles-delete-refused-on-server | green |  |  |
| fk-lww-serverwins-settles-parent-missing-on-server | green |  |  |
| fk-lww-serverwins-uploads-ordinary-changes | green |  |  |
| fk-parent-created-between-table-fetches-arrives-in-same-sync | green |  |  |
| fk-propagation-keeps-parents-of-rows-it-keeps | green |  |  |
| fk-serverwins-restores-row-moved-to-new-parent-on-server | green |  |  |
| fk-versionless-download-reports-held-back-row-after-scan | differs | 5/expect-result |  |
| fk-versionless-pending-row-keeps-local-content-after-scan | green |  |  |
| fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-lww-collect | green |  |  |
| fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-versioned-localwins | green |  |  |
| graph-cascade-delete-replays-child-first | green |  |  |
| graph-cascade-save-uploads-parent-and-child | green |  |  |
| graph-child-edit-under-unchanged-root-uploads | green |  |  |
| holder-held-for-another-reason-keeps-its-values | green |  |  |
| holder-referenced-by-other-table-collect-keeps-unsent-referrer | green |  |  |
| holder-referenced-by-other-table-current-child-collect-keeps-unsent-referrer | green |  |  |
| holder-referenced-by-other-table-current-child-legacy-local-schema | green |  | unsupported |
| holder-referenced-by-other-table-current-child-localwins-keeps-unsent-referrer | green |  |  |
| holder-referenced-by-other-table-current-child-lww-serverwins-settles-in-one-sync | green |  |  |
| holder-referenced-by-other-table-current-child-owed-row-cleared-by-refresh | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-dropped-when-row-is-still-there | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-gone-on-server | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-key-created-on-device | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-recorded-before-removal | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-survives-failed-run | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-waits-for-parent-with-unsent-delete | green |  | unsupported |
| holder-referenced-by-other-table-current-child-owed-row-waits-for-unsent-entry | green |  | unsupported |
| holder-referenced-by-other-table-current-child-read-back-row-uploads-its-edit | green |  |  |
| holder-referenced-by-other-table-current-child-serverwins-settles-in-one-sync | green |  |  |
| holder-referenced-by-other-table-current-child-settles | green |  |  |
| holder-referenced-by-other-table-edited-stale-copy-is-kept | green |  |  |
| holder-referenced-by-other-table-entries-go-before-rows | green |  | unsupported |
| holder-referenced-by-other-table-items-settles | green |  |  |
| holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync | green |  |  |
| holder-referenced-by-other-table-propagation-off-keeps-referrer | green |  |  |
| holder-referenced-by-other-table-serverwins-settles-in-one-sync | green |  |  |
| holder-referenced-by-other-table-settles | green |  |  |
| holder-referenced-by-other-table-stale-copy-in-excluded-table-is-kept | green |  |  |
| holder-referenced-by-other-table-stale-copy-settles | green |  |  |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-collect | green |  |  |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-localwins | green |  |  |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-lww-serverwins | green |  |  |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-serverwins | green |  |  |
| holder-referenced-by-other-table-through-label-settles | green |  |  |
| hook-download-skip-stops-table-and-retries | green |  | unsupported |
| hook-parent-read-back-skip-stops-child-table | green |  | unsupported |
| hook-serverwins-read-back-skip-keeps-entry | green |  | unsupported |
| hook-skip-changes-nothing | green |  |  |
| hook-versionless-download-skip-stops-table | green |  | unsupported |
| interrupt-AfterServerFetchBeforeApply-delete-blob-written | green |  |  |
| interrupt-AfterServerFetchBeforeApply-delete-blob-written-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-delete-pending | green |  |  |
| interrupt-AfterServerFetchBeforeApply-delete-pending-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-delete-recreated | green |  |  |
| interrupt-AfterServerFetchBeforeApply-delete-recreated-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-recreate-deleted | green |  |  |
| interrupt-AfterServerFetchBeforeApply-recreate-deleted-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-blob-written | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-blob-written-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-pending | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-pending-serverwins | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-recreated | green |  |  |
| interrupt-AfterServerFetchBeforeApply-update-recreated-serverwins | green |  |  |
| interrupt-BeforeServerFetch-delete-blob-written | green |  |  |
| interrupt-BeforeServerFetch-delete-blob-written-serverwins | green |  |  |
| interrupt-BeforeServerFetch-delete-pending | green |  |  |
| interrupt-BeforeServerFetch-delete-pending-serverwins | green |  |  |
| interrupt-BeforeServerFetch-delete-recreated | green |  |  |
| interrupt-BeforeServerFetch-delete-recreated-serverwins | green |  |  |
| interrupt-BeforeServerFetch-recreate-deleted | green |  |  |
| interrupt-BeforeServerFetch-recreate-deleted-serverwins | green |  |  |
| interrupt-BeforeServerFetch-update-blob-written | green |  |  |
| interrupt-BeforeServerFetch-update-blob-written-serverwins | green |  |  |
| interrupt-BeforeServerFetch-update-pending | green |  |  |
| interrupt-BeforeServerFetch-update-pending-serverwins | green |  |  |
| interrupt-BeforeServerFetch-update-recreated | green |  |  |
| interrupt-BeforeServerFetch-update-recreated-serverwins | green |  |  |
| interrupt-BeforeServerWrite-delete-blob-written | green |  |  |
| interrupt-BeforeServerWrite-delete-pending | green |  |  |
| interrupt-BeforeServerWrite-delete-recreated | green |  |  |
| interrupt-BeforeServerWrite-recreate-deleted | green |  |  |
| interrupt-BeforeServerWrite-update-blob-written | green |  |  |
| interrupt-BeforeServerWrite-update-pending | green |  |  |
| interrupt-BeforeServerWrite-update-recreated | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails | green |  | differs |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails-serverwins | green |  | differs |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-fetch-fails | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-fetch-fails-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-refresh | known | 1/throw:RefreshAsync~SyncTableBase.WriteRowsAndBlobsAsync>SqliteRepositoryCore.BulkInsertAsync:SQLite19:FOREIGN KEY constraint failed[apply:cross:in-scope] |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-serverwins-refresh | known | 1/throw:RefreshAsync~SyncTableBase.WriteRowsAndBlobsAsync>SqliteRepositoryCore.BulkInsertAsync:SQLite19:FOREIGN KEY constraint failed[apply:cross:in-scope] |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails | green |  | differs |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails-serverwins | green |  | differs |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-fetch-fails | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-fetch-fails-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-fetch-fails | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-fetch-fails-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-fetch-fails | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-fetch-fails-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-refresh | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-serverwins | green |  |  |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-serverwins-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-refresh | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-serverwins | green |  |  |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-serverwins-refresh | green |  |  |
| judge-carry-blob-decision-voided-by-row-edit-is-not-overwrite | green |  |  |
| judge-carry-blob-overwrite-is-a-violation | green |  |  |
| judge-carry-edit-lost-is-a-violation | green |  |  |
| judge-carry-fulfilled-by-lww-serverwins-is-not-spurious | green |  |  |
| judge-carry-fulfilled-is-not-lost | green |  |  |
| judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation | green |  |  |
| judge-carry-item-row-overwrite-is-a-violation | green |  |  |
| judge-carry-overwrite-is-a-violation | green |  |  |
| judge-carry-row-decision-voided-by-blob-edit-is-not-overwrite | green |  |  |
| judge-foreign-key-existing-violation-is-not-counted | green |  |  |
| judge-foreign-key-new-violation-in-thrown-run-is-counted | green |  |  |
| judge-foreign-key-new-violation-is-counted | green |  |  |
| judge-foreign-key-reinserted-violation-is-not-counted | green |  |  |
| judge-foreign-key-swapped-violation-is-counted | green |  |  |
| judge-local-discard-is-a-violation | green |  |  |
| judge-local-discard-of-delete-on-row-brought-in-without-blob-failure-is-a-violation | green |  |  |
| judge-local-discard-of-edit-on-row-brought-in-by-thrown-run-is-a-violation | green |  |  |
| judge-lww-serverwins-settlement-without-basis-is-spurious | green |  |  |
| judge-out-of-scope-write-is-a-violation | green |  |  |
| judge-resend-after-thrown-serverwins-restore-without-blob-failure-is-a-violation | green |  |  |
| judge-server-knows-facet-is-compared | green |  |  |
| judge-serverwins-wrote-is-a-violation | green |  |  |
| judge-stale-overwrite-is-a-violation | green |  |  |
| judge-thrown-run-restore-is-a-violation | green |  |  |
| judge-undeclared-carry-discard-is-lost | green |  |  |
| judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious | green |  |  |
| judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious | green |  |  |
| judge-unowned-send-after-settled-record-is-a-violation | green |  |  |
| judge-unowned-send-is-a-violation | green |  |  |
| judge-unreported-overwrite-is-a-violation | green |  |  |
| lw-overwrites-duplicate-key-on-server | green |  |  |
| lw-overwrites-server-row | green |  |  |
| lw-resurrects-row-missing-on-server | green |  |  |
| lww-bypasses-version-guard-on-versioned-tables | green |  |  |
| lww-cascade-save-with-versionless-child-uploads | green |  |  |
| lww-downloads-versionless-table-every-run | green |  |  |
| lww-duplicate-key-on-server-overwrites | green |  |  |
| lww-overwrites-and-resurrects-without-conflict | green |  |  |
| lww-run-exclusion-skips-table-for-one-run | green |  |  |
| lww-serverwins-unuploaded-insert-delete-on-taken-key-is-not-a-settlement | green |  |  |
| lww-uploads-versionless-edits | green |  |  |
| lww-versioned-run-leaves-versionless-table-alone | green |  |  |
| n1-download-unique-delete-then-insert | green |  |  |
| n1-download-unique-without-propagation | green |  |  |
| n1-serverwins-truncation-does-not-strand-children | green |  |  |
| n10-hook-skip-record-resurrects-deleted-row | green |  |  |
| n10-hook-skip-record-reverts-row | green |  |  |
| n10-hook-skip-record-spurious-conflict | green |  |  |
| n12-blob-write-then-delete-resurrected | green |  |  |
| n12-blob-writer-conflicted-blob-empty | green |  |  |
| n12-recreated-row-blob-failed-then-discarded | green |  |  |
| n13-holder-referenced-by-taker | green |  |  |
| n14-control-holder-moved-under-existing-parent | green |  |  |
| n14-holder-moved-under-new-parent-in-batch | green |  |  |
| n15-pending-unique-cycle | known | 1/no-quiesce:pending-unique-cycle:model_folders |  |
| n15-pending-unique-cycle-three-items | known | 1/no-quiesce:pending-unique-cycle:model_items |  |
| n2-serverwins-unique-delete-then-insert | green |  |  |
| n2-serverwins-unique-without-propagation | green |  |  |
| n3-download-parent-table-excluded | green |  |  |
| n3-download-parent-versionless | green |  |  |
| n4-serverwins-restore-out-of-scope-parent | green |  |  |
| n5-versionless-delete-refused-after-stub | green |  |  |
| n5-versionless-delete-refused-never-settles | green |  |  |
| n6-delete-parent-recorded-first | green |  |  |
| n6-refused-write-leaves-nothing | green |  |  |
| n6-reparent-lastwritewins-serverwins | green |  |  |
| n6-upload-reparent-to-new-row | green |  |  |
| n7-collect | green |  |  |
| n7-delete-modified-then-insert-same-value | green |  |  |
| n7-delete-refused-then-insert-same-value | green |  |  |
| n7-localwins | green |  |  |
| n7-localwins-resurrect-value-taken | green |  |  |
| n7-lww-collect | green |  |  |
| n7-lww-localwins | green |  |  |
| n7-lww-serverwins | green |  |  |
| n7-serverwins | green |  |  |
| n786-thrown-serverwins-removal-then-record-dropped | differs | 5/expect-row-change:model_items |  |
| n7s-ascii-case-collect | green |  |  |
| n7s-ascii-case-localwins | green |  |  |
| n7s-ascii-case-lww-serverwins | green |  |  |
| n7s-non-ascii-case-collect | green |  |  |
| n7s-non-ascii-case-localwins | green |  |  |
| n7s-non-ascii-case-lww-serverwins | green |  |  |
| n7s-trailing-space-collect | green |  |  |
| n7s-trailing-space-localwins | green |  |  |
| n7s-trailing-space-lww-serverwins | green |  |  |
| n8-chain | green |  |  |
| n8-holder-with-unsent-change-stops | green |  |  |
| n8-serverwins-holder-is-read-back | green |  |  |
| n8-true-swap-stops | green |  | unsupported |
| n8-unique-value-moved-then-updated | green |  |  |
| n9-stale-child-keeps-holder | green |  |  |
| parent-readback-ack-guards-later-edit | green |  |  |
| parent-readback-collect-keeps-delete | green |  |  |
| parent-readback-collect-keeps-grandparent-delete | green |  |  |
| parent-readback-refresh-across-batches | green |  |  |
| parent-readback-selfref-chain | green |  |  |
| policy-collect-delete-edit | green |  |  |
| policy-collect-edit-delete | green |  |  |
| policy-collect-edit-edit | green |  |  |
| policy-localwins-delete-edit | green |  |  |
| policy-localwins-edit-delete | green |  |  |
| policy-localwins-edit-edit | green |  |  |
| policy-lww-delete-edit | green |  |  |
| policy-lww-edit-delete | green |  |  |
| policy-lww-edit-edit | green |  |  |
| policy-serverwins-delete-edit | green |  |  |
| policy-serverwins-edit-delete | green |  |  |
| policy-serverwins-edit-edit | green |  |  |
| prop-keeps-locally-referenced-rows-across-in-clause-chunks | green |  |  |
| prop-resumes-once-journal-entry-is-settled | green |  |  |
| prop-skips-referring-tables-missing-locally | green |  |  |
| prop-spares-local-insert-not-yet-uploaded | green |  |  |
| prop-spares-row-with-pending-journal-entry | green |  |  |
| reach-after-fault-delete-lww-collect | green |  |  |
| reach-after-fault-delete-lww-localwins | green |  |  |
| reach-after-fault-delete-lww-serverwins | green |  |  |
| reach-after-fault-delete-versioned-collect | green |  |  |
| reach-after-fault-delete-versioned-localwins | green |  |  |
| reach-after-fault-update-lww-collect | green |  |  |
| reach-after-fault-update-lww-localwins | green |  |  |
| reach-after-fault-update-lww-serverwins | green |  |  |
| reach-after-fault-update-versioned-collect | green |  |  |
| reach-after-fault-update-versioned-localwins | green |  |  |
| rec-failed-blob-write-leaves-no-journal-entry | green |  |  |
| rec-failed-update-keeps-earlier-entries-of-same-row | green |  |  |
| rec-failed-update-leaves-no-journal-entry | green |  |  |
| rec-journal-cleanup-by-table-and-wholesale | green |  | unsupported |
| refresh-continues-across-batches | green |  |  |
| refresh-cycle-force-replaces-pending-and-next-sync-sends-nothing | green |  |  |
| refresh-does-not-journal-its-own-writes | green |  |  |
| refresh-empties-tables-in-a-foreign-key-cycle | green |  |  |
| refresh-empty-server-empties-local | green |  |  |
| refresh-excluding-referencing-child-refuses-under-cascade | green |  |  |
| refresh-excluding-referring-child-refuses-and-next-sync-sends-edit | green |  |  |
| refresh-failed-wipe-in-a-foreign-key-cycle-deletes-nothing | green |  | differs |
| refresh-failed-wipe-keeps-acks | green |  | differs |
| refresh-force-discards-pending-and-next-sync-sends-nothing | green |  |  |
| refresh-force-discards-pending-changes | green |  |  |
| refresh-force-forgets-row-gone-on-both-sides-and-same-key-is-inserted | green |  |  |
| refresh-force-in-a-foreign-key-cycle-replaces-pending-changes | green |  |  |
| refresh-force-over-row-limit-discards-unsent-and-is-not-refused | green |  |  |
| refresh-honors-foreign-key-order | differs | 5/expect-result |  |
| refresh-leaves-anchor-next-sync-resumes-from | green |  |  |
| refresh-leaves-local-only-tables-alone | green |  |  |
| refresh-non-positive-batch-size-throws-before-deleting | green |  |  |
| refresh-pending-refused-keeps-edit-that-next-sync-sends | green |  |  |
| refresh-rebuilds-local-from-server | differs | 5/expect-result |  |
| refresh-refusal-order-blob-loss-before-outside-reference | green |  |  |
| refresh-refusal-order-outside-reference-before-pending | green |  |  |
| refresh-refuses-when-local-changes-are-pending | green |  |  |
| refresh-refuses-when-rows-outside-refer-inside | green |  |  |
| refresh-scopes-versionless-tables-by-mode | green |  |  |
| refresh-skips-referring-tables-missing-locally | green |  |  |
| refresh-written-rows-are-not-sent-by-next-sync | green |  |  |
| replay-delete-then-refused-reinsert-deletes-on-server | green |  |  |
| replay-edit-then-refused-delete-still-uploads-the-edit | green |  |  |
| replay-offline-insert-then-delete-is-discarded | green |  |  |
| replay-recreated-pending-delete-is-sent-as-update-external-table | green |  | unsupported |
| replay-unlanded-delete-entry-is-discarded | green |  | unsupported |
| replay-unlanded-delete-entry-is-discarded-external-table | green |  | unsupported |
| replay-versionless-delete-then-refused-reinsert-deletes-on-server | green |  |  |
| repro-b3 | green |  |  |
| repro-b3-grandchild | green |  |  |
| repro-b3-grandchild-under-present-child | green |  |  |
| repro-d2-lww-serverwins | green |  |  |
| repro-d2-upsert-unique-lww-localwins | green |  |  |
| repro-d2-upsert-unique-lww-serverwins | green |  |  |
| repro-d2-upsert-unique-lww-serverwins-held-holder-is-basis | green |  |  |
| repro-d2-versioned-collect | green |  |  |
| repro-d3-folder-name-lww-serverwins | green |  |  |
| repro-d3-folder-name-versioned-collect | green |  |  |
| repro-d3-lww-serverwins | green |  |  |
| repro-d3-versioned-collect | green |  |  |
| repro-d4-lww-serverwins | green |  |  |
| repro-d4-versioned-collect | green |  |  |
| retry-blob-download-copy | green |  |  |
| retry-blob-upload-copy | green |  |  |
| retry-server-fetch | green |  |  |
| retry-server-write | green |  |  |
| rotate-three-codes-without-refresh-does-not-settle | known | 1/no-quiesce:unique-swap:model_items | differs |
| rs6-offline-delete-reinsert | green |  |  |
| rs6-offline-insert-delete-insert-stays-insert | green |  |  |
| rs6-reinsert-after-server-update-conflicts | green |  |  |
| serverwins-holder-chain-moves-under-taker | green |  |  |
| serverwins-holder-moves-under-taker | green |  |  |
| sw-discards-delete-left-by-failed-run-after-recreate-elsewhere | green |  |  |
| sw-discards-insert-then-delete-on-key-taken-on-server | green |  |  |
| sw-discards-local-change | green |  |  |
| sw-does-not-stop-download-at-rows-it-drops | green |  |  |
| sw-drops-only-scoped-entries | green |  |  |
| sw-duplicate-key-on-server-reloads-server-row | green |  |  |
| sw-duplicate-key-on-server-upload-only-reloads-server-row | green |  |  |
| sw-leaves-row-edited-during-the-run | green |  | unsupported |
| sw-leaves-row-edited-during-the-run-then-uploads-it | green |  |  |
| sw-removes-local-insert-server-never-received | green |  |  |
| sw-removes-row-deleted-on-server | green |  |  |
| sw-restore-does-not-advance-anchor | green |  |  |
| sw-restores-row-deleted-locally | green |  |  |
| sw-restores-row-the-server-has-not-changed | green |  |  |
| swap-folder-names-without-refresh-does-not-settle | known | 1/no-quiesce:unique-swap:model_folders | differs |
| swap-without-refresh-does-not-settle | known | 1/no-quiesce:unique-swap:model_items | differs |
| three-devices-change-reaches-the-other-two-in-different-orders | green |  |  |
| thrown-resolver-row-delete-is-dropped-and-row-comes-back | green |  | unsupported |
| thrown-serverwins-run-hides-settled-insert | green |  |  |
| thrown-serverwins-run-hides-settlement | green |  |  |
| thrown-serverwins-sync-leaves-restored-row-out-of-local-row-changes | green |  |  |
| thrown-serverwins-upload-then-lww-sends-restored-row-as-edit | green |  |  |
| truncated-parent-held-stops-child-table | green |  |  |
| truncated-parent-read-back-across-tables | green |  |  |
| truncated-parent-read-back-selfref-chain | green |  |  |
| ul-local-delete-is-uploaded | green |  |  |
| ul-local-insert-is-uploaded-and-mirrors-server-version | green |  |  |
| ul-local-update-is-uploaded | green |  |  |
| ul-pending-edit-does-not-hide-other-rows-server-change | green |  |  |
| ul-row-edited-before-settle-keeps-its-version-until-sent | green |  |  |
| ul-settles-more-journal-entries-than-one-in-clause | green |  |  |
| ul-stale-journal-entry-is-discarded | green |  | unsupported |
