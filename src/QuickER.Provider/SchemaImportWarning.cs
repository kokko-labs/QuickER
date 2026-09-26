using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// スキーマ取込で「意味モデルへ宣言どおりには写し取れなかった」種類。
/// </summary>
/// <remarks>
/// <para>
/// 表示文言は持たない（言語中立）。GUI（DB 取込の完了通知）と CLI（<c>scaffold</c> の stderr）が
/// それぞれ自前の resx で整形する＝<see cref="SyncPlanWarningKind"/> と同じ流儀。
/// </para>
/// <para>
/// ここに載るのは「取込自体は成功しているが、DB の宣言と図の間に説明できる差が生まれた」ものだけで、
/// 取込を中断させるものではない。逆に、差が生まれないもの（正規化して同じ意味の別表記にした等）は載せない
/// ＝毎回出る警告は形骸化するため。
/// </para>
/// </remarks>
public enum SchemaImportWarningKind
{
    /// <summary>
    /// ドメイン型（PostgreSQL の <c>CREATE DOMAIN</c>）の列を、その基底型として取り込んだ。
    /// </summary>
    /// <remarks>
    /// 意味モデルはドメイン型を持たないため基底型へ平坦化する（従来からの挙動）。
    /// この図から DDL を生成するとドメインではなく基底型の列になり、ドメインの CHECK 制約は失われる。
    /// <c>Subject</c> = 列名 / <c>Detail</c> = ドメイン型名。
    /// </remarks>
    DomainTypeFlattened,

    /// <summary>
    /// テーブルは見えているのに列を 1 つも取得できなかった。
    /// </summary>
    /// <remarks>
    /// 列ゼロのエンティティは DDL を生成できないため、黙って取り込むと後段で分かりにくく失敗する。
    /// <c>Subject</c> / <c>Detail</c> ともに空。
    /// </remarks>
    TableColumnsUnavailable,

    /// <summary>
    /// 取込範囲外のスキーマ（データベース）を参照する外部キーを、リレーションとして取り込まなかった。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参照先テーブルが図に存在しない以上リレーションを作れないため除外する。従来は無告知だった。
    /// <c>Subject</c> = 制約名 / <c>Detail</c> = 参照先（<c>スキーマ.テーブル</c> または スキーマ名）。
    /// </para>
    /// <para>
    /// SQLite の適用形: SQLite は取込範囲が単一 DB のため他スキーマという概念自体が無く、この警告は
    /// 「取込時に参照先テーブルが実在しなかった」ことを指す（<c>CREATE TABLE</c> 時に参照先の実在を
    /// 検査しないため、存在しないテーブルを参照する定義・参照先を後から DROP した定義が起こり得る）。
    /// </para>
    /// </remarks>
    ForeignKeyOutsideScope,

    /// <summary>
    /// 大文字小文字だけが違う同名テーブルが同じ取込範囲にあったため、後から来たほうを取り込まなかった。
    /// </summary>
    /// <remarks>
    /// 取込のテーブル辞書は大文字小文字を区別しない（SQL Server の既定照合順序に合わせた 5 方言共通の既定）。
    /// PostgreSQL / Oracle は引用識別子で <c>"Dup"</c> と <c>dup</c> を共存させられるため、黙って上書きすると
    /// 1 エンティティへ潰れて両テーブルの列が混ざる。混ぜるより落とすほうが安全なので、後着を名指しで捨てる。
    /// <c>Subject</c> = 取り込まなかったテーブル名 / <c>Detail</c> = 取り込んだほうのテーブル名。
    /// </remarks>
    TableNameCollision,

    /// <summary>
    /// 列の型表記が DDL・同期スクリプトへ出せない形だった。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 型は識別子でも文字列リテラルでもないため <see cref="SqlTypeText"/> が構造的ホワイトリストで絞っており、
    /// そこを通らない表記は DDL 生成・同期生成の入口が<b>図全体</b>を止める。取込はその表記を持ち帰れてしまう
    /// （SQLite の宣言型は任意のテキスト・PostgreSQL の引用が要る型名は <c>"od;d"</c> のように返る）ので、
    /// 黙って図へ入れると「後で DDL を叩いた瞬間に、関係ない全テーブルごと失敗する」形で遅れて露見する。
    /// </para>
    /// <para>
    /// 取込完了時に名指しするのは、その場が「一番早く分かる場所」だから。取込自体は成功させる
    /// （型を書き換えれば使える図なので、取り込めないことにするほうが損失が大きい）。
    /// <c>Subject</c> = 列名 / <c>Detail</c> = その型表記。
    /// </remarks>
    ColumnTypeNotEmittable,

    /// <summary>
    /// パーティション親テーブルを、パーティション定義を持たない通常テーブルとして取り込んだ。
    /// </summary>
    /// <remarks>
    /// 意味モデルはパーティションを持たないため、この図から生成した DDL は<b>パーティションされていない</b>
    /// テーブルを作る（<see cref="DomainTypeFlattened"/> と同じ「基底の形へ落として取り込む」告知）。
    /// <c>Subject</c> / <c>Detail</c> ともに空。
    /// </remarks>
    PartitionDefinitionLost,

    /// <summary>
    /// 仮想テーブル（SQLite の FTS5 / R*Tree 等）を、付属表（シャドウテーブル）ともに取り込まなかった。
    /// </summary>
    /// <remarks>
    /// 仮想テーブルは通常テーブルの形で写し取れないため取り込まない。付属表（例: FTS5 の
    /// <c>{テーブル}_data</c> / <c>{テーブル}_idx</c>）も黙って一緒に除外し、この警告 1 件が代表する
    /// （付属表ごとに警告を出すと本体の仮想テーブル 1 つに対して警告が何件も並んでしまうため）。
    /// <c>Subject</c> / <c>Detail</c> ともに空。
    /// </remarks>
    VirtualTableExcluded,

    /// <summary>
    /// 計算列・生成列を取り込んだが、その式または生成規則は意味モデルに載らなかった。
    /// </summary>
    /// <remarks>
    /// 意味モデルは式を持たないため、列自体は普通の列として取り込み
    /// 「書き込みを受け付けない」という事実だけを <see cref="Column.IsComputed"/> で運ぶ
    /// （<see cref="DomainTypeFlattened"/> と同じ「基底の形へ落として取り込む」告知）。
    /// 帰結として、この図から生成する DDL はその列を<b>普通の列</b>として作り直し、
    /// 生成コードはその列を INSERT / UPDATE の対象から外す。
    /// <c>Subject</c> = 列名 / <c>Detail</c> = 式または生成規則（<c>GENERATED ALWAYS AS ROW START</c> 等。
    /// 取得できない方言・生成規則では空）。
    /// </remarks>
    ComputedColumnExpressionLost,

    /// <summary>
    /// 無効化された制約（PRIMARY KEY / UNIQUE / FOREIGN KEY）を、何も強制しないため取り込まなかった。
    /// </summary>
    /// <remarks>
    /// <para>
    /// DISABLE された制約（Oracle の <c>DISABLE</c> 句）・<c>NOCHECK</c> で無効化された外部キー
    /// （SQL Server の <c>ALTER TABLE ... NOCHECK CONSTRAINT</c>）は一意性も参照整合性も実際には
    /// 強制しないため、図へ取り込むと「DB には無い保証」を宣言してしまう。ENABLE 済み（有効）の
    /// 制約だけが実在する制約として取り込まれる。
    /// </para>
    /// <para>
    /// SQL Server の外部キーは「無効化されているか（<c>is_disabled</c>）」だけを見る。
    /// <c>WITH NOCHECK</c> で追加されたが現在は有効な外部キー（<c>is_not_trusted = 1</c> かつ
    /// <c>is_disabled = 0</c>）は、以後の書き込みには強制が効くため取り込む（この場合は告げない）。
    /// SQL Server は PRIMARY KEY / UNIQUE 制約を無効化する構文を持たないため、この種別は
    /// 外部キーのみが対象になる。
    /// </para>
    /// <c>Subject</c> = 制約名 / <c>Detail</c> = 制約の種類（<c>"PRIMARY KEY"</c> / <c>"UNIQUE"</c> /
    /// <c>"FOREIGN KEY"</c>）。
    /// </remarks>
    DisabledConstraintExcluded,

    /// <summary>
    /// テンポラルテーブル（システムバージョニング）の履歴表を取り込まなかった。
    /// </summary>
    /// <remarks>
    /// 履歴表（SQL Server の <c>SYSTEM_VERSIONING</c> が自動生成・管理する変更履歴の保管先）は
    /// 本表の付属オブジェクトであり、それ自体が独立したエンティティではない。本表（現在値・
    /// <c>temporal_type = 2</c>）は通常のテーブルとして取り込む（その期間列は
    /// <see cref="ComputedColumnExpressionLost"/> で別途告げる）。
    /// <c>Subject</c> は空 / <c>Detail</c> = 本表名。
    /// </remarks>
    TemporalHistoryTableExcluded,

    /// <summary>
    /// SQLite の rowid 表で、非 rowid-alias の主キー列を NOT NULL へ補正した。
    /// </summary>
    /// <remarks>
    /// SQLite の rowid 表は、単一列かつ宣言型がちょうど <c>INTEGER</c>（rowid 別名＝暗黙の自動採番）の
    /// 主キー以外、互換性のため主キー列へ NULL を許す（<c>PRIMARY KEY</c> は ANSI SQL と違い
    /// <c>NOT NULL</c> を含意しない）。意味モデル・GUI は主キーを常に NOT NULL として扱うため、
    /// 取込はこの列を NOT NULL へ補正する（補正自体は変えない・この警告は告知のみ）。
    /// 既存行に NULL の主キーがあると、この図からの同期（テーブル再構築）でデータ移送が失敗し得る。
    /// <c>Subject</c> = 列名 / <c>Detail</c> は空。
    /// </remarks>
    PrimaryKeyNullabilityAdjusted,
}

/// <summary>
/// 取込で意味モデルへ宣言どおりには写し取れなかった箇所の構造化警告（取込自体は成功している）。
/// </summary>
/// <remarks>
/// <see cref="SyncPlanWarning"/> と同じ「言語中立 record ＋ 表示側で resx 文言化」の形。
/// <see cref="Subject"/> / <see cref="Detail"/> の意味は <see cref="Kind"/> ごとに決まる
/// （各 <see cref="SchemaImportWarningKind"/> の XmlDoc が正本）。
/// </remarks>
/// <param name="Kind">警告の種類</param>
/// <param name="TableName">対象テーブル名</param>
/// <param name="Subject">種類ごとの対象（列名・制約名など）</param>
/// <param name="Detail">種類ごとの補足（ドメイン型名・参照先など）</param>
public sealed record SchemaImportWarning(
    SchemaImportWarningKind Kind,
    string TableName,
    string Subject = "",
    string Detail = ""
);

/// <summary>5 方言の取込が共通して行う検査（方言に依らない警告の作り手）</summary>
public static class SchemaImportWarnings
{
    /// <summary>
    /// DDL・同期スクリプトへ出せない型表記を持つ列を、方言に依らず洗い出す。
    /// </summary>
    /// <remarks>
    /// 5 方言すべてが対象。SQLite の宣言型は任意のテキストがそのまま返り、PostgreSQL は引用が要る型名を
    /// 引用込みで返し、SQL Server / Oracle は別名型・オブジェクト型の名前を実名で返す。どの方言でも
    /// <see cref="SqlTypeText.IsSafe"/> を通らない表記を持ち帰り得るため、取込の最後に一律で掛ける
    /// （取込ごとに実装するとどれか 1 方言で漏れ、その方言だけ遅れて露見する）。
    /// </remarks>
    public static IEnumerable<SchemaImportWarning> DetectUnemittableColumnTypes(
        IEnumerable<Entity> entities
    ) =>
        entities.SelectMany(entity =>
            entity
                .Columns.Where(column => !SqlTypeText.IsSafe(column.DataType))
                .Select(column => new SchemaImportWarning(
                    SchemaImportWarningKind.ColumnTypeNotEmittable,
                    entity.TableName,
                    column.Name,
                    // 型表記は警告文へそのまま載るため、行構造を壊さないよう制御文字を畳んでおく
                    SqlComment.Sanitize(column.DataType)
                ))
        );
}
