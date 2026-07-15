# EC 注文リモートサンプル（ec-order-remote）

*日本語 | [English](README.md)*

QuickER の「リモートサービス生成（GenerateRemoteServices）」で ER 図から生成したコードだけで、
3 階層（クライアント → HTTP + JSON → サーバー → SQLite）を構成して実際に動かせるサンプルです。
題材は [ec-order](../ec-order/README.ja.md) と同じ EC の注文ドメイン（顧客・商品・注文・注文明細）で、
そこへ名前付きクエリ 2 本（顧客別の注文検索・注文サマリーの射影）を加えた図を使います。

クライアントの呼び出しコードは DB 直結時とまったく同じインターフェイス（`I{Entity}RemoteRepository`）で書けており、
違いは DI 登録が `AddGeneratedHttpRemoteRepositories` の 1 行に変わっているだけです。生成された
`Http{Entity}RemoteRepository`（クライアント）と `MapGeneratedRemoteEndpoints`（サーバー）が、その間を
HTTP + JSON で自動的につなぎます。

## 構成

| ファイル | 役割 |
|---|---|
| `EcOrderRemote.json` | ER 図（GUI の保存形式）。名前付きクエリ 2 本を含む。GUI で開いて編集できる |
| `quicker.json` | CLI の生成オプション（名前空間・出力ファイル名・`GenerateRemoteServices=true`） |
| `EcOrderRemote.sql` | 図から生成した SQLite DDL（チェックイン済み） |
| `Generated/EcOrderRemote.g.cs` | 本体生成物（Entity・SQLite 方言の QuickER 版 Repository・リモート面契約・HTTP クライアント・DI 拡張。チェックイン済み） |
| `Generated/EcOrderRemote.RemoteServer.g.cs` | サーバー生成物（Minimal API の `MapGeneratedRemoteEndpoints`。ASP.NET Core を要する別ファイル。チェックイン済み） |
| `EcOrderRemote.Shared/` | 本体生成物のみをリンクする共有クラスライブラリ（クライアント／サーバー双方の土台） |
| `EcOrderRemote.Server/` | サーバー生成物をリンクし、SQLite で待ち受ける Web アプリ（`Microsoft.NET.Sdk.Web`） |
| `EcOrderRemote.Client/` | HTTP クライアント実装だけを使い、各シナリオを検証するコンソールアプリ |

各プロジェクトは QuickER 本体プロジェクトには一切参照せず、利用者のプロジェクトと同じく NuGet パッケージ
（`Microsoft.Data.Sqlite` など）と ASP.NET Core の FrameworkReference（サーバーのみ）だけを参照します。
サーバー生成物は ASP.NET Core を要するため、CLI が同じ出力先へ書き出す 2 ファイルのうち本体生成物のみを
Shared でリンクし、サーバー生成物は Web SDK を使う Server プロジェクトでリンクします。

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

クライアントは各シナリオの結果を日本語で表示し、すべて成功すると終了コード 0 で終わります。期待値と異なる場合は
例外で終了（終了コード非 0）します。サーバーの起動待ちは自動でリトライします。

ポートを変える場合は、サーバーとクライアントの両方に第 1 引数として同じベース URL を渡します
（例 `http://127.0.0.1:5299`）。

```powershell
dotnet run --project samples/ec-order-remote/EcOrderRemote.Server -- http://127.0.0.1:5299
dotnet run --project samples/ec-order-remote/EcOrderRemote.Client -- http://127.0.0.1:5299
```

## 見どころ

- **呼び出しコードが直結時と同一**: クライアントは `ICustomerRemoteRepository` などのインターフェイスへ普通に
  CRUD・保存・クエリを呼ぶだけ。DI 登録を `AddGeneratedRepositories`（DB 直結）へ差し替えれば、同じコードが
  そのままローカル実行になります。
- **名前付きクエリのリモート転送**: DSL 条件＋ページングの `GetByCustomerAsync` と、射影 DTO を返す
  `GetSummariesAsync` が HTTP 越しに同じ結果を返します（射影 `OrderSummaryRow` も JSON で往復）。
- **`SaveConflictException` の型復元**: 存在しない注文の更新保存はサーバー側で楽観的競合となり、HTTP 409 ＋
  構造化 JSON を経てクライアント側でも同じ `SaveConflictException` として復元されます＝直結時とまったく同じ
  `catch` が書けます。
- **保存後の RowState 確定**: グラフ保存が成功すると、クライアント側でもローカルの `HasChanges` が `false` に
  確定します（直結時の `EntityGraphSaver.AcceptChanges` と同じ意味論）。

## 図を GUI で開く

`EcOrderRemote.json` は GUI（QuickER.Gui）の保存形式そのものです。GUI を起動し、
`samples/ec-order-remote/EcOrderRemote.json` を開くと図（名前付きクエリを含む）を閲覧・編集できます。

## 生成コード・DDL を再生成する

### 実 CLI で再生成する

```powershell
dotnet run --project src/QuickER.Cli -- generate `
  --schema samples/ec-order-remote/EcOrderRemote.json `
  --out samples/ec-order-remote/Generated `
  --provider sqlite `
  --config samples/ec-order-remote/quicker.json
```

`GenerateRemoteServices` は `quicker.json` 側で指定済みです（`--remote-services` フラグでも同等）。
本体生成物とサーバー生成物の 2 ファイルが同じ `--out` へ出力されます。

### ドリフトテストの再生成モードで一括再生成する

テンプレート等を変更したら、既存フィクスチャと同じ 1 コマンドで再生成できます。

```powershell
$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
```

再生成後は環境変数なしで同じテストを流し、緑（ドリフトなし）になることを確認してください。

## 詳細ドキュメント

- リモートサービス生成を含むコード生成の詳細は [`docs/code-generation.md`](../../docs/code-generation.md) を参照してください。
