# QuickER.Runtime.AspNetCore

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイムのうち、リモートエンドポイントの**サーバー側固定エンジン**です。生成された `MapGeneratedRemoteEndpoints` の裏側（リクエスト読み取りと Minimal API マッピング・失敗の 400 / 409 / 500 への分類・相関 ID 付きのエラー詳細公開ポリシー・バイナリストリーミングの補助）を提供し、`QuickER.Runtime` の方言中立なリモート契約を ASP.NET Core 上で実装します。NuGet 依存はありません（代わりに `Microsoft.AspNetCore.App` 共有フレームワークを参照します）。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--use-runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定し、リモートサービス生成（`GenerateRemoteServices`）を有効にした場合に、`QuickER.Runtime` とあわせて参照します。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

参照するのは**サーバーファイル（`{ベース名}.RemoteServer.g.cs`）を載せるプロジェクト**です。ASP.NET Core 共有フレームワークを解決できる Web プロジェクト（SDK が `Microsoft.NET.Sdk.Web`、または `<FrameworkReference Include="Microsoft.AspNetCore.App" />` を宣言したプロジェクト）である必要があります。クライアント側（`Http{Entity}RemoteRepository`）は `QuickER.Runtime` だけで成立するため、クライアントプロジェクトは本パッケージを参照しません。

per-entity のエンドポイント（`GeneratedRemoteEndpoints`＝`MapGeneratedRemoteEndpoints` と `OnServerError` partial フックを含む）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開されます。コードを生成したツールと同一バージョンを参照してください。0.x の間は minor 更新にも破壊的変更が含まれることがあります（リポジトリの CONTRIBUTING.ja.md のバージョニング方針を参照）。

## ライセンス

MIT License（パッケージ自体に適用）。QuickER が生成したコードはあなたの成果物であり、ライセンス上の義務は一切ありません。

詳細: https://github.com/kokko-labs/QuickER
