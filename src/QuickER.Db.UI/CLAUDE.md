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

## 接続プロファイルの（DBMS, 名前）の一意

- 保存済みのプロファイルは（DBMS, 名前）で一意にする（どちらも大文字小文字を無視）。判定は `SqlConnectionProfile.HasEntryKey` の 1 か所で、接続ダイアログの上書き先・名前の変更の衝突の探し方と、ストアの書き込みの判定がこれを使う（比べ方が食い違うと、ダイアログが別物とみなした名前をストアが断るか、ダイアログが確認した上書きをストアが複製として書く）。
- `SqlConnectionProfileStore` の `Upsert`・`Rename` は、書き込み用の 1 回の読み取りの一覧で、対象でも置き換え先でもない同じ（DBMS, 名前）を見つけたら、何も書かずに `ConnectionProfileNameConflictException`（衝突したプロファイルを持つ）を投げる。
  - 判定を書き込みと同じ読み取りの上で行うので、呼び出し側の一覧の古さ（開いた後にほかのプロセスが同名を保存した）にも、上書きの確認を出している間の書き込みにも依らない。
  - `Upsert` は正規化の前に判定する（断ったときは渡したインスタンスも書き換えない）。`Rename` は対象が無ければ衝突より先に `false` を返す。
  - 上書きしてよければ、衝突したプロファイルの Id を置き換え先に渡す。以前の版や手編集でディスクに既にある同名の 2 つも、この形で 1 つにできる。
- 壊すと：ダイアログの一覧が古いまま保存すると、上書き先を見つけられずに同じ名前のプロファイルが 2 つでき、名前で上書き先を決める保存がどちらを指すか決まらなくなる。
- ガード：`ProfileStoreTests`（行 `conflict-*` の 6 行と、全行に共通の表明「書く手は、手の前に（DBMS, 名前）で一意だったディスクに同じ（DBMS, 名前）を作らない」）・`ConnectionDialogTests`（組の不変条件 9 の「行の終わりにディスクも一意」と、ダイアログの側の扱いは次の節）。mutation 実証済み。

## ダイアログとコマンドの状態の不変条件

- 接続ダイアログ（`DbConnectionDialogViewModel`）は、どの手の後にも次を守る。
  - 同期モードは方言を図に固定する（選択中の方言が変わらず、DBMS を選べず、別方言の前回接続・プロファイルを当てない）。一覧には固定した方言のプロファイルだけを並べる（`ReloadProfiles`）。並べると、別方言を選んだときに選択だけが残り（入力欄は前のプロファイルのまま）、名前の変更・削除が入力欄に出ていないプロファイルに効く。
  - 選択を付け替えたら、選んだプロファイルを入力欄と保存名の欄へ当てる（選択中のプロファイルと入力欄が別のプロファイルを指さない）。
  - 接続テストの実行中は OK で確定できない（取り消しはできる）。
  - 取り消せない上書き・削除は警告つきの確認（`ConfirmWarning`）だけで聞く。
  - ストアの失敗（connections.json を読めない中止・暗号ファイルの書き込みの失敗・同じ（DBMS, 名前）の衝突）は例外を外へ出さず、一覧をディスクの実体どおりに保ち、ステータス行で知らせる。エラーのモーダルは OK の前回接続の記録の失敗だけで、確定は止めない。
  - 一覧の読み込み（開くとき・書き込みの後の読み直し）で connections.json を読めなかったら（`SqlConnectionProfileStore.Read` の分類 Unavailable）、一覧と選択を空にし、`AreProfilesUnavailable` を立て、ステータス行で「読めない」を知らせる（`DbConnection_ProfilesUnavailable`）。読めなかった一覧を「保存されたものが無い」と同じ空として黙って出さない。
    - 開くときは、読めていない前回接続を復元しない。
    - 書き込みの直後の読み直しが読めなかったときは、ステータス行を成功・失敗の文で上書きしない（「一覧を読み直した」とも言わない）。ディスクは書き込みのとおりに残る。
    - 印が立っている間の保存は、上書き先を探す前に一覧を読み直す。まだ読めなければ何も書かずに中止を知らせる（`DbConnection_ProfileStoreUnavailable`）。読めたら、読み直した一覧で上書き先を探す（同名があれば警告つきの確認）。読み直さずに探すと、ディスクにある同名を見つけられず、確認なしに同じ名前のプロファイルがもう 1 つできる。
    - ストアの読む入口は状態つきの `Read()` 1 つで、一覧と前回接続を同じ 1 回の読み取りから返す。`Read()` は、書き込みの直後の読み直しだけが読めない・壊れたファイルを読む状況をテストが作るために virtual にしている。
  - 一覧の読み込みで connections.json が壊れていたら（分類 Corrupt＝JSON として読めない・旧配列形式）、一覧と選択を空にし、ステータス行で壊れていることを知らせる（`DbConnection_ProfilesCorrupt`。壊れたファイルの場所と、最初に壊れていたファイルの写しの場所＝`CorruptBackupPath` を入れる）。黙って空で出すと、保存済みの接続が消えたように見え、次の保存で退避されたことにも気づけない。
    - `AreProfilesUnavailable` は立てず、保存も止めない（書けばファイルは正常に戻り、ストアは書く前に 1 回だけ `connections.json.corrupt` へ退避する）。保存の前の読み直しは、読めないときだけ。
    - 書き込みの後の読み直し（保存・名前の変更・削除の成功の後、書き込みの失敗の後、同名の衝突の後）で読めない・壊れているときは、その文を残し、成功・失敗の文で上書きしない（`ProfilesNotListed`）。
  - 保存・名前の変更がストアに同じ（DBMS, 名前）の衝突で断られたら（開いた後にほかのプロセスが同名を保存した）、一覧を読み直し、衝突したプロファイルへの上書きを警告つきの確認で聞く（衝突したのが選択中のプロファイルでも聞く）。承諾なら、衝突したプロファイルを置き換え先にして 1 回だけ書き直す。書き直しも断られたら、確認を繰り返さずに何も書かずに止めて知らせる（`DbConnection_ProfileNameConflict`）。読み直しで一覧を出せなければ、確かめずにその文を残して止める（`ResolveNameConflict`）。
  - 読めない保存済みパスワードの暗号ファイルを、入力し直しなしに失わない。保存・名前の変更の直後に入力欄を保存値で塗り直さない。
  - 確定して閉じるのは結果が入ったときだけ。SQLite の新規作成のファイルは OK で確定するまで作らず、作った後に掴まない（`src/QuickER.Provider.Sqlite/CLAUDE.md`「ファイルを掴まない接続」）。
- DB 取込（`DbImportCommandService`）は、図を高々 1 回だけ、取込が成功し・確認が要らないか承認され・図を読んだ時点と置き換える時点の世代が同じときだけ置き換える。終端の通知はちょうど 1 件（取り消し・確認を断ったときは 0 件）で、失敗はモーダル。確認は未保存なら警告つき。
  - 確認が要らないのは `DiagramReplacementCheck.CanSkipConfirmation` が真のとき（失うものが無いか構造が同じで、壊れるクエリも説明の上書きも無い）。未保存の変更があっても、構造が同じならこの判定で確認を省く（未保存かどうかは「失うものが無い」の枝だけが見る）。行 `import-dirty-same-structure-skips-confirmation-and-replaces`。
- DB 同期は接続を確定した時点の図を目標にし、世代は比べない（確認を挟まずに図を 1 回読むだけ。「確認の間に変わった図」は取込だけ）。
- 同期のダイアログ（`SchemaSyncDialogViewModel`）は、実行中は閉じられず、最中は操作を受け付けない（最中に始めた再計算は DB を読み直さない）。確認を断ったら実行器を呼ばず、実行の結果はモーダル 1 件で知らせてから差分を取り直す。取り直しに失敗したら差分とスクリプトを残さない（古いスクリプトを流さない）。
  - 自動で閉じるのは、コミットし、取り直しが成功し、差分が 0 件のときだけ。取り直しの失敗も差分を空にするので、件数だけで閉じると、成功のモーダルの直後に失敗の理由（ステータス行）ごと黙って閉じる。コミットの後の取り直しの失敗は閉じずに `ShowError`（`SchemaSync_RefreshAfterCommitFailedMessage`）で知らせる（`RefreshCoreAsync` が失敗の例外を返す）。
  - 実行の失敗・例外の後の取り直しの失敗はステータス行だけで知らせる（失敗のモーダルは出した後で、ダイアログは閉じないので理由が残る。モーダルを 2 件続けない）。
- 壊すと：同期の固定した方言と違う接続先へ DDL を流す・画面に出ていない別方言のプロファイルの名前を変える／消す・テスト中の確定で同じ DB を同時に叩く・失敗が黙って消える・読めなかった一覧や壊れたファイルを空として黙って出し、同じ名前のプロファイルを 2 つ作る・開いた後にほかのプロセスが保存した同名を確認なしに複製する・確認の間の変更を古い取込結果で上書きする・取り直せなかった古いスクリプトを流す・取り直せなかった同期を「図と DB が一致した」と見せて閉じる。
- ガード：`ConnectionDialogTests`・`ImportSyncTests`（どの行でも組が見張る。不変条件の一覧と出典の行は各組 `ConnectionDialogHarness`・`ImportSyncHarness` の XmlDoc。取り直しの失敗で閉じないことは組の不変条件 11 と行 `sync-dialog-execute-success-then-refresh-failure-stays-open-and-shows-error`。最中に始めた再計算が DB を読み直さないことは `ImportSyncHarness` の不変条件 14 と行 `sync-dialog-refresh-during-execute-does-not-reread-database`）。mutation 実証済み。
  - 同期モードの一覧と選択：`ConnectionDialogHarness` の不変条件 3（同期モードの一覧と選択中のプロファイルは固定した方言のものだけ）と 13（選択を付け替えた手の後、保存名の欄が選んだプロファイルの名前）。行 `sync-mode-lists-only-fixed-dialect-profiles`・`rename-and-delete-sync-with-other-dialect-in-store-touch-only-listed-profile`・`rename-sync-to-name-of-other-dialect-profile-does-not-ask-and-keeps-it`。
  - 一覧を読めないとき：`ConnectionDialogHarness` の不変条件 6（`AreProfilesUnavailable` の間は一覧も選択も空で、ステータス行が知らせている）と 9（行の終わりにディスクのプロファイルも（DBMS, 名前）で一意）。開くときは行 `open-store-locked-shows-empty-list-and-reports-unavailable`。保存の前の読み直しは行 `save-after-store-locked-open-still-locked-writes-nothing`・`save-after-store-locked-open-released-rereads-and-asks-overwrite`。書き込みの直後の読み直しは行 `save-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`rename-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`delete-store-locked-right-after-write-reports-unavailable-and-keeps-write`・`save-write-fails-then-store-locked-at-reload-reports-unavailable`（組の手 `LockConnectionsFileOnNextRead` が、テストの派生のストアで次の `Read()` の直前に connections.json を掴む）。ストアの分類は `ProfileStoreTests`（行 `legacy-sweep-reads-by-json-state`）。
  - 壊れたファイル：`ConnectionDialogHarness` の不変条件 14（開いた直後に分類が Corrupt なら、一覧も選択も空で、ステータス行が壊れていることを知らせている）。行 `open-corrupt-connections-file-reports-and-shows-empty-list`・`save-after-corrupt-open-backs-up-and-lists-saved-profile`。書き込みの後の読み直しは行 `save-connections-file-corrupted-right-after-write-reports-corrupt`・`rename-connections-file-corrupted-right-after-write-reports-corrupt`・`delete-connections-file-corrupted-right-after-write-reports-corrupt`・`save-write-fails-then-connections-file-corrupted-at-reload-reports-corrupt`・`save-conflict-then-connections-file-corrupted-at-reload-stops-without-asking`（組の手 `CorruptConnectionsFileOnNextRead` が、次の `Read()` の直前に connections.json を壊す）。
  - 開いた後にほかのプロセスが保存した同名：行 `save-name-added-by-other-process-after-open-asks-and-declined-keeps`・`save-name-added-by-other-process-after-open-confirmed-replaces-it`・`save-selected-profile-whose-name-was-taken-by-other-process-asks-overwrite`・`rename-to-name-added-by-other-process-asks-overwrite`・`save-overwrite-confirm-while-other-process-replaces-target-asks-again`・`save-conflict-again-after-confirm-reports-and-writes-nothing`（組の `OtherProcessStore` が、同じ保存先の別のストアとしてほかのプロセスの代役をする）。
  - 接続ダイアログの網羅しない組は、起こり得ない組（Accepted）の 4 項目だけ。
