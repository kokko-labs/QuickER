using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 主キー変更（<see cref="SchemaDiffKind.AlterPrimaryKey"/>）セクションのフェーズ。
/// </summary>
/// <remarks>
/// <para>
/// 逐次 DDL 方言では主キー変更を「旧主キーの解除（<see cref="Drop"/>）」と「新主キーの付与（<see cref="Add"/>）」の
/// 2 セクションへ分割し、その間に列定義変更（<see cref="SchemaDiffKind.AlterColumn"/>）を挟む。旧主キー列の
/// NULL 許容化は主キー制約が残ったままでは失敗する（SQL Server Msg 5074 → 4922・Oracle ORA-01451 等）ため。
/// </para>
/// <para>
/// <see cref="None"/> は主キー変更以外の全セクション（既定値）。主キー変更セクションに <see cref="None"/> が
/// 現れた場合、レンダラーは DROP → ADD を 1 セクション内で連続出力する。
/// </para>
/// </remarks>
public enum PrimaryKeyPhase
{
    /// <summary>フェーズの概念を持たないセクション（主キー変更以外の全種別）。</summary>
    None = 0,

    /// <summary>旧主キー制約の解除フェーズ（列定義変更より前に置く）。</summary>
    Drop,

    /// <summary>新主キー制約の付与フェーズ（列定義変更より後に置く）。</summary>
    Add,
}

/// <summary>
/// 同期スクリプトの 1 セクション分（同一種別の差分項目の集合）を表す実行計画の単位。
/// </summary>
/// <remarks>
/// セクションは <see cref="SyncPlanner"/> が固定順序で並べて <see cref="SyncPlan"/> を構成する。
/// 方言別レンダラー（<see cref="ISyncScriptBuilder"/> の実装）はこの単位を消費して SQL 化する。
/// 同一 <see cref="Kind"/> のセクションが複数回・別位置に現れることがある（主キー変更の 2 フェーズ）。
/// </remarks>
public sealed class SyncPlanSection
{
    /// <summary>このセクションが担当する差分種別。</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>
    /// 主キー変更セクションのフェーズ（主キー変更以外は既定の <see cref="PrimaryKeyPhase.None"/>）。
    /// </summary>
    public PrimaryKeyPhase PrimaryKeyPhase { get; init; } = PrimaryKeyPhase.None;

    /// <summary>このセクションに含まれる差分項目（入力の出現順を保持する）。</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; init; } = [];
}

/// <summary>
/// 実行計画に付随する警告の種別。
/// </summary>
/// <remarks>
/// 表示文言は持たない（言語中立）。GUI は自前の resx で整形し、スクリプト側で示す場合は英語で書く。
/// いずれも「実行を止めるほどの確実性は無いが、静かに壊れうる」ものを利用者へ伝えるための材料。
/// </remarks>
public enum SyncPlanWarningKind
{
    /// <summary>
    /// 参照先の主キーが変わる外部キーの参照先列が、同期後に候補キーでなくなりうる
    /// （逐次 DDL の方言では自動再作成が、テーブル再構築の方言では同期後の外部キー検査が実行時に失敗しうる）。
    /// </summary>
    /// <remarks>
    /// 判定は「同期後に被参照列が候補キーであることを証明できたか」で行う（同期後の主キー、または同期後に
    /// 存在する一意制約と完全に一致するか）。証明できない構成でも一意インデックス等で実際には通ることがあり、
    /// 断定はできないため実行ブロックではなく警告に留める。
    /// </remarks>
    ForeignKeyRebuildMayLoseCandidateKey,

    /// <summary>
    /// 削除しようとしている一意制約が、既存の外部キーの被参照列そのものである
    /// （＝候補キーが失われ、外部キーが壊れる・削除が実行時に拒否されうる）。
    /// </summary>
    /// <remarks>
    /// 主キーが同じ列を覆っていれば候補キーは保たれるが、その判定には同期後の主キー構成が要る。
    /// 過剰警告を許容して実行はブロックしない（<see cref="ForeignKeyRebuildMayLoseCandidateKey"/> と同じ方針）。
    /// </remarks>
    UniqueConstraintDropMayBreakForeignKey,

    /// <summary>
    /// テーブル再構築で、意味モデルが持たない属性（列の属性 <c>AUTOINCREMENT</c> / <c>DEFAULT</c> /
    /// <c>CHECK</c> / <c>COLLATE</c> / 生成列、制約の句 <c>ON CONFLICT</c> / <c>DEFERRABLE INITIALLY DEFERRED</c>、
    /// テーブルオプション <c>WITHOUT ROWID</c> / <c>STRICT</c>）が失われる。
    /// </summary>
    /// <remarks>
    /// 再構築の <c>CREATE TABLE</c> はモデルから組み立て直すため、モデルに無い属性は再現されない
    /// （インデックス・トリガーは <see cref="SchemaAuxiliaryObject"/> が CREATE 文全文で温存しているのと対照的）。
    /// 検出は live の <c>CREATE TABLE</c> 文に対する文字列検査（<see cref="TableRebuildAttributeDetector"/>）で、
    /// 断定はできないため実行ブロックではなく警告に留める（他 2 種と同じ方針）。
    /// </remarks>
    TableRebuildDropsColumnAttribute,

    /// <summary>
    /// 列定義変更で外部キーの両端の列の型が同期後に一致しなくなるため、その外部キーを外し直さない
    /// （＝列定義変更・主キーの解除が依存エラーで失敗する）。
    /// </summary>
    /// <remarks>
    /// 外部キーの両端の列の型変更を拒否する方言（SQL Server・MySQL）では、外部キーを外して列を変え、付け直す。
    /// 主キーの変更でも参照する外部キーを外して付け直す。ところが両端の型が食い違うと付け直しが必ず失敗し、DDL が
    /// 暗黙コミットされる方言では外部キーが消えたまま残る。そのため外さずに列定義変更・主キーの解除の時点で失敗させ
    /// （データも定義も変わらない）、両端の列を揃えるよう知らせる。食い違う外部キーが 1 本でもあれば、列の変更が必ず
    /// 失敗するので、ほかの外部キーも外し直さない。
    /// 選んだ外部キーの追加・作り直しが同期後に両端の型の食い違う外部キーになる場合も、同じ理由で計画から外して名指しする
    /// （作り直しは削除ごと外すので、外部キーは live の定義のまま残る。逐次 DDL の方言のみ）。
    /// 比較は <see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>（差分の型比較と同じ規則に加え、
    /// MySQL では同じ文字列型の長さだけの違いを許す）。
    /// </remarks>
    ForeignKeyColumnTypeMismatch,

    /// <summary>
    /// 選んだ項目が、選んでいない変更（表・列の追加、主キーの変更）に依存するため、その項目を計画から外した。
    /// </summary>
    /// <remarks>
    /// 例: 選んでいない表・列の追加を前提にする外部キー・一意制約・主キー・説明の追加、選んでいない主キーの変更で
    /// 主キーから外れるはずの列を NULL 許容にする列の変更。そのまま実行すると、存在しない表・列を参照して失敗するか、
    /// 方言によっては黙って何もしない（SQLite の再構築）ので、外して名指しする。依存先を自動では選ばない
    /// （利用者が選ばなかった変更を入れることになるため）。
    /// </remarks>
    SelectedItemRequiresUnselectedItem,

    /// <summary>
    /// 選んだ外部キーの追加・作り直しの参照先の列が、同期後の主キーにも一意制約にもちょうど一致しない（列の集合で比べる）。
    /// </summary>
    /// <remarks>
    /// 主キーの変更に伴う暗黙の付け直し（<see cref="ForeignKeyRebuildMayLoseCandidateKey"/>）と同じ証明を、利用者が選んだ
    /// 外部キーの追加・作り直し（作り直しは展開後の追加）に掛ける。証明できなくても取り込んでいない一意索引で通る
    /// 構成があるので、実行は止めずに警告に留める（追加は失敗しやすく、MySQL / Oracle の作り直しは外部キーが外れたまま
    /// 残り得る）。
    /// </remarks>
    ForeignKeyNotOnCandidateKey,

    /// <summary>
    /// 選んだ外部キーの追加・作り直しが参照アクション <c>SET DEFAULT</c> を使い、子の列に NOT NULL の列がある（SQL Server）。
    /// </summary>
    /// <remarks>
    /// SQL Server は NOT NULL の子の列に DEFAULT 制約が無いと、この外部キーを作れない（エラー 1762）。図は列の既定値を
    /// 持たないので、DB 側の列に DEFAULT 制約があるかは分からない。あれば通るので、実行は止めずに警告に留める
    /// （<see cref="SyncDialectCapabilities.SetDefaultRequiresColumnDefault"/>）。
    /// </remarks>
    SetDefaultOnNotNullColumn,

    /// <summary>
    /// 参照アクション <c>SET NULL</c> の外部キーの子の列が、同期後に NOT NULL になる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は、選んだ外部キーの追加・作り直し（子の列の同期後の NULL 許容で見る）と、選んだ列の変更（主キーの変更に伴う暗黙の
    /// NOT NULL 化を含む）で子の列が NOT NULL になる DB の外部キー。
    /// </para>
    /// <para>
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>true</c> の方言（SQL Server・MySQL）は
    /// この外部キーを作れないので、選んだ追加・作り直しを計画から外し（作り直しは削除ごと）、DB にある外部キーは外し直さない。
    /// <c>false</c> の方言は作れるが、参照される親の行の削除・キーの更新が失敗するので、警告だけを出す。
    /// </para>
    /// </remarks>
    SetNullOnNotNullColumn,

    /// <summary>選んだ項目が、ドットを含む名前の表に触れる（SQLite）。</summary>
    /// <remarks>
    /// SQLite は <c>"a"."b"</c> を「添付したデータベース a の表 b」と読むので、a を添付していない限り失敗する
    /// （<see cref="SyncDialectCapabilities.SupportsSchemaQualifiedTableNames"/>）。添付していれば通るので、実行は止めずに
    /// 警告に留める。表ごとに 1 件。
    /// </remarks>
    SchemaQualifiedTableName,

    /// <summary>選んだ列の変更が、既定値のある列の型の種類を変える（SQLite 以外の 4 方言）。</summary>
    /// <remarks>
    /// 同期は DB の既定値を保つので、既定値が新しい型に合わないと、同期そのものか既定値を使う行の追加が失敗する
    /// （<see cref="SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange"/>）。合うかは図からは分からないので、
    /// 実行は止めずに警告に留める。
    /// </remarks>
    DefaultOnColumnTypeKindChange,

    /// <summary>選んだ列の削除で構成列が消える一意制約を、削除を選んでいなくても外した。</summary>
    /// <remarks>
    /// 構成列が消える一意制約は同期後に残れない。外さないと、方言によって列の削除が断られるか、一意制約が黙って消える・
    /// 残りの列の制約へ縮む。外したことを名指しするのは、残りの列で作り直すかを利用者が決められるようにするため
    /// （外部キーの消える端は参照する列が消えれば意味を失うので、外すだけで名指ししない）。
    /// </remarks>
    UniqueConstraintDroppedWithColumn,

    /// <summary>選んだ説明の項目が、この方言が保存できない文字を書く（MySQL）。</summary>
    /// <remarks>
    /// MySQL は説明の BMP の外の文字（絵文字など）を断らずに 1 文字ずつ <c>?</c> へ置き換えて保存する
    /// （<see cref="QuickER.Dialects.IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>）。差分は置き換えた形で比べるので、
    /// 同期した後に差分は出ないが、DB の説明は図と違う文字になる。表・列ごとに 1 件。
    /// </remarks>
    DescriptionCharactersReplaced,

    /// <summary>
    /// 選んだ主キーの変更の新しい主キーと同じ列集合の一意制約を、削除を選んでいなくても外した（主キーと同じ列集合の一意制約を
    /// 持てない方言＝<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/> が偽の逐次 DDL の方言＝Oracle）。
    /// </summary>
    /// <remarks>
    /// Oracle は同じ列に主キーと一意制約を両方は持てず、主キーの付与が ORA-02261 で断られる。差分は図の主キーと同じ列集合の
    /// 一意制約を比較から外すので、利用者は一意制約の削除を選べない。値の一意性は主キーが保ち、図からの DDL もその一意制約を省くので、
    /// 外して名指しする（付け直さない）。その一意制約を参照する外部キーは外して、新しい主キーへ付け直す。
    /// </remarks>
    UniqueConstraintReplacedByPrimaryKey,
}

/// <summary>
/// 実行計画に付随する警告 1 件（言語中立）。
/// </summary>
/// <param name="Kind">警告種別</param>
/// <param name="TableName">
/// 対象テーブル。<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/> では
/// 外部キーを保有する子テーブル、<see cref="SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey"/>
/// では一意制約を削除するテーブル（＝外部キーの参照先）、<see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/>
/// では外部キーを保有する子テーブル、<see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> では
/// 計画から外した項目のテーブル、<see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> と
/// <see cref="SyncPlanWarningKind.SetDefaultOnNotNullColumn"/> と <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> では外部キーを保有する子テーブル、<see cref="SyncPlanWarningKind.SchemaQualifiedTableName"/> ではドットを含む表名（Detail は空）、
/// <see cref="SyncPlanWarningKind.DefaultOnColumnTypeKindChange"/> では列のテーブル（Detail は「列名 (旧の型 → 新しい型)」）、
/// <see cref="SyncPlanWarningKind.UniqueConstraintDroppedWithColumn"/> と <see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/> では一意制約のテーブル（Detail は「制約名 (構成列, …)」）、
/// <see cref="SyncPlanWarningKind.DescriptionCharactersReplaced"/> では説明のテーブル（Detail は「表」または「表.列」）。
/// </param>
/// <param name="Detail">
/// 補足（外部キー制約名など。無ければ空文字）。
/// <see cref="SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey"/> では壊れうる外部キーの制約名。
/// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> では外し直さない外部キー、または計画から外した追加・作り直しの外部キーの制約名。
/// <see cref="SyncPlanWarningKind.TableRebuildDropsColumnAttribute"/> では失われる属性の SQL キーワードを
/// カンマ区切りで並べたもの（<c>AUTOINCREMENT, DEFAULT</c> 等）。SQL の綴りそのものなので言語中立に扱える。
/// <see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> では「外した項目の対象 (足りないもの)」
/// （例 <c>FK_orders_customer (customer)</c>・<c>PRIMARY KEY (orders.code)</c>・<c>code (PRIMARY KEY)</c>）。
/// <see cref="SyncPlanWarningKind.ForeignKeyNotOnCandidateKey"/> では外部キーの制約名。
/// <see cref="SyncPlanWarningKind.SetDefaultOnNotNullColumn"/> では「外部キーの制約名 (NOT NULL の子の列, …)」。
/// <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> では「外部キーの制約名 (同期後に NOT NULL の子の列, …)」。
/// 対象は制約名・列名か <c>PRIMARY KEY</c>、足りないものは表名・<c>表.列</c>か <c>PRIMARY KEY</c>（識別子と SQL の綴り）。
/// </param>
public sealed record SyncPlanWarning(
    SyncPlanWarningKind Kind,
    string TableName,
    string Detail = ""
);

/// <summary>
/// 差分項目から組み立てた方言中立の実行計画。
/// </summary>
/// <remarks>
/// <see cref="SyncPlanner.BuildPlan"/> が選択済み項目を種別ごとにグループ化し、依存関係を満たす
/// 固定順序でセクションを並べたもの。方言別レンダラーはこの計画を SQL へ変換するだけでよい。
/// </remarks>
public sealed class SyncPlan
{
    /// <summary>実行順に並んだセクション一覧（空セクションは含まない）。</summary>
    public IReadOnlyList<SyncPlanSection> Sections { get; init; } = [];

    /// <summary>
    /// 逐次 DDL で表現できない変更をテーブル再構築でまとめた計画（rebuild 方言のみ・他方言は空）。
    /// </summary>
    public IReadOnlyList<TableRebuildPlan> Rebuilds { get; init; } = [];

    /// <summary>
    /// 列順をネイティブ DDL（<c>ALTER TABLE ... MODIFY ... AFTER</c>）で並べ替える計画
    /// （Native 方言＝MySQL のみ・他方言は空。rebuild 方言では再構築へ畳むためここは常に空）。
    /// </summary>
    public IReadOnlyList<TableReorderPlan> Reorders { get; init; } = [];

    /// <summary>
    /// この計画を実行する前に利用者へ伝えるべき警告（言語中立・SQL 生成には影響しない）。
    /// </summary>
    /// <remarks>
    /// 警告があっても計画自体は実行可能な形で成立している（危険な部分は除外済み、または実行時に失敗しうると
    /// 分かっているだけ）。表示層は実行確認の文言へ織り込む。
    /// </remarks>
    public IReadOnlyList<SyncPlanWarning> Warnings { get; init; } = [];

    /// <summary>
    /// 利用者が選んだのに、計画が外した項目（選んだ元の項目そのもの）。外した理由はそれぞれ警告（<see cref="Warnings"/>）で名指しする。
    /// </summary>
    /// <remarks>
    /// 外す理由は、選んでいない変更を前提にする（<see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/>）・
    /// 同期後に両端の型が食い違う外部キーの追加と作り直し（<see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/>）・
    /// 作れない方言の、子の列が NOT NULL になる SET NULL の外部キーの追加と作り直し
    /// （<see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/>）。型の変更も含む列の変更で NULL 許容化だけを外した項目も
    /// ここに入る（型の変更は計画に残る）。警告と一覧は同じ箇所で必ず一緒に積む。
    /// </remarks>
    public IReadOnlyList<SchemaDiffItem> ExcludedItems { get; init; } = [];

    /// <summary>生成対象のセクション・再構築・並べ替えが 1 件も無いか。</summary>
    public bool IsEmpty => Sections.Count == 0 && Rebuilds.Count == 0 && Reorders.Count == 0;
}

/// <summary>
/// テーブル再構築（新テーブル作成 → データ移送 → 入替）1 件分の実行計画。
/// </summary>
/// <remarks>
/// <para>
/// SQLite は <c>ALTER COLUMN</c> / <c>ADD・DROP CONSTRAINT</c> を持たないため、列型変更・列削除・FK 変更は
/// テーブル再構築で実現する。再構築後のスキーマ（<see cref="NewDefinition"/>）は「DB 現状（live）＋選択された差分のみ」
/// を合成したもので、未選択の変更は決して混入させない（<see cref="SyncPlanner"/> が合成する）。
/// </para>
/// <para>
/// <see cref="CreateOnly"/> が <c>true</c> のときは新規テーブル作成のみで、データ移送・旧テーブルの
/// DROP / RENAME を行わない（<see cref="CopyColumns"/> / <see cref="AuxiliaryObjects"/> は空）。
/// </para>
/// </remarks>
public sealed class TableRebuildPlan
{
    /// <summary>再構築対象のテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>再構築後のテーブル定義（合成済み・深いコピー）。</summary>
    public Entity NewDefinition { get; init; } = new();

    /// <summary>再構築後に張る外部キー仕様（解決済み・<c>CREATE TABLE</c> 内へインライン出力する）。</summary>
    public IReadOnlyList<TableRebuildForeignKey> ForeignKeys { get; init; } = [];

    /// <summary>新規テーブル作成のみか（データ移送・DROP / RENAME を行わない）。</summary>
    public bool CreateOnly { get; init; }

    /// <summary>
    /// データ移送（<c>INSERT ... SELECT</c>）の対象列名（live と <see cref="NewDefinition"/> の両方に存在する列）。
    /// </summary>
    public IReadOnlyList<string> CopyColumns { get; init; } = [];

    /// <summary>再構築後に再現する補助オブジェクト（インデックス・トリガー）。</summary>
    /// <remarks>
    /// 一意制約は意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本のため、
    /// <see cref="NewDefinition"/> 側で合成済み＝ここには含まれない。
    /// </remarks>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>この再構築の由来となった差分項目（UI の確認表示用）。</summary>
    public IReadOnlyList<SchemaDiffItem> SourceItems { get; init; } = [];
}

/// <summary>
/// ネイティブ列順変更（<c>ALTER TABLE ... MODIFY ... AFTER</c>）1 テーブル分の実行計画。
/// </summary>
/// <remarks>
/// 逐次 DDL で列を並べ替えられる方言（MySQL）向け。<see cref="Moves"/> は「最小移動」に刈り込まれており
/// （最長増加部分列を不動とし、それ以外の列だけを移動する）、各移動は目標順で前から確定する
/// （<see cref="SyncPlanner"/> が合成する）。
/// </remarks>
public sealed class TableReorderPlan
{
    /// <summary>並べ替え対象のテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>実行順に並んだ列移動一覧（各列を <see cref="ColumnMove.AfterColumn"/> の直後へ動かす）。</summary>
    public IReadOnlyList<ColumnMove> Moves { get; init; } = [];

    /// <summary>この並べ替えの由来となった差分項目（UI の確認表示用）。</summary>
    public IReadOnlyList<SchemaDiffItem> SourceItems { get; init; } = [];
}

/// <summary>
/// ネイティブ列順変更での 1 列分の移動。
/// </summary>
/// <param name="Column">
/// 移動する列の完全定義（合成済み）。選択済み AlterColumn / AddColumn があればその新定義、無ければ live 定義。
/// </param>
/// <param name="AfterColumn">
/// この列を直後へ置く列名。<c>null</c> のときは先頭（<c>FIRST</c>）へ移動する。
/// </param>
public sealed record ColumnMove(Column Column, string? AfterColumn);

/// <summary>
/// テーブル再構築の <c>CREATE TABLE</c> にインライン出力する、解決済みの外部キー 1 件。
/// </summary>
/// <param name="ConstraintName">FK 制約名</param>
/// <param name="ChildColumns">子テーブル側の外部キー列名（宣言順。複合外部キーは 2 件以上）</param>
/// <param name="ParentTable">参照先（親）テーブル名</param>
/// <param name="ParentColumns">参照先（親）の被参照列名（宣言順。子側と同数・同順）</param>
/// <param name="OnDelete">親行削除時の参照アクション</param>
/// <param name="OnUpdate">親キー更新時の参照アクション</param>
public sealed record TableRebuildForeignKey(
    string ConstraintName,
    IReadOnlyList<string> ChildColumns,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
);
