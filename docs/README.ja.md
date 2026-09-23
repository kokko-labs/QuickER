# QuickER ドキュメント

*[English](README.md) | 日本語*

以下は最初に読む順に並べています。
「まずはここから」の 2 本で QuickER が何のための道具かと一巡の手触りをつかみ、残りは必要になったときに引くリファレンスです。

## まずはここから

- [QuickER が ER モデルを正本にする理由](overview.ja.md)：道具としての背景。
  DDL・エンティティクラス・設計書に同じスキーマを書き写すと何が壊れるか、コードファーストとどう違うか、AI はどこに入るか。
  手順書ではなく読み物です。
- [チュートリアル（設計から実行まで）](getting-started.ja.md)：同梱の EC 注文サンプルで、図の編集 → DDL 出力 → コード生成 → アプリ実行までを一巡します。
  SQLite のファイル DB を使うので、DB サーバは不要です。

## 図を編集する

- [ER 図の編集](er-editor.ja.md)：エディタそのもの。
  エンティティと列、リレーション、複数選択と一括操作、Undo / Redo、対象 DBMS の切り替え、ファイル操作と自動保存、キーボードショートカット。
- [データベース連携](database.ja.md)：実データベースからのスキーマ取込、図との差分同期、DDL 生成を 5 方言について説明します。
  方言ごとの再現度（何を表現でき、何を表現できないか）も含みます。
- [インポートとエクスポート](import-export.ja.md)：それ以外の形式（DBML・Mermaid・Excel / HTML 定義書・画像・生成 C#）について、どちら向きに対応しているか、何が運ばれ何が落ちるか。

## コードを生成する

- [生成コードの使い方](code-generation.ja.md)：生成された C# のリファレンス。
  エンティティ、EditModel、リポジトリ、値オブジェクト、名前付きクエリ、EF Core モード、リモートサービス、双方向同期と、それぞれを有効にするオプション。
  最大のドキュメントなので、通読せず使う機能を検索して引いてください。
- [CLI リファレンス（quicker）](cli.ja.md)：`quicker generate` / `scaffold` / `reverse` / `mcp` の各オプションと、設定ファイル `quicker.json` のキー。

## AI と連携する

- [AI チャットの設定](ai-chat.ja.md)：開いている図を会話で編集するアプリ内チャットの設定（接続タブ 4 つ・接続方式は計 6 種）と、図からモック画面を起こす AI モック生成。
- [MCP サーバ（quicker mcp）](mcp.ja.md)：QuickER を stdio の MCP サーバとして起動し、外部の AI エージェント（Claude Code・Codex など）が自分のワークフローの中で図の編集やコード生成を行えるようにする方法。

## リポジトリの他の場所

- [README](../README.ja.md)：QuickER の概要（スクリーンショット付き）。
- [EC 注文サンプル](../samples/ec-order/README.ja.md)・[3 階層構成サンプル](../samples/ec-order-remote/README.ja.md)：ビルドして動かせるプロジェクト。
- [変更履歴](../CHANGELOG.ja.md)・[コントリビューション](../CONTRIBUTING.ja.md)・[ライセンス](../LICENSING.ja.md)・[セキュリティポリシー](../SECURITY.ja.md)
