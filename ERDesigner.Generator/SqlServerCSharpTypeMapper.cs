using System.Globalization;
using System.Text.RegularExpressions;

namespace ERDesigner.Generator;

internal sealed partial class SqlServerCSharpTypeMapper
{
    public CSharpTypeInfo Map(string dataType)
    {
        var normalized = Normalize(dataType);
        var baseType = GetBaseType(normalized);
        var maxLength = TryGetLength(normalized);

        return baseType switch
        {
            "bit" => Value("bool"),
            "tinyint" => Value("byte"),
            "smallint" => Value("short"),
            "int" => Value("int"),
            "bigint" => Value("long"),
            "real" => Value("float"),
            "float" => Value("double"),
            "decimal" or "numeric" or "money" or "smallmoney" => Value("decimal"),
            "date" or "datetime" or "datetime2" or "smalldatetime" => Value("DateTime"),
            "time" => Value("TimeSpan"),
            "datetimeoffset" => Value("DateTimeOffset"),
            "uniqueidentifier" => Value("Guid"),
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => Reference("byte[]"),
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => Reference("string", maxLength),
            _ => Reference("string"),
        };
    }

    private static CSharpTypeInfo Value(string typeName) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = false,
        };

    private static CSharpTypeInfo Reference(string typeName, int? maxLength = null) =>
        new()
        {
            TypeName = typeName,
            IsReferenceType = true,
            MaxLength = maxLength,
        };

    private static string Normalize(string dataType) => dataType.Trim().ToLowerInvariant();

    private static string GetBaseType(string normalizedDataType)
    {
        var parenIndex = normalizedDataType.IndexOf('(', StringComparison.Ordinal);
        return parenIndex < 0 ? normalizedDataType : normalizedDataType[..parenIndex].Trim();
    }

    private static int? TryGetLength(string normalizedDataType)
    {
        var match = LengthRegex().Match(normalizedDataType);
        if (!match.Success || match.Groups[1].Value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : null;
    }

    [GeneratedRegex(@"\((max|\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();
}
