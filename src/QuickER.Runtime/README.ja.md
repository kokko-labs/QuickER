# QuickER.Runtime

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイム共通基盤です。**依存パッケージゼロ**（BCL のみ）で、方言中立の Repository 共通契約（`IRepository<TEntity, TKey>`・`ISqlExecutor`・式木クエリ基盤 など）を提供します。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--use-runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定した場合に、スキーマ非依存の固定コードを本パッケージへの参照で賄います。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

方言エンジン（`QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite`）または EF Core 部品（`QuickER.Runtime.EntityFrameworkCore`）と組み合わせて使います。DI 登録拡張（`AddGenerated*Repositories`）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開され、同一メジャー内で互換です。

## ライセンス

MIT License。QuickER が生成したコードとあわせて、制限なく利用・改変・配布できます。

詳細: https://github.com/kokko-labs/QuickER
