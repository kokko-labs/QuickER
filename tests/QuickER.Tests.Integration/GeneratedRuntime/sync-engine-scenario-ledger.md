<!-- 手で直さない生成物（SyncSupportAdoSqliteEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord が、新しいエンジンの端末で決定的なシナリオを流した結果から作る）。書き直し：$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj --filter "FullyQualifiedName~ScenarioLedgerDrift"; $env:QUICKER_REGEN_FIXTURES=$null -->

# 新しいエンジンの端末の決定的なシナリオの控え

green: 373 / unsupported: 99 / differs: 20

| シナリオ | 結果 | 詳細 | 今のエンジン |
|---|---|---|---|
| 12-selfref-propagation | green |  | green |
| 12-selfref-propagation-completes-in-one-run | green |  | green |
| 13-propagation-pending-label-parent | green |  | green |
| 13-propagation-pending-row-parent | green |  | green |
| 13-propagation-truncated-child | green |  | green |
| 14-owed-blob-source-gone | green |  | green |
| 15-propagation-excluded-child | green |  | green |
| 15-propagation-versionless-child | green |  | green |
| 16-failed-blob-write-record-reverts-blob | green |  | green |
| 16-failed-blob-write-record-spurious-conflict | green |  | green |
| 16-failed-update-record-reverts-row | green |  | green |
| 16-failed-write-record-overwrites-blob | green |  | green |
| 17-serverwins-deleted-parent | green |  | green |
| 18-serverwins-restore-subtree | green |  | green |
| 19-varchar-key-paging-skips-rows | green |  | green |
| 20-download-selfref-order | green |  | green |
| 20-refresh-selfref-order | green |  | green |
| 21-versionless-held-row-referenced | green |  | green |
| 22-forced-overwrite-hides-server-blob | green |  | green |
| 23-insert-after-discarded-blob-record | green |  | green |
| 23-inserted-row-blob-retry | green |  | green |
| 23-localwins-resurrect-without-blob | green |  | green |
| 24-deferred-upsert-carries-children | green |  | green |
| 24-unique-delete-then-insert | green |  | green |
| 25-datetime-key-local | green |  | green |
| 25-datetime-key-unspecified | green |  | green |
| 25-datetime-key-utc | green |  | green |
| 25-legacy-ack-key-text | unsupported | LegacyKeyTextsOp | green |
| 25-legacy-journal-key-text | unsupported | LegacyKeyTextsOp | green |
| 26-external-table-overwrites-pending-edit | unsupported | ExternalTables | green |
| 27-held-object-delete | green |  | green |
| 27-key-only-stub-delete | green |  | green |
| 27-stub-delete-after-blob-write | green |  | green |
| 28-hook-assigns-key | green |  | green |
| 42-refresh-refused-by-rows-outside | green |  | green |
| ack-failed-download-then-edit-uploads-and-still-detects-echo | green |  | green |
| ack-propagation-drops-ack-so-reinsert-of-same-key-is-clean | green |  | green |
| ack-serverwins-applied-without-download-then-edit-is-not-a-false-modified-conflict | green |  | green |
| ack-serverwins-restored-row-then-edit-is-not-a-false-duplicate | green |  | green |
| ack-truncated-echo-of-offline-insert-then-edit-is-not-a-false-duplicate | green |  | green |
| ack-truncated-echo-then-delete-is-not-a-false-delete-conflict | green |  | green |
| ack-truncated-echo-then-edit-is-not-a-false-modified-conflict | differs | 5/expect-result | green |
| apply-skip-stops-table | unsupported | ApplySkip | green |
| blob-after-hook-write-matching-no-row-is-not-recorded | unsupported | ExpectFacetOp:journal | green |
| blob-after-hook-write-on-another-row-is-uploaded | unsupported | ExpectFacetOp:journal | green |
| blob-after-hook-write-on-update-is-uploaded | green |  | green |
| blob-after-hook-write-rolled-back-with-failed-save | unsupported | ExpectFacetOp:journal | green |
| blob-after-hook-write-with-other-key-type-uses-row-key | unsupported | ExpectFacetOp:journal | green |
| blob-default-does-not-download | green |  | green |
| blob-default-does-not-upload | green |  | green |
| blob-default-preserves-existing-local-blobs-new-rows-arrive-empty | green |  | green |
| blob-edit-without-local-blob-keeps-server-blob | green |  | green |
| blob-failed-download-copy-is-retried | unsupported | ExpectFacetOp:blob-pending | green |
| blob-failed-refresh-copy-is-retried | green |  | green |
| blob-failed-upload-no-false-conflict | green |  | green |
| blob-include-downloads-blobs | green |  | green |
| blob-include-uploads-local-blob | green |  | green |
| blob-inserted-row-blob-is-recorded-and-uploaded | unsupported | ExpectFacetOp:journal | green |
| blob-only-edit-after-serverwins-discarded-unknown-insert-uploads-row-and-blob | green |  | green |
| blob-only-edit-is-journaled-and-uploaded | unsupported | ExpectFacetOp:journal | green |
| blob-owed-copy-keeps-unsent-local-blob | green |  | green |
| blob-owed-copy-made-for-ordinary-edit | green |  | green |
| blob-refresh-discard-drops-local-blobs | green |  | green |
| blob-refresh-discard-empties-blob-of-row-written-after-retry | green |  | green |
| blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling | green |  | green |
| blob-refresh-discard-then-sync-does-not-send-empty-blob-back | green |  | green |
| blob-refresh-include-reloads-blobs | green |  | green |
| blob-refresh-refuses-when-blobs-would-be-lost | green |  | green |
| blob-thrown-serverwins-then-collect-overwrites-newer-server-blob | green |  | green |
| blob-uploaded-blob-does-not-echo | green |  | green |
| c2-serverwins-out-of-scope-delete | green |  | green |
| conc-local-edit-during-download-is-not-overwritten-by-batch | differs | 5/expect-result | green |
| conc-local-insert-during-download-is-not-deleted-by-propagation | green |  | green |
| conflict-collected-by-default-with-both-rows | unsupported | ExpectFacetOp:journal | green |
| conflict-collected-row-keeps-local-content | unsupported | ExpectFacetOp:journal | green |
| conflict-delete-is-not-resurrected-by-same-run-download | differs | 5/expect-result | green |
| conflict-delete-of-row-updated-on-server | green |  | green |
| conflict-discarded-lets-next-run-catch-up | unsupported | ClearJournalOp | green |
| conflict-duplicate-key-on-server-is-reported | green |  | green |
| conflict-duplicate-key-then-local-delete-brings-server-row-down | green |  | green |
| conflict-truncation-continues-following-tables-and-reads-parents | differs | 5/expect-result | green |
| cycle-pin-to-new-item-uploads-in-rounds | unsupported | ExpectFacetOp:journal | green |
| cycle-pinned-item-is-read-back-before-folder | green |  | green |
| cycle-serverwins-restores-folder-pinned-to-new-item | unsupported | ExpectFacetOp:journal | green |
| d1-folder-recreate-delete | green |  | green |
| d2-delete-check-fails-lww-serverwins | green |  | green |
| d2-delete-refused-by-referencing-row | green |  | green |
| d2-delete-refused-by-referencing-row-after-fault | green |  | green |
| dl-batch-larger-than-one-in-clause | green |  | green |
| dl-continues-across-batches | green |  | green |
| dl-downloaded-rows-are-not-journaled | unsupported | ExpectFacetOp:journal | green |
| dl-empty-server-syncs-to-zero | green |  | green |
| dl-incremental-downloads-only-newer-rows | green |  | green |
| dl-initial-downloads-everything | unsupported | ExpectFacetOp:journal | green |
| dl-interrupted-download-resumes-from-applied-maximum | green |  | green |
| dl-non-positive-batch-size-throws-before-upload-changes-anything | unsupported | ExpectFacetOp:journal | green |
| dl-propagation-can-be-turned-off | green |  | green |
| dl-propagation-removes-rows-missing-on-server | green |  | green |
| dl-repeated-sync-downloads-nothing | green |  | green |
| dl-server-update-after-upload-is-still-downloaded | green |  | green |
| dl-table-emptied-outside-sync | green |  | green |
| e2-leftover-unset-key-entry | unsupported | LeftoverUnsetKeyEntryOp | green |
| fk-child-of-excluded-parent-table-stops-and-names-parent-table | green |  | green |
| fk-delete-refused-on-server-holds-back-child-collect | unsupported | ExpectFacetOp:journal | green |
| fk-delete-refused-on-server-holds-back-child-localwins | unsupported | ExpectFacetOp:journal | green |
| fk-delete-refused-on-server-is-settled-by-serverwins | unsupported | ExpectFacetOp:journal | green |
| fk-lww-serverwins-label-delete-refused-while-item-reparent-is-held | green |  | green |
| fk-lww-serverwins-settles-delete-refused-on-server | unsupported | ExpectFacetOp:journal | green |
| fk-lww-serverwins-settles-parent-missing-on-server | unsupported | ExpectFacetOp:journal | green |
| fk-lww-serverwins-uploads-ordinary-changes | green |  | green |
| fk-parent-created-between-table-fetches-arrives-in-same-sync | green |  | green |
| fk-propagation-keeps-parents-of-rows-it-keeps | green |  | green |
| fk-serverwins-restores-row-moved-to-new-parent-on-server | unsupported | ExpectFacetOp:journal | green |
| fk-versionless-download-reports-held-back-row-after-scan | differs | 5/expect-result | green |
| fk-versionless-pending-row-keeps-local-content-after-scan | green |  | green |
| fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-lww-collect | unsupported | ExpectFacetOp:journal | green |
| fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-versioned-localwins | unsupported | ExpectFacetOp:journal | green |
| graph-cascade-delete-replays-child-first | unsupported | ExpectFacetOp:journal | green |
| graph-cascade-save-uploads-parent-and-child | unsupported | ExpectFacetOp:journal | green |
| graph-child-edit-under-unchanged-root-uploads | unsupported | ExpectFacetOp:journal | green |
| holder-held-for-another-reason-keeps-its-values | green |  | green |
| holder-referenced-by-other-table-collect-keeps-unsent-referrer | green |  | green |
| holder-referenced-by-other-table-current-child-collect-keeps-unsent-referrer | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-legacy-local-schema | unsupported | LocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-localwins-keeps-unsent-referrer | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-lww-serverwins-settles-in-one-sync | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-cleared-by-refresh | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-dropped-when-row-is-still-there | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-gone-on-server | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-key-created-on-device | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-recorded-before-removal | unsupported | LocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-survives-failed-run | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-waits-for-parent-with-unsent-delete | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-owed-row-waits-for-unsent-entry | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-read-back-row-uploads-its-edit | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-serverwins-settles-in-one-sync | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-current-child-settles | green |  | green |
| holder-referenced-by-other-table-edited-stale-copy-is-kept | green |  | green |
| holder-referenced-by-other-table-entries-go-before-rows | unsupported | RefuseJournalRemovalOp | green |
| holder-referenced-by-other-table-items-settles | green |  | green |
| holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync | differs | 5/expect-row-change:model_items | green |
| holder-referenced-by-other-table-propagation-off-keeps-referrer | green |  | green |
| holder-referenced-by-other-table-serverwins-settles-in-one-sync | differs | 5/expect-row-change:model_items | green |
| holder-referenced-by-other-table-settles | green |  | green |
| holder-referenced-by-other-table-stale-copy-in-excluded-table-is-kept | green |  | green |
| holder-referenced-by-other-table-stale-copy-settles | differs | 5/expect-row-change:model_items | green |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-collect | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-localwins | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-lww-serverwins | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-stale-copy-with-current-child-settles-serverwins | unsupported | ExpectLocalSqlOp:quicker_sync_row_pending | green |
| holder-referenced-by-other-table-through-label-settles | green |  | green |
| hook-download-skip-stops-table-and-retries | unsupported | ApplySkip | green |
| hook-parent-read-back-skip-stops-child-table | unsupported | ApplySkip | green |
| hook-serverwins-read-back-skip-keeps-entry | unsupported | ApplySkip | green |
| hook-skip-changes-nothing | green |  | green |
| hook-versionless-download-skip-stops-table | unsupported | ApplySkip | green |
| interrupt-AfterServerFetchBeforeApply-delete-blob-written | green |  | green |
| interrupt-AfterServerFetchBeforeApply-delete-blob-written-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-delete-pending | green |  | green |
| interrupt-AfterServerFetchBeforeApply-delete-pending-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-delete-recreated | green |  | green |
| interrupt-AfterServerFetchBeforeApply-delete-recreated-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-recreate-deleted | green |  | green |
| interrupt-AfterServerFetchBeforeApply-recreate-deleted-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-blob-written | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-blob-written-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-pending | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-pending-serverwins | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-recreated | green |  | green |
| interrupt-AfterServerFetchBeforeApply-update-recreated-serverwins | green |  | green |
| interrupt-BeforeServerFetch-delete-blob-written | green |  | green |
| interrupt-BeforeServerFetch-delete-blob-written-serverwins | green |  | green |
| interrupt-BeforeServerFetch-delete-pending | green |  | green |
| interrupt-BeforeServerFetch-delete-pending-serverwins | green |  | green |
| interrupt-BeforeServerFetch-delete-recreated | green |  | green |
| interrupt-BeforeServerFetch-delete-recreated-serverwins | green |  | green |
| interrupt-BeforeServerFetch-recreate-deleted | green |  | green |
| interrupt-BeforeServerFetch-recreate-deleted-serverwins | green |  | green |
| interrupt-BeforeServerFetch-update-blob-written | green |  | green |
| interrupt-BeforeServerFetch-update-blob-written-serverwins | green |  | green |
| interrupt-BeforeServerFetch-update-pending | green |  | green |
| interrupt-BeforeServerFetch-update-pending-serverwins | green |  | green |
| interrupt-BeforeServerFetch-update-recreated | green |  | green |
| interrupt-BeforeServerFetch-update-recreated-serverwins | green |  | green |
| interrupt-BeforeServerWrite-delete-blob-written | green |  | green |
| interrupt-BeforeServerWrite-delete-pending | green |  | green |
| interrupt-BeforeServerWrite-delete-recreated | green |  | green |
| interrupt-BeforeServerWrite-recreate-deleted | green |  | green |
| interrupt-BeforeServerWrite-update-blob-written | green |  | green |
| interrupt-BeforeServerWrite-update-pending | green |  | green |
| interrupt-BeforeServerWrite-update-recreated | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-delete-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails | differs | 5/expect-result, op/failed:UpdateFolderOp:InvalidOperationException:No row to change: D# UpdateFolder id=# parent=null name=p-local via=Direct | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails-serverwins | differs | 5/expect-result, op/failed:UpdateFolderOp:InvalidOperationException:No row to change: D# UpdateFolder id=# parent=null name=p-local via=Direct | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-fetch-fails | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-fetch-fails-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-refresh | green |  | known |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-serverwins-refresh | green |  | known |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails | differs | 5/expect-result, op/failed:UpdateFolderOp:InvalidOperationException:No row to change: D# UpdateFolder id=# parent=null name=p-local via=Direct | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails-serverwins | differs | 5/expect-result, op/failed:UpdateFolderOp:InvalidOperationException:No row to change: D# UpdateFolder id=# parent=null name=p-local via=Direct | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-fetch-fails | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-fetch-fails-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-delete-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-fetch-fails | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-fetch-fails-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-fetch-fails | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-fetch-fails-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-refresh | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-serverwins | green |  | green |
| interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-delete-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-cross-table-update-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-delete-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-parent-after-child-selfref-update-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-delete-next-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-edit-sync-serverwins-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-refresh | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-serverwins | green |  | green |
| interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-next-sync-serverwins-refresh | green |  | green |
| judge-carry-blob-decision-voided-by-row-edit-is-not-overwrite | green |  | green |
| judge-carry-blob-overwrite-is-a-violation | green |  | green |
| judge-carry-edit-lost-is-a-violation | green |  | green |
| judge-carry-fulfilled-by-lww-serverwins-is-not-spurious | green |  | green |
| judge-carry-fulfilled-is-not-lost | green |  | green |
| judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation | green |  | green |
| judge-carry-item-row-overwrite-is-a-violation | green |  | green |
| judge-carry-overwrite-is-a-violation | green |  | green |
| judge-carry-row-decision-voided-by-blob-edit-is-not-overwrite | green |  | green |
| judge-foreign-key-existing-violation-is-not-counted | green |  | green |
| judge-foreign-key-new-violation-in-thrown-run-is-counted | green |  | green |
| judge-foreign-key-new-violation-is-counted | green |  | green |
| judge-foreign-key-reinserted-violation-is-not-counted | green |  | green |
| judge-foreign-key-swapped-violation-is-counted | green |  | green |
| judge-local-discard-is-a-violation | green |  | green |
| judge-local-discard-of-delete-on-row-brought-in-without-blob-failure-is-a-violation | green |  | green |
| judge-local-discard-of-edit-on-row-brought-in-by-thrown-run-is-a-violation | green |  | green |
| judge-lww-serverwins-settlement-without-basis-is-spurious | green |  | green |
| judge-out-of-scope-write-is-a-violation | green |  | green |
| judge-resend-after-thrown-serverwins-restore-without-blob-failure-is-a-violation | green |  | green |
| judge-server-knows-facet-is-compared | green |  | green |
| judge-serverwins-wrote-is-a-violation | green |  | green |
| judge-stale-overwrite-is-a-violation | green |  | green |
| judge-thrown-run-restore-is-a-violation | green |  | green |
| judge-undeclared-carry-discard-is-lost | green |  | green |
| judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious | green |  | green |
| judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious | green |  | green |
| judge-unowned-send-after-settled-record-is-a-violation | green |  | green |
| judge-unowned-send-is-a-violation | green |  | green |
| judge-unreported-overwrite-is-a-violation | green |  | green |
| lw-overwrites-duplicate-key-on-server | unsupported | ExpectFacetOp:journal | green |
| lw-overwrites-server-row | green |  | green |
| lw-resurrects-row-missing-on-server | unsupported | ExpectFacetOp:journal | green |
| lww-bypasses-version-guard-on-versioned-tables | green |  | green |
| lww-cascade-save-with-versionless-child-uploads | green |  | green |
| lww-downloads-versionless-table-every-run | green |  | green |
| lww-duplicate-key-on-server-overwrites | unsupported | ExpectFacetOp:journal | green |
| lww-overwrites-and-resurrects-without-conflict | green |  | green |
| lww-run-exclusion-skips-table-for-one-run | unsupported | ExpectFacetOp:journal | green |
| lww-serverwins-unuploaded-insert-delete-on-taken-key-is-not-a-settlement | unsupported | ExpectFacetOp:journal | green |
| lww-uploads-versionless-edits | unsupported | ExpectFacetOp:journal | green |
| lww-versioned-run-leaves-versionless-table-alone | unsupported | ExpectFacetOp:journal | green |
| n1-download-unique-delete-then-insert | green |  | green |
| n1-download-unique-without-propagation | green |  | green |
| n1-serverwins-truncation-does-not-strand-children | green |  | green |
| n10-hook-skip-record-resurrects-deleted-row | green |  | green |
| n10-hook-skip-record-reverts-row | green |  | green |
| n10-hook-skip-record-spurious-conflict | green |  | green |
| n12-blob-write-then-delete-resurrected | green |  | green |
| n12-blob-writer-conflicted-blob-empty | green |  | green |
| n12-recreated-row-blob-failed-then-discarded | green |  | green |
| n13-holder-referenced-by-taker | green |  | green |
| n14-control-holder-moved-under-existing-parent | green |  | green |
| n14-holder-moved-under-new-parent-in-batch | green |  | green |
| n15-pending-unique-cycle | green |  | known |
| n15-pending-unique-cycle-three-items | green |  | known |
| n2-serverwins-unique-delete-then-insert | differs | 5/expect-row-change:model_items | green |
| n2-serverwins-unique-without-propagation | green |  | green |
| n3-download-parent-table-excluded | green |  | green |
| n3-download-parent-versionless | green |  | green |
| n4-serverwins-restore-out-of-scope-parent | green |  | green |
| n5-versionless-delete-refused-after-stub | green |  | green |
| n5-versionless-delete-refused-never-settles | green |  | green |
| n6-delete-parent-recorded-first | green |  | green |
| n6-refused-write-leaves-nothing | unsupported | ExpectNoAckOp | green |
| n6-reparent-lastwritewins-serverwins | green |  | green |
| n6-upload-reparent-to-new-row | green |  | green |
| n7-collect | green |  | green |
| n7-delete-modified-then-insert-same-value | green |  | green |
| n7-delete-refused-then-insert-same-value | green |  | green |
| n7-localwins | green |  | green |
| n7-localwins-resurrect-value-taken | green |  | green |
| n7-lww-collect | green |  | green |
| n7-lww-localwins | green |  | green |
| n7-lww-serverwins | green |  | green |
| n7-serverwins | green |  | green |
| n786-thrown-serverwins-removal-then-record-dropped | differs | 5/expect-row-change:model_items | green |
| n7s-ascii-case-collect | green |  | green |
| n7s-ascii-case-localwins | green |  | green |
| n7s-ascii-case-lww-serverwins | green |  | green |
| n7s-non-ascii-case-collect | green |  | green |
| n7s-non-ascii-case-localwins | green |  | green |
| n7s-non-ascii-case-lww-serverwins | green |  | green |
| n7s-trailing-space-collect | green |  | green |
| n7s-trailing-space-localwins | green |  | green |
| n7s-trailing-space-lww-serverwins | green |  | green |
| n8-chain | green |  | green |
| n8-holder-with-unsent-change-stops | green |  | green |
| n8-serverwins-holder-is-read-back | green |  | green |
| n8-true-swap-stops | unsupported | ExpectFacetOp:blob-pending | green |
| n8-unique-value-moved-then-updated | differs | 5/expect-row-change:model_items | green |
| n9-stale-child-keeps-holder | green |  | green |
| parent-readback-ack-guards-later-edit | green |  | green |
| parent-readback-collect-keeps-delete | green |  | green |
| parent-readback-collect-keeps-grandparent-delete | green |  | green |
| parent-readback-refresh-across-batches | green |  | green |
| parent-readback-selfref-chain | green |  | green |
| policy-collect-delete-edit | green |  | green |
| policy-collect-edit-delete | green |  | green |
| policy-collect-edit-edit | green |  | green |
| policy-localwins-delete-edit | green |  | green |
| policy-localwins-edit-delete | green |  | green |
| policy-localwins-edit-edit | green |  | green |
| policy-lww-delete-edit | green |  | green |
| policy-lww-edit-delete | green |  | green |
| policy-lww-edit-edit | green |  | green |
| policy-serverwins-delete-edit | green |  | green |
| policy-serverwins-edit-delete | green |  | green |
| policy-serverwins-edit-edit | green |  | green |
| prop-keeps-locally-referenced-rows-across-in-clause-chunks | green |  | green |
| prop-resumes-once-journal-entry-is-settled | unsupported | ExpectFacetOp:journal | green |
| prop-skips-referring-tables-missing-locally | green |  | green |
| prop-spares-local-insert-not-yet-uploaded | green |  | green |
| prop-spares-row-with-pending-journal-entry | unsupported | ExpectFacetOp:journal | green |
| reach-after-fault-delete-lww-collect | green |  | green |
| reach-after-fault-delete-lww-localwins | green |  | green |
| reach-after-fault-delete-lww-serverwins | green |  | green |
| reach-after-fault-delete-versioned-collect | green |  | green |
| reach-after-fault-delete-versioned-localwins | green |  | green |
| reach-after-fault-update-lww-collect | green |  | green |
| reach-after-fault-update-lww-localwins | green |  | green |
| reach-after-fault-update-lww-serverwins | green |  | green |
| reach-after-fault-update-versioned-collect | green |  | green |
| reach-after-fault-update-versioned-localwins | green |  | green |
| rec-failed-blob-write-leaves-no-journal-entry | unsupported | ExpectFacetOp:journal | green |
| rec-failed-update-keeps-earlier-entries-of-same-row | unsupported | ExpectFacetOp:journal | green |
| rec-failed-update-leaves-no-journal-entry | unsupported | ExpectFacetOp:journal | green |
| rec-journal-cleanup-by-table-and-wholesale | unsupported | ExpectFacetOp:journal | green |
| refresh-continues-across-batches | green |  | green |
| refresh-cycle-force-replaces-pending-and-next-sync-sends-nothing | green |  | green |
| refresh-does-not-journal-its-own-writes | unsupported | ExpectFacetOp:journal | green |
| refresh-empties-tables-in-a-foreign-key-cycle | green |  | green |
| refresh-empty-server-empties-local | green |  | green |
| refresh-excluding-referencing-child-refuses-under-cascade | unsupported | ExpectFacetOp:journal | green |
| refresh-excluding-referring-child-refuses-and-next-sync-sends-edit | green |  | green |
| refresh-failed-wipe-in-a-foreign-key-cycle-deletes-nothing | unsupported | ExpectFacetOp:acks | green |
| refresh-failed-wipe-keeps-acks | unsupported | ExpectFacetOp:acks | green |
| refresh-force-discards-pending-and-next-sync-sends-nothing | green |  | green |
| refresh-force-discards-pending-changes | unsupported | ExpectFacetOp:journal | green |
| refresh-force-forgets-row-gone-on-both-sides-and-same-key-is-inserted | green |  | green |
| refresh-force-in-a-foreign-key-cycle-replaces-pending-changes | unsupported | ExpectFacetOp:journal | green |
| refresh-honors-foreign-key-order | differs | 5/expect-result | green |
| refresh-leaves-anchor-next-sync-resumes-from | green |  | green |
| refresh-leaves-local-only-tables-alone | green |  | green |
| refresh-non-positive-batch-size-throws-before-deleting | green |  | green |
| refresh-pending-refused-keeps-edit-that-next-sync-sends | green |  | green |
| refresh-rebuilds-local-from-server | differs | 5/expect-result | green |
| refresh-refusal-order-blob-loss-before-outside-reference | green |  | green |
| refresh-refusal-order-outside-reference-before-pending | green |  | green |
| refresh-refuses-when-local-changes-are-pending | unsupported | ExpectFacetOp:journal | green |
| refresh-refuses-when-rows-outside-refer-inside | unsupported | ExpectFacetOp:acks | green |
| refresh-scopes-versionless-tables-by-mode | green |  | green |
| refresh-skips-referring-tables-missing-locally | green |  | green |
| refresh-written-rows-are-not-sent-by-next-sync | green |  | green |
| replay-delete-then-refused-reinsert-deletes-on-server | unsupported | ExpectFacetOp:journal | green |
| replay-edit-then-refused-delete-still-uploads-the-edit | unsupported | ExpectFacetOp:journal | green |
| replay-offline-insert-then-delete-is-discarded | unsupported | ExpectFacetOp:journal | green |
| replay-recreated-pending-delete-is-sent-as-update-external-table | unsupported | ExternalTables | green |
| replay-unlanded-delete-entry-is-discarded | unsupported | ExpectFacetOp:journal | green |
| replay-unlanded-delete-entry-is-discarded-external-table | unsupported | ExternalTables | green |
| replay-versionless-delete-then-refused-reinsert-deletes-on-server | unsupported | ExpectFacetOp:journal | green |
| repro-b3 | green |  | green |
| repro-b3-grandchild | green |  | green |
| repro-b3-grandchild-under-present-child | green |  | green |
| repro-d2-lww-serverwins | green |  | green |
| repro-d2-upsert-unique-lww-localwins | green |  | green |
| repro-d2-upsert-unique-lww-serverwins | green |  | green |
| repro-d2-upsert-unique-lww-serverwins-held-holder-is-basis | unsupported | ExpectFacetOp:journal | green |
| repro-d2-versioned-collect | green |  | green |
| repro-d3-folder-name-lww-serverwins | green |  | green |
| repro-d3-folder-name-versioned-collect | green |  | green |
| repro-d3-lww-serverwins | green |  | green |
| repro-d3-versioned-collect | green |  | green |
| repro-d4-lww-serverwins | green |  | green |
| repro-d4-versioned-collect | green |  | green |
| retry-blob-download-copy | green |  | green |
| retry-blob-upload-copy | green |  | green |
| retry-server-fetch | green |  | green |
| retry-server-write | green |  | green |
| rotate-three-codes-without-refresh-does-not-settle | differs | op/failed:UpdateItemOp:SQLite19:UNIQUE constraint failed: model_items.code | known |
| rs6-offline-delete-reinsert | green |  | green |
| rs6-offline-insert-delete-insert-stays-insert | green |  | green |
| rs6-reinsert-after-server-update-conflicts | green |  | green |
| serverwins-holder-chain-moves-under-taker | green |  | green |
| serverwins-holder-moves-under-taker | green |  | green |
| sw-discards-delete-left-by-failed-run-after-recreate-elsewhere | green |  | green |
| sw-discards-insert-then-delete-on-key-taken-on-server | green |  | green |
| sw-discards-local-change | unsupported | ExpectFacetOp:journal | green |
| sw-does-not-stop-download-at-rows-it-drops | unsupported | ExpectFacetOp:journal | green |
| sw-drops-only-scoped-entries | unsupported | ExpectFacetOp:journal | green |
| sw-duplicate-key-on-server-reloads-server-row | unsupported | ExpectFacetOp:journal | green |
| sw-duplicate-key-on-server-upload-only-reloads-server-row | unsupported | ExpectFacetOp:journal | green |
| sw-leaves-row-edited-during-the-run | unsupported | ExpectFacetOp:journal | green |
| sw-leaves-row-edited-during-the-run-then-uploads-it | green |  | green |
| sw-removes-local-insert-server-never-received | unsupported | ExpectFacetOp:journal | green |
| sw-removes-row-deleted-on-server | unsupported | ExpectFacetOp:journal | green |
| sw-restore-does-not-advance-anchor | green |  | green |
| sw-restores-row-deleted-locally | unsupported | ExpectFacetOp:journal | green |
| sw-restores-row-the-server-has-not-changed | unsupported | ExpectFacetOp:journal | green |
| swap-folder-names-without-refresh-does-not-settle | differs | op/failed:UpdateFolderOp:SQLite19:UNIQUE constraint failed: model_folders.name | known |
| swap-without-refresh-does-not-settle | differs | op/failed:UpdateItemOp:SQLite19:UNIQUE constraint failed: model_items.code | known |
| three-devices-change-reaches-the-other-two-in-different-orders | green |  | green |
| thrown-resolver-row-delete-is-dropped-and-row-comes-back | unsupported | ExpectFacetOp:acks | green |
| thrown-serverwins-run-hides-settled-insert | green |  | green |
| thrown-serverwins-run-hides-settlement | green |  | green |
| thrown-serverwins-sync-leaves-restored-row-out-of-local-row-changes | green |  | green |
| thrown-serverwins-upload-then-lww-sends-restored-row-as-edit | green |  | green |
| truncated-parent-held-stops-child-table | green |  | green |
| truncated-parent-read-back-across-tables | green |  | green |
| truncated-parent-read-back-selfref-chain | green |  | green |
| ul-local-delete-is-uploaded | green |  | green |
| ul-local-insert-is-uploaded-and-mirrors-server-version | green |  | green |
| ul-local-update-is-uploaded | unsupported | ExpectFacetOp:journal | green |
| ul-pending-edit-does-not-hide-other-rows-server-change | green |  | green |
| ul-row-edited-before-settle-keeps-its-version-until-sent | green |  | green |
| ul-settles-more-journal-entries-than-one-in-clause | unsupported | ExpectFacetOp:journal | green |
| ul-stale-journal-entry-is-discarded | unsupported | RecordJournalOp | green |
