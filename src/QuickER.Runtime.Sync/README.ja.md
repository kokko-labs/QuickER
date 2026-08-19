# QuickER.Runtime.Sync

*[English](README.md) | 日本語*

QuickER（ER 図デザイナ）が生成する C# コードのランタイムのうち、**双方向同期エンジン**です。サーバーを正とする「サーバー＝SQL Server／ローカル＝SQLite」構成で、ローカルの共有ジャーナル 1 テーブルに記録したオフライン編集をサーバーへ再生し、サーバー側の変更は `rowversion` 列の走査で差分取得します。`rowversion` 列を持たないテーブルも、オプトインの後勝ちモード（キー順の全量スキャン・競合検出なしの上書き）で同期できます。使うのは `QuickER.Runtime` の方言中立契約（`IRepository` / `ISqlExecutor` / `ConcurrencyMode`）だけで、ADO にも EF Core にも触れません。NuGet 依存は一切ありません（BCL のみ）。

サーバー側には追加スキーマを作りません。差分の再開点はローカルのミラー版列の最大値から導出するため、データと食い違い得る管理行が存在しません。作るのはローカルのジャーナル 1 テーブルだけで、初回利用時に作成されます。

競合を黙って解決することはありません。既定では、サーバーと衝突したローカル変更はジャーナルに残り、両者の値を添えた構造化結果として返ります。

## 使いどき

既定では QuickER の生成コードはランタイム込みのインライン出力で自己完結するため、**本パッケージは不要**です。生成時に `--use-runtime-packages`（CLI）またはランタイムをパッケージ参照にするオプション（GUI）を指定し、同期支援（`GenerateSyncSupport`）を有効にした場合に、`QuickER.Runtime` / `QuickER.Runtime.SqlServer` / `QuickER.Runtime.Sqlite` とあわせて参照します。必要な PackageReference は生成コードのヘッダと CLI 出力に案内されます。

per-entity の同期記述子・ジャーナル記録デコレータ・直結差分ソース・DI 登録（`AddGeneratedSyncSupport`）はスキーマ依存物のため本パッケージには含まれず、常に生成コード側に出力されます。

## バージョン互換

パッケージ版は QuickER ツール版とロックステップ（同一バージョン）で公開されます。コードを生成したツールと同一バージョンを参照してください。0.x の間は minor 更新にも破壊的変更が含まれることがあります（リポジトリの CONTRIBUTING.ja.md のバージョニング方針を参照）。

## ライセンス

MIT License（パッケージ自体に適用）。QuickER が生成したコードはあなたの成果物であり、ライセンス上の義務は一切ありません。

詳細: https://github.com/kokko-labs/QuickER
