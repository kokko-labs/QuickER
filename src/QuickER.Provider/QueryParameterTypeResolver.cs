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
                || !typeCatalog.TryFormat(canonical, out var nativeType)
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
}
