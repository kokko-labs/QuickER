# QuickER.AI.Chat

チャット機能（ER 設計のプロファイル・ER 設計ツールの定義・チャットのダイアログ・`AiChatFeatureModule`）。
接続設定・添付・API キー・エンジンの破棄の作法は `src/QuickER.AI.UI/CLAUDE.md`、エンジンの共通の契約は `src/QuickER.AI/CLAUDE.md`「チャットエンジンの共通の契約」を参照する。

## ダイアログの状態の不変条件

- チャットのダイアログ（`AiChatDialogViewModel`）は、次の 3 つを守る。
  1. ターンの実行中でなければ、応答中（`IsStreaming`）の吹き出しを残さない。`IsTurnInProgress` のセッターが、ターンの終わり（完了・失敗・中断・送信の例外のどれでも）に `ErChatMessage.EndStreaming` で外す。外すのは通知を出し終えた後なので、通知の中で吹き出しを読むと応答中に見える。
  2. ターンの実行中に接続方式を変えない（タブ・`TryChangeBackend` は拒否する）。
  3. 切り替えられない状態（`CanChangeBackend` が偽＝ターンの実行中か会話の開始待ち）の `TryChangeBackend` は、確認を出さずに断り、接続方式を変えない。
- 外すと、ターンの後も吹き出しが Markdown 表示へ切り替わらない・実行中のエンジンを置き去りにして別のエンジンへ移る・会話を消す確認が開始待ちの間に出る。
- ガード：`ChatDialogTests`（どの行でも組 `ChatDialogHarness` が 3 つを見張る。行 `turn-assistant-messages-stop-streaming-*`・`backend-switch-during-turn-is-rejected-without-confirm`・`backend-can-change-backend-matches-rejection` ほか）。mutation 実証済み。
- 表の組は claude と copilot のクライアントに必ず偽物を渡す（claude は見つからない＝CI と同じ結果）。実物を渡すと、機械に claude.exe があるかで結果と残りの作業フォルダ（`%TEMP%\QuickER\claude-code\<guid>`）が変わる。行の終わりに全エンジンを破棄する。
