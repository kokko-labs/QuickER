using System.Globalization;
using System.Text;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>列の C# 側の種類（オラクルの比較の仕方と、生成器が使える演算子を決める）</summary>
public enum DslClr
{
    /// <summary>可変長の文字列</summary>
    String,

    /// <summary>固定長の文字列（SQLite は詰めない）</summary>
    FixedString,

    /// <summary>long</summary>
    Int64,

    /// <summary>int</summary>
    Int32,

    /// <summary>short</summary>
    Int16,

    /// <summary>byte（符号なし 0〜255）</summary>
    Byte,

    /// <summary>sbyte</summary>
    SByte,

    /// <summary>ushort</summary>
    UInt16,

    /// <summary>uint</summary>
    UInt32,

    /// <summary>ulong</summary>
    UInt64,

    /// <summary>decimal</summary>
    Decimal,

    /// <summary>DateTime</summary>
    DateTime,

    /// <summary>Guid</summary>
    Guid,

    /// <summary>GuidKey の値オブジェクト（文字列の主キー）</summary>
    GuidKey,
}

/// <summary>総当たりの図の列</summary>
/// <param name="Name">列名</param>
/// <param name="DataType">方言の宣言型</param>
/// <param name="Clr">C# 側の種類</param>
/// <param name="Nullable">NULL を許すか</param>
/// <param name="PrimaryKey">主キーか</param>
public sealed record DslColumn(
    string Name,
    string DataType,
    DslClr Clr,
    bool Nullable,
    bool PrimaryKey = false
)
{
    /// <summary>文字列一致を使える列か</summary>
    public bool IsString => Clr is DslClr.String or DslClr.FixedString or DslClr.GuidKey;

    /// <summary>整数の列か</summary>
    public bool IsInteger =>
        Clr
            is DslClr.Int64
                or DslClr.Int32
                or DslClr.Int16
                or DslClr.Byte
                or DslClr.SByte
                or DslClr.UInt16
                or DslClr.UInt32
                or DslClr.UInt64;

    /// <summary>順序比較（&lt; など）を使える列か（型の検査が文字列・Guid の順序比較を断る）</summary>
    public bool IsOrdered => IsInteger || Clr is DslClr.Decimal or DslClr.DateTime;

    /// <summary>モデルの列（Id は図を組み立てるときに決まる）</summary>
    public Column Model { get; } =
        new()
        {
            Name = Name,
            DataType = DataType,
            IsNullable = Nullable && !PrimaryKey,
            IsPrimaryKey = PrimaryKey,
        };
}

/// <summary>比較演算子</summary>
public enum DslOp
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
}

/// <summary>文字列一致の種類</summary>
public enum DslMatchKind
{
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary>比較の右辺</summary>
public abstract record DslOperand;

/// <summary>リテラル（数値・文字列）</summary>
/// <param name="Value">C# の値</param>
/// <param name="Text">DSL の綴り</param>
public sealed record DslLiteral(object Value, string Text) : DslOperand;

/// <summary>パラメータ</summary>
/// <param name="Name">パラメータ名</param>
/// <param name="Values">値（リストでなければ 1 つ）</param>
/// <param name="IsList">リストか</param>
/// <param name="ByColumn">source_column で型を決めるか（false は型トークン）</param>
/// <param name="Column">型を決める列</param>
public sealed record DslParam(
    string Name,
    IReadOnlyList<object> Values,
    bool IsList,
    bool ByColumn,
    DslColumn Column
) : DslOperand;

/// <summary>条件の構文木</summary>
public abstract record DslNode;

/// <summary>1 つの条件（原子）</summary>
public abstract record DslAtom : DslNode
{
    /// <summary>対象の列</summary>
    public required DslColumn Column { get; init; }

    /// <summary>失敗のキーに使う原子の種類（演算子の形と列の種類）</summary>
    public abstract string Kind { get; }

    /// <summary>原子自身の否定（NOT LIKE・NOT IN・IS NOT NULL など）</summary>
    public virtual bool InherentNegation => false;

    /// <summary>
    /// 型の検査が断ると決まっている原子か（生成時に警告して、その原子を含むクエリを飛ばす）。
    /// 列の型の範囲外の整数リテラル・GuidKey の列への完全一致以外の文字列一致
    /// </summary>
    public virtual bool IsRejectedByTypeCheck => false;

    /// <summary>このクエリが起こし得る、既知の原因で説明できるコンパイラの診断（コード → 原因の名前）</summary>
    public virtual IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects) =>
        [];
}

/// <summary>比較（列 演算子 右辺）</summary>
public sealed record DslCompare(DslOp Op, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        $"compare-{Op.ToString().ToLowerInvariant()}-{OperandKind}:{Column.Clr.ToString().ToLowerInvariant()}";

    private string OperandKind =>
        Operand is DslParam ? "param"
        : RejectedByTypeCheck ? "literal-rejected"
        : "literal";

    /// <summary>
    /// 型の検査が断るリテラルか（C# の整数リテラルにならない値と、列の型〔値オブジェクトなら内包型〕の範囲外。
    /// 生成時に警告で飛ばされることを確かめる）
    /// </summary>
    public bool RejectedByTypeCheck { get; init; }

    public override bool IsRejectedByTypeCheck => RejectedByTypeCheck;
}

/// <summary>IS [NOT] NULL</summary>
public sealed record DslNullCheck(bool IsNot) : DslAtom
{
    public override string Kind => $"null-check:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => IsNot;
}

/// <summary>[NOT] LIKE（リテラルは % の位置で分解・パラメータは部分一致）</summary>
public sealed record DslLike(bool Negated, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        Operand switch
        {
            DslLiteral literal =>
                $"like-{LikeShape(literal.Text)}:{Column.Clr.ToString().ToLowerInvariant()}",
            _ => $"like-param:{Column.Clr.ToString().ToLowerInvariant()}",
        };

    public override bool InherentNegation => Negated;

    /// <summary>GuidKey の列は部分一致のメソッドを持たないので、完全一致（ワイルドカードなしのリテラル）以外は断られる</summary>
    public override bool IsRejectedByTypeCheck =>
        Column.Clr == DslClr.GuidKey
        && !(Operand is DslLiteral literal && LikeShape(literal.Text) == "exact");

    /// <summary>リテラルのパターンの形（exact・prefix・suffix・contains）</summary>
    public static string LikeShape(string quoted)
    {
        var pattern = quoted[1..^1];
        var starts = pattern.StartsWith('%');
        var ends = pattern.EndsWith('%') && pattern.Length > 1;

        return (starts, ends) switch
        {
            (true, true) => "contains",
            (true, false) => "suffix",
            (false, true) => "prefix",
            _ => "exact",
        };
    }
}

/// <summary>[NOT] IN</summary>
public sealed record DslIn(bool Negated, DslParam List) : DslAtom
{
    public override string Kind => $"in:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => Negated;

    public override IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects) =>
        valueObjects && Column.Nullable ? [("CS8604", "in-nullable-vo")] : [];
}

/// <summary>[NOT] CONTAINS / STARTSWITH / ENDSWITH</summary>
public sealed record DslMatch(DslMatchKind Match, bool Negated, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        $"{Match.ToString().ToLowerInvariant()}-{(Operand is DslLiteral ? "literal" : "param")}:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => Negated;

    /// <summary>GuidKey の列は部分一致のメソッドを持たないので、CONTAINS 系は断られる</summary>
    public override bool IsRejectedByTypeCheck => Column.Clr == DslClr.GuidKey;
}

/// <summary>NOT</summary>
public sealed record DslNot(DslNode Inner) : DslNode;

/// <summary>AND</summary>
public sealed record DslAnd(IReadOnlyList<DslNode> Items) : DslNode;

/// <summary>OR</summary>
public sealed record DslOr(IReadOnlyList<DslNode> Items) : DslNode;

/// <summary>総当たりの 1 クエリ</summary>
public sealed class DslQuery
{
    public required string Name { get; init; }

    public required DslNode Condition { get; init; }

    public required QueryReturnShape Returns { get; init; }

    /// <summary>原子だけのクエリか（食い違いを原子の種類へ帰着させるため）</summary>
    public required bool IsAtomQuery { get; init; }

    /// <summary>単一・射影の並び（主キーの降順なら true）</summary>
    public bool OrderDescending { get; init; }

    /// <summary>射影の 2 つめのフィールドの列（射影のときだけ）</summary>
    public DslColumn? ProjectedColumn { get; init; }

    /// <summary>射影の 2 つめのフィールドを非 NULL と指定するか（NULL 許容の参照型・VO の列で CS8601＝A8）</summary>
    public bool ProjectedNonNull { get; init; }

    /// <summary>射影の結果の型名</summary>
    public string? ResultTypeName { get; init; }

    /// <summary>射影の型名を既存の型と衝突させた種類（衝突させないなら null）</summary>
    public string? TypeNameCollision { get; init; }

    /// <summary>原子の一覧</summary>
    public IEnumerable<DslAtom> Atoms => DslFuzz.AtomsOf(Condition);

    /// <summary>パラメータの一覧（出現順・重複なし）</summary>
    public IEnumerable<DslParam> Parameters =>
        Atoms
            .SelectMany(atom =>
                atom switch
                {
                    DslCompare { Operand: DslParam p } => [p],
                    DslLike { Operand: DslParam p } => [p],
                    DslMatch { Operand: DslParam p } => [p],
                    DslIn @in => [@in.List],
                    _ => Array.Empty<DslParam>(),
                }
            )
            .DistinctBy(p => p.Name);

    /// <summary>このクエリが起こし得る、既知の原因の診断</summary>
    public IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects)
    {
        foreach (var known in Atoms.SelectMany(atom => atom.KnownDiagnostics(valueObjects)))
        {
            yield return known;
        }

        if (ProjectedNonNull)
        {
            yield return ("CS8601", "projection-nonnull-field");
        }
    }
}

/// <summary>実装先ごとの文字列一致の大文字小文字の扱い（docs の表の写し）</summary>
public enum DslCaseRule
{
    /// <summary>序数（大文字小文字を区別する）</summary>
    Ordinal,

    /// <summary>ASCII の英字だけを畳む（SQLite の組み込みの LIKE）</summary>
    AsciiFold,

    /// <summary>格納先任せ（序数と畳んだ結果が違う行は判定しない）</summary>
    Indeterminate,

    /// <summary>
    /// 列の照合順序（SQL Server。既定の <c>..._CI_AS</c> は大文字小文字を区別せず、<c>=</c> は末尾の空白を無視する）。
    /// 大文字小文字か末尾の空白だけで結果が変わる行は判定しない（NULL の行と、照合順序に依らず決まる行は判定する）
    /// </summary>
    /// <remarks>
    /// docs の表の SQL Server の行（「列の照合順序に従う」）と、同じ節の「<c>= ''</c> は末尾の空白を無視する」の写し。
    /// 文字列一致（LIKE のワイルドカードつき・Contains / StartsWith / EndsWith）は末尾の空白を区別するので、
    /// 大文字小文字だけを畳む（同じ節の「<c>EndsWith</c> は詰めた値に対して比べる」）。
    /// </remarks>
    Collation,
}

/// <summary>
/// 簡易 DSL の条件の総当たり（生成器・描画・オラクル）
/// </summary>
/// <remarks>
/// <para>
/// オラクル（<see cref="Evaluate"/>）は生成器の構文木を、エミッタを使わずに docs の意味で評価する独立実装。正本は
/// docs/code-generation(.ja).md の「クエリ（式木 → SQL 変換）」節（NULL の扱い・文字列一致の NULL の前提・
/// 「NOT と &lt;&gt; は C# の否定と同じ、文字列一致だけは NULL の行がどちら向きでも一致しない」）と、
/// 同じ節の「大文字小文字・空白・日付部品」の表（SQLite の組み込みの LIKE は ASCII の英字だけを畳む・EF Core は格納先次第・
/// インメモリは序数・SQL Server は列の照合順序）。実装の挙動は写さない（実装と一緒に間違えるため）。
/// </para>
/// <para>
/// 大文字小文字の表で「格納先次第」「照合順序次第」の実装先は、序数と畳んだ結果が違う行を判定しない（<see cref="DslCaseRule.Indeterminate"/>）。
/// 完全一致の LIKE（ワイルドカードの無いリテラル）は表に載っていないため、SQLite でも判定しない側に倒す。
/// </para>
/// </remarks>
public static class DslFuzz
{
    /// <summary>原子を出現順に列挙する</summary>
    public static IEnumerable<DslAtom> AtomsOf(DslNode node) =>
        node switch
        {
            DslAtom atom => [atom],
            DslNot not => AtomsOf(not.Inner),
            DslAnd and => and.Items.SelectMany(AtomsOf),
            DslOr or => or.Items.SelectMany(AtomsOf),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    // ---------------- 描画 ----------------

    /// <summary>条件を DSL の文字列にする</summary>
    public static string Render(DslNode node) =>
        node switch
        {
            DslAtom atom => RenderAtom(atom),
            DslNot not => "NOT " + Wrap(not.Inner),
            DslAnd and => string.Join(" AND ", and.Items.Select(Wrap)),
            DslOr or => string.Join(" OR ", or.Items.Select(Wrap)),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    private static string Wrap(DslNode node) =>
        node is DslAtom ? Render(node) : "(" + Render(node) + ")";

    private static string RenderAtom(DslAtom atom)
    {
        var column = atom.Column.Name;

        return atom switch
        {
            DslCompare c => $"{column} {OpText(c.Op)} {RenderOperand(c.Operand)}",
            DslNullCheck n => $"{column} IS {(n.IsNot ? "NOT " : "")}NULL",
            DslLike l => $"{column} {(l.Negated ? "NOT " : "")}LIKE {RenderOperand(l.Operand)}",
            DslIn i => $"{column} {(i.Negated ? "NOT " : "")}IN @{i.List.Name}",
            DslMatch m =>
                $"{column} {(m.Negated ? "NOT " : "")}{m.Match.ToString().ToUpperInvariant()} {RenderOperand(m.Operand)}",
            _ => throw new ArgumentOutOfRangeException(nameof(atom)),
        };
    }

    private static string RenderOperand(DslOperand operand) =>
        operand switch
        {
            DslLiteral literal => literal.Text,
            DslParam param => "@" + param.Name,
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

    private static string OpText(DslOp op) =>
        op switch
        {
            DslOp.Eq => "=",
            DslOp.Ne => "<>",
            DslOp.Lt => "<",
            DslOp.Le => "<=",
            DslOp.Gt => ">",
            DslOp.Ge => ">=",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    // ---------------- オラクル ----------------

    /// <summary>行に条件を当てる（true / false、判定しない行は null）</summary>
    /// <param name="node">条件</param>
    /// <param name="row">列名 → 実装先から読み戻した値（VO は開いた値）</param>
    /// <param name="caseRule">文字列一致の大文字小文字の扱い</param>
    /// <param name="negate">外側の NOT（ド・モルガンで押し下げる）</param>
    public static bool? Evaluate(
        DslNode node,
        IReadOnlyDictionary<string, object?> row,
        DslCaseRule caseRule,
        bool negate = false
    ) =>
        node switch
        {
            DslNot not => Evaluate(not.Inner, row, caseRule, !negate),
            DslAnd and => negate
                ? Any(and.Items.Select(item => Evaluate(item, row, caseRule, true)))
                : All(and.Items.Select(item => Evaluate(item, row, caseRule, false))),
            DslOr or => negate
                ? All(or.Items.Select(item => Evaluate(item, row, caseRule, true)))
                : Any(or.Items.Select(item => Evaluate(item, row, caseRule, false))),
            DslAtom atom => EvaluateAtom(atom, row[atom.Column.Name], caseRule, negate),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    /// <summary>Kleene の AND</summary>
    private static bool? All(IEnumerable<bool?> values)
    {
        var unknown = false;

        foreach (var value in values)
        {
            if (value == false)
            {
                return false;
            }

            unknown |= value is null;
        }

        return unknown ? null : true;
    }

    /// <summary>Kleene の OR</summary>
    private static bool? Any(IEnumerable<bool?> values)
    {
        var unknown = false;

        foreach (var value in values)
        {
            if (value == true)
            {
                return true;
            }

            unknown |= value is null;
        }

        return unknown ? null : false;
    }

    private static bool? EvaluateAtom(
        DslAtom atom,
        object? value,
        DslCaseRule caseRule,
        bool negate
    )
    {
        switch (atom)
        {
            // 比較と NOT は C# の lifted な意味（NULL は非 null 値と等しくない＝<> は真・順序比較は偽。NOT はそのまま否定）
            case DslCompare compare:
                return Xor(
                    CompareUnder(value, compare.Op, OperandValue(compare.Operand), caseRule),
                    negate
                );

            case DslNullCheck check:
                return (value is null) ^ check.IsNot ^ negate;

            // IN は C# の List.Contains（NULL の列はリストに無い＝NOT IN は真）
            case DslIn @in:
                return Xor(InUnder(value, @in.List.Values, caseRule), @in.Negated ^ negate);

            // 文字列一致は NULL の行がどちらの向きでも一致しない
            case DslLike like:
            {
                if (value is null)
                {
                    return false;
                }

                var (kind, pattern) = like.Operand switch
                {
                    DslLiteral literal => LikePattern(literal.Text),
                    DslParam param => ("contains", (string)param.Values[0]),
                    _ => throw new ArgumentOutOfRangeException(nameof(atom)),
                };

                // 完全一致の LIKE は docs の大文字小文字の表に載っていない＝序数以外の実装先では判定しない側に倒す
                var rule =
                    kind == "exact" && caseRule == DslCaseRule.AsciiFold
                        ? DslCaseRule.Indeterminate
                        : caseRule;

                return Xor(Match((string)value, kind, pattern, rule), like.Negated ^ negate);
            }

            case DslMatch match:
            {
                if (value is null)
                {
                    return false;
                }

                var kind = match.Match switch
                {
                    DslMatchKind.Contains => "contains",
                    DslMatchKind.StartsWith => "prefix",
                    _ => "suffix",
                };

                return Xor(
                    Match((string)value, kind, (string)OperandValue(match.Operand), caseRule),
                    match.Negated ^ negate
                );
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(atom));
        }
    }

    private static bool? Xor(bool? value, bool flip) => value is null ? null : value.Value ^ flip;

    private static object OperandValue(DslOperand operand) =>
        operand switch
        {
            DslLiteral literal => literal.Value,
            DslParam param => param.Values[0],
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

    private static (string Kind, string Pattern) LikePattern(string quoted)
    {
        var shape = DslLike.LikeShape(quoted);
        var pattern = quoted[1..^1];

        return shape switch
        {
            "contains" => (shape, pattern[1..^1]),
            "suffix" => (shape, pattern[1..]),
            "prefix" => (shape, pattern[..^1]),
            _ => (shape, pattern),
        };
    }

    /// <summary>文字列一致（大文字小文字の扱いは実装先ごと）</summary>
    private static bool? Match(string value, string kind, string pattern, DslCaseRule rule)
    {
        bool Test(Func<string, string> fold)
        {
            var v = fold(value);
            var p = fold(pattern);

            return kind switch
            {
                "contains" => v.Contains(p, StringComparison.Ordinal),
                "prefix" => v.StartsWith(p, StringComparison.Ordinal),
                "suffix" => v.EndsWith(p, StringComparison.Ordinal),
                _ => string.Equals(v, p, StringComparison.Ordinal),
            };
        }

        var ordinal = Test(s => s);

        return rule switch
        {
            DslCaseRule.Ordinal => ordinal,
            DslCaseRule.AsciiFold => Test(FoldAscii),
            DslCaseRule.Collation => Test(s => CollationKey(s, trimTrailing: kind == "exact"))
            == ordinal
                ? ordinal
                : null,
            _ => Test(FoldAscii) == ordinal && Test(s => s.ToLowerInvariant()) == ordinal
                ? ordinal
                : null,
        };
    }

    /// <summary>照合順序で同じとみなされる形（大文字小文字を畳み、等号の比較なら末尾の空白も除く）</summary>
    private static string CollationKey(string value, bool trimTrailing) =>
        trimTrailing ? value.ToLowerInvariant().TrimEnd(' ') : value.ToLowerInvariant();

    /// <summary>比較（照合順序の実装先の文字列の =・&lt;&gt; は、照合順序で結果が変わる行を判定しない）</summary>
    private static bool? CompareUnder(object? value, DslOp op, object operand, DslCaseRule rule)
    {
        var ordinal = Compare(value, op, operand);

        if (
            rule != DslCaseRule.Collation
            || value is not string text
            || operand is not string other
        )
        {
            return ordinal;
        }

        var folded = Compare(CollationKey(text, true), op, CollationKey(other, true));

        return folded == ordinal ? ordinal : null;
    }

    /// <summary>IN（照合順序の実装先の文字列は、序数で一致しないが照合順序で一致する値があれば判定しない）</summary>
    private static bool? InUnder(object? value, IReadOnlyList<object> values, DslCaseRule rule)
    {
        if (value is null)
        {
            return false;
        }

        if (values.Any(v => SameValue(value, v)))
        {
            return true;
        }

        return
            rule == DslCaseRule.Collation
            && value is string text
            && values.Any(v =>
                v is string other && CollationKey(text, true) == CollationKey(other, true)
            )
            ? null
            : false;
    }

    /// <summary>ASCII の英字だけを小文字へ畳む</summary>
    private static string FoldAscii(string value) =>
        string.Create(
            value.Length,
            value,
            (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
                }
            }
        );

    /// <summary>C# の lifted な比較（NULL の列：= は偽・&lt;&gt; は真・順序比較は偽）</summary>
    private static bool Compare(object? value, DslOp op, object operand)
    {
        if (value is null)
        {
            return op == DslOp.Ne;
        }

        var order = CompareValues(value, operand);

        return op switch
        {
            DslOp.Eq => order == 0,
            DslOp.Ne => order != 0,
            DslOp.Lt => order < 0,
            DslOp.Le => order <= 0,
            DslOp.Gt => order > 0,
            _ => order >= 0,
        };
    }

    private static bool SameValue(object value, object other) => CompareValues(value, other) == 0;

    /// <summary>読み戻した値どうしが等しいか（NULL どうしは等しい）</summary>
    public static bool SameStoredValue(object? value, object? other) =>
        value is null || other is null ? value is null && other is null : SameValue(value, other);

    /// <summary>値どうしの順序（数値は decimal へ・文字列は序数・Guid と日時はそのまま）</summary>
    private static int CompareValues(object value, object other) =>
        (value, other) switch
        {
            (string a, string b) => string.CompareOrdinal(a, b),
            (Guid a, Guid b) => a.Equals(b) ? 0 : 1,
            (DateTime a, DateTime b) => a.CompareTo(b),
            _ => Convert
                .ToDecimal(value, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(other, CultureInfo.InvariantCulture)),
        };

    // ---------------- データの値 ----------------

    /// <summary>列の種類ごとの値の候補（境界値を含む）</summary>
    public static IReadOnlyList<object> ValuePool(DslClr clr) =>
        clr switch
        {
            DslClr.String =>
            [
                "",
                "a",
                "A",
                "abc",
                "ABC",
                "Abc",
                "xabcx",
                "a%b",
                "a_b",
                "%",
                "_",
                "ä",
                "Ä",
                "日本",
                "a b",
                "abc ",
                "ab",
            ],
            DslClr.FixedString => ["", "a", "A", "abc", "ABC", "a%b", "a_b", "x", "abcdefghij"],
            DslClr.Int64 => [long.MinValue, -1L, 0L, 1L, 2L, 10L, 100L, long.MaxValue],
            DslClr.Int32 => [int.MinValue, -1, 0, 1, 5, int.MaxValue],
            DslClr.Int16 => [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue],
            DslClr.Byte => [(byte)0, (byte)1, (byte)127, (byte)128, (byte)255],
            DslClr.SByte => [sbyte.MinValue, (sbyte)-1, (sbyte)0, (sbyte)1, sbyte.MaxValue],
            DslClr.UInt16 => [(ushort)0, (ushort)1, (ushort)32768, ushort.MaxValue],
            DslClr.UInt32 => [0u, 1u, 2147483648u, uint.MaxValue],
            DslClr.UInt64 => [0UL, 1UL, 9223372036854775808UL, ulong.MaxValue],
            DslClr.Decimal => [-99999999.99m, -1.5m, 0m, 0.01m, 1.5m, 2.25m, 10m, 99999999.99m],
            DslClr.DateTime =>
            [
                new DateTime(1999, 12, 31, 23, 59, 59),
                new DateTime(2000, 1, 1),
                new DateTime(2024, 2, 29),
                new DateTime(2024, 2, 29, 12, 34, 56),
            ],
            DslClr.Guid =>
            [
                new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
                new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            ],
            DslClr.GuidKey =>
            [
                "00000000-0000-0000-0000-000000000001",
                "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                "6F9619FF-8B86-D011-B42D-00C04FC964FF",
                "ffffffff-ffff-ffff-ffff-ffffffffffff",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(clr)),
        };

    /// <summary>数値のリテラルの綴り</summary>
    public static string NumberText(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture)!;

    /// <summary>型トークン（型トークンでパラメータを宣言するとき）</summary>
    public static string TypeToken(DslClr clr) =>
        clr switch
        {
            DslClr.String or DslClr.FixedString or DslClr.GuidKey => "string",
            DslClr.Int64 => "int64",
            DslClr.Int32 => "int32",
            DslClr.Int16 => "smallint",
            DslClr.Byte => "tinyint",
            DslClr.Decimal => "decimal(10,2)",
            DslClr.DateTime => "datetime",
            DslClr.Guid => "guid",
            _ => throw new ArgumentOutOfRangeException(nameof(clr), clr, "型トークンの無い種類"),
        };

    /// <summary>型トークンを持つ種類か（符号なしと sbyte は列から型を決める）</summary>
    public static bool HasTypeToken(DslClr clr) =>
        clr is not (DslClr.SByte or DslClr.UInt16 or DslClr.UInt32 or DslClr.UInt64);
}
