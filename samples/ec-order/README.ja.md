# EC 注文サンプル（ec-order）

*[English](README.md) | 日本語*

QuickER が ER 図から生成した C# コードを、外部 DB なしで実際に動かせる最小サンプルです。
題材は EC の注文ドメイン（顧客・商品・注文・注文明細）で、SQLite ファイル DB に対して
QuickER 版 Repository（SQLite 方言）による CRUD・グラフ保存・Include・生 SQL 集計・
削除カスケードに加え、EditModel と Mapper による画面編集の往復（入力文字列の変換エラー検知・
確定値の変更追跡・エンティティへの書き戻し）を実演します。

生成コードの基本機能の実演はこのサンプルが担い、3 階層構成（HTTP + JSON）に固有のシナリオだけを
[ec-order-remote](../ec-order-remote/README.ja.md) が別途担います。

## 構成

| ファイル | 役割 |
|---|---|
| `EcOrder.json` | ER 図（GUI の保存形式）。GUI で開いて編集できる |
| `EcOrder.sql` | 図から生成した SQLite DDL（チェックイン済み） |
| `quicker.json` | CLI の生成オプション（名前空間・出力ファイル名を持つ最小構成） |
| `EcOrderSample/Generated/EcOrder.g.cs` | 図から生成した C# コード（チェックイン済み） |
| `EcOrderSample/Generated/EcOrder.g.md` | 生成 API のリファレンス Markdown（`--generate-api-docs` の同梱出力・チェックイン済み） |
| `EcOrderSample/Program.cs` | DDL で DB を作成し CRUD を実演するコンソールアプリ |

コンソールアプリは QuickER 本体プロジェクトには一切参照せず、利用者のプロジェクトと同じく
NuGet パッケージ（`Microsoft.Data.Sqlite` など）のみを参照します。

## 実行する

リポジトリ直下から次を実行します（.NET 10 SDK が必要）。

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

起動時に `EcOrder.sql` の DDL で SQLite ファイル DB（`ec-order.db`。実行ファイルと同じ `bin` 配下に作成）を
作り直し、各シナリオの結果を英語で表示します。期待値と異なる場合は例外で終了（終了コード非 0）します。

## 図を GUI で開く

`EcOrder.json` は GUI（QuickER.Gui）の保存形式そのものです。GUI を起動し、
`samples/ec-order/EcOrder.json` を開くと図を閲覧・編集できます。

## 生成コード・DDL を再生成する

### 実 CLI で再生成する

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json `
  --generate-api-docs
```

`--generate-api-docs` を付けると `EcOrder.g.cs` と同じベース名で API リファレンス Markdown
`EcOrder.g.md` も出力されます（ドリフトテストの検証対象）。

### ドリフトテストの再生成モードで一括再生成する

チェックイン済みの生成物は `EcOrderSampleDriftTests` が「実 CLI と同一経路の再生成物とバイト一致」を
検証します。テンプレート等を変更したら、既存フィクスチャと同じ 1 コマンドで再生成できます。

```powershell
$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
```

再生成後は環境変数なしで同じテストを流し、緑（ドリフトなし）になることを確認してください。
