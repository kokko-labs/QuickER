using System.Collections.Generic;
using System.Linq;
using ERDesigner.ViewModels;

namespace ERDesigner.UndoRedo;

/// <summary>
/// エンティティとそれに接続されたすべてのリレーションを削除するコマンドです。
/// Undo で両方を復元します。
/// </summary>
public class RemoveEntityCommand : IUndoableCommand
{
    private readonly MainViewModel _main;
    private readonly EntityViewModel _entity;
    private List<RelationshipViewModel> _removedRelationships = new();

    /// <summary>新しい <see cref="RemoveEntityCommand"/> を生成します。</summary>
    /// <param name="main">削除を行う <see cref="MainViewModel"/>。</param>
    /// <param name="entity">削除対象のエンティティ。</param>
    public RemoveEntityCommand(MainViewModel main, EntityViewModel entity)
    {
        _main = main;
        _entity = entity;
    }

    /// <inheritdoc />
    public string Description => $"エンティティ削除: {_entity.TableName}";

    /// <inheritdoc />
    public void Execute()
    {
        _removedRelationships = _main.Relationships.Where(r => r.Source == _entity || r.Target == _entity).ToList();

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Remove(r);
        }

        _main.Entities.Remove(_entity);
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Entities.Add(_entity);

        foreach (var r in _removedRelationships)
        {
            _main.Relationships.Add(r);
        }
    }
}
