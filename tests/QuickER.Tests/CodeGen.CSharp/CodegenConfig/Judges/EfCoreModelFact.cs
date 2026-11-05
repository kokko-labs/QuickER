using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 決まった行が EF Core のモデルの探針（<see cref="CodegenProbe.EfCoreModel"/>）へ追加で表明する、モデルの事実 1 つ
/// </summary>
/// <remarks>
/// エンティティ型はモデルの中の CLR 型の名前（<c>OrderEntity</c> など）で引く（名前空間はオプションで変わるので見ない）。
/// </remarks>
internal abstract record EfCoreModelFact
{
    /// <summary>エンティティ型がモデルにある</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact Present(string entityClass) => new PresentFact(entityClass);

    /// <summary>エンティティ型がモデルに無い（主キーの無い表の除外）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact Absent(string entityClass) => new AbsentFact(entityClass);

    /// <summary>エンティティ型がナビゲーションを 1 つも持たない（除外した型への自動発見の経路が無い）</summary>
    /// <param name="entityClass">エンティティの型名</param>
    public static EfCoreModelFact NoNavigations(string entityClass) =>
        new NoNavigationsFact(entityClass);

    /// <summary>エンティティ型の外部キーがちょうど 1 つで、その結合先の親のキー（と外部キーの列・削除の振る舞い）が期待どおり</summary>
    /// <param name="entityClass">子のエンティティの型名</param>
    /// <param name="principalProperties">親のキーのプロパティ名（並び順どおり。null なら見ない）</param>
    /// <param name="principalIsPrimaryKey">親のキーが主キーか（null なら見ない）</param>
    /// <param name="dependentProperties">外部キーのプロパティ名（null なら見ない）</param>
    /// <param name="deleteBehavior">削除の振る舞い（null なら見ない）</param>
    public static EfCoreModelFact SingleForeignKey(
        string entityClass,
        IReadOnlyList<string>? principalProperties = null,
        bool? principalIsPrimaryKey = null,
        IReadOnlyList<string>? dependentProperties = null,
        DeleteBehavior? deleteBehavior = null
    ) =>
        new SingleForeignKeyFact(
            entityClass,
            principalProperties,
            principalIsPrimaryKey,
            dependentProperties,
            deleteBehavior
        );

    /// <summary>モデルと照合する（問題を返す）</summary>
    /// <param name="model">組んだモデル</param>
    public abstract IEnumerable<string> Check(IModel model);

    /// <summary>モデルからエンティティ型を CLR 型の名前で引く（無ければ null）</summary>
    /// <param name="model">モデル</param>
    /// <param name="entityClass">エンティティの型名</param>
    public static IEntityType? Find(IModel model, string entityClass) =>
        model.GetEntityTypes().SingleOrDefault(type => type.ClrType.Name == entityClass);

    private sealed record PresentFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model) =>
            Find(model, EntityClass) is null
                ? [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"]
                : [];
    }

    private sealed record AbsentFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model) =>
            Find(model, EntityClass) is null
                ? []
                : [$"探針（EF Core のモデル）: {EntityClass} がモデルにある（除外されるはず）"];
    }

    private sealed record NoNavigationsFact(string EntityClass) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                return [$"探針（EF Core のモデル）: {EntityClass} がモデルに無い"];
            }

            var navigations = entity
                .GetNavigations()
                .Select(navigation => navigation.Name)
                .ToList();

            return navigations.Count == 0
                ? []
                :
                [
                    $"探針（EF Core のモデル）: {EntityClass} がナビゲーション [{string.Join(", ", navigations)}] を持つ",
                ];
        }
    }

    private sealed record SingleForeignKeyFact(
        string EntityClass,
        IReadOnlyList<string>? PrincipalProperties,
        bool? PrincipalIsPrimaryKey,
        IReadOnlyList<string>? DependentProperties,
        DeleteBehavior? DeleteBehavior
    ) : EfCoreModelFact
    {
        public override IEnumerable<string> Check(IModel model)
        {
            var entity = Find(model, EntityClass);

            if (entity is null)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} がモデルに無い";
                yield break;
            }

            var foreignKeys = entity.GetForeignKeys().ToList();

            if (foreignKeys.Count != 1)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーが {foreignKeys.Count} 個（期待 1 個）";
                yield break;
            }

            var foreignKey = foreignKeys[0];
            var principal = foreignKey.PrincipalKey.Properties.Select(p => p.Name).ToList();
            var dependent = foreignKey.Properties.Select(p => p.Name).ToList();

            if (
                PrincipalProperties is not null
                && !principal.SequenceEqual(PrincipalProperties, StringComparer.Ordinal)
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの親のキーが ({string.Join(", ", principal)})（期待 ({string.Join(", ", PrincipalProperties)})）";
            }

            if (
                PrincipalIsPrimaryKey is { } isPrimaryKey
                && foreignKey.PrincipalKey.IsPrimaryKey() != isPrimaryKey
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの親のキーが主キー{(isPrimaryKey ? "でない" : "である（主キーでない親の列へ結合するはず）")}";
            }

            if (
                DependentProperties is not null
                && !dependent.SequenceEqual(DependentProperties, StringComparer.Ordinal)
            )
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの列が ({string.Join(", ", dependent)})（期待 ({string.Join(", ", DependentProperties)})）";
            }

            if (DeleteBehavior is { } behavior && foreignKey.DeleteBehavior != behavior)
            {
                yield return $"探針（EF Core のモデル）: {EntityClass} の外部キーの削除の振る舞いが {foreignKey.DeleteBehavior}（期待 {behavior}）";
            }
        }
    }
}
