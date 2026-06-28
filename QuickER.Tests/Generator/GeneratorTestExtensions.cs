using QuickER.Generator;
using QuickER.Model;
using QuickER.SqlServer;

namespace QuickER.Tests.Generator;

/// <summary>
/// テスト用の便宜オーバーロード。
/// </summary>
/// <remarks>
/// 本番の <see cref="CSharpCodeGenerationService.Generate(ErDiagram, IReadOnlyDictionary{System.Guid, CSharpTypeInfo}, CodeGenerationOptions)"/>
/// は DB 非依存で解決済み型を受け取る。テストでは SQL Server プロバイダで型解決してから呼ぶこのオーバーロードを使い、
/// 既存テストの呼び出し形（diagram, options）を維持する。
/// </remarks>
internal static class GeneratorTestExtensions
{
    public static CodeGenerationResult Generate(
        this CSharpCodeGenerationService service,
        ErDiagram diagram,
        CodeGenerationOptions options
    ) => service.Generate(diagram, SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram), options);
}
