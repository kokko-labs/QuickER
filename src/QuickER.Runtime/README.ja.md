# QuickER.Runtime

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイム共通基盤です。**依存パッケージゼロ**（BCL のみ）で、方言中立の Repository 共通契約（`IRepository<TEntity, TKey>`・`ISqlExecutor`・式木クエリ基盤 など）を提供します。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--use-runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定した場合に、スキーマ非依存の固定コードを本パッケージへの参照で賄います。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

方言エンジン（`QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite`）・EF Core 部品（`QuickER.Runtime.EntityFrameworkCore`）・インメモリエンジン（`QuickER.Runtime.InMemory`）・リモートエンドポイントのサーバー側エンジン（`QuickER.Runtime.AspNetCore`）と組み合わせて使います。DI 登録拡張（`AddGenerated*Repositories`）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開されます。コードを生成したツールと同一バージョンを参照してください。0.x の間は minor 更新にも破壊的変更が含まれることがあります（リポジトリの CONTRIBUTING.ja.md のバージョニング方針を参照）。

## ライセンス

MIT License（パッケージ自体に適用）。QuickER が生成したコードはあなたの成果物であり、ライセンス上の義務は一切ありません。

詳細: https://github.com/kokko-labs/QuickER
