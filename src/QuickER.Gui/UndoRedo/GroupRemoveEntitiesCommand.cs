using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.UndoRedo;

/// <summary>複数エンティティ（と接続リレーション・名前付きクエリ）の一括削除を単一の Undo 単位として扱う複合コマンド</summary>
/// <remarks>
/// 単一削除（<see cref="RemoveEntityCommand"/>）と同じく、削除対象を端点に持つリレーションと、削除対象の
/// 名前付きクエリを併せて退避・復元する。
/// 1 回の Undo で全エンティティと巻き添えリレーションが同時に復元される
/// （重複するリレーション＝複数の対象エンティティを結ぶ線を二重に退避しないよう集合で管理する）。
/// </remarks>
public sealed class GroupRemoveEntitiesCommand : IUndoableCommand
{
    /// <summary>削除を行うメイン ViewModel</summary>
    private readonly MainViewModel _main;

    /// <summary>削除対象のエンティティ群</summary>
    private readonly IReadOnlyList<EntityViewModel> _entities;

    /// <summary>削除したエンティティと元の位置（Undo の復元元）</summary>
    private List<(int Index, EntityViewModel Item)> _removedEntities = new();

    /// <summary>巻き添えで削除したリレーションと元の位置（Undo の復元元）</summary>
    private List<(int Index, RelationshipViewModel Item)> _removedRelationships = new();

    /// <summary>巻き添えで削除した名前付きクエリと元の位置（Undo の復元元）</summary>
    private List<(int Index, QueryDefinition Item)> _removedQueries = new();

    /// <summary><see cref="GroupRemoveEntitiesCommand"/> を生成する</summary>
    /// <param name="main">削除を行うメイン ViewModel</param>
    /// <param name="entities">削除対象のエンティティ群</param>
    public GroupRemoveEntitiesCommand(MainViewModel main, IReadOnlyList<EntityViewModel> entities)
    {
        _main = main;
        _entities = entities;
    }

    /// <inheritdoc />
    public string Description => string.Format(Strings.Undo_GroupRemoveEntities, _entities.Count);

    /// <inheritdoc />
    public void Execute()
    {
        var targets = _entities.ToHashSet();

        // 削除対象エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）。
        // 対象同士を結ぶ線は両端が対象のため一度だけ退避すればよい（集合判定で重複を避ける）。
        // どちらも元の位置を記録し、Undo で同じ位置へ戻す（末尾へ戻すと図の順序が変わる）
        _removedRelationships = IndexedRemoval.RemoveWhere(
            _main.Relationships,
            r => targets.Contains(r.Source) || targets.Contains(r.Target)
        );
        _removedEntities = IndexedRemoval.RemoveWhere(_main.Entities, targets.Contains);

        // 削除対象の名前付きクエリも取り除く（残すとどこからも触れない孤児になる）
        _removedQueries = EntityQueryRemoval.Remove(
            _main,
            _entities.Select(entity => entity.Id).ToHashSet()
        );
    }

    /// <inheritdoc />
    public void Undo()
    {
        // リレーションは端点のエンティティが戻ってから戻す
        IndexedRemoval.Restore(_main.Entities, _removedEntities);
        IndexedRemoval.Restore(_main.Relationships, _removedRelationships);
        EntityQueryRemoval.Restore(_main, _removedQueries);

        // 復元メンバーは IsSelected を保持したまま戻るため、主選択も復元して
        // パネル表示（一括操作カード・削除ボタンの実行可否）との整合を取る
        _main.SelectedEntity = _entities.FirstOrDefault();
    }
}
