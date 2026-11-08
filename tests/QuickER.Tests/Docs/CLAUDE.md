# tests/QuickER.Tests/Docs

CLAUDE.md・docs・方言の宣言の整合ガード（依存図・フォルダ一覧を実体と機械照合、docs 索引・ライセンス文書のパリティ、docs の方言の表と宣言の照合）。
テストのフォルダ構成全体は、ルートの `CLAUDE.md` の索引に従う。

## docs の方言の表

docs の方言ごとの違いの表は宣言と一致する。表の直前の HTML コメントの目印 `<!-- dialect-table: 名前 -->` で探し、`DialectDocsTableTests` が英日の表を方言の宣言と照合する。表は `docs/database(.ja).md` の 14 個と `docs/code-generation(.ja).md` の 3 個。

### 照合の共通規則

- 目印は英日それぞれ 1 回で、テストの登録表（`Tables`）と一致する。登録の無い表は照合されずに静かに嘘になる。
- 英日の同じ目印の表は、行数・列数と記号のセル（✅ と —）が一致する。語のセルは英日で比べない。記号のセルは、記号だけか、記号の後ろに括弧を 1 つ置いた形。
- 方言の並びは `DialectNames.All` の順のプロバイダの表示名。
- 英語の行見出しから宣言への対応表で、セルの ✅ / — を宣言と比べる。行の集合は対応表のキーと完全一致。
- 表や行を足すときは、同じ区切りで対応表を足す。宣言に無い事実は表に書かず、表と同じことを言う本文の文は消して、理由と注意の文だけを残す。
- 固定は `DialectDocsTableTests` の 22 テスト：`Markers_AppearOnceInEachLanguageAndAreRegistered`・`EnglishAndJapaneseTables_AgreeInShapeAndSymbols`・`DialectsFollowDialectNamesOrder`・`SymbolCells_AreBareOrFollowedByParentheses`、各表の照合（下に表ごとに挙げる）、`ImportWarningTable_CoversEveryWarningKind`。mutation 実証済み。Docs の外のテストが見ない件（取込の警告の表・モデル化する範囲の表・`DEFAULT` の行の括弧・接続の表の一部）は、この照合だけが見張る。

### docs/database(.ja).md

- `sync-capabilities`（検出される差分の節）：`SyncDialectCapabilities` の 8 行。「既存の表の変更を文で実行する」は `SupportsAlterColumn && SupportsForeignKeyAlter`（`SyncPlanner` が再構築の方言を決める式の否定）。`SyncCapabilitiesTable_MatchesDeclarations`。
- `foreign-key-rebuilds`（主キーの同期の節）：同じく 7 つ。付け直しの失敗で外れたまま残り得る＝`!TransactionalDdl`。`ForeignKeyRebuildTable_MatchesDeclarations`。
- `identifier-length-limits`（識別子の長さの上限の節）：行は方言。`IdentifierLengthLimit` の Unit / Max / TableFileNameMax / RepresentsSupplementaryCharacters と `TruncatesLongIdentifiers`。数・単位の語・長すぎる名前の語も英日で比べる。`IdentifierLengthLimitTable_MatchesDeclarations`。
- `import-warnings`（取込の警告の節）：行は警告の種別の集まり。種別を `SchemaImportWarningKinds.DefinitionKind` で種類へ写し、`UnsupportedTreatments` の取込の扱いが None でない方言に ✅。種類を持たない種別（範囲外の外部キー・表名と列名の衝突・ドットを含む表名）は、取込のコードが警告を出す方言をテストの定数で持つ。最後の列は、まとめて告げる種別（`IsAggregated`）だけが「取り込まず名前を並べる」。対応表が `SchemaImportWarningKind` の全種別を 1 回ずつ受け持つこと（網羅）も確かめる。`ImportWarningTable_MatchesDeclarations`。
- `imported-type-spelling`（取り込んだ型の表記の節）：行は方言。宣言の表記を `NormalizeTypeNotation` に通すと取込の表記になる（振る舞いの照合）。`ImportedTypeSpellingTable_MatchesNormalization`。
- `modeling-fidelity`（図がモデル化する範囲の節）：方言の列を持たない。取込・DDL・SQLite の再構築のセルの語を、種類の取込・DDL・同期（SQLite）の扱いの値から決める。セルが方言名を挙げるなら、扱いが None でない方言とちょうど一致。`ModelingFidelityTable_MatchesTreatments`。
- `same-type-spellings`（検出される差分の節）：行は方言。「同じとみなす」綴りの組は `AreSameType` が真、「差分のまま」の組は偽（振る舞いの照合）。`SameTypeSpellingsTable_MatchesAreSameType`。
- `referential-actions`（検出される差分の節）：行は方言。`SupportsOnUpdateAction` / `SupportsSetDefaultAction` / `SetDefaultRequiresColumnDefault` と、扱いの表 E1・E2 の Ddl（— のセルは OmitWithAnnotation で注記を名乗る）と Diff（Fold の参照アクションだけを差分の列が名指しする）。`ReferentialActionsTable_MatchesDeclarations`。
- `column-alteration`（検出される差分の節）：照合順序は A4 の同期の扱いが Preserve。`DEFAULT` の保持と型の種類の変更の確認は `ChecksDefaultOnColumnTypeChange`。`DEFAULT` の行の括弧は、A2 の同期の扱いが Warning の方言だけが「DB が保つ」。`ColumnAlterationTable_MatchesDeclarations`。
- `descriptions`（説明の同期の節）：行は方言。`SupportsDescriptions` が偽の方言だけが取込元と書き方を — にし、BMP の列を空にする（E5 の Ddl が Annotation ならコメント行と書く）。BMP の列は `IdentifierLengthLimit.RepresentsSupplementaryCharacters`。取込元のコードスパンは方言の `*SchemaImporter.cs` に、書き方のコードスパンは説明つきの図の DDL に現れる。日本語のコードスパンは英語の同じ行と一致する。`DescriptionsTable_MatchesDeclarationsAndCode`。
- `unconvertible-types`（方言切替の節）：行は元の図の列の形、列は移行先の 5 方言。行見出しの方言の表示名の後ろのコードスパンを、その方言の型の見本として GUI の方言切替と同じ `DiagramTypeConverter.CreatePlan` に通す。同じ行の見本は移行先ごとに結果がそろう（そろわない見本を足すと赤）。✅ ⇔ 変換する（移行先が見本と同じ方言なら元のまま）。✅ の括弧のコードスパンは変換後の型。「NOT NULL lifted／NOT NULL を解除」⇔ `MakeNullable`。「up to 38 digits／38 桁まで」（数は `DiagramTypeConverter.CappedPrecision` から作る）⇔ `PrecisionCapped`。`UnconvertibleTypesTable_MatchesTypeConversion`。
- `target-limits`（方言切替の節）：行は上限を確かめる方言だけ（`DialectNames.All` の順の一部分＝向き `DialectRowSubset`）。各方言の `TryFormat` の境界を見る：文字列・バイナリは `名前(数)` を読んで上限ちょうどで true・1 つ超えで false、decimal の精度は (P,0) と (P+1,0)、スケールは下限と上限（「0 to the precision／0〜精度」は精度を超えるスケールを書き出さない）、小数秒はその方言が書き出せる時刻系の種類ごと。数は桁区切りのカンマを外して読み、英日の表をそれぞれ照合する。`TargetLimitsTable_MatchesTryFormatBoundaries`。
- `mysql-integers`（方言切替の節）：方言の列を持たない。MySQL の `TryParse` の正規型の種類を「中立の型」の語（Decimal は精度・スケールのコードスパン）と比べ、MySQL から SQL Server への `CreatePlan` の変換後の型を SQL Server の列のコードスパンと比べる。日本語のコードスパンは英語と一致する。`MySqlIntegersTable_MatchesTypeConversion`。
- `connection-settings`（接続の指定の節）：行は方言。列は接続先・既定のポート・方言固有の項目。`ConnectionSettingsTable_MatchesDialogAndFactories`。
  - 語のセル（Host／ホスト・File path／ファイルパス・Create new／新規作成・Authentication／認証方式・TrustServerCertificate・SSL Mode・not encrypted／暗号化されない）：語がある ⇔ `DbConnectionDialogViewModel` がその項目を出す（`ShowServerFields` / `ShowFilePath` / 同期の画面の `ShowCreateNewFile` / `ShowAuthMode` / `ShowTrustServerCertificate` / `ShowSslMode` / `ShowOracleEncryptionNote`。接続プロファイルの保存先は一時フォルダ）。
  - 「service name／サービス名」⇔ 接続文字列の組み立てが「データベース名」欄を EZConnect のサービス名に使う。認証方式の括弧の名前の並び ⇔ 接続ダイアログの認証方式の表示名（`DbConnectionDialogViewModel.AuthModes` の並び。英語の表は中立の resx、日本語の表は ja の resx の値）。既定のポートの数 ＝ `IDatabaseProvider.DefaultPort` ＝ ポートを空けた `BuildConnectionString` が使うポート（— ⇔ null）。
  - 「対応 DBMS」の版だけの表は、宣言で照合できないので目印を付けない（通信路の暗号化・取込の範囲の表と同じ）。

### docs/code-generation(.ja).md

- `csharp-types`（DB 型と C# 型の対応の節）：行は方言ごとにまとまった行と最後のすべての方言の行（向き `DialectRowGroups`。方言は `DialectNames.All` の順に 1 つ残らず現れ、最後の行の見出しは「Any／共通」）。DB の型のセルの各コードスパンをその方言の型マッパー（`TypeMapper.ResolveColumnTypes`）に通し、C# の型のコードスパンが 1 つなら別名のすべてがその型へ写り、複数なら位置で組にする。「Left out／外す」⇔ `IsExcludedFromEditModel`。すべての方言の行は 5 方言とも `IsFallbackType` かつ `string`、ほかの行は代替の型でない。「`float(1)` to `float(24)`／〜」の範囲は端を照合し、範囲のすぐ外の型が同じ C# 型へ写らないことも確かめる。日本語のコードスパンと範囲の数は英語の同じ行と一致する。`CSharpTypesTable_MatchesTypeMappers`。
- `unbounded-binary`（無制限バイナリ列の除外の節）：行は方言。除外する列のコードスパン ⇔ `IsUnboundedBinary` が真、除外しない列 ⇔ 偽。`(n)` は数へ置き換え、`(MAX)` のように括弧だけのコードスパンは同じセルの各型の後ろに付けて試す。`UnboundedBinaryTable_MatchesTypeMappers`。
- `database-numbering`（DB が採番する列の節）：行は方言。`DatabaseNumberingTable_MatchesDeclarations`。
  - 値を拒む採番の ✅ ⇔ IdentityRejectingValues の `DialectDeclarations.CanOccur` ⇔ 方言切替が使う `IdentityColumnMarks.CanBeHeldBy`。✅ なら取込の扱いが MarkWithNotice、— なら「持てない」と「切替」を名乗る。
  - 値も受け付ける採番の ✅ ⇔ IdentityAcceptingValues があり得て取込の扱いが Notice。生成 DDL の注記の ✅ ⇔ IdentityRejectingValues の Ddl の扱いが Annotation。
  - 括弧のコードスパンは ✅ のセルだけに書き、その方言の `*SchemaImporter.cs` に現れる。日本語のコードスパンは英語の同じ行と一致する。

## CLAUDE.md の照合

`ClaudeMdParityTests` は、ルートの `CLAUDE.md` の「機械照合できる一覧」（アーキテクチャ図の依存矢印・プロジェクトの網羅・テストフォルダ一覧・固定フィクスチャ一覧）が実リポジトリと一致することを固定する。
変更を能動的に書き換える動機が生まれない一覧（依存方向図の参照列挙・フォルダ列挙）の側を、実体と突合して陳腐化を止める。
次の書式を契約にするので、ルートの `CLAUDE.md` の書式を変えるときはこの契約とテストも更新する。

- アーキテクチャ図：「QuickER.Model」で始まる最初の素の ``` フェンスブロック。フェンスは開閉が対で現れるため、正規表現の単発マッチでなく行走査で開閉を対応付ける（単発で探すと直前ブロックの閉じフェンスを開きと誤認し、以降の切り出しがすべてずれる）。
- 依存矢印行：「QuickER.X → A, B, C」。参照列挙の終端は空白 2 個以上か行末。矢印を持たない行（はしご部・依存ゼロ宣言）は矢印照合の対象外。参照列挙は実 csproj の ProjectReference と過不足なく一致する。
- 全プロジェクト：`src/` 配下の全 `QuickER.*` プロジェクトが `CLAUDE.md` のどこかに登場する（図または本文。ランタイムパッケージ 7 種は図でなく「ランタイム配布」節の本文で説明されるため、全文を対象にする）。直後にプロジェクト名の続きが来ない出現を要求し（`QuickER.Runtime` が `QuickER.Runtime.Sqlite` の部分文字列として拾われる誤検出を避ける）、方言プロバイダの並記は「/ 短縮名」の形も正とする。
- テストのミラー一覧：`tests/QuickER.Tests/{A|B|…}/` の中括弧。横断フォルダは「横断フォルダは」を含む行のバッククォートの「`X/`」列挙。どちらも `tests/QuickER.Tests` の実フォルダと一致する。
- 固定フィクスチャの箇条：`- **テンプレート変更時は**` で始まる行から、次に行頭が `- ` の行の直前まで（途中の再生成コマンドのフェンスと、それに続く段落も本文に含む）。照合はこの本文に限る。`CLAUDE.md` 全体で探すと、別の節での言及で偶然通る。
  - `GeneratedFixture/` 直下の `*.g.cs` がすべて載っている。本体の生成物は「ファイル名」か「`.g.cs` を外した名前」のどちらでも載っていればよい（2 ファイル構成のフィクスチャは「本体＋`X.RemoteServer.g.cs` の 2 ファイル構成」と書く流儀のため）。ただし基底名の一致は直後が `.RemoteServer` でない出現に限る（サーバー側の記載だけで本体の記載漏れを見逃さないため）。`*.RemoteServer.g.cs` はフルのファイル名で書く。
  - `*FixtureDriftTests` がすべて載っている。`RuntimePackageSourceDriftTests` とサンプルのドリフトテスト（`EcOrderSampleDriftTests` / `EcOrderRemoteSampleDriftTests`）は別の文で扱うので照合しない。
  - 名前の一致は両側の識別子境界つきで見る。フィクスチャ名どうしが包含する（`SqlServerBinaryFixture.g.cs` ⊃ `BinaryFixture.g.cs`・`SqlitePortableFixture.g.cs` ⊃ `PortableFixture.g.cs`）ので、左境界が無いと別のフィクスチャの記載で本体の記載漏れが素通りし、右境界が無いと同じ箇条に残る別の記述（`ComputedColumnFixtureDriftTests` や `BinaryFixtureDefinition.Build()`）で一致してしまう。

`ClaudeMdReferenceTests` は、リポジトリの全 CLAUDE.md（ルートと領域のフォルダ。`bin` / `obj` / `.git` / `.claude`〔worktree のコピーがある〕/ `tasks` は探さない）が書いた名前の実在を固定する。
索引から詳細を指す書き方と、不変条件ごとにガードの名前を書く運用は、名前が古くなっても誰も気付かないので、書かれた側を実体と突き合わせる。

- テストの名前：末尾が `Tests` か `TestsBase` の識別子を拾い、`Prefix{A,B}Suffix` の中括弧の省略は展開する。tests/ の C# の型の宣言にあること。直前が `*` の名前（`*DdlGeneratorTests`）はパターンなので照合しない。`…ForTests` で終わる名前（`ResetForTests` など）は製品のテスト用メンバーなので拾わない。実装先をスラッシュで並べる省略は、最後の部分が別の名前に読まれるので、中括弧の形（`RemoteService{Ado,EfCore}RuntimeTests`）で書く
  - 名前の直後が `.cs` ならファイルの実在、`.メソッド名` ならそのクラスを宣言するファイルにメソッド名が現れること。メソッド名が `_` で終わるか直後が `*` なら前方一致
- 期待の行・シナリオの Id：ハイフンを含み、ハイフンで終わらないバッククォートの小文字の語のうち、最初のハイフンまでの語が行 Id の接頭辞の集合にあるもの。tests/ の C# ソースに現れること
  - 接頭辞の集合は手で持たない。tests/ の C# ソースの文字列リテラル（補間・連結の先頭を含む）の先頭の、最初のハイフンまでの語から機械で作る（`ClaudeMdReferenceTests.RowIdPrefixesOf`）。表に新しい接頭辞の行を足せば、そのまま照合の対象に入る
  - この集合は表の行 Id より広い（Integration の表は QuickER.Tests から列挙できないので、行 Id の出どころであるリテラルから作る）。実質は「CLAUDE.md がバッククォートで名指すハイフンつきの小文字の語は、tests/ のソースに実在する」の照合になる。行 Id でない語（`paths-ignore` など）も、多くは tests/ のどこかに現れて実在の照合を通る
  - 接頭辞が当たるのに行 Id でない語（型表記の例・ワークフローの成果物の名前など）は、理由を添えて `ClaudeMdReferenceTests.NotRowIds` に置く。一覧の語が CLAUDE.md から消えるか tests/ に現れるようになったら赤になる
  - 長い行 Id の後ろ半分だけの略記（`swapped-pairs-lost` のような書き方）は、接頭辞が当たらず照合されないので、行 Id を全部書く
  - Id の多くは補間（`$"realdb-column-drop-{…}"`）か連結（`"realdb-pk-on-new-column-" + shape`）で組み立てるので、ハイフンで切った接頭辞（ハイフン 2 つ以上）が補間か連結の直前（`P{` / `P"`）に現れるか、接頭辞と残りの文字列リテラル `"S"` の両方が現れれば実在とみなす。組み立てる Id は接頭辞までしか照合できない
  - `*` を含む Id は断片ごとに照合する。ハイフンで始まる断片は、そのまま現れるか、先頭のハイフンを外した文字列リテラルの先頭（`"S`）として現れればよい（`$"module-{値}-{名前}"` の後ろを別のリテラルで組み立てる形）。Id でない語（`{方言}-386-…` のような書き方の例・ハイフンで終わる接頭辞の言及）は拾わない
- CLAUDE.md への参照：リポジトリのルートからの相対パスをバッククォートで囲む形（例：`src/QuickER.Provider/CLAUDE.md`「取込」）。直後の「」に見出しを書いたときは、その CLAUDE.md にその文字列の見出し（`#` の行）があること。見出しの文字列には書式記号（`**`・バッククォート）を入れない
- 影響の地図の名前（`ClaudeMdReferenceTests.MapNames_ExistInImpactMap`）：バッククォートの中身の全体が守りの名前の形のもの（エンジンの部分は `{エンジンの ID}/{部分}`＝ID は `EngineIds.All`・部分は大文字で始まる英数字、単体の表は `table:` ＋英字で始まる英数字、規則・照合・文言の行は `rule:` / `parity:` / `english:` / `phrase:` ＋小文字・数字・ハイフン）を拾い、地図と同じ関数（`ImpactMapSources.Protections` の名前）に実在すること。前置きは `ProtectionNames` の 1 か所で、地図を作る側と同じものを使う（地図の Markdown を読み直さない・写しを作らない）
  - 形で拾うので、パスの断片（`CodegenConfig/Judges/`・`GeneratedRuntime/`）・書式の説明（`rule:{Id}`）・ID でない語（`Gui/Services`）・バッククォートの外の名前は拾わない。テストのフォルダの名前がエンジンの ID と同じもの（`CodegenConfig`）のパスは、末尾に `/` を付けるか親のフォルダから書く（`CodegenConfig` と `/` と大文字で始まる名前だけのパスは地図の名前と区別できない）
  - 地図の名前の直後の（…）の中と「の行 `a` / `b`」に書いた行 Id は、その守りのテストのソース（部分ファイルとフォルダを広げたもの）に現れること（`ImpactMapSources.OwnsRowId`）。行 Id の拾い方は上の期待の行と同じで、組み立てる Id は守りのソースの中で組み立てていればよい。テストのソースを持たない守り（規則・照合・文言の行）に続く行 Id は照合しない
  - 地図の名前を 1 つも拾わないと赤（拾う形の空振りを止める）
- 自己検査（`Checker_ReportsFictitiousNamesIdsAndLinks`）が、架空のテストの名前・メソッド・ファイル・Id・パス・見出し・地図の名前・別の守りの行を名指しすることと、パターン・前方一致・組み立てる Id・`…ForTests` のメンバー・地図の名前に似た語・続けて書いていない行を名指ししないことを固定する。mutation 実証済み（地図の名前は、架空の名前を書く・台帳の部分を改名する・別の守りの行を続ける、のどれも赤）

### src・ワークフロー・スクリプト・生成テンプレートの名指し

- src の XmlDoc・コメント、`.github/workflows`・`scripts`・生成テンプレート（`*.scriban`）が名指すテストの名前と行 Id も実在する。テストを改名したり表の行へ移したりすると、CLAUDE.md と同じく古いまま残る（テストのクラスを表の行へ移したとき、src の XmlDoc 5 か所と Integration のテストの名前の古い形 3 か所が残っていた）。
- 照合はソースの規則の表の禁止の規則 4 つ（範囲ごとに 1 行：`comment-test-names-src`・`comment-test-names-templates`・`comment-test-names-workflows`・`comment-test-names-scripts`）で、読み方は上の `ClaudeMdReferenceTests.FindMissingTestNames`・`ClaudeMdReferenceTests.FindMissingRowIds` をそのまま使う（写しを作らない。書式の契約を変えると両方に効く）。
  - C# のソースはコメント（`//`・`///`・`/* */`。条件コンパイルのどの分岐も読む）だけを読む。ワークフロー・スクリプト・テンプレートは本文の全部を読む（テストの絞り込みの文字列もテストの名前を書くので）。
  - 行 Id は「行」の直後に書いたものだけを拾う（`行 xxx-yyy`・`行 <c>xxx-yyy</c>`・``行 `xxx-yyy` ``）。
  - 既知の一覧は空（例外を持たない）。
- ガード：`SourceRuleTests`（規則 `comment-test-names-src`・`comment-test-names-templates`・`comment-test-names-workflows`・`comment-test-names-scripts`／自己検査の行 `comment-test-names-src-forms`・`comment-test-names-src-inactive-branch`・`comment-test-names-workflows-forms`）。mutation 実証済み（src の XmlDoc に消えたクラスの名前を戻す・ワークフローに架空の名前を足す、のどちらも赤）。
