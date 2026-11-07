# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## この文書の構成

- このファイル（ルート）は、要求のたびに読まれる索引。概要・コマンド・アーキテクチャ・固定フィクスチャ・設計判断と不変条件の索引・規約・ワークフローを持つ
- 詳細は領域のフォルダの `CLAUDE.md` にある。Claude Code はそのフォルダのファイルを読んだときに自動で読み込む。索引の各行の「→」の先が詳細の場所
- Codex・Copilot などサブフォルダの CLAUDE.md を自動で読まないエージェントは、触る領域のフォルダの CLAUDE.md を先に読む
- 一覧の照合：依存図・プロジェクトの網羅・テストのフォルダ一覧・固定フィクスチャの一覧はこのファイルを `ClaudeMdParityTests` が、全 CLAUDE.md が名指すテスト名と CLAUDE.md のパス・見出しは `ClaudeMdReferenceTests` が照合する（書式の契約は `tests/QuickER.Tests/Docs/CLAUDE.md`「CLAUDE.md の照合」）

## プロジェクト概要

QuickER は WPF 製の ER 図デザイナ。DB スキーマのインポート／DDL 生成（SQL Server / PostgreSQL / MySQL / Oracle / SQLite の5方言）、Scriban テンプレートによる C# コード生成、AI チャットによる図の操作（MCP サーバ内蔵）、外部 AI エージェント向け stdio MCP サーバ（CLI `quicker mcp`）を持つ。コメント・コミットメッセージは日本語。ただし**生成物（.g.cs の XmlDoc・コメント・実行時例外メッセージ、.g.md）に埋め込む固定文は英語が正本**（製品の中立言語＝resx の中立と同じ英語。テンプレート `Templates/CSharpRuntime/*.scriban` 内の全コメントは生成出力そのものなので英語で書く。図の Description 由来のユーザーデータは無加工。API リファレンスは `ApiDocsLanguage`（English / Japanese / Both）／CLI `--api-docs-lang` で日本語版 `{ベース名}.ja.g.md` だけ・両方も出力可能＝テンプレート対訳ペア `ApiReferenceDoc.scriban`/`ApiReferenceDoc.ja.scriban` は構造パリティテストが同時更新を強制。生成出力の CJK 混入は GeneratedOutputEnglishGuardTests が検知）。**samples/ 配下は手書きコード（Program.cs のコメント・出力・データ）・図の Description も英語**（生成物と同居する国際向けショーケースのため。サンプルの README.ja.md のみ日本語）。この方針は `PhraseTests`（英語の行 `sample-files`）が機械で固定する＝`samples/` 配下のテキストファイル全部（生成物の `.g.cs` / `.g.md` / `.sql` も含む）を走査し、除外は `README.ja.md`・言語切替リンクの行（`](README.ja.md)`）・`bin` / `obj` の 3 つだけ（内容がバイナリのファイル＝先頭 8 KB に NUL を含むものは別枠で判定しない）（除外は出どころの関数 `EnglishTextRows.SampleLines` の規則で、その自己検査は `SampleEnglishGuardTests`。除外を広げるときは理由を `SampleLines` の remarks へ書く）。文章だけの方針は静かに嘘になる＝実際にサンプルの csproj 4 本が日本語のコメントのまま残っていた。

## コマンド

```powershell
dotnet build QuickER.slnx                                # ビルド
dotnet test QuickER.slnx --no-build                      # 全テスト
dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~DialectDdlTableTests"   # 単一テストクラス
csharpier format .                                       # 整形（グローバルツール）。コード修正後は必ず実行
```

### テストの構成

- テストは 2 プロジェクト構成: `tests/QuickER.Tests`（net10.0-windows / WPF 依存＝**Windows でのみ実行可能**）と `tests/QuickER.Tests.Integration`（net10.0・WPF 非依存＝クロスプラットフォーム。実 DB 統合＋生成コードのランタイムテスト）
  - CI は windows-latest（全体ビルド＋全テスト）と ubuntu-latest（Integration のみ・実コンテナ＝実 DB 検証の唯一の CI 経路）の 2 ジョブ
  - Windows のジョブのテストは TEMP / TMP をランナーの一時領域へ向ける（既定の C: の一時領域はファイルの作成・削除が遅い。`WorkflowGuardTests.WindowsTestStep_PutsTempFilesOnRunnerTemp` が固定）
  - 補助の console exe `tests/QuickER.Tests.FakeCodexAppServer`（CodexAppServerClient のテストの偽の app-server・テストは持たない）を QuickER.Tests が参照して出力へコピーする
- **テストのフォルダ構成は src のプロジェクト構成をミラー**する（`tests/QuickER.Tests/{Dialects|Model|Document|Provider|Provider.SqlServer|Provider.PostgreSql|Provider.MySql|Provider.Oracle|Provider.Sqlite|CodeGen.CSharp|CodeGen.UI|CodeReverse.CSharp|Db.UI|AI|AI.UI|AI.Chat|AI.Mock|Extensibility|Platform|Settings|Gui|Gui.Common|Cli|Mcp|Mcp.Tools}/`＝SUT の所在プロジェクトに対応・namespace はフォルダ追従で `QuickER.Tests.{フォルダ}`）
  - Gui/ 配下はさらに Behaviors/Services/ViewModels/Views/UndoRedo をミラーする
  - `QuickER.Tests/Dialects/` は QuickER.Dialects を SUT とする単体テストで、`QuickER.Tests.Integration/Dialects/` はアプリの DB 面の実 DB 検証（名前が近いだけで別物）
  - 横断フォルダは `Docs/`（CLAUDE.md の照合・docs 索引とライセンス文書のパリティ・docs の方言の表と宣言の照合）・`GeneratedFixture/`（ドリフト検知＝パス不動・regen スクリプトが参照）・`PublicApi/`（ランタイムパッケージ公開 API 面の承認ファイル）・`Resources/`（resx パリティ）・`Samples/`・`TestDoubles/`（共有テストダブル）・`TestSupport/`（WPF/STA 起動ヘルパとテストのエンジンの枠組み）
- `tests/QuickER.Tests.Integration/` は 3 サブフォルダ：`Dialects/`（アプリ自身の DB 面サービスの実 DB 検証・`*IntegrationTests`）・`GeneratedRuntime/`（生成コードの実行時挙動・原則 `*RuntimeTests` だが `*ParityTests` なども同居する。`RuntimeTestMatrixTests` はこのフォルダを名前空間で全数走査するので接尾辞は問わない）・`GeneratedFixture/`（インメモリ系ランタイムテスト）。共有フィクスチャ（*ContainerFixture / SqliteTempDatabase）はプロジェクト直下
  - **生成フィクスチャ（.g.cs・*FixtureDefinition・manual partial）は `tests/QuickER.Tests/GeneratedFixture/` の実体を Compile リンクで二重コンパイル**する（実体パスは不動・再生成は QuickER.Tests 側。manual partial は .g.cs と同一アセンブリ必須）
  - SQL Server / PostgreSQL / MySQL / Oracle は Testcontainers の実コンテナを使い、**Docker 不在時は自動スキップ**、**厳格モード `QUICKER_REQUIRE_DOCKER=1` ではスキップせず失敗**する（壊れた Docker 構成がスキップ緑に化けない。CI の ubuntu ジョブと Docker 稼働の開発機で設定する）。この開発機では Docker 稼働＝全件実行・スキップ 0 が正常値
  - SQLite 系は実ファイル DB（SqliteTempDatabase）を使うため Docker 不要＝両ジョブとも常時実行される。一時 DB は `%TEMP%\quicker-it\<実行>\<db>` に置き（TEMP 直下に同じ前置きの名前を並べると 8.3 の短い名前がぶつかって作成が遅くなる）、片付けは 3 段で行う（`SqliteTempRoot`）：その場（Dispose）・実行の終わり（全プールを解放して実行の親フォルダを消す）・次の実行の始め（ロックのファイルを開ける＝終わった実行の残りを消す。走っている実行には触らない）
- Docker 必須テストには `[Trait("RequiresDocker", "true")]` が付き、`--filter "RequiresDocker=true"`（または `!=`）で選別できる
- 生成コードの Roslyn コンパイル検証（`CodegenCaseTests` の行 `compile-*`）は Docker 不要で常時実行される（単発オプションの空振りの検査は `src/QuickER.CodeGen.CSharp/CLAUDE.md`）
- **生成コードのランタイムテストは「共通基底 1＋バックエンド派生 N＋派生固有 Fact」の形に揃え**、存在しない面は条件スキップでなくサブクラス階層で分ける（→ `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`）
- **テストのエンジン**（総当たり・モデルベース・実装先どうしの一致・網羅の計数・夜間の深い実行）の共通の枠組みは `tests/QuickER.Tests/TestSupport/Engines/`（→ `tests/QuickER.Tests/TestSupport/CLAUDE.md`）。同期計画の網と行は → `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`、実装先どうしの一致と同期のモデルは → `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`
- **src と tests の C# ソースに、タブ（U+0009）・FF（U+000C）・NEL（U+0085）・U+2028・U+2029 の実文字を置かない**（リテラル・コメントの中でもエスケープ表記で書く。ツール越しの書き込みで実文字に化けると、コンパイルが通ったまま値だけが変わる。`SourceRuleTests`（規則 `source-invisible-characters-src`・`source-invisible-characters-tests`））
- **英語ガードのテストが使う CJK の文字の範囲は `TestSupport.CjkText.Pattern` の 1 つだけ**（範囲の写しを置くと、1 か所だけ狭い写しの出力の混入を見逃す。`SourceRuleTests`（規則 `cjk-range-copies`） が tests/ の文字列リテラルから範囲の端の文字を探し、`CjkText.cs` 以外に現れたら名指しする）
- GUI テストのウィンドウのアクティブ・フォーカス・失敗時の状態の記録、条件コンパイルの全分岐を読むソースのガード、テストの DbContext の構成、テストが作る SQLite の DB の開き方は → `tests/QuickER.Tests/TestSupport/CLAUDE.md`

## アーキテクチャ

依存方向（上が被依存側。矢印はプロジェクト参照）：

```
QuickER.Model → Dialects    意味モデル（方言の宣言だけを参照する。X/Y/色などの視覚情報を持たない）
  ▲
QuickER.CodeGen.CSharp    DB非依存のC#コード生成エンジン（Scriban・テンプレートは埋め込みリソース）
  ▲
QuickER.Provider     DB抽象化の共通基盤（DdlGeneratorBase、インポータ共有部品）
  ▲
QuickER.Provider.SqlServer / Provider.PostgreSql / Provider.MySql / Provider.Oracle / Provider.Sqlite    方言プロバイダ（5実装で対称構造）
  ▲
QuickER.Gui (WPF) / QuickER.Cli                    合成ルート（全プロジェクトを直接または推移的に参照）

QuickER.Document → Model, Platform    保存単位 DiagramDocument = schema(意味) + layout(視覚) の1ファイル
QuickER.Mcp → Dialects    MCP ツール定義の正本（ToolDefinition・ErDiagramToolCatalog＝英語）と stdio サーバホスト・file 注入（パッケージの依存は ModelContextProtocol SDK と Microsoft.Extensions.Hosting のみ・プロジェクトは方言の名前のために Dialects だけを参照・WPF 非依存）
QuickER.Mcp.Tools → Model, Document, Mcp, CodeGen.CSharp    ファイルベースの ER 図ツール実行ホスト（ステートレスに読込→変更→保存。set_query の DSL/生 SQL 検証に CodeGen.CSharp の Queries を使用。CLI `quicker mcp` が合成）
QuickER.AI       → Model, Settings, Mcp, Platform    AIチャットエンジン共通基盤 AI/MCP/ASP.NET Core（WPF非依存。VM操作は IErDiagramToolHost 経由でアプリ側）
QuickER.Dialects     方言の宣言（方言名 DialectNames・識別子の長さの上限 IdentifierLengthLimits・MySQL のファイル名の符号化・図で表せない定義の種類 UnsupportedDefinitionKind と方言ごとの宣言 DialectDeclarations と機能ごとの扱い UnsupportedTreatments・スキーマで修飾されたテーブル名の分け方 SchemaQualifiedName。依存ゼロ・net10.0。Model と Mcp が参照し、他は推移的に見る）
QuickER.Settings → Platform    設定 JSON の汎用永続化ストア JsonSettingsStore<T>（net10.0。AI / CodeGen.UI / Gui が参照）
QuickER.Platform     OS との境界（原子的ファイル書き込み AtomicFile・書き出しの文字コード TextEncodings・保存先と一時フォルダ AppDataPaths。依存ゼロ・net10.0。Settings / Document / Db.UI / Cli / AI / AI.UI / AI.Chat / AI.Mock / CodeGen.CSharp / Gui が参照）
QuickER.CodeReverse.CSharp → Model, Provider    C# リバース取込のパーサ（Roslyn 構文解析のみ・コンパイル/アセンブリ読込なし。Roslyn 依存はこのプロジェクトへ隔離。Cli と CodeGen.UI が参照）
QuickER.Extensibility → Model, Provider    フィーチャーモジュール契約（IFeatureModule / FeatureToolbarItem / IErDiagramHost。WPF 非依存・net10.0）
QuickER.Gui.Common   → Gui.Abstractions    汎用 WPF 部品（MessageBoxDialogService / WpfFileDialogService / PasswordBoxBehavior / InformationDetailsDialog）

機能プロジェクト（WPF。Gui を参照しない＝Gui が参照する。ランチャー・アダプタは各機能側にあり、Gui は IErDiagramHost 実装とカタログだけを持つ）:
QuickER.Gui.Abstractions  アプリ汎用 UI サービス抽象（IDialogService / IFileDialogService。実装は Gui.Common / Gui 側）
QuickER.AI.UI      → AI, Gui.Abstractions, Platform    AI 共有 UI 部品（接続パネル・添付・ApiKeyStore・WpfUiDispatcher）
QuickER.AI.Chat    → AI, AI.UI, Extensibility, Gui.Abstractions, Gui.Common, Mcp, Mcp.Tools, Platform    チャット機能（ErDesign プロファイル・ER 設計ツール定義・チャットダイアログ・AiChatFeatureModule）
QuickER.AI.Mock    → AI, AI.UI, Extensibility, Gui.Abstractions, Gui.Common, CodeGen.CSharp, Provider, Mcp, Platform    モック生成機能（モックフォルダ方式のセッション・ツール・ストア・プレビュー一式・単一 HTML バンドル出力・モックプロジェクト生成〔Blazor 既定／WPF〕・MockGenerationFeatureModule）
QuickER.CodeGen.UI → CodeGen.CSharp, CodeReverse.CSharp, Extensibility, Gui.Abstractions, Gui.Common, Provider, Settings, Provider.SqlServer, Provider.Sqlite    C# コード生成／クエリ定義ダイアログ・CodeGenerationFeatureModule
QuickER.Db.UI      → Extensibility, Gui.Abstractions, Gui.Common, Provider, Platform, Provider.SqlServer, Provider.Sqlite    DB 取込／DB 同期ダイアログ・接続プロファイル（connections.json＋DPAPI シークレット）・DbToolsFeatureModule
```

各プロジェクトの設計判断と不変条件は、そのプロジェクトのフォルダの `CLAUDE.md` にある（下の索引から辿れる）。

## 生成テンプレートと固定フィクスチャ

- 生成テンプレートは `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/*.scriban` の部品 11 本（`_00_HeaderAttributes` 〜 `_10_Sync`）で、固定順の単純連結で 1 本へ戻す。**並べ替え・境界での行編集をせず、CRLF を保つ**（連結結果のバイト一致が生成コードのバイト一致の前提）。部品を変えるときは、部品ごとの注意と読む CLAUDE.md を → `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/CLAUDE.md`
- **テンプレート変更時は** `tests/QuickER.Tests/GeneratedFixture/` の固定フィクスチャを再生成する。各ドリフトテストが「再生成→差分ゼロ」と出力ファイル構成そのものを検証し、失敗メッセージに再生成手順がある。フィクスチャ（用途と主に使うテスト）：
  - GeneratedFixture.g.cs：SQL Server の全カバレッジ
  - PortableFixture.g.cs：方言可搬な EF Core 単独出力
  - SqlitePortableFixture.g.cs：SQLite 方言の QuickER 版 Repository＋EF Core の併存
  - MultiTargetPortableFixture.g.cs：sqlserver/sqlite マルチターゲットの QuickER 版 Repository・keyed DI（自己参照の nodes を含む＝IncludeGraph の edge-skip。マルチ辞書の多重定義で生成）
  - InMemoryFixture.g.cs：インメモリ Repository（自己参照の nodes・空の値の EditModel の表 blank_codes / blank_probes を含む）
  - QueryFixture.g.cs：名前付きクエリ入りの SQLite 方言＋EF Core＋InMemory（customers→orders→order_lines の 3 階層と自己参照の nodes）
  - RemoteContractFixture.g.cs：QueryFixture と同一図で GenerateRemoteContracts=true
  - RemoteServiceFixture：同図で GenerateRemoteServices=true。本体＋RemoteServiceFixture.RemoteServer.g.cs の 2 ファイル構成（サーバーファイルの実コンパイルで ASP.NET Core 参照を検証）
  - BinaryFixture：ExcludeUnboundedBinaryColumns=true の SQLite 方言＋EF Core＋InMemory＋リモート。本体＋BinaryFixture.RemoteServer.g.cs の 2 ファイル構成
  - SqlServerBinaryFixture.g.cs：BinaryFixture と同一図の SQL Server 方言版（QuickER 版 Repository 単独・図は BinaryFixtureDefinition.Build()。SqlServerBinaryColumnRuntimeTests）
  - BinaryVoFixture.g.cs：VO 有効 × 無制限バイナリ除外 × EditModel/Mapper の最小構成（vault_items。BinaryVoEditModelRuntimeTests）
  - ConcurrencyFixture：rowversion 列 × VO の SQL Server 方言＋EF Core＋InMemory＋リモート。本体＋ConcurrencyFixture.RemoteServer.g.cs の 2 ファイル構成（gadgets / gadget_notes・除外列 attachment・kits / kit_parts の代替キー。ConcurrencyVo{InMemory,SqlServer,EfCore,Remote}RuntimeTests・RemoteEfCoreUnboundedBinarySqlServerRuntimeTests・BackendParitySqlServerRuntimeTests）
  - MultiTargetRowVersionFixture.g.cs：rowversion 列 × sqlserver/sqlite マルチターゲット（sync_items。MultiTargetRowVersion{,Sqlite}RuntimeTests）
  - SqliteValueConversionFixture.g.cs：SQLite 方言 × VO の最小構成（time_probes。SqliteValueConversionRuntimeTests）
  - UniquenessSqlServerFixture.g.cs：QueryFixture の図からクエリを外した SQL Server 方言（UniquenessCheckSqlServerRuntimeTests・IncludeGraphSqlServerRuntimeTests）
  - SyncFixture：双方向同期 × マルチターゲット × リモートサービス。本体＋SyncFixture.RemoteServer.g.cs の 2 ファイル構成（sync_orders / sync_order_lines / 版なしの sync_notes / 独立の sync_markers。Sync{Sqlite,Http,SqlServer,SqlServerHttp}RuntimeTests）
  - SyncVoFixture：VO × 双方向同期の最小構成（rowversion 列は NOT NULL 宣言）。本体＋SyncVoFixture.RemoteServer.g.cs の 2 ファイル構成（SyncVoRuntimeTests）
  - SyncModelFixture：同期のモデルベーステストの図（model_folders / model_labels / model_items / model_events・すべて NO ACTION）。本体＋SyncModelFixture.RemoteServer.g.cs の 2 ファイル構成（SyncModelRuntimeTests）
  - IdentityColumnFixture：非主キーの採番列 × SQL Server 方言＋EF Core＋リモート。本体＋IdentityColumnFixture.RemoteServer.g.cs の 2 ファイル構成（{SqlServer,PostgreSql}IdentityColumnRuntimeTests・RemoteEfCoreGeneratedValuesRuntimeTests）
  - ComputedColumnFixture.g.cs：計算列 × sqlserver/sqlite マルチターゲット（{Sqlite,SqlServer}ComputedColumnRuntimeTests・SqliteComputedColumnDiagramDdlRuntimeTests）
  - TranslatorParityFixture.g.cs：マルチターゲット＋InMemory で式の翻訳・引用・キー照合を C# の意味論と突き合わせる図（予約語の列名 `group`・閉じ角括弧の列名 `x]y`・固定長の `nchar(10)`・予約語の表名 `order`・bigint の主キー×int の外部キー〔SQL Server はこの外部キー制約を作れないので `BuildWithoutCrossTypeForeignKey` の図で DDL を作る〕・キー列だけの表。TranslatorParityRuntimeTestsBase と TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests）
  - BackendParityFixture：実装先どうしの一致の SQLite 方言＋EF Core＋インメモリ＋リモート（VO・除外 blob。行バージョンは持たない）。本体＋BackendParityFixture.RemoteServer.g.cs の 2 ファイル構成（BackendParity*RuntimeTests）
  - BackendParityEfCoreOnlyFixture：同じ図の EF Core＋リモートだけ（EF Core 単独のスコープ）。本体＋BackendParityEfCoreOnlyFixture.RemoteServer.g.cs の 2 ファイル構成（実装先 RemoteEfCoreOnly のサーバー）
  - ドリフトテスト：GeneratedFixtureDriftTests / PortableFixtureDriftTests / SqlitePortableFixtureDriftTests / MultiTargetPortableFixtureDriftTests / InMemoryFixtureDriftTests / QueryFixtureDriftTests / RemoteContractFixtureDriftTests / RemoteServiceFixtureDriftTests / BinaryFixtureDriftTests / SqlServerBinaryFixtureDriftTests / BinaryVoFixtureDriftTests / ConcurrencyFixtureDriftTests / MultiTargetRowVersionFixtureDriftTests / SqliteValueConversionFixtureDriftTests / UniquenessSqlServerFixtureDriftTests / SyncFixtureDriftTests / SyncVoFixtureDriftTests / SyncModelFixtureDriftTests / ComputedColumnFixtureDriftTests / IdentityColumnFixtureDriftTests / TranslatorParityFixtureDriftTests / BackendParityFixtureDriftTests / BackendParityEfCoreOnlyFixtureDriftTests
  - 再生成は次の 1 コマンド（環境変数 `QUICKER_REGEN_FIXTURES` を立てるとドリフト検知と同一経路で上書き。実処理は FixtureDriftHarness）：

  ```powershell
  $env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter "FullyQualifiedName~Drift"; $env:QUICKER_REGEN_FIXTURES=$null
  ```

  - 再生成後は環境変数なしで同じテストを流し、緑を確かめる
  - 同じコマンドで、ランタイムパッケージ用ソース 7 本（`src/QuickER.Runtime*/Runtime*.g.cs`・RuntimePackageSourceDriftTests）、動くサンプルのチェックイン生成物（ec-order の `EcOrder.g.cs` / `EcOrder.g.md` / `EcOrder.sql`、ec-order-remote の `EcOrderRemote.g.cs` / `EcOrderRemote.RemoteServer.g.cs` / `EcOrderRemote.sql`・EcOrderSampleDriftTests / EcOrderRemoteSampleDriftTests＝実 `quicker generate` と同一経路で照合し、DDL は `-- Generated at:` 行だけを正規化）、公開 API の承認ファイル（`tests/QuickER.Tests/PublicApi/`）も再生成される。いずれも .gitattributes で eol=crlf 固定
  - テストのエンジンの種の指紋の記録 2 本（`tests/QuickER.Tests/TestSupport/engine-fingerprints.json`／`tests/QuickER.Tests.Integration/engine-fingerprints.json`・eol=lf）も同じ変数で書き直される（Integration の分は同じコマンドを `tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj` に向ける。各アセンブリの EngineFingerprintDriftTests が検証）。指紋は生成器の乱数の引き方の変化を検出するもので、実行の列は守らない（→ `tests/QuickER.Tests/TestSupport/CLAUDE.md`）
  - **再生成は承認であって検証ではない**：差分が出たら、先に「利用者を壊す変更か」「乱数の引き方を意図して変えたか」を判断する。テンプレートの変更がパッケージの公開 API 面を変えるときは「regen → 再ビルド → regen」の 2 回が要る（1 回目は旧アセンブリの面で承認してしまう）
- ランタイムパッケージ 7 csproj の依存集合と公開 API 面の固定は → `src/QuickER.Runtime/CLAUDE.md`
- 生成テンプレート・固定フィクスチャ・DDL 生成・`PasswordBoxBehavior` に触るときは、対応するテスト（DialectDdlTableTests / PasswordBoxBehaviorTests / CSharpCodeGenerationServiceTests / GeneratedFixtureDriftTests）を必ず実行する

## 設計判断と不変条件の索引

ビルド・型検査では検出できず、壊すと静かに回帰する規則を、領域ごとに 1 項目 1〜2 行で並べる。各行は「何を守るか：壊すと何が起きるか。ガード：主なテスト。→ 詳細の場所」。

### 横断の規則

- **書き出す文字列（DDL・同期スクリプト・型の表記・生成コード・エクスポート・ツールの応答）は書式のカルチャに依らず同じ。数値・日時は InvariantCulture、`IgnoreCase` の正規表現は `CultureInvariant` を併せて付け、`\d` は `ECMAScript` のときだけ使う**：sv-SE の負号や th-TH の仏暦、tr-TR の大文字小文字、全角数字が出力や判定を壊す。ガード：`CultureInvariantDialectOutputTests`・`DialectDdlTableTests`（全行に掛かる自動のカルチャの照合）・`SourceRuleTests`（規則 `regex-ignore-case-culture`・`regex-digit-classes`）・`SourceRuleTests`（規則 `numeric-interpolation-culture`）・`GeneratedCodeCultureAnalyzerTests`。→ `tests/QuickER.Tests/TestSupport/CLAUDE.md`「カルチャのガード」

### 名前と制約名
- **図に保存する名前の同一性は `SchemaNames`（Model）だけが決める（前後の空白を除き `OrdinalIgnoreCase`。綴りは書き換えない。キーは `SchemaNames.Key`）**：入口ごとに比較や `Trim`・`ToLowerInvariant` で書くと、Unicode の特殊な文字で表・列の組と答えが割れる。ガード：`SourceRuleTests`（規則 `schema-name-comparisons`）・`SourceRuleTests`（規則 `sync-name-comparison-tokens`）・`SchemaNamesKeyTests`・`NameFuzzCompilationTests`。→ `src/QuickER.Model/CLAUDE.md`「名前の同一性（SchemaNames）」
- **制約名の安全化と名前の無い制約の既定名は `ConstraintNames` だけで組む**：写すと前後に空白のあるテーブル名で DDL と同期の制約名が食い違い、存在しない制約を外そうとして失敗する。ガード：`SourceRuleTests`（規則 `constraint-name-safe-rule-copies`・`constraint-name-default-compositions`）・`DialectDdlTableTests`（行 `ddl-*-trimmed-table`・`sync-*-trimmed-table`）・`ConstraintNameCreationPathTests`。→ `src/QuickER.Model/CLAUDE.md`「制約名の組み立て（ConstraintNames）」
- **既定の制約名は識別子の長さの上限に収め、上限は省略できない引数で受ける（経路は 2 つだけ）**：省略可能にすると黙って縮めない呼び出しが残り、DDL の適用が断られるか PostgreSQL で別の名前に切り詰められる。ガード：`IdentifierLengthLimitDeclarationTests`・`SourceRuleTests`（規則 `model-dialect-lookups`）・`LongDefaultConstraintNameTests`・`DialectDdlTableTests`（行 `ddl-long-default-names-are-shortened`・`sync-add-table-long-default-names-are-shortened`）・`SyncPlanExpectationRows` の行 `diff-long-default-*`。→ `src/QuickER.Model/CLAUDE.md`「識別子の長さの上限」
- **名前の入力・ツール・方言切替は、受け付けない名前を入口と同じ判定（`DescribeViolation`）で知らせる**：判定を写すと入口ごとに食い違う。ガード：`NameLimitStatusWarningTests`・`ToolScenarioTests`（行 `name-limit-*`）・`NameLimitNotesTests`。→ `src/QuickER.Model/CLAUDE.md`「名前の上限の通知（NameLimitNotes）」

### モデル

- **意味と視覚の分離**：`Entity`（Model）は座標・色を持たない。視覚状態は `EntityViewModel` と `EntityLayout`（Document）が持ち、エクスポータ・生成器は意味モデル `ErDiagram` だけを消費する
- **一意制約の正本は `Entity.UniqueConstraints`。未設定名は `SynthesizeName` で合成し、ぶつかる名前には番号を足す**：ぶつかると SQL Server・PostgreSQL・Oracle・MySQL で DDL・同期が名前の重複で失敗する。ガード：`ConstraintNamesTests`・`SourceRuleTests`（規則 `unique-constraint-synthesize-name-calls`）・`UniqueConstraintResolvedNameLimitTests`・`DialectDdlTableTests`（行 `ddl-unique-unnamed-synthesized-name-is-safe`）。→ `src/QuickER.Model/CLAUDE.md`「一意制約」
- **列が NULL を持てるか（主キーの列は NULL 許容の印に依らず NOT NULL）は `ColumnNullability.AllowsNull` だけが決め、`IsPrimaryKey` と `IsNullable` を 1 つの式で組んで写さない**：DDL・同期スクリプト・差分・計画・C# 生成・GUI の読込の写しが割れると、DDL が NOT NULL で作った列に偽の差分が出る。ガード：`SourceRuleTests`（規則 `column-nullability-copies`）・`DialectDdlTableTests`・`SchemaDiffServiceTests`。→ `src/QuickER.Model/CLAUDE.md`「列の NULL 許容（ColumnNullability）」
- **主キーの実効順は `Entity.GetPrimaryKeyColumnsInOrder()` だけが決める**：素の `Columns.Where(IsPrimaryKey)` はビルドもテストも緑のまま、取込図でだけ列順へ退行する。ガード：`EntityPrimaryKeyOrderTests`・`DocumentFormatTests`（行 `*-primary-key-*`）・`UndoOperationTests`（行 `primary-key-*`）。→ `src/QuickER.Model/CLAUDE.md`「主キーの実効順」
- **主キーの順序を生成物・DBML へ載せるか告知するかの判定は `GetReorderedPrimaryKeyColumnNames()` だけ**：独自に変えると順序を明示していない図まで出力が変わる。ガード：`CSharpReversePrimaryKeyOrderTests`・`FormatRoundTripTests`（形の行 `dbml-reordered-primary-key`・`dbml-primary-key-in-column-order-*`・`mermaid-primary-key-order-*`・`csharp-reordered-primary-key`・`csharp-description-and-primary-key-order`／規則の行 `dbml-rule-primary-key-*`・`csharp-rule-primary-key-order-*`）。→ `src/QuickER.Model/CLAUDE.md`「主キーの順序の出力判定」
- **リレーションの列対応の正本は `Relationship.ColumnPairs`（推測フォールバックなし）**：列ペア 0 件や解決不能な列の FK は DDL・差分の対象外として明示スキップする。ガード：`ForeignKeyColumnResolverTests`・`FormatRoundTripTests`（形の行 `dbml-unnamed-relationships`／規則の行 `dbml-rule-same-pair-twice-without-names-second-gets-unique-name`・`dbml-rule-single-unnamed-ref-stays-unnamed`・`dbml-rule-named-ref*`・`csharp-rule-two-unnamed-references-get-distinct-names-in-ddl`・`csharp-rule-single-unnamed-reference-stays-unnamed`・`csharp-rule-explicit-names-are-kept`／マージの行 `merge-csharp-current-with-duplicate-unnamed-references-gets-distinct-names`・`merge-csharp-names-supplied-by-current-diagram-are-kept`）。→ `src/QuickER.Model/CLAUDE.md`「リレーションの列ペア」

### 方言の宣言
- **方言の違いは方言の宣言だけが持ち、宣言は既定値を持たない**：既定値を足すと能力を足したとき書き忘れた方言が旧来の値で黙って動く。ガード：`SyncDialectCapabilitiesNoDefaultsTests`・`SourceRuleTests`（規則 `sync-capabilities-initializers`・`dialect-name-literals`）・`UnsupportedCoverageTests`。→ `src/QuickER.Dialects/CLAUDE.md`「宣言は既定値を持たない」
- **図のテーブル名を最初の `.` でスキーマと表に分ける規則は `SchemaQualifiedName` だけが持つ**：別々の規則で分けると `a.b.c` のような名前で DDL と CRUD が別の表を指す。ガード：`SourceRuleTests`（規則 `schema-qualified-name-dot-splits`）・`DialectIdentifierTableTests`（行 `split-*`・`runtime-quote-*`）。→ `src/QuickER.Dialects/CLAUDE.md`「スキーマ修飾名の分け方（SchemaQualifiedName）」

### 図ファイル
- **Version 1 のまま足すフィールドは、読み手が欠落を無害に扱える形に限り、無害でなければ Version を上げる**：古い版で保存すると新しいフィールドが消える。ガードなし（設計判断）。→ `src/QuickER.Document/CLAUDE.md`「図ファイル形式の前方互換」
- **Id 重複と必須の文字列の欠落の検査は `JsonStorageService.TryLoad` の 1 箇所で拒否だけを行い、修復しない**：呼び出し側に散らすと起動時の復元が検証の外に残り、最悪経路でクラッシュする。ガード：`DocumentFormatTests`（行 `tryload-duplicate-*`・`tryload-null-*`）・`DocumentFileTests`（乱数の手の列・不変条件 (5)）。→ `src/QuickER.Document/CLAUDE.md`「図ファイルの読込検査」

### 取込

- **プロバイダの対称性**：5 方言は SchemaImporter／`DdlGeneratorBase` 派生／型マップ／実 DB 統合テストの同じ構造を持ち、インポータの基底クラス化は意図的に見送る。共有部品は `ForeignKeyRelationshipBuilder`・`UniqueConstraintImportBuilder`・`SchemaTableEntry`。→ `src/QuickER.Provider/CLAUDE.md`「プロバイダの対称性」
- **取込は宣言どおりだけを持ち帰り、持ち帰れなかったものは取込警告で告げる**：文言は `SchemaImportWarningText` だけが持ち、表示側は飾りだけ。黙ると後で DDL・同期が図全体ごと止まる。ガード：`SchemaImportWarningTextTests`・`DialectImportWarningTableTests`。取込の範囲の正本は `docs/database.md`（.ja）。→ `src/QuickER.Provider/CLAUDE.md`「取込警告」
- **大文字小文字だけ違う表名・列名は後着を捨て、全解決点を序数一致で引く**：捨てるだけでは採用した表へ列・制約が混ざる。ガード：`{PostgreSql,Oracle,MySql,SqlServer,Sqlite}ImportFidelityIntegrationTests`・`SchemaTableEntryNameTests`・`NameIdentitySqliteImportFuzzTests`。→ `src/QuickER.Provider/CLAUDE.md`「大文字小文字だけ違うテーブル名」
- **最初の `.` で分けて元へ戻らない表名は取り込まない**：別の表として読まれる。判定は `SchemaImportTableNames` の 1 つだけ。ガード：`SchemaImportTableNamesTests`・5 方言の `*ImportFidelityIntegrationTests.Import_DottedTableName_*`。→ `src/QuickER.Provider/CLAUDE.md`「ドットを含むテーブル名」
- **出せない型表記は取込完了時に 5 方言すべてで告げる**：黙って入れると後の DDL・同期が全テーブルごと止まる。ガード：`PostgreSqlImportFidelityIntegrationTests.Import_ColumnTypeNotEmittable_IsReported`・`PostgreSqlSchemaImporterTests`。→ `src/QuickER.Provider/CLAUDE.md`「出せない型表記の告知」
- **取り込んだ型表記はその方言の DDL として再適用できる（不動点）**：再適用できない表記はその図を DDL にも同期にも使えなくする。ガード：5 方言の `*DdlRoundTripIntegrationTests.ImportedSchema_RegeneratedDdl_CanBeReapplied`・`SqlServerDdlRoundTripIntegrationTests`・`OracleDdlRoundTripIntegrationTests`。→ `src/QuickER.Provider/CLAUDE.md`「取り込んだ表記の再適用」
- **取込・同期のコマンドは `DbCommands.Create` で作る**：生成箇所ごとに書くとタイムアウトが静かに漏れる。ガード：`SourceRuleTests`（規則 `schema-command-bypasses`・`schema-command-helper-uses`）。→ `src/QuickER.Provider/CLAUDE.md`「コマンドのタイムアウト」
- **`DbSslMode` の未指定はビルダーへ代入しないことで表し、保存は名前で行う**：既定値の代入で既存の接続文字列が変わり、整数保存では列挙の追加で無言の降格になる。ガード：`DbSslModeConnectionStringTests`・`ProfileStoreTests`（行 `tls-*`）。→ `src/QuickER.Provider/CLAUDE.md`「接続文字列の TLS 要求水準」
- **方言ごとの取込の事実（PG の `pg_attribute`・Oracle の ENABLED の制約と除外する表・SQLite の PRAGMA と主キーの補正・SQL Server のテンポラル表・MySQL の範囲外の外部キー）**：外すと列ゼロ・DB に無い保証・取込全体の失敗・誤結合が起きる。ガード：`OracleImportFidelityIntegrationTests`・`SqliteImportFidelityIntegrationTests`・`MySqlImportFidelityIntegrationTests`・`SqlServerImportFidelityIntegrationTests`・`PostGisImportIntegrationTests`。→ `src/QuickER.Provider.PostgreSql/CLAUDE.md`「取込」ほか各方言の「取込」

### 型表記

- **型表記の比較は方言の `*TypeNotation.Normalize`（取込の表記）へ寄せ、差分・同期は `SyncDialectCapabilities.AreSameType` を通す**：素の比較だと同期しても消えない ALTER COLUMN が出る。ガード：`{SqlServer,PostgreSql,MySql,Oracle}TypeNotationIntegrationTests`・`DialectTypeTableTests`（正規化と同じ型の判定の行）。→ `src/QuickER.Provider/CLAUDE.md`「型表記の比較は取込の表記へ寄せる」
- **`SqlTypeText` は構造的なホワイトリストで、括弧引数は数値・語・`*`、列定義句の密輸を断り、値リスト型に長さの上限を掛けない**：キーワード許可集合や数値限定にすると実在の型の 1 列で図全体が止まる。ガード：`DialectTypeTableTests`（安全性の行）・`SqlTypeTextTests`・`PostGisImportIntegrationTests`。→ `src/QuickER.Provider/CLAUDE.md`「SqlTypeText の安全パターン」
- **`TryFormat` は書き出せない型（負のスケール・長さ無し・精度なし・方言の上限超え）を false で返す**：true だと方言切替が適用で失敗するか意味の違う型を黙って作る。ガード：`DialectTypeTableTests`（書き出しの行と上限の対）。→ `src/QuickER.Provider/CLAUDE.md`「TryFormat が書き出さない型」
- **クエリのパラメータには列の制約を当てない（可変長の長さ無しは無制限・固定長は入れない）**：巻き込むと動いているクエリが生成時エラーへ落ちる。ガード：`LengthlessTypeTokenQueryGenerationTests`・`DialectTypeTableTests`（パラメータの型解決の行）・`FormatRoundTripTests`（規則の行 `csharp-rule-lengthless-token-*`・`csharp-rule-token-with-length-*`）。→ `src/QuickER.Provider/CLAUDE.md`「クエリのパラメータの型解決」
- **スケールの符号は 3 箇所同時に許し、型マッパーは負のスケールを読まない**：片肺だと取り込んだ列のトークンが読み戻せない。ガード：`DialectTypeTableTests`（負のスケールの行）・`CodegenCaseTests` の行 `code-negative-scale-column-is-decimal-property`。→ `src/QuickER.Provider/CLAUDE.md`「スケールの符号」
- **精度なしの Decimal を 38 桁へ読み替えるのは方言切替（`DiagramTypeConverter`）だけ**：`TryFormat` へ移すとトークン・リバース・パラメータまで黙って 38 桁へ化ける。ガード：`DialectTypeTableTests`（方言切替の行）・`DiagramTypeConverterTests`・`MainViewModelTargetDbmsTests`・`CSharpReverseBareDecimalTests`・`FormatRoundTripTests`（形の行 `csharp-oracle-precisionless-*`・`csharp-bare-decimal-*`／規則の行 `csharp-rule-precisionless-scaled-token-*`）。→ `src/QuickER.Provider/CLAUDE.md`「方言切替の精度の読み替え」
- **型マッパーはドライバが返す CLR 型へ写す（PG の時間帯の語・timetz・inet、Oracle の LOCAL TIME ZONE と単位語、SQLite の REAL、MySQL の tinytext、SQL Server の float(n)）**：違う型だと読み取りが `InvalidCastException` で表ごと使えない。ガード：`DialectTypeTableTests`（マッパーの行）・各方言の `*DialectTypeRealDbTests`（同じ行を実 DB で流し、ドライバの `GetFieldType` と比べる）・`CodegenCaseTests` の行 `code-fallback-type-column-is-string-property`。→ `src/QuickER.Provider/CLAUDE.md`「型マッパーはドライバの CLR 型へ写す」
- **方言の型の上限と取込の表記**：上限の値・正規化の規則は方言の宣言。ガード：`DialectTypeTableTests`（上限の対・網羅の照合）・`{MySql,PostgreSql}DialectTypeRealDbTests`（上限の対を実 DB で適用して取り込み直す）。→ 各方言の CLAUDE.md「型表記」

### 差分と同期

- **同期は差分 → 方言中立の計画 → 方言の SQL の 2 段で、方言差は既定値の無い `SyncDialectCapabilities` で宣言する**：既定値があると足した能力を書き忘れた方言が黙って旧来の値で動く。ガード：`CapabilityTreatmentParityTests`。→ `src/QuickER.Provider/CLAUDE.md`「2 段構造と方言の宣言」
- **生成 SQL の固定文は英語が正本**：ガード：`DialectDdlTableTests`（全行に掛かる自動の英語の照合・行 `ddl-table-fixed-text-english`・`sync-*-fixed-text-english`）。→ `src/QuickER.Provider/CLAUDE.md`「生成 SQL の固定文」
- **制約名の安全化と既定名の正本は Model の `ConstraintNames`、識別子は 5 方言対称に分割クォートする**：方言側に写しを置くと DDL と同期の名前が割れる。ガード：`DialectIdentifierTableTests`（行 `quote-dot-*`）・`SqlServerDefaultSchemaDescriptionIntegrationTests`。→ `src/QuickER.Provider/CLAUDE.md`「制約名の正本と識別子の分割クォート」
- **SQL の信頼境界（動的 SQL の二段エスケープ・実行時に読んだ名前は DB のクォート関数・逆引きは表の所有者で絞る・`--` コメントは `SqlComment.Sanitize`・型は `SqlTypeText`・名前の入口検証・合成 PK 名の `Escape`・PG のドルタグ）**：外すと名前 1 つで同期が壊れるか別の表の制約を外す。ガード：`SourceRuleTests`（規則 `sql-comment-unsanitized-holes`）・`SqlNameTextTests`・`DialectDdlTableTests`（行 `ddl-primary-key-name-escaped`・`sync-*-primary-key-name-escaped`・`ddl-*-name-with-newline-is-rejected`・`sync-*-name-with-newline-is-rejected`）。→ `src/QuickER.Provider/CLAUDE.md`「SQL の信頼境界」（C# 側は `src/QuickER.CodeGen.CSharp/CLAUDE.md`「名前・型文字列の信頼境界（C# 生成側）」）
- **利用者が付けた名前は縮めずに DDL・同期の入口で上限と表せない文字を断る**：名前を拾い忘れた種類だけが黙って DB に届く。ガード：`OutputIdentifierLimitGuardTests`・`IdentifierLimitEntryTests`・`NameFuzzDdlRoundTripTestsBase`・`MySqlIdentifierLengthLimitIntegrationTests`。→ `src/QuickER.Provider/CLAUDE.md`「利用者が付けた名前の上限」
- **差分は大文字小文字だけ違う表・列を live の綴りへ揃えた複製で組む**：揃えないと引用識別子の方言でスクリプトが相手を指せない。ガード：`SchemaDiffServiceTests.Items_CarryDiagramObjects`・`SchemaDiffServiceMatchingCountTests`。→ `src/QuickER.Provider/CLAUDE.md`「差分の名前の綴り」
- **複合外部キーの列ペアは差分で並びを比べず、DDL・同期は参照先の（同期後の）キーの並びで出す**：並びのまま付け直すと失敗し、差分が並びを比べると永久に食い違う。ガード：`ForeignKeyColumnOrderTests`・`DialectDdlTableTests`（行 `ddl-fk-composite-reordered-to-referenced-key`・`sync-other-add-composite-fk-reordered-to-referenced-key`）・`SyncPlannerTests`・`SchemaDiffItemTests`。→ `src/QuickER.Provider/CLAUDE.md`「複合外部キーの列ペアの並び」
- **外部キーの作り直しは `AlterForeignKey` 1 項目へ畳み、計画の入口で展開する。差分は外部キーを本数で比べる**：2 項目のままだと既定の追加だけが名前の重複で失敗する。→ `src/QuickER.Provider/CLAUDE.md`「外部キーの作り直しの畳み込み」
- **一意制約の差分は名前の段 → 列集合の段で組み、同じ実効名の追加と削除は `AlterUniqueConstraint` へ畳んで `ExpandAlters` で展開する**：列集合だけで組むと名前を保った制約の追加が同じ名前で失敗し、展開を外すと作り直しが黙って消える。→ `src/QuickER.Provider/CLAUDE.md`「一意制約の差分」
- **CREATE TABLE の制約行は `TableConstraintLineBuilder` が単一正本、DB の一意制約の照合キーは `UniqueConstraintKey`（表＋名前＋列集合）**：再インライン化すると同期で作った表だけ UNIQUE が無くなり、キーから名前を外すと双子の一意制約の片方が残って列の変更が止まる。→ `src/QuickER.Provider/CLAUDE.md`「一意制約の照合キーと付け直し」
- **主キーの同期は 3 フェーズで、旧主キーに支えられた FK を外して付け直し、新しい主キー列は NOT NULL にする**：ガード：期待の行 `plan-implicit-fk-*`。→ `src/QuickER.Provider/CLAUDE.md`「主キーの同期」
- **候補キーは列の集合の完全一致で証明し、証明できない付け直しは警告して暗黙コミットの方言では最後へ回す**：並べないと警告していない外部キーまで巻き添えで消える。→ `src/QuickER.Provider/CLAUDE.md`「候補キーの証明」
- **外部キーの暗黙の外し直しは付け直せない外部キーを外したまま残さない（型の食い違いは外さず名指し・必ず断られる文は先頭へ・端が消える外部キーは外すだけ）**：暗黙コミットの方言で外部キーが消えたまま残る。ガード：`SyncPlannerTests`・`DialectDdlTableTests`（行 `ddl-fk-*-note`）・`MySqlSyncScriptBuilderTests`。→ `src/QuickER.Provider/CLAUDE.md`「外部キーの暗黙の外し直し」
- **選んでいない変更を前提にする項目は外して名指しし、依存先を自動で選ばない**：そのまま実行すると存在しない表・列を参照して失敗するか部分的な定義のまま成功する。ガード：`SchemaSyncWarningTextTests`。→ `src/QuickER.Provider/CLAUDE.md`「選んでいない変更を前提にする項目」
- **構成列の削除は方言に依らず同じ形で扱い、列が 1 本も残らない表の列の削除は外す**：方言ごとに断られる・黙って消える・縮むと結果が割れる。ガード：`SyncPlannerDropMemberColumnTests.DropColumnsOnly_DropsUniqueConstraintBeforeColumnInScript`・`SchemaSyncWarningTextTests`。→ `src/QuickER.Provider/CLAUDE.md`「構成列の削除と列が残らない表」
- **列の無い表は `SqlTableShape` の 1 つの判定で、DDL は入口で図全体を止め、同期は計画が表の追加を外して名指しし、同期スクリプトの入口も断る（5 方言同じ）**：通すと 4 方言が `CREATE TABLE t ( );` の構文エラーで断り、PostgreSQL だけ取り込み直せない表ができる。ガード：`DialectDdlTableTests`（行 `ddl-table-without-columns-is-rejected`・`sync-add-table-without-columns-*`）・期待の行 `plan-add-table-without-columns-is-excluded-with-warning`・`SchemaSyncWarningTextTests`。→ `src/QuickER.Provider/CLAUDE.md`「列の無い表」
- **同期スクリプトのセクションは固定順で、表の削除を追加より前に置く**：追加が先だと引き継いだ一意制約の名前がぶつかる。→ `src/QuickER.Provider/CLAUDE.md`「セクションの順序」
- **説明の空欄の判定は `DescriptionText` だけ、BMP の外の文字は方言の宣言で扱う**：経路ごとに判定すると適用直後に偽の差分が出る。ガード：`BlankDescriptionTests`・`DescriptionStoredTextTests`・`DialectDdlTableTests`（行 `ddl-description-*`・`sync-other-description-*`）・期待の行 `diff-blank-description-*`・`diff-description-stored-form-*`・`plan-description-characters-replaced-*`。→ `src/QuickER.Provider/CLAUDE.md`「説明の空欄と表せない文字」
- **方言が表せない参照アクションは比べる前に実効値へ畳む**：取り込み直すたびに作り直しの差分が出る。ガード：`OracleTypeNotationIntegrationTests.UnsupportedReferentialActions_LeaveNoForeignKeyDiff`・期待の行 `realdb-mysql-set-default-is-omitted-and-leaves-no-fk-diff`・`DialectDdlTableTests`（行 `ddl-fk-on-delete-set-default-omitted-with-note-oracle`・`ddl-fk-set-default-omitted-with-note-mysql`）。→ `src/QuickER.Provider/CLAUDE.md`「参照アクションの畳み込み」
- **計算列の NULL 許容は差分に出さず、主キーと同じ列集合の一意制約は持てない方言で比較から外して DDL で省く**：比べると意味のない ALTER COLUMN や永久に消えない差分が出る。ガード：`DialectDdlTableTests`（行 `ddl-unique-on-primary-key-columns-omitted-where-unsupported`）・`OracleDdlRoundTripIntegrationTests.UniqueConstraintOnPrimaryKey_IsOmittedAndApplies`。→ `src/QuickER.Provider/CLAUDE.md`「計算列と主キーと同じ列集合の一意制約」
- **列の変更は図に無い列属性をカタログから読み直して保ち、既定値のある列の型の種類の変更は警告する**：保たないと照合順序・既定値・生成列の式が黙って消える。ガード：`MySqlColumnAttributeSyncIntegrationTests`・`PostgreSqlColumnAttributeSyncIntegrationTests`・`ImportSyncTests`（行 `sync-dialog-type-kind-change-with-default-warns-from-imported-defaults`）・`OracleImportFidelityIntegrationTests.Import_EmptyStringAndParenthesizedNullDefaults_AreNotDefaults`。→ `src/QuickER.Provider/CLAUDE.md`「列の変更が保つ属性」
- **SQLite の再構築の合成は live の列オブジェクトを保ち、失う属性を警告し、写す列が無くても行を保つ**：図の列へ差し替えると一意制約が消え主キーの順序が変わる。ガード：`TableRebuildAttributeDetectorTests`・`SqliteSyncScriptBuilderTests`。→ `src/QuickER.Provider/CLAUDE.md`「テーブル再構築の合成」
- **SQLite の同期実行器は外部キー違反とビューの破損を前後で比べる。切り替えた PRAGMA は接続をプールしないので接続と一緒に消える**：DB 全体で判定すると既存の孤児行で無関係な同期が失敗する。ガード：`SqliteSchemaSyncIntegrationTests`・`SourceRuleTests`（規則 `sqlite-file-connection-pooling-off`）。→ `src/QuickER.Provider.Sqlite/CLAUDE.md`「同期実行器」
- **QuickER が SQLite のファイルを開く接続（新規作成・取込・同期）は `SqliteFileConnections.Create` だけが作り、プールしない**：プールした接続は閉じた後もファイルを掴み、操作の直後に利用者がファイルを移動・削除すると Windows で共有違反になる。ガード：`SourceRuleTests`（規則 `sqlite-file-connection-creations`・`sqlite-file-connection-pooling-off`）・`ConnectionDialogTests`（組の不変条件 12）・`SyncPlanFuzzTestsBase.ExpectationRows_MatchOnRealDatabase`（SQLite の汎用の表明 6）。→ `src/QuickER.Provider.Sqlite/CLAUDE.md`「ファイルを掴まない接続」

### C# 生成

- **CodeGen.CSharp は DB 非依存（型解決はプロバイダ側・`CSharpTypeInfo` の辞書を入力に受ける）**：Provider が CodeGen.CSharp を参照する契約が崩れる。→ `src/QuickER.CodeGen.CSharp/CLAUDE.md`「DB 非依存」
- **生成の単発オプションのケースは「既定へ戻した比較用」と照合して空振りを検出する**：オプションが出力を変えない空振りが見えなくなる。ガード：`CodegenCaseTests` の行 `compile-single-option-*`（関係 OptionEffect。対照の「同じオプションどうしは同じ」も同じ関係が見る）。→ `src/QuickER.CodeGen.CSharp/CLAUDE.md`「生成の単発オプションのケース」
- **DB が値を作る列（rowversion・計算列・採番列）は別々のマーカーで書き込みから外し、共用しない**：共用すると版の解決が計算列を版と誤認し、主キーの採番列を外すとキー無しの INSERT になる。ガード：`StoreGeneratedCapabilityGenerationTests`・`CodegenCaseTests` の行 `code-computed-column-*`・`CodegenCaseTests` の行 `code-identity-column-*`。→ `src/QuickER.CodeGen.CSharp/CLAUDE.md`「DB が値を作る列の書き込み除外」
- **rowversion の書き込み除外は `dialect_assigns_store_generated` ＋ `repositories` のときだけ**：方言を持たないスコープでフラグを単独で読むと意味が崩れる。ガード：`StoreGeneratedCapabilityGenerationTests`。→ 同上「行バージョン列（store-generated）」
- **計算列は `[ComputedColumn]`・採番列は `[IdentityColumn]` で方言に依らず外し、DDL・同期の注記は単一の正本から出す**：注記が方言名の分岐に散る。ガード：`DialectDdlTableTests`（行 `ddl-computed-column-*`・`ddl-identity-column-*`・`sync-*-computed-column-*`・`sync-*-identity-column-*` と扱いの表の自動の照合）。→ 同上「計算列・生成列」「DB が値を拒む採番列」
- **マルチターゲット × rowversion は型統合・NULL 許容化・トークン不刻印で成り立つ**：版ガードがリバースで黙って消える。ガード：`MultiTargetRowVersionRuntimeTests`・`MultiTargetRowVersionSqliteRuntimeTests`。→ 同上「マルチターゲット × rowversion」
- **FK 列は親側列の値オブジェクト型を共有し、列名→VO 型の独自解決を下流に作らない**：EF Core のモデル検証が FK と PK の CLR 型不一致で落ちる。ガード：`CodegenCaseTests` の行 `code-vo-foreign-key-*`・`model-vo-foreign-key-*`・`probe-efcore-model-vo-foreign-key-name-mismatch`。→ 同上「FK 列の値オブジェクト型統一」
- **`[Column]` は常時付与で、生成ランタイムの列判定は `[Column]` を許可リストとして読む**：手書きプロパティが実在しない SQL 列に化ける。ガード：`ExtensionShimAcceptanceTests`。→ 同上「Column 属性は常時付与」
- **同義の型表記は verbatim 併記（`NativeType`）で保存し、大小だけの違いは刻まずマージ取込が現在図の綴りへ戻す（`ReverseMergePostProcessor.ApplyTypeSpellings`）**：リバースで型表記が変わり、次の同期が全列へ `ALTER COLUMN` を出す／SQLite の図で取り込むたびに `int` が `INT` へ変わり置換の確認が出る。ガード：`CSharpReverseVerbatimTypeTests`・`FormatRoundTripTests`（形の行 `csharp-verbatim-type-*`・マージの表明で `dbml-long-default-name-sqlite`／規則の行 `csharp-rule-verbatim-*`）・`CodegenCaseTests` の行 `code-portable-fixture-identical-across-postgresql-mysql-oracle`。→ 同上「同義の型表記の保存（verbatim 併記）」
- **層別フォルダ出力は層判定の正本 `LayerOf`・名前空間の出所の記録・固定 infra の public 化で成り立つ**：層分けの ON / OFF で生成テキストが食い違う。ガード：`LayeredOutputTests`・`CodegenCaseTests` の行 `compile-layered-*`・`CSharpCodeGenerationServiceTests`。→ 同上「層別フォルダ出力と出力先」
- **`CodeSubdirectory` は名前空間に追随せず、合流点は `GeneratedFilePlanner.Plan` の出口 1 箇所**：非分割で素通りするか、手書き partial が `.Generated.` 名前空間になる。ガード：`CodeSubdirectoryTests`。→ 同上「出力先のサブフォルダ」
- **ランタイム配布は 7 パッケージ（`QuickER.Runtime` / `.SqlServer` / `.Sqlite` / `.EntityFrameworkCore` / `.InMemory` / `.AspNetCore` / `.Sync`）と分割生成が 1:1 対称**：パッケージの型集合と Runtime 系ファイルがずれる。ガード：`CodegenCaseTests` の行 `code-split-runtime-files-hold-no-schema-dependent-types`・`CodegenCaseTests` の行 `code-split-sync-fixed-engine-and-per-entity-code`。→ 同上「ランタイム配布」
- **リモート契約生成は契約が無い構成を生成時エラーで止める**：診断ゼロで何も生成されない。ガード：`RemoteContractRuntimeTests`。→ 同上「リモート契約生成」
- **QuickER 版 Repository と EF Core は排他の単独出力で、ADO 依存が方言間・EF Core 単独に漏れない**：ガード：`MultiTargetRepositoryRuntimeTests`・`MultiTargetRepositorySqliteKeyedResolutionTests`。→ 同上「DB アクセスの排他と単独出力」
- **固定 infra は図の主キーの形に依らずオプションだけで出し、DI の登録のローカル関数は Repository があるときだけ出す**：主キーが 1 列の表が無い図で共通契約だけが抜け、生成の診断がエラー 0 のまま QuickER 版・EF Core 版・インメモリのどれもコンパイルできない。ガード：`CodegenCaseTests` の行 `compile-composite-key-only-all-surfaces` / `compile-composite-key-only-efcore` / `compile-keyless-only-all-surfaces` ほか・網羅の配列の水準 `composite-only` / `keyless-only`。→ 同上「Repository を作れる表が無い図」
- **C# 生成は主キーの列を図の NULL 許容に依らず NOT NULL とみなす（判定は Model の `ColumnNullability.AllowsNull`、読み替えは生成の入口の 1 か所）**：外すと同期の記述子がコンパイルできず（CS0266 / CS8603）、インメモリのシードが既定の構成で起動時に投げる。どちらも診断はエラー 0。ガード：`CodegenCaseTests` の行 `compile-nullable-primary-key-sync` / `compile-nullable-primary-key-sync-string-value-objects` / `probe-inmemory-seed-nullable-primary-key` / `relation-nullable-primary-key-all-surfaces`（関係 PrimaryKeyNotNull を併せて掛ける）・網羅の配列の水準 `int-nullable`。→ 同上「主キーの列の NULL 許容」
- **生成 `.g.cs` のヘッダー（版・内容ハッシュ）は生成サービスの 1 箇所で刻み、正規化は `GeneratedContentHash` だけが持つ**：生成直後のファイルが「手編集」と判定される。ガード：`GeneratedContentHashTests`・`GeneratedHeaderStampTests`・`GenerateOverwriteGuardTests`。→ 同上「生成コードのヘッダー」
- **新しい行の EditModel は値オブジェクトでない列の空文字列・空バイト配列を未入力として読む**：空欄の新しい行が必須チェックを素通りする。ガード：`EditModelBlankInputTests`・`EditModelScenarioTests`。→ 同上「新しい行の空の値」
- **Mapper の 4 分岐は値型のとき `.Value` で取り出す（参照型へ広げない）**：CS0266 でコンパイルできない。ガード：`CodegenCaseTests` の行 `compile-*`（共通図の値型の計算列・採番列）・`CodegenCaseTests` の行 `exec-computed-column-not-null-value-type-mapper-unwraps-nullable`。→ 同上「Mapper の 4 分岐の Value」
- **配列列と inet の列は EditModel・Mapper・VO に載せない（`IsExcludedFromEditModel`）**：既定の構成で CS0029、VO の等値が参照比較になる。ガード：`CodegenCaseTests` の行 `code-postgresql-array-columns-*`・`code-postgresql-network-types-inet-excluded-from-editmodel`。→ 同上「配列列と inet の列」
- **インメモリのシード値は列の型でなく役割から作り、主キーが 1 列でない表とそれを直接・間接に参照する表には入れない**：一意制約が重複したストアができて更新が必ず失敗し、複合主キーの表で既定の `AddGeneratedInMemoryRepositories()` が起動時に投げる。ガード：`InMemorySampleSeedExecutionTests`・`CodegenCaseTests` の行 `probe-inmemory-seed-composite-key`。→ 同上「インメモリのシード値」
- **GuidKey 値オブジェクトの長さ検証は素通しした MaxLength が駆動し、36 未満は Warning（Error にしない）**：検証がスタックから消える。ガード：`CodegenCaseTests` の行 `probe-guidkey-create-generates-unique-guid`。→ 同上「GuidKey 値オブジェクトの長さ検証」
- **生成型名の名簿は実際の出力と一致し続ける**：DTO の型名がコンパイルできない出力になる。ガード：`GeneratedTypeNameDriftTests`。→ 同上「型名の名簿」
- **テーブル・列から決まる型名のぶつかりは `GeneratedTypeNames.Analyze` が名指しで止める（型名は Ordinal・ルート名だけ大小無視）**：ガード：`CodegenCaseTests` の行 `diag-type-name-collision-*`・`diag-type-name-reserved-*`・`diag-type-name-route-case-collision-*`・`compile-type-names-distinct-*`。→ 同上「型名のぶつかりを生成時に止める」
- **メンバー名の大文字小文字だけの違いは構成に依らず生成時エラー**：JSON の設定が大小無視のため実行時に型の情報が組めない。ガード：`CodegenCaseTests` の行 `diag-member-name-case-only-*`・`compile-member-name-case-editmodel-only`・`GeneratedMemberNameCaseTests`（実際の Kestrel の往復）・`GeneratedFixedMemberDriftTests`。→ 同上「エンティティのメンバー名の大文字小文字」
- **ナビゲーション名は重なるときだけ役割名へ付け直し、同じ列ペアの外部キー重複は生成前に止める**：ガード：`NavigationNameCollisionTests`。→ 同上「ナビゲーション名と外部キーの重複」
- **文字列リテラルへ書く規則の正本は `GeneratedLiteralText`、XmlDoc の名前は専用の `*XmlDoc` フィールドで XML エスケープする**：リテラル・XmlDoc 経由でコード注入や行またぎが起きる。ガード：`CodegenCaseTests` の行 `compile-name-quoted-backslash`・`code-xmldoc-name-angle-bracket-fallback`・`code-xmldoc-form-feed-collapses-and-no-injected-member`・`diag-name-control-character-*`・`NameTrustBoundaryGenerationTests`（説明の FF）・`QueryLiteralEscapeScanTests`。→ 同上「名前・型文字列の信頼境界（C# 生成側）」

### 名前付きクエリ

- **簡易 DSL は単一の C# ラムダへエミットし、NULL 許容列の文字列一致は「列が NULL でない」を AND する**：インメモリが NULL 行で `NullReferenceException` になり実装先が割れる。ガード：`NamedQueryInMemoryRuntimeTests`。→ `src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md`「NULL 許容列の文字列一致」
- **NOT は原子まで押し下げる（ド・モルガン・NULL 許容列の順序比較の否定に `== null ||`）**：`NOT (UNKNOWN)` で NULL 行が落ちる。ガード：`DslConditionFuzzTests`（次元表の因子 NotNesting と `DslFuzzUnits.Coverage` の決まった形）・`QueryConditionCSharpEmitterTests`。→ 同上「NOT の押し下げ」
- **型整合検査は整数リテラルの範囲も見る・GuidKey VO への文字列一致は警告で飛ばす**：ガード：`CodegenCaseTests` の行 `diag-query-type-check-rejects-*`・`DslConditionFuzzTests`。→ 同上「型整合検査」
- **生 SQL は改行を含むときだけ行ごとの逐語リテラル＋エスケープの連結で埋め込み、生成前に静的検証する（未宣言＝該当クエリのみスキップ）**：ガード：`QueryGenerationTests`・`QueryCodegenDiagnosticsTests`・`CodegenCaseTests` の行 `code-query-raw-sql-undeclared-parameter-*`。→ 同上「生 SQL の埋め込みと静的検証」
- **Repository を作らない表（主キーが 1 列でない）の名前付きクエリは、表の警告の直後にクエリごとの警告で名指しする（契約を生成しない構成では表の警告と同じく出さない）**：外すとクエリが黙って消え、利用者には表の警告しか見えない。ガード：`CodegenCaseTests` の行 `diag-repository-single-key-composite-query-warning` / `diag-repository-single-key-keyless-query-warning` / `diag-repository-single-key-query-without-contract-no-warning`・網羅の配列の診断の規則。→ `src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md`「Repository の無い表のクエリ」
- **パラメータ名の予約は「外すとコンパイルできない名前だけ」、生成本体が呼ぶメンバーは `this.` で修飾する**：予約が増え続けるか、同名パラメータがメンバーを隠す。ガード：`QueryReservedParameterNameTests`・`QueryParameterNameSafetyTests`・`CodegenCaseTests` の行 `diag-query-reserved-parameter-name-*`。→ 同上「パラメータ名の予約と this 修飾」

### 生成ダイアログ

- **生成ダイアログで画面に出ていないオプションは設定にも結果にも持ち越さない（画面の値は保持）。それに従う欄（サーバー層フォルダ）も同じ判定で隠す**：無制限バイナリ除外が静かに効き続け、リモート・同期が生成時エラーで止まる。隠れた値を読む欄は画面に残る。ガード：`CodegenCaseTests` の行 `gui-gate-remote-dropped-no-db-access` / `gui-gate-sync-dropped-single-dialect` / `gui-gate-exclude-binary-dropped-no-db-access`・`CSharpGenerationDialogViewModelTests.ShowServerLayerDirectory_TracksRemoteServices`。→ `src/QuickER.CodeGen.UI/CLAUDE.md`「画面に出ていないオプション」
- **設定の全キーは生成オプションとダイアログの往復で値を落とさない**：キーを足して配線を 1 か所忘れる。ガード：`GenerationSettingsParityTests`・`ParityTests`（行 `generation-settings-keys-match-options`）。→ 同上「設定の全キーの往復」

### 生成コードのランタイム（共通）

- **ランタイムの正本は Scriban テンプレートで、`src/QuickER.Runtime*/Runtime*.g.cs` は再生成物（手編集禁止）**：手で直すと再生成で消える。ランタイム 7 パッケージの CLAUDE.md の一覧もここ。→ `src/QuickER.Runtime/CLAUDE.md`「ランタイムの CLAUDE.md 一覧」
- **`BulkInsertAsync` の契約は 4 実装で共通**（null 要素はスキップ・戻り値は実挿入数・空は接続を開かず 0・冒頭で取り消し確認・外部キーのとおりに書きナビゲーションを変えない・渡した順に入れる）：実装先ごとに挙動が割れる。ガード：`GeneratedRuntimeParityTestsBase`・実装先の一致の `bulk-insert-contract`・`bulk-insert-navigation-*`。→ `src/QuickER.Runtime/CLAUDE.md`「BulkInsert の契約」
- **EditModel は確定値が正本で `BindingXxx` はその表示投影。エラーは 2 ストア（入力系と重複）で、重複はチェックごとのスロットに分ける**：入力文字列から再パースすると秒未満や Kind が落ち、片方の解消が他方の所見を消して不正入力が保存される。ガード：`EditModelRemovedValidationTests`・`EditModelValidateUniqueStaleTests`・`EditModelValidateUniqueExecutionTests`。→ `src/QuickER.Runtime/CLAUDE.md`「EditModel のロード意味論と検証経路」
- **rowversion の有無がそのまま楽観排他のポリシー。`ConcurrencyMode` の未定義値は入口で fail-fast、「行なし」と「版が古い」は実在確認で区別**：未定義値が黙って強制上書きになる。ガード：`SqlServerConcurrencyRuntimeTests`・`InMemoryConcurrencyRuntimeTests`・`RemoteConcurrencyRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「rowversion による楽観排他」
- **Save フックはグラフ保存の 2 形態だけで発火する。Before の `false` はその 1 件を単独スキップ、After はコミット前・同一トランザクション**：直接 CRUD に発火させると契約が崩れ、整合の責任が曖昧になる。ガード：`SaveHook{Ado,EfCore,InMemory,SqlServer,Remote}RuntimeTests`・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「Save フック」
- **IncludeGraph の Include ツリーは「1 ナビゲーション 1 ノード」。`AddIncludeNodes` はディープコピーで取り込む**：重複ノードが先行の孫を消し、共有ツリーが以後の全クエリを汚染する。ガード：`IncludeGraph{SqlServer,Ado,EfCore,InMemory}RuntimeTests`・`IncludeGraphRuntimeTestsBase`。→ `src/QuickER.Runtime/CLAUDE.md`「グラフ取得（IncludeGraph）」
- **拡張シム層：実装は `*Core`（partial にしない）、利用者の拡張面は現行名の生成 partial。パッケージ参照モードでも拡張面は生成側に出す**：モード切替で利用者の拡張コードが書き直しになる。ガード：`CodegenCaseTests` の行 `code-extension-shim-*`・`ExtensionShimAcceptanceTests`・`ExtensionShimRepositoryAcceptanceTests`。→ `src/QuickER.Runtime/CLAUDE.md`「拡張シム層」
- **文字列一致の翻訳判定は、インターフェイスマップとメソッドハンドルで引く（`IsAssignableFrom`＋名前一致にしない）**：自前の `Contains` が実行されないまま LIKE へ化ける。ガード：`StringMatchMethod{Ado,SqlServer,EfCore,InMemory}RuntimeTests`・`SqlExpressionTranslatorStringMatchTests`。→ `src/QuickER.Runtime/CLAUDE.md`「拡張シム層」
- **式の翻訳は NULL を C# の意味論に揃える：値の側が null の `==` / `!=`・列側の `!=`・列同士・IN / NOT IN は `IS [NOT] NULL` へ補償、否定は演算子を反転、列を含まない bool は事前評価**：3 値論理で NULL 行が静かに脱落し、InMemory・EF Core と結果が割れる。ガード：`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests`（述語の表 `TranslatorParityPredicateRows`）・`BackendParityRuntimeTests`（シナリオ `query-null-comparison-and-ordering`）・`SqlExpressionTranslatorInClauseTests`・`SqlExpressionTranslatorFoldingTests`。→ `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」
- **ナビゲーションプロパティは列でないので翻訳不能として弾く。日付部品は宣言型が日付のときだけ変換する。文字列一致の null 引数は全バックエンドで `ArgumentNullException`**：存在しない列の SQL が実行時 DB エラーになる。ガード：`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」
- **生 SQL バインダのコレクション IN 展開は、置換の境界を左右両方で守る**：右だけだと `@@ROWCOUNT` の末尾まで置換して SQL が壊れる。→ `src/QuickER.Runtime/CLAUDE.md`「生 SQL バインダ」
- **無制限バイナリ列の除外：INSERT は全列・SELECT / UPDATE は縮小列。除外列に値を残したままの更新は `InvalidOperationException`。EditModel は新規行（Added）でだけ NOT NULL の除外列を必須にする**：blob を黙って取りこぼす。ガード：`CodegenCaseTests` の行 `exec-excluded-binary-required-when-added-*`・`BinaryVoEditModelRuntimeTests`・`BinaryColumn{Ado,EfCore,Remote}RuntimeTests`・`BinaryInMemoryFixtureRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「無制限バイナリ列の除外」
- **Stream アクセサは `length` ちょうどのバイト数を要求し、`WithExactLength` 越しに読む**：SQL Server は宣言長を無視して全部保存し、SQLite は zeroblob の残りを 0 で埋めたままコミットする。ガード：実装先の一致の `blob-stream-contract`・`BinaryRemoteLengthRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「無制限バイナリ列の除外」
- **ランタイム 7 csproj の依存集合は完全一致で固定し、公開 API 面は承認ファイルで固定する**：依存の追加・メンバー削除が利用者のコンパイル済みコードを黙って壊す。ガード：`RuntimePackageProjectDependencyGuardTests`・`CodegenCaseTests` の行 `code-split-runtime-files-hold-no-schema-dependent-types`。→ `src/QuickER.Runtime/CLAUDE.md`「パッケージの依存集合と公開 API 面」
- **トランザクションの失敗経路は `SqlTransactions.RollbackQuietlyAsync` だけを呼ぶ**：完了済みトランザクションへの Rollback が元の例外を置き換え、失敗原因が消える。ガード：`SqlTransactionRollbackTests`。→ `src/QuickER.Runtime/CLAUDE.md`「トランザクション失敗経路」
- **グラフ保存の書く順は `GraphSaveOrder`（削除しない書き込み→削除→後回し）の 1 つで、4 実装先とフックの順が同じ**：子を別の親へ移して元の親を消す保存や、削除した一意な値の再利用が実装先ごとに割れる。ガード：`GraphSaveOrderTests`・`GraphSaveEfCoreSqlServerRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「書く順」
- **グラフ保存が成功したら削除した子は親のコレクションから外れる（`CascadeCollections.RemoveDeletedChildren`）**：外れないと再保存で削除済みの子が残る。ガード：`BackendParitySqlServerRuntimeTests.DeletedSingleChild_StaysOnItsNavigation`。→ `src/QuickER.Runtime/CLAUDE.md`「削除した子の外れ方」
- **外部キーとナビゲーションが食い違うグラフと、同じ行を 2 回書くグラフ（2 つのインスタンスの変更・変更を持つ同じインスタンスへの 2 つの道）は `GraphConsistency.Check` で書く前に断る**：実装先ごとに保存結果が割れる。ガード：`RemoteSaveResponseClientTests`・実装先の一致の `n2-*` / `n3-*`。→ `src/QuickER.Runtime/CLAUDE.md`「整合の検査」
- **EditModel は保存後の Entity の状態を 1 行ずつ見て `AcceptSaved` で受け入れる（`AcceptChanges()` は全部受け入れる別物）**：版が古いままの 2 回目の保存が偽の競合になり、飛ばした行まで確定する。ガード：`EditModelSaveRuntimeTestsBase`・`CodegenCaseTests` の行 `exec-accept-saved-excluded-binary-override-*`。→ `src/QuickER.Runtime/CLAUDE.md`「AcceptSaved」
- **Save フックが blob を書いたら、保存が書いた行の版をコミット前に読み直しコミット後に反映する（`ExecuteSqlAsync` では立てない・変更のない行は読み直さない）**：次の保存が偽の競合になる。ガード：`SaveHookBinaryWriteSqlServerRuntimeTests`・`SaveHookBinaryWriteSqliteRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「Save フックが無制限バイナリ列を書いたとき」
- **重複の事前チェックの NULL の規則は方言の宣言（SQL Server は衝突・SQLite は衝突しない）を共有の照合へ渡す**：2 行目の NULL を見落とし、同期が断られた理由を見つけられず止まる。ガード：`UniquenessGenerationTests`・`UniquenessCheckSqlServerRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「重複の事前チェックの NULL の規則」
- **Include とカスケード削除の CLR 側のキー照合は `IncludeKeys.Normalize` を通す**：型の違う主キーと外部キーで、ナビゲーションがエラーなしに空になり、子が消し残される。ガード：`IncludeKeysTests`。→ `src/QuickER.Runtime/CLAUDE.md`「Include とカスケード削除のキー照合」
- **更新できる列が無い表の UPDATE は、キー列を自分自身へ代入する**：`UPDATE t SET WHERE` の構文エラーになる。ガード：`UpdateSqlWithoutAssignableColumnsTests`。→ `src/QuickER.Runtime/CLAUDE.md`「更新できる列が無い表の UPDATE」
- **ドライバが返した値の型変換は `RawValueConverter.ConvertRaw` を通す（`Convert.ChangeType` 単独にしない）**：`IConvertible` を実装しない型が SQLite で必ず `InvalidCastException` になる。ガード：`RawValueConverterTests`・`SqliteValueConversionRuntimeTests`。→ `src/QuickER.Runtime/CLAUDE.md`「ドライバが返した値の型変換」
- **値オブジェクトの `Create` / 検証規則の本体は共有ルールクラスに 1 回だけ置き、リフレクション解決は `FlattenHierarchy` 必須**：外しても通るが高速経路が静かにフォールバックに落ちる。ガード：`HandwrittenValueObjectTests`・`ValueObjectScenarioTests`。→ `src/QuickER.Runtime/CLAUDE.md`「値オブジェクト」
- **値オブジェクトの型独自入力・空欄の引き取りは、基底 `TryCreateFrom` が `TSelf.` 経由で照会する。定義済みインスタンス（`[DeclaredInstance]`）はフックを消費しない別駅**：型名呼びがフックを迂回する。ガード：`ValueObjectScenarioTests`・`ValueObjectDeclaredInstanceTests`・`ValueObjectDeclaredInputTests`。→ `src/QuickER.Runtime/CLAUDE.md`「値オブジェクト」

### SQL Server

- **SqlParameter は `[SqlColumnType]` 由来の明示型。文字列 Size の「宣言長超過なら値長」ガードを固定 Size にしない**：サイレントなデータ破損。→ `src/QuickER.Runtime.SqlServer/CLAUDE.md`「SqlParameter の明示型」
- **SQL Server の BulkInsert は `CheckConstraints` と `KeepNulls` を常時付け、`BulkCopyTimeout` に接続の `CommandTimeout` を渡す**：FK / CHECK を通す非対称、NULL へ既定値が入る差、常に 30 秒で打ち切られる差。ガード：`GeneratedRuntimeAdoParityTests`。→ `src/QuickER.Runtime.SqlServer/CLAUDE.md`「BulkInsert のオプション」
- **版は `OUTPUT INSERTED` で回収し `RowVersionCollector` がコミット後に適用する**：ロールバック時に幻の版が残る。ガード：`SqlServerConcurrencyRuntimeTests`。→ `src/QuickER.Runtime.SqlServer/CLAUDE.md`「行バージョンの OUTPUT と版の回収」
- **Include の無い取得は FOR JSON でなく `MaterializeRootsAsync` で読む。Include のある取得の float / real は `CONVERT(…, 3)` で書き、NULL は resolver modifier で既定値へ戻す**：FOR JSON は float を 16 桁に丸め NULL をキーごと落とし、`GetAllAsync` と値が食い違う。ガード：`SqlServerPlainQueryRuntimeTests`。→ `src/QuickER.Runtime.SqlServer/CLAUDE.md`「Include の無い取得と FOR JSON」
- **空・空白は `DATALENGTH` と `NOT LIKE` で判定し、LIKE パターンは可変長形で束縛する**：`= ''` が末尾の空白を無視し、固定長で束縛するとパターンが空白で詰められる。ガード：`TranslatorParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.SqlServer/CLAUDE.md`「式の翻訳（SQL Server）」

### SQLite

- **生成 `SqlConnectionFactory` は `Foreign Keys` が未指定のときだけ `True` を補う**：SQLite は既定で FK を検査せず、DDL が宣言した FK が黙って無効になる。サンプルと統合テストがこの既定に依存。ガード：`SqliteForeignKeyDefaultRuntimeTests`。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「接続ファクトリ（外部キーと busy_timeout）」
- **接続を開くたびに `PRAGMA busy_timeout` を `Default Timeout` に合わせる（プールから戻った接続も）**：自動コミットの `RETURNING` が、別の接続の読み取りだけで即座に「database is locked」になる。ガード：`SqliteBusyTimeoutDefaultRuntimeTests`。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「接続ファクトリ（外部キーと busy_timeout）」
- **SQLite 版 BulkInsert はコマンドを 1 回だけ用意して値だけ差し替える（`RebindInsertParameters`）**：行ごとの `new SqliteCommand` は毎行 parse / plan になる。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「INSERT コマンドの使い回し」
- **SQLite の Stream は単独 Read を 1 つの読み取りトランザクションで行い、参加モードの書き込みはセーブポイントで囲む**：rowid の再利用で別の行の blob を返す、失敗した Stream の途中バイトがコミットされる。ガード：`BinaryColumnAdoRuntimeTests`・`SaveHookBinaryWriteSqliteRuntimeTests`。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「無制限バイナリ列のストリーミング」
- **`IncludeLoader` の 3 か所（親キー収集・子の辞書・親からの引き当て）は正規化を必ずそろえ、IN 句へは元の値を束縛する**：1 か所だけ戻すと Include が全部空になる。ガード：`TranslatorParitySqliteRuntimeTests`。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「Include のローダ（IncludeLoader）」
- **日付部品は `datetime(date(col))`、`DateTimeOffset` は格納文字列の位置から取り出す**：strftime が UTC へ換算する。ガード：`TranslatorParitySqliteRuntimeTests`。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「式の翻訳（SQLite）」

### インメモリ

- **インメモリの直接 CRUD は引数の `RowState` を触らない**：触ると Added のまま Insert した後の `SaveAsync` が「変更なし」になり、実 DB なら失敗するケースが通る。ガード：実装先の一致の `direct-crud-leaves-row-state`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「直接 CRUD は RowState を触らない」
- **ストアは主キーを `InMemoryKeyComparer` で比べ、キーで引く集合には必ずこの比較器を渡す**：`byte[]` の主キーが参照比較になり、読み戻したキーで自分の行を見つけられない。ガード：`BinaryKeyExecutionTests`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「ストアのキー比較」
- **保存単位は `InMemorySaveStaging` の copy-on-write で all-or-nothing。検証の判定順は重複キー→存否→版**：失敗した保存がストアを書かず、無くなった行に版比較をしない。取り込み時に行が無かった削除は検証も適用もしない。ガード：`InMemoryConcurrencyRuntimeTests`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「保存単位のステージング」
- **ストアは列だけを写し取る（`CopyColumns` / `CopyRow`）。配列と内包 `byte[]` の VO は複製して共有しない**：グラフごとの複製は超線形で、ナビを落とさないと再保存が子を二重 INSERT する。ガード：`BinaryInMemoryFixtureRuntimeTests`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「列コピーと読み取り」
- **一意制約の検査は索引を引く**：行ごとの全件比較は行数の 2 乗で遅くなる。ガード：`InMemoryUniquenessScalingTests`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「一意制約の索引」
- **インメモリは述語・並び替えがナビゲーションを辿ると `NotSupportedException` で断る**：ストアのナビは常に空で、結果が黙って 0 件 / 全件になる。ガード：`TranslatorParityInMemoryRuntimeTests`。→ `src/QuickER.Runtime.InMemory/CLAUDE.md`「ナビゲーションを断る」

### EF Core

- **EF Core モードの前提（EF Core 無効時は生成物に依存を出さない・EF Core 単独のスコープは行の組み立て器を出さない）**：出すと不要な依存や死んだコードが混ざる。ガード：`EfCoreEntitySaveMetadataTests`・`CodegenCaseTests` の行 `code-efcore-save-metadata-*`・`code-efcore-disabled-emits-no-ef-code`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「EF Core モードの前提」
- **EF Core のグラフ保存は `TrackGraph` を使わず、カスケードの閉包を `Entry(...).State` で 1 件ずつ登録する**：子 1 件の削除で親と兄弟まで消え、別インスタンスの同じ親で追跡が衝突する。ガード：`BackendParityRuntimeTests`・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「グラフ保存の追跡」
- **EF Core の保存と一括挿入は渡したエンティティのナビゲーションを変えない（`NavigationSnapshot`。一括挿入は行のナビゲーションを空にして保存し成功・失敗とも戻す）。生成 DbContext の削除は `ClientNoAction`**：fix-up で子が消える・一括挿入の外部キーがナビゲーションで上書きされる・DB が断る削除が UPDATE で通る。ガード：`BackendParityRuntimeTests`（`bulk-insert-navigation-*` ほか）・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「渡したエンティティのナビゲーションを変えない」「削除の振る舞いとフックの順」
- **EF Core の書く順の補い（外部キーの保存先の値を読む・一意制約ごとの `HasIndex().IsUnique()`）**：付け替えで元の親を先に消す、一意な値の入れ替えで断られる。ガード：`GraphSaveEfCoreSqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「書く順への対応」
- **変わった代替キーは EF Core 版 Repository が `SaveChanges` の前に自分で書き、削除する行の値を取るときは 3 段目を 2 つめの文脈で削除の後に書く**：EF Core は代替キーを書かず変更検出が例外にし、削除の前に書くと一意制約に断られる。ガード：`BackendParityRuntimeTests`（`h1-*`・`graph-updated-row-takes-alternate-key-of-deleted-row-*`）・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「変わった代替キーを自分で書く」
- **EF Core の保存のやり直しは行ごとに・分類の後で数え、フックありの経路は試行ごとにトランザクションを張り直し、採番値はコミット後に書く**：10 行以上の解決で必ず失敗する・二重の文・幻の版が残る。ガード：`EfCoreConcurrencyRuntimeTests`・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「保存のやり直しと実行戦略」
- **カスケードの `ExecuteDeleteAsync` は根のキーを先に読んで子孫から消す**：条件の再評価で子孫だけが消え、`Contains` は SQL Server の上限を超える。ガード：`EfCoreExecuteDeleteRuntimeTests`・`BackendParitySqlServerRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「カスケードの ExecuteDeleteAsync」
- **値オブジェクトの列の Fluent 桁指定・Unicode でない列の `IsUnicode(false)`・値オブジェクトの文字列一致の LIKE の対応付け**：CAST が既定の型になる、SQL Server が列を変換して比べる、Oracle の null パターンが全行に一致する。ガード：`QueryFixtureEfCoreValueCastTests`・`CodegenCaseTests` の行 `model-efcore-non-unicode-*` / `code-efcore-non-unicode-follows-max-length*`・`QueryFixtureEfCoreLikePatternTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「値オブジェクトの列と式の翻訳」
- **主キーで引くキーはメンバー参照で式木へ渡す・生 SQL と Save フックの SQL は DbContext のコマンドタイムアウトを写す・生 SQL のエンティティ取得は除外 blob 列を必須にする**：クエリのコンパイルが増える、接続の既定のまま走る、次の保存で blob を上書きする。ガード：`EfCoreKeyParameterRuntimeTests`・`EfCoreCommandTimeoutRuntimeTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「主キーの引き当て・生 SQL・タイムアウト」
- **EF Core 版 Repository の SQLite のロック待ち（`SqliteBusyTimeoutInterceptor`）**：`UPDATE … RETURNING` が別の接続の読み取りだけで即座に失敗する。ガード：`EfCoreSqliteBusyTimeoutRuntimeTests`・`SqliteBusyTimeoutInterceptorTests`。→ `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「SQLite のロック待ち」

### リモート

- **リモートサービスの構成と配布（クライアント固定部は Core パッケージ、サーバー固定部は `RemoteServerEngine`、per-entity 出力はモード間で不変）**：置き場を崩すとパッケージ参照モードで出力が変わる。ガード：`RemoteServiceAdoRuntimeTests`・`RemoteServiceEfCoreRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「構成と配布」
- **`RemoteAccess` は既定値なしの必須**：全行の読み書き削除面の開放/保護を既定値に決めさせない。ガード：`RemoteAccessRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「認可」
- **エンドポイントのマップはエンジンのジェネリックメソッドへ集約し、`OnServerError` と 500 の詳細非公開はグループのメタデータで繋ぐ**：アセンブリを跨ぐ partial 契約が使えず、署名を変えるとワイヤ互換が崩れる。ガード：`RemoteErrorDetailRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「エンドポイントのマップ」「500 の詳細非公開とサーバーエラーのフック」
- **クライアントの失敗分類（JSON でない 200・VO 検証違反・本文を読めなかった理由・バイナリ転送の 2xx 判定）**：プロキシの 200 を成功と読んで別物の HTML を blob に書く。ガード：`RemoteResponseBodyRuntimeTests`・`BinaryTransferResponseRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「クライアントの分類」
- **400 の分類は `ClassifyFailuresAsync` の 1 か所・必須フィールド欠落とページング引数と未定義の `ConcurrencyMode` は 400**：リポジトリ奥の例外が 500＋ログ＋フックに化ける、版チェックが無効化される。ガード：`RemoteServerEngineRuntimeTests`・`RemoteHostConfigurationRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「400 の分類」
- **切断はタイミングに依存せず無応答で扱い、要求のタイムアウトは 504 にする**：混んだ機械で切断が 400 / 500 に化ける、タイムアウトが空の 200 になる。ガード：`RemoteServerEngineRuntimeTests`・`RemoteHostConfigurationRuntimeTests`・`RemoteSqlClientCancellationRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「切断とタイムアウト」
- **保存の応答は保存した行の列の値を運び、クライアントは位置で突き合わせて書き戻す（`Saved` / `Unsaved`）**：Save フックが書いた値・採番列・計算列がクライアントへ届かない。ガード：`RemoteEfCoreGeneratedValuesRuntimeTests`・`RemoteSaveResponseClientTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「保存の応答は列の値を運び位置で突き合わせる」
- **除外した無制限バイナリ列は JSON の通信で値として運ばない（`EnterRemoteRequest` の印・`StripExcludedFromResult`・EF Core の `KeepStoredUnboundedBinaries`）**：全列を書く EF Core のサーバーが保存済みの blob を NULL や空で上書きする。ガード：`RemoteEfCoreUnboundedBinaryRuntimeTests`・`RemoteRequestMarkRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「除外した無制限バイナリ列を値として運ばない」
- **バイナリ転送エンドポイント（404 の marker・411 / 413・`allowUnboundedUploads` のオプトイン・`Expect: 100-continue`・`ExactLengthContent`）**：設定ミスが「データなし」に化ける、413 がリセットで捨てられる、長さ違反の blob が保存される。ガード：`BinaryColumnRemoteRuntimeTests`・`BinaryRemoteLengthRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「バイナリ転送エンドポイント」
- **リモートの DI 登録は非 keyed・keyed × ベースアドレス版・HttpClient ファクトリ版の 4 本**：keyed 版が無いとサーバー＝HTTP・ローカル＝SQLite の構成を組めない。ガード：`RemoteKeyedRegistrationRuntimeTests`。→ `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「DI 登録」

### 双方向同期

- **同期の生成条件と診断**：実効方言が sqlserver+sqlite のちょうど 2 つ・`GenerateRepositories`・同期可能テーブル 1 件以上でなければ生成時エラー、固定長文字列の主キーは警告（空白詰めでキーが置き換わり収束しない）。ガード：`SyncSupportGenerationTests`・`CodegenCaseTests` の行 `diag-sync-fixed-length-key-warning-nchar` / `diag-sync-fixed-length-key-warning-char` / `diag-sync-fixed-length-key-variable-nvarchar-no-warning` / `diag-sync-fixed-length-key-variable-varchar-no-warning` / `diag-sync-fixed-length-key-without-sync-support-no-warning` / `diag-sync-fixed-length-non-key-column-no-warning`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「生成の条件と診断」
- **アンカーは保存せずミラー版の MAX から導き、ミラー版を書くのはダウンロードだけ**：アップロードの版をミラーへ書くと順序付きの流れを追い越して恒久的に取りこぼす。差分 SQL の `@anchor` は `CAST(... AS binary(8))`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「アンカーは保存せず導出する」
- **受理記録（ack）は行単位でアンカーではなく、original は max(ミラー版, ack 版)**：ack 優先固定は掃除漏れで偽競合へ戻る。settle は「補助記録が先・エントリが後」、掃除はコミット後。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「受理記録（ack）はアンカーではない」
- **ローカル変更はデコレータで journal-first に記録し、グラフ保存はカスケード全体を記録する**：フック方式は直接 CRUD を素通しし、ルートだけ見ると子データが両側から消える。エンジン自身の書き込みは `SyncSession.Suppress()`。ガード：`SyncRuntimeTestsBase.RecordedIds_FollowTheOrderOfTheRecords`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「デコレータ方式と journal-first」
- **失敗した書き込みの記録はロックの中で取り消し、成功した書き込みは記録と突き合わせる**：残すと着地しなかった変更を送る・フックが飛ばした行や決めたキーと記録が食い違う。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「失敗した書き込みの記録を取り消す」
- **Save フックが context で書いた blob は保存と同じトランザクションでジャーナルへ記録し、フックは観測を外して呼ぶ**：記録しないとサーバーへ送られず次のダウンロードが上書きし、観測を付けたままだと無関係な DB にジャーナルを書く。ガード：`SyncRuntimeTestsBase`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「Save フックが context で書いた無制限バイナリ列」
- **送る操作は最新の記録でなく、いまのローカル行の有無と記録全体で決める**：最新だけで決めると拒否された削除で編集が、失敗した再挿入でサーバーの削除が消える。削除再生は先に probe する。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「送る操作の決め方」
- **ServerWins はサーバー行を読み直してからエントリを捨て、適用はそのテーブルのダウンロードの直後・親→子**：捨てるだけだとローカル編集が残り、アップロード段で適用すると未着の親を参照して外部キーで止まる。読み直した版はミラーでなく ack へ。ガード：`SyncVoRuntimeTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「競合ポリシー」
- **アップロードは巡に分け、断られた書き込みは進展がある限り次の巡へ回す**：表の中だけで回すと一時的な失敗や一意な値の空きを持ち越し、後勝ち＋ServerWins が変更を捨てる。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「アップロードの巡」
- **サーバーが断った理由は書き込みが失敗したときだけ確かめ、確かめられなければ元の例外と一緒に投げる**：参照の有無を推測で決めると偽の競合で削除を捨てる。ガード：`SyncKeptRowsTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「サーバーが断った理由の確認」
- **一意な値の削除→再挿入は upsert を削除の後へ回し、持ち主が古ければ読み直す**：回さないと同じ値の挿入が断られ、古い持ち主が新しい行を拒み続ける。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「一意な値の持ち主を新しくする」
- **ダウンロードは未送信の行で打ち切り（そのテーブルだけ）、ack 一致はキーと版の両方で照合する**：キーだけだと他者の変更を取りこぼし、打ち切りを後ろへ連動させると古い子が直らない行き詰まりが続く。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「未送信の行との突き合わせ」
- **ローカルの Save フックが適用の行を飛ばしたら、その表をその行で止める**：後ろを書くと再開点が飛ばした行を越えて二度と降りない。登録簿なしで飛ばしたら例外。ガード：`SyncLocalSaveHookRegistrationTests`・`SyncRuntimeTestsBase`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「Save フックが行を飛ばしたら止める」
- **親がローカルに無い行は解決役がサーバーから親を読み直して先に適用する**：ミラー版を書くとアンカーが壊れ、範囲外・未送信の削除の親を戻すと利用者の削除を黙って取り消す。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「親をそろえる解決役」
- **削除伝搬はローカルで参照されていない行だけを消し、残す行の祖先も残す**：参照を見ないとローカルの外部キーで同期が止まり続ける。ガード：`SyncKeptRowsTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「削除伝搬」
- **同期の実行中のローカル書き込みは実行器ごとのロック 2 つで並べ、取り方はコールバック形**：ロックを分けると画面と同期で別物になり、async の中で立てた印は呼び出し元へ戻らない。保存フックからの同期は断る。ガード：`SyncSqliteRuntimeTests`・`SyncRuntimeTestsBase`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「同期の実行中の書き込み」
- **blob は書いた列だけを送り、挿入の経路は全列・上書きで送らなかった列はコピー待ちへ**：記録の列だけだと復活行が空の blob で作られ、他者の blob が恒久的に降りない。ガード：`SyncVoRuntimeTests`・`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「送る列」
- **洗い替えは per-table / per-batch でクラッシュ安全にし、範囲外の参照・blob の損失・未送信は何も消す前に断る**：既定 `LastWriteWins` でないと版なし子の図で構造的に失敗し、受理記録を全消しの前に消すと偽の競合になる。ガード：`SyncConstructionExclusionTests.ExcludeFromSync_ReferringLocalRows_RefuseRefresh`・`SyncRefreshBenchmarkRuntimeTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「洗い替え」
- **版なしテーブルは後勝ちモードのときだけ全量スキャンで同期し、除外はラン単位と構築時の 2 層**：Versioned は版なしに触れない宣言を保つ。ガード：`SyncConstructionExclusionTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「版なしテーブルと後勝ちモード」
- **キーのテキストは日時の Kind を落とし、decimal を指数表記にせず、文字列キーのページングは列の型へ CAST する**：Kind つき・指数表記・nvarchar 比較でそれぞれ行が一致しない・毎回止まる・読み飛ばす。ガード：`SyncKeyTextRuntimeTests`・`CodegenCaseTests` の行 `probe-sync-key-text-decimal-key`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「キーのテキスト」
- **SyncAsync が自分から変えたローカル行は `LocalRowChanges` に残し、例外のときも Data で運ぶ**：載せないと捨てた・読み直した行を利用者が知る手段が無い。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「実行が変えた行の一覧」
- **差分ソースは `HasMore` で継続を決め、HTTP は差分ソースの薄い remoting で上限は呼び出し側の値**：勝手な ceiling は実行中のトランザクションの行を読み飛ばす。ダウンロードの進捗ガードがハングを止める。ガード：`SyncHttpRuntimeTests`・`SyncSqlServerHttpRuntimeTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「差分ソースと HTTP」
- **デコレータの差し替えは元の登録の作り方を保つ**：factory 登録だけを受けると非 keyed の `AddGeneratedSyncEngine(null)` が必ず投げる。ガード：`SyncConfigMatrixRuntimeTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「生成と DI」
- **同期のテストのエンジン**：決定的なシナリオは `SyncModelRuntimeTests`・`SyncModelHttpRuntimeTests`・`SyncModelSqlServerRuntimeTests`（`SyncModelSqlServerRuntimeTests.SqlServerOnly`・`ISyncModelTransportTests`・`SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport`・`SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`・`SyncModelRuntimeTests.Family`）、実 SQL Server の固有は `SyncSqlServerRuntimeTests`。→ `src/QuickER.Runtime.Sync/CLAUDE.md`「ガードテストの一覧」

### GUI（アプリ）

- **完了通知は「外部とのやり取り＝モーダル／図ファイル自身の読み書き＝ステータスバー」で使い分け、失敗は常にモーダル、内訳つきは `ShowInformationDetails`、判断材料の一覧は `ConfirmWarningDetails`（全件）**：提示先が割れる／MessageBox がスクロールせずボタンが画面外へ出る。→ `src/QuickER.Gui/CLAUDE.md`「完了通知の提示先」
- **更新フィードの環境変数による差し替えは `#if DEBUG` 限定、更新の適用は通常の終了経路の後（`PostExitActions`）だけ**：配布ビルドで任意フィードが更新の受け口になり、Closing を飛ばすと自動保存が走らない。ガード：`LifecycleTests`（行 `update-*`・`post-exit-*`）・`PostExitExhaustiveTests`。→ `src/QuickER.Gui/CLAUDE.md`「配布と自動更新」
- **再起動の要求と起動処理を分け、再起動の直前に単一インスタンスの座を手放す**：適用が失敗すると QuickER が閉じたまま戻らず、座を持ったままだと新しいインスタンスが黙って終わる。ガード：`PostExitExhaustiveTests`・`LifecycleTests`（行 `post-exit-*`）・`SingleInstanceGuardTests`・`SourceRuleTests`（規則 `program-single-instance-uses`・`program-single-instance-bypasses`）。→ `src/QuickER.Gui/CLAUDE.md`「再起動と単一インスタンス」
- **アプリ終了の連鎖は段ごとに `ShutdownSteps.Run` を通し、終了条件は `ShutdownMode="OnMainWindowClose"` ＋ `MainWindow` の明示で紐づける**：前段の失敗で子プロセスが孤児になり、Hide されたウィンドウだけが残る。クラッシュ経路も同じ後始末を通す。ガード：`ShutdownChainExhaustiveTests`・`LifecycleTests`（行 `steps-*`・`modules-*`・`crash-*`）・`SourceRuleTests`（規則 `app-shutdown-mode-declaration`・`app-shutdown-mode-overrides`・`app-main-window-assignment`・`app-feature-module-shutdown-uses`・`app-ui-crash-handler-uses`・`app-ui-crash-handler-bypasses`）・`CrashHandlingServiceTests`。→ `src/QuickER.Gui/CLAUDE.md`「終了の連鎖」
- **モーダルは必ず `DialogOwner.Resolve()` のオーナー付きで出し、実行中フラグを落としてから出す**：モードレスの機能ウィンドウの背面へ回り込んで画面が固まって見える。`PasswordBoxBehavior` の DP 既定値は null 必須。ガード：`SourceRuleTests`（規則 `dialog-owner-resolve-uses`・`file-dialog-ownerless-shows`・`message-box-owner-resolver-copies`）・`PasswordBoxBehaviorTests`。→ `src/QuickER.Gui.Common/CLAUDE.md`「モーダルはオーナー付き」「PasswordBoxBehavior の DP 既定値」
- **自動整列（格子・階層・力学・追記）は図の形に依らず、決定的で表を重ねず、追記は既存を動かさない**：同じ図の配置が開くたびに変わる・表が重なる・追記で既存が崩れる。ガード：`AutoLayoutScenarioTests`。→ `src/QuickER.Gui/Services/CLAUDE.md`「自動整列の共通の性質」

### GUI（ViewModel）

- **保存・読込直後の最終既知ハッシュは書き出した／読み取った内容そのものから採る（ディスクを読み直さない）**：隙間に書いた外部内容を自分の内容と誤認し、次の Ctrl+S が照合を素通りする。ガード：`DocumentFileTests`（行 `race-*` と乱数の手の列・不変条件 (2)）・`MainViewModelLoadHashRaceTests`（実監視の追従）。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「最終既知ハッシュ」
- **上書き保存は書き込み前にディスクの現状を見て確認する（将来版は常に、外部変更は開いているファイルのみ、判定材料は VM に持たない）**：外部の内容や将来版のデータを黙って消す。ガード：`DocumentFileTests`（行 `guard-*`・`newer-*` と乱数の手の列・不変条件 (1)）。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「上書き保存の確認」
- **自動保存の文書メタは図の内容のハッシュを持ち、起動時に照合する**：落ちた後に別の図が前のファイルのパスつきで復元され、確認なしに上書きする。ガード：`DocumentFileTests`（行 `meta-*` と乱数の手の列・不変条件 (4)）。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「自動保存の文書メタ」
- **文書の外部変更検知（クリーンは自動再読込・ダーティは確認）と、処理前のドラッグの打ち切り（開始時点へ復元）**：コミットすると自動再読込が確認ダイアログへ化け、素のリセットは履歴に無い変更を残す。ガード：`DocumentFileTests`（行 `ext-*`・`drag-*` と乱数の手の列・不変条件 (3)）。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「文書と外部変更検知」「外部変更の処理とドラッグの打ち切り」
- **確認ダイアログの前に図から作った結果は、確認の後で `DiagramRevision` を比べ、変わっていれば当てずに取りやめる**：自動再読込やチャットの編集を古い結果で上書きして捨てる。ガード：`MainViewModelTargetDbmsTests`・`DiagramImportServiceTests`・`MainViewModelErDiagramHostTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「確認の間に変わった図」
- **図の一括置換は `ClearDiagramCollections` を通して解除は Clear の後、`RunBulkDiagramUpdate` で再取得を最外側の終了時 1 回にする**：追跡が漏れて幽霊コマンドが積まれ、所要時間が要素数の積で伸びる。ガード：`DiagramChangeTrackerDetachTests`・`MainViewModelBulkDiagramUpdateTests`・`LayoutRefreshCountTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「図の一括置換」
- **選択・検索結果・作成中リレーションの始点は図から外れた時点で解く**：追跡の外れた要素を編集して履歴に残らず、古い検索結果から削除すると同じ Id の別の表のクエリまで消える。ガード：`SelectionInvariantTests`・`PendingEditOnDetachTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「選択・検索結果・作成中リレーションの解除」
- **図の ViewModel の読み書きプロパティは「Undo で追跡する」か「履歴の外」のどちらかに宣言し、連動変更は 1 履歴に畳み、削除は `IndexedRemoval`、子列の外部キーの印は Undo で戻す**：表に無い新プロパティが履歴に残らず、連動変更が別履歴になる。ガード：`TrackedPropertyUndoExhaustiveTests`・`UndoOperationTests`（行 `removal-order-*`）・`RemovalOrderRoundTripTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「Undo の追跡宣言」
- **選択候補コレクションは `CandidateListSync.SyncTo`（Move）で同期し、選択中の項目を ItemsSource から外さない／当て直しは「候補へ足す → 選択 → 絞る」の順**：ComboBox が null を書き戻して正本を変え、Undo/Redo 中にアプリが落ちる。ガード：`CandidateListSyncTests`・`CandidateRowReconcileTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「選択候補コレクションの同期」
- **受け付けない名前（識別子の上限・表せない文字）は DDL の入口と同じ判定で、GUI の確定・ツール応答・方言切替の確認に知らせる**：判定の写しが食い違う。ガード：`NameLimitStatusWarningTests`・`NameLimitNotesTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「名前の上限の通知と方言切替の確認」
- **一意制約カード（行リストの構成列編集・UQ マーク・列削除でカスケード）**：重複選択や制約の変質を防ぐ。ガード：`UniqueConstraintResolvedNameLimitTests`・`EntityViewModelUniqueConstraintTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「一意制約の GUI」
- **主キーの順序の GUI（`EntityViewModel` の保持とミラー、カードに膜の編集を置かない、`SetPrimaryKeyCommand` で 1 履歴）**：取込→保存で順序が消え、ロックを迂回する。ガード：`UndoOperationTests`（行 `primary-key-*`）・`PrimaryKeyOrderCardTests`・`ErDiagramToolHostParityTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「主キーの順序の GUI」
- **リレーションの GUI（ペア行エディタ・親 PK 全列の自動ペア化・列削除で全ペアクリア）**：自動ペア化が単独の `id` を誤爆する。ガード：`ForeignKeyColumnResolverTests`。→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「リレーションの GUI」

### GUI（ビヘイビア・ビュー）

- **キャンバスの進行中操作の静的状態は `[ThreadStatic]`、`CanvasViewportBehavior` は後始末も対（ウィンドウへの 5 購読を外し、`Loaded` で戻す）**：並列テストが状態を奪い合い散発失敗になり、ウィンドウの入力を拾い続ける。ガード：`DragBehaviorCancelTests`・`CanvasViewportBehaviorDetachTests`。→ `src/QuickER.Gui/Behaviors/CLAUDE.md`「進行中操作の静的状態は [ThreadStatic]」「CanvasViewportBehavior の後始末」
- **Space のパンは、キーボードで移ったボタン類の Space を横取りしない（`MostRecentInputDevice` を使わない）**：Tab で移ったボタンが押せなくなる。ガード：`CanvasViewportBehaviorSpaceKeyTests`。→ `src/QuickER.Gui/Behaviors/CLAUDE.md`「Space のパンは、キーボードで移ったボタン類の Space を横取りしない」
- **全画面表示は通常サイズを経由して最大化し、通常サイズへ戻されたら枠を戻す（最小化では解かない）**：タスクバーが前面に残る／掴めないウィンドウが残る。ガード：`PanelVisibilityTests`。→ `src/QuickER.Gui/Views/CLAUDE.md`「全画面表示」
- **パネルを畳むときは中の論理フォーカスをパネルの外へ出す**：`LostFocus` が発火せず入力途中の値が黙って消える。ガード：`PanelVisibilityTests`。→ `src/QuickER.Gui/Views/CLAUDE.md`「パネルを畳むときは、その中に論理フォーカスを残さない」
- **DataGrid は行の追加・削除の既定を切り、列一覧の Delete は `RemoveColumnCommand` へ結ぶ**：列の削除が履歴にも未保存にも残らず後始末も通らない。ガード：`ColumnGridDeleteKeyTests`・`SourceRuleTests`（規則 `datagrid-row-edits`）。→ `src/QuickER.Gui/Views/CLAUDE.md`「DataGrid の行の追加・削除」

### 取込・エクスポートの形式

- **形式の往復は形式の往復のエンジン（形の行 × 形式の共通の表明・規則の行・マージの行）で確かめ、形式の違いはアダプタの宣言だけが持つ（運ばない面は根拠つき・告知する形式は告知で説明できる面を宣言しない）**：宣言が抜け道になると往復の取りこぼしが黙って緑になる。ガード：`FormatRoundTripTests`（`NotCarriedDeclarations_HaveEvidence`・`ImportableAdapters_MatchProductImportFormats`・`ShapeTestClasses_CoverFormatAxis`・`Coverage_MatchesGaps`）・`FormatShapeTestsBase`・`FormatRuleTests`・`FormatMergeTests`。→ `src/QuickER.Gui/Services/CLAUDE.md`「形式の往復のエンジン」
- **Mermaid / DBML の出力は、表現できない情報を `DetectOmissions`（図の中身から判定）で告知し、形式ごとにセッション 1 回、SQL DDL・定義書・画像は告知しない。取込の規則で決まるもの（制約名・推論される列ペア・主キー列の外部キーの印）は出力を 1 回だけ取り込み直して判定する**：判定が出力とずれる／毎回の通知が形骸化し本当の欠落が埋もれる。ガード：`FormatRoundTripTests`（形の行の告知の期待＝`mermaid-quoted-constraint-label-*`・`mermaid-plain-constraint-name-not-read-back-*`・`mermaid-primary-key-order-*`・`mermaid-omissions-*`・`dbml-omissions-*`・`mermaid-unique-on-*`・`dbml-primary-key-child-column-is-carried`・`excel-pairless-relationships` ほか）。→ `src/QuickER.Gui/Services/CLAUDE.md`「出力形式の情報欠落告知（Mermaid / DBML）」
- **往復する形式（DBML・Excel 定義書）は自分の出力が書いた形を取込で断らない（型の無い列は DBML が `""`・Excel が空のセル、同じ列ペアの 2 本は 2 行）**：自分が書いたファイルを読み戻せない。ガード：`FormatRoundTripTests`（形の行 `mermaid-empty-data-type`／規則の行 `excel-rule-identical-relationship-rows-are-both-read`・`dbml-rule-parse-error-line-number`）。→ `src/QuickER.Gui/Services/CLAUDE.md`「往復する形式は自分の出力を読む（型の無い列・同じ列ペアの 2 本）」
- **一意制約・主キーの順序・複合外部キー・制約名の形式ごとの扱い（DBML は往復、Mermaid は告知／単一列のみ、Excel は順序を運ばない）と、名前の無い関係の既定名は `NameUnnamedForeignKeys` 共有**：形式間で図が食い違う。ガード：`FormatRoundTripTests`（形の行 `dbml-reordered-primary-key`・`dbml-unnamed-relationships`・`mermaid-unnamed-relationships-relates`／規則の行 `dbml-rule-primary-key-*`・`dbml-rule-same-pair-twice-without-names-second-gets-unique-name`・`mermaid-rule-*` のラベルの行）。→ `src/QuickER.Gui/Services/CLAUDE.md`「一意制約・主キーの順序・列ペアの入出力」
- **DBML の取込はテーブルブロック内の行をキーワードだけでブロックと判定しない**：`records` などの列が消え、`note` で取込全体が失敗し自分の出力を読み戻せない。ガード：`FormatRoundTripTests`（形の行 `dbml-keyword-column-names`／規則の行 `dbml-rule-single-line-skippable-block-is-not-a-column-*`）。→ `src/QuickER.Gui/Services/CLAUDE.md`「DBML の取込：テーブルブロック内の行をキーワードだけでブロックと判定しない」
- **Excel 定義書の取込は、カラム名が空でほかのセルに値がある行を断る（表の終わりとみなさない）**：行と後ろのカラムが黙って消える。ガード：`FormatRoundTripTests`（規則の行 `excel-rule-row-without-column-name-but-other-content-*`・`excel-rule-middle-row-without-column-name`・`excel-rule-row-with-only-number-ends-table`・`excel-rule-empty-row-ends-table`）。→ `src/QuickER.Gui/Services/CLAUDE.md`「Excel 定義書の取込：名前が空の行」
- **Excel 定義書の取込と C# リバースは、主キーの列を `ColumnNullability.AllowsNull` で NOT NULL として読む（必須のセルが空の行・`[Key] int?`）**：図だけが NULL 許容の主キーを持ち、DDL と生成コードと食い違う。ガード：`FormatRoundTripTests`（規則の行 `excel-rule-primary-key-row-with-empty-required-cell-is-not-null`・`csharp-rule-nullable-key-property-is-not-null`）。→ `src/QuickER.Gui/Services/CLAUDE.md`「取込は主キーの列を NOT NULL で読む（Excel 定義書・C# リバース）」
- **Mermaid / DBML の識別子・ラベルは `ExportTextSanitizer.Sanitize`、DBML のリテラルは `DbmlLiteral.Escape` / `Unescape` の対（左から 1 パス）**：名前による注入と二重の復元。ガード：`FormatRoundTripTests`（形の行 `injection-*`・`dbml-backslash-constraint-names`）。→ `src/QuickER.Gui/Services/CLAUDE.md`「出力のエスケープ」

### 機能モジュール

- **フィーチャーモジュール方式：Gui は機能の型を知らず、依存は「プラグイン → 契約 ← ホスト」、`FeatureToolbarItem` の BeginsGroup は起動時のみ反映**：機能プロジェクトが Gui に混ざる／実行中に変えても折返しが変わらない。ガード：`FeatureToolbarGroupTests`。→ `src/QuickER.Extensibility/CLAUDE.md`「フィーチャーモジュール方式」「FeatureToolbarItem」
- **`IErDiagramHost` の能力（IsDirty・DiagramRevision・ReplaceDiagram・ReplaceQueries・イベント）と、列リネーム時のクエリ条件追従（`QueryConditionRenameFollower`、読めない新名は書き換えない）**：機能側が図を壊す／Undo で条件が戻らない。ガード：`QueryConditionRenameFollowerTests`・`MainViewModelErDiagramHostTests`。→ `src/QuickER.Extensibility/CLAUDE.md`「IErDiagramHost」「列リネームのクエリ条件追従」
- **同梱構成は `IncludeDbToolModules` / `IncludeAiModules` / `IncludeCodeGenModules` で独立に外せる**：外した構成で依存が残る。→ `src/QuickER.Extensibility/CLAUDE.md`「同梱構成」
- **接続プロファイルの暗号ファイルは「消すのは JSON の書き込みの前・書くのは後」**：新しい接続先と古いパスワードの組が残り、前の接続先の資格情報を別のサーバーへ送る。ガード：`ProfileStoreTests`（行 `pairing-*`）・`ConnectionDialogTests`（行 `delete-secret-delete-fails-*`・`rename-replaced-secret-delete-fails-*`・`ok-last-used-secret-write-fails-*`）。→ `src/QuickER.Db.UI/CLAUDE.md`「接続プロファイルの暗号ファイルの順序」
- **接続ダイアログは同期モードで方言を固定し、接続テスト中は確定できず、ストアの失敗で一覧をディスクの実体どおりに保つ。DB 取込は世代が同じときだけ図を置き換えて終端の通知をちょうど 1 件出し、同期のダイアログは確認を断ったら実行器を呼ばず、取り直しに失敗したら古いスクリプトを残さず、コミットの後なら閉じずにモーダルで知らせる**：固定した方言と違う接続先へ DDL を流す・確認の間の変更を古い取込結果で上書きする・取り直せなかった古いスクリプトを流す・取り直せなかった同期を一致したと見せて閉じる。ガード：`ConnectionDialogTests`・`ImportSyncTests`（どの行でも組が見張る）。→ `src/QuickER.Db.UI/CLAUDE.md`「ダイアログとコマンドの状態の不変条件」

### MCP

- **MCP ツールの定義の正本は英語の `ErDiagramToolCatalog` 1 つで、GUI の `ErDiagramDynamicTools` と意味論を揃える**：食い違うと同じ呼び出しが GUI と MCP で違う図を作る。検証はどの変更よりも前に置く（GUI は変更をその場で VM へ反映するため）。ガード：`ToolScenarioTests`・`McpToolStructureGuardTests`。→ `src/QuickER.Mcp/CLAUDE.md`「ツール定義の正本と両ホストのパリティ」
- **MCP の set_primary_key / set_unique_constraint / add_relationship / set_query の規則**（主キーは全体置換・外れた列の NULL 許容は据え置き・ロックを持たない非対称、制約名の衝突拒否、クエリ定義の検証を生成器と共有）：両ホストで図が割れる。ガード：`ErDiagramToolHostParityTests`・`QueryToolGenerationRuleTests`。→ `src/QuickER.Mcp/CLAUDE.md`「個別ツールの規則」
- **MCP はステートレスで、レイアウトを書かず、stdout を JSON-RPC 専用にする**：`file` の機械注入と「読込→変更→保存」で完結させ、stdout が汚れるとプロトコルが壊れる。ガード：`McpServerE2ETests.McpServer_IgnoresAppSettingsInWorkingDirectory`。→ `src/QuickER.Mcp/CLAUDE.md`「ステートレス設計」「stdio の純度」
- **コード生成ツールの結果は英語固定（`CurrentUICulture` だけ InvariantCulture にして戻す）**：CLI と resx を共有するため、外すと言語が漏れる。ガード：`CodeGenToolSetLanguageTests`。→ `src/QuickER.Mcp/CLAUDE.md`「コード生成ツール」
- **ツールの引数の形は実行前に入口（`ToolArgumentShape`）で 1 回だけ検査する**：外すと黙った既定値・型違いの読み捨て・生の例外になる。値の読み取りと列ペアの比較は正本 1 か所。ガード：`ToolArgumentShapeFuzzTests`・`SourceRuleTests`（規則 `tool-argument-parsing-copies`・`tool-argument-parsing-owners`）。→ `src/QuickER.Mcp/CLAUDE.md`「ツールの引数の形の検査」
- **ツールの複数行の応答は改行を LF だけで組む**：ホストごとに改行が食い違う。ガード：`ToolScenarioTests`（行 `line-ending-summary-uses-lf-only`）。→ `src/QuickER.Mcp/CLAUDE.md`「ツールの応答の改行」
- **MCP の呼び出しは `DiagramFileGate` で直列化し、何も変えない呼び出しでは図のファイルを書かず、`create_diagram` は上書きしない**：書くと開いている GUI が再読込・確認を出し、並列の変更系が互いの更新を消す。ガード：`DocumentErDiagramFileHandlingTests`・`DocumentErDiagramToolConcurrencyTests`。→ `src/QuickER.Mcp.Tools/CLAUDE.md`「何も変えない呼び出しで書かない」

### AI チャット

- **Copilot のシェル許可は、パスが取れないなら全要素が許可リスト（`dotnet` のみ）のときだけ承認し、URL 参照・書き込みリダイレクトは両方の枝で拒否する**：緩めると `dotnet build && curl … | sh` のような連結が通る。`dotnet` 自体が外へ出る経路は塞げない（残余）。ガード：`ShellPermissionTests`。→ `src/QuickER.AI/CLAUDE.md`「Copilot のシェル許可」
- **内蔵チャットの CLI バックエンドは作業フォルダを `%TEMP%\QuickER\{backend}\<guid>` へ隔離し、Codex は read-only を明示する**：実行フォルダが承認なしで読まれる。モック生成側は出力先へ書くので workspace-write。ガード：`BackendWorkingDirectoryTests`・`CodexAppServerClientStartInfoTests`。→ `src/QuickER.AI/CLAUDE.md`「CLI バックエンドの作業フォルダ」
- **Codex への送信は書き込みを取り消しと競わせ、書き込みロックは書き込みが終わるまで持ってちょうど 1 回手放す**：同期のパイプの書き込みはプールが詰まると取り消しが効かず、要求が取り消しでもタイムアウトでも戻らない。先に手放すと次の行が混ざり、手放し忘れると以後の送信が止まる。ガード：`CodexAppServerClientConnectionTests.Request_CanceledBeforeWriteEnds_ReleasesWriteLockOnceWhenWriteEnds`。→ `src/QuickER.AI/CLAUDE.md`「Codex の送信の取り消しと書き込みロック」
- **チャットエンジン 4 種のツール実行と活動通知は `ChatToolInvocation` を通す**：通知を保護しないと購読側の例外で実行済みの呼び出しに「未実行」の結果が積まれ、AI が二重に操作する。引数は `Func<string>` で try の内側で取り出す。ガード：`ChatToolInvocationTests`・`SourceRuleTests`（規則 `chat-engine-tool-invocation-uses`・`chat-engine-tool-invocation-copies`）・`ChatEngineContractTests`。→ `src/QuickER.AI/CLAUDE.md`「ツール実行と活動通知」
- **チャットエンジン 4 種の共通の契約は 4 エンジンへ同じ行で流し、違いはアダプタの出典つきの宣言に置く（持たない組は手で書いた一覧）**：エンジンごとに別々に書くと 1 エンジンだけの退行を見逃し、宣言を書き換えると行が黙って外れる。ガード：`ChatEngineContractTests`・`ChatDriverTests`。→ `src/QuickER.AI/CLAUDE.md`「チャットエンジンの共通の契約」
- **チャットのダイアログは、ターンの外に応答中の吹き出しを残さず、ターン中に接続方式を変えず、切り替えられない状態の切替を確認なしで断る**：外すと吹き出しが Markdown へ戻らない・実行中のエンジンを置き去りにする・開始待ちに会話を消す確認が出る。ガード：`ChatDialogTests`。→ `src/QuickER.AI.Chat/CLAUDE.md`「ダイアログの状態の不変条件」
- **回答の Markdown 表示は、リンクを http / https に限り、画像を読み込まず、HTML を解釈しない**：AI の回答は外部入力で、他のリンクは UNC などで外部へ接続する。ガード：`ChatMarkdownRendererTests`・`ProcessLaunchTests`（行 `external-chat-link-*`）。→ `src/QuickER.AI.UI/CLAUDE.md`「回答の Markdown 表示」
- **回答の組み立てに失敗したら、その吹き出しだけテキスト表示へ落とす（深さは再帰なしで測り上限 32）**：インラインの深い入れ子はスタックを使い切ってプロセスごと落ちる。ガード：`AssistantMessageViewTests`・`ChatMarkdownRendererTests`。→ `src/QuickER.AI.UI/CLAUDE.md`「組み立ての失敗はテキスト表示へ」
- **吹き出しを内容の幅へ縮めるときは先に部品を測り、内側の余白は覚えずに毎回読む**：`PagePadding` が最初のレイアウト中に置き換わり、先に幅を決めると最後の文字が次の行へ落ちる。ガード：`AssistantMessageViewTests`。→ `src/QuickER.AI.UI/CLAUDE.md`「吹き出しの幅」
- **保存済みの API キーは「無い／読めた／読めない」を分け、読めないキーを空で上書きも削除もしない**：保存のオフで暗号ファイルが消える。ガード：`ApiKeyStoreTests`・`ConnectionSettingsTests`（行 `failure-unreadable-api-key-is-neither-overwritten-nor-deleted` と不変条件 1）。→ `src/QuickER.AI.UI/CLAUDE.md`「保存済みの API キー」
- **`ForceClose` は中断 → エンジンの破棄をスレッドプールで上限つきに通す**：常駐プロセスが孤児になる。ガード：`AiChatDialogForceCloseTests`・`EngineShutdownTests`。→ `src/QuickER.AI.UI/CLAUDE.md`「エンジンの破棄の作法」

### AI モック生成

- **モックフォルダの書き込みは `AtomicFile` で、ファイル → `mock.json` の順に成功してからマニフェストを差し替える**：書けなかった変更が次の保存へ紛れ込む。ガード：`MockFolderTests`（どの手の後にも「失敗した手は mock.json もメモリも変えない」を見張る・行 `save-screen-manifest-write-failure-does-not-leak-into-later-save`）。→ `src/QuickER.AI.Mock/CLAUDE.md`「保存単位とフォルダ」
- **初回ターンは成功で終わったときだけ済みにする**：失敗を済みにすると開始プロンプトが届かず、次の再開が「変更なし」になる。ガード：`MockFolderTests`（行 `session-start-*`・`session-feedback-*`）・`MockDialogTests`（どの行でも組が見張る・行 `converse-first-turn-*`）。→ `src/QuickER.AI.Mock/CLAUDE.md`「初回ターン」
- **モック生成のダイアログは、ターン中に接続方式とモックフォルダを変えず、作ったエンジンをちょうど 1 回破棄し、生成の可否と無効の理由を食い違わせない**：変えるとセッションを切り離して完了が届かずダイアログが固まり、破棄が漏れると常駐の子プロセスが残る。ガード：`MockDialogTests`（どの行でも組 `MockDialogHarness` が見張る）。→ `src/QuickER.AI.Mock/CLAUDE.md`「ダイアログの状態の不変条件」
- **モックのプレビューは遷移をフォルダ直下の html に限り、サブリソースを既定拒否し、ダウンロードを取り消す**：AI の HTML が外部へ通信・ファイルを置く。ガード：`MockPreviewPanelRuntimeTests`。→ `src/QuickER.AI.Mock/CLAUDE.md`「プレビュー」
- **最終 `dotnet build` は自分の `.sln` を明示し、MSBuild の自動 import を無効化し、直前に `obj` / `bin` を削除する**：AI が書いた obj のターゲットや rsp がビルドで実行される。祖先の global.json / NuGet.Config は無効化しない。ガード：`MockBuildBoundaryTests`（行 `args-*`・`clear-*`）。→ `src/QuickER.AI.Mock/CLAUDE.md`「最終ビルド」
- **成果物の探索はプロジェクトフォルダ配下だけを、アクセス拒否を無視して `MockProjectFiles` で走査する**：権限のないサブフォルダ 1 つでランが落ちる。ガード：`MockBuildBoundaryTests`（行 `enumerate-*`）。→ `src/QuickER.AI.Mock/CLAUDE.md`「成果物の探索」
- **最終ビルド前の変更検知は、ホワイトリスト差分と内容 SHA-256 で行い、読めないフォルダ・リンクは毎回「変更」に出す**：サンドボックス外のビルドで AI のコードが走る。確認手段が null ならビルドしない。ガード：`MockBuildBoundaryTests`（行 `change-*`）。→ `src/QuickER.AI.Mock/CLAUDE.md`「最終ビルド前の変更検知」
- **API キー方式の提出パス（`ResolveEmitPath`）は拡張子ホワイトリストのまま保ち、保護フォルダは綴りの別名にも負けない形で照合する**：拒否リストに戻すと自動 import でビルドが任意コード実行になる。ガード：`PathEntranceTests`（行 `emit-*`・`find-blocked-folder-*`）・`MockProjectEmitToolsTests`。→ `src/QuickER.AI.Mock/CLAUDE.md`「提出パスの判定」
- **モックプロジェクト生成の実行器 4 種は権限を QuickER 側で絞らない（API キーだけ境界あり）**：権限差の正本は docs/ai-chat.md の「注意」節。起動の設定と最終ビルドの境界越えの宣言は実行器の網がアダプタの宣言（出典つき）と照合する。ガード：`MockAgentContractTests`。→ `src/QuickER.AI.Mock/CLAUDE.md`「実行器 4 種」

### OS との境界
- **アプリ設定・作業状態の保存先はローカル（`AppDataPaths.LocalRoot`）の 1 つだけで、ローミングへ置かない**：DPAPI の秘密が他端末へ複製されても復号できない。ガード：`SourceRuleTests`（規則 `app-data-path-lookups`）・`AppDataPathsTests`。→ `src/QuickER.Platform/CLAUDE.md`「保存先」
- **`.cmd` / `.bat` シムの起動は必ず `BatchShimProcessGuard` を通す**：引数の `"` や `&` が cmd の再解釈で引用の外へ抜け、任意コマンドの実行につながる。ガード：`ProcessLaunchTests`（起動の表の行 `shim-*`・`resolve-*`）・`BatchShimProcessGuardTests`・`BatchShimTrailingNameTests`。→ `src/QuickER.Platform/CLAUDE.md`「.cmd / .bat シムの起動」
- **外部のプロセス・URL・フォルダを開く処理は `ExternalLauncher` を通す**：入口ごとに書くと URL のスキーム制限やフルパス解決が食い違う。ガード：`SourceRuleTests`（規則 `process-launches`・`process-launch-create-process-starts`）・`ProcessLaunchTests`（行 `external-*`）。→ `src/QuickER.Platform/CLAUDE.md`「外部の起動」
- **パスがフォルダの中にあるかの判定は `PathContainment` だけが持つ**：前方一致を入口ごとに書くと末尾の区切りの扱いが食い違い、ドライブ直下で配下を 1 件も承認しない。ガード：`PathContainmentTests`・`PathContainmentCallerFuzzTests`・`SourceRuleTests`（規則 `path-prefix-comparisons`）。→ `src/QuickER.Platform/CLAUDE.md`「パスがフォルダの中にあるかの判定」
- **利用者・AI から受け取った名前をパスに使う入口の判定は `PathSegments` だけが持つ**：入口ごとに持つと断る形が食い違う。ガード：`PathSegmentsTests`・`PathSegmentEntranceFuzzTests`・`SourceRuleTests`（規則 `path-name-checks`）。→ `src/QuickER.Platform/CLAUDE.md`「パスの名前の判定」
- **ファイルへの書き込みは `AtomicFile` を通し、UTF-8 は `TextEncodings` だけが作る**：素の書き込みは途中で落ちると利用者のファイルが中途半端な内容で残る。ガード：`SourceRuleTests`（規則 `raw-file-writes`・`utf8-encoding-creations`）・`AtomicFileTableTests`・`AtomicFileTests`。→ `src/QuickER.Platform/CLAUDE.md`「ファイルの書き込みと文字コード」
- **原子的な書き込みの一時ファイルの名前は `AtomicFile.TemporaryPathFor` だけが組み立てる**：元の名前をそのまま付けると長い名前の保存が必ず失敗する。ガード：`AtomicFileTableTests`（行 `atomic-temporary-path-*`・`atomic-is-temporary-file-name-*`）・`AtomicFileNamingAndMetadataTests`・`UnboundedBinaryReadToFileTests`。→ `src/QuickER.Platform/CLAUDE.md`「一時ファイルの名前」

### 設定
- **設定ファイルの保存は型が知らないキーを引き継ぎ、読み直しが `Unavailable` のときは書かない**：古い版で 1 度保存しただけで新しい版のキーが消える。ガード：`SettingsStoreTests`（行 `save-unknown-*`・`save-when-reread-is-unavailable-writes-nothing`）。→ `src/QuickER.Settings/CLAUDE.md`「知らないキーの引き継ぎ」
- **利用者の 1 操作を保存する呼び出し側は、保存の失敗（例外と `false`）を両方扱う**：握り潰すと保存できていないのに成功に見える。ガード：`ConnectionSettingsTests`（行 `failure-*` と不変条件 4）。→ `src/QuickER.Settings/CLAUDE.md`「保存の失敗の扱い」

### テストの土台

- **エンジンの共通の枠組みは `TestSupport/Engines/` の 7 部品（`SeedSource`・`KnownFailureSet`・`KnownCaseLedger`・`EngineReport`・`CoverageGuard`・`SeedFingerprint`・`SeedSummary`）を通す。BCL と xUnit だけに依存する**：WPF に依存すると Compile リンクする Integration のビルドが止まる。既存の 7 エンジンは載せ替え済みで、変数名・既定の種の数・開始番号は不変。ガード：`SeedSourceTests`・`KnownFailureSetTests`・`KnownCaseLedgerTests`・`CoverageGuardTests`。→ `tests/QuickER.Tests/TestSupport/CLAUDE.md`「エンジンの核（Engines/）」「既存エンジンの載せ替え」
- **エンジンは次元表（`DimensionTable`）と網羅の計数（`CoverageCounter`）、登録簿、因子の棚卸し、行列の軸で「何をどの水準まで流すか」を宣言する**：一覧の組が出るようになったら一覧から消す。種の指紋が変わったら指紋と一緒に一覧も書き直す。`isValid` には原理的に起こり得ない組だけを書く。ガード：`DimensionTableTests`・`CoverageCounterTests`・`FactorInventoryGuardTests`・`MatrixAxisGuardTests`。→ 同上「次元表と網羅」
- **テストの土台は共有の部品を通す（`GeneratedCodeCompiler`・`RuntimeReferenceSet`・`TwoHostToolHarness`・`ToolArgumentShapeFuzzRunner`・`OptionKeys`・`GuardKnownList`・`SourceFiles`）**：写しを作ると 1 か所だけ取り残される。ガード：`ToolHostAxisGuardTests`・`PhraseTests`（英語の行 `tool-definitions-*`）。→ 同上「共有の土台」
- **テストの一時フォルダは `TestTempFolder` で作り、親の名前のリテラルと `Directory.CreateTempSubdirectory` を書かない（作らない偽のパスだけ `TestTempFolder.Root` から組む）**：写すと消し忘れと失敗の経路での消し損ねが写り、%TEMP% に残りが溜まる。ガード：`SourceRuleTests`（規則 `test-temp-folder-root-copies`）。→ `tests/QuickER.Tests/TestSupport/CLAUDE.md`「共有の土台」
- **夜間の深い実行は `nightly.yml`（`QUICKER_ENGINE_PROFILE=nightly`・`NightlyEngine` の目印）。種を使うテストは目印の true / false のどちらかを必ず宣言し、結果は TRX で残す**：宣言漏れは `SeedSource.Resolve` が赤にする。夜間なら埋まる組は赤にせず出力へ書く。ガード：`WorkflowGuardTests.Nightly_JobsKeepTestResultsAsArtifacts`・`ParityTests`（行 `nightly-jobs`・`nightly-job-test-commands`・`nightly-job-engine-profile`）・`SeedSourceTests`。→ 同上「夜間の深い実行」
- **種の指紋の要約は機械のカルチャ・時差に依らない（`SeedSummary.UnderInvariantCulture`）**：日本語・日本時間で作った記録が英語・UTC の CI で食い違う。ガード：`EngineSummaryEnvironmentTests`・`SeedFingerprintTests`。→ 同上「種の指紋」
- **フォーカス・キー入力・Activated に左右される GUI テストは `WindowActivationTestCollection` に入れる。Space を流すテストは先に `PhysicalMouseButtons.WaitUntilReleased` を通す**：並列だと全件実行でだけ落ちる。実際のマウスのボタンが押されたままだと Space で Click しない。失敗時の状態は `GuiFailureDiagnostics`（`Capture` は必ず false を返し例外を出さない）が書く。ガード：`SourceRuleTests`（規則 `window-activation-collection`・`window-activation-markers`）・`GuiFailureDiagnosticsTests`・`PhysicalMouseButtonsTests`。→ 同上「GUI テストの土台」
- **ソースを走査するガードは条件コンパイルのどの分岐も読む（`ConditionalSourceParse`・`SourceSymbolScan`）。Roslyn のパースを直接呼ぶのは理由つきの一覧だけ**：1 通りのパースだと外れる分岐の禁止呼び出しを見落とす。ガード：`ConditionalSourceParseTests`・`SourceRuleTests`（規則 `direct-roslyn-parses`）・`SourceRuleTests`（自己検査の行 `*-inactive-branch`・`SourceRuleTests.SymbolScan_ResolvesBclCallsInEveryProject`）。→ 同上「ソースを走査するガード」
- **テストで DbContext のプロバイダを指定する呼び出しの直後には `.IgnoreServiceProviderCacheLimit()` を続ける**：EF Core のサービスプロバイダが 20 を超えると関係の無いテストが全件実行で落ちる。ガード：`EfCoreServiceProviderLimitGuardTests`・`SourceRuleTests`（規則 `ef-core-provider-cache-limit`）。→ 同上「EF Core のテストの構成」
- **テストが作る同期・実装先の一致の SQLite の DB は `SqliteLightJournal`（MEMORY・synchronous=OFF）で開く。`PERSIST` にしない**：Windows で既定の接続との混在が `disk I/O error` になる。ガード：`SqliteLightJournalTests`。→ 同上「SQLite のテストの DB」
- **簡易 DSL の条件の意味は総当たり（`DslConditionFuzzTests`）で docs の意味と照合する。オラクル `DslFuzz.Evaluate` は docs の写しで、実装の挙動を写さない**：既知の失敗の一覧は空で保つ。網羅の単位 `DslFuzzUnits.Coverage` を種の前に流す。ガード：`DslConditionFuzzTests`・`DslConditionFuzzSqlServerRuntimeTests`。→ 同上「簡易 DSL の総当たり」

### テストの土台（Integration）

- **同期計画の総当たりは、実 DB の総当たりに静的な規則（R1〜R6）・能力の組の網・決定的な行・形の行・来歴の行・網羅の行を足し、同じランナーと既知の失敗の一覧で照合する**：実 DB が断る形を予言できない規則や、網羅の行を夜間だけに回すと、普段の実行で偽の欠け・見逃しが出る。ガード：`SyncPlanCapabilityNetTestsBase`・`SyncPlanFuzzOracleTests`。→ `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」「網羅の行」
- **同期の差分と計画の期待は期待の行 `SyncPlanExpectationRows` に置き、実 DB の段は汎用の表明 1〜5 で流す（既知の失敗の一覧は通さない）**：計画が警告しない失敗は `ExpectsUnwarnedFailure(理由)` の行でだけ受ける。ガード：`SyncPlanFuzzTestsBase`・`SyncPlanCapabilityNetTestsBase`。→ `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」
- **失敗後に制約が消えていないことは規則 1〜5 で強く表明し、1 種の例外は記録して次の種へ進む**：例外でテストごと止まると前の種の違反が隠れる。ガード：`SyncPlanFuzzOracleTests`・`SyncPlanFuzzRunnerGuardTests`。→ `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「総当たりの表明と運用」
- **実装先どうしの一致は、生成器の手・結果の表・網羅の行・決まった形のシナリオ・比べ方の例外 2 つで固定し、外部キーとナビゲーションが食い違うグラフは乱数の列で作らない**：作ると保存の手が断られるだけで後の列が何も比べない。ガード：`BackendParityRuntimeTests`・`BackendParitySqlServerRuntimeTests`。→ `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「実装先どうしの一致」
- **生成コードのランタイムテストは「共通基底 1＋バックエンド派生 N＋派生固有 Fact」で書き、存在しない面はスキップでなくサブクラス階層で分ける**：派生を足すだけで全共通シナリオが掛かり、機能×実装の網に穴が開かない。ガード：`SaveHookRuntimeTestsBase`・`ConcurrencyRuntimeTestsBase`。→ `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「ランタイムテストの形」
- **同期のエンジンの振る舞いは、同期のモデルの 3 つの Theory（直結・HTTP・実 SQL Server）が同じ宣言のシナリオを流して確かめる（Theory は分割して並列化する）**：分割がシナリオを落とす・重ねると静かに行が欠ける。ガード：`SyncModelRuntimeTests`・`SyncRuntimeTestsBase`。→ `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「双方向同期のテスト」
- **同期のモデルは「止まらない・収束・消失なし・根拠のない競合なし」を乱数の操作の列で表明し、構成の行列と割り込みの点で補う**：根拠のない競合や空振りの割り込みを見逃すと、同期の退行が緑のまま残る。ガード：`SyncModelRuntimeTests`・`SyncConfigMatrixRuntimeTests`。→ `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「同期のモデル（SyncModelRuntimeTests）」

### docs と CLAUDE.md の照合
- **docs の方言の表は宣言と一致する（目印 `<!-- dialect-table: 名前 -->`）**：登録の無い表は照合されず静かに嘘になる。ガード：`DialectDocsTableTests`。→ `tests/QuickER.Tests/Docs/CLAUDE.md`「docs の方言の表」
- **ルートの `CLAUDE.md` の機械照合できる一覧（依存矢印・プロジェクト網羅・フォルダ一覧・固定フィクスチャ一覧）は書式が契約**：書式を変えるときはテストも更新する。ガード：`ClaudeMdParityTests`。→ `tests/QuickER.Tests/Docs/CLAUDE.md`「CLAUDE.md の照合」
- **src のコメント・ワークフロー・スクリプト・生成テンプレートが名指すテストの名前と行 Id は実在する（読み方は CLAUDE.md の照合と同じ関数）**：テストの改名・表の行への移し替えで名指しが古いまま残る。ガード：`SourceRuleTests`（規則 `comment-test-names-src`・`comment-test-names-templates`・`comment-test-names-workflows`・`comment-test-names-scripts`）。→ `tests/QuickER.Tests/Docs/CLAUDE.md`「src・ワークフロー・スクリプト・生成テンプレートの名指し」

## コーディング規約

- 既存コードと同様、ビヘイビアや主要メソッドに分かりやすい日本語コメントを付ける（**samples/ 配下の手書きコードは例外で英語コメント**）
- `if` / `for` / `switch` / `try` 等のブロック前後には空行を1行入れる（最終判断は CSharpier の挙動を優先）
- 文字列リテラルの定数化は一貫性重視：一部の表示文言だけを定数化せず、やるなら同種をまとめて整理する
- ビルド警告は可能な限り解消する

## ワークフロー

- `git add` / `git commit` は**ユーザーの明示的な指示があるまで実行しない**（変更はワーキングツリーに残してレビューを待つ）。ブランチ作成はタスク開始時に行ってよい
- **コミットメッセージは「件名 70 字前後の要約＋空行＋本文に詳細」**。設計判断・検証結果の全文は本文へ書く（情報量は減らさない）。件名は切り詰めでなく要約として完結させ、履歴から識別できる固有性（機能名・対象コンポーネント名）を持たせる。Co-Authored-By トレーラは本文末尾
- 計画・教訓は `tasks/todo.md` / `tasks/lessons.md` に記録する運用。**tasks/ はリポジトリに含めない**（メンテナのローカルで非公開管理・.gitignore 済み）。tasks/ が存在する環境では、セッション開始時に lessons.md を確認する
- レイアウト等ビジュアルに直結する機能は、機能テストに加えて見た目の品質指標（占有面積・充填率・交差数・線長）を実測してから完了とする（lessons.md 参照）
- **不変条件を足すときは、その不変条件を壊すと赤になるガードテストを同時に足し、このファイルの索引に 1 行（何を守るか・壊すと何が起きるか・ガードテスト名・詳細の場所）、詳細をその領域のフォルダの CLAUDE.md に書く**（ガードテスト名は両方に書く）。詳細を書く CLAUDE.md が無い領域なら、そのフォルダに新しく作る。文章だけの不変条件は静かに嘘になる——記述と実装が食い違ったまま残っていた例（性能の doc・ServerWins の docs 乖離）があったための運用。書いたテスト名・CLAUDE.md のパスと見出しは `ClaudeMdReferenceTests` が実在を照合する
- **GitHub Actions のアクションは commit SHA でピンする**（`uses: owner/action@<sha> # vX.Y.Z`）。タグは可変で、上流のタグ付け替え・乗っ取りで実行内容が黙って差し替わる（公式アクションも例外にしない）。更新は `.github/dependabot.yml`（github-actions・weekly）が SHA とバージョンコメントを両方書き換える PR として持ってくるので、手で追わない。NuGet は Directory.Packages.props の中央管理＋ロックファイルで別管理のため Dependabot の監視対象に入れていない
- **公開する NuGet パッケージの一覧は `scripts/pack-packages.ps1` の 1 か所だけに置く**（ci.yml・publish.yml はこのスクリプトを呼ぶだけで一覧を写さない。スクリプトは pack を 1 本ずつ終了コードで検査し、続けて `verify-packages.ps1` を一覧の本数で呼ぶ。LicenseDocumentParityTests もこの一覧を公開パッケージの正本として読む）
  - **ci.yml の paths-ignore にテストが読むファイル（docs/・ルート直下の *.md・LICENSE・各フォルダの CLAUDE.md）を入れない**（文書だけの変更でガードが走らなくなる）
  - **publish.yml の push は `--skip-duplicate` を残し**（途中で失敗した公開のやり直しのため）、その前の `scripts/check-unpublished-version.ps1` が、全パッケージがその版で公開済みのときだけ止める（`VersionPrefix` の上げ忘れ。一部だけ公開済みはやり直しとして続行・照会の失敗は止める・dry_run では実行しない）
  - 固定は `WorkflowGuardTests`（一覧の写し・pack の失敗の伝達・本数の追従・paths-ignore・PR だけの取り消し・サンプルの再ビルド・リリースのタグの `--target`・既存リリースの照会の失敗・公開済みの版の確認の位置と判定）
