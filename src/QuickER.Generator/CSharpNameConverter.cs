using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickER.Generator;

/// <summary>
/// テーブル名・カラム名を C# の識別子へ変換するコンバーター
/// </summary>
/// <remarks>
/// 命名変換規則:
/// <list type="bullet">
/// <item><description>クラス名: テーブル名を単数形化 → PascalCase 化 → 用途別サフィックス（Entity / EditModel / Mapper）を付与。既に同サフィックスで終わる場合は重複付与しない</description></item>
/// <item><description>プロパティ名: カラム名を PascalCase 化。C# キーワードと一致する場合は <c>@</c> を前置する</description></item>
/// <item><description>ナビゲーション名: コレクションは複数形、単一参照は単数形のテーブル名を PascalCase 化する</description></item>
/// <item><description>PascalCase 化: 単語分割（区切り文字・大小文字境界・数字境界）後、各単語を先頭大文字化して連結。空になる場合は "Generated"、先頭が数字の場合は <c>_</c> を前置する</description></item>
/// <item><description>単数形化・複数形化: 英語の簡易規則のみ対応（ies⇔y、末尾 s の増減）。不規則変化は対象外</description></item>
/// </list>
/// </remarks>
internal sealed partial class CSharpNameConverter
{
    /// <summary>C# の予約キーワード一覧。識別子衝突時の <c>@</c> エスケープ判定に使う</summary>
    private static readonly HashSet<string> Keywords =
    [
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    ];

    /// <summary>テーブル名からエンティティクラス名を生成する（例: "order_items" → "OrderItemEntity"）</summary>
    public string ToEntityClassName(string tableName) =>
        EnsureSuffix(ToPascalCase(Singularize(tableName)), "Entity");

    /// <summary>テーブル名から EditModel クラス名を生成する（例: "order_items" → "OrderItemEditModel"）</summary>
    public string ToEditModelClassName(string tableName) =>
        EnsureSuffix(ToPascalCase(Singularize(tableName)), "EditModel");

    /// <summary>テーブル名から Mapper クラス名を生成する（例: "order_items" → "OrderItemMapper"）</summary>
    public string ToMapperClassName(string tableName) =>
        EnsureSuffix(ToPascalCase(Singularize(tableName)), "Mapper");

    /// <summary>
    /// カラム名からプロパティ名を生成する（例: "customer_id" → "CustomerId"）
    /// </summary>
    /// <remarks>C# キーワードと一致する場合は <c>@</c> を前置して有効な識別子にする</remarks>
    public string ToPropertyName(string columnName)
    {
        var propertyName = ToPascalCase(columnName);
        return Keywords.Contains(propertyName) ? "@" + propertyName : propertyName;
    }

    /// <summary>
    /// カラム名から値オブジェクト（Value Object）のクラス名を生成する（例: "customer_id" → "CustomerIdValue"）
    /// </summary>
    /// <remarks>
    /// 列名（正規化 Pascal）でグローバルに共有する VO のため、名前はカラム名のみから決まる（テーブル名を含めない）。
    /// 末尾に "Value" を付けるためキーワード衝突は起きない（@ エスケープ不要）。
    /// </remarks>
    public string ToValueObjectClassName(string columnName) => ToPascalCase(columnName) + "Value";

    /// <summary>カラム名を VO 共有キー（正規化 Pascal 名）へ変換する。同名判定・グルーピングに使う</summary>
    public string ToColumnKey(string columnName) => ToPascalCase(columnName);

    /// <summary>
    /// テーブル名からナビゲーションプロパティ名を生成する
    /// </summary>
    /// <param name="tableName">参照先のテーブル名</param>
    /// <param name="collection">コレクションナビゲーション（1対多の「多」側）かどうか。true なら複数形、false なら単数形にする</param>
    public string ToNavigationName(string tableName, bool collection)
    {
        var baseName = collection
            ? ToPascalCase(Pluralize(Singularize(tableName)))
            : ToPascalCase(Singularize(tableName));
        return Keywords.Contains(baseName) ? "@" + baseName : baseName;
    }

    /// <summary>
    /// 文字列を PascalCase の識別子へ変換する
    /// </summary>
    /// <remarks>
    /// 単語ごとに小文字化してから先頭のみ大文字化するため、連続大文字の頭字語も "Id" のような表記になる。
    /// 有効な単語が一つもない場合は "Generated"、先頭が数字になる場合は <c>_</c> を前置して識別子として成立させる
    /// </remarks>
    private static string ToPascalCase(string value)
    {
        var builder = new StringBuilder();

        foreach (var part in TokenizeWords(value))
        {
            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            var lower = part.ToLowerInvariant();
            builder.Append(textInfo.ToTitleCase(lower));
        }

        var result = builder.Length == 0 ? "Generated" : builder.ToString();
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    /// <summary>指定サフィックスで終わらない場合のみサフィックスを付与する</summary>
    private static string EnsureSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.Ordinal) ? value : value + suffix;

    /// <summary>
    /// テーブル名を簡易規則で単数形化する
    /// </summary>
    /// <remarks>
    /// 規則: 末尾 "ies" → "y"（categories → category）、末尾 "s"（"ss" を除く）→ 除去（orders → order）。
    /// 不規則変化（people 等）には対応しない。判定前に単語分割して "_" 連結した正規化形へ変換する
    /// </remarks>
    private static string Singularize(string value)
    {
        var pascal = ToSimpleToken(value);
        if (pascal.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && pascal.Length > 3)
        {
            return pascal[..^3] + "y";
        }

        return
            pascal.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && !pascal.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
            && pascal.Length > 1
            ? pascal[..^1]
            : pascal;
    }

    /// <summary>
    /// 単語を簡易規則で複数形化する
    /// </summary>
    /// <remarks>規則: 末尾 "y" → "ies"（category → categories）、既に "s" で終わる場合はそのまま、それ以外は "s" を付与する</remarks>
    private static string Pluralize(string value)
    {
        if (value.EndsWith("y", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            return value[..^1] + "ies";
        }

        return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? value : value + "s";
    }

    /// <summary>単語分割した結果を "_" で連結し、単数形化の判定に使う正規化形へ変換する</summary>
    private static string ToSimpleToken(string value) => string.Join("_", TokenizeWords(value));

    /// <summary>
    /// 文字列を識別子の構成単語へ分割する
    /// </summary>
    /// <remarks>
    /// まず記号・空白などの非英数字で分割し、各断片をさらに PascalCase 境界
    /// （大文字小文字の切り替わり・頭字語の終わり・数字の並び）で分割する。
    /// 例: "order_itemsV2" → ["order", "items", "V", "2"]
    /// </remarks>
    private static IEnumerable<string> TokenizeWords(string value)
    {
        foreach (
            var part in WordSplitRegex()
                .Split(value.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
        )
        {
            var matches = PascalCaseWordRegex().Matches(part);

            if (matches.Count == 0)
            {
                yield return part;
                continue;
            }

            foreach (Match match in matches)
            {
                yield return match.Value;
            }
        }
    }

    /// <summary>非英数字（記号・空白等）の並びを単語区切りとして検出する正規表現</summary>
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSplitRegex();

    /// <summary>PascalCase 文字列内の単語（頭字語・通常単語・数字列）を検出する正規表現</summary>
    [GeneratedRegex(
        @"\p{Lu}+(?=\p{Lu}\p{Ll}|\p{Nd}|$)|\p{Lu}?\p{Ll}+|\p{Nd}+",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex PascalCaseWordRegex();
}
