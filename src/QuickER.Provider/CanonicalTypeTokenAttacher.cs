using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 解決済みの列型辞書へ、図の方言の型カタログから導いた方言中立トークン（<see cref="CSharpTypeInfo.CanonicalTypeToken"/>）を付加する。
/// </summary>
/// <remarks>
/// <para>
/// 生成器（<c>QuickER.CodeGen.CSharp</c>）は DB 非依存のため、型カタログの解釈はプロバイダ層のここで行う。
/// 各列の <see cref="Column.DataType"/> を <see cref="ITypeCatalog.TryParse"/> で正規型へ解析し、成功したものだけ
/// <see cref="CanonicalTypeToken.Format"/> でトークン化して型情報へ載せる。解析不能な自由記述型はトークン null のまま
/// （属性を省略する）。
/// </para>
/// <para>
/// トークンは canonical 由来で方言に依存しないため、可搬図の各方言表記から同一トークンが得られる
/// （EF 単独出力の方言可搬性を維持する）。図の方言以外のマッパで解決した辞書へ付加する場合も、
/// 図の方言の型カタログ 1 つを基準にすればよい。
/// </para>
/// </remarks>
public static class CanonicalTypeTokenAttacher
{
    /// <summary>
    /// 列型辞書へ、図の方言の型カタログ由来の中立トークンを付加した新しい辞書を返す。
    /// </summary>
    /// <param name="columnTypes">プロバイダの型マッパで解決済みの「カラム ID → C# 型情報」辞書</param>
    /// <param name="diagram">列の DB 型表記（<see cref="Column.DataType"/>）を持つ ER 図</param>
    /// <param name="typeCatalog">図の方言の型カタログ（<see cref="Column.DataType"/> の解析に使う）</param>
    /// <returns>各列に <see cref="CSharpTypeInfo.CanonicalTypeToken"/> を付加した新しい辞書（元の辞書は変更しない）</returns>
    public static IReadOnlyDictionary<Guid, CSharpTypeInfo> Attach(
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        ErDiagram diagram,
        ITypeCatalog typeCatalog
    )
    {
        ArgumentNullException.ThrowIfNull(columnTypes);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(typeCatalog);

        // カラム ID → DB 型表記の対応を図から引く（トークン付加の判定に使う）
        var dataTypeByColumn = new Dictionary<Guid, string>();

        foreach (var entity in diagram.Entities)
        {
            foreach (var column in entity.Columns)
            {
                dataTypeByColumn[column.Id] = column.DataType;
            }
        }

        var result = new Dictionary<Guid, CSharpTypeInfo>(columnTypes.Count);

        foreach (var (columnId, typeInfo) in columnTypes)
        {
            var token = ResolveToken(columnId, dataTypeByColumn, typeCatalog);

            // 既にトークンが載っている（外部で付加済み）場合や、解析不能でトークンが得られない場合は
            // それぞれ現状を尊重する。トークンが新たに解決できたときだけ載せ替える。
            if (
                token is null
                || string.Equals(typeInfo.CanonicalTypeToken, token, StringComparison.Ordinal)
            )
            {
                result[columnId] = typeInfo;

                continue;
            }

            result[columnId] = new CSharpTypeInfo
            {
                TypeName = typeInfo.TypeName,
                IsReferenceType = typeInfo.IsReferenceType,
                MaxLength = typeInfo.MaxLength,
                Precision = typeInfo.Precision,
                Scale = typeInfo.Scale,
                SqlDbTypeName = typeInfo.SqlDbTypeName,
                SqlDeclaredLength = typeInfo.SqlDeclaredLength,
                IsRowVersion = typeInfo.IsRowVersion,
                IsUnboundedBinary = typeInfo.IsUnboundedBinary,
                CanonicalTypeToken = token,
            };
        }

        return result;
    }

    /// <summary>指定列の DB 型表記を正規型へ解析し、中立トークンを返す（解析不能・列不明は null）</summary>
    private static string? ResolveToken(
        Guid columnId,
        IReadOnlyDictionary<Guid, string> dataTypeByColumn,
        ITypeCatalog typeCatalog
    )
    {
        if (
            !dataTypeByColumn.TryGetValue(columnId, out var dataType)
            || !typeCatalog.TryParse(dataType, out var canonical)
        )
        {
            return null;
        }

        return CanonicalTypeToken.Format(canonical);
    }
}
