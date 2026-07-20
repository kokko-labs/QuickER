# EC 注文リモートサンプル（ec-order-remote）

*日本語 | [English](README.md)*

QuickER の「リモートサービス生成（GenerateRemoteServices）」で ER 図から生成したコードだけで、
3 階層（クライアント → HTTP + JSON → サーバー → SQLite）を構成して動かす最小サンプルです。
題材は [ec-order](../ec-order/README.ja.md) と同じ EC の注文ドメインで、基本の CRUD・グラフ保存・Include・生 SQL などの
実演はそちらが担います。本サンプルのシナリオは、HTTP 越しであることが意味を持つ次の点だけに絞っています。

- **DI 登録 1 行での切り替え** — クライアントの呼び出しコードは DB 直結時と同一のインターフェイス
  （`I{Entity}RemoteRepository`）のままで、DI 登録が `AddGeneratedHttpRemoteRepositories` の 1 行に変わっているだけです
- **保存後の RowState 確定** — グラフ保存が成功すると、クライアント側でも `HasChanges` が直結時
  （`EntityGraphSaver.AcceptChanges`）と同じ意味論で確定します
- **名前付きクエリのリモート転送** — 射影 DTO（`OrderSummaryRow`）が JSON でクライアントまで届きます
- **`SaveConflictException` の型復元** — サーバー側の楽観的競合が HTTP 409＋構造化 JSON を経て、
  クライアント側でも同じ例外型で catch できます（直結時とまったく同じ `catch` が書けます）

## 構成

| ファイル | 役割 |
|---|---|
| `EcOrderRemote.json` | ER 図（名前付きクエリ 2 本を含む）。GUI で開いて編集できる |
| `quicker.json` | CLI の生成オプション（`GenerateRemoteServices=true` など） |
| `EcOrderRemote.sql` | 図から生成した SQLite DDL（チェックイン済み） |
| `Generated/EcOrderRemote.g.cs` | 本体生成物（Entity・Repository・リモート面契約・HTTP クライアント・DI 拡張。チェックイン済み） |
| `Generated/EcOrderRemote.RemoteServer.g.cs` | サーバー生成物（Minimal API の `MapGeneratedRemoteEndpoints`。チェックイン済み） |
| `EcOrderRemote.Shared/` | 本体生成物のみをリンクする共有クラスライブラリ（クライアント／サーバー双方の土台） |
| `EcOrderRemote.Server/` | サーバー生成物をリンクし、SQLite で待ち受ける Web アプリ（`Microsoft.NET.Sdk.Web`） |
| `EcOrderRemote.Client/` | HTTP クライアント実装だけで独自シナリオを検証するコンソールアプリ |

サーバー生成物は ASP.NET Core を要するため、CLI が同じ出力先へ書き出す 2 ファイルのうち本体生成物のみを
Shared でリンクし、サーバー生成物は Web SDK を使う Server プロジェクトでリンクしています
（利用者のプロジェクトで 2 ファイルを配置する際の参考になる構成です）。各プロジェクトは QuickER 本体を参照せず、
NuGet パッケージと ASP.NET Core の FrameworkReference（サーバーのみ）だけを参照します。

## 実行する

リポジトリ直下から、2 つのターミナルで順に実行します（.NET 10 SDK が必要）。

ターミナル 1（サーバー）:

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server
```

起動時に `EcOrderRemote.sql` の DDL で SQLite ファイル DB（`ec-order-remote.db`。実行ファイルと同じ `bin` 配下に作成）を
作り直し、`http://127.0.0.1:5210/quicker` で待ち受けます。

ターミナル 2（クライアント）:

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client
```

クライアントは各シナリオの結果を英語で表示し、すべて成功すると終了コード 0 で終わります。期待値と異なる場合は
例外で終了（終了コード非 0）します。サーバーの起動待ちは自動でリトライします。

ポートを変える場合は、サーバーとクライアントの両方に第 1 引数として同じベース URL を渡します
（例 `http://127.0.0.1:5299`）。

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server -- http://127.0.0.1:5299
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client -- http://127.0.0.1:5299
```

## 生成コード・DDL を再生成する

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order-remote/EcOrderRemote.json `
  --out samples/ec-order-remote/Generated `
  --provider sqlite `
  --config samples/ec-order-remote/quicker.json
```

`GenerateRemoteServices` は `quicker.json` 側で指定済みで、本体生成物とサーバー生成物の 2 ファイルが同じ `--out` へ
出力されます。ドリフトテストの再生成モードによる一括再生成の手順は [ec-order](../ec-order/README.ja.md) と共通です。

## 詳細ドキュメント

リモートサービス生成の仕様は [`docs/code-generation.ja.md`](../../docs/code-generation.ja.md) の
「リモートサービス生成」節を参照してください。
