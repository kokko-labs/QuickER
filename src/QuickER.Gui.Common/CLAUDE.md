# QuickER.Gui.Common

汎用 WPF 部品（`MessageBoxDialogService` / `WpfFileDialogService` / `PasswordBoxBehavior` / `InformationDetailsDialog`）の規則。

## PasswordBoxBehavior の DP 既定値
- `PasswordBoxBehavior` の DP 既定値は **null 必須**。`string.Empty` にすると空文字バインド時に PasswordChanged が購読されず、入力が VM に届かない。
- バインド先 VM プロパティは空文字で初期化する。
- ガード：`PasswordBoxBehaviorTests`。

## モーダルはオーナー付き
- `MessageBoxDialogService` の 7 メソッド（`IDialogService` 実装の全部）と `WpfFileDialogService` の 4 メソッドは、すべて共有の `DialogOwner.Resolve()`（アクティブなウィンドウ→`MainWindow`。どちらも表示済みのものだけ）を通す。
  - オーナーを与えない `MessageBox.Show` は、モードレスで開いた機能ウィンドウ（AI モック生成：`MockGenerationLauncher` が `Owner = null; Show()`）の背面へ回り込んで見えなくなる。モーダルは呼び出し元スレッドを止めるので、画面はダイアログを出す直前の状態のまま固まって見える（モック生成の完了ダイアログで実際に発生し、`StatusMessage` だけ完了になって実行中表示とボタン無効化が残った）。
  - Z 順序は WPF 実行時の挙動でテストでは守れないので、オーナー付与を外さない。
  - ガード：`SourceRuleTests` の規則 `dialog-owner-resolve-uses`（両サービスの共有）・`file-dialog-ownerless-shows`・`message-box-owner-resolver-copies`（オーナーなしの表示と自前の複製が無いこと）。
- 完了・失敗のモーダルは `finally` で実行中フラグを解除した**後**に出す（背面に回り込んでも画面は完了状態として正しく見える）。ガード：`GenerateMockProject_ResetsProgressBeforeShowingDialog`。
