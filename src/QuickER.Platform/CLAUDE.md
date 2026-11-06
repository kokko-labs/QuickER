# QuickER.Platform

OS との境界（原子的ファイル書き込み `AtomicFile`・書き出しの文字コード `TextEncodings`・保存先と一時フォルダ `AppDataPaths`・プロセス／URL の起動 `ExternalLauncher`・パスの判定 `PathSegments` / `PathContainment`）。依存ゼロ・net10.0。
Platform の外はここの入口を通す。通さない出現は各ガードが理由つきの一覧で数える。

## 保存先

アプリ設定・作業状態の保存先はローカル（`%LOCALAPPDATA%\QuickER`）。算出は `AppDataPaths.LocalRoot` の 1 つだけ。

- 使うのは `JsonSettingsStore` の既定コンストラクタ（gui-settings.json / ai-settings.json / codegen-settings.json）・`SqlConnectionProfileStore`（connections.json＋DPAPI シークレット）・`ApiKeyStore`・自動保存（last_diagram.json）・クラッシュログの 5 つ。一時フォルダ `%TEMP%\QuickER` も `AppDataPaths.TempRoot` の 1 つ。
- ローミング（`%APPDATA%`）へ置かない。ここにあるのが機械固有の作業状態と DPAPI（CurrentUser）で保護した秘密で、ドメインのローミングプロファイルで他端末へ複製されても復号できず、別端末のパスを運ぶだけになるため。
- モックのプレビューの WebView2 のユーザーデータも `AppDataPaths.LocalRoot\WebView2`（`MockPreviewPanel.DefaultUserDataFolder`）。既定の実行ファイルの隣は、インストール版では Velopack が更新のたびに入れ替える `current` の中、ポータブル版では書き込めないことのある場所。
- 移行処理は書かない。ローミング側の旧ファイルは読まず、既存ユーザーの設定は一度リセットされる（CHANGELOG に明記済み）。
- ガード：`AppDataPathsTests`（`LocalRoot` が LocalApplicationData 配下でローミングの配下でないこと）・`SourceRuleTests` の規則 `app-data-path-lookups`（`Environment.GetFolderPath` の LocalApplicationData / ApplicationData / UserProfile と `Path.GetTempPath` の呼び出しが Platform の外では理由つきの一覧だけであること。構文木で数え、一覧に無い出現も無くなった出現も赤）・`MockPreviewPanelRuntimeTests`（実ランタイムで指定した置き場所にデータができる。ランタイムが無ければスキップせずに失敗）。mutation 実証済み。

## .cmd / .bat シムの起動

`.cmd` / `.bat` シムの起動は必ず `BatchShimProcessGuard` を通す。

- `ProcessStartInfo.FileName` が `.cmd` / `.bat`（npm 導入の CLI の典型）のとき CreateProcess は内部で `cmd.exe` を経由し、.NET が C ランタイム規約で組んだコマンドラインを cmd がもう一度自分の構文で解釈する。引数の `"` や `&` が引用の外へ抜けて任意コマンドの実行につながる（BatBadBut 型）。
- 入口が 2 つあり、引数の形が違えば取り得る防御も違う。
  - `WrapCommandLine`：連結済みの 1 本の文字列（Codex の app-server 引数）。トークン境界が分からず個別に引用できないため、`" & | < > ^ %` を含んだら起動前に拒否する。
  - `Apply(ProcessStartInfo)`：`ArgumentList`（Claude Code）。トークン境界が分かるので各トークンを `"` で包み、内側の `"` を `""` へ倍加し、`"` 直前の連続 `\` を C ランタイム規約どおり倍にする。引用の内側では `& | < > ^ ( )` は cmd にとってただの文字になるため、残る危険は `%` の展開（展開結果が `"` を持ち込める）だけで、これだけを拒否する。
- 包み直しは両入口とも `cmd.exe /d /s /v:off /c`。遅延展開の `!` は `/v:off` で無効化する（利用者がレジストリの `DelayedExpansion` で有効にしていても展開させない）。
- シムのパス自体の `"` と `%` も両入口で拒否する（`%` は cmd が環境変数として展開し、起動する実体が別物に化け得る）。Copilot の `CopilotCliLocator.EnsureSafeToLaunch` は同じ 2 文字に加えて `!` を拒否する。SDK が cmd を起動するので `/v:off` を付けられないため。
- `Apply` の側をメタ文字の一律拒否にしない。Blazor のシステムプロンプトは `<Routes @rendermode="InteractiveServer" />` を含み、引数で渡る以上、モックプロジェクト生成が `.cmd` 導入環境で丸ごと動かなくなる。
- 改行だけは引数・パスのどちらでも両入口で拒否する。cmd は `%*` を展開する段階で最初の改行から後ろを捨て、その引数の残りだけでなく後続の引数もすべてエラーなしに消える（実機プローブで確認）。引用しても防げない（`cmd.exe /d /s /v:off /c` へ包み直す前の段階で起きる）のでガード層で拒否するしかない。複数行の内容はファイル経由で渡す（`ClaudeCodeProcessClient.AppendSystemPromptArguments` がシステムプロンプトを `%TEMP%\QuickER\claude-code\{GUID}.txt`〔BOM なし UTF-8〕へ書いて `--append-system-prompt-file` で渡し、ターン終了後の finally でベストエフォート削除する）。
  防御の二重化として、引数の積み順は「`--model` / `--resume` が先・プロンプト系が後」。
- 改行の判定は `BatchShimProcessGuard.ContainsNewline` を Copilot の `EnsureSafeToLaunch` と共有する（写しを持たない）。
- Copilot は例外。起動が GitHub.Copilot.SDK の内側で QuickER が引数を組み立てないため、掛けられるのは解決パスの検証（引用符・`%`・`!` の拒否）だけ。`EnsureSafeToLaunch` は起動直前に置く。解決側へ置くと `IsAvailable` の検出だけで例外になる。残余は docs/ai-chat.md の注意節。文言 resx はバックエンドごと（どの CLI が止まったかを名指しするため）で、ポリシーだけが共有。
- 起動する実体はフルパスで渡す（防御の二重化）。包み直しの `cmd.exe` は `BatchShimProcessGuard.CmdExePath`（`Environment.SystemDirectory` 直下）、codex は `CodexCliLocator` が PATH から解決したフルパス（`CodexAppServerClient.ResolveLaunch`）。
  共有の PATH 走査 `PathExecutableResolver` は相対要素（`.` 等）を読み飛ばし、同じフォルダの `.exe` / `.cmd` / `.bat` は PATHEXT の順に試す（無ければ既定の `.exe`・`.bat`・`.cmd`。PATHEXT に無い候補とほかの拡張子は試さない。コマンドラインで名前を打って起動されるものと同じ）。
- シムの判定 `IsBatchShim` は拡張子を見る前に末尾の空白と `.` を落とす。Windows が落として開くので、落とさないと `x.cmd ` が包み直しも引数の検査も通らずに cmd へ渡る。
- `DotnetBuildRunner` の `dotnet` だけは名前のまま。名前だけの起動は `.exe` を補って探し `.cmd` を拾わない（実測）のに対し、共有の PATH 走査は `.cmd` も候補にするため、フルパス化すると PATH 前方の `dotnet.cmd` で引数ガードの無いバッチ経由の起動へ化ける。実測では .NET 10 の `Process.Start` は名前だけの `FileName` をカレントフォルダ・`WorkingDirectory` から探さないため、単独では穴ではない。
- ガード：`ProcessLaunchTests`（OS との境界のエンジンの起動の表。入口〔Guard の 2 入口・Claude / Codex / Copilot の配線・PATH の走査・ロケータ〕× 入力の形の行：包み直しの決定的な引用形式 `shim-wrap-shim-wrapped-in-quoted-form`・パスの `%` の拒否 `shim-{wrap,apply}-rejects-path-percent`・Copilot の `%` と `!` の拒否 `shim-copilot-rejects-path-{percent,exclamation}`・Codex の起動 `shim-codex-launch-*`・相対の PATH 要素を読み飛ばす `resolve-{codex,copilot}-skips-relative-entries`・PATHEXT の順 `resolve-pathext-*`。網羅しない組は `ProcessLaunchDimensions.Gaps`）・`BatchShimProcessGuardTests.CmdExePath_システムフォルダ直下のcmdをフルパスで指す`（cmd.exe の実在）・`BatchShimTrailingNameTests`（実プロセス）。mutation 実証済み。

## 外部の起動

外部のプロセス・URL・フォルダを開く処理は `ExternalLauncher` を通す。

- 入口は 4 つ。
  - `OpenUrl`：`UrlPolicy`（http / https の絶対 URL でホストを持つものだけ）を通す。About のリンク・Codex のログイン URL・チャットの回答のリンク。
  - `OpenFolder`：フルパスで在るフォルダだけ。
  - `StartExecutable`：フルパスだけ（自分の再起動）。
  - `CreateProcess`：標準入出力をつなぐ子プロセスを起動前の状態で返す（claude 2・codex・dotnet）。シェル経由の起動・名前だけの実行ファイル（`BareExecutableNames`＝`dotnet` だけを許す。上の `.cmd` の項の理由）・包み直していない `.cmd` / `.bat` のシム（`BatchShimProcessGuard` で `cmd.exe` の形へ包み直してから渡す）を断る。
- シムのガードと PATH の解決（`BatchShimProcessGuard`・`PathExecutableResolver`）も Platform に置く。
- 入口ごとに起動を書くと、URL のスキームの制限やフルパスの解決が入口ごとに食い違う（Codex のログイン URL がホストを確かめていなかった例がある）。
- QuickER がプロセスを作らないもの（GitHub.Copilot.SDK・Velopack・WebView2）は対象外。
- チャットのリンクを開く側 `ShellChatLinkOpener` も `UrlPolicy.IsSafe` を再検査してから渡す（`ProcessLaunchTests` の行 `external-chat-link-*`）。
- ガード：`SourceRuleTests` の規則 `process-launches`・`process-launch-create-process-starts`（`SourceSymbolScan` で src の Platform の外をシンボルで判定し、静的な `Process.Start`・`Process` の生成〔目的型の `new()` も〕・`UseShellExecute` への定数の false でない値の代入〔値が定数でないものも〕・入口の名前が `ShellExecute` / `CreateProcess` / `WinExec` で始まる P/Invoke を 0 にする。インスタンスの `Start()` は、受け手が `ExternalLauncher.CreateProcess` の結果で初期化した局所変数のときだけ許す。解決できない呼び出しは「未解決」として同じ一覧に出す。自己検査の行 `process-launches-forms` が、using static・別名・目的型の new・非定数の代入・P/Invoke を拾い、CreateProcess の戻り値の起動・定数の false・無関係な P/Invoke を拾わないことも固定する）・`ProcessLaunchTests`（OS との境界のエンジンの起動の表の行 `external-*`：開いてはいけない URL の表は起動せずに断る・フルパスでない／無いフォルダ・名前だけの実行ファイル・包み直していないシム・シェル経由の子プロセスを断る・`dotnet` だけは名前のまま）。mutation 実証済み。

## パスがフォルダの中にあるかの判定

判定は `PathContainment` だけが持つ。

- `IsSameOrUnder`（同じか中）・`IsUnder`（中・同じは含まない）・`IsDirectChild`（直下）・`Normalize`（フルパスにして末尾の区切りを落とす。ドライブ直下 `C:\` は区切りで終わる形のまま）。
- 両方を `GetFullPath` で正規化してから大文字小文字を区別せずに比べる。中にあるかの前方一致は、フォルダが区切りで終わるなら区切りを足さない。正規化できない値は「中にない」。
- 使う入口：Copilot の自動承認（`CopilotWorkspacePermissionPolicy`）・API キー方式の提出の最後の脱出の判定（`ResolveEmitPath`）・モックのプレビューの遷移とサブリソース（`MockPreviewPanel.IsNavigationAllowed` / `IsResourceAllowed`＝直下）・削除してよいかの重なり（`MockProjectFiles.PathsOverlap`。リンクを実体まで解決してから渡す）。
- 入口ごとに前方一致を書くと末尾の区切りの扱いが食い違う（出力フォルダがドライブ直下のとき Copilot だけが `D:\` に区切りを足して `D:\\` と比べ、配下を 1 件も承認しない、という形）。
- リンク・8.3 の短い名前・`\\?\` の形の別名は追わない。
- ガード：`PathContainmentTests`（ドライブ直下・UNC・末尾の区切り・大文字小文字・`..`・兄弟の前方一致・同じパス・正規化できない値の表）・`PathContainmentCallerFuzzTests`（同じ表を 4 つの呼び出し元へそれぞれの入力の形で流す。提出はすべての根の形で中を受け付ける。呼び出し元 × 行の形の網羅は `PathContainmentCallerFuzzTests.Containment_Coverage_MatchesGaps`＝OS との境界のエンジンの部分 Containment）・プレビューの URI の次元（スキーム・fragment・非 .html・null）は `PathEntranceTests` の行 `preview-*`・`SourceRuleTests` の規則 `path-prefix-comparisons`（`string.StartsWith` の第 1 引数〔局所変数なら初期化子を 3 段まで〕が `Path` の区切りの扱いか `GetFullPath` を使う呼び出しを、src の Platform の外で 0 にする。シンボルで判定し、解決できない呼び出しは「未解決」の別の欄。自己検査の行 `path-prefix-comparisons-forms` と Platform 自身の 1 件〔規則の空振りの検知〕で走査の空振りを確かめる）。mutation 実証済み。

## パスの名前の判定

利用者の設定・AI の出力から受け取った名前を出力先の下で使う入口の判定は `PathSegments` だけが持つ。`CheckSegment`（1 区間）か `CheckRelativePath`（相対パス）で判定し、断るときは `PathSegments.Describe` の文言で当たった規則を名指しする（UI 言語で出す入口と、AI へ返す英語固定の入口がある）。

- 入口は 9 つ。
  - 層フォルダ、生成コードと API リファレンスの出力サブフォルダ（`LayerDirectoryValidator`）。
  - API リファレンスのファイル名。
  - 非分割の出力ファイル名（生成前の診断 `CodeGen_Error_InvalidOutputFileName`。そこから作る `.RemoteServer.g.cs`・API リファレンスの名前も判定する。分割出力では使わないので判定しない）。
  - 生成物のファイル名（`GeneratedFileWriter`）。
  - モックのプロジェクト名（`MockProjectScaffoldService.CheckProjectName`）。あわせて、生成するプロジェクトが自分用に使うフォルダの名前（`MockProjectEmitTools.ProtectedFolderNames`＝Generated・design・obj・bin）を大文字小文字を問わず断る。API キー方式の提出はパスのどの区間でもこの名前を断るので、その名前のプロジェクトは必ず生成に失敗する。一覧は提出の判定と 1 つで、写しを持たない。ダイアログは実行と同じく前後の空白を除いた名前で判定する。
  - 画面のファイル名（`MockFolderStore.CheckScreenFileName`）。マニフェストの画面を読む API キー方式の素材読み `ApiKeyMockProjectAgent.ReadScreenHtml` も通し、断る名前は読まない（開いた後に書き換えられた `mock.json` で設計フォルダの外を読まない）。
  - API キー方式の提出のパス（`MockProjectEmitTools.ResolveEmitPath`）。
- 規則（`PathSegmentViolation`）：空・`.` と `..`・区切り・絶対パスとドライブ・使えない文字（制御文字と `" * : < > ? |`。OS に依らず Windows の集合）・区間の先頭の空白・区間の末尾の `.` と空白・`~`（8.3 の別名）・予約されたデバイス名（拡張子つきも。`CONIN$`・`CONOUT$`・上付き数字の `COM¹`〜`COM³`・`LPT¹`〜`LPT³` も）・255 文字超。
- 設定の値（層フォルダ・出力サブフォルダ・API リファレンスのファイル名）と提出のパスは、値全体の前後の空白を取り除いてから判定する。決まった形への正規化で、意図と違う場所へは書かない。断るのは Windows が黙って落とす区間の内側の空白だけ。
- 提出のパスはさらに、AI の出力の境界の前処理として空の区間と `.` の区間を落としてから判定する（AI がよく書く `./MockApp/…` を断らない）。先頭の `/` は落とす前に断る（落とすと `/a` が `a` になる）。落として何も残らなければ空として断る。共有の `CheckRelativePath` は `.` も空の区間も断る（設定の入口には `./` を許す理由が無い）。
- 入口ごとに判定を持つと断る形が食い違う。
- ガード：`PathSegmentsTests`（規則ごとの表）・`PathSegmentEntranceFuzzTests`（形と違反の一覧は `PathSegmentForms` の 1 つを `PathSegmentsTests` と共有する。入口 × 違反の網羅は `PathSegmentEntranceFuzzTests.Segments_Coverage_MatchesGaps`＝OS との境界のエンジンの部分 Segments。断るべき形 37〔1 区間 26・相対パス 11〕を 9 つの入口のそれぞれで成り立つ形に組み立てて流し、すべて断りメッセージに当たった規則の文言を含むこと、値全体の前後の空白と提出のパスの落とす区間は断らずに受け付けること、正当な名前〔250 文字の名前を含む〕を断らないこと、プロジェクト名の入口が予約のフォルダ名を断ることを見る）・`CodegenCaseTests` の行 `diag-outputfilename-invalid-slash` / `diag-outputfilename-invalid-reserved-name` / `diag-outputfilename-derived-name-too-long`・`MockProjectScaffoldServiceTests`（プロジェクト名で断るフォルダの名前は、提出がすべて断られる名前と同じ）・`MockGenerationDialogViewModelTests`（予約されたフォルダ名のプロジェクト名では生成不可・前後の空白は除いて判定する）・`PathEntranceTests`（行 `screen-read-*`＝画面ファイルの素材読みは断る名前を読まない・行 `emit-*`＝`./`・空の区間・前後の空白を含む提出のパスを受け付け、落とした後の保護フォルダは断り、`.` だけ・`./` だけは空として断る）・`SourceRuleTests` の規則 `path-name-checks`（`Path.GetInvalidFileNameChars` / `GetInvalidPathChars` / `IsPathRooted` が src の Platform の外で 0）。mutation 実証済み。

## ファイルの書き込みと文字コード

ファイルへの書き込みは `AtomicFile` を通し、UTF-8 の文字コードは `TextEncodings` だけが作る。

- 素の書き込みは既存のファイルを切り詰めてから書くので、途中で落ちると利用者のファイルが中途半端な内容で残る。
- 対象は図・設定・接続プロファイルに加えて、エクスポート 7 種（DDL・DBML・Mermaid・HTML と Excel の定義書・SVG・PNG）・生成物（`GeneratedFileWriter`）・モックの出力（画面・設計書・単一 HTML・スキャフォールド・API キー方式の提出・実行ログ）・秘密（`ApiKeyStore`・接続パスワード）。
- ストリームで書く出力（PNG のエンコーダ・ClosedXML の `SaveAs(Stream)`）は `AtomicFile.Write(path, Action<Stream>)` の中で書く（渡したストリームを閉じない）。
- 文字コード：`TextEncodings.Utf8NoBom`（不正なサロゲートは例外。AtomicFile の既定・内容のハッシュ・UTF-8 の検査）と `Utf8NoBomLenient`（U+FFFD へ置き換え。利用者の入力や AI の出力をそのまま含む書き出し・ログ・子プロセスの標準入出力）の 2 つ。BOM 付きのエクスポートは BCL の `Encoding.UTF8`。
- ガード：
  - `SourceRuleTests` の規則 `raw-file-writes`：src の Platform 以外のプロジェクトを `SourceSymbolScan`（TestSupport）で BCL とテストの出力の DLL を参照にしたコンパイルとして組み、呼び出し・生成が指すメソッドのシンボルで数える。目的型の `new()`・`using static`・別名・`FileInfo` も同じ API として拾う。
    - 数えるのは、File / FileInfo / Directory / DirectoryInfo の書き込み・作成・写し・移動・置き換え・削除、FileMode / FileAccess が定数で読み取りと言えない `File.Open`・`File.OpenHandle`・`FileInfo.Open` と `FileStream` の生成（変数の FileMode は書き込みとみなす）、パスを受ける `StreamWriter` の生成と `SaveAs`。ストリームを受ける多重定義は数えないので、`AtomicFile.Write` のラムダの中も特別扱いしない（ラムダの中の File.* は数える）。
    - 対象の名前でシンボルが解決しない呼び出しは「未解決」として同じ一覧で照合し、`SourceRuleTests.SymbolScan_ResolvesBclCallsInEveryProject` がどのプロジェクトでも BCL の呼び出しを解決できていることを表明する（参照の組み立てが壊れても空振りしない）。
    - 理由つきの一覧：%TEMP% の一時ファイル・一意の名前を CreateNew で作るクラッシュログ・読めない設定の退避の File.Copy・Platform を参照できないランタイムのパッケージ 7・自分の一時物の後片付け・remove_screen の画面の削除・最終ビルドの前の obj / bin の削除・空の値の保存による秘密の削除。厳密に照合する。ガードが拾う書き方は自己検査の行 `raw-file-writes-forms` のソースで固定する。
  - `SourceRuleTests` の規則 `utf8-encoding-creations`：`new UTF8Encoding(` と型が UTF8Encoding の `new(` が Platform の外で 0。
  - `AtomicFileTableTests`（OS との境界のエンジンの原子的な書き込みの表。書く形 × 保存先 × 失敗の段の行で、書く行はすべて一時ファイルの残りが無いことを共通に確かめる）：`Write` の途中の失敗で保存先が無傷（行 `atomic-write-stream-writer-throws-*`）。並行・読み取りのリトライの時間・ハンドルは `AtomicFileTests` が実 OS で見る。
  - mutation 実証済み。

## 一時ファイルの名前

原子的な書き込みの一時ファイルの名前は `AtomicFile.TemporaryPathFor` だけが組み立て、残骸の判定は `AtomicFile.IsTemporaryFileName`。

- 形は `{元の名前}.{GUID の 32 桁}.tmp`。元の名前は 1 区間の上限（255）に収まるよう先頭を残して切る（サロゲートペアの途中では切らない）。元の名前をそのまま前に付けると、218 文字を超える名前の保存先では一時ファイルの名前が上限を超え、パスの判定（`PathSegments`＝255 まで）を通った名前の保存が必ず失敗する。
- フォルダの中身を写す処理（モックの同梱）は残骸をこの判定で除く。
- 生成ランタイムの `UnboundedBinaryColumns.ReadToFileAsync` は Platform を参照できないので写しを持つ（同じ名前の規則・ディスクへの確定・保存先があれば File.Replace・競合の再試行）。一致は `UnboundedBinaryReadToFileTests` が一時ファイルの名前を `IsTemporaryFileName` に通して確かめる。
- File.Replace が使えないときの代わりの移動（`MoveOverPreservingMetadata`）は、保存先の属性（読み取り専用を除く）と ACL を移した先へ写す。最善努力で、写せなくても保存は成功。ACL は Windows でだけ写す。読み取った `FileSecurity` をそのまま渡すと変更の印が無く何も書かれないので、SDDL から作り直す。
- ガード：`AtomicFileTableTests`（行 `atomic-temporary-path-*`＝長さ 1〜255・サロゲート、行 `atomic-is-temporary-file-name-*`＝判定の表、行 `atomic-write-all-text-long-file-name-*`）・`AtomicFileNamingAndMetadataTests`（隠し属性・移動の属性と ACL）・`GeneratedFileWriterLongNameTests`・`MockFolderStoreTests.SaveScreen_LongFileName_Succeeds`・`UnboundedBinaryReadToFileTests`（名前の形・隠し属性）・`MockProjectScaffoldRegenerationTests.Scaffold_SkipsAtomicWriteLeftoversInMockFolder`。mutation 実証済み。
