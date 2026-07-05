using QuickER.Generator;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 「DB 型解決（プロバイダ） → C# コード生成（Generator）」という結合点を 1 箇所に集約するファサード。
/// </summary>
/// <remarks>
/// アプリ・CLI の双方がここを経由することで、型解決と生成の手順がドリフトしない。
/// 生成器自体は DB 非依存のままで、方言依存は <see cref="IColumnTypeMapper"/> に閉じる。
/// </remarks>
public static class DiagramCodeGenerator
{
    /// <summary>指定のプロバイダで型を解決し、ER 図から C# コードを生成する（単一方言・後方互換）</summary>
    /// <param name="typeMapper">図の方言の型マッパ（DB 型 → C# 型の解決）</param>
    /// <param name="typeCatalog">図の方言の型カタログ。列の DB 型を正規型へ解析し、DB 定義メタ属性の中立トークンを付加するのに使う</param>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    public static CodeGenerationResult Generate(
        IColumnTypeMapper typeMapper,
        ITypeCatalog typeCatalog,
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(typeMapper);
        ArgumentNullException.ThrowIfNull(typeCatalog);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(options);

        var columnTypes = typeMapper.ResolveColumnTypes(diagram);
        // DB 定義メタ属性用の方言中立トークンを後処理で付加する（マッパ実装は変更しない）
        columnTypes = CanonicalTypeTokenAttacher.Attach(columnTypes, diagram, typeCatalog);
        return new CSharpCodeGenerationService().Generate(diagram, columnTypes, options);
    }

    /// <summary>
    /// 図の方言（主辞書）と、Repository 実効方言ごとの型マッパ（レジストリ）で型を解決し、
    /// ER 図からマルチターゲット C# コードを生成する。
    /// </summary>
    /// <param name="primaryTypeMapper">図の方言の型マッパ。共有バケット（Entity / EditModel / Mapper / VO）の主辞書を作る</param>
    /// <param name="primaryTypeCatalog">図の方言の型カタログ。列の DB 型を正規型へ解析し、DB 定義メタ属性の中立トークンを主辞書へ付加するのに使う</param>
    /// <param name="dialectTypeMappers">
    /// 方言名（プロバイダ名。例 <c>"sqlserver"</c> / <c>"sqlite"</c>）→ その方言の型マッパ。
    /// <see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/> の各方言実装バケットの型解決に使う。
    /// </param>
    /// <param name="diagram">生成元の ER 図定義</param>
    /// <param name="options">生成対象や属性付与を制御するオプション</param>
    /// <remarks>
    /// 生成器の DB 非依存を保つため、方言ごとの型解決はここ（プロバイダ層）で行い、解決済み辞書を渡す。
    /// DB 定義メタ属性の中立トークンは canonical 由来で方言に依存しないため、共有 Entity を作る主辞書へのみ
    /// （図の方言の型カタログで）付加すれば足りる。方言間の C# 型不一致診断・<c>[SqlColumnType]</c> 補完はサービス側が担う。
    /// </remarks>
    public static CodeGenerationResult Generate(
        IColumnTypeMapper primaryTypeMapper,
        ITypeCatalog primaryTypeCatalog,
        IReadOnlyDictionary<string, IColumnTypeMapper> dialectTypeMappers,
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(primaryTypeMapper);
        ArgumentNullException.ThrowIfNull(primaryTypeCatalog);
        ArgumentNullException.ThrowIfNull(dialectTypeMappers);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(options);

        var primaryColumnTypes = primaryTypeMapper.ResolveColumnTypes(diagram);
        // DB 定義メタ属性用の方言中立トークンを主辞書へ付加する（共有 Entity 出力にのみ影響。マッパ実装は変更しない）
        primaryColumnTypes = CanonicalTypeTokenAttacher.Attach(
            primaryColumnTypes,
            diagram,
            primaryTypeCatalog
        );

        // 実効方言ごとに、その方言のマッパで列型を解決した辞書を用意する（マッパ未登録の方言は主辞書で代替）
        var columnTypesByDialect = new Dictionary<
            string,
            IReadOnlyDictionary<Guid, CSharpTypeInfo>
        >(StringComparer.OrdinalIgnoreCase);

        foreach (var dialect in options.EffectiveRepositoryDialects)
        {
            if (columnTypesByDialect.ContainsKey(dialect))
            {
                continue;
            }

            columnTypesByDialect[dialect] = dialectTypeMappers.TryGetValue(dialect, out var mapper)
                ? mapper.ResolveColumnTypes(diagram)
                : primaryColumnTypes;
        }

        return new CSharpCodeGenerationService().Generate(
            diagram,
            primaryColumnTypes,
            columnTypesByDialect,
            options
        );
    }
}
