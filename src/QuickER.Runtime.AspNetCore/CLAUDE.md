# QuickER.Runtime.AspNetCore

リモートサービス生成（`GenerateRemoteServices`）の固定ランタイムとテンプレートの規則を置く。
サーバー側の固定部（`RemoteServerEngine` ほか）はこのパッケージが持つ。クライアント側の固定部は Core パッケージが持つので、ここにまとめて書く。
利用者向けの意味論（直列化・例外型の復元・400 分類・liveness・楽観排他の転送・RowState 確定・500 の詳細非公開・HttpClient の所有権と寿命・更新系のリトライ禁止・keyed 登録・バイナリ転送エンドポイント）は `docs/code-generation.md`（.ja）の「リモートサービス」節が正本。
ここには実装の骨格と、壊すと静かに回帰する判断だけを書く。
Save フック・楽観排他・無制限バイナリの共通規則は `src/QuickER.Runtime/CLAUDE.md`「Save フック」「rowversion による楽観排他」「無制限バイナリ列の除外」を参照する。

## 構成と配布

- 転送方式は gRPC でなく HTTP+JSON とし、実証済みの JSON 資産を転用する（`EntityBaseCore.Clone` と同じ意味論の `RemoteJson.Options`＝VO コンバータ・RowState 込み・IgnoreCycles）。
  - `IgnoreReadOnlyProperties` は指定しない。クエリ転送エンベロープの匿名型が get-only で空 JSON になるため。
- CLI は `--generate-remote-services`。クライアント：`Http{Entity}RemoteRepository`（BCL の HttpClient のみ）を本体生成物へ同梱し、登録は `AddGeneratedHttpRemoteRepositories(baseAddress | HttpClient ファクトリ)`。名前付きクエリは実装方式に依らず全部転送メソッドにする。
- サーバー：`MapGeneratedRemoteEndpoints(access, prefix="/quicker")`（Minimal API・`POST {prefix}/{エンティティ}/{操作}`・`RouteGroupBuilder` を返し追加ポリシーを付与できる）を別ファイル `{ベース名}.RemoteServer.g.cs`（バケット RemoteServer）へ出す。ASP.NET Core の FrameworkReference が必須のため、本体へ連結しない。
- 固定部の置き場は 2 つに分かれる。
  - クライアント側の固定部（`RemoteJson` / `RemoteRepositoryException` / エンベロープ / `HttpRemoteRepositoryCore` 基底）は共有 infra で、Core パッケージへも収載する（BCL のみ＝依存ゼロ維持）。per-entity クライアントと DI 登録はスキーマ依存物として常に生成側。
  - サーバー側の固定部は、リクエスト読み取り・例外分類・応答書き込み・汎用 CRUD マッピング・バイナリ転送の補助を `RemoteServerEngine`（とトップレベル型 `RemoteBadRequestException` / `RemoteErrorDetailPolicy` / `DisableRequestBodySizeLimit` / `DeferredOctetStreamBody`）に切り出す。分割生成では `Runtime.AspNetCore.g.cs`、パッケージ参照モードでは `QuickER.Runtime.AspNetCore` が持つ（非分割インラインでは同じサーバーファイルへ同居）。
- per-entity 側は `RemoteServerEngine.` 修飾で呼ぶ。方言エンジン参照と同型で、モード間で per-entity 出力はテキスト不変。
- 共有ヘルパー `RemoteEntityGraph`（応答の収集・キー直列化・版プロパティ解決）は Core パッケージ収載の `public` 静的クラス。サーバーファイルは別プロジェクトへ置ける前提のため `internal` にできない。カスケードの走査はクライアント・サーバーとも `EntityBaseCore.EnumerateCascadeChildren` を使い、属性の走査を写さない。
- `PingAsync` は `HttpRemoteRepositoryCore` 基底に置く。直結実装と共有する契約に liveness の意味がないため、リモート面インターフェイスには出さない（呼ぶには具象 `Http{Entity}RemoteRepository` へキャストする）。
- ルートは定数 `RemotePaths`（`DefaultPrefix` = `"/quicker"`・`HealthRoute`）へ一本化し、サーバー既定値・クライアント・サンプルが同じ値を読む。
- BulkInsert・`Query()`・生 SQL はリモート面に無いため転送対象外（設計どおり）。

## 認可

- `MapGeneratedRemoteEndpoints` の第 1 引数 `RemoteAccess` は既定値なしの必須とする。全行の読み書き削除面の開放/保護を、誰も書いていない既定値に決めさせない（`exposeErrorDetails` / `allowUnboundedUploads` と同じ「危険は明示させる」文法）。
  - `RequireAuthorization` はグループ全体（health を含む）へ既定認可ポリシーを適用する。
  - `AllowAnonymous` はメタデータを付けない。`[AllowAnonymous]` を付けると、ホストの FallbackPolicy を弱体化させるため。
  - 未定義値はマップ時に `ArgumentOutOfRangeException`。
- ガード：`RemoteAccessRuntimeTests`（実 HTTP）。

## エンドポイントのマップ

- CRUD・保存・重複事前チェック・バイナリ転送・同期エンドポイントのマップは、エンジンのジェネリックメソッドへ集約している。
  - per-entity の `Map{E}Endpoints` は `MapCrud<TEntity,TKey,TRepository>(group, ルート名)` を 1 回呼び、バイナリ列ごとに `MapBinaryColumn<TKey>(group, ルート, allowUnbounded, read/write デリゲート)` を呼ぶ。
  - キー復元はハンドラのラムダ内に置く。不正な `?id=` の 400 分類・411 の判定位置・DELETE=`write(null)` の構造を変えないため。
  - 同期は `MapSyncEndpoints` 系で、エンジンでなく生成側 `GeneratedRemoteEndpoints` の private ジェネリックメソッド（スキーマ依存ファイル在住）。`SyncPage` は版なしテーブルへの張り分けを per-entity 側の呼び分けで維持する。
  - 名前付きクエリのエンドポイントだけが per-entity のまま。
  - `{E}CheckUniquenessRequest` record は、既存 `RemoteEntityRequest<TEntity>` と完全同型のため持たない（private record なので公開 API 非破壊・ワイヤ互換）。
  - `RequireSyncSources` は `(Type, string)[]` の表と固定 foreach で書く。素の `Type[]` にしないのは、例外メッセージの型名表記（`int` 等の C# キーワード形）をテストが固定しており、実行時導出だと `Int32` に化けるため。報告名は生成時に書き下ろす。
- DI 解決も per-entity 側は `RemoteServerEngine.Repository<T>(context)` 経由に統一する。
- mutation 実証済み。

## 500 の詳細非公開とサーバーエラーのフック

- `OnServerError` の partial 契約はアセンブリを跨げないため、メタデータ経由で繋ぐ。
  - 生成側 `GeneratedRemoteEndpoints` が `RaiseServerError`（メソッドグループ。partial 未実装でも実在する）を `RemoteErrorDetailPolicy(exposeErrorDetails, RaiseServerError)` としてグループのメタデータに載せ、エンジンの `WriteServerErrorAsync` がそこから呼び出す。
  - エンドポイント不一致時は「隠す＋フックなし」の安全側既定にする。
- 500 の詳細非公開スイッチも同じメタデータに乗る。ハンドラの署名は変えず、同一プロセスの複数グループが独立する。
- 500 を書く分類の catch は共通ヘルパー `WriteServerErrorAsync(context, ex)`（ログ→フック→本文の 3 手順を 1 箇所へ）を呼ぶだけにする。500 のフックの例外は隔離して別の 1 件として記録し、本文は必ず書く。
- 400/409 はスイッチの影響を受けない。400/409 ではログも `OnServerError` も呼ばない（両者は 500 専用）。
- 応答書き込みは共通ヘルパー `WriteErrorAsync(context, statusCode, type, message, conflict?, correlationId?)` へ集約する。
- ガード：`RemoteErrorDetailRuntimeTests`（500 の非公開/透過・相関 ID のログ突き合わせ・400/409 の対照・旧ボディ退化・バイナリ経路）。

## クライアントの分類

- `SaveConflictException` はリモートでも復元する。409 の `RemoteError` へ `Reason` / `EntityType` / `Key` を載せ（この 3 つは SaveConflict 以外では null。欠落した旧ボディは `Unknown` へ退化）、クライアントが同じプロパティを持つ例外を組み直す。楽観排他の共通の骨格は `src/QuickER.Runtime/CLAUDE.md`「rowversion による楽観排他」
- リクエストは `ConcurrencyMode` を運ぶ（Mode の欠けた旧エンベロープは既定の Optimistic へ落ちる）。Insert / Update の応答は書き込んだ行の版を、Save の応答は版対応表（`RemoteRowVersionEntry`）を運び、クライアントが共有ヘルパー `RemoteEntityGraph` で書き戻す
- フックが飛ばした行は保存の応答 `RemoteSaveResult.Skipped`（`RemoteEntityRef`＝CLR 型名×`RemoteJson` のキー・既定 null で旧ボディ互換）で伝える。サーバーは保存成功後のグラフから「HasChanges かつ非 Removed」を版対応表と同じ走査・同じ照合で集め、クライアントの `AcceptChanges` は一致する行を `MarkUnchanged` しない（直結と同じく未保存のまま残る）

クライアントの失敗分類で落としやすい 4 点。

- 成功ステータスなのに本文が期待した JSON でない応答も `RemoteRepositoryException` にする。`InvokeAsync` の成功パスが `JsonException` / `NotSupportedException` を包み直す。
  - 応答しているのが生成エンドポイントではない別物（プロキシやポータルの 200）で、実体は転送の失敗のため。素の `JsonException` では他のリモート失敗と同じ catch で拾えない。
  - JSON リテラル `null` の本文は正当な JSON で例外にならないため、明示チェックで同じ分類にする。クライアントの実体は private `InvokeCoreAsync(…, allowNullResult)` 1 本で、null が正当な結果の操作（GetById・単一戻り形・null 許容スカラー。内側の型の `?` サフィックスで振り分ける）だけが `InvokeNullableAsync` 経由になる。200＋`null` は従来どおり「該当行なし」で返す。
  - ガード：`RemoteResponseBodyRuntimeTests`（両アーム）。
- VO 併用時は `ValueObjectValidationException` も同じ 2 フィルタ（成功パス／`ReadErrorAsync`）へ含める。`ValueObjectJsonConverter.Read` の `TVo.Create` 検証違反は JsonException で出ないため、含めないと素通りする。
- 失敗応答の本文を読めなかった理由も保全する。`ReadErrorAsync` が解析失敗の例外を `ToException` まで運び innerException へ載せる。「本文が RemoteError でなかった」と「message が空の RemoteError だった」を呼び出し側が区別できる。
- バイナリ転送は、エンドポイントが返す形の成功応答だけを成功にする（`HttpRemoteRepositoryCore.EnsureBinaryEndpointAnswered`）。
  - GET は 200 かつ Content-Type のメディア型が `application/octet-stream`（パラメータを除き大文字小文字を無視）。PUT / DELETE は 204。それ以外の 2xx は `RemoteRepositoryException`（ステータスと Content-Type を名指し）で、GET は宛先へ何も書かない。
  - エンドポイントの手前の別物（セッション切れをサインインページへ転送する認証プロキシ・何にでも 200 を返す中継）の 2xx を成功と読むと、GET はその HTML を blob として書いて true、PUT / DELETE はサーバーに届いていない書き込みで true になる。JSON と同じ理由。
  - HTTP の同期の blob のコピー（`HttpSyncServerSource` の `ISyncBinaryColumns`）も同じヘルパーを通る。
  - ガード：`BinaryTransferResponseRuntimeTests`（転送するプロキシ・200 で答える中継 × GET / PUT / DELETE と JSON の対照）、`SyncBinaryTransferResponseRuntimeTests`。mutation 実証済み。

## 400 の分類

- 境界は「`ReadRequestAsync` と `ParseKeyFromQuery` の 2 関数内の失敗」で、内部マーカー `RemoteBadRequestException` に包み直して `ClassifyFailuresAsync` が catch する（例外型の列挙をしない）。
- 分類の catch の並びは `ClassifyFailuresAsync` の 1 か所だけにする。`ExecuteAsync` と blob の 3 executor（Download / Upload / Delete）は、本体をこれへ渡すだけ。
- 必須フィールド欠落も 400 にする。
  - エンベロープは positional record のため `{}` は Entity / Entities / 参照型キーが既定の null のまま通り、リポジトリ奥の null 引数例外＝500＋ログ＋フックに化けるため。
  - 共有ヘルパー `Required<T>(T? value, string name)`（無制約ジェネリック＝値型キーは素通し）を、Insert / Update / Save / SaveMany の Entity・SaveMany の Entities・GetById / Delete の Id へ適用する。
  - per-entity のマップを組み立てるモデルビルダー側（`CSharpGenerationModelBuilder.Remote.cs`）も同じ規則で、重複事前チェック（CheckUniqueness）の entity と名前付きクエリの参照型パラメータ（VO・string・`IReadOnlyList<T>`）を包む。判定は `QueryPayloadParameter.IsReferenceType`（列由来は `IsUnderlyingReferenceType`、VO・トークン由来は `CSharpTypeInfo.IsReferenceType`）。
- リスト内 null 要素は 400 にしない。3 実装先の SaveAsync がいずれも `entity is not null` で除外しており、直結の契約どおり黙って無視するのが正。応答収集側（`CollectRowVersions` / `CollectSkipped`）の null ガードで NRE→500 を塞ぐ。
- 未定義の `ConcurrencyMode` 値も 400 にする。enum は JSON で任意の数値を受けるため、Update / Save / SaveMany の 3 ハンドラが `ValidatedMode` で `Enum.IsDefined` 検証する（素通しすると版チェックが黙って無効化される）。
- 名前付きクエリのページング引数も 400 にする（`ValidatedPaging(take, skip)`）。`take<=0` / `skip<0` はクエリパイプラインが `ArgumentOutOfRangeException` で拒否する値で、素通しすると呼び出し側の不備が 500＋ログ＋フックに化ける。2 つは常に対で出るので、まとめて 1 回検証する。
  - 生成クライアントはこの 400 を出さない。ページング付きの転送メソッドは先頭で直結と同じ例外（`ArgumentOutOfRangeException`・順序は Skip→Take・ParamName は `SqlQuery.Skip/Take` の引数名 `count`・文言も同一）で弾き、HTTP を投げない。
  - クライアントの Insert / Update / Save / SaveMany / CheckUniqueness も入口で `ArgumentNullException.ThrowIfNull` を通し、SaveMany のリスト内 null 要素は送信前のガードと保存後の RowState 確定で飛ばす。例外型・パラメータ名まで直結（ADO / EF Core / InMemory）と一致させるのが契約の一部で、実装を差し替えても catch が変わらない。
  - 文言の複製は `_05_QueryPipeline` とモデルビルダーの 2 箇所になるため、一致はパリティテスト（RemoteService 20・22・23）が固定する。サーバー側 400 は手書きクライアント向けの防御として残す。
- Kestrel の `BadHttpRequestException` はステータスを素通しする（ボディサイズ超過の 413 を 500 に化けさせないため）。

## 切断とタイムアウト

### 開始済みの応答

- 分類 catch は `when (!Response.HasStarted)` で守る。応答が書き出し始めたあとの失敗ではステータスを変えられず、エラー本文を書くと部分応答へ継ぎ足しになるため。
- 開始済み用の `catch (Exception)` を 1 本持ち、`LogServerError` だけ通して rethrow する。ホストが接続を中断するので、クライアントには破損応答でなく切断が見える。
  - `OnServerError` は 500 応答の拡張点なので、500 を出さないこの経路では呼ばない。
  - blob 側は本文をストリーミングする分、開始済みで落ちやすい。この 1 本が無いと失敗が痕跡を残さず消える。

### 本文の途中の切断

Kestrel は本文の読み取りの例外を先に投げ、`RequestAborted` は後からスレッドプールで立てる（`HttpProtocol.CancelRequestAbortedToken` が `ServiceContext.Scheduler.Schedule` へ予約する。RST はさらに `SocketConnection.FireConnectionClosed` の予約を挟む）。
「トークンが立っているか」だけで判定すると、混んだ機械（CI）では切断が 400 / 500 に化ける（アップロードの RST は 500＋ログ＋フック）。
そのため `ClassifyFailuresAsync` は、タイミングに依存しないよう次の順で判定する。

1. `BadHttpRequestException` を最初に、運んできたステータスのまま返す。
   - FIN（本文が宣言長より前に終わった）は、本文の読み取りが本文の終わりを先に見れば Kestrel 自身の分類どおり 400。Kestrel は例外を投げる前に応答の側を自分で閉じる（`Http1MessageBody.ThrowUnexpectedEndOfRequestContent`）ので、400 は記録に残るだけで回線には出ない（半閉じのクライアントも 0 バイト。実測）。
   - FIN でも Kestrel は `RequestAborted` を立てる（実 Kestrel で確認）。読み取りがトークンを先に見ると（トークンが立った後に読み取りを始めた・待機中の読み取りで取り消しが勝った）`OperationCanceledException` で終わり、手順 2 の切断として扱われる（無応答・StatusCode は 200 のまま）。
   - どちらでも回線は 0 バイトで、ログ・フック・ホストへの中断依頼は出ず、ホストの記録（Hosting の Request finished）は 499。差が見えるのはエンドポイントの後で `Response.StatusCode` を読むミドルウェアだけで、例外から FIN 由来の取り消しを見分ける手段が無いので製品では区別しない。
2. 切断（`IsClientDisconnect`＝例外の連鎖に `ConnectionResetException` か `ConnectionAbortedException` がある、または `RequestAborted` が立っている間の `OperationCanceledException` / `IOException`）は無応答にする。
   - ログもフックも出さず、ホストがまだ知らなければ `context.Abort()` で知らせる（`AbandonConnection`）。知らせないと、エンドポイントが先に返ったとき Kestrel が本文の自動読み捨てを試み、RST が残した読み手で失敗して `RequestBodyDrainBodyReaderInvalidState` を Error で出す（`Http1MessageBody.OnConsumeAsync`）。
   - トークンが立っていれば知らせない（`ApplicationAbortedConnection` の Information ログを毎回出さない）。
   - `ConnectionAbortedException` を含めるのは、MinRequestBodyDataRate 違反などで Kestrel 自身が接続を中断した要求も応答を届けられない点は同じだから。トークンが立つ前に届いても 500＋ログ＋フックにしない（誤った警報を出さない）。
   - 切断として扱った要求は Debug で 1 行だけ記録する（`LogClientDisconnect`）。
3. 分類した応答の書き込みは catch の外で 1 回だけ行い、切断で失敗したら黙って終える（書いている最中にトークンが立つと `WriteAsJsonAsync` が `OperationCanceledException` を投げる）。`ReadRequestAsync` も切断（同じ判定）なら `RemoteBadRequestException` に包まない。

- 既知の残余：`ConnectionResetException` を使わないホスト（HTTP.sys 等）では、トークンより先に別の `IOException` が届くと 400 / 500 になり得る（Kestrel と IIS は `ConnectionResetException`）。HTTP/2 のストリームのリセットは型を持たない `IOException`（InnerException なし）で来るので、HTTP/2 では時期への依存が残る。見送りで、docs の既知の限界に書く。
- ガード：`RemoteServerEngineRuntimeTests` の `Json_` / `Upload_ClientDisconnectsMidBody_IsNotAnsweredAsFault`（生ソケットで FIN / RST × JSON / アップロード × トークンの時期。要求の寿命の機能を差し替えて「分類まで立たない」〔Late〕「分類の前に立ちホストも知っている」〔Early〕を決め、実 Kestrel のままの 2 通りを持つ。全ケースで回線 0 バイト・Error のログ 0・OnServerError のフック 0 を厳密に表明する）。`RemoteHostConfigurationRuntimeTests` の HTTP/2 のケースは、トークンが立っている間の緑を固定するだけで、決定的な赤は作らない。mutation 実証済み。

### 要求のタイムアウト

- ホストの要求のタイムアウトは切断と取り違えない。`ClassifyFailuresAsync` は、切断の catch より前で `IHttpRequestTimeoutFeature.RequestTimeoutToken`（`RequestTimeouts` が置く。要求のトークンより先に立つ）を見て、時間切れなら失敗を `OperationCanceledException` としてミドルウェアへ返す（取り消し以外の型は内側に包む）。ミドルウェアが 504 で答える。
  - 切断として飲み込むと、JSON は空の 200、アップロードは応答なしで終わる。判定を要求のトークンで行うと、本文の読み取りの例外が先に来る形で時期に依存する。
  - ほかのミドルウェアの連結トークンは見分けられない（docs の既知の限界）。
  - アップロードの時間切れで Kestrel が本文の読み捨ての失敗を Error で 1 件記録するのはエンジンの外の後始末で、504 は届く。`context.Abort()` で消すと 504 が届かなくなるので採らない。
- 要求のトークンが立った後の DB の例外（`System.Data.Common.DbException`）も切断に数える。SqlClient は取り消しを `SqlException` で報告するため。それ以外の型は、クライアントがいなくなっていても 500＋ログ＋フックのままにする（遅い経路の本物の不具合を隠さない）。
- アップロードの本文は前進専用の包み（`ForwardOnlyRequestBody`）で渡す。`EnableBuffering` のホストでは本文がシーク可能になり、バッファした分だけの長さが宣言長と食い違ってすべて 500 になるため。
- バイナリの PUT は `?id=` のキーを 411 / 413 の検査より先に読む。
- ガード：`RemoteHostConfigurationRuntimeTests`（RequestTimeouts の止まった PUT・遅い Save の 504・EnableBuffering 3 通り・投げるフック・キーの 2 件・HTTP/2）、`RemoteSqlClientCancellationRuntimeTests`（実 SQL Server でクエリ中の取り消し＝Debug 1 件だけ。対照＝取り消しと重なった DB でない失敗、取り消していない DB のエラーは Error＋フック）、`RemoteServerEngineRuntimeTests` の「サーバーのタイムアウトは、要求のトークンが立っていなくても…」、`BinaryColumnRemoteRuntimeTests` の 9c（上限超え＋読めないキーは 400）。mutation 実証済み。

## 保存の応答は列の値を運び位置で突き合わせる

- Insert / Update の応答（`RemoteInsertResult` / `RemoteUpdateResult` の `Values`）と Save / SaveMany の応答（`RemoteSaveResult.Saved`）が、サーバーのエンティティが保存の後に持つ `[Column]` の値を運ぶ（`RemoteEntityGraph.ReadColumnValues` / `WriteColumnValues`、`RemoteJson.Options`）。
  - 主キー・版を含み、`[UnboundedBinaryColumn]` は除く（クライアントの更新のガードと整合する）。
  - これで、サーバー側の Save フックが書いた値と、EF Core のサーバーが読み戻した値（採番列・計算列）がクライアントへ届く。
- Save の値はキーでなく位置で照合する。
  - 並びは `RemoteEntityGraph.CollectSaveRows`（削除されていない行と null の要素を飛ばしてカスケードの順にたどる）1 つが決める。
  - サーバーは、リポジトリを呼ぶ前に受け取ったグラフで一覧を作り、保存の後に `ReadSavedValues` で読む。クライアントの `MatchSavedValues` は送ったグラフで同じ一覧を作る。
  - 保存の後にたどると、保存がコレクションを変える分（削除した子を外す・EF Core の fix-up が追跡中の子を親のコレクションへ入れる）だけ数がずれる。自己参照の親と子を別々の根で保存すると、EF Core のサーバーで値が 1 行も戻らなかった。
  - フックが飛ばした行は null で、クライアントはその行を飛ばした行として扱う（値の無い応答では従来どおりキーの一覧）。
- 飛ばした行と削除した行の値は `RemoteSaveResult.Unsaved`（位置でない init プロパティ＝コンストラクタの署名を変えない）が運ぶ。
  - 並びは `RemoteEntityGraph.CollectAllRows`（行の状態と保存のカスケードの指定に依らず、カスケードの子をすべてたどる）で、保存の後も変更を持つ行（削除した行・フックが飛ばした行）だけ値、ほかは null。
  - 削除した子を残った親のコレクションから外す共通規則と、サーバーが残った Removed の子を「飛ばした行」として応答に載せる処理（`RemoteEntityGraph.CollectSkipped`）は `src/QuickER.Runtime/CLAUDE.md`「削除した子の外れ方」を参照する。
- クライアントは `Saved` の値の後に、`RowState` を変えずに書き戻す。数が合わなければ値を使わず、従来どおり確定する。以前のサーバーの応答は欄を持たず書き戻さない。以前のクライアントは知らない欄を読み捨てる。
- 値は版の表（キーで照合）の後に書き戻す。食い違えば値が勝つ（フックが主キーを変えると版の表は外れる）。
- ガード：実装先の一致のシナリオ `remote-save-writes-back-hook-values`・`remote-write-back-by-position`（Remote・RemoteEfCoreOnly）、`RemoteEfCoreGeneratedValuesRuntimeTests`（EF Core のサーバー・実 SQL Server の採番列が Insert / Save / Update で戻る・Docker）、`RemoteSaveResponseClientTests`（応答を差し替えた HTTP）、`EditModelSaveRuntimeTestsBase` の Before フックのシナリオ（リモートの派生でも流す）、`h3-edit-then-skip-*`・`hook-edit-then-continue-delete`（Remote・RemoteEfCoreOnly の slots）。mutation 実証済み。
- `CollectSaveRows` が削除行を飛ばさないようにしても緑のまま（両側が同じ関数で並びを作るので食い違わない）。サーバーの一覧を保存の後に作っても緑のまま（EF Core の保存が渡したエンティティのナビゲーションを元へ戻すため）。保存の前に作る形は二重の守りとして残す。

## 除外した無制限バイナリ列を値として運ばない

- blob はバイナリのエンドポイントだけで運ぶ。除外した無制限バイナリ列を、JSON の通信では値として運ばない。
- サーバーのエンジン（`RemoteServerEngine.ExecuteAsync`＝JSON のエンドポイントがすべて通る 1 か所）は次の 2 つを行う。
  - 処理の間、`UnboundedBinaryColumns.EnterRemoteRequest()` の印（Core・AsyncLocal）を立てる。同時に走る別の要求と呼び出し元には見えず、入れ子は破棄で外の状態へ戻る。
  - 処理が返したエンティティ（単体か列・カスケードの子も）の除外列を `UnboundedBinaryColumns.StripExcludedFromResult` で未取得の値へ戻してから書く。射影の DTO・件数・応答の封筒はエンティティでないので触らない（除外列を参照する射影は値を運ぶ）。戻すのは返すエンティティそのもので、要求の中で後に保存が無く、リポジトリは変更の追跡が持たないエンティティを返すため（EF Core は読み取りの前に作り読み取りの後に破棄する DbContext、インメモリは複製）。
- EF Core のリポジトリは、印が立っている間だけ、SaveChanges の前に毎回 `KeepStoredUnboundedBinaries`（更新する行の除外列の元の値を今の値にして未変更にする）を通す。送られてくる除外列は未取得なので、全列を書くと保存済みの blob が NULL や空になるため。
  - 毎回通すのは、後勝ちのやり直しで保存先の値を元の値として取り込む（`OriginalValues.SetValues`）と元の値が blob に戻り、次の変更の検出が除外列を書き換えた列として扱うため。
  - 挿入は送られた値を書く。直結の EF Core は全列を読み書きする（印が無い）。
- ガード：実装先の一致のシナリオ `blob-excluded-columns-read-and-write-guard`・`blob-unset-update-from-new-entity-keeps-blobs`・`remote-crud-round-trip`（EF Core のサーバー〔RemoteEfCoreOnly〕）、`RemoteEfCoreUnboundedBinaryRuntimeTests`（BinaryFixture・SQLite の EF Core のサーバー）、`RemoteEfCoreUnboundedBinarySqlServerRuntimeTests`（ConcurrencyFixture の gadgets.attachment・実 SQL Server で版の古い ForceOverwrite の単一の更新とグラフ保存）、`RemoteRequestMarkRuntimeTests`（破棄・入れ子・例外・非同期のメソッドから呼び出し元へ戻らない・並行の流れに見えない）。mutation 実証済み。

## バイナリ転送エンドポイント

JSON エンベロープ（POST+Base64）とは意図的に別形式の、第 2 形式の専用エンドポイント。`GenerateRemoteServices` かつ無制限バイナリ列の除外が効くときに追加する。
形は `GET/PUT/DELETE {prefix}/{エンティティ}/{列名}?id=`（キーは URL クエリ・本文は生 blob・`application/octet-stream`）。
無制限バイナリ列の除外の共通規則は `src/QuickER.Runtime/CLAUDE.md`「Stream アクセサ」を参照する。

### 応答の形

- GET は応答開始を最初の書き込みまで遅延するラッパーストリーム（`DeferredOctetStreamBody`）で、「false→404・true（空を含む）→200」を本文ストリーミングと両立させる。先に本文を書くと 404 に切り替えられないため。
  - 書き込み前の `FlushAsync` で応答を始めない。始めると行なしの 404 へ切り替えられない。
  - 同期の `Write` は応答の本文へ同期で書く。非同期を待って塞ぐと `AllowSynchronousIO` の禁止をすり抜ける（既定ではホストの規則どおり例外＝500）。
- PUT は成功 204・行なし 404・`Content-Length` 欠落（chunked）は 411・id 欠落/不正は 400（GET / DELETE も同じ）。DELETE は `Write(id,null)`＝NULL 化（0 バイト PUT＝空ボディと構造的に区別する）。
- 「行なし/NULL」の 404 には marker `RemoteError{Type:"NotFound"}` を載せ、クライアントで `false` へ畳むのはこの marker 付きだけにする。
  - 本文なしの素の 404（ベースアドレス/プレフィックス誤り・消えたルート・プロキシ自身の応答）は `RemoteRepositoryException(404)` として送出する。設定ミスが「データなし」に化けないため。
  - 判定は「本文を 1 度だけ読んで `Type` を見る」段階形で、解析失敗は例外側へ倒す。
- 411 にも、他の分類済み拒否と同じ `RemoteError{Type:"BadRequest"}` 本文を付ける。ボディのドレインは不要（早期 return のまま、3KB〜50MB の chunked すべてで 411 が正常に受信されることを実測で確認済み）。

### サイズ制限

- リクエストサイズ制限の解除はオプトインとする。`MapGeneratedRemoteEndpoints(access, prefix, exposeErrorDetails, bool allowUnboundedUploads = false)` の第 4 引数が true のときだけ、このエンドポイントへ `IRequestSizeLimitMetadata` メタデータ（`DisableRequestBodySizeLimit`）を付与する。
  - 既定 false ではホストの上限（Kestrel 既定 30MB）が効き、超過は 413。任意サイズの本文を受けるのは DoS 面のため認可併用を推奨する。`RouteGroupBuilder` で別値へ上書きできる。
  - フラグはマップ時に評価し、ハンドラ署名は変えない。バイナリブロックを持つ per-entity の `Map{X}Endpoints` だけが引数を受ける。
- 413 は宣言長の事前チェックで出す（`EffectiveRequestBodySizeLimit`）。書き込みは本文を読む前に宣言長ぶんの領域を確保する（SQLite の `zeroblob(宣言長)`）のに対し、ホストの上限は本文を読んだときにしか発火しないため、`Content-Length` を書き込み前に上限と突き合わせる。
- 上限の解除は feature でなくエンドポイントのメタデータで判別する。feature の `null` は「意図的な解除」と「feature を公開しないホスト（IIS アウトオブプロセス等）」の両方で起き、後者を「上限なし」と読むと、オプトインしていないエンドポイントで事前チェックが無言で消えるため。
  - 判定順：`IRequestSizeLimitMetadata` があればその値（`null`＝解除）→ feature の上限 → Kestrel 既定 30,000,000 のフォールバック。ホストが上限を全体解除していても、オプトインなしならこの既定が効く（安全側。docs 明記）。
- 解除側の取引：宣言長の検査も外れるため、本文を 1 バイトも送らずに宣言長ぶんの blob 先行確保を毎回させられる（確保はロールバックされるが I/O は消費する）。認可併用を推奨する理由。

### クライアントのアップロード

- 生成クライアントのアップロード（`UploadUnboundedBinaryColumnAsync`＝`Write{列}Async` と同期の blob のアップロード）は `Expect: 100-continue` を付ける。
  - 上限を超える宣言長では Kestrel が本文を読み捨てない（後始末の `ConsumeAsync` が `Http1ContentLengthMessageBody.OnReadStarting` で keep-alive を切って打ち切る）。本文の送信中に接続が閉じられ、遅れて届いた本文へのリセットで受信済みの 413 が捨てられる（Windows）。利用者には 413 の `RemoteRepositoryException` でなく `HttpRequestException` が届いていた（31MB では大半、4KB でも負荷時に起きる。実測）。
  - Expect があれば Kestrel は本文を読み始めるまで 100 Continue を送らず、HttpClient は 1024 バイトを超える本文を最終応答の前に送らずに接続を閉じる。
  - 待ち時間はハンドラーの `Expect100ContinueTimeout`（既定 1 秒・生成側では変えない。利用者は HttpClient ファクトリ版の登録で変えられる）で、過ぎると本文が送られて同じ競合が戻り得る。
  - JSON エンベロープの要求には付けない（毎回 1 往復増えるのに対し、30MB を超えるエンベロープは想定外）。
- クライアント（`Http{Entity}RemoteRepository`）は `ResponseHeadersRead`＋`CopyToAsync` で読み、Content-Length 付きの専用の本文 `ExactLengthContent`（`HttpRemoteRepositoryCore` の private な入れ子）で書く。非シークの Stream は送信前に `ArgumentException` で検証する。
- アップロードも Stream を `WithExactLength` 越しに読む。直結と同じ長さの契約で、違えば `ArgumentException`（ParamName=length）。HttpClient は送信中の `ArgumentException` を包まずにそのまま出す（包むのは `InvalidOperationException` と `IOException`）ので、取り出す処理は持たない。
- 長すぎる Stream は、宣言長の本文を送り終える前に断る。`ExactLengthStream` は宣言長に届く読み取りを返す前に 1 バイト先を読む（サーバーは宣言長の本文を受け取れば書き込みを確定するので、送り終えてからでは遅い）。長さちょうどの Stream ではその先読みが終端の 0 を見るだけで、呼び出し側の Stream から余分なバイトを読まない。直結のエンジンも同じ包みを使う。
- 本文は送るたびに新しく包む。HttpClient が転送に従って送り直すときは、シーク可能な Stream を最初の送信の開始位置へ戻す（`StreamContent` と同じ。戻さないと転送の先へ 0 バイトの本文を送って長さ違反になる）。シークできない Stream は 2 回送れず `HttpRequestException`。
- 渡された Stream の所有権は呼び出し側にあり、閉じも破棄もしない（直結エンジンと同じ）。本文 `ExactLengthContent` は Stream を破棄しない。`StreamContent` へ呼び出し側の Stream を渡すための非クローズのラッパー `NonClosingStream`（Core パッケージ収載・Dispose/DisposeAsync だけ無視して他は委譲）は、公開の型として残る。
- キー直列化は `RemoteJson.Options`（VO 内包値）を URL エンコードし、サーバーが同設定で復元する。クライアント固定部（`FormatKeyForQuery`・Download/Upload ヘルパー）は `HttpRemoteRepositoryCore` 基底（Core パッケージ収載）。
- `WithUnboundedBinary()`・`Query()`・生 SQL のリモート化はスコープ外。
- ガード：`BinaryColumnRemoteRuntimeTests` の 9b（`Stream_Put_WithoutOptIn_IsRejectedByDefaultRequestSizeLimit`＝生成クライアントで 413 の `RemoteRepositoryException` だけを合格にする。テストのクライアントだけ待ち時間を 30 秒にして負荷に依らず決定的にし、プロセスで最初の 413 はサーバー側の JIT で閉じるのが遅れて競合が出にくいので、同じ 413 の経路を `Expect` 付きの素の PUT で先に 1 回通してから確かめる）、`BinaryRemoteLengthRuntimeTests`（`BinaryLengthMismatchCases` を実サーバーへ流して blob が変わらない・長さちょうどのシークできない Stream は読んだバイト数がちょうど・`WithExactLength` 単体の同期と非同期の両方・307 で送り直す本文が開始位置から読まれる）、`BinaryColumn{Ado,EfCore,Remote}RuntimeTests`・`BinaryInMemoryFixtureRuntimeTests`（リモートは HTTP 越しの往復/404 false/DELETE→NULL/空ボディ 0 バイト/送信前 length 例外/`allowUnboundedUploads: true` での 31MB 転送成功と既定での 413 拒否の対照。CI 常時実行）。mutation 実証済み。

## DI 登録

- 非 keyed・keyed の 2 系統 ×（ベースアドレス版／HttpClient ファクトリ版）の 4 本を用意する。keyed 版は方言別拡張の keyed 版と対で、「サーバー＝HTTP・ローカル＝SQLite」のハイブリッド構成を組むための部品。
- Save の応答の版対応表は `RemoteRowVersionEntry`（CLR 型名＋`RemoteJson` でシリアライズしたキー）で運ぶ。クライアントが書き込んだ行の版として書き戻す。
- 共有 HttpClient（`OwnedHttpClient`）も同じキーで `AddKeyedSingleton` する。非 keyed 登録とも別キーとも衝突せず、コンテナ所有（破棄も従来どおり）を保つ。
- ガード：`RemoteService{Ado,EfCore}RuntimeTests`（Kestrel を 127.0.0.1 の空きポートで in-process 起動・CI 常時実行）、`RemoteKeyedRegistrationRuntimeTests`（Kestrel 2 台をキー別に登録して往復・非 keyed 解決との分離・同一キー後勝ち・コンテナ所有の破棄）。
