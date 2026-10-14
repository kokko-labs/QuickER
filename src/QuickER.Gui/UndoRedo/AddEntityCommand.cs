using System.Collections.Generic;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>エンティティを 1 件追加するコマンド（Undo で同じエンティティを削除する）</summary>
public class AddEntityCommand : IUndoableCommand
{
    /// <summary>追加先のメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>追加対象のエンティティ</summary>
    private readonly EntityViewModel _entity;

    /// <summary>Undo で一緒に外した、このエンティティの名前付きクエリと元の位置（Redo の復元元）</summary>
    /// <remarks>
    /// クエリの定義は履歴の外なので、追加の後に定義したクエリがあり得る。エンティティだけを外すと、
    /// どこからも触れない孤児が残る（<see cref="EntityQueryRemoval"/>）
    /// </remarks>
    private List<(int Index, QueryDefinition Item)> _removedQueries = new();

    /// <summary><see cref="AddEntityCommand"/> を生成する</summary>
    /// <param name="main">追加先のメイン ViewModel</param>
    /// <param name="entity">追加対象のエンティティ</param>
    public AddEntityCommand(MainViewModel main, EntityViewModel entity)
    {
        _main = main;
        _entity = entity;
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_AddEntity, _entity.TableName);

    /// <inheritdoc />
    public void Execute()
    {
        _main.Entities.Add(_entity);

        // Redo では、Undo で一緒に外したクエリを元の位置へ戻す
        EntityQueryRemoval.Restore(_main, _removedQueries);
        _removedQueries = new();
    }

    /// <inheritdoc />
    public void Undo()
    {
        _main.Entities.Remove(_entity);
        _removedQueries = EntityQueryRemoval.Remove(_main, new HashSet<Guid> { _entity.Id });
    }
}
