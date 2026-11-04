# QuickER.Settings

設定 JSON の汎用永続化ストア `JsonSettingsStore<T>`（net10.0）。AI / CodeGen.UI / Gui が参照する。保存先のフォルダは `src/QuickER.Platform/CLAUDE.md`「保存先」。

## 知らないキーの引き継ぎ

設定ファイルの保存は、型が知らないキーを引き継ぐ。

- `JsonSettingsStore.Save` は書き込みの直前にディスクの JSON を読み直し、`TSettings` が知らないキーを再帰的に引き継ぐ。派生 3 つ（`GuiAppSettingsStore` / `AiSettingsStore` / `CSharpGenerationSettingsStore`）が共有する。
- 読み込みが未知のキーを読み捨てる以上、引き継がずに書くと、新しい版の QuickER が足したキーを古い版で 1 度保存しただけで失う。設定は図ファイルと違って前方互換の版番号を持たず、`IsNewerFormat` に相当する拒否・確認の仕組みが無いので、黙って捨てると利用者の設定がただ消える。
- 既知のキーは型のメタデータから求める：`JsonOptions.GetTypeInfo(typeof(TSettings)).Properties` を走査し、再帰するのは `JsonTypeInfoKind.Object` のプロパティだけ（辞書・配列・値は値ごと置き換え）。
- シリアライズ結果のキーから求めない。(a) 辞書型プロパティは JSON ではオブジェクトとして出るため、再帰すると「利用者が消したエントリ」が未知キーとして復活する。(b) 将来 `DefaultIgnoreCondition` が入ると、利用者が null にした既知のプロパティが出力に現れず、ディスクの旧値が未知キーとして復活する。どちらも今は該当する設定型が無いので表に出ないが、1 つ足した瞬間に警告なく起きる。
- キー名の比較は読み込み側と同じく大文字小文字を区別しない。読み込みは `PropertyNameCaseInsensitive`（CLI の quicker.json と同じ規則で、PascalCase のファイルを GUI の「設定読込」で読める）。引き継ぎの照合も `OrdinalIgnoreCase` にそろえ、入れ子へ入るときは書き出す側の子を型の綴りで引く。片方だけ区別すると、既知として読んだ `"RootNamespace"` を未知として持ち越し、次の読み込みで古い値が保存した値を上書きする。
- null を許さない参照型のプロパティへの明示 `null` はキーの欠落と同じに読む：resolver modifier `TreatNullAsMissing`（NRT 注釈の `JsonPropertyInfo.IsSetNullable` で判定。コンストラクタの既定値のまま）。代入すると読み手が NullReferenceException で落ち、コード生成ダイアログが開けなくなる。コレクションの要素の null は対象外。
- 読み直しが `Unavailable` のときは何も書かない。読めないまま上書きすると、そこにあった未知のキーを黙って捨てる。`Missing` と壊れた JSON はマージせずに書く（`.corrupt` への退避は従来どおり）。
- `SaveTo`（エクスポート）はマージしない。
- 割り切り：配列の要素の中の未知キーは運べない（要素の増減・並べ替えで突き合わせが決まらない）。`connections.json`（`SqlConnectionProfileStore`）はこのストアを通らないため対象外。
- ガード：`JsonSettingsStoreTests`（`Save_AfterFailedBackup_DoesNotBackUpHealthyFile` を含む）・`CSharpGenerationDialogViewModelTests`（設定読込は CLI の quicker.json〔PascalCase〕を読む・設定の参照型のキーがすべて明示 null でも開けて確定できる）。mutation 実証済み。

## 保存の失敗の扱い

利用者の 1 操作を保存する呼び出し側は、保存の失敗（例外と `false`）を両方扱う。

- `Save` は書いたかを `bool` で返す。基底の `TryUpdate`（読み直し→変更→保存）と `TrySave`（`Save` の例外を投げない形）が失敗の理由を返す。
- 呼び出し側：`GuiAppSettingsStore.TryUpdate`（設定メニューの更新確認・言語切替）・コード生成ダイアログの OK。`false` と書き込みの例外の両方を失敗として知らせる。
- AI の接続設定（`ChatConnectionSettingsViewModel`）：モデルの履歴の × は保存できたときだけ候補から消す。読み込みが `Unavailable` なら候補を作り直さない（既定値から作り直すと履歴が消えて見える）。失敗はコマンドの外へ例外を出さずに `ModelHistoryRemoveFailed` で親へ知らせる（UI の例外の受け皿がアプリを終了させるため）。成功したターンの記録と接続設定の保存は、`false` も例外にして親の既存の通知へ渡す。
- 書けたら壊れていた印（`LastLoadCorrupt`）を戻す。残すと、退避に失敗した後の次の保存が正常なファイルを退避先へ写す。接続プロファイルは書く前の読み直しで印を作り直すので要らない。
- ガード：`ChatConnectionSettingsSaveFailureTests`（書けない・読めないときの × と接続設定の保存）・`JsonSettingsStoreTests.Save_AfterFailedBackup_DoesNotBackUpHealthyFile`。mutation 実証済み。
