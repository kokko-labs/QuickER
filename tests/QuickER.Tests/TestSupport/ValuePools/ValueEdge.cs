using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>値の系統（値の実行時の型で決まる。<see cref="Narrowing"/> だけは列の相の変換先で決まる）</summary>
public enum ValueFamily
{
    /// <summary>DateTime</summary>
    DateTime,

    /// <summary>DateTimeOffset</summary>
    DateTimeOffset,

    /// <summary>TimeSpan</summary>
    TimeSpan,

    /// <summary>decimal</summary>
    Decimal,

    /// <summary>整数（sbyte〜ulong）</summary>
    Integer,

    /// <summary>浮動小数点（float・double）</summary>
    Floating,

    /// <summary>文字列</summary>
    String,

    /// <summary>バイト列（byte[]）</summary>
    Binary,

    /// <summary>Guid</summary>
    Guid,

    /// <summary>bool</summary>
    Boolean,

    /// <summary>値を変える明示の変換の元の値（列の相に変換先 <see cref="ValueColumn.CastTarget"/> のある観測だけ）</summary>
    Narrowing,
}

/// <summary>値の端の水準（値のプールの名簿の 1 行）</summary>
/// <param name="Id">Id（小文字とハイフン。系統の接頭辞で始める）</param>
/// <param name="Family">系統</param>
/// <param name="Branch">その値で製品のどの判定の答えが変わりうるか（1 文）</param>
/// <param name="NeedsColumn">列の相が無いと判定できない水準か（列の型の桁・長さ・範囲と比べる水準）</param>
public sealed record ValueEdge(string Id, ValueFamily Family, string Branch, bool NeedsColumn);

/// <summary>値を比べる・書く列の相（宣言の型から読んだ桁・長さ・範囲と、値を変える明示の変換の変換先）</summary>
/// <remarks>
/// 欠けた相は null（判定できない水準は判定しない）。整数の列は <see cref="Min"/>・<see cref="Max"/> に型の範囲と
/// <see cref="Scale"/> に 0 を持つ（decimal の値と比べるときの範囲とスケール）。
/// </remarks>
public sealed record ValueColumn
{
    /// <summary>宣言の型の名前（例 <c>datetime2(2)</c>。型の名前を持たない相は null）</summary>
    public string? SqlType { get; init; }

    /// <summary>decimal の精度</summary>
    public int? Precision { get; init; }

    /// <summary>小数の桁（decimal のスケール・日時と時刻の小数秒の桁・整数の列は 0）</summary>
    public int? Scale { get; init; }

    /// <summary>文字列・バイト列の長さ（max・長さの無い型は null）</summary>
    public int? Length { get; init; }

    /// <summary>固定長か（char・nchar・binary）</summary>
    public bool? FixedLength { get; init; }

    /// <summary>Unicode で持つか（nchar・nvarchar は真、char・varchar は偽）</summary>
    public bool? Unicode { get; init; }

    /// <summary>値の範囲の最小（money・smallmoney と整数の列の型の範囲）</summary>
    public decimal? Min { get; init; }

    /// <summary>値の範囲の最大（money・smallmoney と整数の列の型の範囲）</summary>
    public decimal? Max { get; init; }

    /// <summary>値を変える明示の変換・符号だけの変換の変換先（無ければ null）</summary>
    public Type? CastTarget { get; init; }

    /// <summary>型の名前と引数（<c>name(a)</c>・<c>name(a,b)</c>・<c>name</c>）</summary>
    private static readonly Regex TypePattern = new(
        "^(?<name>[a-z][a-z0-9_ ]*?)\\s*(?:\\(\\s*(?<a>max|[0-9]+)\\s*(?:,\\s*(?<b>-?[0-9]+)\\s*)?\\))?(?<suffix>(?:\\s+[a-z ]+)?)$",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 宣言の型の名前から相を読む（読めない型は <see cref="SqlType"/> だけを持つ相を返す。例外にしない）。
    /// 配列（<c>time[]</c>）は要素の型の相を読む
    /// </summary>
    /// <param name="sqlType">宣言の型の名前</param>
    public static ValueColumn FromSqlType(string sqlType)
    {
        ArgumentNullException.ThrowIfNull(sqlType);
        var text = sqlType.Trim().ToLowerInvariant();

        if (text.EndsWith("[]", StringComparison.Ordinal))
        {
            text = text[..^2].TrimEnd();
        }

        var column = new ValueColumn { SqlType = sqlType.Trim() };
        var match = TypePattern.Match(text);

        if (!match.Success)
        {
            return column;
        }

        var name = match.Groups["name"].Value.Trim();
        var suffix = match.Groups["suffix"].Value.Trim();
        var max = match.Groups["a"].Value == "max";
        int? a =
            match.Groups["a"].Success && !max
                ? int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture)
                : null;
        int? b = match.Groups["b"].Success
            ? int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture)
            : null;
        var unsigned = suffix.Contains("unsigned", StringComparison.Ordinal);

        return name switch
        {
            "decimal" or "numeric" or "number" when a is not null => column with
            {
                Precision = a,
                Scale = b ?? 0,
            },
            "money" => column with
            {
                Scale = 4,
                Min = -922_337_203_685_477.5808m,
                Max = 922_337_203_685_477.5807m,
            },
            "smallmoney" => column with { Scale = 4, Min = -214_748.3648m, Max = 214_748.3647m },
            // SQL Server の tinyint（0〜255）。MySQL の符号つきの tinyint は観測する側が CLR の型から範囲を付け直す
            "tinyint" => Integer(column, 0, 255),
            "smallint" => unsigned
                ? Integer(column, 0, ushort.MaxValue)
                : Integer(column, short.MinValue, short.MaxValue),
            "int" or "integer" or "mediumint" => unsigned
                ? Integer(column, 0, uint.MaxValue)
                : Integer(column, int.MinValue, int.MaxValue),
            "bigint" => unsigned
                ? Integer(column, 0, ulong.MaxValue)
                : Integer(column, long.MinValue, long.MaxValue),
            "datetime2" or "datetimeoffset" or "time" => column with { Scale = a ?? 7 },
            "timestamp" or "timestamptz" => column with { Scale = a ?? 6 },
            "datetime" when a is not null => column with { Scale = a },
            "char" or "character" => column with
            {
                Length = a ?? 1,
                FixedLength = true,
                Unicode = false,
            },
            "nchar" => column with { Length = a ?? 1, FixedLength = true, Unicode = true },
            "varchar" or "varchar2" or "character varying" => column with
            {
                Length = a,
                FixedLength = false,
                Unicode = false,
            },
            "nvarchar" or "nvarchar2" => column with
            {
                Length = a,
                FixedLength = false,
                Unicode = true,
            },
            "binary" => column with { Length = a ?? 1, FixedLength = true },
            "varbinary" or "raw" => column with { Length = a, FixedLength = false },
            _ => column,
        };
    }

    /// <summary>整数の列の相（型の範囲・スケール 0）</summary>
    private static ValueColumn Integer(ValueColumn column, decimal min, decimal max) =>
        column with
        {
            Scale = 0,
            Min = min,
            Max = max,
        };
}

/// <summary>エンジン・表が作った入力の値 1 つの観測</summary>
/// <param name="Value">値（リスト・配列は IN のパラメータ＝要素ごとに分類する）</param>
/// <param name="Column">比べる・書く列の相（無ければ null）</param>
/// <param name="Where">観測の出どころ（行 Id・単位の名前など。行列の出力で例を示す）</param>
public sealed record ValueObservation(object? Value, ValueColumn? Column, string Where);
