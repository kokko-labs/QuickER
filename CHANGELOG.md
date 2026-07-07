# 変更履歴（Changelog）

QuickER の利用者に影響する変更を記録します。形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、バージョンは [Semantic Versioning](https://semver.org/lang/ja/) に従います（0.x の間の版上げルールとリリース手順は [CONTRIBUTING.md](CONTRIBUTING.md) を参照）。

## [Unreleased]

### Added

- 初回公開
  - ビジュアル ER 設計（crow's foot 記法・1対1 / 1対多 / 多対多・複合主キー・FK 参照アクション・包括的 Undo/Redo・ズーム/パン/検索/ミニマップ等の大規模図向けキャンバス UX）
  - マルチ DB 対応（SQL Server / PostgreSQL / MySQL / Oracle / SQLite の 5 方言でスキーマ取込・差分同期・DDL 生成・方言切替の型自動変換）
  - C# コード生成（Entity / EditModel / Mapper＋DB アクセス 3 択: なし / Repository (QuickER) / EF Core。同一インターフェイスで DI 登録 1 行の差し替え・ランタイム NuGet パッケージ参照モード）
  - AI チャットによる図の生成・編集（OpenAI / Anthropic / Ollama / Codex / Claude Code）、ER 図からのモック生成
  - 入出力（DBML / Mermaid / Excel 定義書 / PNG / SVG / ベクタ印刷）と CLI（`quicker generate` / `quicker scaffold`）
  - 動くサンプル `samples/ec-order`（SQLite・外部 DB 不要）
