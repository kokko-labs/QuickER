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
- ガード：`ProfileStoreTests`（OS との境界のエンジンの接続プロファイルの保存の表。行 `pairing-failed-*`＝入口 7 つ × 暗号ファイルを掴む・connections.json を掴む・暗号ファイルのフォルダに作成を拒否する、の 16 通りで、残った暗号ファイルがどれも JSON のその位置のプロファイルの接続先のパスワードであること・孤児が無いこと・失敗が返ること。行 `pairing-success-*` の 3 件と `pairing-rename-keeps-password-of-same-server`。全行に共通して、connections.json のフォルダと暗号フォルダに一時ファイルが残らないことも確かめる）・`ConnectionDialogTests` の行 `delete-secret-delete-fails-reports-and-reloads`・`rename-replaced-secret-delete-fails-reports-and-reloads`・`ok-last-used-secret-write-fails-confirms-and-shows-error`。mutation 実証済み。

## ダイアログとコマンドの状態の不変条件

- 接続ダイアログ（`DbConnectionDialogViewModel`）は、どの手の後にも次を守る。
  - 同期モードは方言を図に固定する（選択中の方言が変わらず、DBMS を選べず、別方言の前回接続・プロファイルを当てない）。
  - 接続テストの実行中は OK で確定できない（取り消しはできる）。
  - 取り消せない上書き・削除は警告つきの確認（`ConfirmWarning`）だけで聞く。
  - ストアの失敗（connections.json を読めない中止・暗号ファイルの書き込みの失敗）は例外を外へ出さず、一覧をディスクの実体どおりに保ち、ステータス行で知らせる。エラーのモーダルは OK の前回接続の記録の失敗だけで、確定は止めない。
  - 読めない保存済みパスワードの暗号ファイルを、入力し直しなしに失わない。保存・名前の変更の直後に入力欄を保存値で塗り直さない。
  - 確定して閉じるのは結果が入ったときだけ。SQLite の新規作成のファイルは OK で確定するまで作らない。
- DB 取込（`DbImportCommandService`）は、図を高々 1 回だけ、取込が成功し・確認が要らないか承認され・図を読んだ時点と置き換える時点の世代が同じときだけ置き換える。終端の通知はちょうど 1 件（取り消し・確認を断ったときは 0 件）で、失敗はモーダル。確認は未保存なら警告つき。
- DB 同期は接続を確定した時点の図を目標にし、世代は比べない（確認を挟まずに図を 1 回読むだけ。「確認の間に変わった図」は取込だけ）。
- 同期のダイアログ（`SchemaSyncDialogViewModel`）は、実行中は閉じられず、最中は操作を受け付けない。確認を断ったら実行器を呼ばず、実行の結果はモーダル 1 件で知らせてから差分を取り直す。取り直しに失敗したら差分とスクリプトを残さない（古いスクリプトを流さない）。
- 壊すと：同期の固定した方言と違う接続先へ DDL を流す・テスト中の確定で同じ DB を同時に叩く・失敗が黙って消える・確認の間の変更を古い取込結果で上書きする・取り直せなかった古いスクリプトを流す。
- ガード：`ConnectionDialogTests`・`ImportSyncTests`（どの行でも組が見張る。不変条件の一覧と出典の行は各組 `ConnectionDialogHarness`・`ImportSyncHarness` の XmlDoc）。mutation 実証済み。
