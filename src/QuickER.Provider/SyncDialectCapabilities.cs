using System.Text.RegularExpressions;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 列順同期（列の並び替え）を DB 方言がどう実現できるかを表す区分。
/// </summary>
public enum ColumnReorderMode
{
    /// <summary>列順の同期は非対応（差分検知時は案内表示のみ）。</summary>
    None,

    /// <summary><c>ALTER TABLE ... MODIFY ... AFTER</c> など、逐次 DDL で直接並び替えられる（MySQL）。</summary>
    Native,

    /// <summary>テーブル再構築（新テーブル作成 → データ移送 → 入替）が必要（SQLite）。</summary>
    Rebuild,
}

/// <summary>
/// DB 方言がスキーマ同期でどこまで表現できるかを宣言するケーパビリティ。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SyncPlanner"/> がこの宣言を参照し、逐次 DDL で表現できない変更
/// （SQLite のテーブル再構築・MySQL のネイティブ列順変更など）をセクションへ振り分ける。
/// </para>
/// </remarks>
public sealed partial record SyncDialectCapabilities
{
    /// <summary><c>ALTER COLUMN</c>（型 / NULL 制約の変更）を逐次 DDL で実行できるか。</summary>
    public bool SupportsAlterColumn { get; init; } = true;

    /// <summary>外部キーの <c>ADD</c> / <c>DROP CONSTRAINT</c> を逐次 DDL で実行できるか。</summary>
    public bool SupportsForeignKeyAlter { get; init; } = true;

    /// <summary>テーブル / 列コメント（説明）を設定する機構を持つか。</summary>
    public bool SupportsDescriptions { get; init; } = true;

    /// <summary>
    /// 列定義変更（<c>ALTER COLUMN</c>）の前に、その列が関与する外部キーを一旦外す必要があるか。
    /// </summary>
    /// <remarks>
    /// SQL Server は FK に参加している列の型を変更しようとすると依存エラー（Msg 5074）になり、MySQL も外部キーの
    /// 両端の列の型変更を拒否する（参照する列と参照される列の型が食い違う状態を一瞬も許さない）ため、両方言とも <c>true</c>。
    /// <c>true</c> の方言では <see cref="SyncPlanner"/> が該当 FK の DROP / ADD を自動で計画へ注入する
    /// （ユーザーが明示的に DROP を選択している FK は対象外＝二重削除も再作成もしない。同期後に両端の列の型が
    /// 一致しない FK も対象外＝付け直しが必ず失敗するため、外さずに列定義変更の時点で失敗させて警告する）。
    /// </remarks>
    public bool AlterColumnRequiresForeignKeyRebuild { get; init; }

    /// <summary>
    /// 列定義変更（<c>ALTER COLUMN</c>）の前に、その列を構成列に持つ主キー・一意制約を一旦外す必要があるか。
    /// </summary>
    /// <remarks>
    /// SQL Server は主キー・一意制約の構成列の型を変えようとすると依存エラー（Msg 5074）になるため <c>true</c>。
    /// <c>true</c> の方言では <see cref="SyncPlanner"/> が、構成列が変わる主キー・一意制約を同じ構成のまま
    /// 外して付け直す項目を計画へ注入する。MySQL は <c>MODIFY</c> で構成列の型を変えられるうえ、一意索引が
    /// 外部キーを支えていると外す段で失敗する（エラー 1553）ため <c>false</c>。
    /// </remarks>
    public bool AlterColumnRequiresKeyConstraintRebuild { get; init; }

    /// <summary>
    /// 外部キーが、子列を先頭に持つ索引を必要とするか（その索引を外す前に外部キーを一旦外す必要があるか）。
    /// </summary>
    /// <remarks>
    /// MySQL は外部キーの子列を先頭に持つ索引を必須とし、旧主キー・削除する一意制約の索引がその唯一の索引だと、
    /// <c>DROP PRIMARY KEY</c> / <c>DROP INDEX</c> がエラー 1553 になるため <c>true</c>。<see cref="SyncPlanner"/> は、
    /// 子列がそれらの索引の先頭に乗る外部キーを外して付け直す（付け直しのとき MySQL が必要な索引を自動で作る）。
    /// </remarks>
    public bool ForeignKeyRequiresLeadingIndex { get; init; }

    /// <summary>外部キー制約名が DB に永続化されるか（SQLite は制約名が合成名で永続化されない）。</summary>
    public bool PersistsForeignKeyConstraintNames { get; init; } = true;

    /// <summary>列順同期の実現方式。</summary>
    public ColumnReorderMode ColumnReorder { get; init; } = ColumnReorderMode.None;

    /// <summary>
    /// 型表記を「その表記で宣言した列を取り込むと返る表記」へ寄せる関数（既定は恒等）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// DB は宣言の綴りをそのまま保存するとは限らない（SQL Server の <c>numeric(18,0)</c> は <c>numeric(18)</c>、
    /// PostgreSQL の <c>timestamp</c> は <c>timestamp(6)</c> として返る）。図・型の選択肢・<c>TryFormat</c> の表記と
    /// 取込の表記をそのまま比べると、適用しても消えない <c>ALTER COLUMN</c> が出続けるため、差分の型比較
    /// （<see cref="AreSameType"/>）は両側をこの関数に通してから比べる。
    /// </para>
    /// <para>
    /// 実体は方言ごとの静的関数（<c>{方言}TypeNotation.Normalize</c>）で、取込側も同じ関数を通す＝ここは配線だけ。
    /// 契約は「取込の出力は不動点（正規化しても変わらない）」「冪等」の 2 つ。
    /// </para>
    /// </remarks>
    public Func<string, string> NormalizeTypeNotation { get; init; } = static type => type;

    /// <summary>外部キーの <c>ON UPDATE</c> 句を持つか（Oracle は持たない）。</summary>
    /// <remarks>
    /// <c>false</c> の方言では差分の比較で図側の <c>ON UPDATE</c> を <c>NO ACTION</c> へ畳む
    /// （<see cref="EffectiveOnUpdate"/>）。畳まないと、DB が表せない参照アクションの差分が同期しても消えない。
    /// </remarks>
    public bool SupportsOnUpdateAction { get; init; } = true;

    /// <summary>外部キーの参照アクション <c>SET DEFAULT</c> が効くか（Oracle は持たず、MySQL の InnoDB は受け付けても効かない）。</summary>
    /// <remarks>
    /// <c>false</c> の方言では差分の比較で <c>ON DELETE</c> / <c>ON UPDATE</c> の <c>SET DEFAULT</c> を <c>NO ACTION</c> へ畳む
    /// （<see cref="EffectiveOnDelete"/> / <see cref="EffectiveOnUpdate"/>）。MySQL は <c>SET DEFAULT</c> を宣言として保存するが、
    /// 親を消すと外部キーエラーになる＝実際の動きは <c>NO ACTION</c>（<c>RESTRICT</c>）と同じ。
    /// </remarks>
    public bool SupportsSetDefaultAction { get; init; } = true;

    /// <summary>計算列・生成列の型を、式を変えずに <c>ALTER COLUMN</c> で変えられるか（PostgreSQL だけ）。</summary>
    /// <remarks>
    /// <para>
    /// 意味モデルは計算列の式を持たないので、同期は式を書けない。PostgreSQL は <c>ALTER COLUMN ... TYPE</c> が生成列の
    /// 式を保ったまま型だけを変える。SQL Server（計算列は <c>ALTER COLUMN</c> できない）と MySQL（式を書かない
    /// <c>MODIFY</c> は生成列を通常の列へ変えようとして断られる）は失敗し、SQLite の再構築は式を落とす
    /// （Oracle も式を書かない変更はできない）。
    /// </para>
    /// <para>
    /// <c>false</c> の方言では、計算列の型の差分を選択できない案内項目にする（<see cref="SchemaDiffService"/>）。
    /// NULL 許容は方言に依らず差分に出さない（計算列の NULL 許容は式が決める性質）。
    /// </para>
    /// </remarks>
    public bool SupportsComputedColumnTypeChange { get; init; }

    /// <summary>主キーと同じ列集合の一意制約を作れるか（SQLite と Oracle は作れない）。</summary>
    /// <remarks>
    /// SQLite は主キーと同じ列集合の <c>UNIQUE</c> を冗長として索引を作らず、取り込んでも現れない（差分が永久に消えない）。
    /// Oracle は ORA-02261 で断る。<c>false</c> の方言では、図の主キーと同じ列集合の一意制約の追加を、
    /// 選択できない案内項目にする（<see cref="SchemaDiffService"/>）。主キーが一意性を保証するので、失うものは無い。
    /// </remarks>
    public bool SupportsUniqueConstraintOnPrimaryKey { get; init; } = true;

    /// <summary>
    /// 複合外部キーが参照する列の並びが、参照先の主キー・一意制約の列の並びと一致する必要があるか（SQL Server・MySQL）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL Server（Msg 1776）と MySQL（Missing unique key）は、参照する列の並びがキーの列の並びと違うと外部キーを作れない。
    /// PostgreSQL・Oracle・SQLite は列の集合が一致すれば並びを問わない（いずれもコンテナ・実ファイルで確認）。
    /// </para>
    /// <para>
    /// <c>false</c> の方言では、外部キーの列ペアの並びは意味を持たないので、差分は列ペアを順不同で比べる
    /// （<see cref="SchemaDiffService"/>）。Oracle のカタログは外部キーの列を参照先のキーの並びで記録し、宣言の並びを
    /// 残さないため、並びまで比べると取り込み直すたびに作り直しの差分が出た。
    /// </para>
    /// </remarks>
    public bool ReferencedKeyRequiresSameColumnOrder { get; init; } = true;

    /// <summary>外部キーの両端の文字列型が、長さだけ違っていてもよいか（MySQL）。</summary>
    /// <remarks>
    /// MySQL は外部キーの両端の文字列型（<c>char</c> / <c>varchar</c> / <c>binary</c> / <c>varbinary</c>）の長さの違いを許す
    /// （文字集合・照合順序は同じでなければならない）。<c>true</c> の方言では、同期後の外部キーの両端の型の照合
    /// （<see cref="AreForeignKeyCompatibleTypes"/>）が長さだけの違いを食い違いとみなさない。食い違いとみなすと
    /// 外部キーを外し直さず、片側の長さを変える列の変更が MySQL のエラー 1832 で失敗した。
    /// </remarks>
    public bool ForeignKeyAllowsStringLengthDifference { get; init; }

    /// <summary>同期スクリプトの DDL がトランザクションで巻き戻るか（MySQL・Oracle は DDL を暗黙にコミットする）。</summary>
    /// <remarks>
    /// <c>false</c> の方言では、途中の文が失敗すると前の文が適用されたまま残る。外部キーを外した後の付け直しが
    /// 失敗すると外部キーが消えたまま残るため、<see cref="SyncPlanner"/> は付け直しが必ず失敗する変更
    /// （<see cref="ReferencedKeyRequiresSameColumnOrder"/> の方言で、参照される複合キーの列の並びだけが合わなくなる変更）
    /// を計画から外して警告する。
    /// </remarks>
    public bool TransactionalDdl { get; init; } = true;

    /// <summary>この方言の識別子（テーブル名・列名・制約名）の長さの上限</summary>
    /// <remarks>
    /// 各方言の識別子クラスの <c>Limit</c>（値の正本は Model の <see cref="IdentifierLengthLimits"/>）を指す。
    /// 既定は上限なし（方言を指定しない差分の比較のため）。5 方言のプロバイダーがそれぞれの値を宣言していることは
    /// <c>IdentifierLengthLimitDeclarationTests</c> が固定する。
    /// </remarks>
    public IdentifierLengthLimit IdentifierLengthLimit { get; init; } =
        IdentifierLengthLimit.Unlimited;

    /// <summary>上限を超える識別子を断らずに黙って切り詰める方言か（PostgreSQL）</summary>
    /// <remarks>
    /// <c>true</c> の方言では、差分が名前の無いリレーションの外部キーを live と突き合わせるとき、live の名前が
    /// 「縮める前の既定名を <see cref="IdentifierLengthLimit.Truncate"/> で切ったもの」と一致すれば同じ外部キーとみなす。
    /// 旧版の QuickER は長い既定名をそのまま DDL へ書き、PostgreSQL が 63 バイトで切って保存していたため、
    /// 吸収しないと既定名を縮めるようにした版で同期すると、同じ外部キーが追加と削除の 2 項目に化ける
    /// （<c>PostgreSqlLegacyTruncatedNameIntegrationTests</c> が固定）。
    /// </remarks>
    public bool TruncatesLongIdentifiers { get; init; }

    /// <summary>2 つの型表記が、この方言の DB で同じ宣言として保存されるか</summary>
    /// <remarks>
    /// 両側を <see cref="NormalizeTypeNotation"/> に通したうえで <see cref="DbTypeText.AreEquivalent"/>
    /// （前後空白・大文字小文字を無視）で比べる。同期の <c>ALTER COLUMN</c> 判定と、外部キーの両端の型照合が使う。
    /// </remarks>
    public bool AreSameType(string? left, string? right) =>
        DbTypeText.AreEquivalent(
            NormalizeTypeNotation(left ?? string.Empty),
            NormalizeTypeNotation(right ?? string.Empty)
        );

    /// <summary>2 つの型表記が、この方言で外部キーの両端に並べられるか（同期後の外部キーの型の照合に使う）</summary>
    /// <remarks>
    /// <see cref="AreSameType"/> で同じ型なら並べられる。<see cref="ForeignKeyAllowsStringLengthDifference"/> が
    /// <c>true</c> の方言では、同じ文字列型で長さだけが違う型（<c>varchar(50)</c> と <c>varchar(100)</c>。括弧の後ろの
    /// 文字集合などの指定は同じであること）も並べられる。
    /// </remarks>
    public bool AreForeignKeyCompatibleTypes(string? left, string? right)
    {
        if (AreSameType(left, right))
        {
            return true;
        }

        if (!ForeignKeyAllowsStringLengthDifference)
        {
            return false;
        }

        var l = StringTypePattern().Match(NormalizeTypeNotation(left ?? string.Empty));
        var r = StringTypePattern().Match(NormalizeTypeNotation(right ?? string.Empty));

        return l.Success
            && r.Success
            && string.Equals(
                l.Groups["base"].Value,
                r.Groups["base"].Value,
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                l.Groups["rest"].Value.Trim(),
                r.Groups["rest"].Value.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    /// <summary>長さを持つ文字列型の表記（基本の型・長さ・括弧の後ろの指定）</summary>
    [GeneratedRegex(
        @"^\s*(?<base>char|varchar|binary|varbinary)\s*\(\s*\d+\s*\)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline
    )]
    private static partial Regex StringTypePattern();

    /// <summary>この方言の DB で実際に効く <c>ON DELETE</c> の参照アクション</summary>
    public ForeignKeyReferentialAction EffectiveOnDelete(ForeignKeyReferentialAction action) =>
        action == ForeignKeyReferentialAction.SetDefault && !SupportsSetDefaultAction
            ? ForeignKeyReferentialAction.NoAction
            : action;

    /// <summary>この方言の DB で実際に効く <c>ON UPDATE</c> の参照アクション</summary>
    public ForeignKeyReferentialAction EffectiveOnUpdate(ForeignKeyReferentialAction action) =>
        !SupportsOnUpdateAction
        || action == ForeignKeyReferentialAction.SetDefault && !SupportsSetDefaultAction
            ? ForeignKeyReferentialAction.NoAction
            : action;
}
