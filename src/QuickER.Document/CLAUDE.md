# QuickER.Document

保存単位 `DiagramDocument`（schema＝意味 ＋ layout＝視覚の 1 ファイル）と、図ファイルの読み書き（`JsonStorageService`）。

## 図ファイル形式の前方互換

`DiagramDocument.Version` は 1 のまま据え置き、`ExtensionData` による未知プロパティの保持も入れない（最小案）。

- 帰結：古い QuickER（0.1.0）で開いて保存すると、それ以降に足したフィールドは消える。System.Text.Json の既定で未知のプロパティは読み捨てるため。
- この帰結を「無害」に限定する規則：Version 1 のまま足すフィールドは、読み手が欠落・古い値を無害に扱える形に限る。無害でないフィールドを足すときは Version を上げる。
  上げれば 0.1.0 以降の読み手は `IsNewerFormat` で気付く。GUI は開く前に続行確認・上書き保存前にも確認、外部変更の自動再読込と MCP の変更系は拒否、CLI は警告。
- 「無害に扱える」の判定例：
  - `Entity.PrimaryKeyColumnIds` は欠落＝空リストで、読み手は列宣言順へフォールバックする。失われるのは順序の明示だけで、主キーの膜は `Column.IsPrimaryKey` が運ぶので図は壊れない。ガード：`DocumentFormatTests`（行 `load-legacy-json-without-primary-key-column-ids-yields-empty-list`・`load-explicit-null-primary-key-column-ids-normalized`）。
  - `Column.IsComputed` は欠落＝false で、失われるのは「書き込みから外す」指定だけ（列そのものは `Column` が運ぶ）。生成コードがその列を書きに行き実 DB がエラーを返す従来の挙動へ戻るだけで、図は壊れない（黙ったデータ喪失ではなくエラーとして表に出る）。`bool` なので明示 null はリストと違い修復せず読込を拒否する。ガード：`DocumentFormatTests`（行 `load-legacy-json-without-is-computed-yields-false`・`tryload-explicit-null-is-computed-rejected-as-invalid-json`）。
  - `Column.IsIdentity` は欠落＝false で、失われるのは書き込み除外だけ（計算列と同じ判定）。
- 0.1.0 以降に v1 のまま足した公開プロパティは上の 3 つ。`Entity.UniqueConstraints`・`Relationship.ColumnPairs` は 0.1.0 以前（`git diff v0.1.0..HEAD -- src/QuickER.Model src/QuickER.Document` で確認済み）。
- 規則そのものは設計判断で機械検査できないため、ガードはない。

## 図ファイルの読込検査

Id 重複・必須の文字列の欠落の検査の置き場は `JsonStorageService.TryLoad` の 1 箇所で、拒否だけを行い修復しない。

- GUI（開く・再読込・起動時の作業状態復元）・MCP ツールホスト・CLI が共有する唯一の形式検証経路なので、呼び出し側へ散らさない。
  起動時の復元が検証の外にあると、重複 Id の `last_diagram.json` を復元した時に、DBMS 切替の未処理例外→緊急保存も同じ理由で失敗→`Environment.Exit(1)` という最悪経路へ入る。失敗時は従来の復元失敗と同じく空の図で起動する。
- 検査するのはエンティティ Id・同一エンティティ内の列 Id・エンティティをまたぐ列 Id・リレーション Id の 4 つ（`FindDuplicateId`）。4 つは別々の分岐で、報告する情報が違うので、片方を消してももう片方では代替できない。
- リレーションの Id も辞書のキーに使われる（`ConstraintNames.ResolveForeignKeyNamesById` の `ToDictionary`＝重複すると MCP・内蔵チャットのツールが例外、`CSharpGenerationModelBuilder` のナビゲーション名の辞書＝黙って上書き、`CSharpReverseParser` の関係のメタデータ）。一意制約・クエリの Id は参照も辞書化もされないので検査しない。
- Id を張り替える「修復」は実装しない。重複はアプリ側のどの経路でも作れず JSON の手編集でしか起こらないので、意味を推測するより名指しして断るほうが直せる。
- 拒否メッセージには必ずテーブル名（列なら列名も、リレーションなら制約名か親と子のテーブル名）と重複した Id を含める。英語固定（例外メッセージと同じ扱い）。見出しは表示側の resx：GUI は `Open_DuplicateId`・CLI は `Cli_SchemaDuplicateId`・MCP は英語リテラル。
- `TryLoad` の逆直列化とルートキーの評価は try の内側に置く。ルートのキー重複（`FindDuplicateRootProperty` が `JsonException` で断る）も型不一致の `JsonException` も、外へ漏れると呼び出し側は「読めなかった」以上を言えない。
- `Version < 1` は `NotDiagramDocument` として拒否する。黙って受理すると `Version` キーを持つだけの JSON が図として通る。
- 必須の文字列の欠落も同じ置き場で拒否する。欠落はキーが無いときと明示の `null` の 2 つ。テーブル名・列名・列の型・クエリ名・パラメータ名・射影フィールド名の 6 つは、既定値で埋めず `FindMissingRequiredText` が場所を名指しして断る（`DocumentLoadError.MissingRequiredText`）。
  - キーの欠落は、読込の設定の型情報の修飾子（`CreateWithRequiredTextUnset`）が 5 つの型を「必須の文字列を `null` にしたインスタンス」から作ることで、明示の `null` と同じ値として検査へ届く。検査は 1 つの分岐で両方を見て、文言も区別しない（`missing or null`）。モデルの初期値（`NewTable`・`int` など）は GUI・ツールの新規作成のための値で、キーの無い図に当てると壊れた図がそれらしい名前・型のまま開き、上書き保存で元の内容が戻らない。差し替えるのは作り方だけで、ほかの初期値と直列化の出力は変わらない。
  - Id 重複より先に検査する。名前が `null` のままでは重複の報告が場所を名指しできない。
  - 線引きは「推測で埋めると図の意味が変わるか」。説明・メモは空文字へ修復する（`Normalize`。対象 DBMS も従来どおり既定の方言へ修復する）。`Normalize` は 6 つの必須の文字列に触れない（触れると検査に届かない）。列の型を既定の `int` で埋めるのは名前を勝手に付け直すのと同じ性質の書き換えで、そのまま上書き保存すると手編集した JSON の元の内容が失われる。
  - bool・列挙・Id・参照先の Id のキーの欠落は従来どおり既定値で吸収する（行 `load-omitted-keys-fall-back-to-defaults`）。必須の文字列と違い、既定値が「それらしい偽の名前」にならないため（Id と参照先の欠落の扱いは別の検討）。
  - 欠落が起きるのは JSON の手編集だけ。0.1.0 以降の版はこれらのキーを必ず書く（初期値が非 null で、`WhenWritingNull` で消えるのはメモリ上が `null` のときだけ）。モデル側は非 NULL 許容宣言、`GetString()!` の呼び出しは全て `ValueKind == JsonValueKind.String` で守られ、他の経路は NRT ＋ `TreatWarningsAsErrors` がビルドで止める。要素そのものが `null`（`"Columns": [null]`）のときは従来どおり `Compact` が黙って捨てる。
  - 検査しない `Load`（テストのフィクスチャの読み戻し用・src から呼ばない）は、欠落も明示の `null` も `null` のまま返す（同じ設定で読む）。
  - 表示側は Id 重複と同じ 4 経路（GUI＝`Open_MissingRequiredText`・CLI 2 箇所＝`Cli_SchemaMissingRequiredText`〔生文字列〕・MCP は英語リテラル）。
- ガード：`DocumentFormatTests`（行 `tryload-duplicate-*`＝拒否 4 種＋文言にテーブル名と Id＋非対象 Id の受理、TryLoad の分類の行 `tryload-*-invalid-json` / `tryload-*-not-diagram-document`、必須の文字列の行 `tryload-null-*`・`tryload-missing-*`〔6 つの文字列ごとに明示の null とキーの欠落を対にした行〕・`tryload-missing-required-text-reported-before-duplicate-id`）・`DocumentFileTests`（行 `doc-restore-duplicate-ids-empty`＝起動時の復元、行 `openfail-*`＝開くの文言。乱数の手の列は断った読込の後に図・パス・未保存・ディスクが変わらないことを毎手で表明する）。mutation 実証済み。
