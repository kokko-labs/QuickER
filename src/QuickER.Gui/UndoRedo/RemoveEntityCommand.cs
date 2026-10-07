using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>エンティティと接続中の全リレーション・そのエンティティの名前付きクエリを削除するコマンド（Undo で復元する）</summary>
public class RemoveEntityCommand : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>削除したエンティティと元の位置（Undo の復元元）</summary>
    private List<(int Index, EntityViewModel Item)> _removedEntities = new();

    /// <summary>巻き添えで削除したリレーションと元の位置（Undo の復元元）</summary>
    private List<(int Index, RelationshipViewModel Item)> _removedRelationships = new();

    /// <summary>巻き添えで削除した名前付きクエリと元の位置（Undo の復元元）</summary>
    private List<(int Index, QueryDefinition Item)> _removedQueries = new();

    /// <summary><see cref="RemoveEntityCommand"/> を生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entity">削除対象のエンティティ</param>
    public RemoveEntityCommand(MainViewModel main, EntityViewModel entity)
    {
        _main = main;
        _entity = entity;
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_RemoveEntity, _entity.TableName);

    /// <inheritdoc />
    public void Execute()
    {
        // 削除エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）。
        // どちらも元の位置を記録し、Undo で同じ位置へ戻す（末尾へ戻すと図の順序が変わる）
        _removedRelationships = IndexedRemoval.RemoveWhere(
            _main.Relationships,
            r => r.Source == _entity || r.Target == _entity
        );
        _removedEntities = IndexedRemoval.RemoveWhere(_main.Entities, e => e == _entity);

        // そのエンティティの名前付きクエリも取り除く（残すとどこからも触れない孤児になる）
        _removedQueries = EntityQueryRemoval.Remove(_main, new HashSet<Guid> { _entity.Id });
    }

    /// <inheritdoc />
    public void Undo()
    {
        // リレーションは端点のエンティティが戻ってから戻す
        IndexedRemoval.Restore(_main.Entities, _removedEntities);
        IndexedRemoval.Restore(_main.Relationships, _removedRelationships);
        EntityQueryRemoval.Restore(_main, _removedQueries);
    }
}
