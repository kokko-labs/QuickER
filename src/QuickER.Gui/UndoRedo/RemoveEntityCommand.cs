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

    /// <summary>巻き添えで削除したリレーション（Undo の復元元）</summary>
    private List<RelationshipViewModel> _removedRelationships = new();

    /// <summary>巻き添えで削除した名前付きクエリと元の位置（Undo の復元元）</summary>
    private List<(int Index, QueryDefinition Query)> _removedQueries = new();

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
        // 削除エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）
        _removedRelationships = _main
            .Relationships.Where(r => r.Source == _entity || r.Target == _entity)
            .ToList();

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Remove(r);
        }

        _main.Entities.Remove(_entity);

        // そのエンティティの名前付きクエリも取り除く（残すとどこからも触れない孤児になる）
        _removedQueries = EntityQueryRemoval.Remove(_main, new HashSet<Guid> { _entity.Id });
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Entities.Add(_entity);

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Add(r);
        }

        EntityQueryRemoval.Restore(_main, _removedQueries);
    }
}
