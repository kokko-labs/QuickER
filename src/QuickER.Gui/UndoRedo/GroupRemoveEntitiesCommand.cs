using System.Collections.Generic;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>複数エンティティ（と接続リレーション・名前付きクエリ）の一括削除を単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// 1 回の Undo で全エンティティと巻き添えリレーション・クエリが同時に復元される。
/// 退避・復元・主選択の戻し方は <see cref="EntityRemovalCommandBase"/> が単独の削除と共有する
/// </remarks>
public sealed class GroupRemoveEntitiesCommand : EntityRemovalCommandBase
{
    /// <summary>削除対象のエンティティの数（説明文用）</summary>
    private readonly int _count;

    /// <summary><see cref="GroupRemoveEntitiesCommand"/> を生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entities">削除対象のエンティティ群</param>
    public GroupRemoveEntitiesCommand(MainViewModel main, IReadOnlyList<EntityViewModel> entities)
        : base(main, entities)
    {
        _count = entities.Count;
    }

    /// <inheritdoc />
    public override string Description => string.Format(Strings.Undo_GroupRemoveEntities, _count);
}
