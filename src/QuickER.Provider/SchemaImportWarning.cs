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
    /// 参照先テーブルが図に存在しない以上リレーションを作れないため除外する。従来は無告知だった。
    /// <c>Subject</c> = 制約名 / <c>Detail</c> = 参照先（<c>スキーマ.テーブル</c> または スキーマ名）。
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
