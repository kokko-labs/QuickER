# QuickER.AI.UI

AI 共有 UI 部品（接続パネル・添付・`ApiKeyStore`・チャット回答の Markdown 表示・`EngineShutdown`）。

## 回答の Markdown 表示

AI の回答は外部の入力なので、組み立て（`ChatMarkdownRenderer`）は次を守る。

- **リンクは http / https に限る。** リンク先を `UrlPolicy.TryGetSafeUri`（QuickER.Platform・http / https の絶対 URL でホストを持つものだけ）に通す。
  通らないもの（`file:`・UNC・`//server/share`・`javascript:`・`ms-settings:` 等・相対・メール）はリンク文字列を普通の文字で出す。
  `//server/share` は絶対 URL として解釈すると UNC になるので断る。
- `Hyperlink.NavigateUri` を設定せず、クリックで `IChatLinkOpener.Open(Uri)` だけを呼ぶ。WPF の既定のナビゲーション経路に開く処理を担わせず、開く経路を 1 本にする。
- ツールチップは開く URL を `UrlPolicy.ToDisplayText` の表記で出す。国際化ドメイン名のホストは punycode で出す（`AbsoluteUri` は Unicode のまま出すので、見た目の似た別の文字のホストを見分けられない）。
- `UrlPolicy` はユーザー情報（`user:pass@`）を持つ URL を断る。`https://example.com@evil.test/` は別のホストに見せられる。
- 開く側 `ShellChatLinkOpener` も `UrlPolicy.IsSafe` を再検査してから `ExternalLauncher.OpenUrl`（そこでも同じ規則で確かめる）へ渡し、開けなかったときは原因の例外文を添えて `IDialogService.ShowError` で知らせる（握り潰さない）。
- **画像（`LinkInline.IsImage`）は代替テキストだけを出す。** Image も Hyperlink も作らず、外部 URL への通信を起こさない。
- **HTML は解釈しない。** パイプラインの `DisableHtml()` で HTML として扱わせないうえ、`HtmlBlock` / `HtmlInline` が来ても文字として出す（二重化）。
- パイプラインは `UsePipeTables` / `UseAutoLinks` / `DisableHtml` の 3 つだけを明示する。`UseAdvancedExtensions` のような一括有効化は、属性の付与・数式・図など審査していない構文まで一度に開くので使わない。
- ガード：`ChatMarkdownRendererTests`（http / https 以外のリンクは押せない普通の文字になる・開いてよいリンク先は絶対 URL でホストを持つものだけ・リンクは押せて NavigateUri が null でツールチップに実際の URL を出す・画像は代替テキストだけ・インラインとブロックの HTML は文字として出す）・`ShellChatLinkOpenerTests`（http / https 以外の URL は渡されてもシェルへ渡さない・開けなかったときは URL と原因を添えてエラーで知らせる）。mutation 実証済み。
- 外部のプロセス・URL を開く入口の規則は `src/QuickER.Platform/CLAUDE.md`「外部の起動」を参照する。

## 組み立ての失敗はテキスト表示へ

- 回答の組み立てに失敗したら、その吹き出しだけテキスト表示へ落とす。
  Markdig はブロックの入れ子を 128 段で `ArgumentException` にして断るが、強調などインラインの入れ子には上限が無く、組み立ての再帰は約 7,000 段でスタックを使い切ってプロセスごと落ちる（捕まえられない）。
- `ChatMarkdownRenderer.Render` は解析直後に構文木の深さを**再帰を使わずに**（`MeasureDepth`＝明示スタック）測り、`MaxNestingDepth`（32）を超えたら組み立てに入らず `InvalidOperationException` で断る。
- 表示部品 `AssistantMessageView` は `Render` の例外をすべて受け止めてテキスト表示へ落とし、失敗した本文を覚えて同じ本文で組み立てを繰り返さない。依存関係プロパティの変更コールバックから例外を漏らすと、アプリの未処理例外になる。
- 隣り合う飾りの無い Run は `MergePlainRuns` で 1 つにまとめる。対にならない記号は Markdig が 1 文字ずつ別の節点にし、リンクの中に短い Run が数千並ぶと WPF のレイアウトが数十秒から数分かかる（`[` の 10,000 段で 200 秒超が 2.3 秒に）。レイアウトの遅さは例外にならないので、表示部品の捕捉では救えない。
- 帰結：連続した対にならない `[` が 30 個前後を超える回答は、Markdig が節点を入れ子にするため深さの上限に掛かり、テキスト表示になる。
- ガード：`AssistantMessageViewTests`（入れ子が深すぎる回答は落ちずにテキスト表示になる＝強調・引用・リストの 3 通り。強調の入れ子が上限を超える回答は組み立てずに断る）・`ChatMarkdownRendererTests`（対にならない記号が続く回答は飾りの無い文字を 1 つの Run にまとめる）。mutation 実証済み。

## 吹き出しの幅

- 文章だけの回答の吹き出しを内容の幅へ縮めるときは、先に部品を測ってから幅を決める。
- `RichTextBox` は内容の幅に縮まないので、`AssistantMessageView.MeasureOverride` が Markdown 表示の部品の `MaxWidth` を「内容の幅＋内側の余白＋余裕」にする。
  内容の幅は `ChatMarkdownWidth.Measure`＝文書の直下が文章の段落だけのとき、インラインを書式ごと折り返さない `TextBlock` へ写して測る。それ以外は null（最大幅）。
- **`RichTextBox` は文書の `PagePadding` を最初のレイアウトの途中で自分の既定（左右 5）へ置き換える**（代入直後は 0、Loaded の時点で 5,0,5,0 を実測）。先に幅を決めると 10 狭くなり、最後の文字が次の行へ落ちる。ビルドもレイアウトも通り、例外にもならない。
- そのため、先に `base.MeasureOverride` で部品を測ってから幅を求め、上限が変わったときだけ測り直す。
- 内容の幅は（本文・書体・大きさ・描画方式）をキーに覚えるが、**内側の余白（PagePadding ＋ Padding ＋ BorderThickness）は覚えずに毎回その時点の値を読む**。
- 余裕 `FitSafetyMargin` は 4。測る側と表示する側の丸めの差は実測で 0.22 までで、余裕 0 では折り返しが出る。狭すぎると文字が欠けるので広いほうへ倒す。表示倍率は 100% でしか実測していない。
- ガード：`AssistantMessageViewTests`（短い 1 行の回答は吹き出しが最大幅より狭くなる・長い段落の回答は最大幅で折り返す・表を含む回答は最大幅のまま・太字や見出しを含む短い回答でも縮めた幅で文字が欠けない〔4 通り〕・縮めた吹き出しもウィンドウを狭めればその幅で折り返す）。mutation 実証済み。

## 保存済みの API キー

- `ApiKeyStore.Load` は「無い／読めた／読めない」を分けて返し（`SavedApiKey`・`SavedApiKeyStatus`。接続パスワードの `SavedPassword` と同じ形）、読めないキーを空の値で上書きも削除もしない。
- 読めないもの（別のユーザー・別の PC で暗号化したもの・壊れたファイル・一時的なロック）を空として返すと、保存のオフや接続設定の保存が暗号ファイルを消す。
- 接続設定の VM（`ChatConnectionSettingsViewModel`）は、読めないときキー欄の下に注記を出し（`IsSavedApiKeyUnreadable`）、新しいキーが入力されるまで保存の呼び出しをしない。
- ガード：`ApiKeyStoreTests.Load_UndecryptableFile_IsUnreadable`・`ConnectionSettingsTests`（行 `failure-unreadable-api-key-is-neither-overwritten-nor-deleted`。どの行でも、読めない API キーを新しいキーの入力が無いまま保存の関数へ渡さないことを組が見張る＝不変条件 1）。mutation 実証済み。

## エンジンの破棄の作法

- ウィンドウの `ForceClose`（AI チャット・モック生成）は、閉じる前に次を通す。
  1. VM の `RequestInterrupt()` で実行中のターン・生成を打ち切る
  2. VM の `ShutdownEngines(上限)` でエンジンを破棄する
- 中断が止められるのは実行中の処理だけで、ターンを実行していない常駐プロセス（codex app-server・copilot ランタイム）はエンジンの破棄でしか止まらない。放置すると claude / codex / copilot / dotnet の子プロセスが孤児になる。
- 破棄は必ずスレッドプールで走らせて上限つきで待つ。終了経路は UI スレッドなので、直接待つとデッドロックし得るのと、ハングした破棄でアプリの終了を長く止めないため。上限は既定 3 秒。
- 個々のエンジンの破棄は独立で、1 つの失敗が他を止めない。この作法の正本は `EngineShutdown.DisposeAll`。
- 各段の例外の受け止め（`ShutdownSteps.Run`）と終了の連鎖は `src/QuickER.Gui/CLAUDE.md`「終了の連鎖」を参照する。モック生成の追加の段（プローブ用エンジンの破棄）は `src/QuickER.AI.Mock/CLAUDE.md`「ForceClose」を参照する。
- ガード：`AiChatDialogForceCloseTests`（ForceClose はエンジンの破棄まで通す）・`ChatDialogTests`（行 `shutdown-disposes-all-engines`＝ShutdownEngines は全エンジンを破棄する・`shutdown-hanging-dispose-returns-within-timeout`＝破棄がハングしても上限で戻る）・`EngineShutdownTests`。
