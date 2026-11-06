# QuickER.CodeGen.UI

C# コード生成ダイアログの設定とオプションの往復の規則。生成器の側の規則は `src/QuickER.CodeGen.CSharp/CLAUDE.md` を見る。

## 画面に出ていないオプション

- 生成ダイアログで画面に出ていない生成オプションは、設定にも結果にも持ち越さない
- `CSharpGenerationDialogViewModel.ToSettings` は 4 プロパティを、対応する表示条件との AND で書き出す
  - `GenerateRemoteContracts` / `GenerateRemoteServices`：`ShowRemoteContracts`
  - `GenerateSyncSupport`：`ShowSyncSupport`
  - `ExcludeUnboundedBinaryColumns`：`ShowExcludeUnboundedBinary`
- 落とすのは保存値と結果オプションだけ。**画面の値は保持する**（構成を選び直せば元のチェックが見える）
- 隠れたオプションに従う欄も、同じ判定で隠す。サーバー層フォルダの欄（`ShowServerLayerDirectory`）は、`ToSettings` と共有する `EffectiveGenerateRemoteServices`（`ShowRemoteContracts && GenerateRemoteServices`）を読む
  - 素の `GenerateRemoteServices` を読むと、DB アクセス「なし」で HTTP 実装の行が隠れても、チェックが入ったままなら欄だけが残る（生成物は変わらない）
  - ガード：`CSharpGenerationDialogViewModelTests.ShowServerLayerDirectory_TracksRemoteServices`（表示と変更通知）。mutation 実証済み
- ゲートを外すと、以前の構成で付けたチェックが画面に出ていないまま効き続ける
  - リモート対応と同期支援は、生成時エラー（契約が無い／実効方言が sqlserver + sqlite の 2 つでない）で止まる
  - 無制限バイナリ列の除外は、生成物が静かに変わる。`CSharpGenerationModelBuilder` の EditModel 必須判定（`isExcludedUnboundedBinary`）が `GenerateRepositories` のゲート無しにこのオプションを読むため、QuickER 版 Repository を生成しない構成でも、NOT NULL の無制限バイナリ列が必須入力から外れる
- ガード：`CodegenCaseTests` の行 `gui-gate-remote-dropped-no-db-access` / `gui-gate-sync-dropped-single-dialect` / `gui-gate-exclude-binary-dropped-no-db-access`。mutation 実証済み

## 設定の全キーの往復

- 設定（`CSharpGenerationSettings`）の全キーは、生成オプションとダイアログの往復で値を落とさない
- `GenerationSettingsParityTests` が、反射で全数を確かめる（キーを足して 1 か所だけ配線を忘れると赤）
  1. 設定と生成オプション（`CodeGenerationOptions`）の公開キーの集合が一致する（出力先の 2 つを除く）
  2. 全キーを既定と違う値にした設定の `ToCodeGenerationOptions` が、同じ値を渡す
  3. ダイアログへ読み込んで書き戻すと、全キーの値が保たれる。画面に出る構成を 2 通り（QuickER 版 Repository と EF Core）で流し、2 通りで全キーを既定と違う値で通ったことも表明する
- 配線は `ToCodeGenerationOptions`・`ToSettings`・`ApplySettings` の 3 か所で、どれか 1 キーが抜けると赤になる
- mutation 実証済み
