using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ERDesigner.Generator;

internal sealed partial class CSharpNameConverter
{
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

    public string ToEntityClassName(string tableName) => EnsureSuffix(ToPascalCase(Singularize(tableName)), "Entity");

    public string ToEditModelClassName(string tableName) => EnsureSuffix(ToPascalCase(Singularize(tableName)), "EditModel");

    public string ToMapperClassName(string tableName) => EnsureSuffix(ToPascalCase(Singularize(tableName)), "Mapper");

    public string ToPropertyName(string columnName)
    {
        var propertyName = ToPascalCase(columnName);
        return Keywords.Contains(propertyName) ? "@" + propertyName : propertyName;
    }

    public string ToNavigationName(string tableName, bool collection)
    {
        var baseName = collection ? ToPascalCase(Pluralize(Singularize(tableName))) : ToPascalCase(Singularize(tableName));
        return Keywords.Contains(baseName) ? "@" + baseName : baseName;
    }

    private static string ToPascalCase(string value)
    {
        var parts = WordSplitRegex().Split(value.Trim()).Where(part => !string.IsNullOrWhiteSpace(part));
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            var lower = part.ToLowerInvariant();
            builder.Append(textInfo.ToTitleCase(lower));
        }

        var result = builder.Length == 0 ? "Generated" : builder.ToString();
        return char.IsDigit(result[0]) ? "_" + result : result;
    }

    private static string EnsureSuffix(string value, string suffix) => value.EndsWith(suffix, StringComparison.Ordinal) ? value : value + suffix;

    private static string Singularize(string value)
    {
        var pascal = ToSimpleToken(value);
        if (pascal.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && pascal.Length > 3)
        {
            return pascal[..^3] + "y";
        }

        return pascal.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !pascal.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && pascal.Length > 1 ? pascal[..^1] : pascal;
    }

    private static string Pluralize(string value)
    {
        if (value.EndsWith("y", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            return value[..^1] + "ies";
        }

        return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? value : value + "s";
    }

    private static string ToSimpleToken(string value) => string.Join("_", WordSplitRegex().Split(value.Trim()).Where(part => !string.IsNullOrWhiteSpace(part)));

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordSplitRegex();
}
