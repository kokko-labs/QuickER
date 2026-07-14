# QuickER.Runtime.Sqlite

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイムのうち、Repository (QuickER) の **SQLite 方言エンジン**です。`QuickER.Runtime` の方言中立契約を SQLite（マルチクエリの IncludeLoader・`LIMIT/OFFSET`・`strftime` など）で実装します。依存は `Microsoft.Data.Sqlite`（と脆弱性修正版 SQLitePCLRaw のピン）のみです。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定し、Repository (QuickER) の対象 DB に SQLite を含めた場合に、`QuickER.Runtime` とあわせて参照します。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

DI 登録拡張（`AddGeneratedSqliteRepositories` など）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開され、同一メジャー内で互換です。

## ライセンス

MIT License。QuickER が生成したコードとあわせて、制限なく利用・改変・配布できます。

詳細: https://github.com/kokko-labs/QuickER
