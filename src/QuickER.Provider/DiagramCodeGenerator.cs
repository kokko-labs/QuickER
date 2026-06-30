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
    /// <summary>指定のプロバイダで型を解決し、ER 図から C# コードを生成する</summary>
    public static CodeGenerationResult Generate(
        IColumnTypeMapper typeMapper,
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(typeMapper);
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(options);

        var columnTypes = typeMapper.ResolveColumnTypes(diagram);
        return new CSharpCodeGenerationService().Generate(diagram, columnTypes, options);
    }
}
