using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 図の列型文字列（<see cref="Column.DataType"/>）が DDL・同期スクリプトへ素通しで出せる安全な表記かを検証する。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Column.DataType"/> は GUI の自由入力・DB 取込・DBML/Excel 取込・MCP/AI 経由の任意文字列で、
/// DDL 生成（<see cref="DdlGeneratorBase"/>）と 5 方言の同期スクリプト生成は、この文字列を
/// <b>クォートもエスケープもせず</b> SQL へ補間する（型は識別子でも文字列リテラルでもないため包む場所が無い）。
/// そのため、ここで安全な表記だけに絞ることが唯一の防壁になる。
/// </para>
/// <para>
/// 判定は <b>構造的ホワイトリスト</b>で、文を終わらせる・文字列を開く・コメントを始める文字
/// （<c>;</c> <c>'</c> <c>"</c> <c>`</c> <c>\</c> <c>--</c> <c>/*</c> と改行・制御文字）を一切通さない。
/// 一方で「意味のある型か」は判定しない＝<see cref="ITypeCatalog.TryParse"/> による型解釈は従来どおり
/// fail-soft のまま（解析不能でも取り込む）にしておく。取込は 5 方言とも実 DB の表記を verbatim に近い形で
/// 持ち帰るため、既知キーワードの許可集合にすると <c>LONG RAW</c> / <c>SDO_GEOMETRY</c> /
/// <c>VARYING CHARACTER(255)</c> / 別名型の非 ASCII 名のような実在の型を弾いてしまい、
/// セキュリティ修正が実 DB でのデータ欠落へ化ける。
/// </para>
/// <para>
/// 許す形（大文字小文字は問わない・前後に空白や改行が付いた表記は<b>拒否する</b>＝検証した文字列と
/// 出力する文字列を一致させる）:
/// <list type="bullet">
///   <item>語（英字・各国語の文字で始まり、以降は文字・数字・<c>_</c> <c>$</c>）の空白区切りの並び。
///   例 <c>int</c> / <c>double precision</c> / <c>LONG RAW</c> / <c>TIMESTAMP WITH TIME ZONE</c>。
///   ただし 2 語目以降に列定義の句を導く語（<c>NOT</c> / <c>NULL</c> / <c>DEFAULT</c> など）は通さない
///   （型の直後は列定義の続きなので <c>int NOT NULL</c> がそのまま有効な DDL になり、図の宣言と食い違う）。
///   <c>#</c> を語の構成文字から外しているのは、MySQL では <c>#</c> が行末コメントの開始で、<c>int#</c> が
///   通ると <c>MODIFY COLUMN</c> の後続句（NOT NULL / COMMENT / AFTER）が黙って消えるため</item>
///   <item>各語の直後に置ける括弧引数。引数は<b>数値または語</b>（文字・数字・<c>_</c>）を 1 つか 2 つで、
///   さらに Oracle の単位語（<c>(10 BYTE)</c>）を後ろに置ける。例 <c>nvarchar(max)</c> /
///   <c>decimal(10,2)</c> / <c>TIMESTAMP(6) WITH TIME ZONE</c> / <c>INTERVAL DAY(2) TO SECOND(6)</c> /
///   <c>int(10) unsigned</c>。引数に語を許すのは <c>max</c> のためだけでなく、PostGIS の
///   <c>geometry(Point,4326)</c> / <c>geography(MultiPolygon)</c> のように<b>実在の型が語引数を取る</b>ため
///   （数値限定にすると、PostGIS を使う DB を取り込んだ図が<b>その 1 列のせいで丸ごと</b> DDL も同期も
///   生成できなくなる＝入口の検証は図全体を止める）。語には引用符・空白・記号を含めないので、
///   通す文字の集合は「語・数字・カンマ・<c>*</c>」のまま閉じている（<c>*</c> は Oracle の
///   <c>NUMBER(*,2)</c>＝「精度は最大・スケールは 2」のための 1 文字で、文を終わらせも文字列を開きもしない）</item>
///   <item>末尾の配列表記 <c>[]</c>（PostgreSQL）。多次元のため繰り返せる。例 <c>integer[]</c> /
///   <c>integer[][]</c></item>
///   <item>MySQL の値リスト型 <c>enum('a','b')</c> / <c>set('x','y')</c>。引用符はここだけ許し、
///   中身は「<c>'</c> と <c>\</c> と制御文字を含まない文字列（<c>''</c> による <c>'</c> の表現は可）」に限る
///   （MySQL の取込は <c>COLUMN_TYPE</c> を無加工で持ち帰るため、弾くと enum 列のある実 DB が同期できなくなる）</item>
/// </list>
/// </para>
/// <para>
/// 既知の割り切り: PostgreSQL の 1 バイト char 型 <c>"char"</c>（引用符込み）と、取込が型名を解決できなかった
/// ときのフォールバック <c>user-defined</c>（ハイフン）は通さない。どちらもそのまま DDL へ出しても
/// 構文エラーになる表記で、名指しで失敗するほうが壊れた DDL を投げるより早く分かる。
/// </para>
/// </remarks>
public static partial class SqlTypeText
{
    /// <summary>受け付ける型文字列の長さ上限（これを超える表記は実在しない）</summary>
    public const int MaxLength = 200;

    /// <summary>型文字列が DDL へ素通しで出せる安全な表記か</summary>
    /// <remarks>
    /// <para>
    /// 空・空白のみは「型が未設定」であって注入ではないため安全側（true）とする（従来どおり素通しする）。
    /// </para>
    /// <para>
    /// 一方で<b>前後に空白・改行が付いた表記は拒否する</b>。生成側は <see cref="Column.DataType"/> を
    /// そのまま補間するため、Trim 後の姿だけを見て通すと「検証した文字列」と「出力する文字列」が食い違う。
    /// 例えば <c>"\nGO\n"</c> は Trim 後は語 1 つの <c>GO</c> に見えるが、出力されると SQL Server の
    /// バッチ分割（<c>^\s*GO\s*$</c>）を誤作動させ得る。
    /// </para>
    /// </remarks>
    public static bool IsSafe(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return true;
        }

        // 検証した文字列と出力する文字列を一致させる（前後の空白・改行は呼び出し側で落としてもらう）
        if (dataType.Length > MaxLength || dataType != dataType.Trim())
        {
            return false;
        }

        if (ValueListTypePattern().IsMatch(dataType))
        {
            return true;
        }

        return SafeTypePattern().IsMatch(dataType) && !ContainsSmuggledColumnClause(dataType);
    }

    /// <summary>
    /// 2 語目以降に列定義の句（<c>NOT NULL</c> / <c>DEFAULT</c> 等）が現れるか。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 語の空白区切りの並びは <c>double precision</c> / <c>LONG RAW</c> / <c>TIMESTAMP WITH TIME ZONE</c> の
    /// ような実在の型のために許しているが、その隙間からは<b>句</b>を密輸できる。型は列定義の途中へ補間されるので
    /// <c>int NOT NULL</c> のような表記はそのまま有効な DDL になり、NULL 許容・既定値・照合順序・制約が
    /// 図の宣言と食い違ったまま通る（構文エラーにならないので気付けない）。
    /// </para>
    /// <para>
    /// 判定は「括弧の中を除いた語」単位で行う（<c>VARCHAR2(100 CHAR)</c> の単位語や <c>DECIMAL(10,2)</c> の
    /// 引数を語として数えない）。拒否語は 5 方言の型カタログ・取込が組み立てる実在表記のどれとも衝突しない
    /// ものだけに絞ってあり、全数の照合は <c>SqlTypeTextTests</c> が固定する。
    /// </para>
    /// </remarks>
    private static bool ContainsSmuggledColumnClause(string dataType)
    {
        var withoutArguments = ParenthesizedArgumentPattern().Replace(dataType, " ");
        var words = withoutArguments.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        return words.Skip(1).Any(word => ColumnClauseKeywords.Contains(word));
    }

    /// <summary>型表記の 2 語目以降には現れ得ない、列定義の句を導くキーワード</summary>
    private static readonly HashSet<string> ColumnClauseKeywords = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "NOT",
        "NULL",
        "DEFAULT",
        "PRIMARY",
        "KEY",
        "REFERENCES",
        "CHECK",
        "GENERATED",
        "IDENTITY",
        "COLLATE",
        "UNIQUE",
        "ON",
    };

    /// <summary>図の全列の型文字列を検証し、安全でない表記があれば名指しで例外を投げる（DDL 生成の入口）</summary>
    /// <exception cref="InvalidOperationException">安全でない型文字列を含む列がある場合</exception>
    public static void Validate(ErDiagram diagram) =>
        ThrowIfAnyUnsafe(
            diagram.Entities.SelectMany(entity =>
                entity.Columns.Select(column => (entity.TableName, column))
            )
        );

    /// <summary>実行計画に現れる全列の型文字列を検証し、安全でない表記があれば名指しで例外を投げる（同期生成の入口）</summary>
    /// <remarks>
    /// 列定義を出力しうる経路をすべて拾う＝セクション項目の <see cref="SchemaDiffItem.Entity"/> と
    /// <see cref="SchemaDiffItem.Column"/>、テーブル再構築の合成後定義、ネイティブ列順変更の移動列。
    /// </remarks>
    /// <exception cref="InvalidOperationException">安全でない型文字列を含む列がある場合</exception>
    public static void Validate(SyncPlan plan)
    {
        var columns = new List<(string TableName, Column Column)>();

        foreach (var item in plan.Sections.SelectMany(section => section.Items))
        {
            if (item.Entity is not null)
            {
                columns.AddRange(
                    item.Entity.Columns.Select(column => (item.TableName, Column: column))
                );
            }

            if (item.Column is not null)
            {
                columns.Add((item.TableName, item.Column));
            }
        }

        foreach (var rebuild in plan.Rebuilds)
        {
            columns.AddRange(
                rebuild.NewDefinition.Columns.Select(column => (rebuild.TableName, Column: column))
            );
        }

        foreach (var reorder in plan.Reorders)
        {
            columns.AddRange(
                reorder.Moves.Select(move => (reorder.TableName, Column: move.Column))
            );
        }

        ThrowIfAnyUnsafe(columns);
    }

    /// <summary>安全でない型文字列を持つ列を「テーブル.列 → 型文字列」の形で名指しして例外を投げる</summary>
    /// <remarks>
    /// 例外文は英語が正本（生成 SQL に乗る固定文と同じ流儀＝方言中立・カルチャ非依存）。型文字列そのものを
    /// 文面へ載せるが、制御文字はここでも空白へ畳んでからにする（例外文を表示する側の行構造を壊さないため）。
    /// </remarks>
    private static void ThrowIfAnyUnsafe(IEnumerable<(string TableName, Column Column)> columns)
    {
        var offenders = columns
            .Where(pair => !IsSafe(pair.Column.DataType))
            .Select(pair =>
                $"{pair.TableName}.{pair.Column.Name} = '{SqlComment.Sanitize(pair.Column.DataType)}'"
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (offenders.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "The following columns have a data type that cannot be written into SQL safely: "
                + string.Join(", ", offenders)
                + ". A data type may only contain words (letters, digits, _, $) separated by single spaces, "
                + "optional parenthesized arguments such as (max), (50) or (10,2), an optional trailing [], "
                + "or a MySQL value list such as enum('a','b'). Leading and trailing whitespace is not allowed, "
                + "and column clauses (NOT NULL, DEFAULT, COLLATE, ...) belong in the diagram, not in the data type. "
                + "Fix the data type in the diagram."
        );
    }

    // 語 ＋ 省略可能な括弧引数（数値 / 語＝max・PostGIS の Point 等・Oracle の BYTE / CHAR 単位語つき）の
    // 空白区切りの並び、末尾に省略可能な配列表記 []（多次元のため繰り返し可）。
    // 区切りは空白のみ（改行・タブは通さない＝1 行で書ける表記だけを許す）。
    [GeneratedRegex(
        @"^[\p{L}_][\p{L}\p{Nd}_$]*(?: ?\( ?(?:[+-]?\d{1,10}|\*|[\p{L}_][\p{L}\p{Nd}_]*)(?: ?, ?(?:[+-]?\d{1,10}|\*|[\p{L}_][\p{L}\p{Nd}_]*))?(?: +[\p{L}][\p{L}\p{Nd}_]*)? ?\))?(?: +[\p{L}_][\p{L}\p{Nd}_$]*(?: ?\( ?(?:[+-]?\d{1,10}|\*|[\p{L}_][\p{L}\p{Nd}_]*)(?: ?, ?(?:[+-]?\d{1,10}|\*|[\p{L}_][\p{L}\p{Nd}_]*))?(?: +[\p{L}][\p{L}\p{Nd}_]*)? ?\))?)*(?: ?\[ ?\])*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SafeTypePattern();

    // MySQL の値リスト型 enum('a','b') / set('x','y')。リテラルの中身は ' と \ と制御文字を含まない
    // （'' による ' の表現は許す）。取込が COLUMN_TYPE を無加工で持ち帰るため、この形だけ引用符を通す。
    [GeneratedRegex(
        @"^(?:enum|set) ?\( ?'(?:[^'\\\p{Cc}]|'')*'(?: ?, ?'(?:[^'\\\p{Cc}]|'')*')* ?\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ValueListTypePattern();

    // 語の直後に置かれた括弧引数（句の密輸判定で語として数えないために取り除く）。
    // 引数の中身は SafeTypePattern が既に検証済みなので、ここでは括弧の対応だけを見れば足りる。
    [GeneratedRegex(@"\([^()]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParenthesizedArgumentPattern();
}
