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
/// （属性を省略する）。あわせて、トークン経由で同じ方言へ書き戻すと綴りが揃わない列にだけ、図が持っていた
/// 型表記そのものを <see cref="CSharpTypeInfo.VerbatimDbType"/> として載せる（C# リバースが綴りごと復元できるようにする）。
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
            var resolved = typeInfo.IsRowVersion
                ? default
                : ResolveToken(columnId, dataTypeByColumn, typeCatalog);

            // 既にトークンが載っている（外部で付加済み）場合や、解析不能でトークンが得られない場合は
            // それぞれ現状を尊重する。トークンが新たに解決できたときだけ載せ替える。
            if (
                resolved.Token is null
                || (
                    string.Equals(
                        typeInfo.CanonicalTypeToken,
                        resolved.Token,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        typeInfo.VerbatimDbType,
                        resolved.VerbatimDbType,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                result[columnId] = typeInfo;

                continue;
            }

            // 差分（トークンと元表記）だけを with 式で載せ替える。全項目を列挙して new し直すと、プロパティが
            // 増えたときの写し漏れがコンパイルを通ってしまい、写されなかった項目に対応する属性が黙って消える
            result[columnId] = typeInfo with
            {
                CanonicalTypeToken = resolved.Token,
                VerbatimDbType = resolved.VerbatimDbType,
                CanonicalRoundTripDbType = resolved.RoundTripped,
            };
        }

        return result;
    }

    /// <summary>
    /// 指定列の DB 型表記を正規型へ解析し、中立トークンと（必要なら）元の型表記を返す（解析不能・列不明は両方 null）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VerbatimDbType</c> は「トークンから同じ方言へ書き戻したとき、図が持っていた表記と綴りが揃わない列」
    /// にだけ入れる。中立トークンは型の意味だけを運ぶため、同義の複数表記（<c>numeric</c> と <c>decimal</c>・
    /// <c>ntext</c> と <c>nvarchar(max)</c>・<c>datetime</c> と <c>datetime2</c>）は代表表記へ畳まれ、
    /// その列を C# リバースで復元すると図の型表記が黙って変わる（次の DB 同期が <c>ALTER COLUMN</c> を出す）。
    /// </para>
    /// <para>
    /// 比較は <see cref="DbTypeText.AreEquivalent"/>＝同期が <c>ALTER COLUMN</c> を出すかどうかの判定と同じ規則。
    /// 厳密一致にすると、<c>TryFormat</c> が大文字で返す SQLite 方言の図で全列が「綴りが変わる」と判定され、
    /// 実害のない大小差のために生成物が総入れ替えになる。
    /// </para>
    /// <para>
    /// 書き戻し自体ができない方言（<c>TryFormat</c> が false）では、リバース側がトークン文字列をそのまま
    /// 型として採るため綴りは必ず変わる。この場合も元の表記を刻んで復元できるようにする。
    /// </para>
    /// </remarks>
    private static (string? Token, string? VerbatimDbType, string? RoundTripped) ResolveToken(
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
            return (null, null, null);
        }

        var token = CanonicalTypeToken.Format(canonical);
        var roundTripped = typeCatalog.TryFormat(canonical, out var nativeType)
            ? nativeType
            : token;

        return DbTypeText.AreEquivalent(dataType, roundTripped)
            ? (token, null, null)
            : (token, dataType, roundTripped);
    }
}
