# チュートリアル（設計から実行まで）

*[English](getting-started.md) | 日本語*

同梱の EC 注文サンプルを使って、「図を編集する → DDL を出す → コードを生成する → アプリを動かす」の一巡を体験します。SQLite のファイルデータベースを使うため、外部データベースの準備は不要です。

## 前提

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（サンプルの実行と CLI に必要）

## 1. QuickER をインストールする

[GitHub Releases](https://github.com/kokko-labs/QuickER/releases) から Setup.exe（インストーラー・自動更新対応）または Portable zip（展開して `QuickER.exe` を実行）を入手します。チャンネルの違いは [README のインストール節](../README.ja.md#インストール)を参照してください。

ソースコードから起動する場合は次のとおりです。

```powershell
git clone https://github.com/kokko-labs/QuickER.git
cd QuickER
dotnet run --project src/QuickER.Gui
```

## 2. まずサンプルをそのまま動かす

リポジトリには、図（`EcOrder.json`）から生成した DDL と C# コードがチェックイン済みで含まれています。何も変更せずに動かしてみます。

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

起動時に DDL から SQLite ファイル DB が作り直され、登録・グラフ保存・検索などのシナリオが順に実行されます。最後に「すべてのシナリオが成功しました。」と表示されれば成功です。

## 3. 図を開く

QuickER を起動し、Ctrl+O で `samples/ec-order/EcOrder.json` を開きます。顧客（customers）、商品（products）、注文（orders）、注文明細（order_lines）の 4 テーブルからなる ER 図が表示されます。

## 4. 図を編集する

試しに、商品テーブルへ列を 1 つ追加してみます。

1. キャンバスで `products` をクリックして選択します
2. 右のプロパティパネル「項目」グリッド右上の「＋」で列を追加します
3. カラム名を `stock`、型を `INTEGER` にし、NULL チェックを付けます（NULL 許容にしておくと、既存のサンプルコードへ影響しません）
4. Ctrl+S で図を保存します

編集操作の詳細は [ER 図の編集](er-editor.ja.md) を参照してください。

## 5. DDL を出力する

ツールバーの「出力」から DDL を選び、`samples/ec-order/EcOrder.sql` へ上書き保存します。サンプルアプリは起動のたびにこの DDL でデータベースを作り直すため、これだけで DB スキーマの変更が反映されます。

## 6. コードを生成する

CLI で、図から C# コードを再生成します。

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order/EcOrder.json `
  --out samples/ec-order/EcOrderSample/Generated `
  --provider sqlite `
  --config samples/ec-order/quicker.json
```

`Generated/EcOrder.g.cs` が更新され、`products` の Entity と EditModel に `stock` に対応するプロパティが増えていることを確認できます。GUI のコード生成ダイアログから生成する方法や、生成オプションの詳細は [CLI リファレンス](cli.ja.md)と[生成コードの使い方](code-generation.ja.md)を参照してください。

## 7. もう一度実行する

```powershell
dotnet run --project samples/ec-order/EcOrderSample
```

新しい列を含むスキーマとコードで、同じシナリオがそのまま成功します。図を 1 か所直しただけで、DDL・Entity・EditModel が追従したことになります。

## 次のステップ

- [ER 図の編集](er-editor.ja.md) — エディタ機能のリファレンス
- [データベース連携](database.ja.md) — 既存 DB からの取込と差分同期
- [インポートとエクスポート](import-export.ja.md) — DBML / Mermaid / 定義書との相互運用
- [AI チャットの設定](ai-chat.ja.md) — 対話による図の生成
- [QuickER の設計思想](overview.ja.md) — このワークフローの背景
