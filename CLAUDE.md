# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

QuickER は WPF 製の ER 図デザイナ。DB スキーマのインポート／DDL 生成（SQL Server / PostgreSQL / MySQL / Oracle / SQLite の5方言）、Scriban テンプレートによる C# コード生成、AI チャットによる図の操作（MCP サーバ内蔵）を持つ。コメント・コミットメッセージは日本語。

## コマンド

```powershell
dotnet build QuickER.slnx                                # ビルド
dotnet test QuickER.slnx --no-build                      # 全テスト
dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~DdlGeneratorTests"   # 単一テストクラス
csharpier format .                                       # 整形（グローバルツール）。コード修正後は必ず実行
```

- テストは net10.0-windows / WPF 依存のため **Windows でのみ実行可能**（CI も windows-latest）
- `tests/QuickER.Tests/Integration/` は実 DB テスト。SQL Server / PostgreSQL / MySQL / Oracle は Testcontainers の実コンテナを使い、**Docker 不在時は自動スキップ**される（フィクスチャが検出）。この開発機では Docker 稼働＝全件実行・スキップ 0 が正常値。CI では Linux コンテナが使えないため常にスキップされる。SQLite 系は実ファイル DB（SqliteTempDatabase）を使うため **Docker 不要＝CI でも常時実行**される
- 生成コードの Roslyn コンパイル検証（GeneratedCodeCompilationTests）は Docker 不要で常時実行される

## アーキテクチャ

依存方向（上が被依存側。矢印はプロジェクト参照）：

```
QuickER.Model        意味モデル（依存ゼロ。X/Y/色などの視覚情報を持たない）
  ▲
QuickER.CodeGen.CSharp    DB非依存のC#コード生成エンジン（Scriban・テンプレートは埋め込みリソース）
  ▲
QuickER.Provider     DB抽象化の共通基盤（DdlGeneratorBase、インポータ共有部品）
  ▲
QuickER.SqlServer / PostgreSql / MySql / Oracle / Sqlite    方言プロバイダ（5実装で対称構造）
  ▲
QuickER.Gui (WPF) / QuickER.Cli                    合成ルート（全プロジェクトを参照）

QuickER.Document → Model    保存単位 DiagramDocument = schema(意味) + layout(視覚) の1ファイル
QuickER.AI       → Model, Settings    AIチャットエンジン共通基盤 AI/MCP/ASP.NET Core（WPF非依存。VM操作は IErDiagramToolHost 経由でアプリ側）
QuickER.Settings     設定 JSON の汎用永続化ストア JsonSettingsStore<T>（依存ゼロ・net10.0。AI / CodeGen.UI が参照）

機能プロジェクト（WPF。Gui を参照しない＝Gui が参照する。MainViewModel 依存のアダプタとランチャーは Gui 側に残る）:
QuickER.Gui.Abstractions  アプリ汎用 UI サービス抽象（IDialogService / IFileDialogService。実装は Gui / AI.UI 側）
QuickER.AI.UI      → AI, Gui.Abstractions    AI 共有 UI 部品（接続パネル・添付・ApiKeyStore・ダイアログサービス実装）
QuickER.AI.Chat    → AI, AI.UI, Gui.Abstractions    チャット機能（ErDesign プロファイル・ER 設計ツール定義・チャットダイアログ）
QuickER.AI.Mock    → AI, AI.UI, Gui.Abstractions, CodeGen.CSharp, Provider    モック生成機能（MockDesign 一式・WPF モック生成）
QuickER.CodeGen.UI → CodeGen.CSharp, Gui.Abstractions, Provider, Settings, SqlServer, Sqlite    C# コード生成ダイアログ
```

押さえるべき設計判断：

- **意味と視覚の分離**: `Entity`（Model）は座標・色を持たない。視覚状態は `EntityViewModel` と `EntityLayout`（Document）が保持。エクスポータ・生成器は意味モデル `ErDiagram` のみを消費する
- **CodeGen.CSharp の DB 非依存**: 型解決（DB型→C#型）はプロバイダ側の責務。CodeGen.CSharp は解決済み `CSharpTypeInfo` の辞書を入力に受け取る
- **プロバイダの対称性**: 5方言は同じ構造（SchemaImporter / DdlGeneratorBase 派生 / 型マップ / 実 DB 統合テスト。統合テストは SQL Server / PostgreSQL / MySQL / Oracle が Testcontainers、SQLite は実ファイル DB）。方言 SQL の説明コメントは SQL Server 版と同水準に揃える。スキーマインポータの基底クラス化は**意図的に見送り**（方言差分が大きい）。共有部品は ForeignKeyRelationshipBuilder / UniqueColumnSetBuilder / SchemaTableEntry
- **生成コードの汎用性**: 生成される C# は CommunityToolkit 等の UI フレームワークに依存させない（WPF 以外でも使える設計）。Repository 生成は単一キー・アプリ側採番前提（複合キー・DB自動採番は対象外）
- **EF Core モード（GenerateEfCore）**: 既存 Entity をそのまま EF に乗せる方言非依存の QuickErDbContext＋EF 版 Repository を生成し、DI 登録（AddGeneratedRepositories ⇔ AddGeneratedEfCoreRepositories）の差し替えだけで自作 SQL Server 実装と交換できる。GenerateEfCore=false のとき生成物に EF への依存は一切出ない（using 含む）。スキーマ作成は従来どおり DDL 生成の責務で、EF は既存スキーマへの接続専用（Migrations / HasColumnType は範囲外）
- **DB 定義メタ属性（`[DbColumnMeta]` / `[DbTableMeta]`）**: 生成 Entity を「DB 定義の自己記述ドキュメント」にする定義用メタ（将来の C#→ErDiagram リバースの布石）。列に方言中立の型トークン（`CanonicalTypeToken`。例 `string(50)` / `decimal(10,2)` / `int32`・小文字・-1=max）と説明、テーブルに説明を刻む。付与は `IncludeDataAnnotations` ON かつ Entity 生成時のみで**対象 DB・Repository/EF 設定に依らない**。トークンは図の方言 `ITypeCatalog.TryParse` → `CanonicalTypeToken.Format` を `DiagramCodeGenerator` の後処理（`CanonicalTypeTokenAttacher`）で主辞書へ付加する（マッパ実装は無変更・解析不能な自由記述型はトークン null＝属性省略）。実行時型付けの `[SqlColumnType]`（SQL Server 専用・Size ガード）とは**責務分離**で無関係。canonical 由来のため可搬図では方言に依らず同一トークン（PortableFixtureDialectIndependenceTests が保証。**可搬フィクスチャの文字列は Unicode で統一すること**＝Ansi/Unicode 差でトークンが割れるため。lessons.md 参照）
- **ランタイム配布の折衷（インライン既定＋NuGet パッケージ参照モード）**: 生成コードのスキーマ非依存な固定コード（ランタイム）は既定で従来どおりインライン出力（自己完結・依存最小）。`UseRuntimePackages`（CLI `--runtime-packages`／GUI チェックボックス）で固定コードを出力せず、NuGet パッケージ `QuickER.Runtime`（共通基盤＋方言中立契約・依存ゼロ）／`QuickER.Runtime.SqlServer`／`QuickER.Runtime.Sqlite`（各方言エンジン・ADO 依存は方言別に排他）／`QuickER.Runtime.EntityFrameworkCore`（EF 共通部品・EF Core Relational 依存・ADO/DI なし）への参照で賄うモードに切り替わる（**4 パッケージ**）。パッケージ用ソース（`src/QuickER.Runtime*/QuickERRuntime*.g.cs`・4 本）の正本は Scriban テンプレートで、`RuntimePackageSourceRenderer` が「空図＋全機能 ON＋固定名前空間＋internal→public」でレンダリングしたものをチェックイン（**手編集禁止**）。DI 登録拡張（AddGenerated*Repositories）はエンティティ別登録を含むスキーマ依存物なので**常に生成側**（パッケージには入れない）。**EF Core 生成（GenerateEfCore）とパッケージ参照モードは併用可**＝EF 固定 infra を `TContext : DbContext` ジェネリック化したことで具象 QuickErDbContext 依存が生成側（DI 登録・エンティティ別実装）だけに残り、EF エンジンは EF パッケージへ切り出せる。パッケージモードでも QuickErDbContext・Fluent 構成・EfCore{Entity}Repository・AddGeneratedEfCoreRepositories は**常に生成側**に出力する。EF パッケージの EntitySaveMetadata は EF が使うメンバー（KeyProperty/AllProperties/PropertyByColumn/ColumnList/CascadeNavigations）だけの縮小版で、方言クォート焼き込みの SQL 文メンバー（TableName/*Sql）は自作 Repository コンテキスト（`repositories`）限定にガードして持たない（**マルチターゲット×EF の排他は別理由＝契約の型同一性で残る**）。PackageReference の届け方は「案内のみ」（生成ヘッダ＋ダイアログ/CLI 出力。csproj には触らない）で、バージョンはロックステップ（Directory.Build.props の `VersionPrefix`＝パッケージ版＝ツール版。案内の版解決は `RuntimePackages.ResolveGuidanceVersion`）。公開は `.github/workflows/publish.yml`（workflow_dispatch・dry_run 既定 true）
- **名前付きクエリ（ErDiagram.Queries）**: 図に保存するクエリ定義（`QueryDefinition`・エンティティ/列は Guid 参照）から Repository メソッドを生成する。条件は簡易 DSL（UI 表記も「簡易 DSL」。比較・AND/OR/NOT・括弧・IS [NOT] NULL・[NOT] LIKE・[NOT] IN・CONTAINS/STARTSWITH/ENDSWITH。パーサ＝`QueryConditionParser`・診断はローカライズ済み）で、**単一の C# ラムダ式へエミット**（`QueryConditionCSharpEmitter`）して既存 `Query()` パイプライン（式木→`SqlExpressionTranslator`／EF Where 直適用）に流す＝方言 SQL 翻訳は実行時・エミッタに方言分岐なし。VO 列は `Vo.Create(値)` 比較・VO×IN はメソッド冒頭でリスト持ち上げ。戻り形は 一覧/単一/件数/射影（DTO 生成・寛容マッパー互換）＋スカラー（生 SQL／手動実装専用）。実装方式の UI 表記は「簡易 DSL」「生 SQL」「手動実装 (partial クラス)」（enum は Dsl/Sql/Manual のまま）。実装の出し分けは統一規則「**SQL（または DSL 共有本体）が与えられていない実装先は手動実装（manual）**＝契約宣言のみ生成・実装はユーザーが partial クラスへ通常メソッドで書く（漏れはコンパイルエラー）」——EF×生 SQL・SQL 辞書にない方言・インメモリがすべて同じ規則に落ちる。型トークン解決は列型と同じくプロバイダ層（`QueryParameterTypeResolver`・列と同一経路で解決）。LIKE の意味論はランタイムのエスケープ設計に合わせ「リテラルは % 位置で分解・`LIKE @p` は部分一致（値リテラル扱い）」で、生 LIKE パターンは生 SQL の担当。生 SQL バインダはコレクション値を `@名0,@名1,...` へ IN 展開（空リストは `(NULL)`＝不一致）。**EF Core Sqlite は decimal のサーバーサイド比較・並び替え非対応**のため、SQLite×EF で使う図の DSL 条件・並び順は整数キー等に限る（実 DB パリティは NamedQueryAdo/EfCoreRuntimeTests＝CI 常時実行）
- **リモート契約生成（GenerateRemoteContracts）**: リモート操作用インターフェイス `I{Entity}RemoteRepository` を**追加生成**する bool オプション（既定 false。CLI `--remote-contracts`／GUI「リモート対応」チェック＝DB アクセス配下・「なし」時は非表示。3 階層構想 Phase 2）。ランタイム共通部は**オプションに依らず常時**、基底分割 `IRemoteRepository<TEntity, TKey>`（CRUD＋Save×2＝ネットワーク境界を越えられる操作）← `IRepository`（＋Query()・生 SQL 3 種・BulkInsert）を非破壊で出力する（既存利用コードは無変更でコンパイル可）。ON のとき `I{Entity}RemoteRepository`（IRemoteRepository 継承＋**名前付きクエリ全部**＝Dsl/Sql/Manual ともシグネチャはリモート可能）が追加され、既存の `I{Entity}Repository` はそれと IRepository を継承する**全機能面のまま**（純粋に追加的＝ON にしても既存コードは一切壊れない）。**実装クラス・DI 実装登録は従来どおり全機能面基準**（間仕切りは純粋にインターフェイス水準・エンジンに分岐なし）で、リモート面は同一インスタンスへの転送として追加登録（keyed 版も同様）。EF Core・InMemory・マルチターゲット・UseRuntimePackages と直交。リモート面だけに依存したコードは、実体を Web サービス経由の実装（下記リモートサービス生成）へ差し替えてもコンパイル時に安全。実 DB 検証は RemoteContractRuntimeTests（CI 常時実行）
- **リモートサービス生成（GenerateRemoteServices）**: リモート面を **HTTP + JSON** で提供するクライアント／サーバー実装を生成する bool オプション（既定 false。CLI `--remote-services`／GUI「リモート対応」行の 2 つ目チェック。ON はリモート面を自動含意。3 階層構想 Phase 3）。転送方式は gRPC でなく HTTP+JSON＝**実証済みの JSON 資産を転用**（EntityBase.Clone と同じ意味論の `RemoteJson.Options`＝VO コンバータ・RowState 込み・IgnoreCycles。**IgnoreReadOnlyProperties は指定しない**＝クエリ転送エンベロープの匿名型が get-only で空 JSON になるため）。(1) クライアント＝`Http{Entity}RemoteRepository`（BCL の HttpClient のみ・`AddGeneratedHttpRemoteRepositories(baseAddress | HttpClient ファクトリ)`・名前付きクエリは**実装方式に依らず全部**転送メソッド化）を本体生成物へ同梱し、(2) サーバー＝`MapGeneratedRemoteEndpoints(prefix="/quicker")`（Minimal API・`POST {prefix}/{エンティティ}/{操作}`・RouteGroupBuilder を返し認可付与可）を**別ファイル** `{ベース名}.RemoteServer.g.cs` へ出力（ASP.NET Core の FrameworkReference 必須のため本体へ連結しない。バケット RemoteServer）。クライアント側固定部（RemoteJson・RemoteRepositoryException・エンベロープ・HttpRemoteRepository 基底）は共有 infra＝**Core パッケージへも収載**（BCL のみ＝依存ゼロ維持）で、per-entity クライアント・DI 登録はスキーマ依存物として常に生成側。**例外は型を復元**: SaveConflictException⇔HTTP 409＋構造化 JSON（RemoteError）・その他⇔500＋RemoteRepositoryException＝直結⇔リモートで catch が変わらない。グラフ保存成功後はクライアント側でも RowState を確定（[NavigationReference(Cascade)] 走査＝EntityGraphSaver.AcceptChanges と同じ意味論）。**BulkInsert・Query()・生 SQL はリモート面に無いため転送対象外**（設計どおり）。実 HTTP 検証は RemoteServiceAdo/EfCoreRuntimeTests（Kestrel を 127.0.0.1 空きポートで in-process 起動・CI 常時実行）
- **DB アクセスの排他と単独出力**: Repository バケットは「共通契約」（GenerateRepositories || GenerateEfCore で出力）と「自作 Repository 実装」（GenerateRepositories 時のみ）に分割されており、**GenerateEfCore 単独の生成物には自作実装の ADO 依存（Microsoft.Data.SqlClient / Microsoft.Data.Sqlite）が一切出ない**（ガードテストあり）。自作 Repository は `RepositoryDialect` で方言を選ぶ多DB対応（既定 `sqlserver`＝FOR JSON／`sqlite`＝プレーン SELECT＋IncludeLoader マルチクエリ・LIMIT/OFFSET・strftime）で、生成時に片方言のみを出力し方言間の ADO 依存も排他（ガードテスト双方向）。`RepositoryDialects`（リスト・単一 `RepositoryDialect` の後方互換上位）に複数方言を指定するとマルチターゲット出力になり、中立契約（IRepository / I{Entity}Repository / ISqlExecutor など）を `RepositoryNamespace` に 1 回・方言別実装を `.SqlServer` / `.Sqlite` の方言別 namespace に出力する。DI は方言別拡張＋keyed 版（AddGeneratedSqlServerRepositories / AddGeneratedSqliteRepositories・`object? serviceKey` 付き）で、単一契約型を keyed 解決して同一プロセスで複数 DB へ書き分けられる（実 DB 結合は MultiTargetRepositoryRuntimeTests＝Docker 依存・keyed 解決の型検証は MultiTargetRepositorySqliteKeyedResolutionTests＝CI 実行）。マルチターゲット（実効方言 2 つ以上）と GenerateEfCore は排他で、併用指定は診断エラーになる。GUI の生成ダイアログは DB アクセスをラジオ3択（なし既定／Repository (QuickER)＝常時選択可・対象 DB チェック群 [SQL Server / SQLite] で選択（既定=図の方言、未対応方言の図では空・最低1つ必須）／EF Core）で排他選択させる。両方 ON はパリティ検証用に CLI／オプション直指定でのみ可能

## 不変条件（ビルド・型検査では検出できず、壊すと静かに回帰する）

- `Templates/CSharpRuntime.scriban` は **CRLF 固定**（.gitattributes で強制）。生成コードのバイト一致が前提
- **テンプレート変更時は** `tests/QuickER.Tests/GeneratedFixture/` の固定フィクスチャ **8 つ**（GeneratedFixture.g.cs＝SQL Server 全カバレッジ・PortableFixture.g.cs＝方言可搬な EF 単独出力・SqlitePortableFixture.g.cs＝SQLite 方言の自作 Repository＋EF Core 併存・MultiTargetPortableFixture.g.cs＝sqlserver/sqlite マルチターゲット自作 Repository・EF なし・keyed DI・InMemoryFixture.g.cs＝インメモリ Repository・QueryFixture.g.cs＝名前付きクエリ入りの SQLite 方言＋EF 併存・RemoteContractFixture.g.cs＝QueryFixture と同一図で GenerateRemoteContracts=true＝リモート面追加の比較対照・RemoteServiceFixture＝同図で GenerateRemoteServices=true。**本体＋RemoteServiceFixture.RemoteServer.g.cs の 2 ファイル構成**＝サーバーファイルはテストプロジェクトで実コンパイルされ ASP.NET Core 参照の検証を兼ねる）の再生成が必要。各ドリフトテスト（GeneratedFixtureDriftTests / PortableFixtureDriftTests / SqlitePortableFixtureDriftTests / MultiTargetPortableFixtureDriftTests / InMemoryFixtureDriftTests / QueryFixtureDriftTests / RemoteContractFixtureDriftTests / RemoteServiceFixtureDriftTests＝出力ファイル構成そのものも検知対象）が「再生成→差分ゼロ」を検証し、失敗メッセージに再生成手順がある。再生成は次の 1 コマンドでまとめて処理する（環境変数 `QUICKER_REGEN_FIXTURES` を立てるとドリフト検知と同一経路で上書き。実処理は FixtureDriftHarness に集約）：

  ```powershell
  $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
  ```

  再生成後は環境変数なしで同じテストを流し、緑（ドリフトなし）を確認する（MultiTargetPortableFixture はマルチ辞書オーバーロードで生成するため、FixtureDriftHarness の該当オーバーロード経由で再生成される）。同じ 1 コマンドで**ランタイムパッケージ用ソース 4 本**（`src/QuickER.Runtime*/QuickERRuntime*.g.cs`＝Core/SqlServer/Sqlite/EntityFrameworkCore・RuntimePackageSourceDriftTests が検証・.gitattributes で eol=crlf 固定）と、**動くサンプルのチェックイン生成物 2 本**（`samples/ec-order/EcOrderSample/Generated/EcOrder.g.cs`＝SQLite 方言・自作 Repository・VO なし／`samples/ec-order/EcOrder.sql`＝SQLite DDL・EcOrderSampleDriftTests が検証。生成コードは**実 CLI と同一経路**で照合＝実 `quicker generate` が生成した物と同一であることを保証。DDL は非決定的な `-- 生成日時:` 行のみ正規化して比較・.gitattributes で eol=crlf 固定）も再生成される
- ランタイムパッケージ 4 csproj の依存集合は**完全一致で固定**（RuntimePackageProjectDependencyGuardTests）: Core=PackageReference ゼロ／SqlServer=Microsoft.Data.SqlClient のみ／Sqlite=Microsoft.Data.Sqlite＋SQLitePCLRaw ピンのみ／EntityFrameworkCore=Microsoft.EntityFrameworkCore.Relational のみ。DI 系・ADO・他方言を足すとガードが落ちる（依存を足す前に「それは本当にパッケージの責務か（スキーマ依存物ではないか）」を疑う）
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
