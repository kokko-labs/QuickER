using Microsoft.EntityFrameworkCore.Metadata;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>エンティティ型の主キーのプロパティが、期待どおりの並びである（生成した <c>HasKey</c> が主キーの実効順に従う）</summary>
/// <param name="EntityClass">エンティティの型名</param>
/// <param name="Properties">主キーのプロパティ名（並び順どおり）</param>
internal sealed record EfCoreModelPrimaryKeyFact(
    string EntityClass,
    IReadOnlyList<string> Properties
) : EfCoreModelFact
{
    public override IEnumerable<string> Check(IModel model)
    {
        var entity = Find(model, EntityClass);

        if (entity is null)
        {
            return [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"];
        }

        var actual = entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToList() ?? [];

        return actual.SequenceEqual(Properties, StringComparer.Ordinal)
            ? []
            :
            [
                $"探針（EF Core のモデル）: {EntityClass} の主キーが [{string.Join(", ", actual)}]（期待 [{string.Join(", ", Properties)}]）",
            ];
    }
}
