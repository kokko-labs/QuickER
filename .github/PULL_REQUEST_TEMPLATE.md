<!-- PR を作る前に、まず Issue で方針の相談をお願いします（typo 等の小修正は不要）。詳細は CONTRIBUTING.md 参照 -->

## 関連 Issue

<!-- 事前相談した Issue へのリンク（例: #12）。小修正の場合は「小修正のためなし」と書いてください -->

## 変更概要

## チェックリスト

- [ ] 事前相談の Issue がある（または typo 等の小修正である）
- [ ] `dotnet test QuickER.slnx` が緑（Docker なし環境での統合テスト自動スキップは可）
- [ ] `csharpier format .` を実行した
- [ ] コメント・コミットメッセージは日本語（既存の規約に合わせた）
- [ ] （該当時）生成テンプレート変更に伴う固定フィクスチャの再生成を実行した（CONTRIBUTING.md 参照）
- [ ] （該当時）利用者に影響する変更を CHANGELOG.md と CHANGELOG.ja.md **両方**の Unreleased 欄へ追記した
