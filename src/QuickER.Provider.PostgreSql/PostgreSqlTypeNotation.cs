using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickER.Provider.PostgreSql;

/// <summary>
/// PostgreSQL の型表記を、QuickER の短縮語彙で「その表記で宣言した列を取り込むと返る表記」へ寄せる。
/// </summary>
/// <remarks>
/// <para>
/// 取込は <c>format_type()</c> の正準表記を情報源にし、表記としてはこの関数で短縮語彙へ寄せる
/// （<see cref="PostgreSqlSchemaImporter.NormalizeFormatType"/> はここへ委ねる）。正準表記を素通しすると、
/// 既存の図が持つ短縮表記（<c>varchar(50)</c>）と取り込んだ表記（<c>character varying(50)</c>）が食い違い、
/// しかも <c>ALTER TABLE ... TYPE character varying(50)</c> を実行しても <c>format_type()</c> の出力は
/// 変わらないので、その差分は<b>実行しても消えない</b>（実 PostgreSQL 16 で確認済み）。
/// 差分の型比較（<see cref="SyncDialectCapabilities.NormalizeTypeNotation"/>）も図の表記をここへ通すので、
/// 図・型の選択肢・<c>TryFormat</c> が書く短縮語彙（素の <c>timestamp</c>・<c>numeric(18,0)</c>）も
/// 取込の表記と同じ形へそろう。
/// </para>
/// <para>
/// 規則はいずれも「同じ型の別表記」への置換（意味は変わらない）:
/// <list type="bullet">
///   <item><c>character varying[(n)]</c> → <c>varchar[(n)]</c> / <c>character(n)</c> → <c>char(n)</c></item>
///   <item><c>timestamp[(n)] without time zone</c> と素の <c>timestamp[(n)]</c> → <c>timestamp(n)</c>（無修飾は既定精度 6）</item>
///   <item><c>timestamp[(n)] with time zone</c> と素の <c>timestamptz[(n)]</c> → <c>timestamptz(n)</c>（同上）</item>
///   <item><c>time[(n)] without time zone</c> と素の <c>time[(n)]</c> → <c>time(n)</c>（同上）。
///   <c>time with time zone</c> 側は QuickER の従来語彙がそのままなので触らない</item>
///   <item><c>numeric(p,0)</c> → <c>numeric(p)</c>（<c>format_type()</c> はスケール 0 も必ず書く）</item>
/// </list>
/// これ以外（<c>bit(8)</c> / <c>bit varying(16)</c> / <c>interval day to second(3)</c> /
/// <c>numeric(10,-2)</c> / ユーザー定義型）は前後の空白を落とすだけで素通しする。照合は大文字小文字を
/// 区別せず、当てはまった表記は小文字の短縮語彙で返す。
/// </para>
/// <para>
/// 配列は末尾の <c>[]</c> を外して要素型へ同じ規則を当て、付け直す（<c>character varying(20)[]</c> →
/// <c>varchar(20)[]</c>）。要素型だけ正準表記のまま残すと、同じ型に 2 通りの綴りが生まれるため。
/// </para>
/// <para>
/// 取込の表記がそのまま DDL として通り再取込で同じ表記へ戻る（不動点）ことは
/// <c>PostgreSqlDdlRoundTripIntegrationTests</c> と <c>PostgreSqlImportFidelityIntegrationTests</c> が、
/// 選択肢と <c>TryFormat</c> の全表記が取込と一致することは <c>PostgreSqlTypeNotationIntegrationTests</c> が
/// 実 DB で固定する。
/// </para>
/// </remarks>
public static partial class PostgreSqlTypeNotation
{
    /// <summary>PostgreSQL が小数秒に付ける既定精度（<c>timestamp</c> = <c>timestamp(6)</c>）</summary>
    private const int DefaultFractionalSecondsPrecision = 6;

    /// <summary>型表記を取込が返す短縮語彙の表記へ寄せる</summary>
    /// <param name="dataType">図・型の選択肢・<c>TryFormat</c>・<c>format_type()</c> のいずれかの型表記</param>
    /// <returns>取込が返す表記（当てはまる規則が無ければ前後の空白を除いただけの表記）</returns>
    public static string Normalize(string dataType)
    {
        var text = dataType.Trim();

        // 配列は要素型へ同じ規則を当てて [] を付け直す（format_type は次元数を表記に出さない）
        var suffix = "";

        while (text.EndsWith("[]", StringComparison.Ordinal))
        {
            suffix = "[]" + suffix;
            text = text[..^2].TrimEnd();
        }

        return NormalizeElementType(text) + suffix;
    }

    /// <summary>配列の <c>[]</c> を外した要素型 1 つを短縮語彙へ正規化する</summary>
    private static string NormalizeElementType(string text)
    {
        var dateTime = DateTimeTypePattern().Match(text);

        if (dateTime.Success)
        {
            var precision = PrecisionOrDefault(dateTime);
            var withTimeZone = string.Equals(
                dateTime.Groups["zone"].Value,
                "with",
                StringComparison.OrdinalIgnoreCase
            );

            return (dateTime.Groups["name"].Value.ToLowerInvariant(), withTimeZone) switch
            {
                ("timestamp", false) => $"timestamp({precision})",
                ("timestamp", true) => $"timestamptz({precision})",
                ("time", false) => $"time({precision})",
                // time with time zone は QuickER の従来語彙がそのままなので触らない
                _ => text,
            };
        }

        var shortDateTime = ShortDateTimeTypePattern().Match(text);

        if (shortDateTime.Success)
        {
            var name = shortDateTime.Groups["name"].Value.ToLowerInvariant();
            return $"{name}({PrecisionOrDefault(shortDateTime)})";
        }

        var character = CharacterTypePattern().Match(text);

        if (character.Success)
        {
            var shortName = character.Groups["varying"].Success ? "varchar" : "char";
            return shortName + character.Groups["args"].Value;
        }

        var zeroScaleNumeric = ZeroScaleNumericPattern().Match(text);

        if (zeroScaleNumeric.Success)
        {
            return $"numeric({zeroScaleNumeric.Groups["p"].Value})";
        }

        return text;
    }

    /// <summary>小数秒の精度のグループ（無ければ既定の 6）を文字列で返す</summary>
    private static string PrecisionOrDefault(Match match) =>
        match.Groups["p"].Success
            ? match.Groups["p"].Value
            : DefaultFractionalSecondsPrecision.ToString(CultureInfo.InvariantCulture);

    // timestamp / time の正準表記（"timestamp(3) without time zone" 等）。修飾子は名称の直後に入る
    [GeneratedRegex(
        @"^(?<name>timestamp|time)(?:\((?<p>\d+)\))? (?<zone>with|without) time zone$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DateTimeTypePattern();

    // 短縮語彙の timestamp / timestamptz / time（精度の有無を問わない）。図・選択肢・TryFormat が書く形
    [GeneratedRegex(
        @"^(?<name>timestamp|timestamptz|time)\s*(?:\(\s*(?<p>\d+)\s*\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ShortDateTimeTypePattern();

    // character varying(n) / character(n)（長さ無しの正準表記も一応受ける）
    [GeneratedRegex(
        @"^character(?<varying> varying)?(?<args>\(\d+\))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex CharacterTypePattern();

    // format_type はスケール 0 も必ず書き下ろすため、QuickER の従来表記 numeric(p) へ畳む
    // （図・選択肢・TryFormat が書く numeric(18,0) も同じ形へ寄せる）
    [GeneratedRegex(
        @"^numeric\s*\(\s*(?<p>\d+)\s*,\s*0\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ZeroScaleNumericPattern();
}
