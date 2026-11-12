# 生成コードのランタイムの機能 × 実装先の行列

<!-- 生成物（RuntimeTestMatrixTests が宣言から作る）。手で直さない。書き直し（リポジトリ直下・PowerShell）：$env:QUICKER_REGEN_FIXTURES=1; dotnet test tests/QuickER.Tests.Integration/QuickER.Tests.Integration.csproj --filter "FullyQualifiedName~RuntimeMatrixDrift"; $env:QUICKER_REGEN_FIXTURES=$null -->

生成コードのランタイムのテスト（tests/QuickER.Tests.Integration）を、機能ごとに実装先ごとの担当のテストのクラス・エンジンと、担当でないセルの理由で並べる。
出どころはテストのクラスの RuntimeCell 属性・実装先を比べるエンジンの登録（RuntimeMatrixEngines）・行の表（RuntimeMatrixRows）。

## 実装先

| 実装先 | 見出し | 生成の実装 | 方言 |
|---|---|---|---|
| `AdoSqlServer` | QuickER 版 SQL Server | QuickER 版 Repository（ADO.NET を直接使う生成の実装） | sqlserver |
| `AdoSqlite` | QuickER 版 SQLite | QuickER 版 Repository（ADO.NET を直接使う生成の実装） | sqlite |
| `EfCoreSqlServer` | EF Core SQL Server | EF Core 版 Repository | sqlserver |
| `EfCoreSqlite` | EF Core SQLite | EF Core 版 Repository | sqlite |
| `EfCorePostgreSql` | EF Core PostgreSQL | EF Core 版 Repository | postgresql |
| `EfCoreMySql` | EF Core MySQL | EF Core 版 Repository | mysql |
| `EfCoreOracle` | EF Core Oracle | EF Core 版 Repository | oracle |
| `InMemory` | インメモリ | インメモリ Repository | — |
| `Remote` | リモート | HTTP クライアント＋生成サーバー（in-process の Kestrel） | — |

## セルの語

| 語 | 意味 |
|---|---|
| 担当 | 担当のテストのクラスかエンジンがある |
| 担当（サーバー：…） | リモートの担当。HTTP クライアントの裏のサーバーの実装先を並べる（リポジトリなし＝転送だけのスタブ・HTTP を使わない検査） |
| 空き（区切り） | まだ検証していない。括弧は直す区切り（決まっていなければきっかけ待ち） |
| 割り切り | 意図して検証しない（根拠の docs か CLAUDE.md の見出しを添える） |
| 非該当 | その組み合わせが原理的に成り立たない |

## 行列

| 機能 | QuickER 版 SQL Server | QuickER 版 SQLite | EF Core SQL Server | EF Core SQLite | EF Core PostgreSQL | EF Core MySQL | EF Core Oracle | インメモリ | リモート |
|---|---|---|---|---|---|---|---|---|---|
| `CrudParity` | 担当 | 担当 | 担当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `Concurrency` | 担当 | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当 | 担当（サーバー：QuickER 版 SQL Server・インメモリ・リポジトリなし） |
| `EditModelSave` | 担当 | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当 | 担当（サーバー：QuickER 版 SQL Server・インメモリ） |
| `ConcurrencyVo` | 担当 | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当 | 担当（サーバー：QuickER 版 SQL Server・インメモリ） |
| `SaveHook` | 担当 | 担当 | 担当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `BinaryColumn` | 担当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQL Server・EF Core SQLite・リポジトリなし） |
| `BinaryColumnEditModel` | 空き（きっかけ待ち） | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 空き（きっかけ待ち） | 非該当 |
| `UniquenessCheck` | 担当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite） |
| `UniquenessEnforcement` | 非該当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `IncludeGraph` | 担当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 非該当 |
| `GraphSave` | 担当 | 担当 | 担当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `BackendParity` | 担当 | 担当 | 担当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQL Server・EF Core SQLite） |
| `EfCoreExecutionStrategy` | 非該当 | 非該当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 非該当 | 非該当 |
| `IgnoredWrite` | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 非該当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `IncludeGraphSelfReference` | 担当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 担当 | 非該当 |
| `NamedQuery` | 担当 | 担当 | 担当 | 担当 | 空き（区切り D3） | 空き（区切り D3） | 空き（区切り D3） | 担当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `NamedQueryRawSql` | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 非該当 | 担当（サーバー：QuickER 版 SQLite・EF Core SQLite） |
| `TranslatorOperator` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 |
| `ValueObjectStringMatch` | 担当 | 担当 | 空き（きっかけ待ち） | 担当 | 空き（区切り D3） | 空き（区切り D3） | 空き（区切り D3） | 担当 | 非該当 |
| `TranslatorParity` | 担当 | 担当 | 担当 | 担当 | 空き（区切り D3） | 空き（区切り D3） | 空き（区切り D3） | 担当 | 非該当 |
| `SchemaBootstrap` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 |
| `MultiTarget` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） |
| `MultiTargetRowVersion` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） |
| `ComputedColumn` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 空き（きっかけ待ち） |
| `IdentityColumn` | 担当 | 非該当 | 担当 | 非該当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 非該当 | 担当（サーバー：EF Core SQL Server） |
| `SyncSupport` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当（サーバー：QuickER 版 SQL Server・QuickER 版 SQLite・リポジトリなし） |
| `SyncRefresh` | 担当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当（サーバー：QuickER 版 SQLite） |
| `DialectPortability` | 非該当 | 非該当 | 空き（きっかけ待ち） | 担当 | 担当 | 担当 | 担当 | 非該当 | 非該当 |
| `EditModelValidateUnique` | 担当 | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） |
| `RemoteContract` | 割り切り | 担当 | 割り切り | 担当 | 割り切り | 割り切り | 割り切り | 割り切り | 非該当 |
| `RemoteTransport` | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 担当（サーバー：QuickER 版 SQL Server・QuickER 版 SQLite・リポジトリなし） |
| `SqlExecutorInjection` | 割り切り | 担当 | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 空き（きっかけ待ち） | 非該当 | 非該当 |
| `ForeignKeyDefault` | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 |
| `BusyTimeoutDefault` | 非該当 | 担当 | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 |
| `ValueConversion` | 非該当 | 担当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 | 非該当 |

## 機能ごとの詳細

### `CrudParity`

CRUD・グラフ保存・Query() の共通シナリオ（パリティ基底が同一シナリオを各実装へ掛ける）

- QuickER 版 SQL Server：担当＝`GeneratedRuntimeAdoParityTests`・`SchemaQualifiedTableRuntimeTests`・`SqlServerPlainQueryRuntimeTests`・`SqlServerQueryPipelineRuntimeTests`
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 CrudParityRemote）・`GeneratedSqliteAdoRuntimeTests`
- EF Core SQL Server：担当＝`EfCoreCommandTimeoutRuntimeTests`・`GeneratedRuntimeEfCoreParityTests`・`GeneratedRuntimeEfCoreSpecificTests`・`SchemaQualifiedTableRuntimeTests`
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 CrudParityRemote）・`EfCoreExecuteDeleteRuntimeTests`・`EfCoreKeyParameterRuntimeTests`・`GeneratedSqliteEfCoreParityRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（シナリオの群 CrudParityRemote）・`InMemoryFixtureRuntimeTests`
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 CrudParityRemote）・`RemoteServiceAdoRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteServiceEfCoreRuntimeTests`（サーバー：EF Core SQLite）

### `Concurrency`

rowversion による楽観排他（版ガード・SaveConflictException の分類）

- QuickER 版 SQL Server：担当＝`SqlServerConcurrencyRuntimeTests`
- QuickER 版 SQLite：非該当＝SQLite 単独の図に rowversion 列は生まれない（timestamp は日時の別名へ解決される）ため、版ガードの対象そのものが存在しない。マルチターゲットでの非対称は MultiTargetRowVersion 行が持つ
- EF Core SQL Server：担当＝`EfCoreConcurrencyRuntimeTests`
- EF Core SQLite：非該当＝SQLite 単独の図に rowversion 列は生まれない（timestamp は日時の別名へ解決される）ため、版ガードの対象そのものが存在しない。マルチターゲットでの非対称は MultiTargetRowVersion 行が持つ
- EF Core PostgreSQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core MySQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core Oracle：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- インメモリ：担当＝`InMemoryConcurrencyRuntimeTests`・`SaveConflictDetailsRuntimeTests`
- リモート：担当＝`ConcurrencyRemoteAdoSqlServerRuntimeTests`（サーバー：QuickER 版 SQL Server）・`RemoteConcurrencyRuntimeTests`（サーバー：インメモリ）・`SaveConflictDetailsRuntimeTests`（サーバー：リポジトリなし）

### `EditModelSave`

EditModel の保存の受け入れ（AcceptSaved＝版・DB とフックが書いた値の読み戻し・フックが飛ばした行・足して消した子）

- QuickER 版 SQL Server：担当＝`EditModelSaveSqlServerRuntimeTests`
- QuickER 版 SQLite：非該当＝シナリオは rowversion 列の版を読み戻す形で、SQLite 単独の図に rowversion 列は生まれない。読み戻しの処理は実装先に依らない（マッパーの共通部）
- EF Core SQL Server：担当＝`EditModelSaveEfCoreRuntimeTests`
- EF Core SQLite：非該当＝シナリオは rowversion 列の版を読み戻す形で、SQLite 単独の図に rowversion 列は生まれない。読み戻しの処理は実装先に依らない（マッパーの共通部）
- EF Core PostgreSQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core MySQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core Oracle：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- インメモリ：担当＝`EditModelSaveInMemoryRuntimeTests`
- リモート：担当＝`EditModelSaveRemoteAdoSqlServerRuntimeTests`（サーバー：QuickER 版 SQL Server）・`EditModelSaveRemoteRuntimeTests`（サーバー：インメモリ）

### `ConcurrencyVo`

楽観排他 × 値オブジェクト（版プロパティが VO 型になる経路の Wrap/Unwrap）

- QuickER 版 SQL Server：担当＝`ConcurrencyVoSqlServerRuntimeTests`
- QuickER 版 SQLite：非該当＝Concurrency 行と同じ理由（SQLite 単独図に rowversion 列が無い）
- EF Core SQL Server：担当＝`ConcurrencyVoEfCoreRuntimeTests`
- EF Core SQLite：非該当＝Concurrency 行と同じ理由（SQLite 単独図に rowversion 列が無い）
- EF Core PostgreSQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core MySQL：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- EF Core Oracle：非該当＝rowversion 型は SQL Server だけにあり（src/QuickER.Runtime/CLAUDE.md「rowversion による楽観排他」の既知の制限）、PostgreSQL・MySQL・Oracle の図に版の列が生まれないため、版ガードの対象そのものが存在しない
- インメモリ：担当＝`ConcurrencyVoInMemoryRuntimeTests`
- リモート：担当＝`ConcurrencyVoRemoteAdoSqlServerRuntimeTests`（サーバー：QuickER 版 SQL Server）・`ConcurrencyVoRemoteRuntimeTests`（サーバー：インメモリ）

### `SaveHook`

ISaveHook（Before のスキップ・After のトランザクション参加）

- QuickER 版 SQL Server：担当＝`BackendParitySqlServerRuntimeTests`・`SaveHookBinaryWriteSqlServerRuntimeTests`・`SaveHookSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 SaveHook・SaveHookFullSurface・SaveHookBlobStream・SaveHookForeignKey・SaveHookWriteBack）・`SaveHookAdoRuntimeTests`・`SaveHookBinaryWriteSqliteRuntimeTests`
- EF Core SQL Server：担当＝`BackendParitySqlServerRuntimeTests`・`EfCoreCommandTimeoutRuntimeTests`
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 SaveHook・SaveHookFullSurface・SaveHookBlobStream・SaveHookForeignKey・SaveHookWriteBack）・`SaveHookEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（シナリオの群 SaveHook・SaveHookFullSurface・SaveHookBlobStream・SaveHookForeignKey・SaveHookWriteBack）・`SaveHookInMemoryRuntimeTests`・`SaveHookRegistrationRuntimeTests`
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 SaveHook・SaveHookFullSurface・SaveHookBlobStream・SaveHookForeignKey・SaveHookWriteBack）・`SaveHookRemoteRuntimeTests`（サーバー：QuickER 版 SQLite）

### `BinaryColumn`

無制限バイナリ列の除外・WithUnboundedBinary・Stream アクセサ

- QuickER 版 SQL Server：担当＝`SqlServerBinaryColumnRuntimeTests`
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 BinaryColumn・BinaryColumnFullSurface・BinaryColumnRawSql・BinaryColumnBlobStream・BinaryColumnBlobStreamFullSurface）・`BinaryColumnAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 BinaryColumn・BinaryColumnFullSurface・BinaryColumnRawSql・BinaryColumnBlobStream・BinaryColumnBlobStreamFullSurface）・`BinaryColumnEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（シナリオの群 BinaryColumn・BinaryColumnFullSurface・BinaryColumnRawSql・BinaryColumnBlobStream・BinaryColumnBlobStreamFullSurface）・`BinaryInMemoryFixtureRuntimeTests`
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 BinaryColumn・BinaryColumnFullSurface・BinaryColumnRawSql・BinaryColumnBlobStream・BinaryColumnBlobStreamFullSurface）・`BinaryColumnRemoteRuntimeTests`（サーバー：QuickER 版 SQLite）・`BinaryRemoteLengthRuntimeTests`（サーバー：QuickER 版 SQLite）・`BinaryTransferResponseRuntimeTests`（サーバー：リポジトリなし）・`RemoteEfCoreUnboundedBinaryRuntimeTests`（サーバー：EF Core SQLite）・`RemoteEfCoreUnboundedBinarySqlServerRuntimeTests`（サーバー：EF Core SQL Server）・`RemoteRequestMarkRuntimeTests`（サーバー：リポジトリなし）

### `BinaryColumnEditModel`

無制限バイナリ除外 × 値オブジェクト × EditModel / Mapper の往復（未取得の除外列を素通しする保存）

- QuickER 版 SQL Server：空き（きっかけ待ち）＝EditModel / Mapper は方言に依らない生成物で、除外の意味論は SQLite で代表させている（SQL Server 側の除外の実 DB 検証は BinaryColumn 行が持つ）
- QuickER 版 SQLite：担当＝`BinaryVoEditModelRuntimeTests`
- EF Core SQL Server：非該当＝EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない
- EF Core SQLite：非該当＝EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない
- EF Core PostgreSQL：非該当＝EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない
- EF Core MySQL：非該当＝EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない
- EF Core Oracle：非該当＝EF Core は列選択を自身が行うため除外が非適用＝除外列が未取得になる状況が発生しない
- インメモリ：空き（きっかけ待ち）＝インメモリも除外の意味論を持つが、往復の主題は「実 DB の UPDATE が除外列に触れない」ことなので実 DB 側を先に押さえている
- リモート：非該当＝EditModel / Mapper はクライアント側の型でリモート面を経由しない

### `UniquenessCheck`

UNIQUE 制約の事前チェック（重複検出・自分自身の除外）

- QuickER 版 SQL Server：担当＝`UniquenessCheckSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`UniquenessCheckAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`UniquenessCheckEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`UniquenessCheckInMemoryRuntimeTests`
- リモート：担当＝`UniquenessCheckRemoteRuntimeTests`（サーバー：QuickER 版 SQLite）

### `UniquenessEnforcement`

書き込み時の UNIQUE 制約強制（インメモリが実 DB の制約の代役を務める。実 DB の実装先は、DB の拒否が呼び出し側へ同じ形で届き何も書かないことを実装先の一致が比べる）

- QuickER 版 SQL Server：非該当＝実 DB では制約が DDL の実体として存在し、拒否するのは DB 自身＝生成コードの挙動ではない（SQLite の実装先の拒否の届き方は実装先の一致が比べ、SQL Server 方言の実装先の一致は版の系統だけを持つ）
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 UniquenessEnforcement）
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 UniquenessEnforcement）
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（シナリオの群 UniquenessEnforcement）・`InMemoryUniquenessRuntimeTests`
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 UniquenessEnforcement）

### `IncludeGraph`

グラフ取得糖衣（IncludeGraph＝カスケード閉包の Include ツリー）

- QuickER 版 SQL Server：担当＝`IncludeGraphSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`IncludeGraphAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`IncludeGraphEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`IncludeGraphInMemoryRuntimeTests`
- リモート：非該当＝Query() はリモート面に無く、Include ツリーは転送されない

### `GraphSave`

グラフ保存がカスケードの閉包だけを保存する（親への逆参照を辿らない・削除の根の子孫）

- QuickER 版 SQL Server：担当＝`BackendParitySqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 GraphSaveOrder・GraphSaveRuntime・GraphSaveRuntimeRemote）・`GraphSaveAdoRuntimeTests`
- EF Core SQL Server：担当＝`BackendParitySqlServerRuntimeTests`・`GraphSaveEfCoreSqlServerRuntimeTests`
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 GraphSaveOrder・GraphSaveRuntime・GraphSaveRuntimeRemote）・`GraphSaveEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（シナリオの群 GraphSaveOrder・GraphSaveRuntime・GraphSaveRuntimeRemote）
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 GraphSaveOrder・GraphSaveRuntime・GraphSaveRuntimeRemote）

### `BackendParity`

実装先どうしの一致（同じ操作の列を流し、戻り値・保存先・呼び出し側のグラフ・Save フックを QuickER 版と比べる）

- QuickER 版 SQL Server：担当＝`BackendParitySqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（全シナリオ）
- EF Core SQL Server：担当＝`BackendParitySqlServerRuntimeTests`
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（全シナリオ）
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`BackendParityRuntimeTests`（全シナリオ）
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／全シナリオ）・`BackendParitySqlServerRuntimeTests`（サーバー：EF Core SQL Server）

### `EfCoreExecutionStrategy`

再試行する実行戦略のもとで、自前のトランザクションを開く経路（フックありの保存・カスケードの一括削除）を試行ごとに組み直す

- QuickER 版 SQL Server：非該当＝実行戦略は EF Core の機構
- QuickER 版 SQLite：非該当＝実行戦略は EF Core の機構
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 EfCoreExecutionStrategy（EfCore の実装先だけを数える））
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：非該当＝実行戦略は EF Core の機構
- リモート：非該当＝実行戦略は EF Core の機構

### `IgnoredWrite`

版の無い表で競合以外の理由により更新が当たらないときの扱い（行なしとして報告・やり直さない）

- QuickER 版 SQL Server：空き（きっかけ待ち）＝QuickER 版は SQL Server・SQLite とも版の無い表で 0 行を実在確認しない同じ規則で、SQLite で代表させている
- QuickER 版 SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 IgnoredWrite）
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`BackendParityRuntimeTests`（シナリオの群 IgnoredWrite）
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：非該当＝インメモリにはトリガー・行レベルセキュリティが無く、文が当たらない状況が生まれない
- リモート：担当＝`BackendParityRuntimeTests`（サーバー：QuickER 版 SQLite・EF Core SQLite／シナリオの群 IgnoredWrite）

### `IncludeGraphSelfReference`

IncludeGraph の edge-skip（自己参照ナビを展開しない）

- QuickER 版 SQL Server：担当＝`IncludeGraphSelfReferenceSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`IncludeGraphSelfReferenceAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`IncludeGraphSelfReferenceEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：担当＝`IncludeGraphSelfReferenceInMemoryRuntimeTests`
- リモート：非該当＝Query() はリモート面に無く、Include ツリーは転送されない

### `NamedQuery`

名前付きクエリ（簡易 DSL・射影・手動実装）

- QuickER 版 SQL Server：担当＝`DslConditionFuzzSqlServerRuntimeTests`（実装先の軸 ado・efcore（方言 sqlserver））
- QuickER 版 SQLite：担当＝`NamedQueryAdoRuntimeTests`・`SqlitePortableNamedQueryRuntimeTests`
- EF Core SQL Server：担当＝`DslConditionFuzzSqlServerRuntimeTests`（実装先の軸 ado・efcore（方言 sqlserver））
- EF Core SQLite：担当＝`NamedQueryEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core MySQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core Oracle：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- インメモリ：担当＝`NamedQueryInMemoryRuntimeTests`
- リモート：担当＝`RemoteServiceAdoRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteServiceEfCoreRuntimeTests`（サーバー：EF Core SQLite）

### `NamedQueryRawSql`

名前付きクエリの生 SQL 実装（戻り形・パラメータ展開）

- QuickER 版 SQL Server：空き（きっかけ待ち）＝SQL Server の名前付きクエリの実行時テストは簡易 DSL の総当たりだけで、生 SQL の実装は未整備
- QuickER 版 SQLite：担当＝`NamedQueryAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`NamedQueryEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：非該当＝生 SQL の実装先を持たないインメモリでは契約宣言のみが生成され、実装は利用者の partial 実装になる（「SQL が与えられていない実装先は手動実装」の統一規則）
- リモート：担当＝`RemoteServiceAdoRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteServiceEfCoreRuntimeTests`（サーバー：EF Core SQLite）

### `TranslatorOperator`

式木 → SQL 翻訳の演算子分岐（NULL 意味論の補償・bool 列の短縮形）

- QuickER 版 SQL Server：担当＝`TranslatorParitySqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`TranslatorParitySqliteRuntimeTests`
- EF Core SQL Server：非該当＝EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う（SqlExpressionTranslator を通らない）
- EF Core SQLite：非該当＝EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う（SqlExpressionTranslator を通らない）
- EF Core PostgreSQL：非該当＝EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う（SqlExpressionTranslator を通らない）
- EF Core MySQL：非該当＝EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う（SqlExpressionTranslator を通らない）
- EF Core Oracle：非該当＝EF Core は式木を Where へ直接渡し、翻訳は EF Core の LINQ プロバイダが行う（SqlExpressionTranslator を通らない）
- インメモリ：非該当＝インメモリは式木をコンパイルして C# 意味論で評価する（SQL を組み立てない）
- リモート：非該当＝Query() はリモート面に無く、式木は転送されない

### `ValueObjectStringMatch`

値オブジェクトの文字列一致（Contains / StartsWith / EndsWith）の判定を、具象の値オブジェクト型から引いた MethodInfo でも同じにする

- QuickER 版 SQL Server：担当＝`StringMatchMethodSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`StringMatchMethodAdoRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core SQLite：担当＝`StringMatchMethodEfCoreRuntimeTests`
- EF Core PostgreSQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core MySQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core Oracle：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- インメモリ：担当＝`StringMatchMethodInMemoryRuntimeTests`
- リモート：非該当＝Query() はリモート面に無く、式木は転送されない

### `TranslatorParity`

式木の翻訳・識別子の引用・Include の照合・UPDATE 文の組み立てを C# の意味論と突き合わせる共通シナリオ

- QuickER 版 SQL Server：担当＝`TranslatorParitySqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`TranslatorParitySqliteRuntimeTests`
- EF Core SQL Server：担当＝`TranslatorParityEfCoreSqlServerRuntimeTests`
- EF Core SQLite：担当＝`TranslatorParityEfCoreSqliteRuntimeTests`
- EF Core PostgreSQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core MySQL：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- EF Core Oracle：空き（区切り D3）＝PostgreSQL・MySQL・Oracle の EF Core 版のクエリ（述語・簡易 DSL・照合順序・空文字列）の照合は区切り D3 が足す
- インメモリ：担当＝`TranslatorParityInMemoryRuntimeTests`
- リモート：非該当＝Query() はリモート面に無く、式木は転送されない

### `SchemaBootstrap`

生成 DDL の適用ヘルパー（ApplyDdlAsync）

- QuickER 版 SQL Server：担当＝`SqlServerSchemaBootstrapRuntimeTests`
- QuickER 版 SQLite：担当＝`SqliteSchemaBootstrapRuntimeTests`
- EF Core SQL Server：非該当＝スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用
- EF Core SQLite：非該当＝スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用
- EF Core PostgreSQL：非該当＝スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用
- EF Core MySQL：非該当＝スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用
- EF Core Oracle：非該当＝スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用
- インメモリ：非該当＝インメモリにスキーマの概念が無い
- リモート：非該当＝スキーマ適用はサーバー側の運用作業でリモート面に無い

### `MultiTarget`

マルチターゲット生成（中立契約 1 回＋方言別実装・keyed DI）

- QuickER 版 SQL Server：担当＝`MultiTargetRepositoryRuntimeTests`
- QuickER 版 SQLite：担当＝`MultiTargetRepositoryRuntimeTests`・`MultiTargetRepositorySqliteKeyedResolutionTests`
- EF Core SQL Server：非該当＝マルチターゲットと EF Core は生成時に排他（診断エラー）
- EF Core SQLite：非該当＝マルチターゲットと EF Core は生成時に排他（診断エラー）
- EF Core PostgreSQL：非該当＝マルチターゲットと EF Core は生成時に排他（診断エラー）
- EF Core MySQL：非該当＝マルチターゲットと EF Core は生成時に排他（診断エラー）
- EF Core Oracle：非該当＝マルチターゲットと EF Core は生成時に排他（診断エラー）
- インメモリ：空き（きっかけ待ち）＝インメモリとの併用はコンパイル行列で押さえているが、keyed DI に方言実装とインメモリを同居させる実行時テストは無い
- リモート：空き（きっかけ待ち）＝リモート面との併用は直交だが、実行時の組合せ検証は無い

### `MultiTargetRowVersion`

マルチターゲット × rowversion（SQL Server は書き込み除外＋版ガード・SQLite は通常列）

- QuickER 版 SQL Server：担当＝`MultiTargetRowVersionRuntimeTests`
- QuickER 版 SQLite：担当＝`MultiTargetRowVersionRuntimeTests`・`MultiTargetRowVersionSqliteRuntimeTests`
- EF Core SQL Server：非該当＝マルチターゲットと EF Core は生成時に排他
- EF Core SQLite：非該当＝マルチターゲットと EF Core は生成時に排他
- EF Core PostgreSQL：非該当＝マルチターゲットと EF Core は生成時に排他
- EF Core MySQL：非該当＝マルチターゲットと EF Core は生成時に排他
- EF Core Oracle：非該当＝マルチターゲットと EF Core は生成時に排他
- インメモリ：空き（きっかけ待ち）＝インメモリは擬似版を採番する SQL Server の代役として振る舞う（案 B のスコープ外）。ハイブリッド構成での併用は未検証
- リモート：空き（きっかけ待ち）＝ミラー列をリモート面越しに往復させる専用テストは無い（HTTP 越しに実 rowversion を運ぶ経路自体は SyncSupport 行が押さえている）

### `ComputedColumn`

計算列・生成列（[ComputedColumn] による INSERT / UPDATE 除外・SELECT では読む）

- QuickER 版 SQL Server：担当＝`SqlServerComputedColumnRuntimeTests`
- QuickER 版 SQLite：担当＝`SqliteComputedColumnDiagramDdlRuntimeTests`・`SqliteComputedColumnRuntimeTests`
- EF Core SQL Server：非該当＝計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する
- EF Core SQLite：非該当＝計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する
- EF Core PostgreSQL：非該当＝計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する
- EF Core MySQL：非該当＝計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する
- EF Core Oracle：非該当＝計算列フィクスチャはマルチターゲットで、EF Core とは生成時に排他。Fluent の ValueGeneratedOnAddOrUpdate は生成テキストの両アームテストが固定する
- インメモリ：非該当＝インメモリは式を評価しない＝計算列も普通のプロパティとして値を保つ（意図的な割り切り）
- リモート：空き（きっかけ待ち）＝リモート面越しの専用テストは無い（除外は転送経路でなく EntitySaveMetadata の側で効く）

### `IdentityColumn`

非主キーの自動採番列（[IdentityColumn] による INSERT / UPDATE 除外・EF Core の ValueGeneratedOnAddOrUpdate・SELECT では読む）

- QuickER 版 SQL Server：担当＝`SqlServerIdentityColumnRuntimeTests`
- QuickER 版 SQLite：非該当＝自動採番列を持つ図は SQL Server 方言の図で、SQLite 単独の図に IDENTITY の概念が無い（除外は列自身の事実で方言に依らないため、SQL Server の実 DB で代表させている）
- EF Core SQL Server：担当＝`SqlServerIdentityColumnRuntimeTests`
- EF Core SQLite：非該当＝自動採番列を持つ図は SQL Server 方言の図で、SQLite 単独の図に IDENTITY の概念が無い（除外は列自身の事実で方言に依らないため、SQL Server の実 DB で代表させている）
- EF Core PostgreSQL：担当＝`PostgreSqlIdentityColumnRuntimeTests`
- EF Core MySQL：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- EF Core Oracle：空き（きっかけ待ち）＝この機能を EF Core 版のこの方言の実 DB で流すテストは無い（EF Core の列を方言ごとに分けて見えた空き）
- インメモリ：非該当＝インメモリは採番を行わない＝自動採番列も普通のプロパティとして値を保つ（意図的な割り切り）
- リモート：担当＝`RemoteEfCoreGeneratedValuesRuntimeTests`（サーバー：EF Core SQL Server）

### `SyncSupport`

双方向同期支援（差分ダウンロード・ジャーナル再生・削除伝搬・競合収集・ループ防止）

- QuickER 版 SQL Server：担当＝`SyncConfigMatrixRuntimeTests`・`SyncModelSqlServerRuntimeTests`・`SyncSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`SyncConstructionExclusionTests`・`SyncKeyTextRuntimeTests`・`SyncLocalSaveHookRegistrationTests`・`SyncModelRuntimeTests`・`SyncModelRuntimeTestsPart2`・`SyncNullKeyRuntimeTests`・`SyncSqliteRuntimeTests`・`SyncVoRuntimeTests`
- EF Core SQL Server：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core SQLite：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core PostgreSQL：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core MySQL：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core Oracle：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- インメモリ：非該当＝同期はサーバー DB とローカル DB の 2 つの実 DB の間の話で、インメモリにはミラーすべきサーバー版を採番する主体が居ない
- リモート：担当＝`SyncBinaryTransferResponseRuntimeTests`（サーバー：リポジトリなし）・`SyncConfigMatrixRuntimeTests`（サーバー：QuickER 版 SQL Server）・`SyncEndpointRegistrationRuntimeTests`（サーバー：リポジトリなし）・`SyncHttpRuntimeTests`（サーバー：QuickER 版 SQLite）・`SyncModelHttpRuntimeTests`（サーバー：QuickER 版 SQLite）・`SyncModelHttpRuntimeTestsPart2`（サーバー：QuickER 版 SQLite）・`SyncModelHttpRuntimeTestsPart3`（サーバー：QuickER 版 SQLite）・`SyncModelHttpRuntimeTestsPart4`（サーバー：QuickER 版 SQLite）・`SyncSqlServerHttpRuntimeTests`（サーバー：QuickER 版 SQL Server）

### `SyncRefresh`

洗い替え（全消し＋サーバー全行の流し込み・未送信変更の拒否と force・FK 順・アンカーの継続）

- QuickER 版 SQL Server：担当＝`SyncSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`SyncRefreshBenchmarkRuntimeTests`・`SyncSqliteRuntimeTests`
- EF Core SQL Server：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core SQLite：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core PostgreSQL：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core MySQL：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- EF Core Oracle：非該当＝同期支援はマルチターゲット前提で、EF Core とは生成時に排他
- インメモリ：非該当＝洗い替えはサーバー DB の全行でローカル DB を作り直す操作で、インメモリには作り直す先の実 DB が無い
- リモート：担当＝`SyncHttpRuntimeTests`（サーバー：QuickER 版 SQLite）

### `DialectPortability`

方言可搬フィクスチャの実 DB 実行（PostgreSQL / MySQL / Oracle / SQLite）

- QuickER 版 SQL Server：非該当＝可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない
- QuickER 版 SQLite：非該当＝可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない
- EF Core SQL Server：空き（きっかけ待ち）＝可搬フィクスチャを SQL Server の実 DB で流すテストは無い（SQL Server の EF Core 版は SQL Server 方言のフィクスチャで CrudParity 行が持つ）
- EF Core SQLite：担当＝`GeneratedEfCoreSqliteRuntimeTests`
- EF Core PostgreSQL：担当＝`GeneratedEfCorePostgreSqlRuntimeTests`
- EF Core MySQL：担当＝`GeneratedEfCoreMySqlRuntimeTests`
- EF Core Oracle：担当＝`GeneratedEfCoreOracleRuntimeTests`
- インメモリ：非該当＝可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない（インメモリは方言に依らないため可搬性の検証対象にならない）
- リモート：非該当＝可搬フィクスチャは EF Core 単独出力で生成しており QuickER 版 Repository を持たない

### `EditModelValidateUnique`

EditModel.ValidateUniqueAsync（DB 照合による重複検出）

- QuickER 版 SQL Server：担当＝`UniquenessCheckSqlServerRuntimeTests`
- QuickER 版 SQLite：担当＝`EditModelValidateUniqueRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- EF Core SQLite：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- EF Core PostgreSQL：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- EF Core MySQL：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- EF Core Oracle：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- インメモリ：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している
- リモート：空き（きっかけ待ち）＝DB 照合の実 DB 検証は QuickER 版（SQLite・SQL Server）だけ。照合 SQL は UniquenessCheck 側で 5 実装を通している

### `RemoteContract`

リモート契約生成（I{Entity}RemoteRepository の面と、その裏に居る直結実装）

- QuickER 版 SQL Server：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- QuickER 版 SQLite：担当＝`RemoteContractRuntimeTests`
- EF Core SQL Server：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- EF Core SQLite：担当＝`RemoteContractRuntimeTests`
- EF Core PostgreSQL：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- EF Core MySQL：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- EF Core Oracle：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- インメモリ：割り切り＝契約の面はエンジンに依らないため SQLite / EF Core の 2 実装で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「リモート契約生成」）
- リモート：非該当＝契約そのものは HTTP を経由しない（HTTP 側は RemoteTransport 行）

### `RemoteTransport`

HTTP 転送層（health・エラー分類と詳細秘匿・応答本文・VO 本文・keyed 登録・認可の明示選択）

- QuickER 版 SQL Server：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- QuickER 版 SQLite：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- EF Core SQL Server：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- EF Core SQLite：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- EF Core PostgreSQL：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- EF Core MySQL：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- EF Core Oracle：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- インメモリ：非該当＝転送層はサーバー背後のエンジンに依らない（サーバーの背後が QuickER 版と EF Core の 2 通りでも同じことは、CrudParity 行の RemoteService 系と実装先の一致のリモート 2 つの実装先が見る）
- リモート：担当＝`RemoteAccessRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteErrorDetailRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteHealthRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteHostConfigurationRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteKeyedRegistrationRuntimeTests`（サーバー：QuickER 版 SQLite）・`RemoteResponseBodyRuntimeTests`（サーバー：リポジトリなし）・`RemoteServerEngineRuntimeTests`（サーバー：リポジトリなし）・`RemoteSqlClientCancellationRuntimeTests`（サーバー：QuickER 版 SQL Server）・`RemoteValueObjectBodyRuntimeTests`（サーバー：リポジトリなし）

### `SqlExecutorInjection`

DI 登録した ISqlExecutor が生 SQL 経路へ実際に届くこと

- QuickER 版 SQL Server：割り切り＝注入経路はテンプレート上で方言に依らないため SQLite で代表させている（根拠：src/QuickER.CodeGen.CSharp/CLAUDE.md「DB アクセスの排他と単独出力」）
- QuickER 版 SQLite：担当＝`SqlExecutorInjectionRuntimeTests`
- EF Core SQL Server：空き（きっかけ待ち）＝EF Core 版も同じ省略可能引数を持つが実行時検証は無い
- EF Core SQLite：空き（きっかけ待ち）＝EF Core 版も同じ省略可能引数を持つが実行時検証は無い
- EF Core PostgreSQL：空き（きっかけ待ち）＝EF Core 版も同じ省略可能引数を持つが実行時検証は無い
- EF Core MySQL：空き（きっかけ待ち）＝EF Core 版も同じ省略可能引数を持つが実行時検証は無い
- EF Core Oracle：空き（きっかけ待ち）＝EF Core 版も同じ省略可能引数を持つが実行時検証は無い
- インメモリ：非該当＝インメモリに生 SQL 経路が無い
- リモート：非該当＝生 SQL はリモート面に無い

### `ForeignKeyDefault`

生成 SqlConnectionFactory が FK 検査を既定で有効にすること

- QuickER 版 SQL Server：非該当＝SQL Server は FK を常に検査するため既定の補完という概念が無い
- QuickER 版 SQLite：担当＝`SqliteForeignKeyDefaultRuntimeTests`
- EF Core SQL Server：非該当＝接続文字列の組み立ては QuickER 版 Repository の責務
- EF Core SQLite：非該当＝接続文字列の組み立ては QuickER 版 Repository の責務
- EF Core PostgreSQL：非該当＝接続文字列の組み立ては QuickER 版 Repository の責務
- EF Core MySQL：非該当＝接続文字列の組み立ては QuickER 版 Repository の責務
- EF Core Oracle：非該当＝接続文字列の組み立ては QuickER 版 Repository の責務
- インメモリ：非該当＝インメモリに接続が無い
- リモート：非該当＝接続はサーバー側の構成

### `BusyTimeoutDefault`

SQLite の接続を開くたびに busy timeout を待ち時間（Default Timeout・EF Core はコマンドタイムアウト）へ合わせること

- QuickER 版 SQL Server：非該当＝SQL Server のロック待ちは CommandTimeout が決め、接続に当てる設定が無い
- QuickER 版 SQLite：担当＝`SqliteBusyTimeoutDefaultRuntimeTests`
- EF Core SQL Server：非該当＝SQL Server のロック待ちは CommandTimeout が決め、接続に当てる設定が無い
- EF Core SQLite：担当＝`EfCoreSqliteBusyTimeoutRuntimeTests`
- EF Core PostgreSQL：非該当＝busy timeout は SQLite の接続の設定で、PostgreSQL・MySQL・Oracle に当てる設定が無い
- EF Core MySQL：非該当＝busy timeout は SQLite の接続の設定で、PostgreSQL・MySQL・Oracle に当てる設定が無い
- EF Core Oracle：非該当＝busy timeout は SQLite の接続の設定で、PostgreSQL・MySQL・Oracle に当てる設定が無い
- インメモリ：非該当＝インメモリに接続が無い
- リモート：非該当＝接続はサーバー側の構成

### `ValueConversion`

SQLite の TEXT 値から IConvertible でない CLR 型（TimeSpan 等）への変換

- QuickER 版 SQL Server：非該当＝SQL Server は対応する SQL 型を持ち、この変換経路を通らない
- QuickER 版 SQLite：担当＝`SqliteValueConversionRuntimeTests`
- EF Core SQL Server：非該当＝値変換は EF Core の ValueConverter の責務
- EF Core SQLite：非該当＝値変換は EF Core の ValueConverter の責務
- EF Core PostgreSQL：非該当＝値変換は EF Core の ValueConverter の責務
- EF Core MySQL：非該当＝値変換は EF Core の ValueConverter の責務
- EF Core Oracle：非該当＝値変換は EF Core の ValueConverter の責務
- インメモリ：非該当＝インメモリは CLR 値をそのまま保持する
- リモート：非該当＝転送は JSON で、SQLite の格納表現に依らない
