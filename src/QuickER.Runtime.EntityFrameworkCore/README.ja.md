# QuickER.Runtime.EntityFrameworkCore

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイムのうち、**EF Core 版 Repository の共通部品**です。`TContext : DbContext` ジェネリックの EF Core 版 Repository 基底や値オブジェクト翻訳プラグインを提供し、`QuickER.Runtime` の方言中立契約を EF Core で実装します。依存は `Microsoft.EntityFrameworkCore.Relational` のみ（ADO・DI 系への依存なし）です。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--use-runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定し、EF Core 生成（GenerateEfCore）を有効にした場合に、`QuickER.Runtime` とあわせて参照します。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

具象 `QuickErDbContext`・Fluent 構成・エンティティ別 `EfCore{Entity}Repository`・DI 登録拡張（`AddGeneratedEfCoreRepositories`）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開され、同一メジャー内で互換です。

## ライセンス

MIT License。QuickER が生成したコードとあわせて、制限なく利用・改変・配布できます。

詳細: https://github.com/kokko-labs/QuickER
