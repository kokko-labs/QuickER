# CSharpRuntime テンプレート

生成コードの正本（Scriban の部品 11 本）。`src/QuickER.Runtime*/Runtime*.g.cs` はここから再生成するパッケージ用ソースで、手で編集しない。

**部品を変えるときは、下の表の CLAUDE.md を先に読む**（ランタイムの規則はテンプレートの部品と 1:1 のランタイムパッケージのフォルダに置いてあり、このフォルダを読んでも自動では読み込まれない）。

## 部品の分割の規則

- `ScribanCSharpRenderer.LoadTemplate` が `CSharpTemplateParts.OrderedResourceNames` の固定順で部品を単純連結し、1 本のテンプレート本文へ戻して 1 回だけ解析する（Scriban の include は使わない）
- 各部品は分割前の連続した行の範囲そのもの。**並べ替え・境界での行の編集をしない**（連結結果が分割前とバイト完全一致であることが、生成コードのバイト一致の前提）
- 部品はすべて **CRLF 固定**（.gitattributes の `*.scriban text eol=crlf`）。連結順（番号順）と CRLF を崩さない
- テンプレートを変えたら固定フィクスチャ・パッケージ用ソース・公開 API の承認ファイルを再生成する（ルートの `CLAUDE.md` の「生成テンプレートと固定フィクスチャ」）

## 部品と読む CLAUDE.md

| 部品 | 読む CLAUDE.md | その部品で特に壊しやすいこと |
|---|---|---|
| `_00_HeaderAttributes.scriban` | `src/QuickER.CodeGen.CSharp/CLAUDE.md`「DB が値を作る列の書き込み除外」「DB 定義メタ属性」 | マーカー属性の emit を条件付きにすると、固定 infra が参照できずコンパイルできない |
| `_01_ValueObjects.scriban` | `src/QuickER.Runtime/CLAUDE.md`「値オブジェクト」「拡張シム層」 | Create を名前で引く箇所の `FlattenHierarchy` を外すと、生値が黙って素通しされる |
| `_02_Entities.scriban` | `src/QuickER.Runtime/CLAUDE.md`「グラフ保存」「拡張シム層」 | 列の判定は `[Column]` の許可リスト。除外リストへ戻すと手書きプロパティが実在しない列になる |
| `_03_EditModelsAndMappers.scriban` | `src/QuickER.Runtime/CLAUDE.md`「EditModel のロード意味論と検証経路」・`src/QuickER.CodeGen.CSharp/CLAUDE.md`「EditModel と Mapper の生成」 | 確定値が正本。キャンセル・ロードで入力文字列から確定値を作り直すと秒未満・Kind が落ちる |
| `_04_RepositoryContractCore.scriban` | `src/QuickER.Runtime/CLAUDE.md`「Save フック」「トランザクション失敗経路」・`src/QuickER.Runtime.Sqlite/CLAUDE.md`「接続ファクトリ（外部キーと busy_timeout）」 | catch から素の Rollback を呼ぶと、完了済みトランザクションの例外が元の失敗を消す |
| `_05_QueryPipeline.scriban` | `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」・`src/QuickER.Runtime.SqlServer/CLAUDE.md`「Include の無い取得と FOR JSON」・`src/QuickER.Runtime.Sqlite/CLAUDE.md`「Include のローダ（IncludeLoader）」 | NULL 補償を外すと SQL・EF Core・インメモリで結果が割れる。IncludeLoader の 3 か所のキー正規化はそろえる |
| `_06_RemoteSaveInfraAndDI.scriban` | `src/QuickER.Runtime/CLAUDE.md`「グラフ保存」「rowversion による楽観排他」「更新できる列が無い表の UPDATE」・`src/QuickER.Runtime.AspNetCore/CLAUDE.md`「クライアントの分類」 | EntitySaveMetadata の列集合を変えると、行バージョン・計算列・除外列の書き込み除外が黙って崩れる |
| `_07_InMemory.scriban` | `src/QuickER.Runtime.InMemory/CLAUDE.md` | ステージングの判定順（重複キー → 存否 → 版）とキーの比較器を崩すと、実 DB と結果が割れる |
| `_08_EfCore.scriban` | `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md` | `TrackGraph` へ戻すと Include して読んだ子の削除で親ごと消える |
| `_09_RemoteServer.scriban` | `src/QuickER.Runtime.AspNetCore/CLAUDE.md` | 分類の判定順と切断の扱いを変えると、切断が 400 / 500 に化けてログとフックが鳴る |
| `_10_Sync.scriban` | `src/QuickER.Runtime.Sync/CLAUDE.md` | ミラー版を書くのはダウンロードだけ。アップロードで書き戻すと再開点が嘘になり、行を恒久に取りこぼす |
