---
name: audit-claude-md
description: CLAUDE.md 群（ルートの索引と領域のフォルダの CLAUDE.md）の記述が実コードと食い違っていないか（陳腐化）を多エージェントで全域監査する。「CLAUDE.md を監査して」「陳腐化チェック」の依頼、大型リファクタの後、public 化・リリース前の健診で使用する。
---

# CLAUDE.md 陳腐化監査

CLAUDE.md 群（プロジェクト規約と不変条件の正本＝ルートの索引と、領域のフォルダの CLAUDE.md）の全域を実コードと突合し、陳腐化した記述を洗い出して是正する。
2026-08-25 の初回監査で確立した手順。当時の結果＝食い違い 11 件（9 件が依存図・フォルダ列挙などの「受動的な一覧」・
不変条件の名指し 3 件・散文の設計判断はゼロ）＋エージェント誤検出 1 件。

## 手順

### 0. 機械の網を先に流す

```powershell
dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~ClaudeMdParityTests|FullyQualifiedName~ClaudeMdReferenceTests"
```

依存方向図の矢印 ⇔ csproj・src 全プロジェクトの登場・テストフォルダ一覧・固定フィクスチャの一覧（ルートの CLAUDE.md）は ClaudeMdParityTests が、全 CLAUDE.md が名指すテスト名・期待の行の Id・CLAUDE.md のパスと見出しは ClaudeMdReferenceTests が常時照合している。
赤ならまず CLAUDE.md 側を実体に合わせて直す（このスキルの残りは機械網が届かない散文層の監査）。

### 1. 5 系統の検証エージェントを並列起動する

Explore エージェント（読み取り専用・model: sonnet）を 5 体、担当範囲を分けて**並列**に起動する。
範囲はファイル単位で分ける（CLAUDE.md の一覧は `git ls-files "*CLAUDE.md"`）。分割の目安は次のとおり:

1. ルートの CLAUDE.md（概要・コマンド・依存図・固定フィクスチャ・索引・規約・ワークフロー）＋ tests/ の CLAUDE.md
2. src/QuickER.Provider と 5 方言の Provider.* ・Dialects・Model・Document
3. src/QuickER.CodeGen.CSharp（Queries・Templates を含む）・CodeGen.UI・src/QuickER.Runtime（Core）
4. ほかのランタイム（Runtime.SqlServer / Sqlite / EntityFrameworkCore / InMemory / AspNetCore / Sync）
5. GUI（Gui とその下位・Gui.Common・Extensibility・Db.UI）・AI・MCP・Platform・Settings

各エージェントへの指示の骨子（全員共通）:

- 担当範囲を読み、**具体的に検証可能な主張**（ファイルパス・クラス／メソッド／プロパティ名・enum 値・
  resx キー・テストクラス名・件数・コマンド・フラグ名・「撤去済み」「不変」系の主張）を自分で抽出する。
  **シンボルの列挙を指示側に書かない**（列挙自体が陳腐化するため。抽出→検証の方法だけを与える）
- 各主張を Glob / Grep / Read で実リポジトリと突き合わせる
- 報告は次の 2 区分だけ: 【陳腐化疑い】＝CLAUDE.md の引用（短く）→実際の状態（証拠のパス:行）→修正案の一文
  （ゼロならゼロと明記）／【検証不能】＝理由付きの列挙。設計の良し悪し・文体・提案は報告させない
- 実行結果に依存する主張（実測値・mutation 実証・過去の日付）は検証不能へ分類させる

### 2. 指摘を全件裏取りしてから採用する

エージェントの【陳腐化疑い】は**採用前に必ずメインセッションで実コードを見て確定させる**。
初回監査でも 12 件中 1 件が誤検出だった（stdio MCP のツール数＝「カタログ 11＋Mcp.Tools が足す
create_diagram＝12」で CLAUDE.md が正。エージェントはカタログしか数えていなかった）。
数の主張は「何をどう数えるか」の解釈違いが誤検出の典型形。

### 3. 是正と再発防止

- 確定した食い違いは CLAUDE.md を実体に合わせて修正する（一覧の漏れは追記・所在の移動は追従・
  存在しない名前は実名へ）
- 「実施予定」と書かれた計画が実施済みでないかは必ず疑う（適用済みの計画文は陳腐化爆弾になる）
- 新しく見つかった食い違いのパターンが機械照合できる形なら、ClaudeMdParityTests か ClaudeMdReferenceTests へ網を足すことを検討する
- ルートの索引の行と、詳細の CLAUDE.md の節が食い違っていないか（片方だけ直した跡）も見る
- 結果の要約と教訓は tasks/lessons.md の「CLAUDE.md の陳腐化は『受動的な一覧』に溜まる」へ追記する
