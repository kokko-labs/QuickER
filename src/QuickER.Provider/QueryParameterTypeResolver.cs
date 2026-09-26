using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 名前付きクエリの方言中立型トークン（例: <c>int32</c> / <c>string(50)</c>）を C# 型情報へ解決する。
/// </summary>
/// <remarks>
/// <para>
/// 列型と同じく型解決はプロバイダ層の責務。トークンを <see cref="CanonicalTypeToken.TryParse"/> で正規型へ解析し、
/// 図の方言の <see cref="ITypeCatalog"/> でネイティブ型文字列へ変換したうえで、同じ方言の
/// <see cref="IColumnTypeMapper"/>（合成 ER 図経由）で C# 型へ写す。列と完全に同じ経路を通すため、
/// 「同じトークンの列とパラメータは必ず同じ C# 型になる」ことが構造的に保証される。
/// </para>
/// <para>
/// 解決できないトークンは辞書に含めない（生成サービス側が解決不能の診断エラーを出す）。
/// </para>
/// </remarks>
public static class QueryParameterTypeResolver
{
    /// <summary>図の全クエリ定義が参照する型トークンを収集し、トークン → C# 型情報の辞書を構築する</summary>
    /// <param name="diagram">クエリ定義を含む ER 図</param>
    /// <param name="typeMapper">図の方言の型マッパ</param>
    /// <param name="typeCatalog">図の方言の型カタログ</param>
    public static IReadOnlyDictionary<string, CSharpTypeInfo> Resolve(
        ErDiagram diagram,
        IColumnTypeMapper typeMapper,
        ITypeCatalog typeCatalog
    )
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(typeMapper);
        ArgumentNullException.ThrowIfNull(typeCatalog);

        // 参照される全トークンの収集（パラメータ・スカラー戻り値・射影の自由フィールド）
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in diagram.Queries)
        {
            // 列参照で型付けされるパラメータ・フィールドはトークンを使わない（列型辞書から解決される）。
            // トークン欠落（null / 空白）は生成サービス側が解決不能の診断エラーを出すためここでは収集しない
            foreach (
                var parameter in query.Parameters.Where(p =>
                    p.SourceColumnId is null && !string.IsNullOrWhiteSpace(p.Type)
                )
            )
            {
                tokens.Add(parameter.Type!);
            }

            if (!string.IsNullOrWhiteSpace(query.ScalarType))
            {
                tokens.Add(query.ScalarType);
            }

            foreach (
                var field in query.Fields.Where(f =>
                    f.SourceColumnId is null && !string.IsNullOrWhiteSpace(f.Type)
                )
            )
            {
                tokens.Add(field.Type!);
            }
        }

        if (tokens.Count == 0)
        {
            return new Dictionary<string, CSharpTypeInfo>(StringComparer.OrdinalIgnoreCase);
        }

        // トークン → ネイティブ型の合成列を作り、列型解決と同一経路（ResolveColumnTypes）で C# 型へ写す
        var syntheticEntity = new Entity { TableName = "__QueryParameterTypes" };
        var columnIdByToken = new Dictionary<Guid, string>();

        foreach (var token in tokens)
        {
            if (
                !CanonicalTypeToken.TryParse(token, out var canonical)
                || !TryFormatForParameter(typeCatalog, canonical, out var nativeType)
            )
            {
                continue;
            }

            var column = new Column { Name = $"T{columnIdByToken.Count}", DataType = nativeType };
            syntheticEntity.Columns.Add(column);
            columnIdByToken[column.Id] = token;
        }

        var syntheticDiagram = new ErDiagram { Entities = { syntheticEntity } };
        var resolved = typeMapper.ResolveColumnTypes(syntheticDiagram);
        var result = new Dictionary<string, CSharpTypeInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (columnId, token) in columnIdByToken)
        {
            if (resolved.TryGetValue(columnId, out var info))
            {
                result[token] = info;
            }
        }

        return result;
    }

    /// <summary>長さの無い可変長トークンを「無制限」として整形し直すフォールバックの対象種別</summary>
    /// <remarks>
    /// 固定長（<see cref="CanonicalTypeKind.FixedString"/> / <see cref="CanonicalTypeKind.AnsiFixedString"/> /
    /// <see cref="CanonicalTypeKind.FixedBinary"/>）は含めない。<c>char</c> に「無制限」は存在せず、
    /// 長さ <c>-1</c> を渡すと方言側の整形が機械的に <c>(max)</c> を付けて <c>nchar(max)</c> のような
    /// 実在しない表記を <c>true</c> で返してしまう（その表記は読み戻せても DDL としては通らない）。
    /// 固定長の長さ無しは仕様として意味を成さないため、従来どおり解決不能＝生成時エラーにする。
    /// </remarks>
    private static readonly IReadOnlySet<CanonicalTypeKind> UnboundedFallbackKinds =
        new HashSet<CanonicalTypeKind>
        {
            CanonicalTypeKind.String,
            CanonicalTypeKind.AnsiString,
            CanonicalTypeKind.Binary,
        };

    /// <summary>
    /// 型トークンの正規型を、パラメータ用のネイティブ型表記へ整形する。
    /// </summary>
    /// <remarks>
    /// 通常は方言の <see cref="ITypeCatalog.TryFormat"/> をそのまま使う。長さを要する型で長さが無い正規型は
    /// 「列として書き出せない」ため方言側が <c>false</c> を返すが、クエリのパラメータ・スカラー戻り値・射影
    /// フィールドは列を作らないので長さ制約が無い。可変長の 3 種別に限り「無制限（長さ <c>-1</c>）」として
    /// 整形し直し、C# 型への写像だけを成立させる（トークンが表す型の意味は変わらない）。
    /// </remarks>
    private static bool TryFormatForParameter(
        ITypeCatalog typeCatalog,
        CanonicalType canonical,
        out string nativeType
    )
    {
        if (typeCatalog.TryFormat(canonical, out nativeType))
        {
            return true;
        }

        if (canonical.Length is not null || !UnboundedFallbackKinds.Contains(canonical.Kind))
        {
            return false;
        }

        return typeCatalog.TryFormat(canonical with { Length = -1 }, out nativeType);
    }
}
