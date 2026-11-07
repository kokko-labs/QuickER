# QuickER.Db.UI

DB 取込／DB 同期ダイアログ・接続プロファイル（connections.json＋DPAPI シークレット）の規則。

## 同期のプレビューの失敗

- DB 同期のダイアログは、同期スクリプトのプレビューの生成に失敗したら、例外を外へ出さずにステータス行で知らせる。
  - `UpdatePreview` は差分の項目の `IsSelected` の購読から直接呼ばれるので、例外が抜けると WPF の未処理例外になる（名前・型の信頼境界の検査が、DDL・同期の入口で例外を投げるため）。
  - ガード：`ImportSyncTests`（行 `sync-dialog-preview-generation-failure-reports-status`・`sync-dialog-refresh-preview-failure-keeps-reason`）。mutation 実証済み。

## 接続プロファイルの暗号ファイルの順序
- 暗号ファイルは「消すのは JSON の書き込みの前・書くのは JSON の書き込みの後」の順で扱う。
  - 暗号ファイルのパスは接続先に依らない（前回接続は固定名 `last-connection.dat`・プロファイルは Id ごと）。「JSON → 暗号ファイル」の順で 2 段目だけが失敗すると、新しい接続先と古いパスワードの組が残り、そのまま接続すると前の接続先の資格情報を別のサーバーへ送る。
  - 逆順（暗号ファイル → JSON）も、JSON が失敗すると古い接続先と新しいパスワードの組が残るので採らない。
- `SqlConnectionProfileStore` の書き込み 4 つ（`SaveLastUsed`・`Upsert`・`Rename` の置き換え先・`Delete`）がこの順を守る。どの段の失敗・クラッシュでも残るのは「暗号ファイルの無いプロファイル」（パスワードは空欄＝再入力）か書き終えた状態だけで、孤児の暗号ファイルも残らない。
- 引き換えに、JSON の書き込みが失敗すると古いプロファイルの保存済みパスワードを失う（利用者判断で受け入れ済み）。
- 名前だけの変更（`Rename`）は接続先を変えないので、対象の暗号ファイルに触れない。
- プロファイルの一覧を丸ごと差し替える `SaveAll` は暗号ファイルを持ち回れない（接続先を変えた Id の古い暗号ファイルが残る）ので持たない。
- OK の前回接続の記録の失敗は、確定を止めずに `ShowError`（`DbConnection_LastUsedSaveFailed`）で知らせる。
- ガード：`ProfileStoreTests`（OS との境界のエンジンの接続プロファイルの保存の表。行 `pairing-failed-*`＝入口 7 つ × 暗号ファイルを掴む・connections.json を掴む・暗号ファイルのフォルダに作成を拒否する、の 17 通り（作成の拒否は新しいパスワードを書く 3 入口＝前回接続・Upsert・置き換えつき Upsert）で、残った暗号ファイルがどれも JSON のその位置のプロファイルの接続先のパスワードであること・孤児が無いこと・失敗が返ること。行 `pairing-success-*` の 3 件と `pairing-rename-keeps-password-of-same-server`。全行に共通して、connections.json のフォルダと暗号フォルダに一時ファイルが残らないことも確かめる）・`ConnectionDialogTests` の行 `delete-secret-delete-fails-reports-and-reloads`・`rename-replaced-secret-delete-fails-reports-and-reloads`・`ok-last-used-secret-write-fails-confirms-and-shows-error`。mutation 実証済み。

## ダイアログとコマンドの状態の不変条件

- 接続ダイアログ（`DbConnectionDialogViewModel`）は、どの手の後にも次を守る。
  - 同期モードは方言を図に固定する（選択中の方言が変わらず、DBMS を選べず、別方言の前回接続・プロファイルを当てない）。一覧には固定した方言のプロファイルだけを並べる（`ReloadProfiles`）。並べると、別方言を選んだときに選択だけが残り（入力欄は前のプロファイルのまま）、名前の変更・削除が入力欄に出ていないプロファイルに効く。
  - 選択を付け替えたら、選んだプロファイルを入力欄と保存名の欄へ当てる（選択中のプロファイルと入力欄が別のプロファイルを指さない）。
  - 接続テストの実行中は OK で確定できない（取り消しはできる）。
  - 取り消せない上書き・削除は警告つきの確認（`ConfirmWarning`）だけで聞く。
  - ストアの失敗（connections.json を読めない中止・暗号ファイルの書き込みの失敗）は例外を外へ出さず、一覧をディスクの実体どおりに保ち、ステータス行で知らせる。エラーのモーダルは OK の前回接続の記録の失敗だけで、確定は止めない。
  - 一覧の読み込み（開くとき・書き込みの後の読み直し）で connections.json を読めなかったら（`SqlConnectionProfileStore.Read` の分類 Unavailable）、一覧と選択を空にし、`AreProfilesUnavailable` を立て、ステータス行で「読めない」を知らせる（`DbConnection_ProfilesUnavailable`）。読めなかった一覧を「保存されたものが無い」と同じ空として黙って出さない。
    - 開くときは、読めていない前回接続を復元しない。
    - 書き込みの直後の読み直しが読めなかったときは、ステータス行を成功・失敗の文で上書きしない（「一覧を読み直した」とも言わない）。ディスクは書き込みのとおりに残る。
    - 印が立っている間の保存は、上書き先を探す前に一覧を読み直す。まだ読めなければ何も書かずに中止を知らせる（`DbConnection_ProfileStoreUnavailable`）。読めたら、読み直した一覧で上書き先を探す（同名があれば警告つきの確認）。読み直さずに探すと、ディスクにある同名を見つけられず、確認なしに同じ名前のプロファイルがもう 1 つできる。
    - ストアの読む入口は状態つきの `Read()` 1 つで、一覧と前回接続を同じ 1 回の読み取りから返す。`Read()` は、書き込みの直後の読み直しだけが読めない状況をテストが作るために virtual にしている。
    - 壊れた connections.json（分類 Corrupt）で開くときは、今は知らせない（空の一覧で開き、次の保存で `connections.json.corrupt` へ退避してから書き直す）。
  - 読めない保存済みパスワードの暗号ファイルを、入力し直しなしに失わない。保存・名前の変更の直後に入力欄を保存値で塗り直さない。
  - 確定して閉じるのは結果が入ったときだけ。SQLite の新規作成のファイルは OK で確定するまで作らず、作った後に掴まない（`src/QuickER.Provider.Sqlite/CLAUDE.md`「ファイルを掴まない接続」）。
- DB 取込（`DbImportCommandService`）は、図を高々 1 回だけ、取込が成功し・確認が要らないか承認され・図を読んだ時点と置き換える時点の世代が同じときだけ置き換える。終端の通知はちょうど 1 件（取り消し・確認を断ったときは 0 件）で、失敗はモーダル。確認は未保存なら警告つき。
  - 確認が要らないのは `DiagramReplacementCheck.CanSkipConfirmation` が真のとき（失うものが無いか構造が同じで、壊れるクエリも説明の上書きも無い）。未保存の変更があっても、構造が同じならこの判定で確認を省く（未保存かどうかは「失うものが無い」の枝だけが見る）。行 `import-dirty-same-structure-skips-confirmation-and-replaces`。
- DB 同期は接続を確定した時点の図を目標にし、世代は比べない（確認を挟まずに図を 1 回読むだけ。「確認の間に変わった図」は取込だけ）。
- 同期のダイアログ（`SchemaSyncDialogViewModel`）は、実行中は閉じられず、最中は操作を受け付けない（最中に始めた再計算は DB を読み直さない）。確認を断ったら実行器を呼ばず、実行の結果はモーダル 1 件で知らせてから差分を取り直す。取り直しに失敗したら差分とスクリプトを残さない（古いスクリプトを流さない）。
  - 自動で閉じるのは、コミットし、取り直しが成功し、差分が 0 件のときだけ。取り直しの失敗も差分を空にするので、件数だけで閉じると、成功のモーダルの直後に失敗の理由（ステータス行）ごと黙って閉じる。コミットの後の取り直しの失敗は閉じずに `ShowError`（`SchemaSync_RefreshAfterCommitFailedMessage`）で知らせる（`RefreshCoreAsync` が失敗の例外を返す）。
  - 実行の失敗・例外の後の取り直しの失敗はステータス行だけで知らせる（失敗のモーダルは出した後で、ダイアログは閉じないので理由が残る。モーダルを 2 件続けない）。
- 壊すと：同期の固定した方言と違う接続先へ DDL を流す・画面に出ていない別方言のプロファイルの名前を変える／消す・テスト中の確定で同じ DB を同時に叩く・失敗が黙って消える・読めなかった一覧を空として黙って出し、同じ名前のプロファイルを 2 つ作る・確認の間の変更を古い取込結果で上書きする・取り直せなかった古いスクリプトを流す・取り直せなかった同期を「図と DB が一致した」と見せて閉じる。
- ガード：`ConnectionDialogTests`・`ImportSyncTests`（どの行でも組が見張る。不変条件の一覧と出典の行は各組 `ConnectionDialogHarness`・`ImportSyncHarness` の XmlDoc。取り直しの失敗で閉じないことは組の不変条件 11 と行 `sync-dialog-execute-success-then-refresh-failure-stays-open-and-shows-error`。最中に始めた再計算が DB を読み直さないことは `ImportSyncHarness` の不変条件 14 と行 `sync-dialog-refresh-during-execute-does-not-reread-database`）。mutation 実証済み。
  - 同期モードの一覧と選択：`ConnectionDialogHarness` の不変条件 3（同期モードの一覧と選択中のプロファイルは固定した方言のものだけ）と 13（選択を付け替えた手の後、保存名の欄が選んだプロファイルの名前）。行 `sync-mode-lists-only-fixed-dialect-profiles`・`rename-and-delete-sync-with-other-dialect-in-store-touch-only-listed-profile`・`rename-sync-to-name-of-other-dialect-profile-does-not-ask-and-keeps-it`。
  - 一覧を読めないとき：`ConnectionDialogHarness` の不変条件 6（`AreProfilesUnavailable` の間は一覧も選択も空で、ステータス行が知らせている）と 9（行の終わりにディスクのプロファイルも（DBMS, 名前）で一意）。開くときは行 `open-store-locked-shows-empty-list-and-reports-unavailable`。保存の前の読み直しは行 `save-after-store-locked-open-still-locked-writes-nothing`・`save-after-store-locked-open-released-rereads-and-asks-overwrite`。書き込みの直後の読み直しは行 `save-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`rename-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`delete-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`save-write-fails-then-store-locked-at-reload-reports-unavailable`（組の手 `LockConnectionsFileOnNextRead` が、テストの派生のストアで次の `Read()` の直前に connections.json を掴む）。ストアの分類は `ProfileStoreTests`（行 `legacy-sweep-reads-by-json-state`）。
  - 接続ダイアログの網羅しない組は、起こり得ない組（Accepted）の 4 項目だけ。
