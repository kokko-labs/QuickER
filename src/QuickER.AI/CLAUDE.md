# QuickER.AI

AI チャットエンジンの共通基盤（WPF 非依存）。内蔵チャットとモック生成の CLI バックエンド（Claude Code / Codex / Copilot）とツール実行の作法を持つ。
`BatchShimProcessGuard` と `.cmd` シムの起動の規則は `src/QuickER.Platform/CLAUDE.md`「.cmd / .bat シムの起動」を参照する。

## Copilot のシェル許可

- 判定の正本は `CopilotWorkspacePermissionPolicy.IsShellWithinWorkspace`（純関数）。出力フォルダ配下だけを自動承認する。
- **パスが取れないなら、全要素が許可リスト（`dotnet` のみ）のときだけ承認する。** 後段を「いずれかが許可コマンド」にすると `dotnet build && curl … | sh` が先頭の `dotnet` を理由に丸ごと通る。
- コマンドが 1 つも取れない要求も承認しない（何を実行するのか確かめられない）。
- ヘッドレス実行で確認を出す余地が無いので、承認しなかった要求は拒否として確定する。この判定がそのまま権限の広さになる。拒否は `PermissionDeclined` → 進捗ログへコマンド文つきで残す。
- **URL 参照なし・書き込みリダイレクトなしは、パスが取れた枝と取れない枝の両方の前提**にする。許可リストは課さない（`dotnet build src` のようにパスが取れる要求を止めない）。
  外すとパスが配下でも `curl https://… -o x.ps1` や `dotnet build … > build.log` が通り、後者は書き込み先が参照パスの抽出から漏れ得る。
- ガード：`ShellPermissionTests`（OS との境界のエンジンのシェル許可の表。要求の種類 × コマンド・パス・URL・リダイレクト・迂回の 2 因子の組をすべて網羅する）。mutation 実証済み。
- 残余：ポリシーが読むのはコマンドの分節（`Commands` の各要素の `Identifier`＝実行ファイル名。SDK は生の文字列も渡すが読まない）と `PossiblePaths` / `PossibleUrls` / `HasWriteFileRedirection` だけで、引数を構造として解釈した情報は無い。
  そのため `dotnet` 自体が外へ出る経路（グローバルツールの導入と実行）は塞げない。
  コマンド文の自前構文解析で部分コマンドまで絞る方式は、引用・連結の解釈で静かに外すため採らない。ここは「SDK が渡す情報で表現できる最も狭い規則」であって封じ込めの保証ではない。
- 利用者向けの権限差は `docs/ai-chat.md`（.ja）の「注意」節の表が正本。モック生成での使い方は `src/QuickER.AI.Mock/CLAUDE.md`「AI モック生成」を参照する。

## CLI バックエンドの作業フォルダ

- 内蔵チャットの CLI バックエンドは作業フォルダを `%TEMP%\QuickER\{backend}\<guid>` へ隔離する。Codex だけアプリの実行フォルダのまま `ApprovalPolicy = never` で走っていると、承認なしにその配下を読めてしまうため。
- `CodexChatEngine.BuildThreadStartOptions` は一時フォルダを遅延生成して `Cwd` に据え、`Sandbox = "read-only"` を**明示**する（既定任せにしない）。
  チャットの仕事は `dynamicTools` 経由の図の編集だけでファイルを書く必要が無いのが、read-only にできる根拠。
- **モック生成側（`CodexMockProjectAgent`）は出力フォルダへ書くのが目的なので `workspace-write` のまま**。これが両者の唯一の差。
- 一時フォルダの作成と削除は 3 バックエンド共有の `BackendWorkingDirectory`（`Create` / `TryDelete`）。破棄時の `TryDelete` は子プロセス停止の後に呼ぶ（掴まれたままだと消せない）。ガード：`BackendWorkingDirectoryTests`。
- codex app-server のプロセス自身も `%TEMP%\QuickER\codex`（`BackendWorkingDirectory.ProcessRoot`）で起動する。
  実測した codex-cli 0.157.1 では設定・AGENTS.md はスレッドの cwd から読まれ、プロセスの作業フォルダは使われない。版の変更で読むようになっても実行フォルダに触れないための二重化。ガード：`CodexAppServerClientStartInfoTests`。
- Claude Code のターンとログインの確認は、終了を見届けなかったら（取り消し以外の例外でも）finally で子プロセスを止める。
  - ターン：`ClaudeCodeProcessClientTurnTests` の「取り消し以外の例外でターンを抜けても、claude の子プロセスを止める」
  - ログインの確認：`ClaudeCodeProcessClientProbeTests`（上限時間・破棄・起動直後の破棄で偽の claude〔`.cmd`〕をプロセスツリーごと止めて Unavailable を返す。既定の上限時間は 60 秒＝`DefaultProbeTimeout_IsSixtySeconds`。すぐ終わる claude では `ProbeLogin_WhenClaudeExitsImmediately_ReturnsUnavailableAndReleasesProcess` が控え `_probeProcess` の解放まで見る）

## Codex の送信の取り消しと書き込みロック

- `CodexAppServerClient.SendMessageAsync` は、標準入力への書き込みのタスクを取り消しと競わせて待つ（`WaitAsync`）。書き込みへ渡したトークンだけに頼らない。
  - 子プロセスの標準入力は同期のパイプで、書き込みへ渡したトークンは同期の書き込みの最中にしか効かない。スレッドプールが詰まって書き込みの始まりと取り消しがずれると、取り消しが効かないまま書き込みが止まり続ける（実測：1 秒で取り消した書き込みが 90 秒たっても戻らない）。30 秒の応答のタイムアウトも同じトークンなので、競わせないと要求は取り消しでもタイムアウトでも戻らず、Codex が終わるまで止まる。
  - 取り消しが勝ったら、書き込みの途中かもしれないので接続を捨てる（`AbortConnection`＝子プロセスを止め、パイプが切れて止まっていた書き込みも失敗で終わる）。停止のトークンが勝ったときは接続断の `InvalidOperationException`。
- 書き込みロックは書き込みが終わるまで持ち、ちょうど 1 回手放す。取り消しが先に戻るときは、解放を書き込みの継続へ渡す（finally の 2 つの枝は排他）。先に手放すと、終わっていない書き込みの後ろに次の行が書かれる。手放し忘れると、以後の送信と停止の上限つきの待ち（`WriteDrainLimit`）が止まる。継続の解放は最終破棄の後なら何もしない。
- 書き込みを取り消した要求（`SendRequestAsync`）は、応答待ちを見ずに取り消しの理由（タイムアウトか呼び出し元の取り消し。取り消しの登録と同じ優先順）を返す。接続を捨てた後始末が応答待ちを「接続断」で先に完了させ得るため。応答待ちの例外は観測してから捨てる。
- 取り消しが書き込みの終わりより先に勝つ経路はプールが詰まったときにしか起きないので、テストはテスト専用の `WriteCompletionForTests`（null なら製品の振る舞いは変わらない）で書き込みの終わりを門で止めて通す。
- 取り消しと破棄の経路を確かめるテスト（`CodexAppServerClientConnectionTests` の取り消しの 3 本と破棄の 2 本）は、テスト専用の `RequestTimeoutForTests`（既定は 30 秒＝製品の振る舞い）を `Timeout.InfiniteTimeSpan` にして、30 秒のタイムアウトを外す。
  - 呼び出し元の取り消しとこのタイムアウトは、どちらもスレッドプールで走るタイマーで知らされる。混んだ機械でプールの待ちが 30 秒に届くと、取り消しを処理する時点で両方が立っていて、上の優先順でタイムアウトとして返る（プールを 33 秒塞ぐと、門で止めた書き込みの取り消しもロック待ちの取り消しも `TimeoutException` になる）。テストが自分で取り消しても、そのテストの続きもプールで走るので同じ競争になる。
  - 破棄の 2 本は「破棄の後の決まった秒数の中で終わる」でなく、「上限の中で接続断の失敗で終わる」を表明する（タイムアウトが無いので、終わるなら破棄が終わらせた）。読み口が残る形はパイプが切れないので、停止の取り消しの `InvalidOperationException`（接続断）だけになる。
- ガード：`CodexAppServerClientConnectionTests.Request_CanceledBeforeWriteEnds_ReleasesWriteLockOnceWhenWriteEnds`（取り消しですぐ戻る・書き込みが終わるまでロックの CurrentCount が 0・終わったら 1）・`CodexAppServerClientConnectionTests.Request_WhenWriteIsStuck_CancelsAndAbortsConnection`（取り消しで戻り、接続を捨て、ロックを手放す）。mutation 実証済み（競争を外す・継続の解放を消す・その場でも解放する、のどれも赤）。

## ツール実行と活動通知

- チャットエンジン 4 種（API キー接続・Claude Code・Codex・Copilot）のツール実行と活動通知は `ChatToolInvocation` を通す。次の 2 つは 4 エンジンで同じでなければならない。
  - ディスパッチャ経由で実行し、例外は英語の失敗結果へ畳む
  - 活動通知は購読側の例外から守る
- 通知を保護しないと、購読側（画面）の例外が外側の catch へ落ち、実行済みの呼び出しへ「実行されなかった」合成結果が積まれる。AI がそれを見て同じ操作をやり直す（テーブルの二重追加）。
- 順序は「実行 → 履歴 → 実行済み記録 → 保護つき通知」。通知の失敗は共有キー `Chat_ToolActivityNotifyFailed` で知らせる。
- **引数は `Func<string>` で受け、取り出しも try の内側で行う**。値で受けると呼び出し側の引数評価が try の外で走り、Codex の `arguments` を持たない要求で `JsonElement.GetRawText()` が投げて、応答を送れないまま AI がツール結果を待ち続ける。
- `rethrowCancellation` が 4 エンジンの唯一の差。API キー接続だけが中断を通す（未実行の呼び出しへ合成結果を補って履歴の tool_use↔tool 対応を保つ経路へ渡すため）。
- 承認拒否・許可拒否の通知（`CodexChatEngine.RespondToApprovalAsync` / `CopilotChatEngine.OnPermissionDeclined`）も同じ作法で通す。前者は承認応答の送信より前に呼ぶので、保護しないと購読側の例外で応答まで巻き込まれる。
- ガード：`ChatToolInvocationTests`（作法のテスト）と、4 エンジンのソースの規則（`SourceRuleTests` の規則 `chat-engine-tool-invocation-uses`＝ヘルパーを通ること・`chat-engine-tool-invocation-copies`＝生の失敗文言と裸の `ToolActivityReceived?.Invoke(` が残っていないこと）と、振る舞いを 4 エンジンへ同じ行で流す `ChatEngineContractTests`（行 `tool-host-throws-is-english-failure-result`・`tool-activity-subscriber-throws-still-returns-result`）。mutation 実証済み。

## チャットエンジンの共通の契約

- ターン・ツール・取り消し・失敗・添付・破棄・接続の準備・会話の契約は、4 エンジン（API キー・Codex・Claude Code・Copilot）へ同じ行で流す網が守る（`tests/QuickER.Tests/AI/ChatEngines/`）。エンジンごとの違いは、アダプタの性質の宣言（出典つき）と期待の文の組み方に置き、行の中でエンジンを名指ししない。
- 宣言で「持たない」とした性質を前提にする行はそのエンジンでは流さず、網羅しない組の一覧（手で書いた静的な一覧）に載せる。宣言を変えて流れる組が増減すると、一覧との照合で赤になる。
- ガード：`ChatEngineContractTests`（行 × エンジン・`Coverage_MatchesGaps`・`Declarations_CoverEveryCapabilityWithEvidence`・`AttachmentSupport_MatchesEngineAndResolver`）・`ChatDriverTests`（API キー接続のドライバ 2 つへ同じ発話を流す）。mutation 実証済み。
- ツールの呼び出しの ID の往復はドライバの層の事実なので、契約の行（API キーのアダプタは中立の台本ドライバを使う）ではなくドライバの表が見る。
  - 行 `tool-call-id-round-trips`：本物の `ChatTurnEngine` に本物のドライバを載せて偽のサーバーへ 1 ターン流し、サーバーが付けた ID が 2 回目の要求のアシスタントのツールの呼び出しとツールの結果の両方へそのまま戻ることを照合する。ID を落とすと、サーバーがツールの結果を呼び出しへ突き合わせられない。
  - 偽のサーバーは、Anthropic が internal のクライアントの差し込み口へ渡す HttpClient のハンドラ、OpenAI がループバックの Kestrel（接続のエンドポイントの上書きで向ける）。
  - mutation 実証済み：OpenAI のストリームの `ToolCallId` を写さない・`BuildToolCalls` が ID を捨てる・履歴のツールの呼び出しの ID を変える・ツールの結果の ID を空にする、の 4 か所と Anthropic の対応する 3 か所のどれも赤。ストリームから ID を読む所は、この行だけが捕まえる。
