using QuickER.Model;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// リレーションごとのナビゲーション名（親側・子側）を決める部分。
/// </summary>
/// <remarks>
/// ナビゲーション名を作るのはここだけで、Entity / EditModel / Mapper のナビゲーション（<c>ResolveAllNavigations</c>）と
/// EF Core の Fluent 構成（<c>BuildEfCoreRelationships</c>）が同じ結果を読む。IncludeGraph・API リファレンス・
/// 診断の文言は解決済みのナビゲーションを読むので、自動的に同じ名前になる。
/// </remarks>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>リレーションの親側・子側のナビゲーション名</summary>
    /// <param name="Principal">親（参照される側）が持つ、子へのナビゲーションの名前</param>
    /// <param name="Dependent">子（外部キーを持つ側）が持つ、親への参照の名前</param>
    private readonly record struct RelationshipNavigationNames(string Principal, string Dependent);

    /// <summary>リレーション ID → ナビゲーション名。<see cref="Build"/> の冒頭で 1 回だけ組み立てる</summary>
    private IReadOnlyDictionary<Guid, RelationshipNavigationNames> _navigationNames =
        new Dictionary<Guid, RelationshipNavigationNames>();

    /// <summary>
    /// ナビゲーションを作る全リレーションについて、親側・子側のナビゲーション名を決める
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通常は相手のテーブル名から作る（親側＝子の複数形〔1 対 1 は単数形〕・子側＝親の単数形）。
    /// </para>
    /// <para>
    /// 同じ（親表, 子表）の組にナビゲーションを作るリレーションが 2 本以上あるときだけ、その組の全リレーションを
    /// 外部キー列由来の役割名（<see cref="CSharpNameConverter.ToRoleName"/>）で区別する＝子側は役割名
    /// （<c>ShipCustomer</c>）、親側は <c>{子の複数形}By{役割名}</c>（1 対 1 は <c>{子の単数形}By{役割名}</c>）。
    /// テーブル名だけで作ると同じ名前のメンバーが 2 つできてコンパイルできない（列名を変えても解消しない）。
    /// 1 本だけの組は従来の名前のまま（既存の生成物を変えない）で、組の全リレーションを同じ規則で名付けるので
    /// リレーションの並び順に依らない。自己参照（親表＝子表）も同じ規則で、子側のナビゲーションは作られない。
    /// </para>
    /// <para>
    /// 対象は <c>ResolveAllNavigations</c> がナビゲーションを作るリレーションと同じ（多対多・端点不明・
    /// 複合外部キー・列ペアを解決できないものは除く）。
    /// </para>
    /// </remarks>
    private Dictionary<Guid, RelationshipNavigationNames> ResolveNavigationNames(ErDiagram diagram)
    {
        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        var navigable =
            new List<(Relationship Relationship, Entity Source, Entity Target, Column Dependent)>();

        foreach (var relationship in diagram.Relationships)
        {
            if (
                relationship.Type == RelationshipType.ManyToMany
                || relationship.ColumnPairs.Count != 1
                || !entitiesById.TryGetValue(relationship.SourceEntityId, out var source)
                || !entitiesById.TryGetValue(relationship.TargetEntityId, out var target)
            )
            {
                continue;
            }

            var pair = relationship.ColumnPairs[0];
            var principalColumn = source.Columns.FirstOrDefault(column =>
                column.Id == pair.SourceColumnId
            );
            var dependentColumn = target.Columns.FirstOrDefault(column =>
                column.Id == pair.TargetColumnId
            );

            if (principalColumn is null || dependentColumn is null)
            {
                continue;
            }

            navigable.Add((relationship, source, target, dependentColumn));
        }

        var names = new Dictionary<Guid, RelationshipNavigationNames>();

        foreach (var group in navigable.GroupBy(item => (item.Source.Id, item.Target.Id)))
        {
            var useRoleNames = group.Count() > 1;

            foreach (var (relationship, source, target, dependent) in group)
            {
                var collection = relationship.Type == RelationshipType.OneToMany;
                var childNavigation = _nameConverter.ToNavigationName(target.TableName, collection);

                if (!useRoleNames)
                {
                    names[relationship.Id] = new RelationshipNavigationNames(
                        childNavigation,
                        _nameConverter.ToNavigationName(source.TableName, collection: false)
                    );
                    continue;
                }

                var role = _nameConverter.ToRoleName(dependent.Name, source.TableName);
                names[relationship.Id] = new RelationshipNavigationNames(
                    $"{childNavigation}By{role}",
                    role
                );
            }
        }

        return names;
    }
}
