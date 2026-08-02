# QuickER への貢献ガイド

*[English](CONTRIBUTING.md) | 日本語*

QuickER は個人開発の OSS です。Issue・Pull Request を歓迎しますが、対応は**ベストエフォート**（対応期限の約束なし）で、**サポート対象は最新版のみ**です。

## Issue（バグ報告・機能要望）

- テンプレート（バグ報告・機能要望）を使ってください。テンプレートに合わない相談・質問は空 Issue で構いません
- 日本語・英語どちらでも歓迎します（Issues in English are welcome）
- バグ報告では環境情報（バージョン・入手方法・OS・.NET Runtime）と再現手順が調査の前提になります
- **脆弱性の報告は公開 Issue に書かないでください**。[SECURITY.ja.md](SECURITY.ja.md) の手順で非公開に報告してください

## Pull Request

- **PR を作る前に、まず Issue で方針を相談してください**（事前相談制）。typo 修正などの小さな修正は相談不要です
- 事前相談のない大きな PR は、方針に合わない場合クローズすることがあります

### 開発規約の要点

- 開発環境は Windows ＋ .NET 10 SDK（テストが WPF 依存のため）。Docker があれば実 DB 統合テストも実行されます（無ければ自動スキップ）
- コメント・コミットメッセージは日本語で書きます
- コード修正後は `csharpier format .` を実行してください（グローバルツール）
- `dotnet test QuickER.slnx` が緑であることを確認してください
- 生成テンプレート（`src/QuickER.CodeGen.CSharp/Templates/**/*.scriban`）を変更した場合は、固定フィクスチャ等の再生成が必要です。再生成 → 検証 → 差分表示までを次のスクリプトが行います:

  ```powershell
  ./scripts/regen-fixtures.ps1
  ```

  スクリプトを使わない場合は、次の 1 コマンドで再生成し、そのあと環境変数なしで同じテストを流して緑を確認してください:

  ```powershell
  $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
  ```

- 利用者に影響する変更は CHANGELOG の Unreleased 欄へ追記してください——[CHANGELOG.md](CHANGELOG.md)（英語）と [CHANGELOG.ja.md](CHANGELOG.ja.md)（日本語）の**両方**。粒度は「利用者から見た 1 変更」（おおむね機能ブランチ 1 本）で 1 エントリとし、該当する見出し（`### Added` / `### Changed` / `### Fixed` / `### Removed`）の下へ置きます（見出しが無ければ作成）。内部リファクタリング・テストのみの変更は不要なため、この欄が空のままになることもあります（想定内です）

アーキテクチャと「壊すと静かに回帰する不変条件」は [CLAUDE.md](CLAUDE.md) にまとまっています。

## ライセンスと貢献時の権利処理

- 本リポジトリはプロジェクトごとに **MIT** と **PolyForm Noncommercial 1.0.0＋追加許諾** を使い分けています。大半は MIT で、AI 機能群・コード生成系・CLI・MCP ツール実行ホストの 8 プロジェクトが PolyForm NC＋追加許諾です（対象一覧と正式な条件は [LICENSE-NC.md](LICENSE-NC.md)、平易な解説は [LICENSING.md](LICENSING.md) / [LICENSING.ja.md](LICENSING.ja.md) を参照）
- 提出されたコードは、**取り込み先プロジェクトの現行ライセンスで公開されること**に同意したものとみなします
- PolyForm NC 対象プロジェクトへの貢献では、あわせて**作者（リポジトリオーナー）が当該コードを含むソフトウェアに商用ライセンスを提供し、または将来ライセンスを変更（無料開放を含む）する権利**を許諾したものとみなします（将来の提供方針の変更を外部貢献が阻害しないための取り決めです）

## バージョニング

- [Semantic Versioning](https://semver.org/lang/ja/) に従います。全配布物（GUI・CLI・ランタイムパッケージ 4 種）はロックステップ＝`Directory.Build.props` の `VersionPrefix` で共通管理です
- 0.x の間の版上げルール:
  - **minor**（0.2.0 → 0.3.0）: 新機能、非互換変更（Repository API や生成コードのシグネチャ・構造の変化、パッケージの依存変更）
  - **patch**（0.2.0 → 0.2.1）: バグ修正のみ。利用側の呼び出しコードが壊れない修正は、生成コードの内部実装が変わっても patch とします
- 1.0.0 は「生成コードと Repository API の互換性を約束できる」と判断した時点で宣言します

## リリース手順（メンテナ向け）

リリースは**常に全配布物同時**（NuGet 5 パッケージ＋GUI 配布物（Velopack: full / lite × Setup.exe / Portable zip）＋git タグ `v{版}`）。時期は任意で、頻度は約束しません。

1. CHANGELOG の Unreleased 欄を確認し、版番号（上記ルールで minor / patch を判断）を決める。欄の見出しを版番号＋リリース日（`## [0.2.0] - 2026-09-01`）へ書き換え、その上に空の `## [Unreleased]` を新設する——**[CHANGELOG.md](CHANGELOG.md) と [CHANGELOG.ja.md](CHANGELOG.ja.md) の両方**
2. `Directory.Build.props` の `VersionPrefix` を更新し、CHANGELOG の確定と合わせて 1 コミットにする
3. publish.yml（NuGet 5 パッケージ）を workflow_dispatch で実行する（まず dry_run で確認してから本番実行）
4. release.yml（GUI 配布物の発行と git タグ作成）を workflow_dispatch で実行する（`dry_run` の既定は true。まず dry_run で成果物を確認してから `dry_run=false` で本番実行する）
5. GitHub Release のノートへ CHANGELOG の該当版の内容を転記する（release.yml は意図的に本文を空で作成する。何がリリースされたかの正本は、整理された CHANGELOG に一本化するため）
