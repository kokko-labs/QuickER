# QuickER.Cli

*[English](README.md) | 日本語*

QuickER（AI 支援のビジュアル ER 設計 × マルチ DB 往復 × C# コード生成を一気通貫でつなぐ Windows 用 ER 図デザイナ）のコマンドライン ツールです。GUI なしで、ER 図 JSON からの C# コード生成（`generate`）と、データベース直結のスキャフォールド（`scaffold`）を実行できます。

## インストール

```powershell
dotnet tool install --global QuickER.Cli
```

## 使い方

```powershell
# ER 図 JSON（GUI の保存形式）→ C# コード（Entity / EditModel / Mapper / Repository / EF Core）
quicker generate --schema diagram.json --out ./Generated --provider sqlserver

# 実 DB へ直接接続してスキーマを取込 → C# コード
quicker scaffold --connection "Server=.;Database=Shop;Integrated Security=true;TrustServerCertificate=true" --out ./Generated --provider sqlserver
```

主なオプション:

| オプション | 説明 |
|---|---|
| `--provider <name>` | 対象 DB。`sqlserver`（既定）/ `postgresql` / `mysql` / `oracle` / `sqlite` |
| `--config <file>` | 生成オプション設定ファイル（quicker.json） |
| `--root-namespace <name>` / `--split-files-by-category` | ルート名前空間の指定／カテゴリ別ファイル分割 |
| `--repository-dialects <list>` | QuickER 版 Repository のマルチターゲット生成（例 `sqlserver,sqlite`・keyed DI） |
| `--use-runtime-packages` | ランタイム固定コードを出力せず `QuickER.Runtime.*` パッケージ参照で賄う |
| `--generate-api-docs` | API リファレンス Markdown（`{ベース名}.g.md`）を追加出力 |

設定ファイルの各キーは同名の kebab-case フラグとしても指定でき、設定ファイルより優先されます（優先順位: CLI フラグ ＞ 設定ファイル ＞ 既定値。bool は `--flag` / `--flag false` の三値）。

詳細な CLI リファレンス・生成コードの使い方・動くサンプルは、リポジトリのドキュメントを参照してください:

https://github.com/kokko-labs/QuickER

## ライセンス

PolyForm Noncommercial 1.0.0（パッケージ同梱の LICENSE-NC.md）。**現在は商用利用を含め全員が無料**で利用できます。将来、商用利用のみ有償ライセンス化する可能性があります（個人・非商用は永続無料／基本生成＝Entity / EditModel / Mapper は商用含め永続無料／有償化する場合は事前に告知し、既存利用者には移行期間を設けます）。

**CLI が生成したコード（インライン出力されるランタイム部分を含む）はあなたの成果物**であり、ライセンスによる制限なく自由に利用・改変・配布できます。
