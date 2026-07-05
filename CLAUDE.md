# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

QuickER は WPF 製の ER 図デザイナ。DB スキーマのインポート／DDL 生成（SQL Server / PostgreSQL / MySQL / Oracle の4方言）、Scriban テンプレートによる C# コード生成、AI チャットによる図の操作（MCP サーバ内蔵）を持つ。コメント・コミットメッセージは日本語。

## コマンド

```powershell
dotnet build QuickER.slnx                                # ビルド
dotnet test QuickER.slnx --no-build                      # 全テスト
dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~DdlGeneratorTests"   # 単一テストクラス
csharpier format .                                       # 整形（グローバルツール）。コード修正後は必ず実行
```

- テストは net10.0-windows / WPF 依存のため **Windows でのみ実行可能**（CI も windows-latest）
- `tests/QuickER.Tests/Integration/` は Testcontainers による実 DB テスト。**Docker 不在時は自動スキップ**される（フィクスチャが検出）。この開発機では Docker 稼働＝全件実行・スキップ 0 が正常値。CI では Linux コンテナが使えないため常にスキップされる
- 生成コードの Roslyn コンパイル検証（GeneratedCodeCompilationTests）は Docker 不要で常時実行される

## アーキテクチャ

依存方向（上が被依存側。矢印はプロジェクト参照）：

```
QuickER.Model        意味モデル（依存ゼロ。X/Y/色などの視覚情報を持たない）
  ▲
QuickER.Generator    DB非依存のC#コード生成エンジン（Scriban・テンプレートは埋め込みリソース）
  ▲
QuickER.Provider     DB抽象化の共通基盤（DdlGeneratorBase、インポータ共有部品）
  ▲
QuickER.SqlServer / PostgreSql / MySql / Oracle    方言プロバイダ（4実装で対称構造）
  ▲
QuickER.Gui (WPF) / QuickER.Cli                    合成ルート（全プロジェクトを参照）

QuickER.Document → Model    保存単位 DiagramDocument = schema(意味) + layout(視覚) の1ファイル
QuickER.AI       → Model    AI/MCP/ASP.NET Core（WPF非依存。VM操作は IErDiagramToolHost 経由でアプリ側）
```

押さえるべき設計判断：

- **意味と視覚の分離**: `Entity`（Model）は座標・色を持たない。視覚状態は `EntityViewModel` と `EntityLayout`（Document）が保持。エクスポータ・生成器は意味モデル `ErDiagram` のみを消費する
- **Generator の DB 非依存**: 型解決（DB型→C#型）はプロバイダ側の責務。Generator は解決済み `CSharpTypeInfo` の辞書を入力に受け取る
- **プロバイダの対称性**: 4方言は同じ構造（SchemaImporter / DdlGeneratorBase 派生 / 型マップ / Testcontainers 統合テスト）。方言 SQL の説明コメントは SQL Server 版と同水準に揃える。スキーマインポータの基底クラス化は**意図的に見送り**（方言差分が大きい）。共有部品は ForeignKeyRelationshipBuilder / UniqueColumnSetBuilder / SchemaTableEntry
- **生成コードの汎用性**: 生成される C# は CommunityToolkit 等の UI フレームワークに依存させない（WPF 以外でも使える設計）。Repository 生成は単一キー・アプリ側採番前提（複合キー・DB自動採番は対象外）
- **EF Core モード（GenerateEfCore）**: 既存 Entity をそのまま EF に乗せる方言非依存の QuickErDbContext＋EF 版 Repository を生成し、DI 登録（AddGeneratedRepositories ⇔ AddGeneratedEfCoreRepositories）の差し替えだけで自作 SQL Server 実装と交換できる。GenerateEfCore=false のとき生成物に EF への依存は一切出ない（using 含む）。スキーマ作成は従来どおり DDL 生成の責務で、EF は既存スキーマへの接続専用（Migrations / HasColumnType は範囲外）
- **DB 定義メタ属性（`[DbColumnMeta]` / `[DbTableMeta]`）**: 生成 Entity を「DB 定義の自己記述ドキュメント」にする定義用メタ（将来の C#→ErDiagram リバースの布石）。列に方言中立の型トークン（`CanonicalTypeToken`。例 `string(50)` / `decimal(10,2)` / `int32`・小文字・-1=max）と説明、テーブルに説明を刻む。付与は `IncludeDataAnnotations` ON かつ Entity 生成時のみで**対象 DB・Repository/EF 設定に依らない**。トークンは図の方言 `ITypeCatalog.TryParse` → `CanonicalTypeToken.Format` を `DiagramCodeGenerator` の後処理（`CanonicalTypeTokenAttacher`）で主辞書へ付加する（マッパ実装は無変更・解析不能な自由記述型はトークン null＝属性省略）。実行時型付けの `[SqlColumnType]`（SQL Server 専用・Size ガード）とは**責務分離**で無関係。canonical 由来のため可搬図では方言に依らず同一トークン（PortableFixtureDialectIndependenceTests が保証。**可搬フィクスチャの文字列は Unicode で統一すること**＝Ansi/Unicode 差でトークンが割れるため。lessons.md 参照）
- **DB アクセスの排他と単独出力**: Repository バケットは「共通契約」（GenerateRepositories || GenerateEfCore で出力）と「自作 Repository 実装」（GenerateRepositories 時のみ）に分割されており、**GenerateEfCore 単独の生成物には自作実装の ADO 依存（Microsoft.Data.SqlClient / Microsoft.Data.Sqlite）が一切出ない**（ガードテストあり）。自作 Repository は `RepositoryDialect` で方言を選ぶ多DB対応（既定 `sqlserver`＝FOR JSON／`sqlite`＝プレーン SELECT＋IncludeLoader マルチクエリ・LIMIT/OFFSET・strftime）で、生成時に片方言のみを出力し方言間の ADO 依存も排他（ガードテスト双方向）。`RepositoryDialects`（リスト・単一 `RepositoryDialect` の後方互換上位）に複数方言を指定するとマルチターゲット出力になり、中立契約（IRepository / I{Entity}Repository / ISqlExecutor など）を `RepositoryNamespace` に 1 回・方言別実装を `.SqlServer` / `.Sqlite` の方言別 namespace に出力する。DI は方言別拡張＋keyed 版（AddGeneratedSqlServerRepositories / AddGeneratedSqliteRepositories・`object? serviceKey` 付き）で、単一契約型を keyed 解決して同一プロセスで複数 DB へ書き分けられる（実 DB 結合は MultiTargetRepositoryRuntimeTests＝Docker 依存・keyed 解決の型検証は MultiTargetRepositorySqliteKeyedResolutionTests＝CI 実行）。マルチターゲット（実効方言 2 つ以上）と GenerateEfCore は排他で、併用指定は診断エラーになる。GUI の生成ダイアログは DB アクセスをラジオ3択（なし既定／Repository (QuickER)＝常時選択可・対象 DB チェック群 [SQL Server / SQLite] で選択（既定=図の方言、未対応方言の図では空・最低1つ必須）／EF Core）で排他選択させる。両方 ON はパリティ検証用に CLI／オプション直指定でのみ可能

## 不変条件（ビルド・型検査では検出できず、壊すと静かに回帰する）

- `Templates/CSharpRuntime.scriban` は **CRLF 固定**（.gitattributes で強制）。生成コードのバイト一致が前提
- **テンプレート変更時は** `tests/QuickER.Tests/GeneratedFixture/` の固定フィクスチャ **4 つ**（GeneratedFixture.g.cs＝SQL Server 全カバレッジ・PortableFixture.g.cs＝方言可搬な EF 単独出力・SqlitePortableFixture.g.cs＝SQLite 方言の自作 Repository＋EF Core 併存・MultiTargetPortableFixture.g.cs＝sqlserver/sqlite マルチターゲット自作 Repository・EF なし・keyed DI）の再生成が必要。各ドリフトテスト（GeneratedFixtureDriftTests / PortableFixtureDriftTests / SqlitePortableFixtureDriftTests / MultiTargetPortableFixtureDriftTests）が「再生成→差分ゼロ」を検証し、失敗メッセージに再生成手順がある。再生成は次の 1 コマンドで 4 つまとめて処理する（環境変数 `QUICKER_REGEN_FIXTURES` を立てるとドリフト検知と同一経路で上書き。実処理は FixtureDriftHarness に集約）：

  ```powershell
  $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
  ```

  再生成後は環境変数なしで同じテストを流し、緑（ドリフトなし）を確認する（MultiTargetPortableFixture はマルチ辞書オーバーロードで生成するため、FixtureDriftHarness の該当オーバーロード経由で再生成される）
- DDL 出力の先頭コメントは `-- QuickER によって自動生成された DDL`（DdlGeneratorBase.cs）。このヘッダを検証するテストは存在しないため変更に気づきにくい
- `PasswordBoxBehavior`（QuickER.Gui/Views）の DP 既定値は **null 必須**。string.Empty にすると空文字バインド時に PasswordChanged が購読されず入力が VM に届かない（PasswordBoxBehaviorTests が守る）。バインド先 VM プロパティは空文字で初期化する
- 生成ランタイムの SqlParameter は `[SqlColumnType]` 属性由来の明示型。文字列 Size の「宣言長超過なら値長」ガードを固定 Size にするとサイレントなデータ破損を招く

これらのファイルに触るときは、対応するテスト（DdlGeneratorTests / PasswordBoxBehaviorTests / CSharpCodeGenerationServiceTests / GeneratedFixtureDriftTests）を必ず実行する。

## コーディング規約

- 既存コードと同様、ビヘイビアや主要メソッドに分かりやすい日本語コメントを付ける
- `if` / `for` / `switch` / `try` 等のブロック前後には空行を1行入れる（最終判断は CSharpier の挙動を優先）
- 文字列リテラルの定数化は一貫性重視：一部の表示文言だけを定数化せず、やるなら同種をまとめて整理する
- ビルド警告は可能な限り解消する

## ワークフロー

- `git add` / `git commit` は**ユーザーの明示的な指示があるまで実行しない**（変更はワーキングツリーに残してレビューを待つ）。ブランチ作成はタスク開始時に行ってよい
- 計画・教訓は `tasks/todo.md` / `tasks/lessons.md` に記録する運用。セッション開始時に lessons.md を確認する
- レイアウト等ビジュアルに直結する機能は、機能テストに加えて見た目の品質指標（占有面積・充填率・交差数・線長）を実測してから完了とする（lessons.md 参照）
