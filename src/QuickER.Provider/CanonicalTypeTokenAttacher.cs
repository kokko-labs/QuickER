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
/// （EF Core 単独出力の方言可搬性を維持する）。図の方言以外のマッパで解決した辞書へ付加する場合も、
/// 図の方言の型カタログ 1 つを基準にすればよい。
/// </para>
/// <para>
/// 例外は行バージョン列（<see cref="CSharpTypeInfo.IsRowVersion"/>）で、型カタログが解析できてもトークンを載せない
/// （<c>[DbColumnMeta]</c> を付けない）。詳細は <see cref="Attach"/> 内のコメントを参照。
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
            // 行バージョン列にはトークンを刻まない。中立トークンは「DB が採番する」という store-generated の
            // 意味を運べないため、刻むと C# リバースがその列をただのバイナリ列として復元し、版ガードが黙って消える
            // （リバース側は属性の型トークンだけを見るため、失われたことに気づけない）。列は [StoreGeneratedColumn] が
            // 自己記述するので、トークンを省いても定義情報が失われるわけではない
            var token = typeInfo.IsRowVersion
                ? null
                : ResolveToken(columnId, dataTypeByColumn, typeCatalog);

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

            // 差分（トークン）だけを with 式で載せ替える。全項目を列挙して new し直すと、プロパティが増えたときの
            // 写し漏れがコンパイルを通ってしまう（同じ書き方をしていた [SqlColumnType] 補完側では、実際に
            // CanonicalTypeToken が写されず属性が黙って消えていた）
            result[columnId] = typeInfo with
            {
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
